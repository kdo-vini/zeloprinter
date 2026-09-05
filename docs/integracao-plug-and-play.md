# Integração plug and play do Zelo Impressão

Este documento resume o caminho de menor atrito para integrar `zelopdv`, `zelochat` ou qualquer app web ao `Zelo Impressão`.

## Verdade prática

Hoje não existe integração realmente sem código nos apps web.

Sempre será necessário pelo menos:

- detectar se o app local está aberto;
- disparar o pareamento por código;
- enviar o job de impressão;
- mostrar fallback amigável quando o app local não estiver disponível.

O objetivo de plug and play aqui é outro:

- não depender de `file:../outro-repo/...`;
- não trocar links a cada release;
- reduzir a integração a um trecho pequeno e estável;
- permitir uso por pacote ou por script browser.

## Opção A: SDK por pacote

Use quando o app já tem build com npm, Vite, React ou SvelteKit.

```ts
import {
  detectZeloImpressao,
  pairZeloImpressao,
  sendPrintJob,
  sendRawEscposPrintJob,
  sendTestPrint,
  getPrinters,
  saveConfig,
  getZeloImpressaoFriendlyMessage,
  ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL,
  getZeloImpressaoDownloadPageUrl,
} from "@zelo/impressao-client";
```

Recomendação operacional:

- publicar o pacote em registry privado; ou
- espelhar o código do pacote dentro do próprio app, se o deploy for isolado.

Não usar em produção:

- `file:../zeloprinter/packages/client`

Esse formato serve para desenvolvimento local, mas não para build isolado em CI, Vercel ou deploy por repositório único.

## Opção B: SDK por script browser

Use quando você quer a menor integração possível sem adicionar dependência de build.

Script planejado para distribuição estável:

```html
<script src="https://zelopdv.com.br/downloads/zelo-impressao/sdk/zelo-impressao-client.browser.js"></script>
```

Artefato local para upload:

```bash
npm run sdk:prepare
```

Saída:

```text
release/sdk/zelo-impressao-client.browser.js
release/sdk/manifest.json
```

Depois disso, o app passa a usar `window.ZeloImpressao`.

Exemplo mínimo:

```html
<script>
  const zelo = window.ZeloImpressao.createClient();

  async function imprimirPedido(texto) {
    const status = await zelo.detectZeloImpressao();
    if (!status.running) {
      throw new Error(
        zelo.getZeloImpressaoFriendlyMessage(
          "localhost connection refused",
        ),
      );
    }

    await zelo.sendPrintJob({
      source: "zelochat",
      type: "kitchen_order",
      content: {
        format: "text",
        text: texto,
      },
    });
  }
</script>
```

## Fluxo mínimo recomendado para qualquer app

1. Detectar `GET /health`.
2. Se estiver offline, mostrar:
   - botão `Baixar para Windows`
   - link `Ver instruções`
   - fallback manual pelo navegador
3. Se estiver online mas sem token, pedir código de pareamento.
4. Salvar impressora selecionada uma única vez.
5. Nos fluxos automáticos, exigir o contrato canônico abaixo e agente 0.2.0 ou posterior.
6. Se falhar:
   - `zelopdv`: seguir venda/pedido; fallback de navegador somente por ação manual consciente;
   - `zelochat`: seguir pedido e avisar operador.

`PRINT_OUTCOME_UNKNOWN`/`retrySafe: false` exige conferir a saída antes de reimprimir. Não repetir nem abrir fallback automaticamente. O SDK consulta `/health` antes do POST para distinguir ausência do componente antes de enviar o cupom.

O nativo mantém a conexão automática lançada em 0.1.4: `/connect` só emite token para origens confiáveis presentes em `AutoConnectOrigins` e no CORS. Adicionar uma origem ao CORS não permite emitir token automaticamente. `/pair` oferece o caminho por código. O SDK tenta autoConnect por padrão (desative com `autoConnect: false`) e valida tokens armazenados em `/config`. Até 50 navegadores podem permanecer conectados; ao lotar, novas emissões retornam `PAIRING_LIMIT` sem revogar os anteriores. A ação local “Desconectar navegadores” também desativa a conexão automática de forma persistente (`AUTO_CONNECT_DISABLED`). O código local continua funcionando; reativar autoConnect exige o controle na tela local. HTTP não pode alterar essa autorização.

