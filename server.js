const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const pkg = require('./package.json');
const { DownloadManager } = require('./core/manager');

const HOST = '127.0.0.1';
const PREFERRED_PORT = 3847;
const RENDERER_DIR = path.resolve(__dirname, 'renderer');
const DATA_DIR = path.resolve(__dirname, 'data');
const SETTINGS_FILE = path.join(DATA_DIR, 'settings.json');
const MAX_BODY_BYTES = 64 * 1024;

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};

const FALLBACK_DOWNLOADS_DIR = path.join(os.homedir(), 'Downloads');

const settings = {
  defaultOutputDir: FALLBACK_DOWNLOADS_DIR,
  speedLimitBps: 0,
};

async function isWritableDirectory(dirPath) {
  try {
    await fs.mkdir(dirPath, { recursive: true });
    return true;
  } catch {
    return false;
  }
}

async function loadSettings() {
  await fs.mkdir(DATA_DIR, { recursive: true });
  settings.defaultOutputDir = FALLBACK_DOWNLOADS_DIR;
  let needsRewrite = false;

  try {
    const raw = await fs.readFile(SETTINGS_FILE, 'utf8');
    const parsed = JSON.parse(raw);

    if (typeof parsed.defaultOutputDir === 'string' && parsed.defaultOutputDir.trim()) {
      const candidate = parsed.defaultOutputDir.trim();
      if (await isWritableDirectory(candidate)) {
        settings.defaultOutputDir = candidate;
      } else {
        console.warn(`Folder tersimpan tidak dapat diakses (${candidate}), dialihkan ke ${FALLBACK_DOWNLOADS_DIR}`);
        needsRewrite = true;
      }
    }

    if (typeof parsed.speedLimitBps === 'number' && parsed.speedLimitBps >= 0) {
      settings.speedLimitBps = parsed.speedLimitBps;
    }
  } catch (err) {
    if (err.code !== 'ENOENT') {
      console.error('Gagal membaca settings.json:', err.message);
    }
  }

  await fs.mkdir(settings.defaultOutputDir, { recursive: true });
  if (needsRewrite) {
    await saveSettings();
  }
}

async function saveSettings() {
  try {
    await fs.writeFile(SETTINGS_FILE, JSON.stringify(settings, null, 2), 'utf8');
  } catch (err) {
    console.error('Gagal menyimpan settings.json:', err.message);
  }
}

function sendJson(res, statusCode, data) {
  const body = JSON.stringify(data);
  res.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
  });
  res.end(body);
}

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let total = 0;

    req.on('data', (chunk) => {
      total += chunk.length;
      if (total > MAX_BODY_BYTES) {
        req.destroy();
        return reject(new Error('Body request terlalu besar.'));
      }
      chunks.push(chunk);
    });

    req.on('end', () => {
      if (chunks.length === 0) return resolve({});
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
      } catch {
        reject(new Error('Format JSON tidak valid.'));
      }
    });

    req.on('error', reject);
  });
}

async function serveStatic(res, pathname) {
  const relative = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
  const resolved = path.resolve(RENDERER_DIR, relative);

  if (!resolved.startsWith(RENDERER_DIR + path.sep)) {
    res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
    return res.end('Akses ditolak.');
  }

  try {
    const content = await fs.readFile(resolved);
    const ext = path.extname(resolved).toLowerCase();
    res.writeHead(200, {
      'Content-Type': MIME_TYPES[ext] || 'application/octet-stream',
      'Cache-Control': 'no-store',
    });
    res.end(content);
  } catch (err) {
    if (err.code === 'ENOENT') {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      return res.end('File tidak ditemukan.');
    }
    res.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Gagal membaca file.');
  }
}

async function openInAppWindow(url) {
  if (process.platform !== 'win32') return;

  const candidates = [
    path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Microsoft\\Edge\\Application\\msedge.exe'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Microsoft\\Edge\\Application\\msedge.exe'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Google\\Chrome\\Application\\chrome.exe'),
    path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Google\\Chrome\\Application\\chrome.exe'),
  ];

  for (const exe of candidates) {
    try {
      await fs.access(exe);
      const child = spawn(exe, [`--app=${url}`, '--window-size=1020,660'], {
        detached: true,
        stdio: 'ignore',
      });
      child.unref();
      return;
    } catch {
      // Lanjut cek kandidat browser berikutnya
    }
  }
}

