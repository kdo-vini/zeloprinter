# Prompt para IA Coder - Zelo PDV

Você está no projeto Zelo PDV, que é SvelteKit. Migre a impressão para usar o componente local **Zelo Impressão** como caminho principal, mantendo impressão pelo navegador como fallback.

Contexto:

- O componente local expõe API em `http://127.0.0.1:17321`.
- Existe SDK compartilhado em `../zeloprinter/packages/client` com o pacote `@zelo/impressao-client`.
- WebUSB não deve ser tratado como caminho confiável principal.
- O fluxo de venda não pode quebrar se o componente local estiver offline.

Tarefas:

1. Adicione dependência:

```json
"@zelo/impressao-client": "file:../zeloprinter/packages/client"
```

2. Atualize `src/lib/printService.js`:

- manter as funções públicas existentes (`printVenda`, `printMovCaixa`, `printPagamentoFiado`, `printTeste`);
- antes do fallback atual, tentar enviar ESC/POS para o Zelo Impressão usando `sendRawEscposPrintJob`;
- usar `source: "zelopdv"`;
- usar `type: "receipt"` para recibos e movimentações;
- preservar fallback atual por iframe/browser print;
- traduzir erros com `getZeloImpressaoFriendlyMessage`;
- não remover o WebUSB legado se ele já existir, mas não usá-lo como prioridade.

3. Atualize a tela de integrações/perfil:

- adicionar card "Impressão automática";
- mostrar status: conectado, não instalado ou desconectado;
- permitir pareamento com código via `pairZeloImpressao(code)`;
- listar impressoras com `getPrinters()`;
- salvar impressora selecionada com `saveConfig()`;
- botão "Imprimir teste" usando `sendTestPrint()`;
- mensagem de download placeholder caso o app local não esteja instalado.

4. Segurança/UX:

- nunca mostrar erro técnico como "localhost connection refused";
- usar texto: "O Zelo Impressão não está aberto neste computador. Abra o aplicativo ou use a impressão pelo navegador.";
- se impressora falhar, usar: "Não conseguimos acessar a impressora selecionada. Verifique se ela está ligada e conectada.";
- checkout/venda deve seguir mesmo sem impressão automática.

5. Verificação:

- rodar `npm install`;
- rodar `npm run build`;
- confirmar que os call sites existentes não mudaram.

Resultado esperado:

- PDV tenta Zelo Impressão primeiro;
- se indisponível, abre impressão do navegador;
- a venda não é bloqueada por falha do componente local.
