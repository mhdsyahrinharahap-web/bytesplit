const { app, BrowserWindow, ipcMain, dialog, shell, clipboard } = require('electron');
const path = require('node:path');
const fs = require('node:fs/promises');
const { DownloadManager } = require('./core/manager');

let mainWindow = null;
let manager = null;
let isQuitting = false;
let settingsPath = null;
const settings = {
  defaultOutputDir: null,
  speedLimitBps: 0,
};

async function loadSettings(dataDir) {
  settingsPath = path.join(dataDir, 'settings.json');
  settings.defaultOutputDir = app.getPath('downloads');

  try {
    const raw = await fs.readFile(settingsPath, 'utf8');
    const parsed = JSON.parse(raw);
    if (typeof parsed.defaultOutputDir === 'string' && parsed.defaultOutputDir.trim()) {
      settings.defaultOutputDir = parsed.defaultOutputDir;
    }
    if (typeof parsed.speedLimitBps === 'number' && parsed.speedLimitBps >= 0) {
      settings.speedLimitBps = parsed.speedLimitBps;
    }
  } catch (err) {
    if (err.code !== 'ENOENT') {
      console.error('Gagal membaca settings.json:', err.message);
    }
  }
}

async function saveSettings() {
  if (!settingsPath) return;
  try {
    await fs.writeFile(settingsPath, JSON.stringify(settings, null, 2), 'utf8');
  } catch (err) {
    console.error('Gagal menyimpan settings.json:', err.message);
  }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1020,
    height: 660,
    minWidth: 780,
    minHeight: 520,
    title: 'ByteSplit',
    backgroundColor: '#f3f4f6',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

function sendToRenderer(channel, payload) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send(channel, payload);
  }
}

function registerIpc() {
  ipcMain.handle('state:get', () => ({
    tasks: manager.getTasks(),
    defaultOutputDir: settings.defaultOutputDir,
    speedLimitBps: settings.speedLimitBps,
  }));

  ipcMain.handle('download:add', (_event, payload) => {
    try {
      if (!payload || typeof payload.url !== 'string' || !payload.url.trim()) {
        return { ok: false, error: 'URL unduhan wajib diisi.' };
      }

      const outputDir =
        typeof payload.outputDir === 'string' && payload.outputDir.trim()
          ? payload.outputDir.trim()
          : settings.defaultOutputDir;

      const task = manager.addDownload({
        url: payload.url.trim(),
        outputDir,
        connections: Number(payload.connections) || 8,
        autoCategorize: payload.autoCategorize !== false,
      });
      return { ok: true, task };
    } catch (err) {
      return { ok: false, error: err.message || 'Gagal menambah unduhan.' };
    }
  });

  ipcMain.handle('download:pause', async (_event, id) => {
    if (typeof id !== 'string') return { ok: false, error: 'ID tidak valid.' };
    try {
      await manager.pauseDownload(id);
      return { ok: true };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });

  ipcMain.handle('download:resume', (_event, id) => {
    if (typeof id !== 'string') return { ok: false, error: 'ID tidak valid.' };
    try {
      const task = manager.resumeDownload(id);
      return { ok: true, task };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });

  ipcMain.handle('download:cancel', async (_event, id) => {
    if (typeof id !== 'string') return { ok: false, error: 'ID tidak valid.' };
    try {
      await manager.cancelDownload(id);
      return { ok: true };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });

  ipcMain.handle('download:remove', async (_event, id) => {
    if (typeof id !== 'string') return { ok: false, error: 'ID tidak valid.' };
    try {
      await manager.removeDownload(id, false);
      return { ok: true };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });

  ipcMain.handle('speed:set', async (_event, bytesPerSec) => {
    const applied = manager.setSpeedLimit(Number(bytesPerSec) || 0);
    settings.speedLimitBps = applied;
    await saveSettings();
    return { ok: true, speedLimitBps: applied };
  });

  ipcMain.handle('dialog:chooseFolder', async (_event, updateDefault) => {
    const result = await dialog.showOpenDialog(mainWindow, {
      title: 'Pilih Folder Penyimpanan',
      defaultPath: settings.defaultOutputDir,
      properties: ['openDirectory', 'createDirectory'],
    });

    if (result.canceled || !result.filePaths.length) {
      return { canceled: true };
    }

    const chosen = result.filePaths[0];
    if (updateDefault) {
      try {
        await manager.setDefaultOutputDir(chosen);
        settings.defaultOutputDir = chosen;
        await saveSettings();
      } catch (err) {
        return { canceled: true, error: err.message || 'Gagal menyiapkan folder tujuan.' };
      }
    }
    return { canceled: false, path: chosen };
  });

  ipcMain.handle('file:open', async (_event, id) => {
    const task = manager.getTasks().find((t) => t.id === id);
    if (!task || !task.destPath) {
      return { ok: false, error: 'File tidak ditemukan di daftar.' };
    }
    try {
      await fs.access(task.destPath);
    } catch {
      return { ok: false, error: 'File sudah dipindahkan atau dihapus dari disk.' };
    }
    const errMsg = await shell.openPath(task.destPath);
    return errMsg ? { ok: false, error: errMsg } : { ok: true };
  });

  ipcMain.handle('file:showInFolder', async (_event, id) => {
    const task = manager.getTasks().find((t) => t.id === id);
    if (!task || !task.destPath) {
      return { ok: false, error: 'Lokasi file tidak ditemukan.' };
    }
    try {
      await fs.access(task.destPath);
      shell.showItemInFolder(task.destPath);
      return { ok: true };
    } catch {
      return { ok: false, error: 'File sudah dipindahkan atau dihapus dari disk.' };
    }
  });

  ipcMain.handle('clipboard:read', () => clipboard.readText() || '');
}

app.whenReady().then(async () => {
  const dataDir = path.join(app.getPath('userData'), 'data');
  await fs.mkdir(dataDir, { recursive: true });
  await loadSettings(dataDir);

  manager = new DownloadManager({
    dataDir,
    defaultOutputDir: settings.defaultOutputDir,
  });
  await manager.init();
  manager.setSpeedLimit(settings.speedLimitBps);

  manager.on('update', (task) => sendToRenderer('download:update', task));
  manager.on('list', (tasks) => sendToRenderer('download:list', tasks));

  registerIpc();
  createWindow();
});

app.on('before-quit', async (event) => {
  if (isQuitting || !manager) return;
  event.preventDefault();
  isQuitting = true;
  try {
    await manager.pauseAll();
  } finally {
    app.quit();
  }
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});