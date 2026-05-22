import { cp, mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");
const sourceFile = path.join(
  root,
  "packages",
  "client",
  "src",
  "browser.js",
);
const targetDir = path.join(root, "release", "sdk");
const targetFile = path.join(targetDir, "zelo-impressao-client.browser.js");

async function main() {
  await mkdir(targetDir, { recursive: true });
  await cp(sourceFile, targetFile);

  const packageJson = JSON.parse(
    await readFile(path.join(root, "package.json"), "utf8"),
  );

  await writeFile(
    path.join(targetDir, "manifest.json"),
    `${JSON.stringify(
      {
        name: "zelo-impressao-client-browser",
        version: packageJson.version,
        file: path.basename(targetFile),
      },
      null,
      2,
    )}\n`,
    "utf8",
  );

  console.log(`Prepared browser SDK at ${path.relative(root, targetFile)}`);
}

await main();
