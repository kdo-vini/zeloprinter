import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, writeFile, copyFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";

test("version sync updates SDK and lockfile from root manifest", async () => {
  const root = await mkdtemp(path.join(tmpdir(), "zelo-version-test-"));
  const files = ["package.json", "package-lock.json", "packages/client/package.json", "scripts/version-manager.mjs", "native/README.md", "native/ZeloImpressao/AppConstants.cs", "native/ZeloImpressao/ZeloImpressao.csproj", "native/installer/ZeloImpressao.iss"];
  for (const file of files) {
    await mkdir(path.dirname(path.join(root, file)), { recursive: true });
    await copyFile(new URL(`../${file}`, import.meta.url), path.join(root, file));
  }
  const rootManifest = JSON.parse(await readFile(path.join(root, "package.json"), "utf8"));
  rootManifest.version = "1.2.3";
  await writeFile(path.join(root, "package.json"), JSON.stringify(rootManifest));
  execFileSync(process.execPath, [path.join(root, "scripts/version-manager.mjs"), "sync"], { cwd: root });
  assert.equal(JSON.parse(await readFile(path.join(root, "packages/client/package.json"), "utf8")).version, "1.2.3");
  const lock = JSON.parse(await readFile(path.join(root, "package-lock.json"), "utf8"));
  assert.equal(lock.version, "1.2.3");
  assert.equal(lock.packages[""].version, "1.2.3");
  execFileSync(process.execPath, [path.join(root, "scripts/version-manager.mjs"), "check-tag", "v1.2.3"], { cwd: root });
  assert.throws(() => execFileSync(process.execPath, [path.join(root, "scripts/version-manager.mjs"), "check-tag", "v1.2.4"], { cwd: root, stdio: "pipe" }),
    error => error.status === 1 && error.stderr.toString().includes("1.2.3"));
});
