import { PRODUCT_NAME } from './constants.js';
import { getConfig } from './config.js';
import { buildTestReceipt } from './escpos.js';
import { printRawWindows } from './powershellPrint.js';
import { resolvePrinter } from './printers.js';
import type { PrintJob, PrinterInfo } from './types.js';

export type HtmlPrinter = (html: string, printerName: string) => Promise<void>;

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function textToHtml(text: string): string {
  return `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    @page { margin: 4mm; }
    body { margin: 0; color: #000; background: #fff; font: 12px/1.25 Consolas, "Courier New", monospace; }
    pre { white-space: pre-wrap; margin: 0; }
  </style>
</head>
<body><pre>${escapeHtml(text)}</pre></body>
</html>`;
}

function stripHtml(html: string): string {
  return html
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<\/(p|div|tr|li|h[1-6])>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .trim();
}

export class PrintService {
  constructor(private readonly printHtml?: HtmlPrinter) {}

  async print(job: PrintJob): Promise<{ printer: PrinterInfo; mode: 'raw' | 'driver' }> {
    const cfg = getConfig();
    const requested = job.printerId || job.printerName || cfg.selectedPrinterId || cfg.selectedPrinterName;
    const printer = await resolvePrinter(requested);
    if (!printer) {
      throw new Error('Nenhuma impressora instalada foi encontrada no Windows.');
    }
    if (printer.isOffline) {
      throw new Error('A impressora selecionada está offline.');
    }

    if (job.content.format === 'raw_escpos_base64') {
      const bytes = Buffer.from(job.content.base64, 'base64');
      await printRawWindows(printer.name, bytes);
      return { printer, mode: 'raw' };
    }

    const html = job.content.format === 'html'
      ? job.content.html
      : textToHtml(job.content.text);

    if (this.printHtml) {
      await this.printHtml(html, printer.name);
      return { printer, mode: 'driver' };
    }

    const textBytes = Buffer.from(stripHtml(html) + '\n\n\n', 'utf8');
    await printRawWindows(printer.name, textBytes);
    return { printer, mode: 'raw' };
  }

  async testPrint(printerId?: string): Promise<{ printer: PrinterInfo; mode: 'raw' }> {
    const cfg = getConfig();
    const printer = await resolvePrinter(printerId || cfg.selectedPrinterId || cfg.selectedPrinterName);
    if (!printer) throw new Error('Nenhuma impressora instalada foi encontrada no Windows.');
    await printRawWindows(printer.name, buildTestReceipt());
    return { printer, mode: 'raw' };
  }
}

export function buildHealthCapabilities() {
  return {
    rawEscpos: process.platform === 'win32',
    windowsDriverPrinting: true,
    testPrint: process.platform === 'win32',
    printerSelection: true,
    silentPrinting: true,
    productName: PRODUCT_NAME
  };
}
