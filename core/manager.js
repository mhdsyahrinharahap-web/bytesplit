const EventEmitter = require('node:events');
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const { startTask } = require('./engine');
const { SpeedLimiter } = require('./limiter');
const { validateHttpUrl } = require('./prober');

const CATEGORY_EXT_MAP = {
  Compressed: new Set([
    '.zip', '.rar', '.7z', '.tar', '.gz', '.bz2', '.xz',
    '.tgz', '.zst', '.cab', '.arj', '.lzh', '.ace', '.z',
  ]),
  Documents: new Set([
    '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
    '.txt', '.rtf', '.odt', '.ods', '.odp', '.csv', '.md',
    '.epub', '.djvu', '.chm',
  ]),
  Music: new Set([
    '.mp3', '.wav', '.flac', '.aac', '.ogg', '.m4a', '.wma',
    '.opus', '.aiff', '.mid', '.midi',
  ]),
  Programs: new Set([
    '.exe', '.msi', '.msix', '.apk', '.deb', '.rpm', '.dmg',
    '.pkg', '.appimage', '.iso', '.img', '.bin', '.bat',
    '.cmd', '.ps1', '.sh',
  ]),
  Video: new Set([
    '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.flv', '.webm',
    '.m4v', '.mpeg', '.mpg', '.3gp', '.ts',
  ]),
};

const CATEGORY_LABEL_MAP = {
  compressed: 'Compressed',
  documents: 'Documents',
  music: 'Music',
  programs: 'Programs',
  video: 'Video',
  general: 'Other',
  other: 'Other',
};

function normalizeCategoryLabel(rawCat) {
  if (!rawCat || typeof rawCat !== 'string') return null;
  return CATEGORY_LABEL_MAP[rawCat.trim().toLowerCase()] || null;
}

function inferCategory(fileNameOrUrl) {
  if (!fileNameOrUrl || typeof fileNameOrUrl !== 'string') return 'Other';
  const clean = fileNameOrUrl.split('?')[0].split('#')[0];
  const ext = path.extname(clean).toLowerCase();
  for (const [category, extSet] of Object.entries(CATEGORY_EXT_MAP)) {
    if (extSet.has(ext)) return category;
  }
  return 'Other';
}

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
    const resolvedName = task.fileName || 'Menghubungi server...';
    const category =
      normalizeCategoryLabel(task.category) ||
      inferCategory(task.fileName || task.destPath || task.url);

    return {
      id: task.id,
      url: task.url,
      fileName: resolvedName,
      category,
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

  async setDefaultOutputDir(dir) {
    if (typeof dir !== 'string' || !dir.trim()) {
      throw new Error('Folder penyimpanan tidak valid.');
    }
    const resolved = path.resolve(dir.trim());
    await fs.mkdir(resolved, { recursive: true });
    this.defaultOutputDir = resolved;
    return this.defaultOutputDir;
  }

  addDownload({ url, filename, fileName, outputDir, connections = 8, autoCategorize = true }) {
    const validUrl = validateHttpUrl(url);
    const conn = Math.max(1, Math.min(16, Math.floor(Number(connections) || 8)));
    const rawCustomName = typeof fileName === 'string' ? fileName : filename;
    const customFileName =
      typeof rawCustomName === 'string' && rawCustomName.trim()
        ? path.basename(rawCustomName.trim())
        : null;

    const task = {
      id: crypto.randomUUID(),
      url: validUrl,
      finalUrl: null,
      fileName: customFileName,
      customFileName,
      outputDir: outputDir || this.defaultOutputDir,
      destPath: null,
      tempPath: null,
      totalSize: 0,
      downloaded: 0,
      supportsRange: false,
      connections: conn,
      autoCategorize: autoCategorize !== false,
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

  refreshLink(id, newUrl) {
    const task = this.tasks.get(id);
    if (!task) throw new Error('Tugas unduhan tidak ditemukan.');
    if (task.status === 'completed') {
      throw new Error('Unduhan sudah selesai.');
    }
    if (this.activeHandles.has(id)) {
      throw new Error('Jeda unduhan terlebih dahulu sebelum memperbarui link.');
    }

    const validUrl = validateHttpUrl(newUrl);
    task.url = validUrl;
    task.finalUrl = null;
    task.error = null;
    task.speedBps = 0;
    task.etaSec = null;
    task.status = 'queued';

    this._persist();
    this._launch(task);
    this.emit('update', this._snapshot(task));
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