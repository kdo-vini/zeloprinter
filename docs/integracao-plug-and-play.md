# Integração plug and play do Zelo Impressão

Este documento resume o caminho de menor atrito para integrar `zelopdv`, `zelochat` ou qualquer app web ao `Zelo Impressão`.

## Verdade prática

Para as origens oficiais do ZeloPDV e do ZeloChat, o SDK faz a conexão
automaticamente depois que o Zelo Impressão está instalado e aberto. O usuário
não precisa digitar código nesse caminho.

O código continua disponível para integrações de terceiros ou quando o app
desativa explicitamente o comportamento com `autoConnect: false`.

Todo app integrado precisa:

- detectar se o app local está aberto;
- tentar a conexão automática quando a origem for oficial;
- manter pareamento por código como fallback;
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
  connectZeloImpressao,
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

    if (!status.paired) {
      throw new Error("Conecte o Zelo Impressão para continuar.");
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
3. Se estiver online e a origem for oficial, o SDK chama `POST /connect` automaticamente.
4. Se a conexão automática não for permitida ou estiver desativada, pedir o código de pareamento.
5. Salvar a impressora selecionada uma única vez.
6. Nos fluxos automáticos, tentar `Zelo Impressão` primeiro.
7. Se falhar:
   - `zelopdv`: seguir venda e cair para browser print;
   - `zelochat`: seguir pedido e avisar operador.

## O que ainda precisa existir nos apps principais

Mesmo no modo mais plug and play, `zelochat` e `zelopdv` ainda precisam manter:

- um ponto de integração para disparar impressão;
- UI de status e fallback de pareamento;
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
