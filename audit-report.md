# Zelo Impressão - Audit Report

## Auditoria inicial

Zelo PDV já tinha impressão centralizada em `src/lib/printService.js`, com ESC/POS via WebUSB em `src/lib/printer.js` e fallback HTML em `src/lib/receipt.js`.

ZeloChat tinha lógica própria e separada em `src/services/printerService.ts` e `src/hooks/usePrinter.ts`, também baseada em WebUSB. Isso duplicava builders, controle de conexão, retry e mensagens de erro entre os apps.

Fallback existente identificado:

- PDV: iframe HTML com `window.print()`.
- ZeloChat: aviso ao operador quando auto-print falhava; relatório manual não tinha fallback local robusto.

## Arquitetura implementada

Criada implementação .NET nativa em `/home/vinicius/code/zeloprinter/native/ZeloImpressao`:

- WinForms app com tray e janela simples de configurações.
- Instância única para evitar múltiplas APIs/ícones no tray.
- Inicialização com Windows via registro `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- API localhost em `127.0.0.1:17321`.
- `GET /health` expõe memória RSS/heap para medir consumo em idle.
- Listagem de impressoras Windows via WMI `Win32_Printer`.
- Impressão RAW/ESC/POS via `winspool.drv`.
- Impressão por driver do Windows via `PrintDocument`.
- Config local em `%APPDATA%\Zelo Impressao\config.json`.
- Logs em pasta local do app.
- Conexão automática para origens oficiais, pareamento por código para terceiros e tokens locais independentes.

A implementação Electron anterior permanece no repositório como protótipo/legado do contrato, mas o caminho recomendado de produção é a versão .NET.

Criado SDK compartilhado:

- `/home/vinicius/code/zeloprinter/packages/client`
- Publicado localmente nos apps como `@zelo/impressao-client`.
- Funções: `detectZeloImpressao`, `connectZeloImpressao`, `getPrinters`, `sendPrintJob`, `sendRawEscposPrintJob`, `sendTestPrint`, `fallbackToBrowserPrint`, `pairZeloImpressao`.

## Arquivos afetados

Zelo Impressão:

- `package.json`
- `tsconfig.json`
- `src/main.ts`
- `src/preload.ts`
- `src/httpServer.ts`
- `src/printing.ts`
- `src/printers.ts`
- `src/powershellPrint.ts`
- `src/config.ts`
- `src/pairing.ts`
- `src/validation.ts`
- `src/escpos.ts`
- `src/logger.ts`
- `src/renderer/settings.html`
- `packages/client/package.json`
- `packages/client/src/index.js`
- `packages/client/src/index.d.ts`

Zelo PDV:

- `package.json`
- `package-lock.json`
- `src/lib/printService.js`
- `src/routes/perfil/+page.svelte`

ZeloChat:

- `package.json`
- `package-lock.json`
- `src/services/printerService.ts`
- `src/hooks/usePrinter.ts`
- `src/components/PrinterButton.tsx`

## Integração

PDV mantém os mesmos call sites de impressão. `printService.js` agora tenta Zelo Impressão antes do fallback pelo navegador.

ZeloChat mantém `usePrinter()` para `AppShell`, `CalendarView` e `PrinterButton`, mas o backend de impressão virou API local. O botão existente tenta conexão automática e mantém pareamento por código como fallback.

## Limitações

- O projeto .NET foi escrito neste ambiente Linux, mas `dotnet` não está instalado aqui e WinForms exige validação em Windows.
- Impressão RAW/ESC/POS depende do spooler Windows aceitar jobs RAW para a impressora selecionada.
- Status de impressora no Windows nem sempre reflete falta de papel ou tampa aberta; alguns drivers só informam offline.
- O instalador ainda precisa de assinatura de código e publicação.

## Plano de teste

Executado:

- `zeloprinter` Electron legado: `npm run build`
- `zelopdv`: `npm run build`
- `zelochat`: `npm run lint`
- `zelochat`: `npm run build`

Pendente no Windows:

- `powershell -ExecutionPolicy Bypass -File .\native\build\Build-Windows.ps1`
- `iscc .\native\installer\ZeloImpressao.iss`

Teste manual recomendado no Windows:

1. Instalar Zelo Impressão.
2. Confirmar que abre no tray e inicia com Windows.
3. Medir RAM em idle com a janela fechada e comparar com `/health.memory.rssMb`.
4. Abrir configurações, selecionar impressora instalada e enviar teste.
5. Abrir Zelo PDV, confirmar auto-connect, selecionar impressora e imprimir teste.
6. Registrar venda no PDV e confirmar impressão silenciosa.
7. Desligar Zelo Impressão e confirmar fallback do PDV pelo navegador.
8. Abrir ZeloChat, confirmar auto-connect simultâneo e criar pedido de teste.
9. Confirmar que falha de impressão automática mostra aviso sem bloquear pedido.
10. Testar impressora USB térmica com driver Windows e, se aplicável, modo RAW.

## Recomendações de deployment

- Gerar publish self-contained com `native\build\Build-Windows.ps1`.
- Gerar instalador Windows com Inno Setup via `native\installer\ZeloImpressao.iss`.
- Assinar executável e instalador para reduzir bloqueios do SmartScreen.
- O instalador deve ser install and play: runtime .NET embutido, sem pré-requisito manual para o cliente.
- Publicar link de download nos cards de "Impressão automática" dos apps.
- Versionar o contrato da API se houver breaking changes.
- Manter WebUSB apenas como recurso legado, sem tratá-lo como caminho confiável principal.
