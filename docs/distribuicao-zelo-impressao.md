# Distribuição do Zelo Impressão

Este documento define **uma única fonte de verdade** para o download do instalador do `Zelo Impressão` e como `zelopdv` e `zelochat` devem consumi-la.

## Objetivo

Evitar que cada app mantenha seu próprio link hardcoded ou precise receber upload manual do instalador em vários lugares a cada release.

## Fonte única de verdade

A fonte única de verdade deve ser o **endpoint/URL pública estável do instalador**, não uma dependência local entre repositórios.

O pacote compartilhado `@zelo/impressao-client` expõe as URLs oficiais de download dentro do workspace local, mas apps deployados isoladamente (por exemplo, um build de Vercel que sobe apenas o repositório `zelopdv`) **não podem depender de `file:../outro-repo/...`**.

Na prática:

- publique o **artefato canônico no GitHub Releases** de `kdo-vini/zeloprinter`; a URL estável do próprio `zelopdv.com.br` encaminha para essa release
- centralize a **página pública** em `zelopdv.com.br/zelo-impressao`
- dentro de cada app, prefira consumir essas URLs por módulo local ou configuração própria quando o deploy for isolado
- não assuma que `file:../zeloprinter/packages/client` existirá no ambiente de build
- quando quiser o menor atrito possível, prefira um SDK browser estável em vez de path local entre repositórios

Arquivos:

- `packages/client/src/index.js`
- `packages/client/src/index.d.ts`
- `packages/client/src/browser.js`

Exports principais:

- `ZELO_IMPRESSAO_DOWNLOAD_PAGE_URL`
- `ZELO_IMPRESSAO_DOWNLOADS_BASE_URL`
- `ZELO_IMPRESSAO_INSTALLER_FILENAME`
- `ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL`
- `ZELO_IMPRESSAO_BROWSER_SDK_URL`
- `getZeloImpressaoInstallerUrl(channel?)`
- `getZeloImpressaoDownloadPageUrl()`
- `getZeloImpressaoBrowserSdkUrl()`
- `connectZeloImpressao(options?)`
- `detectZeloImpressao({ autoConnect?: boolean })`

## URLs oficiais

### Página pública

- `https://zelopdv.com.br/zelo-impressao`

Uso:

- onboarding
- FAQ
- suporte
- CTA nos apps
- página com instruções de instalação

### Instalador estável

- `https://zelopdv.com.br/downloads/zelo-impressao/latest/Zelo-Impressao-Setup.exe`

Uso:

- botão direto de download
- links dentro do PDV
- links dentro do Chat
- automações de suporte

### SDK browser estável

- `https://zelopdv.com.br/downloads/zelo-impressao/sdk/zelo-impressao-client.browser.js`

Uso:

- integração rápida sem dependência local entre repositórios
- páginas estáticas ou apps com baixo atrito de build
- fallback quando não fizer sentido publicar `@zelo/impressao-client` em registry

### Releases versionadas

Padrão previsto:

- `https://zelopdv.com.br/downloads/zelo-impressao/<versao>/Zelo-Impressao-Setup.exe`

Exemplo:

- `https://zelopdv.com.br/downloads/zelo-impressao/0.1.0/Zelo-Impressao-Setup.exe`

O helper `getZeloImpressaoInstallerUrl('0.1.0')` monta esse formato automaticamente.

## Fluxo de versionamento recomendado

Antes de publicar uma nova release:

1. Escolha o tipo de versão:
   - `npm run version:bump:patch`
   - `npm run version:bump:minor`
   - `npm run version:bump:major`
2. Se precisar definir manualmente:
   - `npm run version:set -- 0.1.1`
3. Confirme que os arquivos foram sincronizados:
   - `npm run version:sync`

O `package.json` do `zeloprinter` é a fonte de verdade, e os scripts sincronizam automaticamente os demais pontos de versão do app local.

## Fluxo de publicação recomendado

1. Gerar o executável self-contained:
   - `powershell -ExecutionPolicy Bypass -File .\native\build\Build-Windows.ps1`
2. Gerar o instalador:
   - `iscc .\native\installer\ZeloImpressao.iss`
