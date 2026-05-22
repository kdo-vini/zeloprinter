import { execFile } from 'node:child_process';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);

function encodePowerShell(script: string): string {
  return Buffer.from(script, 'utf16le').toString('base64');
}

const RAW_PRINT_SCRIPT = String.raw`
param([string]$PrinterName, [string]$Path)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class RawPrinterHelper {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)]
  public class DOCINFOA {
    [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
    [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
    [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
  }
  [DllImport("winspool.Drv", EntryPoint="OpenPrinterA", SetLastError=true, CharSet=CharSet.Ansi, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);
  [DllImport("winspool.Drv", EntryPoint="ClosePrinter", SetLastError=true, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool ClosePrinter(IntPtr hPrinter);
  [DllImport("winspool.Drv", EntryPoint="StartDocPrinterA", SetLastError=true, CharSet=CharSet.Ansi, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);
  [DllImport("winspool.Drv", EntryPoint="EndDocPrinter", SetLastError=true, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool EndDocPrinter(IntPtr hPrinter);
  [DllImport("winspool.Drv", EntryPoint="StartPagePrinter", SetLastError=true, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool StartPagePrinter(IntPtr hPrinter);
  [DllImport("winspool.Drv", EntryPoint="EndPagePrinter", SetLastError=true, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool EndPagePrinter(IntPtr hPrinter);
  [DllImport("winspool.Drv", EntryPoint="WritePrinter", SetLastError=true, ExactSpelling=true, CallingConvention=CallingConvention.StdCall)]
  public static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, Int32 dwCount, out Int32 dwWritten);
  public static void SendBytes(string printerName, byte[] bytes) {
    IntPtr hPrinter;
    if (!OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    try {
      DOCINFOA di = new DOCINFOA();
      di.pDocName = "Zelo Impressao";
      di.pDataType = "RAW";
      if (!StartDocPrinter(hPrinter, 1, di)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
      try {
        if (!StartPagePrinter(hPrinter)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
          int written;
          if (!WritePrinter(hPrinter, bytes, bytes.Length, out written)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
          if (written != bytes.Length) throw new Exception("Nem todos os bytes foram enviados para a impressora.");
        } finally { EndPagePrinter(hPrinter); }
      } finally { EndDocPrinter(hPrinter); }
    } finally { ClosePrinter(hPrinter); }
  }
}
"@
[byte[]]$bytes = [System.IO.File]::ReadAllBytes($Path)
[RawPrinterHelper]::SendBytes($PrinterName, $bytes)
`;

export async function printRawWindows(printerName: string, bytes: Buffer): Promise<void> {
  if (process.platform !== 'win32') {
    throw new Error('Impressão RAW está disponível apenas no Windows.');
  }

  const dir = await mkdtemp(join(tmpdir(), 'zelo-impressao-'));
  const file = join(dir, 'job.bin');
  try {
    await writeFile(file, bytes);
    await execFileAsync('powershell.exe', [
      '-NoProfile',
      '-ExecutionPolicy',
      'Bypass',
      '-EncodedCommand',
      encodePowerShell(RAW_PRINT_SCRIPT),
      '-PrinterName',
      printerName,
      '-Path',
      file
    ], { windowsHide: true, timeout: 30_000, maxBuffer: 1024 * 1024 });
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
}
