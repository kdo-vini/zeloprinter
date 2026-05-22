import { appendFile, mkdir } from 'node:fs/promises';
import { app, shell } from 'electron';
import { join } from 'node:path';

export function logsDir(): string {
  return join(app.getPath('userData'), 'logs');
}

export async function log(message: string, data?: unknown): Promise<void> {
  try {
    await mkdir(logsDir(), { recursive: true });
    const line = JSON.stringify({
      ts: new Date().toISOString(),
      message,
      data
    });
    await appendFile(join(logsDir(), 'zelo-impressao.log'), line + '\n', 'utf8');
  } catch {
    // Logging must never block printing.
  }
}

export async function openLogsFolder(): Promise<void> {
  await mkdir(logsDir(), { recursive: true });
  await shell.openPath(logsDir());
}
