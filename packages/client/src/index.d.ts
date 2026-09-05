export const ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL: string;
export const ZELO_IMPRESSAO_DOWNLOADS_BASE_URL: string;
export const ZELO_IMPRESSAO_INSTALLER_FILENAME: string;
export const ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL: string;
export const ZELO_IMPRESSAO_BROWSER_SDK_URL: string;
export const ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE: string;
export const ZELO_IMPRESSAO_PRINTER_UNAVAILABLE_MESSAGE: string;
export const ZELO_IMPRESSAO_OUTCOME_UNKNOWN_MESSAGE: string;

export type ZeloImpressaoSource = "zelopdv" | "zelochat";
export type ZeloImpressaoJobType =
  | "receipt"
  | "kitchen_order"
  | "test"
  | "raw_escpos";

export type ZeloImpressaoPrintIntent =
  | { mode: "automatic"; orderId: string; purpose: "order_ticket" }
  | { mode: "manual"; orderId?: string; purpose?: string };

export interface ZeloImpressaoPrintResult {
  ok: true;
  jobId?: string;
  status: "spooled" | "deduplicated";
  /** Null on a replay restored from privacy-preserving persistent history. */
  printer: ZeloImpressaoPrinter | null;
  mode: "raw" | "driver";
  arbitration: { mode: "automatic"; source: ZeloImpressaoSource; orderId: string; purpose: "order_ticket"; duplicate: boolean } | null;
}

export interface ZeloImpressaoPrinter {
  id: string;
  name: string;
  isDefault: boolean;
  isOffline: boolean;
  status: string;
  driverName?: string;
  portName?: string;
}

/** jobId identifies one print intention. Reuse only for retries; a second copy gets a new id. */
export interface ZeloImpressaoPrintJob {
  jobId?: string;
  source: ZeloImpressaoSource;
  companyStoreId?: string;
  /** Automatic: companyStoreId = owner auth UUID, orderId = public.zelo_orders.id. */
  intent?: ZeloImpressaoPrintIntent;
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

export interface ZeloImpressaoClientOptions extends Record<string, unknown> {
  baseUrl?: string;
  timeoutMs?: number;
  autoConnect?: boolean;
}

export function getZeloImpressaoInstallerUrl(channel?: string): string;
export function getZeloImpressaoDownloadPageUrl(): string;
export function getZeloImpressaoBrowserSdkUrl(): string;
export function detectZeloImpressao(
  options?: ZeloImpressaoClientOptions,
): Promise<{
  installed: boolean;
  running: boolean;
  paired: boolean;
  autoConnected?: boolean;
  health?: unknown;
  error?: unknown;
  autoConnectError?: unknown;
  message?: string;
}>;
export function connectZeloImpressao(
  options?: ZeloImpressaoClientOptions,
): Promise<unknown>;
export function pairZeloImpressao(
  code: string,
  options?: ZeloImpressaoClientOptions,
): Promise<unknown>;
export function getPrinters(
  options?: Record<string, unknown>,
): Promise<ZeloImpressaoPrinter[]>;
export function getConfig(options?: Record<string, unknown>): Promise<{
  selectedPrinterId: string | null;
  selectedPrinterName: string | null;
  startWithWindows: boolean;
  requirePairing: boolean;
  autoConnectEnabled: boolean;
  allowedOrigins: string[];
  preferredAutoPrintSource: ZeloImpressaoSource;
  printHistoryCapacity: number;
}>;
export function saveConfig(
  config: Record<string, unknown>,
  options?: Record<string, unknown>,
): Promise<unknown>;
export function sendPrintJob(
  job: ZeloImpressaoPrintJob,
  options?: Record<string, unknown>,
): Promise<ZeloImpressaoPrintResult>;
export function sendRawEscposPrintJob(
  job: {
    jobId?: string;
    source: ZeloImpressaoSource;
    companyStoreId?: string;
    intent?: ZeloImpressaoPrintIntent;
    printerId?: string;
    printerName?: string;
    bytes: Uint8Array | number[];
    type?: ZeloImpressaoJobType;
    metadata?: Record<string, unknown>;
  },
  options?: Record<string, unknown>,
): Promise<ZeloImpressaoPrintResult>;
export function sendTestPrint(
  printerId?: string,
  options?: Record<string, unknown>,
): Promise<unknown>;
export function fallbackToBrowserPrint(html: string): Promise<void>;
export function getZeloImpressaoFriendlyMessage(error: unknown): string;
export function clearZeloImpressaoPairing(): void;
