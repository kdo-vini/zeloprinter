import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import type { PrinterInfo } from './types.js';

const execFileAsync = promisify(execFile);

function psAvailable(): boolean {
  return process.platform === 'win32';
}

function normalizeStatus(value: unknown): string {
  const status = Number(value);
  if (status === 3) return 'ready';
  if (status === 4) return 'printing';
  if (status === 5) return 'warming_up';
  if (status === 7) return 'offline';
  return Number.isFinite(status) ? `status_${status}` : 'unknown';
}

export async function listPrinters(): Promise<PrinterInfo[]> {
  if (!psAvailable()) return [];

  const command = [
    'Get-CimInstance Win32_Printer',
    'Select-Object Name,DeviceID,Default,WorkOffline,PrinterStatus,PortName,DriverName',
    'ConvertTo-Json -Depth 3 -Compress'
  ].join(' | ');

  const { stdout } = await execFileAsync('powershell.exe', [
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-Command',
    command
  ], { windowsHide: true, maxBuffer: 1024 * 1024 });

  if (!stdout.trim()) return [];
  const parsed = JSON.parse(stdout);
  const rows = Array.isArray(parsed) ? parsed : [parsed];

  return rows.map((row) => ({
    id: String(row.DeviceID || row.Name),
    name: String(row.Name || row.DeviceID),
    isDefault: Boolean(row.Default),
    isOffline: Boolean(row.WorkOffline),
    status: normalizeStatus(row.PrinterStatus),
    portName: row.PortName ? String(row.PortName) : undefined,
    driverName: row.DriverName ? String(row.DriverName) : undefined
  }));
}

export async function resolvePrinter(idOrName?: string | null): Promise<PrinterInfo | null> {
  const printers = await listPrinters();
  if (!printers.length) return null;
  if (idOrName) {
    const exact = printers.find((p) => p.id === idOrName || p.name === idOrName);
    if (exact) return exact;
  }
  return printers.find((p) => p.isDefault) || printers[0] || null;
}
