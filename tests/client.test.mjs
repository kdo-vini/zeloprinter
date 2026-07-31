import assert from "node:assert/strict";
import { afterEach, test } from "node:test";

import {
  clearZeloImpressaoPairing,
  detectZeloImpressao,
} from "../packages/client/src/index.js";

const originalLocalStorage = globalThis.localStorage;

function installBrowserMocks(responses) {
  const values = new Map();
  const calls = [];

  globalThis.localStorage = {
    getItem(key) {
      return values.get(key) ?? null;
    },
    setItem(key, value) {
      values.set(key, String(value));
    },
    removeItem(key) {
      values.delete(key);
    },
  };

  globalThis.fetch = async (url, options) => {
    calls.push({ url, options });
    const response = responses.shift();
    if (!response) throw new Error("Unexpected fetch call");
    return {
      ok: response.ok !== false,
      status: response.status || 200,
      statusText: response.statusText || "OK",
      json: async () => response.body,
    };
  };

  return { calls, values };
}

afterEach(() => {
  clearZeloImpressaoPairing();
  if (originalLocalStorage === undefined) {
    delete globalThis.localStorage;
  } else {
    globalThis.localStorage = originalLocalStorage;
  }
});

test("detect auto-connects a first-party browser by default", async () => {
  const { calls, values } = installBrowserMocks([
    { body: { ok: true, pairingRequired: true, paired: false } },
    { body: { ok: true, token: "first-party-token" } },
  ]);

  const status = await detectZeloImpressao({ baseUrl: "http://agent.test" });

  assert.equal(status.installed, true);
  assert.equal(status.running, true);
  assert.equal(status.paired, true);
  assert.equal(status.autoConnected, true);
  assert.equal(values.get("zelo_impressao_token_v1"), "first-party-token");
  assert.equal(calls[1].url, "http://agent.test/connect");
  assert.equal(calls[1].options.method, "POST");
  assert.equal(calls[1].options.headers["X-Zelo-Impressao-Token"], undefined);
});

test("autoConnect false preserves the manual pairing flow", async () => {
  const { calls } = installBrowserMocks([
    { body: { ok: true, pairingRequired: true, paired: false } },
  ]);

  const status = await detectZeloImpressao({
    baseUrl: "http://agent.test",
    autoConnect: false,
  });

  assert.equal(status.installed, true);
  assert.equal(status.running, true);
  assert.equal(status.paired, false);
  assert.equal(status.autoConnected, false);
  assert.equal(calls.length, 1);
});

test("an existing browser token avoids minting another session", async () => {
  const { calls, values } = installBrowserMocks([
    { body: { ok: true, pairingRequired: true, paired: true } },
  ]);
  values.set("zelo_impressao_token_v1", "existing-token");

  const status = await detectZeloImpressao({ baseUrl: "http://agent.test" });

  assert.equal(status.paired, true);
  assert.equal(status.autoConnected, false);
  assert.equal(calls.length, 1);
});

test("a rejected auto-connect keeps the agent detected and exposes the fallback error", async () => {
  installBrowserMocks([
    { body: { ok: true, pairingRequired: true, paired: false } },
    {
      ok: false,
      status: 403,
      statusText: "Forbidden",
      body: {
        ok: false,
        code: "AUTO_CONNECT_NOT_ALLOWED",
        message: "Use o código de pareamento.",
      },
    },
  ]);

  const status = await detectZeloImpressao({ baseUrl: "http://agent.test" });

  assert.equal(status.installed, true);
  assert.equal(status.running, true);
  assert.equal(status.paired, false);
  assert.equal(status.autoConnected, false);
  assert.equal(status.autoConnectError.code, "AUTO_CONNECT_NOT_ALLOWED");
});
