# Zelo Impressão — memória técnica atual

Atualizado em 2026-09-04. Evidências e pendências em [auditoria](docs/audits/2026-09-04-zeloprinter.md).

## Implementação
O aplicativo é .NET 8/WinForms para Windows, com ASP.NET Core em 127.0.0.1:17321, tray, WMI e Winspool/PrintDocument. Electron não está mais no repositório. Node executa apenas scripts de versão, artefato browser e testes; não há dependências npm de runtime.

- Program.cs: instância única, inicialização e shutdown.
- LocalApiServer.cs: HTTP, CORS, autenticação, limite de corpo e erros estruturados.
- ConfigStore.cs/PairingService.cs: configuração e pareamento.
- PrintDispatcher.cs/PrintJournal.cs: submissão serial, arbitragem PDV/Chat e deduplicação persistente.
- InstanceSignal.cs: segunda execução solicita abertura da janela via pipe restrito ao usuário atual.
- PrintService.cs/RawPrinter.cs: validação, driver de texto e bytes ESC/POS.
- packages/client/src/index.js e browser.js: clientes ESM e IIFE; testes de paridade em tests/client.test.mjs.

Configuração fica em %APPDATA%\Zelo Impressao\config.json. Gravação usa arquivo temporário seguido de substituição. Logs em logs/zelo-impressao.log giram em 5 MiB, com um arquivo anterior. Tokens e conteúdo do cupom não são registrados.

## Contrato local

| Rota | Proteção | Resultado |
| --- | --- | --- |
| GET /health | allowlist de Origin quando presente | disponibilidade, versão, memória, capacidades |
| POST /pair | código local temporário | novo token independente |
| POST /connect | Origin na lista separada de origens confiáveis | token independente, comportamento lançado na 0.1.4 |
| GET /printers | token | impressoras Windows |
| GET/POST /config | token | preferências públicas, sem hashes |
| POST /print | token | status: spooled após submissão ao spooler |
| POST /test-print | token | teste ESC/POS na mesma fila |

A conexão automática de 0.1.4 foi preservada: `/connect` exige Origin tanto no CORS quanto em `AutoConnectOrigins`, lista fixa separada. Sem Origin ou apenas com origem adicional no CORS, não emite token. Não exige código para origens confiáveis; `/pair` continua disponível. Os SDKs tentam autoConnect por padrão e validam tokens existentes em `/config` antes de declarar conexão. Uma falha transitória dessa validação não cria outro token; `autoConnect: false` permite manter somente o pareamento por código. source aceita zelopdv e zelochat; ZeloMenu não chama o componente diretamente. companyStoreId participa da dedupe automática, mas não é autorização de tenant.

Pareamento mantém até 50 hashes, compatível com a versão publicada 0.1.4. O hash legado é migrado e listas existentes são normalizadas sem descartar credenciais válidas. Ao atingir o limite, novas emissões recebem `PAIRING_LIMIT`; não há revogação silenciosa do token mais antigo. Cada código dura até dez minutos, é de uso único e bloqueia após cinco erros, até renovação local. “Desconectar navegadores” revoga os tokens e desativa autoConnect no mesmo estado persistido: `/connect` passa a recusar a origem com `AUTO_CONNECT_DISABLED`, inclusive após reinício. `/pair` continua funcional e não reativa autoConnect. A tela local oferece reativação explícita; HTTP não pode alterar `requirePairing` nem `autoConnectEnabled`. Desmarcar somente a permissão de conexão automática impede novas emissões, sem revogar tokens atuais; a ação de desconectar executa ambos. Tokens não expiram automaticamente.

JSON é limitado a 512 KiB, inclusive chunked. Conteúdo vazio, formato desconhecido e base64 inválido são recusados antes de acessar a impressora. Uma impressora escolhida que desapareceu provoca erro, sem desviar o cupom para outro dispositivo.

## Impressão e recuperação
Na versão 0.2.0, pedidos automáticos enviam `intent: { mode: "automatic", orderId: "public.zelo_orders.id", purpose: "order_ticket" }`. `companyStoreId` é o UUID auth do dono da loja em ambos os clientes. A chave canônica normaliza esses UUIDs e ignora source, jobId, impressora e rendering. A preferência padrão é PDV, alterável para Chat na tela existente ou `/config` autenticado. O candidato não preferido aguarda 1500 ms; se o preferido não chegar, imprime. Preferência é limitada a essa janela, não à presença de abas. Após iniciar o executor, só uma recusa explicitamente anterior ao spooler pode trocar uma vez para o outro candidato já recebido. Resultado incerto nunca troca.

Sem intenção automática, `jobId` opcional (até 128 caracteres) continua escopado por source/companyStoreId e exige mesmo conteúdo: conflito retorna 409 JOB_ID_CONFLICT. Segunda via explícita usa `intent.mode: "manual"` e um novo jobId. Reenvio da mesma intenção mantém o id. A fila permite 16 pendentes e executa uma submissão por vez.

`print-history.jsonl` guarda somente hashes e estado/source/mode/timestamp por sete dias; não guarda cupom, nomes de impressoras ou UUIDs em claro. Reserva é gravada e flushed antes do spool. Reserva incompleta após restart é UNKNOWN; sucesso persistido volta `status: "deduplicated"`, `printer: null`. Há 1000 resultados em cache quente; removê-los não remove histórico persistente. Capacidade padrão é 10000 registros, ampliável em passos de 10000 até 50000 sem remover registros. Ao lotar, novas impressões com chave são recusadas; duplicatas confirmadas continuam sendo reconhecidas. Arquivo corrompido bloqueia novas submissões com chave; último append incompleto preserva registros anteriores. Nunca apagar o histórico para contornar a proteção. Retenção expirada e perda/exclusão do arquivo limitam a proteção; não existe exactly-once físico.

Sucesso significa aceitação pelo spooler, não impressão física. Recusas anteriores ao spooler são retrySafe: true e permitem nova tentativa após correção. Falhas incertas ficam em cache e retornam PRINT_OUTCOME_UNKNOWN, retrySafe: false. Não há retry automático.

SDKs consultam /health antes de enviar. Impressão automática exige `canonicalAutoPrint` e `persistentPrintDeduplication`; agente ausente/antigo produz `AUTO_PRINT_COORDINATION_REQUIRED`, `retrySafe: false`, sem POST/fallback automático. Ausência no preflight de uma impressão manual permite fallback. Falha de transporte/JSON depois de iniciar o POST exige conferir a saída. Timeout inclui leitura do corpo. Cancelar HTTP não cancela a tarefa nativa nem sua reserva. Detecção de pareamento testa o token via /config.

## Validação e distribuição
npm test executa testes JS e harness nativo sem imprimir. npm run build compila Release. npm run dotnet:publish:win gera self-contained. npm run sdk:prepare prepara browser/manifest. CI testa antes de empacotar e inclui SDK nos artefatos. Instalador depende do Inno Setup.

O harness permite roll-forward em máquinas com runtime mais novo e pode ser publicado self-contained no runtime 8. Testes usam configuração temporária, pipes isolados e porta efêmera, sem alterar startup real. Falha de startup agora aparece em logs/UI; o instalador remove a chave Run ao desinstalar e não força taskkill. CI verifica que o tag coincide com package.json antes de produzir a release.

[Distribuição](docs/distribuicao-zelo-impressao.md) é a referência das URLs. Edição local do SDK não atualiza cópias dos apps nem artefatos publicados. Hardware térmico, instalador limpo, assinatura, permissão de rede local do navegador e interrupção física de impressão exigem homologação operacional.
