const {
  app,
  BrowserWindow,
  ipcMain,
  dialog,
  shell,
  clipboard,
  Tray,
  Menu,
  Notification,
  nativeImage,
} = require('electron');
const path = require('node:path');
const fs = require('node:fs/promises');
const fsSync = require('node:fs');
const pkg = require('./package.json');
const { DownloadManager } = require('./core/manager');

const DOWNLOADABLE_EXTS = new Set([
  '.zip', '.rar', '.7z', '.tar', '.gz', '.bz2', '.xz',
  '.iso', '.img', '.exe', '.msi', '.msix', '.dmg', '.deb', '.rpm', '.apk',
  '.mp4', '.mkv', '.webm', '.avi', '.mov', '.flv',
  '.mp3', '.flac', '.wav', '.m4a', '.aac', '.ogg',
  '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.csv', '.bin',
]);

// Fallback ikon 16x16 PNG bila build/icon.png belum tersedia saat pengembangan
const FALLBACK_TRAY_ICON =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAWUlEQVR4nGNgGPKAEZ8g/3+4/P9/RkYGBkYGBgZGBgYGJiYGBkYmBgYGJiYGBgYmBgYGJiYGBgYmBgYGBiYGBgYmJgYGBiYGBgYmBgYGBiYGBiYGBgYAAK8BBv2Y8x1OAAAAAElFTkSuQmCC';

let mainWindow = null;
let tray = null;
let manager = null;
let isQuitting = false;
let hasShownTrayNotice = false;
let settingsPath = null;
let clipboardTimer = null;
let lastClipboardText = '';
const lastTaskStatuses = new Map();

const settings = {
  defaultOutputDir: null,
  speedLimitBps: 0,
  clipboardMonitor: true,
};

function getAppIcon() {
  const iconPath = path.join(__dirname, 'build', 'icon.png');
  if (fsSync.existsSync(iconPath)) {
    const img = nativeImage.createFromPath(iconPath);
    if (!img.isEmpty()) return img;
  }
  return nativeImage.createFromDataURL(FALLBACK_TRAY_ICON);
}

function showAndFocusWindow() {
  if (!mainWindow) {
    createWindow();
    return;
  }
  if (!mainWindow.isVisible()) {
    mainWindow.show();
  }
  if (mainWindow.isMinimized()) {
    mainWindow.restore();
  }
  mainWindow.focus();
}

function hasActiveDownloads() {
  if (!manager) return false;
  return manager
    .getTasks()
    .some((t) => t.status === 'downloading' || t.status === 'probing' || t.status === 'queued');
}

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
    if (typeof parsed.clipboardMonitor === 'boolean') {
      settings.clipboardMonitor = parsed.clipboardMonitor;
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

function sendToRenderer(channel, payload) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send(channel, payload);
  }
}

function updateTrayMenu() {
  if (!tray) return;
  const contextMenu = Menu.buildFromTemplate([
    {
      label: 'Buka ByteSplit',
      click: () => showAndFocusWindow(),
    },
    {
      label: 'Pantau Clipboard Otomatis',
      type: 'checkbox',
      checked: settings.clipboardMonitor,
      click: async (menuItem) => {
        settings.clipboardMonitor = Boolean(menuItem.checked);
        await saveSettings();
        sendToRenderer('clipboard:monitorState', settings.clipboardMonitor);
      },
    },
    { type: 'separator' },
    {
      label: 'Keluar',
      click: () => {
        isQuitting = true;
        app.quit();
      },
    },
  ]);
  tray.setContextMenu(contextMenu);
}

