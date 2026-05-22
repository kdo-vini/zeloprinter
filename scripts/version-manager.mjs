import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");

const packageJsonPath = path.join(root, "package.json");
const packageLockPath = path.join(root, "package-lock.json");
const clientPackageJsonPath = path.join(
  root,
  "packages",
  "client",
  "package.json",
);
const csConstantsPath = path.join(
  root,
  "native",
  "ZeloImpressao",
  "AppConstants.cs",
);
const csprojPath = path.join(
  root,
  "native",
  "ZeloImpressao",
  "ZeloImpressao.csproj",
);
const installerPath = path.join(
  root,
  "native",
  "installer",
  "ZeloImpressao.iss",
);
const nativeReadmePath = path.join(root, "native", "README.md");

const VALID_VERSION = /^\d+\.\d+\.\d+$/;

function fail(message) {
  console.error(`ERROR: ${message}`);
  process.exit(1);
}

function assertVersion(version) {
  if (!VALID_VERSION.test(version)) {
    fail(`Versao invalida: "${version}". Use o formato semver simples x.y.z.`);
  }
}

function bumpVersion(version, bumpType) {
  assertVersion(version);
  const [major, minor, patch] = version.split(".").map(Number);

  if (bumpType === "major") return `${major + 1}.0.0`;
  if (bumpType === "minor") return `${major}.${minor + 1}.0`;
  if (bumpType === "patch") return `${major}.${minor}.${patch + 1}`;

  fail(`Tipo de bump invalido: "${bumpType}". Use major, minor ou patch.`);
}

function toAssemblyVersion(version) {
  return `${version}.0`;
}

async function readJson(filePath) {
  return JSON.parse(await readFile(filePath, "utf8"));
}

async function writeJson(filePath, data) {
  await writeFile(filePath, `${JSON.stringify(data, null, 2)}\n`, "utf8");
}

async function replaceInFile(filePath, matcher, replacer) {
  const current = await readFile(filePath, "utf8");

  if (!matcher.test(current)) {
    fail(`Nao encontrei o padrao esperado em ${path.relative(root, filePath)}.`);
  }

  const next = current.replace(matcher, replacer);
  await writeFile(filePath, next, "utf8");
}

async function syncVersionFiles(version) {
  assertVersion(version);
  const assemblyVersion = toAssemblyVersion(version);

  await replaceInFile(
    csConstantsPath,
    /public const string Version = ".*";/,
    `public const string Version = "${version}";`,
  );
  await replaceInFile(
    installerPath,
    /#define MyAppVersion ".*"/,
    `#define MyAppVersion "${version}"`,
  );

  const csprojRaw = await readFile(csprojPath, "utf8");
  if (!/<Version>.*<\/Version>/.test(csprojRaw)) {
    fail("Nao encontrei <Version> em native/ZeloImpressao/ZeloImpressao.csproj.");
  }

  let csprojNext = csprojRaw.replace(
    /<Version>.*<\/Version>/,
    `<Version>${version}</Version>`,
  );

  if (/<AssemblyVersion>.*<\/AssemblyVersion>/.test(csprojNext)) {
    csprojNext = csprojNext.replace(
      /<AssemblyVersion>.*<\/AssemblyVersion>/,
      `<AssemblyVersion>${assemblyVersion}</AssemblyVersion>`,
    );
  } else {
    csprojNext = csprojNext.replace(
      `    <Version>${version}</Version>`,
      `    <Version>${version}</Version>\n    <AssemblyVersion>${assemblyVersion}</AssemblyVersion>`,
    );
  }

  if (/<FileVersion>.*<\/FileVersion>/.test(csprojNext)) {
    csprojNext = csprojNext.replace(
      /<FileVersion>.*<\/FileVersion>/,
      `<FileVersion>${assemblyVersion}</FileVersion>`,
    );
  } else {
    csprojNext = csprojNext.replace(
      `    <AssemblyVersion>${assemblyVersion}</AssemblyVersion>`,
      `    <AssemblyVersion>${assemblyVersion}</AssemblyVersion>\n    <FileVersion>${assemblyVersion}</FileVersion>`,
    );
  }

  await writeFile(csprojPath, csprojNext, "utf8");

  await replaceInFile(
    nativeReadmePath,
    /release\\installer\\Zelo-Impressao-\d+\.\d+\.\d+-Setup\.exe/,
    `release\\installer\\Zelo-Impressao-${version}-Setup.exe`,
  );
}

async function setVersion(version) {
  assertVersion(version);

  const packageJson = await readJson(packageJsonPath);
  packageJson.version = version;
  await writeJson(packageJsonPath, packageJson);

  const packageLock = await readJson(packageLockPath);
  packageLock.version = version;
  if (packageLock.packages?.[""]) packageLock.packages[""].version = version;
  await writeJson(packageLockPath, packageLock);

  const clientPackageJson = await readJson(clientPackageJsonPath);
  clientPackageJson.version = version;
  await writeJson(clientPackageJsonPath, clientPackageJson);

  await syncVersionFiles(version);
  console.log(`Version synchronized to ${version}`);
}

async function main() {
  const command = process.argv[2] || "sync";
  const packageJson = await readJson(packageJsonPath);
  const currentVersion = packageJson.version;

  if (command === "sync") {
    await syncVersionFiles(currentVersion);
    console.log(`Files synchronized to version ${currentVersion}`);
    return;
  }

  if (command === "set") {
    const version = process.argv[3];
    if (!version) fail("Informe a versao. Exemplo: npm run version:set -- 0.1.1");
    await setVersion(version);
    return;
  }

  if (command === "bump") {
    const bumpType = process.argv[3] || "patch";
    const nextVersion = bumpVersion(currentVersion, bumpType);
    await setVersion(nextVersion);
    return;
  }

  fail(`Comando invalido: "${command}". Use sync, set ou bump.`);
}

await main();
