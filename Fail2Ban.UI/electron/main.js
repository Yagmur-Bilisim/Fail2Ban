import { app, BrowserWindow, ipcMain, dialog } from 'electron';
import path from 'path';
import { fileURLToPath } from 'url';
import { createRequire } from 'module';

const require = createRequire(import.meta.url);
const { autoUpdater } = require('electron-updater');

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const isDev = process.env.IS_DEV === 'true';

let mainWindow;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1200,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: true,
      contextIsolation: false
    },
    autoHideMenuBar: true,
    backgroundColor: '#0f172a'
  });

  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
    mainWindow.webContents.openDevTools();
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

// ── Auto Updater ──────────────────────────────────────────────────────────────

function setupAutoUpdater() {
  // Dev modda güncelleme kontrolü yapma
  if (isDev) return;

  autoUpdater.autoDownload = true;       // Güncelleme bulununca arka planda indir
  autoUpdater.autoInstallOnAppQuit = true; // Uygulama kapanınca kur

  // Renderer'a durum bildir
  const send = (channel, data) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send(channel, data);
    }
  };

  autoUpdater.on('checking-for-update', () => {
    send('updater:status', { status: 'checking' });
  });

  autoUpdater.on('update-available', (info) => {
    send('updater:status', { status: 'available', version: info.version });
  });

  autoUpdater.on('update-not-available', () => {
    send('updater:status', { status: 'latest' });
  });

  autoUpdater.on('download-progress', (progress) => {
    send('updater:progress', {
      percent: Math.round(progress.percent),
      transferred: progress.transferred,
      total: progress.total,
      bytesPerSecond: progress.bytesPerSecond
    });
  });

  autoUpdater.on('update-downloaded', (info) => {
    send('updater:status', { status: 'downloaded', version: info.version });

    // Kullanıcıya sor: hemen kur mu?
    dialog.showMessageBox(mainWindow, {
      type: 'info',
      title: 'Güncelleme Hazır',
      message: `v${info.version} indirildi.`,
      detail: 'Uygulamayı yeniden başlatarak güncelleyebilirsiniz.',
      buttons: ['Şimdi Yeniden Başlat', 'Sonra'],
      defaultId: 0,
      cancelId: 1
    }).then(({ response }) => {
      if (response === 0) autoUpdater.quitAndInstall();
    });
  });

  autoUpdater.on('error', (err) => {
    send('updater:status', { status: 'error', message: err.message });
  });

  // Renderer'dan manuel kontrol isteği
  ipcMain.on('updater:check', () => {
    autoUpdater.checkForUpdates();
  });

  // Uygulama hazır olunca 5 sn bekleyip kontrol et (pencere tam yüklenmeden önce çıkmasın)
  setTimeout(() => autoUpdater.checkForUpdates(), 5000);
}

// ─────────────────────────────────────────────────────────────────────────────

app.whenReady().then(() => {
  createWindow();
  setupAutoUpdater();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

app.on('activate', () => {
  if (mainWindow === null) createWindow();
});
