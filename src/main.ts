import { app, BrowserWindow, ipcMain, Menu, nativeImage, Tray } from 'electron';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { PRODUCT_DESCRIPTION, PRODUCT_NAME } from './constants.js';
import { applyStartupSetting, getConfig, updateConfig } from './config.js';
import { getPairingCode } from './pairing.js';
import { startHttpServer } from './httpServer.js';
import { log, openLogsFolder } from './logger.js';
import { PrintService } from './printing.js';
import { listPrinters } from './printers.js';

const __dirname = dirname(fileURLToPath(import.meta.url));

let tray: Tray | null = null;
let settingsWindow: BrowserWindow | null = null;
let printService: PrintService;
let apiServer: Awaited<ReturnType<typeof startHttpServer>> | null = null;

const singleInstanceLock = app.requestSingleInstanceLock();
if (!singleInstanceLock) {
  app.quit();
}

app.on('second-instance', () => {
  showSettings();
});

async function printHtmlSilently(html: string, printerName: string): Promise<void> {
  const win = new BrowserWindow({
    show: false,
    webPreferences: { sandbox: true }
  });
  try {
    await win.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(html)}`);
    await new Promise<void>((resolve, reject) => {
      win.webContents.print(
        { silent: true, deviceName: printerName, printBackground: true },
        (success, failureReason) => {
          if (success) resolve();
          else reject(new Error(failureReason || 'Falha ao imprimir pelo driver do Windows.'));
        }
      );
    });
  } finally {
    win.close();
  }
}

function createTray(): void {
  const icon = nativeImage.createFromDataURL(
    'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAIElEQVR4AWP4//8/AyUYTFhYGJqGgQFVMjIwMDAAAH6uAxEU7c6QAAAAAElFTkSuQmCC'
  );
  tray = new Tray(icon);
  tray.setToolTip(PRODUCT_NAME);
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: PRODUCT_NAME, enabled: false },
    { label: PRODUCT_DESCRIPTION, enabled: false },
    { type: 'separator' },
    { label: 'Configurações', click: () => showSettings() },
    { label: 'Abrir logs', click: () => void openLogsFolder() },
    { type: 'separator' },
    { label: 'Sair', click: () => app.quit() }
  ]));
  tray.on('double-click', () => showSettings());
}

function showSettings(): void {
  if (settingsWindow) {
    settingsWindow.focus();
    return;
  }
  settingsWindow = new BrowserWindow({
    width: 760,
    height: 640,
    title: PRODUCT_NAME,
    webPreferences: {
      preload: join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: false
    }
  });
  settingsWindow.removeMenu();
  settingsWindow.loadFile(join(__dirname, '../src/renderer/settings.html'));
  settingsWindow.on('closed', () => { settingsWindow = null; });
}

async function startApi(): Promise<void> {
  if (apiServer) return;
  apiServer = await startHttpServer(printService);
  await log('api_started');
}

async function restartApi(): Promise<void> {
  if (apiServer) {
    await new Promise<void>((resolve) => apiServer?.close(() => resolve()));
    apiServer = null;
  }
  await startApi();
}

app.whenReady().then(async () => {
  printService = new PrintService(printHtmlSilently);
  applyStartupSetting();
  createTray();
  await startApi();

  ipcMain.handle('settings:get-state', async () => ({
    config: getConfig(),
    printers: await listPrinters(),
    pairing: getPairingCode(),
    version: app.getVersion(),
    apiRunning: Boolean(apiServer)
  }));
  ipcMain.handle('settings:save-config', async (_event, patch) => updateConfig(patch || {}));
  ipcMain.handle('settings:test-print', async (_event, printerId?: string) => printService.testPrint(printerId));
  ipcMain.handle('settings:pairing-code', async () => getPairingCode());
  ipcMain.handle('settings:open-logs', async () => openLogsFolder());
  ipcMain.handle('settings:restart-api', async () => {
    await restartApi();
    return { ok: true };
  });

  if (process.argv.includes('--show')) showSettings();
});

app.on('window-all-closed', () => {
  // Keep the tray component running after the settings window closes.
});
