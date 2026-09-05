const DEFAULT_BASE_URL = "http://127.0.0.1:17321";
const TOKEN_KEY = "zelo_impressao_token_v1";
const TIMEOUT_MS = 1800;

export const ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL =
  "https://zelopdv.com.br/zelo-impressao";
export const ZELO_IMPRESSAO_DOWNLOADS_BASE_URL =
  "https://zelopdv.com.br/downloads/zelo-impressao";
export const ZELO_IMPRESSAO_INSTALLER_FILENAME = "Zelo-Impressao-Setup.exe";
export const ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL = `${ZELO_IMPRESSAO_DOWNLOADS_BASE_URL}/latest/${ZELO_IMPRESSAO_INSTALLER_FILENAME}`;
export const ZELO_IMPRESSAO_BROWSER_SDK_URL =
  `${ZELO_IMPRESSAO_DOWNLOADS_BASE_URL}/sdk/zelo-impressao-client.browser.js`;

export const ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE =
  "O Zelo Impressão não está aberto neste computador. Abra o aplicativo ou use a impressão pelo navegador.";

export const ZELO_IMPRESSAO_PRINTER_UNAVAILABLE_MESSAGE =
  "Não conseguimos acessar a impressora selecionada. Verifique se ela está ligada e conectada.";
export const ZELO_IMPRESSAO_OUTCOME_UNKNOWN_MESSAGE =
  "Não foi possível confirmar a impressão. Confira a saída antes de tentar novamente.";

function normalizeReleaseChannel(channel) {
  const value = String(channel || "latest").trim();
  return value || "latest";
}

export function getZeloImpressaoInstallerUrl(channel = "latest") {
  const normalizedChannel = normalizeReleaseChannel(channel);
  if (normalizedChannel === "latest")
    return ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL;
  return `${ZELO_IMPRESSAO_DOWNLOADS_BASE_URL}/${encodeURIComponent(normalizedChannel)}/${ZELO_IMPRESSAO_INSTALLER_FILENAME}`;
}

export function getZeloImpressaoDownloadPageUrl() {
  return ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL;
}

export function getZeloImpressaoBrowserSdkUrl() {
  return ZELO_IMPRESSAO_BROWSER_SDK_URL;
}

function getStoredToken() {
  try {
    return localStorage.getItem(TOKEN_KEY) || "";
  } catch {
    return "";
  }
}

function setStoredToken(token) {
  try {
    localStorage.setItem(TOKEN_KEY, token);
  } catch {}
}

async function request(path, options = {}) {
  if (path === "/print" || path === "/test-print") {
    const automatic = options.body?.intent?.mode === "automatic";
    let health;
    try { health = await request("/health", { baseUrl: options.baseUrl, timeoutMs: options.timeoutMs ?? TIMEOUT_MS, token: "" }); }
    catch (error) { if (!automatic) throw error; }
    if (automatic && (!health?.capabilities?.canonicalAutoPrint || !health?.capabilities?.persistentPrintDeduplication))
      throw Object.assign(new Error("Abra ou atualize o Zelo Impressão para coordenar a impressão automática entre PDV e Chat."), {
        code: "AUTO_PRINT_COORDINATION_REQUIRED", retrySafe: false,
      });
  }
  const token = options.token ?? getStoredToken();
  const headers = {
    Accept: "application/json",
    ...(options.body ? { "Content-Type": "application/json" } : {}),
    ...(token ? { "X-Zelo-Impressao-Token": token } : {}),
  };

  const body = options.body ? JSON.stringify(options.body) : undefined;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeoutMs ?? TIMEOUT_MS);
  const isPrint = path === "/print" || path === "/test-print";
  let response;
  let data = null;
  try {
    response = await fetch(`${options.baseUrl || DEFAULT_BASE_URL}${path}`, {
      method: options.method || "GET", headers, body, signal: controller.signal,
    });
    try { data = await response.json(); }
    catch (error) { if (response.ok || controller.signal.aborted) throw error; }
    if (response.ok && (!data || typeof data.ok !== "boolean"))
      throw new Error("Invalid response from local printer");
  } catch (error) {
    throw Object.assign(new Error(isPrint ? ZELO_IMPRESSAO_OUTCOME_UNKNOWN_MESSAGE : ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE), {
      code: isPrint ? "PRINT_OUTCOME_UNKNOWN" : "ZELO_IMPRESSAO_UNAVAILABLE",
      retrySafe: !isPrint,
      cause: error,
    });
  } finally {
    clearTimeout(timer);
  }

  if (!response.ok || data?.ok === false) {
    let code =
      data?.code ||
      (response.status === 401 ? "PAIRING_REQUIRED" : "ZELO_IMPRESSAO_ERROR");
    const retrySafe = data?.retrySafe ?? (isPrint
      ? [401, 403, 404, 413, 415].includes(response.status)
      : response.status < 500);
    if (isPrint && !retrySafe) code = "PRINT_OUTCOME_UNKNOWN";
    if (code === "PAIRING_REQUIRED" && options.token === undefined) clearZeloImpressaoPairing();
    const message =
      code === "PRINT_OUTCOME_UNKNOWN" ? ZELO_IMPRESSAO_OUTCOME_UNKNOWN_MESSAGE : code === "PAIRING_REQUIRED"
        ? "Conecte este navegador ao Zelo Impressão usando o código exibido no aplicativo."
        : friendlyMessage(data?.message || response.statusText);
    throw Object.assign(new Error(message), {
      code,
      status: response.status,
      data,
      retrySafe,
    });
  }

  return data;
}

