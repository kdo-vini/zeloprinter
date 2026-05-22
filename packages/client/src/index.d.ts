export const ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL: string;
export const ZELO_IMPRESSAO_DOWNLOADS_BASE_URL: string;
export const ZELO_IMPRESSAO_INSTALLER_FILENAME: string;
export const ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL: string;
export const ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE: string;
export const ZELO_IMPRESSAO_PRINTER_UNAVAILABLE_MESSAGE: string;

export type ZeloImpressaoSource = "zelopdv" | "zelochat";
export type ZeloImpressaoJobType =
  | "receipt"
  | "kitchen_order"
  | "test"
  | "raw_escpos";

export interface ZeloImpressaoPrinter {
  id: string;
  name: string;
  isDefault: boolean;
  isOffline: boolean;
  status: string;
  driverName?: string;
  portName?: string;
}

export interface ZeloImpressaoPrintJob {
  source: ZeloImpressaoSource;
  companyStoreId?: string;
  type: ZeloImpressaoJobType;
  printerId?: string;
  printerName?: string;
  timestamp?: string;
  content:
    | { format: "text"; text: string }
    | { format: "html"; html: string }
    | { format: "raw_escpos_base64"; base64: string };
  metadata?: Record<string, unknown>;
}

export function getZeloImpressaoInstallerUrl(channel?: string): string;
export function getZeloImpressaoDownloadPageUrl(): string;
export function detectZeloImpressao(
  options?: Record<string, unknown>,
): Promise<{
  installed: boolean;
  running: boolean;
  paired: boolean;
  health?: unknown;
  error?: unknown;
  message?: string;
}>;
export function pairZeloImpressao(
  code: string,
  options?: Record<string, unknown>,
): Promise<unknown>;
export function getPrinters(
  options?: Record<string, unknown>,
): Promise<ZeloImpressaoPrinter[]>;
export function getConfig(options?: Record<string, unknown>): Promise<{
  selectedPrinterId: string | null;
  selectedPrinterName: string | null;
  startWithWindows: boolean;
  requirePairing: boolean;
  allowedOrigins: string[];
}>;
export function saveConfig(
  config: Record<string, unknown>,
  options?: Record<string, unknown>,
): Promise<unknown>;
export function sendPrintJob(
  job: ZeloImpressaoPrintJob,
  options?: Record<string, unknown>,
): Promise<unknown>;
export function sendRawEscposPrintJob(
  job: {
    source: ZeloImpressaoSource;
    companyStoreId?: string;
    printerId?: string;
    printerName?: string;
    bytes: Uint8Array | number[];
    type?: ZeloImpressaoJobType;
    metadata?: Record<string, unknown>;
  },
  options?: Record<string, unknown>,
): Promise<unknown>;
export function sendTestPrint(
  printerId?: string,
  options?: Record<string, unknown>,
): Promise<unknown>;
export function fallbackToBrowserPrint(html: string): Promise<void>;
export function getZeloImpressaoFriendlyMessage(error: unknown): string;
export function clearZeloImpressaoPairing(): void;
