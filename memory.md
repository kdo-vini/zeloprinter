# Zelo Impressão - Memória Técnica

## Arquitetura de impressão

Zelo Impressão é o componente local Windows do ecossistema Zelo para impressão automática de comprovantes, pedidos e comandas. Ele substitui WebUSB como caminho principal e expõe uma API HTTP apenas em `127.0.0.1:17321`.

Decisão atual de tecnologia: a implementação definitiva deve ser .NET nativo para Windows. Electron fica como protótipo/legado do contrato, mas não deve ser o alvo do rollout inicial para clientes. A versão .NET deve operar sem janela principal aberta, ficar no tray, iniciar com Windows e ser distribuída como instalador self-contained.

Fluxo prioritário:

1. Zelo PDV ou ZeloChat chama o SDK compartilhado `@zelo/impressao-client`.
2. O SDK envia o job para `http://127.0.0.1:17321`.
3. Zelo Impressão valida origem, token, tamanho e schema do payload.
4. O componente resolve a impressora selecionada ou a padrão do Windows.
5. Impressão na versão .NET:
   - ESC/POS/raw: enviado ao spooler do Windows via `winspool.drv`.
   - Texto/HTML: impresso pelo driver do Windows via `PrintDocument` após normalização para texto.
6. Se o componente local não estiver disponível, os apps mantêm fallback pelo navegador.

## Contrato da API Local

Base URL: `http://127.0.0.1:17321`

- `GET /health`
  - Público para detecção.
  - Retorna status, versão, OS, memória do processo, pareamento e capacidades.

- `GET /printers`
  - Requer `X-Zelo-Impressao-Token`.
  - Retorna impressoras instaladas no Windows com nome, id, padrão, status e driver.

- `POST /print`
  - Requer token.
  - Recebe jobs `receipt`, `kitchen_order`, `test` ou `raw_escpos`.
  - Conteúdo aceito: `text`, `html`, `raw_escpos_base64`.

- `POST /test-print`
  - Requer token.
  - Envia um recibo curto de teste para a impressora configurada ou informada.

- `GET /config`
  - Requer token.
  - Retorna impressora selecionada, inicialização com Windows e origens permitidas.

- `POST /config`
  - Requer token.
  - Atualiza impressora selecionada, nome e preferências locais permitidas.

- `POST /pair`
  - Público, mas exige código de 6 dígitos exibido no app local.
  - Retorna token local que o SDK salva no `localStorage` do navegador.

## Integração Zelo PDV

Arquivo principal: `/home/vinicius/code/zelopdv/src/lib/printService.js`.

As funções públicas existentes continuam iguais:

- `printVenda`
- `printMovCaixa`
- `printPagamentoFiado`
- `printTeste`

Mudança de comportamento: cada função tenta Zelo Impressão primeiro via `sendRawEscposPrintJob`. Se falhar, mantém o fallback HTML/iframe pelo navegador. O WebUSB antigo permanece disponível na tela de integrações como recurso legado/opcional, mas deixou de ser o caminho principal.

A tela `/perfil` ganhou o bloco "Impressão automática" para:

- detectar status: conectado, não instalado ou desconectado;
- parear com código;
- listar impressoras do Windows;
- salvar impressora selecionada;
- enviar impressão de teste;
- orientar fallback pelo navegador.

## Integração ZeloChat

Arquivos principais:

- `/home/vinicius/code/zelochat/src/services/printerService.ts`
- `/home/vinicius/code/zelochat/src/hooks/usePrinter.ts`
- `/home/vinicius/code/zelochat/src/components/PrinterButton.tsx`

O hook `usePrinter()` preserva a API consumida pelo app, mas agora usa Zelo Impressão como backend local. O botão de impressão mostra status, erro amigável e campo de pareamento por código quando necessário.

Pedidos novos usam `POST /print` com `type: "kitchen_order"` e conteúdo em texto. Relatórios do dia tentam Zelo Impressão e, por serem acionados por clique do operador, podem cair para impressão pelo navegador se o componente local estiver indisponível.

## Fallback

PDV:

1. Zelo Impressão local.
2. Impressão HTML pelo navegador.
3. Mensagem amigável via toast.

ZeloChat:

1. Zelo Impressão local.
2. Relatório manual do dia pode abrir fallback pelo navegador.
3. Auto-print de pedido não bloqueia fluxo; falha vira aviso ao operador.

Mensagens técnicas como conexão recusada são traduzidas para textos operacionais, por exemplo: "O Zelo Impressão não está aberto neste computador. Abra o aplicativo ou use a impressão pelo navegador."

## Segurança

Decisões implementadas:

- API escuta somente em `127.0.0.1`.
- CORS por allowlist para domínios Zelo e desenvolvimento local.
- Token local por navegador via pareamento com código temporário.
- Endpoints sensíveis exigem `X-Zelo-Impressao-Token`.
- Payload máximo de 512 KB.
- Validação de schema com Zod.
- Sem endpoint para ler arquivos arbitrários ou executar comandos recebidos da web.
- Impressão RAW só recebe bytes do job e escreve arquivo temporário interno, removido após envio ao spooler.
- Configuração local fica no `userData` do Electron.

## Melhorias futuras

- Medir RSS em idle no Windows real e definir teto operacional antes do rollout amplo.
- Publicar instalador assinado com code signing.
- Adicionar atualização automática.
- Expor status avançado de fila/spooler quando o driver oferecer dados confiáveis.
- Adicionar múltiplos perfis de impressora por tipo de job: caixa, cozinha, entrega.
- Suportar templates HTML oficiais por tipo de recibo.
- Adicionar testes E2E no Windows com impressora virtual e impressora térmica real.
