param(
  [string]$Runtime = "win-x64",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $root "native\ZeloImpressao\ZeloImpressao.csproj"
$publishDir = Join-Path $root "release\dotnet\$Runtime"

Write-Host "Publishing Zelo Impressão ($Runtime, self-contained)..." -ForegroundColor Cyan

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore falhou com código $LASTEXITCODE" }
dotnet publish $project `
  -c $Configuration `
  -r $Runtime `
  --self-contained true `
  -o $publishDir `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  /p:DebugType=None `
  /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou com código $LASTEXITCODE" }

Write-Host "Publish pronto em: $publishDir" -ForegroundColor Green
Write-Host "O executável é self-contained: o cliente não precisa instalar .NET manualmente." -ForegroundColor Green