function openWithExplorer(targetPath) {
  if (process.platform !== 'win32') {
    throw new Error('Fitur buka file/folder otomatis saat ini hanya untuk Windows.');
  }
  const child = spawn('explorer.exe', [targetPath], {
    detached: true,
    stdio: 'ignore',
  });
  child.unref();
}

async function main() {
  await loadSettings();

  const manager = new DownloadManager({
    dataDir: DATA_DIR,
    defaultOutputDir: settings.defaultOutputDir,
  });
  await manager.init();
  manager.setSpeedLimit(settings.speedLimitBps);

  const sseClients = new Set();

  function broadcast(eventName, payload) {
    const frame = `event: ${eventName}\ndata: ${JSON.stringify(payload)}\n\n`;
    for (const client of sseClients) {
      if (client.writableEnded) {
        sseClients.delete(client);
        continue;
      }
      try {
        client.write(frame);
      } catch {
        sseClients.delete(client);
      }
    }
  }

  manager.on('update', (task) => broadcast('update', task));
  manager.on('list', (tasks) => broadcast('list', tasks));

  const server = http.createServer(async (req, res) => {
    const reqUrl = new URL(req.url || '/', `http://${HOST}`);
    const { pathname } = reqUrl;

    try {
      if (req.method === 'GET' && pathname === '/api/events') {
        res.writeHead(200, {
          'Content-Type': 'text/event-stream; charset=utf-8',
          'Cache-Control': 'no-cache',
          Connection: 'keep-alive',
        });
        res.write(`event: list\ndata: ${JSON.stringify(manager.getTasks())}\n\n`);
        sseClients.add(res);
        req.on('close', () => sseClients.delete(res));
        return;
      }

      if (req.method === 'GET' && pathname === '/api/state') {
        return sendJson(res, 200, {
          tasks: manager.getTasks(),
          defaultOutputDir: settings.defaultOutputDir,
          speedLimitBps: settings.speedLimitBps,
          clipboardMonitor: false,
        });
      }

      if (req.method === 'GET' && pathname === '/api/about') {
        return sendJson(res, 200, {
          name: 'ByteSplit',
          version: `v${pkg.version}`,
          about:
            'Aplikasi multi-connection HTTP/HTTPS download manager untuk Windows yang memecah file menjadi beberapa segmen paralel dan mengelompokkan hasil unduhan ke folder kategori otomatis.',
          developer: 'Muhammad Syahrin Harahap',
        });
      }

      if (req.method === 'POST' && pathname === '/api/downloads') {
        const body = await readJsonBody(req);
        if (!body || typeof body.url !== 'string' || !body.url.trim()) {
          return sendJson(res, 400, { ok: false, error: 'URL unduhan wajib diisi.' });
        }

        const requestedDir =
          typeof body.outputDir === 'string' && body.outputDir.trim()
            ? body.outputDir.trim()
            : settings.defaultOutputDir;

        const outputDir = (await isWritableDirectory(requestedDir))
          ? requestedDir
          : settings.defaultOutputDir;

        const filename =
          typeof body.filename === 'string' && body.filename.trim()
            ? path.basename(body.filename.trim())
            : '';

        const task = manager.addDownload({
          url: body.url.trim(),
          filename,
          outputDir,
          connections: Number(body.connections) || 8,
          autoCategorize: body.autoCategorize !== false,
        });
        return sendJson(res, 200, { ok: true, task });
      }

      if (req.method === 'POST' && pathname === '/api/speed') {
        const body = await readJsonBody(req);
        const applied = manager.setSpeedLimit(Number(body.bytesPerSec) || 0);
        settings.speedLimitBps = applied;
        await saveSettings();
        return sendJson(res, 200, { ok: true, speedLimitBps: applied });
      }

      if (req.method === 'POST' && pathname === '/api/settings/folder') {
        const body = await readJsonBody(req);
        if (!body || typeof body.path !== 'string' || !body.path.trim()) {
          return sendJson(res, 400, { ok: false, error: 'Path folder tidak valid.' });
        }
        const chosen = path.resolve(body.path.trim());
        await manager.setDefaultOutputDir(chosen);
        settings.defaultOutputDir = chosen;
        await saveSettings();
        return sendJson(res, 200, { ok: true, path: chosen });
      }

      const taskMatch = pathname.match(/^\/api\/downloads\/([^/]+)(?:\/(pause|resume|cancel|refresh|open|folder))?$/);
      if (taskMatch) {
        let id;
        try {
          id = decodeURIComponent(taskMatch[1]);
        } catch {
          return sendJson(res, 400, { ok: false, error: 'ID tidak valid.' });
        }
        const action = taskMatch[2];

        if (req.method === 'DELETE' && !action) {
          await manager.removeDownload(id, false);
          return sendJson(res, 200, { ok: true });
        }
        if (req.method === 'POST' && action === 'pause') {
          await manager.pauseDownload(id);
          return sendJson(res, 200, { ok: true });
        }
        if (req.method === 'POST' && action === 'resume') {
          const task = manager.resumeDownload(id);
          return sendJson(res, 200, { ok: true, task });
        }
        if (req.method === 'POST' && action === 'cancel') {
          await manager.cancelDownload(id);
          return sendJson(res, 200, { ok: true });
        }
        if (req.method === 'POST' && action === 'refresh') {
          const body = await readJsonBody(req);
          const newUrl = typeof body.newUrl === 'string' ? body.newUrl.trim() : '';
          if (!newUrl) {
            return sendJson(res, 400, { ok: false, error: 'URL baru wajib diisi.' });
          }
          const task = manager.refreshLink(id, newUrl);
          return sendJson(res, 200, { ok: true, task });
        }
        if (req.method === 'POST' && (action === 'open' || action === 'folder')) {
          const task = manager.getTasks().find((t) => t.id === id);
          if (!task || !task.destPath) {
            return sendJson(res, 404, { ok: false, error: 'File tidak ditemukan di daftar.' });
          }
          try {
            await fs.access(task.destPath);
          } catch {
            return sendJson(res, 404, { ok: false, error: 'File sudah dipindahkan atau dihapus dari disk.' });
          }
          const target = action === 'open' ? task.destPath : path.dirname(task.destPath);
          openWithExplorer(target);
          return sendJson(res, 200, { ok: true });
        }
      }

      if (req.method === 'GET' && !pathname.startsWith('/api/')) {
        return serveStatic(res, pathname);
      }

      sendJson(res, 404, { ok: false, error: 'Endpoint tidak ditemukan.' });
    } catch (err) {
      sendJson(res, 400, { ok: false, error: err.message || 'Terjadi kesalahan.' });
    }
  });

  const startListening = (port) => {
    server.listen(port, HOST, async () => {
      const addr = server.address();
      const url = `http://${HOST}:${addr.port}`;
      console.log(`ByteSplit berjalan di ${url}`);
      console.log(`Folder unduhan aktif: ${settings.defaultOutputDir}`);
      console.log('Tekan Ctrl+C di terminal untuk berhenti (unduhan aktif otomatis dijeda).');
      await openInAppWindow(url);
    });
  };

  server.on('error', (err) => {
    if (err.code === 'EADDRINUSE') {
      startListening(0);
    } else {
      console.error('Server error:', err.message);
      process.exit(1);
    }
  });

  startListening(PREFERRED_PORT);

  let shuttingDown = false;
  const shutdown = async () => {
    if (shuttingDown) return;
    shuttingDown = true;
    console.log('\nMenjeda semua unduhan aktif...');
    for (const client of sseClients) client.end();
    await manager.pauseAll();
    server.close(() => process.exit(0));
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

main().catch((err) => {
  console.error('Gagal menjalankan ByteSplit:', err.message);
  process.exit(1);
});