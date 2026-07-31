# Zelo Impressão .NET

Implementação nativa Windows do Zelo Impressão.

## Requisitos para build

Somente o ambiente de build precisa do .NET SDK e do Inno Setup:

- Windows 10/11
- .NET 8 SDK
- Inno Setup 6, para gerar instalador `.exe`

O cliente final não precisa instalar .NET manualmente. O publish é `self-contained`.

## Build install and play

No Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\native\build\Build-Windows.ps1
```

Isso gera:

```text
release\dotnet\win-x64\ZeloImpressao.exe
```

Para gerar instalador com Inno Setup:

```powershell
iscc .\native\installer\ZeloImpressao.iss
```

Saída:

```text
release\installer\Zelo-Impressao-0.1.4-Setup.exe
```

## Versionamento

A versão de release do app agora usa `package.json` como fonte de verdade e é sincronizada automaticamente com:

- `native/ZeloImpressao/AppConstants.cs`
- `native/ZeloImpressao/ZeloImpressao.csproj`
- `native/installer/ZeloImpressao.iss`
- `packages/client/package.json`
- `package-lock.json`

Comandos úteis:

```bash
npm run version:sync
npm run version:set -- 0.1.1
npm run version:bump:patch
npm run version:bump:minor
npm run version:bump:major
```

Regras práticas:

- `patch`: correções e ajustes pequenos
- `minor`: funcionalidades novas compatíveis
- `major`: quebra de compatibilidade ou mudança estrutural grande

Os scripts de build e publish já rodam `version:sync` automaticamente antes de gerar artefatos.

## Distribuição pública

A estratégia oficial de distribuição, links de download, versionamento e consumo pelos apps está documentada em:

- `docs/distribuicao-zelo-impressao.md`

Em resumo:

- o instalador deve ser publicado uma única vez no storage/CDN central
- `zelopdv` e `zelochat` devem consumir o link via `@zelo/impressao-client`
- a URL estável esperada é `https://zelopdv.com.br/downloads/zelo-impressao/latest/Zelo-Impressao-Setup.exe`
- a página pública esperada é `https://zelopdv.com.br/zelo-impressao`

## Critérios antes de publicar

- Instalar em Windows limpo.
- Confirmar que o app abre no tray sem janela obrigatória.
- Confirmar inicialização com Windows.
- Confirmar `/health`, `/connect`, `/pair`, `/printers`, `/config`, `/test-print`.
- Confirmar auto-connect do PDV e do ZeloChat em origens oficiais.
- Confirmar pareamento por código para uma origem de terceiro.
- Confirmar que PDV e ZeloChat permanecem conectados simultaneamente.
- Testar impressora térmica USB instalada no Windows.
- Testar impressora de rede.
- Testar erro com impressora desligada.
- Confirmar fallback dos apps pelo navegador quando o app local está fechado.