function friendlyMessage(message) {
  const raw = String(message || "");
  if (/offline|unavailable|printer|impressora/i.test(raw)) {
    return ZELO_IMPRESSAO_PRINTER_UNAVAILABLE_MESSAGE;
  }
  if (/fetch|refused|network|failed|abort|localhost/i.test(raw)) {
    return ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE;
  }
  return raw || "Não conseguimos concluir a impressão agora.";
}

export async function detectZeloImpressao(options = {}) {
  options = options || {};
  try {
    const health = await request("/health", { ...options, token: "" });
    const token = options.token ?? getStoredToken();
    let paired = !health.pairingRequired;
    let pairingError;
    let autoConnected = false;
    let autoConnectError;
    if (!paired && token) {
      try { await request("/config", options); paired = true; }
      catch (error) { pairingError = error; }
    }
    // A failed token check is not a reason to mint another credential unless the
    // server explicitly rejected authentication. Explicit caller tokens stay explicit.
    if (!paired && options.token === undefined && options.autoConnect !== false &&
        (!token || pairingError?.code === "PAIRING_REQUIRED")) {
      try {
        const connected = await connectZeloImpressao(options);
        paired = Boolean(connected.token);
        autoConnected = paired;
        if (paired) pairingError = undefined;
      } catch (error) { autoConnectError = error; }
    }
    return {
      installed: true,
      running: true,
      paired,
      autoConnected,
      autoConnectError,
      error: autoConnectError || pairingError,
      health,
    };
  } catch (error) {
    return {
      installed: false,
      running: false,
      paired: false,
      error,
      message: ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE,
    };
  }
}

export async function connectZeloImpressao(options = {}) {
  const response = await request("/connect", { ...options, method: "POST", token: "" });
  if (response.token) setStoredToken(response.token);
  return response;
}

export async function pairZeloImpressao(code, options = {}) {
  const response = await request("/pair", {
    ...options,
    method: "POST",
    token: "",
    body: { code: String(code || "").trim() },
  });
  if (response.token) setStoredToken(response.token);
  return response;
}

export async function getPrinters(options = {}) {
  const response = await request("/printers", options);
  return response.printers || [];
}

export async function getConfig(options = {}) {
  const response = await request("/config", options);
  return response.config;
}

export async function saveConfig(config, options = {}) {
  const response = await request("/config", {
    ...options,
    method: "POST",
    body: config,
  });
  return response.config;
}

export async function sendPrintJob(job, options = {}) {
  const response = await request("/print", {
    ...options,
    method: "POST",
    timeoutMs: options.timeoutMs || 12000,
    body: {
      ...job,
      jobId: job.jobId || globalThis.crypto?.randomUUID?.(),
      timestamp: job.timestamp || new Date().toISOString(),
    },
  });
  return response;
}

export async function sendRawEscposPrintJob(
  {
    jobId,
    source,
    companyStoreId,
    printerId,
    printerName,
    bytes,
    type = "raw_escpos",
    intent,
    metadata,
  },
  options = {},
) {
  const buffer =
    bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes || []);
  let binary = "";
  for (let i = 0; i < buffer.length; i += 1)
    binary += String.fromCharCode(buffer[i]);
  return sendPrintJob(
    {
      jobId,
      source,
      companyStoreId,
      type,
      intent,
      printerId,
      printerName,
      timestamp: new Date().toISOString(),
      content: {
        format: "raw_escpos_base64",
        base64: btoa(binary),
      },
      metadata,
    },
    options,
  );
}

export async function sendTestPrint(printerId, options = {}) {
  return request("/test-print", {
    ...options,
    method: "POST",
    timeoutMs: options.timeoutMs || 10000,
    body: { printerId },
  });
}

export function fallbackToBrowserPrint(html) {
  return new Promise((resolve) => {
    const iframe = document.createElement("iframe");
    iframe.style.cssText =
      "position:fixed;top:-9999px;left:-9999px;width:1px;height:1px;border:none;visibility:hidden;";
    document.body.appendChild(iframe);
    const cleanup = () =>
      setTimeout(() => {
        try {
          document.body.removeChild(iframe);
        } catch {}
        resolve();
      }, 500);
    try {
      iframe.contentWindow.addEventListener("afterprint", cleanup);
    } catch {}
    setTimeout(cleanup, 15000);
    const doc = iframe.contentDocument || iframe.contentWindow.document;
    doc.open();
    doc.write(html);
    doc.close();
    if (!/window\.print\s*\(/i.test(html)) {
      setTimeout(() => {
        try {
          iframe.contentWindow.focus();
          iframe.contentWindow.print();
        } catch {}
      }, 150);
    }
  });
}

export function getZeloImpressaoFriendlyMessage(error) {
  return friendlyMessage(error?.message || error);
}

export function clearZeloImpressaoPairing() {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {}
}
