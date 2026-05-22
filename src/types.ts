export type AppSource = 'zelopdv' | 'zelochat';
export type PrintJobType = 'receipt' | 'kitchen_order' | 'test' | 'raw_escpos';

export interface PrinterInfo {
  id: string;
  name: string;
  isDefault: boolean;
  isOffline: boolean;
  status: string;
  driverName?: string;
  portName?: string;
}

export interface AgentConfig {
  selectedPrinterId: string | null;
  selectedPrinterName: string | null;
  startWithWindows: boolean;
  requirePairing: boolean;
  tokenHash: string | null;
  allowedOrigins: string[];
}

export interface PrintJob {
  source: AppSource;
  companyStoreId?: string;
  type: PrintJobType;
  printerId?: string;
  printerName?: string;
  timestamp: string;
  content:
    | { format: 'text'; text: string }
    | { format: 'html'; html: string }
    | { format: 'raw_escpos_base64'; base64: string };
  metadata?: Record<string, unknown>;
}