Uma intenção manual pode fornecer `jobId` (até 128 caracteres), preservado em retries. Segunda via explícita recebe novo id. SDKs geram um id novo por chamada quando o caller não informa um. Exemplos sem `intent.mode: "automatic"` neste documento são impressões manuais.

## Pedido automático compartilhado por PDV e Chat — 0.2.0

```js
await sendPrintJob({
  source: "zelopdv", // no Chat: "zelochat"
  companyStoreId: ownerUserId, // mesmo UUID auth do dono nos dois apps
  intent: { mode: "automatic", orderId: zeloOrderId, purpose: "order_ticket" },
  type: "receipt",
  content: { format: "text", text: receiptText }
});
```

`zeloOrderId` é `public.zelo_orders.id`. Não usar ID local do browser, ID da empresa_perfil ou outro espelho de pedido. O nativo valida UUIDs, normaliza formato/caixa e deduplica loja+pedido+finalidade independentemente de rendering, source, impressora e jobId. O mesmo intent também é aceito por `sendRawEscposPrintJob`.

O aplicativo preferido é configurável, com PDV padrão. O outro espera 1500 ms; após a janela, segue sozinho para não segurar pedidos quando PDV não estiver recebendo eventos. Um PDV que descobrir o pedido depois dessa janela recebe sucesso deduplicado. Quando ambos já são candidatos, uma única recusa pre-spool `retrySafe: true` permite usar o outro; resultado incerto não permite troca.

Sucesso tem `status: "spooled"` ou `"deduplicated"`, `mode` e `arbitration: { mode: "automatic", source, orderId, purpose, duplicate }` do vencedor real. Ambos status são sucesso mesmo com conteúdo diferente. Após restart, replay confirmado tem `printer: null`, porque o histórico não guarda nome de impressora.

O histórico guarda hashes/outcomes por sete dias, reserva antes de invocar spool e restaura tentativas incompletas como `PRINT_OUTCOME_UNKNOWN`. Capacidade padrão 10000, ampliável localmente até 50000; `PRINT_HISTORY_FULL` bloqueia novas intenções com chave sem expulsar registros vigentes. `PRINT_HISTORY_UNAVAILABLE` exige diagnóstico do arquivo/permissões; não apagar o histórico em massa. A proteção termina com expiração/perda do histórico e não comprova saída física do papel.

Automático exige health capabilities `canonicalAutoPrint: true` e `persistentPrintDeduplication: true`. Agente antigo/ausente retorna erro SDK `AUTO_PRINT_COORDINATION_REQUIRED`, `retrySafe: false`, antes do POST. Não acionar browser fallback nem retry automático. Para segunda via após conferência, mandar `intent: { mode: "manual", orderId: zeloOrderId, purpose: "order_ticket" }` com jobId novo.

## O que ainda precisa existir nos apps principais

Mesmo no modo mais plug and play, `zelochat` e `zelopdv` ainda precisam manter:

- um ponto de integração para disparar impressão;
- UI de status e pareamento;
- CTA de download;
- tratamento de erro amigável;
- fallback de impressão pelo navegador.

## O que deixa de exigir mudança nos apps a cada release

Depois da integração inicial, novas releases do instalador não devem exigir mudança no app quando:

- o instalador é publicado em URL estável;
- a página pública continua estável;
- o contrato HTTP local permanece compatível.

URLs estáveis:

- página: `https://zelopdv.com.br/zelo-impressao`
- instalador: `https://zelopdv.com.br/downloads/zelo-impressao/latest/Zelo-Impressao-Setup.exe`
- SDK browser: `https://zelopdv.com.br/downloads/zelo-impressao/sdk/zelo-impressao-client.browser.js`
