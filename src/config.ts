import { createHash, randomBytes } from 'node:crypto';
import Store from 'electron-store';
import { app } from 'electron';
import { DEFAULT_ALLOWED_ORIGINS } from './constants.js';
import type { AgentConfig } from './types.js';

interface StoredShape {
  config: AgentConfig;
}

const defaults: StoredShape = {
  config: {
    selectedPrinterId: null,
    selectedPrinterName: null,
    startWithWindows: true,
    requirePairing: true,
    tokenHash: null,
    allowedOrigins: DEFAULT_ALLOWED_ORIGINS
  }
};

const store = new Store<StoredShape>({
  name: 'zelo-impressao',
  defaults
});

export function getConfig(): AgentConfig {
  return store.get('config');
}

export function updateConfig(patch: Partial<AgentConfig>): AgentConfig {
  const next = { ...getConfig(), ...patch };
  store.set('config', next);
  applyStartupSetting(next.startWithWindows);
  return next;
}

export function applyStartupSetting(enabled = getConfig().startWithWindows): void {
  if (!app.isPackaged && process.platform !== 'win32') return;
  app.setLoginItemSettings({
    openAtLogin: enabled,
    path: process.execPath
  });
}

export function hashToken(token: string): string {
  return createHash('sha256').update(token).digest('hex');
}

export function verifyToken(token: string | undefined | null): boolean {
  const cfg = getConfig();
  if (!cfg.requirePairing) return true;
  if (!cfg.tokenHash || !token) return false;
  return hashToken(token) === cfg.tokenHash;
}

export function issueToken(): string {
  const token = randomBytes(32).toString('base64url');
  updateConfig({ tokenHash: hashToken(token) });
  return token;
}
