# Prompt para IA Coder - ZeloChat

Você está no projeto ZeloChat, que é React/Vite. Migre a impressão para usar o componente local **Zelo Impressão** como caminho principal, mantendo fallback pelo navegador para ações manuais.

Contexto:

- O componente local expõe API em `http://127.0.0.1:17321`.
- Existe SDK compartilhado em `../zeloprinter/packages/client` com o pacote `@zelo/impressao-client`.
- WebUSB não deve ser tratado como caminho confiável principal.
- Pedido novo não pode deixar de entrar no sistema se a impressão falhar.

Tarefas:

1. Adicione dependência:

```json
"@zelo/impressao-client": "file:../zeloprinter/packages/client"
```

2. Atualize `src/services/printerService.ts`:

- remover dependência primária de WebUSB;
- usar `detectZeloImpressao`, `connectZeloImpressao`, `sendPrintJob`, `sendTestPrint`, `pairZeloImpressao` e `fallbackToBrowserPrint`;
- manter builders simples de texto para pedido e relatório do dia;
- enviar pedido com:
  - `source: "zelochat"`;
  - `type: "kitchen_order"`;
  - `content.format: "text"`;
- para relatório manual do dia, tentar Zelo Impressão e cair para `fallbackToBrowserPrint` se falhar.

3. Atualize `src/hooks/usePrinter.ts`:

- preservar a API pública esperada pelos componentes (`supported`, `connected`, `printing`, `deviceName`, `error`, `connect`, `disconnect`, `print`, `printDay`);
- adicionar `pair(code)` se necessário para UI;
- `connect()` deve detectar status do Zelo Impressão e aproveitar o auto-connect padrão;
- só mostrar pareamento por código quando o auto-connect não for permitido ou falhar;
- `print(order)` deve lançar erro amigável, mas o caller não deve bloquear pedido.

4. Atualize `src/components/PrinterButton.tsx`:

- trocar texto de "Conectar impressora USB" para "Impressão automática";
- mostrar status conectado/desconectado;
- quando exigir pareamento, mostrar campo curto para código do Zelo Impressão;
- botão de teste deve usar a função do hook/serviço.

5. UX:

- erro técnico de rede/localhost vira: "O Zelo Impressão não está aberto neste computador. Abra o aplicativo ou use a impressão pelo navegador.";
- erro de impressora vira: "Não conseguimos acessar a impressora selecionada. Verifique se ela está ligada e conectada.";
- auto-print falhando deve mostrar aviso ao operador, sem bloquear criação/atualização do pedido.

6. Verificação:

- rodar `npm install`;
- rodar `npm run lint`;
- rodar `npm run build`;
- confirmar que `AppShell` e `CalendarView` continuam usando o hook sem grandes mudanças.

Resultado esperado:

- ZeloChat usa Zelo Impressão para impressão automática de pedidos;
- relatório manual mantém fallback pelo navegador;
- pedido nunca é perdido ou bloqueado por falha de impressão.
