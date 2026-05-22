import { z } from 'zod';

export const printJobSchema = z.object({
  source: z.enum(['zelopdv', 'zelochat']),
  companyStoreId: z.string().max(120).optional(),
  type: z.enum(['receipt', 'kitchen_order', 'test', 'raw_escpos']),
  printerId: z.string().max(240).optional(),
  printerName: z.string().max(240).optional(),
  timestamp: z.string().datetime().or(z.string().max(80)),
  content: z.discriminatedUnion('format', [
    z.object({ format: z.literal('text'), text: z.string().min(1).max(120_000) }),
    z.object({ format: z.literal('html'), html: z.string().min(1).max(300_000) }),
    z.object({ format: z.literal('raw_escpos_base64'), base64: z.string().min(1).max(400_000) })
  ]),
  metadata: z.record(z.unknown()).optional()
});

export const configPatchSchema = z.object({
  selectedPrinterId: z.string().max(240).nullable().optional(),
  selectedPrinterName: z.string().max(240).nullable().optional(),
  startWithWindows: z.boolean().optional(),
  requirePairing: z.boolean().optional()
});
