# Plano de Migração para .NET

## Decisão

Migrar o app local Zelo Impressão de Electron para .NET nativo antes do rollout para clientes.

Motivo: o produto roda em Windows de balcão, precisa ficar no tray, iniciar com o Windows, consumir pouca RAM, falar com impressoras instaladas e ter instalador simples. .NET/WinForms atende melhor esse cenário que Electron.

## Estratégia

Preservar o que já está certo:

- contrato HTTP local;
- SDK `@zelo/impressao-client`;
- integração PDV/ZeloChat;
- pareamento por código/token;
- fallback dos apps pelo navegador;
- documentação de API e segurança.

Trocar a implementação local:

- de Electron + Node + Chromium;
- para .NET 8 WinForms + ASP.NET Core self-hosted + Winspool/PrintDocument.

## Fases

1. Criar app .NET nativo em `native/ZeloImpressao`.
2. Reimplementar API local compatível:
   - `GET /health`
   - `GET /printers`
   - `POST /print`
   - `POST /test-print`
   - `GET /config`
   - `POST /config`
   - `POST /pair`
3. Reimplementar impressão:
   - RAW/ESC/POS via `winspool.drv`;
   - fallback por driver Windows via `PrintDocument`;
   - teste de impressão ESC/POS.
4. Reimplementar UX local:
   - tray icon;
   - janela de configurações simples;
   - impressora selecionada;
   - iniciar com Windows;
   - código de pareamento;
   - botão de logs;
   - reiniciar API local.
5. Empacotar como install and play:
   - `dotnet publish --self-contained true`;
   - runtime .NET embutido;
   - instalador `.exe`;
   - app abre após instalação;
   - cliente não instala pré-requisito manualmente.
6. Testar em Windows real:
   - PC limpo;
   - impressora térmica USB;
   - impressora de rede;
   - PDV;
   - ZeloChat;
   - fallback com app local fechado.

## Status atual

Implementado no código:

- projeto .NET WinForms;
- API local ASP.NET Core em `127.0.0.1:17321`;
- pareamento/token;
- listagem de impressoras Windows;
- RAW ESC/POS via Winspool;
- driver fallback via `PrintDocument`;
- tray/settings;
- startup com Windows;
- logs;
- script de publish self-contained;
- template Inno Setup.

Pendente porque este ambiente está no Linux sem `dotnet`:

- compilar no Windows;
- gerar instalador;
- testar com spooler real;
- testar com impressora térmica real.

## Critério de pronto para publicar

Não publicar até passar em Windows:

- instala sem pré-requisitos manuais;
- inicia com Windows;
- fica no tray;
- `/health` responde;
- PDV pareia e imprime venda;
- ZeloChat pareia e imprime pedido;
- test print funciona;
- impressora desligada gera erro amigável;
- app fechado aciona fallback dos apps;
- consumo idle aceitável em PC antigo de balcão.
