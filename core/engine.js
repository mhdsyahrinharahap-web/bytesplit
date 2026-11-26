const http = require('node:http');
const https = require('node:https');
const fs = require('node:fs/promises');
const path = require('node:path');
const { probe, validateHttpUrl } = require('./prober');
const { detectCategory, getCategoryDir } = require('./categories');

const MAX_CONNECTIONS = 16;
const MIN_SPLIT_SIZE = 1024 * 1024; // Jangan pecah file di bawah 1 MB
const MAX_REDIRECTS = 5;
const MAX_NETWORK_RETRIES = 4;
const MAX_RATE_LIMIT_RETRIES = 8;
const DEFAULT_TIMEOUT_MS = 20000;
const THROTTLED_TIMEOUT_MS = 60000;
const USER_AGENT = 'ByteSplit/1.1';

function buildChunks(totalSize, requestedConnections, supportsRange) {
  if (!supportsRange || totalSize < MIN_SPLIT_SIZE || requestedConnections <= 1) {
    return [{ id: 0, start: 0, end: totalSize > 0 ? totalSize - 1 : -1, downloaded: 0, status: 'pending' }];
  }

  const clamped = Math.max(1, Math.min(MAX_CONNECTIONS, Math.floor(requestedConnections)));
  const count = Math.min(clamped, Math.max(1, Math.floor(totalSize / (256 * 1024))));
  const chunkSize = Math.floor(totalSize / count);

  const chunks = [];
  for (let i = 0; i < count; i++) {
    const start = i * chunkSize;
    const end = i === count - 1 ? totalSize - 1 : start + chunkSize - 1;
    chunks.push({ id: i, start, end, downloaded: 0, status: 'pending' });
  }
  return chunks;
}

async function fileExists(targetPath) {
  try {
    await fs.access(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function resolveUniqueFilePath(outputDir, fileName) {
  const ext = path.extname(fileName);
  const base = path.basename(fileName, ext);
  let candidate = path.join(outputDir, fileName);
  let counter = 1;

  while ((await fileExists(candidate)) || (await fileExists(`${candidate}.bytesplit.part`))) {
    candidate = path.join(outputDir, `${base} (${counter})${ext}`);
    counter++;
  }
  return candidate;
}

function sleep(ms, signal) {
  if (ms <= 0) return Promise.resolve();
  return new Promise((resolve, reject) => {
    if (signal && signal.aborted) {
      return reject(new Error('Operasi dihentikan.'));
    }

    const onAbort = () => {
      clearTimeout(timer);
      reject(new Error('Operasi dihentikan.'));
    };

    const timer = setTimeout(() => {
      if (signal) signal.removeEventListener('abort', onAbort);
      resolve();
    }, ms);

    if (signal) {
      signal.addEventListener('abort', onAbort, { once: true });
    }
  });
}

function parseRetryAfterMs(headerValue, attempt) {
  if (headerValue) {
    const seconds = Number(headerValue);
    if (Number.isFinite(seconds) && seconds > 0) {
      return Math.min(seconds * 1000, 30000);
    }
    const dateMs = Date.parse(headerValue);
    if (Number.isFinite(dateMs)) {
      const diff = dateMs - Date.now();
      if (diff > 0) return Math.min(diff, 30000);
    }
  }
  const jitter = Math.floor(Math.random() * 500);
  return Math.min(2000 * attempt + jitter, 15000);
}

async function writeFullBuffer(fileHandle, buffer, position) {
  let offset = 0;
  while (offset < buffer.length) {
    const { bytesWritten } = await fileHandle.write(
      buffer,
      offset,
      buffer.length - offset,
      position + offset
    );
    if (bytesWritten === 0) {
      throw new Error('Gagal menulis ke disk (0 bytes written).');
    }
    offset += bytesWritten;
  }
}

function openStream(targetUrl, headers, signal, timeoutMs, redirectsLeft = MAX_REDIRECTS) {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      return reject(new Error('Operasi dihentikan.'));
    }

    const validUrl = validateHttpUrl(targetUrl);
    const urlObj = new URL(validUrl);
    const transport = urlObj.protocol === 'https:' ? https : http;
    let settled = false;

    const finish = (fn, value) => {
      if (settled) return;
      settled = true;
      fn(value);
    };

    const req = transport.request(
      urlObj,
      {
        method: 'GET',
        headers: { 'User-Agent': USER_AGENT, ...headers },
        timeout: timeoutMs,
      },
      (res) => {
        const status = res.statusCode || 0;
        if ([301, 302, 303, 307, 308].includes(status) && res.headers.location) {
          res.destroy();
          if (redirectsLeft <= 0) {
            return finish(reject, new Error('Terlalu banyak redirect saat mengunduh.'));
          }
          const nextUrl = new URL(res.headers.location, validUrl).toString();
          return openStream(nextUrl, headers, signal, timeoutMs, redirectsLeft - 1)
            .then((r) => finish(resolve, r))
            .catch((err) => finish(reject, err));
        }

        finish(resolve, { req, res });
      }
    );

    const onAbort = () => req.destroy(new Error('Operasi dihentikan.'));
    signal.addEventListener('abort', onAbort, { once: true });

    req.on('close', () => signal.removeEventListener('abort', onAbort));
    req.on('timeout', () => req.destroy(new Error('Koneksi timeout saat mengunduh chunk.')));
    req.on('error', (err) => finish(reject, err));

    req.end();
  });
}

