import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('zeloImpressao', {
  getState: () => ipcRenderer.invoke('settings:get-state'),
  saveConfig: (patch: unknown) => ipcRenderer.invoke('settings:save-config', patch),
  testPrint: (printerId?: string) => ipcRenderer.invoke('settings:test-print', printerId),
  refreshPairingCode: () => ipcRenderer.invoke('settings:pairing-code'),
  openLogs: () => ipcRenderer.invoke('settings:open-logs'),
  restartApi: () => ipcRenderer.invoke('settings:restart-api')
});
