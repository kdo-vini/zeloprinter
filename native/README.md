# Zelo Impressão .NET

Implementação nativa Windows do Zelo Impressão.

## Requisitos para build

Somente o ambiente de build precisa do .NET SDK e do Inno Setup:

- Windows 10/11
- .NET 8 SDK
- Inno Setup 6.3 ou posterior, para gerar instalador `.exe`

O cliente final não precisa instalar .NET manualmente. O publish é `self-contained`.

## Validação automatizada

`npm test` verifica os SDKs e o harness nativo com Kestrel real, configuração temporária e porta efêmera. Não imprime papel nem modifica startup do usuário. `npm run build` compila Release.

A versão 0.2.0 arbitra pedidos automáticos entre PDV e Chat e persiste hashes/resultados por sete dias. Veja o [contrato canônico](../docs/integracao-plug-and-play.md). A preferência e a capacidade do histórico podem ser ajustadas na janela existente; segunda via explícita permanece separada da deduplicação automática.

O harness permite roll-forward em máquina com runtime mais novo. Para testar o runtime exato, execute `dotnet publish native/ZeloImpressao.Tests -c Release -r win-x64 --self-contained true -o release/tests` e `release/tests/ZeloImpressao.Tests.exe`.

Evidências e limites de homologação: [auditoria de 2026-09-04](../docs/audits/2026-09-04-zeloprinter.md).

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
release\installer\Zelo-Impressao-0.2.0-Setup.exe
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

- o instalador canônico é publicado uma única vez no GitHub Releases de `kdo-vini/zeloprinter`; a URL pública estável encaminha para a release
- `zelopdv` e `zelochat` devem consumir o link via `@zelo/impressao-client`
- a URL estável esperada é `https://zelopdv.com.br/downloads/zelo-impressao/latest/Zelo-Impressao-Setup.exe`
- a página pública esperada é `https://zelopdv.com.br/zelo-impressao`

## Critérios antes de publicar

- Instalar em Windows limpo.
- Confirmar que o app abre no tray sem janela obrigatória.
- Confirmar inicialização com Windows.
- Confirmar `/health`, `/printers`, `/config`, `/test-print`.
- Conectar PDV e Chat automaticamente e por código; revogar localmente e confirmar que autoConnect permanece bloqueado após reinício.
- Testar impressora térmica USB instalada no Windows.
- Testar impressora de rede.
- Testar erro com impressora desligada.
- Confirmar que app fechado permite fallback manual; impressão automática exige coordenação nativa e não abre fallback concorrente.
