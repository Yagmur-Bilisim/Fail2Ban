const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const path = require('path');
const { autoUpdater } = require('electron-updater');

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
  if (isDev) return;

  autoUpdater.autoDownload = true;
  autoUpdater.autoInstallOnAppQuit = true;

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
    let userMessage = err.message;
    let detail = '';

    if (err.message.includes('net::ERR_INTERNET_DISCONNECTED') || err.message.includes('ENOTFOUND') || err.message.includes('ECONNREFUSED')) {
      userMessage = 'Güncelleme sunucusuna ulaşılamıyor.';
      detail = 'İnternet bağlantınızı kontrol edin veya daha sonra tekrar deneyin.\n\nDetay: ' + err.message;
    } else if (err.message.includes('ENOENT') || err.message.includes('latest.yml')) {
      userMessage = 'Güncelleme dosyası bulunamadı.';
      detail = 'Sunucuda güncelleme paketi henüz yayınlanmamış olabilir.\n\nDetay: ' + err.message;
    } else if (err.message.includes('sha512') || err.message.includes('checksum') || err.message.includes('hash')) {
      userMessage = 'İndirilen dosya bozuk (checksum hatası).';
      detail = 'İndirme sırasında dosya bozulmuş olabilir. Tekrar deneyiniz.\n\nDetay: ' + err.message;
    } else if (err.message.includes('certificate') || err.message.includes('SSL') || err.message.includes('CERT')) {
      userMessage = 'SSL sertifika hatası.';
      detail = 'Güncelleme sunucusunun SSL sertifikası doğrulanamadı.\n\nDetay: ' + err.message;
    } else if (err.message.includes('EPERM') || err.message.includes('EACCES') || err.message.includes('permission')) {
      userMessage = 'Yetki hatası — güncelleme kurulamadı.';
      detail = 'Uygulamayı yönetici olarak çalıştırmayı deneyin.\n\nDetay: ' + err.message;
    } else if (err.message.includes('ENOSPC') || err.message.includes('disk')) {
      userMessage = 'Yetersiz disk alanı.';
      detail = 'Güncelleme indirmek için yeterli disk alanı yok.\n\nDetay: ' + err.message;
    }

    send('updater:status', { status: 'error', message: userMessage });

    dialog.showMessageBox(mainWindow, {
      type: 'error',
      title: 'Güncelleme Hatası',
      message: userMessage,
      detail: detail || err.message,
      buttons: ['Tamam']
    });
  });

  ipcMain.on('updater:check', () => {
    autoUpdater.checkForUpdates();
  });

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