async function downloadChunk(url, chunk, useRange, totalChunks, fileHandle, signal, speedLimiter) {
  if (signal.aborted) {
    if (chunk.status !== 'done') chunk.status = 'paused';
    return;
  }

  if (useRange && chunk.start + chunk.downloaded > chunk.end) {
    chunk.status = 'done';
    return;
  }

  // Beri jeda tipis agar semua koneksi tidak menabrak server di milidetik yang sama
  if (useRange && chunk.id > 0) {
    try {
      await sleep(chunk.id * 200, signal);
    } catch {
      if (signal.aborted) {
        if (chunk.status !== 'done') chunk.status = 'paused';
        return;
      }
    }
  }

  let networkAttempts = 0;
  let rateLimitAttempts = 0;

  while (true) {
    if (signal.aborted) {
      if (chunk.status !== 'done') chunk.status = 'paused';
      return;
    }

    const currentPos = chunk.start + chunk.downloaded;
    if (useRange && currentPos > chunk.end) {
      chunk.status = 'done';
      return;
    }

    chunk.status = 'downloading';
    const headers = useRange ? { Range: `bytes=${currentPos}-${chunk.end}` } : {};
    const timeoutMs =
      speedLimiter && speedLimiter.bytesPerSec > 0 ? THROTTLED_TIMEOUT_MS : DEFAULT_TIMEOUT_MS;

    try {
      const { res } = await openStream(url, headers, signal, timeoutMs);
      const status = res.statusCode || 0;
      const allowFullOk = !useRange || (totalChunks === 1 && currentPos === 0);
      const isStatusValid = useRange ? status === 206 || (allowFullOk && status === 200) : status === 200;

      if (!isStatusValid) {
        const retryAfter = res.headers['retry-after'];
        res.destroy();

        const err = new Error(
          `Chunk #${chunk.id}: server merespons HTTP ${status} (mengharapkan ${useRange ? 206 : 200}).`
        );

        if (status === 429 || status === 503) {
          err.isRateLimit = true;
          err.retryAfter = retryAfter;
        } else if (status >= 400 && status < 500 && status !== 408) {
          err.fatal = true;
        }
        throw err;
      }

      let writePos = currentPos;
      for await (const buf of res) {
        if (signal.aborted) {
          res.destroy();
          chunk.status = 'paused';
          return;
        }

        if (useRange && writePos + buf.length > chunk.end + 1) {
          res.destroy();
          const err = new Error(`Chunk #${chunk.id}: server mengirim data melebihi batas byte.`);
          err.fatal = true;
          throw err;
        }

        await writeFullBuffer(fileHandle, buf, writePos);
        writePos += buf.length;
        chunk.downloaded += buf.length;

        if (speedLimiter) {
          await speedLimiter.consume(buf.length, signal);
        }
      }

      if (signal.aborted) {
        chunk.status = 'paused';
        return;
      }

      if (useRange) {
        const expectedLen = chunk.end - chunk.start + 1;
        if (chunk.downloaded !== expectedLen) {
          throw new Error(`Chunk #${chunk.id} terputus (${chunk.downloaded}/${expectedLen} bytes).`);
        }
      }

      chunk.status = 'done';
      return;
    } catch (err) {
      if (signal.aborted) {
        if (chunk.status !== 'done') chunk.status = 'paused';
        return;
      }

      if (err.fatal || !useRange) {
        chunk.status = 'error';
        throw err;
      }

      let delayMs;
      if (err.isRateLimit) {
        rateLimitAttempts++;
        if (rateLimitAttempts > MAX_RATE_LIMIT_RETRIES) {
          chunk.status = 'error';
          throw err;
        }
        delayMs = parseRetryAfterMs(err.retryAfter, rateLimitAttempts);
      } else {
        networkAttempts++;
        if (networkAttempts > MAX_NETWORK_RETRIES) {
          chunk.status = 'error';
          throw err;
        }
        delayMs = 1000 * networkAttempts;
      }

      chunk.status = 'retrying';
      try {
        await sleep(delayMs, signal);
      } catch {
        if (signal.aborted) {
          if (chunk.status !== 'done') chunk.status = 'paused';
          return;
        }
        throw err;
      }
    }
  }
}