3. Validar a branch pelo workflow `Release Windows Installer` via workflow_dispatch, sem tag: ele gera artefatos de CI sem publicar release.
4. Após revisão e autorização, publicar tag `v<versao>` correspondente ao package.json. O workflow publica o instalador versionado, o alias `Zelo-Impressao-Setup.exe` e os arquivos do SDK no GitHub Releases.
   - Origem canônica: `https://github.com/kdo-vini/zeloprinter/releases/latest/download/Zelo-Impressao-Setup.exe`
   - A rota pública do PDV deve acompanhar essa origem; não fixar `latest` em binário de uma versão anterior via configuração.
5. Se necessário, atualizar a página:
   - `https://zelopdv.com.br/zelo-impressao`

## Regra prática para futuras atualizações

Quando sair uma nova versão do app local:

- publique **uma vez** a release no GitHub pelo workflow
- mantenha o alias estável entre os assets da release
- **não** altere links em `zelopdv` nem em `zelochat`

Se os apps estiverem importando as constantes do pacote compartilhado, eles já continuarão apontando para o mesmo lugar.

## Onde cada produto deve usar esses links

### Zelo PDV

Usar na área de integrações para:

- botão `Baixar para Windows`
- link `Ver instruções`
- fallback quando o app local não estiver instalado

### ZeloChat

Usar em:

- botão `Conectar impressora`
- estado `Zelo Impressão offline`
- modal de conexão de impressora
- onboarding/FAQ quando necessário

## Regra de UX

Prioridade de comunicação:

1. **Zelo Impressão (recomendado)**
2. WebUSB como opção avançada
3. impressão pelo navegador como fallback

Depois que o Zelo Impressão estiver instalado e aberto:

- ZeloPDV e ZeloChat tentam `POST /connect` automaticamente;
- o SDK salva um token independente para cada navegador/origem;
- o usuário só precisa escolher a impressora e fazer o primeiro teste;
- integrações de terceiros continuam usando `POST /pair` com o código exibido no app.

## CORS / origens autorizadas

Para os apps web se comunicarem com o serviço local em `http://127.0.0.1:17321`, as origens precisam estar liberadas no `zeloprinter`.

Arquivo relevante:

- `native/ZeloImpressao/AppConstants.cs`

As origens autorizadas para CORS e as origens autorizadas a criar uma sessão
automaticamente são listas separadas em `AppConstants.cs`. Uma origem nova de
CORS não ganha permissão de auto-connect por acidente.

Origens oficiais com auto-connect:

- `https://zelopdv.com.br`
- `https://www.zelopdv.com.br`
- `https://app.zelopdv.com.br`
- `https://chat.zelopdv.com.br`
- `https://zelochat.com.br`
- `https://www.zelochat.com.br`
- `https://app.zelochat.com.br`
- endereços locais de desenvolvimento (`localhost` e `127.0.0.1` nas portas 3000 e 5173)

O agente mantém até 50 tokens independentes. Na 0.2.0, novas emissões são recusadas
com `PAIRING_LIMIT` quando esse limite é atingido, preservando os navegadores
existentes; o token mais antigo não é mais descartado silenciosamente. A revogação
local invalida os tokens atuais e desativa autoConnect de forma persistente.
O pareamento por código continua disponível; reativar a conexão automática exige
o controle na tela local e não pode ser feito por HTTP.
A instalação existente continua
válida: o hash único antigo é migrado para a nova coleção na leitura da
configuração.

## Observações operacionais

### Assinatura de código

Para reduzir alertas do Windows SmartScreen, o ideal é assinar digitalmente o instalador `.exe` antes da distribuição pública.

### Compatibilidade

O instalador atual é voltado para:

- Windows 10/11
- arquitetura `x64`

### Nome do arquivo

Manter um nome estável ajuda suporte e documentação:

- `Zelo-Impressao-Setup.exe`

Se a release versionada tiver outro nome no pipeline, o storage/CDN deve publicar uma cópia/alias com esse nome na pasta `latest`.

## Exemplo de consumo

```ts
import {
  ZELO_IMPRESSAO_INSTALLER_DOWNLOAD_URL,
  getZeloImpressaoDownloadPageUrl,
} from '@zelo/impressao-client';
```

## Resumo

A partir daqui:

- o link do instalador fica centralizado no pacote compartilhado
- `zelopdv` e `zelochat` não precisam manter URLs próprias
- uma nova release exige upload em **um único local**
- `chat.zelopdv.com.br` passa a ser origem permitida para integração com o app local
- apps web também podem consumir um SDK browser estável, sem `file:../...`
