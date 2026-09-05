import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
import * as esm from "../packages/client/src/index.js";

const browserSource = await readFile(new URL("../packages/client/src/browser.js", import.meta.url), "utf8");
const key = "zelo_impressao_token_v1";
const job = { source: "zelopdv", type: "receipt", content: { format: "text", text: "Pedido" } };
const response = (data, status = 200) => new Response(JSON.stringify(data), { status });

for (const variant of ["esm", "browser"]) {
  function setup(fetch, storedToken = "") {
    const values = new Map([[key, storedToken]]);
    const storage = { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
    if (variant === "esm") {
      globalThis.fetch = fetch;
      Object.defineProperty(globalThis, "localStorage", { configurable: true, value: storage });
      return { client: esm, values };
    }
    const context = vm.createContext({ fetch, localStorage: storage, AbortController, setTimeout, clearTimeout, crypto, btoa, Uint8Array });
    vm.runInContext(browserSource, context);
    return { client: context.ZeloImpressao.createClient(), values };
  }
  const printOnly = fetch => (url, options) => url.endsWith("/health") ? response({ ok: true }) : fetch(url, options);

  test(`${variant}: stale token is rejected by config while agent remains running`, async () => {
    const calls = [];
    const { client, values } = setup(async (url, options) => {
      calls.push(url);
      if (url.endsWith("/health")) return response({ ok: true, paired: true, pairingRequired: true });
      assert.equal(options.headers["X-Zelo-Impressao-Token"], "old-token");
      return response({ ok: false, code: "PAIRING_REQUIRED" }, 401);
    }, "old-token");
    const status = await client.detectZeloImpressao({ autoConnect: false });
    assert.equal(status.running, true);
    assert.equal(status.paired, false);
    assert.equal(values.has(key), false);
    assert.equal(calls.length, 2);
  });

  test(`${variant}: explicit valid token pairs without stored token`, async () => {
    const { client } = setup(async url => url.endsWith("/health")
      ? response({ ok: true, paired: true, pairingRequired: true })
      : response({ ok: true, config: {} }));
    assert.equal((await client.detectZeloImpressao({ token: "valid" })).paired, true);
  });

  test(`${variant}: trusted automatic connection remains available and stores token`, async () => {
    const calls = [];
    const { client, values } = setup(async (url, options) => {
      calls.push(url.split("/").pop());
      if (url.endsWith("/health")) return response({ ok: true, pairingRequired: true });
      assert.equal(options.method, "POST");
      assert.equal(options.headers["X-Zelo-Impressao-Token"], undefined);
      return response({ ok: true, token: "connected" });
    });
    const status = await client.detectZeloImpressao();
    assert.equal(status.paired, true);
    assert.equal(status.autoConnected, true);
    assert.equal(values.get(key), "connected");
    assert.deepEqual(calls, ["health", "connect"]);
  });

  test(`${variant}: valid stored token is verified without minting another token`, async () => {
    const calls = [];
    const { client } = setup(async url => {
      calls.push(url.split("/").pop());
      return response(url.endsWith("/health") ? { ok: true, pairingRequired: true } : { ok: true, config: {} });
    }, "existing");
    assert.equal((await client.detectZeloImpressao()).paired, true);
    assert.deepEqual(calls, ["health", "config"]);
  });

  test(`${variant}: rejected automatic connection keeps manual pairing available`, async () => {
    const { client } = setup(async url => url.endsWith("/health")
      ? response({ ok: true, pairingRequired: true })
      : response({ ok: false, code: "AUTO_CONNECT_NOT_ALLOWED" }, 403));
    const status = await client.detectZeloImpressao();
    assert.equal(status.running, true);
    assert.equal(status.paired, false);
    assert.equal(status.autoConnectError.code, "AUTO_CONNECT_NOT_ALLOWED");
  });

  test(`${variant}: autoConnect false does not mint a token`, async () => {
    const calls = [];
    const { client } = setup(async url => { calls.push(url); return response({ ok: true, pairingRequired: true }); });
    assert.equal((await client.detectZeloImpressao({ autoConnect: false })).paired, false);
    assert.equal(calls.length, 1);
  });

  test(`${variant}: transient config failure does not mint another token`, async () => {
    const calls = [];
    const { client } = setup(async url => {
      calls.push(url.split("/").pop());
      if (url.endsWith("/health")) return response({ ok: true, pairingRequired: true });
      throw new TypeError("network failure");
    }, "existing");
    const status = await client.detectZeloImpressao();
    assert.equal(status.paired, false);
    assert.deepEqual(calls, ["health", "config"]);
  });

  test(`${variant}: connection failure after print starts has unknown outcome`, async () => {
    const { client } = setup(printOnly(async () => { throw new TypeError("Failed to fetch"); }));
    await assert.rejects(client.sendPrintJob(job), error => error.code === "PRINT_OUTCOME_UNKNOWN" && error.retrySafe === false);
  });

  test(`${variant}: unavailable preflight never sends a print request`, async () => {
    const paths = [];
    const { client } = setup(async url => { paths.push(url); throw new TypeError("Failed to fetch"); });
    await assert.rejects(client.sendPrintJob(job), error => error.code === "ZELO_IMPRESSAO_UNAVAILABLE" && error.retrySafe);
    assert.ok(paths.every(url => url.endsWith("/health")));
  });

  test(`${variant}: malformed successful print response cannot report success`, async () => {
    const { client } = setup(printOnly(async () => new Response("not json", { status: 200 })));
    await assert.rejects(client.sendTestPrint(), error => error.code === "PRINT_OUTCOME_UNKNOWN" && !error.retrySafe);
  });

  test(`${variant}: timeout covers reading the response body`, async () => {
    const { client } = setup(printOnly(async (_, options) => ({ ok: true, json: () => new Promise((resolve, reject) => {
      options.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true });
    }) })));
    await assert.rejects(client.sendPrintJob(job, { timeoutMs: 10 }), error => error.code === "PRINT_OUTCOME_UNKNOWN");
  });

  test(`${variant}: pre-spooler structured refusal remains safe to retry`, async () => {
    const { client } = setup(printOnly(async () => response({ ok: false, code: "PRINTER_UNAVAILABLE", retrySafe: true, message: "Impressora offline" }, 503)));
    await assert.rejects(client.sendPrintJob(job), error => error.code === "PRINTER_UNAVAILABLE" && error.retrySafe);
  });

  test(`${variant}: opaque server failure is not safe to retry`, async () => {
    const { client } = setup(printOnly(async () => new Response("internal error", { status: 500 })));
    await assert.rejects(client.sendPrintJob(job), error => error.code === "PRINT_OUTCOME_UNKNOWN" && !error.retrySafe);
  });

  test(`${variant}: legacy generic 400 may follow partial printing`, async () => {
    const { client } = setup(printOnly(async () => response({ ok: false, message: "Falha ao processar solicitação." }, 400)));
    await assert.rejects(client.sendPrintJob(job), error => error.code === "PRINT_OUTCOME_UNKNOWN" && !error.retrySafe);
  });

  test(`${variant}: raw job preserves caller id and byte payload`, async () => {
    let body;
    const { client } = setup(printOnly(async (_, options) => { body = JSON.parse(options.body); return response({ ok: true }); }));
    await client.sendRawEscposPrintJob({ source: "zelopdv", jobId: "receipt-1", bytes: [0, 27, 255] });
    assert.equal(body.jobId, "receipt-1");
    assert.equal(body.content.base64, "ABv/");
  });

  test(`${variant}: new explicit prints have distinct generated job ids`, async () => {
    const ids = [];
    const { client } = setup(printOnly(async (_, options) => { ids.push(JSON.parse(options.body).jobId); return response({ ok: true }); }));
    await client.sendPrintJob(job);
    await client.sendPrintJob(job);
    assert.ok(ids[0]);
    assert.notEqual(ids[0], ids[1]);
  });

  test(`${variant}: automatic printing refuses an old agent before POST`, async () => {
    const paths = [];
    const { client } = setup(async url => { paths.push(url); return response({ ok: true }); });
    await assert.rejects(client.sendPrintJob({ ...job, intent: { mode: "automatic", orderId: "order-id", purpose: "order_ticket" } }),
      error => error.code === "AUTO_PRINT_COORDINATION_REQUIRED" && error.retrySafe === false);
    assert.deepEqual(paths, ["http://127.0.0.1:17321/health"]);
  });

  test(`${variant}: raw automatic identity reaches native and deduplicated is success`, async () => {
    let body;
    const { client } = setup(async (url, options) => {
      if (url.endsWith("/health")) return response({ ok: true, capabilities: { canonicalAutoPrint: true, persistentPrintDeduplication: true } });
      body = JSON.parse(options.body);
      return response({ ok: true, status: "deduplicated", arbitration: { source: "zelopdv", duplicate: true } });
    });
    const result = await client.sendRawEscposPrintJob({ source: "zelochat", companyStoreId: "owner-id", bytes: [27, 64],
      intent: { mode: "automatic", orderId: "order-id", purpose: "order_ticket" } });
    assert.deepEqual(body.intent, { mode: "automatic", orderId: "order-id", purpose: "order_ticket" });
    assert.equal(result.status, "deduplicated");
  });
}

test("browser: storage denial does not crash client construction", () => {
  const context = vm.createContext({ fetch, AbortController, setTimeout, clearTimeout, crypto, btoa });
  Object.defineProperty(context, "localStorage", { get() { throw new Error("SecurityError"); } });
  vm.runInContext(browserSource, context);
  assert.ok(context.ZeloImpressao.createClient());
});