function createTray() {
  const icon = getAppIcon().resize({ width: 16, height: 16 });
  tray = new Tray(icon);
  tray.setToolTip('ByteSplit');
  updateTrayMenu();

  tray.on('click', () => {
    if (mainWindow && mainWindow.isVisible() && !mainWindow.isMinimized()) {
      mainWindow.focus();
    } else {
      showAndFocusWindow();
    }
  });
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1020,
    height: 660,
    minWidth: 780,
    minHeight: 520,
    title: 'ByteSplit',
    icon: getAppIcon(),
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

  mainWindow.on('close', (event) => {
    if (isQuitting) return;
    if (hasActiveDownloads()) {
      event.preventDefault();
      mainWindow.hide();
      if (!hasShownTrayNotice && Notification.isSupported()) {
        hasShownTrayNotice = true;
        new Notification({
          title: 'ByteSplit berjalan di latar belakang',
          body: 'Unduhan aktif tetap berjalan di System Tray.',
          icon: getAppIcon(),
        }).show();
      }
    }
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

function isValidHttpUrl(rawText) {
  if (typeof rawText !== 'string') return false;
  const trimmed = rawText.trim();
  if (!trimmed || trimmed.length > 2048 || /\s/.test(trimmed)) return false;
  try {
    const parsed = new URL(trimmed);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:';
  } catch {
    return false;
  }
}

function isDownloadableUrl(rawText) {
  if (!isValidHttpUrl(rawText)) return false;
  try {
    const parsed = new URL(rawText.trim());
    const ext = path.posix.extname(parsed.pathname).toLowerCase();
    return DOWNLOADABLE_EXTS.has(ext);
  } catch {
    return false;
  }
}

function startClipboardMonitor() {
  lastClipboardText = (clipboard.readText() || '').trim();

  clipboardTimer = setInterval(() => {
    if (!settings.clipboardMonitor) return;
    const current = (clipboard.readText() || '').trim();
    if (!current || current === lastClipboardText) return;
    lastClipboardText = current;

    if (isDownloadableUrl(current)) {
      showAndFocusWindow();
      sendToRenderer('clipboard:link', current);
    }
  }, 1200);
}

function registerIpc() {
  ipcMain.handle('state:get', () => ({
    tasks: manager.getTasks(),
    defaultOutputDir: settings.defaultOutputDir,
    speedLimitBps: settings.speedLimitBps,
    clipboardMonitor: settings.clipboardMonitor,
  }));

  ipcMain.handle('app:getInfo', () => ({
    name: 'ByteSplit',
    version: `v${pkg.version}`,
    about:
      'Aplikasi multi-connection HTTP/HTTPS download manager untuk Windows yang memecah file menjadi beberapa segmen paralel dan mengelompokkan hasil unduhan ke folder kategori otomatis.',
    developer: 'Muhammad Syahrin Harahap',
  }));

  ipcMain.handle('clipboard:setMonitor', async (_event, enabled) => {
    settings.clipboardMonitor = Boolean(enabled);
    await saveSettings();
    updateTrayMenu();
    return { ok: true, clipboardMonitor: settings.clipboardMonitor };
  });

  ipcMain.handle('download:add', (_event, payload) => {
    try {
      if (!payload || !isValidHttpUrl(payload.url)) {
        return { ok: false, error: 'URL unduhan wajib memakai protokol http:// atau https://.' };
      }

      const outputDir =
        typeof payload.outputDir === 'string' && payload.outputDir.trim()
          ? payload.outputDir.trim()
          : settings.defaultOutputDir;

      const filename =
        typeof payload.filename === 'string' && payload.filename.trim()
          ? path.basename(payload.filename.trim())
          : '';

      const task = manager.addDownload({
        url: payload.url.trim(),
        filename,
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

  ipcMain.handle('download:refreshLink', (_event, payload) => {
    if (!payload || typeof payload.id !== 'string' || !isValidHttpUrl(payload.newUrl)) {
      return { ok: false, error: 'ID tugas dan URL baru (http/https) wajib valid.' };
    }
    if (typeof manager.refreshLink !== 'function') {
      return { ok: false, error: 'Metode refreshLink belum ditambahkan di core/manager.js.' };
    }
    try {
      const task = manager.refreshLink(payload.id, payload.newUrl.trim());
      return { ok: true, task };
    } catch (err) {
      return { ok: false, error: err.message || 'Gagal memperbarui link unduhan.' };
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

const gotSingleInstanceLock = app.requestSingleInstanceLock();
if (!gotSingleInstanceLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    showAndFocusWindow();
  });

  app.whenReady().then(async () => {
    if (process.platform === 'win32') {
      app.setAppUserModelId('com.bytesplit.app');
    }

    const dataDir = path.join(app.getPath('userData'), 'data');
    await fs.mkdir(dataDir, { recursive: true });
    await loadSettings(dataDir);

    manager = new DownloadManager({
      dataDir,
      defaultOutputDir: settings.defaultOutputDir,
    });
    await manager.init();
    manager.setSpeedLimit(settings.speedLimitBps);

    for (const t of manager.getTasks()) {
      lastTaskStatuses.set(t.id, t.status);
    }

    manager.on('update', (task) => {
      const prevStatus = lastTaskStatuses.get(task.id);
      lastTaskStatuses.set(task.id, task.status);

      if (prevStatus && prevStatus !== 'completed' && task.status === 'completed') {
        if (Notification.isSupported()) {
          new Notification({
            title: 'Unduhan Selesai',
            body: task.fileName || 'File berhasil diunduh.',
            icon: getAppIcon(),
          }).show();
        }
      }

      sendToRenderer('download:update', task);
    });

    manager.on('list', (tasks) => {
      for (const t of tasks) {
        lastTaskStatuses.set(t.id, t.status);
      }
      sendToRenderer('download:list', tasks);
    });

    registerIpc();
    createTray();
    createWindow();
    startClipboardMonitor();
  });
}

app.on('before-quit', async (event) => {
  if (clipboardTimer) {
    clearInterval(clipboardTimer);
    clipboardTimer = null;
  }
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