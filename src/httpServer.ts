import http, { type IncomingMessage, type ServerResponse } from 'node:http';
import { API_HOST, API_PORT, API_VERSION, MAX_JSON_BYTES, PRODUCT_NAME } from './constants.js';
import { getConfig, updateConfig, verifyToken } from './config.js';
import { confirmPairing } from './pairing.js';
import { buildHealthCapabilities, type PrintService } from './printing.js';
import { listPrinters } from './printers.js';
import { configPatchSchema, printJobSchema } from './validation.js';
import { log } from './logger.js';

function isOriginAllowed(origin: string | undefined): boolean {
  if (!origin) return true;
  return getConfig().allowedOrigins.includes(origin);
}

function setCors(req: IncomingMessage, res: ServerResponse): boolean {
  const origin = req.headers.origin;
  if (typeof origin === 'string' && isOriginAllowed(origin)) {
    res.setHeader('Access-Control-Allow-Origin', origin);
    res.setHeader('Vary', 'Origin');
  } else if (origin) {
    res.statusCode = 403;
    res.end(JSON.stringify({ ok: false, error: 'Origem não autorizada.' }));
    return false;
  }
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Zelo-Impressao-Token');
  res.setHeader('Access-Control-Max-Age', '600');
  return true;
}

function sendJson(res: ServerResponse, status: number, body: unknown): void {
  res.statusCode = status;
  res.setHeader('Content-Type', 'application/json; charset=utf-8');
  res.end(JSON.stringify(body));
}

async function readJson(req: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    const buf = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    size += buf.length;
    if (size > MAX_JSON_BYTES) throw Object.assign(new Error('Payload muito grande.'), { statusCode: 413 });
    chunks.push(buf);
  }
  if (!chunks.length) return {};
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

function requireAuth(req: IncomingMessage, res: ServerResponse): boolean {
  const token = req.headers['x-zelo-impressao-token'];
  const value = Array.isArray(token) ? token[0] : token;
  if (verifyToken(value)) return true;
  sendJson(res, 401, {
    ok: false,
    code: 'PAIRING_REQUIRED',
    message: 'Pareie este navegador com o Zelo Impressão.'
  });
  return false;
}

export function startHttpServer(printService: PrintService): Promise<http.Server> {
  const server = http.createServer(async (req, res) => {
    try {
      if (!setCors(req, res)) return;
      if (req.method === 'OPTIONS') {
        res.statusCode = 204;
        res.end();
        return;
      }

      const url = new URL(req.url || '/', `http://${API_HOST}:${API_PORT}`);

      if (req.method === 'GET' && url.pathname === '/health') {
        const memory = process.memoryUsage();
        sendJson(res, 200, {
          ok: true,
          status: 'running',
          productName: PRODUCT_NAME,
          version: API_VERSION,
          os: process.platform,
          memory: {
            rssMb: Math.round(memory.rss / 1024 / 1024),
            heapUsedMb: Math.round(memory.heapUsed / 1024 / 1024)
          },
          pairingRequired: getConfig().requirePairing,
          paired: Boolean(getConfig().tokenHash),
          capabilities: buildHealthCapabilities()
        });
        return;
      }

      if (req.method === 'POST' && url.pathname === '/pair') {
        const body = await readJson(req) as { code?: string };
        const paired = confirmPairing(String(body.code || ''));
        if (!paired) {
          sendJson(res, 401, { ok: false, message: 'Código de pareamento inválido ou expirado.' });
          return;
        }
        sendJson(res, 200, { ok: true, token: paired.token });
        return;
      }

      if (!requireAuth(req, res)) return;

      if (req.method === 'GET' && url.pathname === '/printers') {
        sendJson(res, 200, { ok: true, printers: await listPrinters() });
        return;
      }

      if (req.method === 'GET' && url.pathname === '/config') {
        const cfg = getConfig();
        sendJson(res, 200, {
          ok: true,
          config: {
            selectedPrinterId: cfg.selectedPrinterId,
            selectedPrinterName: cfg.selectedPrinterName,
            startWithWindows: cfg.startWithWindows,
            requirePairing: cfg.requirePairing,
            allowedOrigins: cfg.allowedOrigins
          }
        });
        return;
      }

      if (req.method === 'POST' && url.pathname === '/config') {
        const parsed = configPatchSchema.parse(await readJson(req));
        const cfg = updateConfig(parsed);
        sendJson(res, 200, { ok: true, config: cfg });
        return;
      }

      if (req.method === 'POST' && url.pathname === '/print') {
        const job = printJobSchema.parse(await readJson(req));
        const result = await printService.print(job);
        await log('print_job_ok', { source: job.source, type: job.type, printer: result.printer.name, mode: result.mode });
        sendJson(res, 200, { ok: true, printer: result.printer, mode: result.mode });
        return;
      }

      if (req.method === 'POST' && url.pathname === '/test-print') {
        const body = await readJson(req) as { printerId?: string };
        const result = await printService.testPrint(body.printerId);
        await log('test_print_ok', { printer: result.printer.name });
        sendJson(res, 200, { ok: true, printer: result.printer, mode: result.mode });
        return;
      }

      sendJson(res, 404, { ok: false, message: 'Endpoint não encontrado.' });
    } catch (error) {
      const err = error as Error & { statusCode?: number };
      await log('api_error', { message: err.message, url: req.url });
      sendJson(res, err.statusCode || 400, {
        ok: false,
        message: err.message || 'Falha ao processar solicitação.'
      });
    }
  });

  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(API_PORT, API_HOST, () => {
      server.off('error', reject);
      resolve(server);
    });
  });
}
