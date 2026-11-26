const EventEmitter = require('node:events');
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const { startTask } = require('./engine');
const { SpeedLimiter } = require('./limiter');
const { validateHttpUrl } = require('./prober');

class DownloadManager extends EventEmitter {
  constructor({ dataDir, defaultOutputDir }) {
    super();
    this.dataDir = dataDir;
    this.stateFile = path.join(dataDir, 'tasks.json');
    this.defaultOutputDir = defaultOutputDir;
    this.tasks = new Map();
    this.activeHandles = new Map();
    this.speedLimiter = new SpeedLimiter(0);
    this._saveChain = Promise.resolve();
  }

  async init() {
    await fs.mkdir(this.dataDir, { recursive: true });
    await fs.mkdir(this.defaultOutputDir, { recursive: true });

    try {
      const raw = await fs.readFile(this.stateFile, 'utf8');
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        for (const item of parsed) {
          // Tugas yang terputus saat aplikasi ditutup otomatis diubah ke paused
          if (item.status === 'downloading' || item.status === 'probing') {
            item.status = 'paused';
            item.speedBps = 0;
            item.etaSec = null;
          }
          this.tasks.set(item.id, item);
        }
      }
    } catch (err) {
      if (err.code !== 'ENOENT') {
        console.error('Gagal membaca riwayat tasks.json:', err.message);
      }
    }
  }

  _persist() {
    const snapshot = JSON.stringify(Array.from(this.tasks.values()), null, 2);
    const tmpFile = `${this.stateFile}.tmp`;

    // Antrekan penulisan agar tidak saling tabrak antar-event
    this._saveChain = this._saveChain
      .then(async () => {
        await fs.writeFile(tmpFile, snapshot, 'utf8');
        await fs.rename(tmpFile, this.stateFile);
      })
      .catch((err) => {
        console.error('Gagal menyimpan tasks.json:', err.message);
      });

    return this._saveChain;
  }

  _snapshot(task) {
    return {
      id: task.id,
      url: task.url,
      fileName: task.fileName || 'Menghubungi server...',
      outputDir: task.outputDir,
      destPath: task.destPath,
      totalSize: task.totalSize,
      downloaded: task.downloaded,
      supportsRange: task.supportsRange,
      connections: task.connections,
      status: task.status,
      error: task.error,
      speedBps: task.speedBps || 0,
      etaSec: task.etaSec,
      createdAt: task.createdAt,
      completedAt: task.completedAt,
      chunks: (task.chunks || []).map((c) => ({
        id: c.id,
        downloaded: c.downloaded,
        size: c.end >= 0 ? c.end - c.start + 1 : 0,
        status: c.status,
      })),
    };
  }

  getTasks() {
    return Array.from(this.tasks.values())
      .sort((a, b) => b.createdAt - a.createdAt)
      .map((t) => this._snapshot(t));
  }

  setSpeedLimit(bytesPerSec) {
    this.speedLimiter.setLimit(bytesPerSec);
    return this.speedLimiter.bytesPerSec;
  }

  addDownload({ url, outputDir, connections = 8 }) {
    const validUrl = validateHttpUrl(url);
    const conn = Math.max(1, Math.min(16, Math.floor(Number(connections) || 8)));

    const task = {
      id: crypto.randomUUID(),
      url: validUrl,
      finalUrl: null,
      fileName: null,
      outputDir: outputDir || this.defaultOutputDir,
      destPath: null,
      tempPath: null,
      totalSize: 0,
      downloaded: 0,
      supportsRange: false,
      connections: conn,
      status: 'queued',
      error: null,
      speedBps: 0,
      etaSec: null,
      chunks: [],
      createdAt: Date.now(),
      completedAt: null,
    };

    this.tasks.set(task.id, task);
    this._launch(task);
    this.emit('list', this.getTasks());
    return this._snapshot(task);
  }

  _launch(task) {
    if (this.activeHandles.has(task.id)) return;

    const handle = startTask(task, {
      speedLimiter: this.speedLimiter,
      onProgress: (t) => this.emit('update', this._snapshot(t)),
      onStateChange: () => {
        this._persist();
      },
    });

    this.activeHandles.set(task.id, handle);
    handle.promise.finally(() => {
      this.activeHandles.delete(task.id);
      this._persist();
      this.emit('update', this._snapshot(task));
    });
  }

  async pauseDownload(id) {
    const handle = this.activeHandles.get(id);
    if (!handle) return false;
    handle.pause();
    await handle.promise;
    return true;
  }

  resumeDownload(id) {
    const task = this.tasks.get(id);
    if (!task) throw new Error('Tugas unduhan tidak ditemukan.');
    if (task.status === 'completed' || this.activeHandles.has(id)) return this._snapshot(task);

    this._launch(task);
    return this._snapshot(task);
  }

  async cancelDownload(id) {
    const task = this.tasks.get(id);
    if (!task) return false;

    const handle = this.activeHandles.get(id);
    if (handle) {
      handle.cancel();
      await handle.promise;
    } else {
      if (task.tempPath) await fs.unlink(task.tempPath).catch(() => {});
      task.status = 'canceled';
      await this._persist();
      this.emit('update', this._snapshot(task));
    }
    return true;
  }

  async removeDownload(id, deleteCompletedFile = false) {
    const task = this.tasks.get(id);
    if (!task) return false;

    if (this.activeHandles.has(id)) {
      await this.cancelDownload(id);
    } else if (task.status !== 'completed' && task.tempPath) {
      await fs.unlink(task.tempPath).catch(() => {});
    }

    if (deleteCompletedFile && task.status === 'completed' && task.destPath) {
      await fs.unlink(task.destPath).catch(() => {});
    }

    this.tasks.delete(id);
    await this._persist();
    this.emit('list', this.getTasks());
    return true;
  }

  async pauseAll() {
    const active = Array.from(this.activeHandles.values());
    for (const h of active) h.pause();
    await Promise.allSettled(active.map((h) => h.promise));
    await this._persist();
  }
}

module.exports = { DownloadManager };