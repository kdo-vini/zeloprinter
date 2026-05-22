(function (globalScope) {
  const DEFAULT_BASE_URL = "http://127.0.0.1:17321";
  const TOKEN_KEY = "zelo_impressao_token_v1";
  const TIMEOUT_MS = 1800;

  const ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL =
    "https://zelopdv.com.br/zelo-impressao";
  const ZELO_IMPRESSAO_DOWNLOADS_BASE_URL =
    "https://zelopdv.com.br/downloads/zelo-impressao";
  const ZELO_IMPRESSAO_INSTALLER_FILENAME = "Zelo-Impressao-Setup.exe";
  const ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL =
    ZELO_IMPRESSAO_DOWNLOADS_BASE_URL +
    "/latest/" +
    ZELO_IMPRESSAO_INSTALLER_FILENAME;
  const ZELO_IMPRESSAO_BROWSER_SDK_URL =
    ZELO_IMPRESSAO_DOWNLOADS_BASE_URL +
    "/sdk/zelo-impressao-client.browser.js";
  const ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE =
    "O Zelo Impressão não está aberto neste computador. Abra o aplicativo ou use a impressão pelo navegador.";
  const ZELO_IMPRESSAO_PRINTER_UNAVAILABLE_MESSAGE =
    "Não conseguimos acessar a impressora selecionada. Verifique se ela está ligada e conectada.";

  function normalizeReleaseChannel(channel) {
    const value = String(channel || "latest").trim();
    return value || "latest";
  }

  function getZeloImpressaoInstallerUrl(channel) {
    const normalizedChannel = normalizeReleaseChannel(channel || "latest");
    if (normalizedChannel === "latest") {
      return ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL;
    }
    return (
      ZELO_IMPRESSAO_DOWNLOADS_BASE_URL +
      "/" +
      encodeURIComponent(normalizedChannel) +
      "/" +
      ZELO_IMPRESSAO_INSTALLER_FILENAME
    );
  }

  function getZeloImpressaoDownloadPageUrl() {
    return ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL;
  }

  function getZeloImpressaoBrowserSdkUrl() {
    return ZELO_IMPRESSAO_BROWSER_SDK_URL;
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

  function createClient(options) {
    const config = options || {};
    const baseUrl = config.baseUrl || DEFAULT_BASE_URL;
    const storage = config.storage || globalScope.localStorage;
    const fetchImpl = config.fetch || globalScope.fetch.bind(globalScope);
    const documentRef = config.document || globalScope.document;
    const btoaImpl = config.btoa || globalScope.btoa.bind(globalScope);

    function getStoredToken() {
      try {
        return storage.getItem(TOKEN_KEY) || "";
      } catch {
        return "";
      }
    }

    function setStoredToken(token) {
      try {
        storage.setItem(TOKEN_KEY, token);
      } catch {}
    }

    function clearZeloImpressaoPairing() {
      try {
        storage.removeItem(TOKEN_KEY);
      } catch {}
    }

    function withTimeout(promiseFactory, timeoutMs) {
      const controller = new AbortController();
      const timeout = setTimeout(function () {
        controller.abort();
      }, timeoutMs || TIMEOUT_MS);
      return promiseFactory(controller.signal).finally(function () {
        clearTimeout(timeout);
      });
    }

    async function request(path, requestOptions) {
      const options = requestOptions || {};
      const token =
        Object.prototype.hasOwnProperty.call(options, "token")
          ? options.token
          : getStoredToken();
      const headers = {
        Accept: "application/json",
        ...(options.body ? { "Content-Type": "application/json" } : {}),
        ...(token ? { "X-Zelo-Impressao-Token": token } : {}),
      };

      let response;
      try {
        response = await withTimeout(function (signal) {
          return fetchImpl(baseUrl + path, {
            method: options.method || "GET",
            headers,
            body: options.body ? JSON.stringify(options.body) : undefined,
            signal,
          });
        }, options.timeoutMs);
      } catch (error) {
        throw Object.assign(new Error(ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE), {
          code: "ZELO_IMPRESSAO_UNAVAILABLE",
          cause: error,
        });
      }

      let data = null;
      try {
        data = await response.json();
      } catch {}

      if (!response.ok || (data && data.ok === false)) {
        const code =
          (data && data.code) ||
          (response.status === 401
            ? "PAIRING_REQUIRED"
            : "ZELO_IMPRESSAO_ERROR");
        const message =
          code === "PAIRING_REQUIRED"
            ? "Conecte este navegador ao Zelo Impressão usando o código exibido no aplicativo."
            : friendlyMessage((data && data.message) || response.statusText);
        throw Object.assign(new Error(message), {
          code,
          status: response.status,
          data,
        });
      }

      return data;
    }

    async function detectZeloImpressao(requestOptions) {
      try {
        const health = await request("/health", {
          ...(requestOptions || {}),
          token: "",
        });
        const hasToken = !!getStoredToken();
        return {
          installed: true,
          running: true,
          paired: !health.pairingRequired || (!!health.paired && hasToken),
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

    async function pairZeloImpressao(code, requestOptions) {
      const response = await request("/pair", {
        ...(requestOptions || {}),
        method: "POST",
        token: "",
        body: { code: String(code || "").trim() },
      });
      if (response.token) {
        setStoredToken(response.token);
      }
      return response;
    }

    async function getPrinters(requestOptions) {
      const response = await request("/printers", requestOptions);
      return response.printers || [];
    }

    async function getConfig(requestOptions) {
      const response = await request("/config", requestOptions);
      return response.config;
    }

    async function saveConfig(nextConfig, requestOptions) {
      const response = await request("/config", {
        ...(requestOptions || {}),
        method: "POST",
        body: nextConfig,
      });
      return response.config;
    }

    async function sendPrintJob(job, requestOptions) {
      return request("/print", {
        ...(requestOptions || {}),
        method: "POST",
        timeoutMs: (requestOptions && requestOptions.timeoutMs) || 12000,
        body: {
          ...job,
          timestamp: job.timestamp || new Date().toISOString(),
        },
      });
    }

    async function sendRawEscposPrintJob(job, requestOptions) {
      const sourceJob = job || {};
      const inputBytes = sourceJob.bytes || [];
      const buffer =
        inputBytes instanceof Uint8Array ? inputBytes : new Uint8Array(inputBytes);
      let binary = "";
      for (let index = 0; index < buffer.length; index += 1) {
        binary += String.fromCharCode(buffer[index]);
      }

      return sendPrintJob(
        {
          source: sourceJob.source,
          companyStoreId: sourceJob.companyStoreId,
          type: sourceJob.type || "raw_escpos",
          printerId: sourceJob.printerId,
          printerName: sourceJob.printerName,
          timestamp: new Date().toISOString(),
          content: {
            format: "raw_escpos_base64",
            base64: btoaImpl(binary),
          },
          metadata: sourceJob.metadata,
        },
        requestOptions,
      );
    }

    async function sendTestPrint(printerId, requestOptions) {
      return request("/test-print", {
        ...(requestOptions || {}),
        method: "POST",
        timeoutMs: (requestOptions && requestOptions.timeoutMs) || 10000,
        body: { printerId },
      });
    }

    function fallbackToBrowserPrint(html) {
      return new Promise(function (resolve) {
        const iframe = documentRef.createElement("iframe");
        iframe.style.cssText =
          "position:fixed;top:-9999px;left:-9999px;width:1px;height:1px;border:none;visibility:hidden;";
        documentRef.body.appendChild(iframe);
        const cleanup = function () {
          setTimeout(function () {
            try {
              documentRef.body.removeChild(iframe);
            } catch {}
            resolve();
          }, 500);
        };
        try {
          iframe.contentWindow.addEventListener("afterprint", cleanup);
        } catch {}
        setTimeout(cleanup, 15000);
        const doc = iframe.contentDocument || iframe.contentWindow.document;
        doc.open();
        doc.write(html);
        doc.close();
        if (!/window\.print\s*\(/i.test(html)) {
          setTimeout(function () {
            try {
              iframe.contentWindow.focus();
              iframe.contentWindow.print();
            } catch {}
          }, 150);
        }
      });
    }

    return {
      baseUrl,
      detectZeloImpressao,
      pairZeloImpressao,
      getPrinters,
      getConfig,
      saveConfig,
      sendPrintJob,
      sendRawEscposPrintJob,
      sendTestPrint,
      fallbackToBrowserPrint,
      getZeloImpressaoFriendlyMessage(error) {
        return friendlyMessage(error && error.message ? error.message : error);
      },
      clearZeloImpressaoPairing,
    };
  }

  globalScope.ZeloImpressao = {
    createClient,
    constants: {
      ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL,
      ZELO_IMPRESSAO_DOWNLOADS_BASE_URL,
      ZELO_IMPRESSAO_INSTALLER_FILENAME,
      ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL,
      ZELO_IMPRESSAO_BROWSER_SDK_URL,
      ZELO_IMPRESSAO_UNAVAILABLE_MESSAGE,
      ZELO_IMPRESSAO_PRINTER_UNAVAILABLE_MESSAGE,
    },
    getZeloImpressaoInstallerUrl,
    getZeloImpressaoDownloadPageUrl,
    getZeloImpressaoBrowserSdkUrl,
  };
})(typeof window !== "undefined" ? window : globalThis);