function startTask(task, { speedLimiter, onProgress, onStateChange }) {
  const controller = new AbortController();
  let stopReason = null; // 'paused' | 'canceled' | null

  const run = async () => {
    let fileHandle = null;
    let timer = null;
    let lastBytes = task.chunks.reduce((sum, c) => sum + c.downloaded, 0);
    let lastTime = Date.now();
    let lastPersistTime = Date.now();

    const notify = (forcePersist = false) => {
      const now = Date.now();
      const totalDownloaded = task.chunks.reduce((sum, c) => sum + c.downloaded, 0);
      const deltaSec = Math.max((now - lastTime) / 1000, 0.001);
      const speedBps =
        task.status === 'downloading' ? Math.max(0, (totalDownloaded - lastBytes) / deltaSec) : 0;

      lastBytes = totalDownloaded;
      lastTime = now;

      task.downloaded = totalDownloaded;
      task.speedBps = speedBps;
      task.etaSec =
        speedBps > 0 && task.totalSize > 0
          ? Math.ceil(Math.max(0, task.totalSize - totalDownloaded) / speedBps)
          : null;

      if (typeof onProgress === 'function') onProgress(task);

      if (typeof onStateChange === 'function' && (forcePersist || now - lastPersistTime >= 2000)) {
        lastPersistTime = now;
        onStateChange(task);
      }
    };

    try {
      task.error = null;

      if (!task.outputDir && !task.baseOutputDir) {
        throw new Error('Folder tujuan penyimpanan tidak valid.');
      }

      const isRefreshingExisting =
        !task.finalUrl &&
        Boolean(task.destPath) &&
        Boolean(task.tempPath) &&
        Array.isArray(task.chunks) &&
        task.chunks.length > 0;

      if (isRefreshingExisting) {
        task.status = 'probing';
        notify(true);

        const meta = await probe(task.url);
        if (controller.signal.aborted) {
          throw new Error('Operasi dihentikan.');
        }

        if (task.totalSize > 0 && meta.totalSize > 0 && meta.totalSize !== task.totalSize) {
          throw new Error('Ukuran file pada link baru berbeda dengan unduhan sebelumnya.');
        }

        task.finalUrl = meta.finalUrl;
        task.supportsRange = meta.supportsRange;
        await fs.mkdir(task.outputDir, { recursive: true });
      } else if (!task.finalUrl || !task.destPath) {
        task.status = 'probing';
        notify(true);

        const meta = await probe(task.url);
        if (controller.signal.aborted) {
          throw new Error('Operasi dihentikan.');
        }

        let targetFileName = meta.fileName;
        if (task.customFileName) {
          const serverExt = path.extname(meta.fileName);
          const customExt = path.extname(task.customFileName);
          targetFileName =
            customExt || !serverExt ? task.customFileName : `${task.customFileName}${serverExt}`;
        }

        let category = detectCategory(targetFileName);
        if (category.id === 'general') {
          category = detectCategory(meta.fileName);
        }
        task.category = category.id;

        if (task.autoCategorize !== false) {
          const baseDir = task.baseOutputDir || task.outputDir;
          task.outputDir = getCategoryDir(baseDir, category.id);
        }

        await fs.mkdir(task.outputDir, { recursive: true });

        const uniqueDest = await resolveUniqueFilePath(task.outputDir, targetFileName);
        task.finalUrl = meta.finalUrl;
        task.fileName = path.basename(uniqueDest);
        task.destPath = uniqueDest;
        task.tempPath = `${uniqueDest}.bytesplit.part`;
        task.totalSize = meta.totalSize;
        task.supportsRange = meta.supportsRange;
        task.chunks = buildChunks(meta.totalSize, task.connections, meta.supportsRange);
      } else {
        await fs.mkdir(task.outputDir, { recursive: true });
      }

      const useRange = task.supportsRange && task.chunks.length > 0 && task.totalSize > 0;
      const canResumeDisk = useRange && (await fileExists(task.tempPath));

      if (canResumeDisk) {
        fileHandle = await fs.open(task.tempPath, 'r+');
      } else {
        for (const chunk of task.chunks) {
          chunk.downloaded = 0;
          chunk.status = 'pending';
        }
        lastBytes = 0;
        fileHandle = await fs.open(task.tempPath, 'w');
        if (task.totalSize > 0 && useRange) {
          await fileHandle.truncate(task.totalSize);
        }
      }

      task.status = 'downloading';
      notify(true);
      timer = setInterval(() => notify(false), 200);

      // allSettled memastikan semua write ke fileHandle selesai sebelum close()
      const results = await Promise.allSettled(
        task.chunks.map((chunk) =>
          downloadChunk(
            task.finalUrl,
            chunk,
            useRange,
            task.chunks.length,
            fileHandle,
            controller.signal,
            speedLimiter
          ).catch((err) => {
            if (!controller.signal.aborted) controller.abort();
            throw err;
          })
        )
      );

      await fileHandle.close();
      fileHandle = null;

      if (stopReason === 'paused') {
        task.status = 'paused';
        task.speedBps = 0;
        task.etaSec = null;
        notify(true);
        return task;
      }

      if (stopReason === 'canceled') {
        await fs.unlink(task.tempPath).catch(() => {});
        task.status = 'canceled';
        task.speedBps = 0;
        task.etaSec = null;
        notify(true);
        return task;
      }

      const failed = results.find((r) => r.status === 'rejected');
      if (failed) {
        throw failed.reason;
      }

      const totalDownloaded = task.chunks.reduce((sum, c) => sum + c.downloaded, 0);
      if (task.totalSize > 0 && totalDownloaded !== task.totalSize) {
        throw new Error(`Ukuran file akhir tidak cocok (${totalDownloaded}/${task.totalSize} bytes).`);
      }

      await fs.rename(task.tempPath, task.destPath);
      task.status = 'completed';
      task.speedBps = 0;
      task.etaSec = 0;
      task.completedAt = Date.now();
      notify(true);
      return task;
    } catch (err) {
      if (fileHandle) {
        await fileHandle.close().catch(() => {});
        fileHandle = null;
      }

      if (stopReason === 'paused') {
        task.status = 'paused';
        task.speedBps = 0;
        task.etaSec = null;
        notify(true);
        return task;
      }

      if (stopReason === 'canceled') {
        if (task.tempPath) await fs.unlink(task.tempPath).catch(() => {});
        task.status = 'canceled';
        task.speedBps = 0;
        task.etaSec = null;
        notify(true);
        return task;
      }

      // Pertahankan file .part jika server mendukung Range agar bisa di-Resume
      if (!task.supportsRange && task.tempPath) {
        await fs.unlink(task.tempPath).catch(() => {});
      }

      task.status = 'error';
      task.error = err.message || 'Terjadi kesalahan saat mengunduh.';
      task.speedBps = 0;
      task.etaSec = null;
      notify(true);
      return task;
    } finally {
      if (timer) clearInterval(timer);
    }
  };

  const promise = run();

  return {
    promise,
    pause: () => {
      if (stopReason) return;
      stopReason = 'paused';
      controller.abort();
    },
    cancel: () => {
      if (stopReason) return;
      stopReason = 'canceled';
      controller.abort();
    },
  };
}

module.exports = { startTask };