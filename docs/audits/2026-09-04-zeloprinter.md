# Auditoria ZeloPrinter — 2026-09-04

## Estado local e reconciliação

Repositório: `C:\Users\Vinicius\Documents\code\zeloprinter`. A auditoria começou com checkout limpo em `71ec37c` (0.1.2). Na preparação da entrega foi descoberta divergência: `origin/main` e `v0.1.4` apontavam para `596eea5`, três commits à frente. O trabalho foi preservado em backup e stash, o checkout avançou por fast-forward e as mudanças foram conciliadas antes de reconstruir os artefatos. A afirmação inicial de que `/connect` não existia valia somente para o checkout antigo; **a conexão automática publicada na 0.1.4 foi preservada**.

Versão publicada: **0.2.0**, commit `e068ff8f6924551e5abb31d8731968fc93172404`, promovido pelo coordenador após revisão. Passam **38 testes JS e 33 cenários nativos**, também executados self-contained no **.NET 8.0.27**. Publish, SDK browser e instalador foram reconstruídos após a reconciliação e a correção final de revogação. Nenhum pedido real ou job de papel foi enviado. O coordenador revisou fila, journal e API; observações sobre falha incerta, troca segura, capacidade e revogação foram incorporadas. [Workflow da release aprovado](https://github.com/kdo-vini/zeloprinter/actions/runs/33933244243), [release pública v0.2.0](https://github.com/kdo-vini/zeloprinter/releases/tag/v0.2.0), publicada em 2026-09-05 00:34 UTC (2026-09-04 21:34 em São Paulo).

**A coordenação durável exige atualizar o agente Windows para 0.2.0 e usar os clientes PDV/Chat compatíveis.** Atualizar somente os sites não adiciona dedupe ao agente antigo: o preflight recusa impressão automática sem as capacidades exigidas. A proteção vale para clientes que usam a mesma instalação/perfil Windows, owner e pedido canônicos, nos limites descritos abaixo.

## Inventário

| Área | Implementação |
| --- | --- |
| Runtime | .NET 8 Windows x64, WinForms/tray, ASP.NET Core/Kestrel |
| Inicialização | `Program.cs`, mutex global; `InstanceSignal.cs`, pipe do usuário atual para mostrar configurações da instância existente |
| HTTP | `LocalApiServer.cs`, loopback IPv4, porta 17321, limite real de 512 KiB inclusive chunked |
| Autorização | CORS e token; `/pair` por código; `/connect` por lista separada de origens confiáveis e permissão local revogável |
| Dados | `%APPDATA%\Zelo Impressao\config.json`, `print-history.jsonl`, logs locais; sem banco/cloud/.env |
| Dispositivos | `PrinterManager.cs`, WMI e InstalledPrinters; seleção explícita não troca silenciosamente de impressora |
| Impressão | RAW Winspool em `RawPrinter.cs`; PrintDocument para texto; HTML convertido em texto |
| Concorrência | `PrintDispatcher.cs`: executor serial, até 16 intenções pendentes, arbitragem automática canônica |
| Persistência | `PrintJournal.cs`: reserva flushed antes do spooler, replay confirmado/incerto, sete dias, capacidade 10–50 mil |
| SDK | ESM/IIFE browser, tipos e matriz de paridade; sem dependências npm de runtime |
| Dependência nativa | System.Management 8.0.0 e frameworks .NET |
| Build/CI | npm/dotnet, Inno Setup, GitHub Actions Windows; tag precisa corresponder à versão antes de publish |

Foram inspecionados C#, SDKs, scripts, workflow, instalador e documentação. Electron, zod, tsx e tipos antigos não tinham consumidores; dependências órfãs foram removidas com npm. Testes injetam um contador na fronteira física, sem mock de sucesso no caminho operacional.

## Achados e correções evidenciados

| Prioridade | Problema/evidência | Resultado |
| --- | --- | --- |
| P1 | Checkout 0.1.2 divergente de 0.1.4; publicar a base antiga perderia funcionalidades lançadas. | Reconciliado sobre `596eea5`, mantendo `/connect`, autoConnect, AppClock, expiração nullable, DTO público e abertura protegida da janela. Artefatos reconstruídos. |
| P1 | PDV/Chat enviam mesmo pedido com ids/renderings distintos; jobId/source não bastam. | Chave automática por owner + zelo_orders.id + purpose. Testes concorrentes e HTTP confirmam um executor. |
| P1 | Dedupe volátil perdia decisões ao reiniciar. | Journal com reserva antes da chamada física; sucesso recuperado é deduplicated e reserva inconclusa é UNKNOWN. Corrupção/falha de escrita bloqueia antes de novo spool. |
| P1 | Falha após POST era classificada unavailable e induzia fallback. | UNKNOWN/retrySafe:false, timeout também no corpo, preflight para ausência antes do envio. HTTP 400 genérico legado não é seguro. |
| P1 | Troca de origem depois de erro parcial poderia duplicar papel. | Uma troca para candidato já recebido, só após recusa explicitamente anterior ao spooler. Nunca troca em falha genérica/incerta; resposta identifica vencedor real. |
| P1 | 0.1.2 sobrescrevia token; 0.1.4 mantinha lista mas descartava o mais antigo ao lotar. | Até 50 tokens, migração compatível e PAIRING_LIMIT sem descarte. Save precede troca de estado. |
| P1 | Revogar só tokens permitia autoConnect imediatamente. Regressão reproduziu “Revoked browser immediately reauthorized itself”. | Revogação também persiste autoConnect desativado. Verificação e emissão sob mesmo lock; `/pair` funciona, reativação só local. Testado após restart. |
| P1 | SDK declarava conectado pela mera existência de token. | `/config` valida token; 401 limpa token implícito. AutoConnect preservado; falha transitória não emite outra credencial. |
| P1 | Impressora escolhida que sumiu caía na padrão/primeira, inclusive PDF. | PRINTER_UNAVAILABLE para seleção ausente; padrão só sem seleção. Teste com catálogo simulado. |
| P1 | Paginação por quebras explícitas perdia linhas longas quebradas visualmente. | Avanço por caracteres medidos/desenhados; bitmap percorre texto completo sem papel. |
| P2 | JSON 200 ilegível era sucesso; schema/base64 inválido chegava tarde ao driver. | SDK exige resposta reconhecível; nativo valida antes do executor físico. Matriz ESM/IIFE e HTTP real. |
| P2 | Content-Length permitia bypass do limite via chunked. | Kestrel limita bytes reais; teste HTTP 413. |
| P2 | `/config` permitia desativar segurança; base 0.1.2 expunha estado interno. | DTO público upstream preservado/estendido. `requirePairing` e `autoConnectEnabled` recusados por HTTP. CORS adicional não permite tokenmint. |
| P2 | Código sem limite/consumo concorrente/renovação garantida. | Cinco erros, uso único sob lock, renovação local. Dez confirmações concorrentes emitem uma vez. |
| P2 | Running antes do bind gerava falso sucesso e impedia recuperação. | Estado só após StartAsync; lifecycle serial, host falho descartado. Teste ocupa/libera porta real. |
| P2 | RAW ignorava EndPage/EndDoc; retorno StartDoc declarado bool. | DWORD correto e conclusão checada; falha física é incerta. Compilado, ainda sem homologação térmica. |
| P2 | Erros genéricos/CORS ausente escondiam a distinção de aceite. | Códigos estruturados e CORS nos erros; 5xx genérico de impressão é incerto. |
| P2 | Startup silencioso; segunda execução não mostrava janela; installer taskkill forçado. | Erro em UI/log, pipe de ativação, shutdown limitado; instalador coopera com mutex e remove Run na desinstalação. |
| P2 | Logs ilimitados, config trocava memória antes do save. | Rotação 5 MiB + anterior, lock, arquivo temporário/substituição antes da troca. Journal tem flush explícito; config não promete resistência a toda queda de energia. |
| P2 | PowerShell podia continuar após dotnet falhar; tag não validava versão. | Exit codes explícitos e check-tag no CI; teste rejeita divergência. Raiz/SDK/lock/C#/Inno sincronizados. |
| P3 | Dependências órfãs incluíam alerta baixo esbuild via tsx. | Removidas; npm audit zero. NuGet sem vulneráveis nas fontes consultadas. |

## Contrato PDV/Chat

Automatic envia `source:zelopdv|zelochat`, `companyStoreId` = UUID auth do dono e `intent:{mode:automatic,orderId:public.zelo_orders.id,purpose:order_ticket}`. Não usar empresa_perfil.id nem online_orders.id. UUIDs são normalizados. Chave ignora conteúdo, jobId, source e impressora; rendering distinto ainda recebe sucesso deduplicated. Token autoriza o componente local inteiro: esses campos identificam dedupe, não autorização de tenant.

Preferência padrão PDV, configurável para Chat. Candidato não preferido aguarda 1500 ms ou chegada do preferido. Atraso maior permite Chat assumir e PDV recebe deduplicated depois. **A prioridade vale para candidatos nessa janela; não é garantia com realtime/poll atrasado 30 s.** Não há heartbeat/presença nem espera de 30 s. Recusa segura pode trocar uma vez para alternativa já recebida; resultado incerto jamais troca.

Sucesso: `status:spooled|deduplicated`, mode, printer e arbitration com source real, orderId, purpose e duplicate. Replay do journal tem `printer:null` para não persistir identidade de impressora. Cancelar HTTP não cancela tarefa/reserva nativa aceita.

Sem intent automatic mantém impressão manual legada. jobId opcional (até 128 caracteres) identifica intenção por source/loja: mesmo conteúdo é deduplicado, outro conteúdo retorna JOB_ID_CONFLICT. Segunda via explícita é mode manual + novo jobId. Manual sem jobId não tem chave de dedupe; SDK gera UUID quando disponível.

SDK exige capacidades canonicalAutoPrint e persistentPrintDeduplication para automatic. Nativo antigo/ausente gera AUTO_PRINT_COORDINATION_REQUIRED/retrySafe:false, sem POST/fallback automático. Depois de POST, resposta perdida/opaca é UNKNOWN. Conferir saída e emitir segunda via conscientemente. Spooled confirma submissão, sem comprovar papel/corte/entrega; chamadas Windows podem bloquear conforme driver/rede. [StartDocPrinter](https://learn.microsoft.com/en-us/windows/win32/printdocs/startdocprinter), [EndDocPrinter](https://learn.microsoft.com/en-us/windows/win32/printdocs/enddocprinter).

`/connect` mantém a lista de origens publicada na 0.1.4. “Desconectar navegadores” remove tokens e desativa autoConnect duravelmente; `/pair` continua. Reativação exige controle local; HTTP não altera essa permissão. ZeloMenu não ganhou acesso direto.

## Retenção e recuperação

Journal guarda hashes de chave/fingerprint, estado, source, mode e timestamp; não guarda cupom, cliente, impressora ou UUID em claro. Append + Flush(true) precede delegate físico. Reserva inconclusa continua incerta após restart. Último append parcial preserva reserva anterior; outras corrupções/falhas bloqueiam novas submissões com chave.

Retenção sete dias, capacidade inicial 10 mil, ampliável localmente de 10 em 10 mil até 50 mil, sem apagar registros vigentes. Ao lotar, novas impressões com chave, **inclusive manuais**, recebem PRINT_HISTORY_FULL; duplicatas confirmadas continuam reconhecidas. Não há limpeza em massa. Ampliar ou aguardar expiração; corrupção exige análise local, sem apagar histórico para destravar. Cache quente: mil entradas/uma hora, sem apagar journal ao expirar. Arquivo tem limite de leitura de 64 MiB e compactação.

A proteção é local a instalação/perfil Windows e por sete dias. Não coordena computadores diferentes, nem resiste à exclusão/perda do histórico, relógio incorreto ou armazenamento que viole flush. Não há exactly-once físico. Reserva seguida de falha anterior ao spool pode exigir conferência mesmo sem papel: escolha conservadora para evitar duplicação.

## Validação e performance

| Procedimento | Resultado |
| --- | --- |
| npm test | 38 JS + 33 nativos passam |
| Harness self-contained win-x64 | 33/33 em .NET 8.0.27 |
| npm run dotnet:publish:win | passou, Release self-contained, sem erros/avisos no publish |
| npm run sdk:prepare | browser e manifest 0.2.0 |
| npm audit / NuGet vulnerable include-transitive | zero alertas reportados |
| git diff --check | sem erro, somente avisos LF/CRLF |
| ISCC 6.7.3 | instalador compilado sem avisos |
| Install/uninstall isolados | exit 0/0, payload idêntico, binário removido, zero processos Printer |

Testes usam diretórios/portas temporários, catálogo/delegate físico falsos, Kestrel HTTP, pipe real e bitmap. Cobrem três candidatos, segunda via, outro owner, reinício, timeout/abort, falhas seguras/incertas, fila/histórico cheios, corrupção/append parcial/expiração e tokens. Não alteram config/startup do usuário nem chamam spooler físico.

A primeira execução do workflow na branch (`33932679489`) falhou antes dos testes: Node 20 no Windows não expande `tests/*.test.mjs`. O script passou a enumerar os dois arquivos explicitamente, mantendo o mesmo conjunto de testes. O workflow também foi alinhado ao Node 24 e checkout/setup-node v7 dos demais produtos. O commit final passou na branch (`33932910059`) e na publicação da tag (`33933244243`).

Benchmark: publicar `native/ZeloImpressao.Tests` self-contained com `-r win-x64`, executar `release/tests/ZeloImpressao.Tests.exe --benchmark`. Log `release/tests-runtime8-final-0.2.0.log`. Windows x64, .NET 8.0.27, host aquecido, 10 warmups e 500 health sequenciais; não mede browser, rede externa, tray cold start ou impressora física.

| Métrica | Resultado |
| --- | --- |
| Start host aquecido | 3,66 ms |
| Health p50 / p95 / máximo | 3,06 / 4,34 / 5,90 ms |
| CPU em 3 s após assentamento | 0 ms acumulados |
| RSS / heap do harness após suíte | 126,36 / 21,14 MiB |
| WMI primeira / quatro seguintes | 229,36 / 73,67–75,25 ms |
| Dispositivos enumerados | 2, nenhum recebeu job |
| 100 submissões duráveis falsas p50 / p95 / máximo | 1,50 / 2,09 / 2,85 ms |
| Journal dessas intenções | 51.482 bytes |
| Executável single-file | 83.082.262 bytes |

WMI é o maior custo local medido fora do hardware. Não se adicionou cache que esconda hot-plug/offline. Não há baseline equivalente de RAM/tempo para provar ganho percentual. Driver travado ainda ocupa executor serial; limite de fila não torna Winspool cancelável.

## Pacote e limites de homologação

Inno estava ausente. Instalador oficial 6.7.3 conferido contra SHA256 do catálogo winget `9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732` e Authenticode válido de Pyrsys B.V. Ferramenta de build instalada no perfil atual em `release/toolchain/InnoSetup`; não instalou Printer operacional.

Artefato reconstruído: `release/installer/Zelo-Impressao-0.2.0-Setup.exe`, **78.486.172 bytes**, SHA256 **`0D04B1CC03E9915CE5DA5DC248E8D09F3B384D700634336F8EFCA90D7A571D78`**. **NotSigned**: nenhum certificado fornecido. Não se copiou instalador antigo/live para simular build.

Artefato público construído pelo CI: `Zelo-Impressao-0.2.0-Setup.exe` e alias `Zelo-Impressao-Setup.exe`, ambos **78.488.107 bytes**, SHA256 **`512a4c4a1db5be8faee85125192ef47c16cc6f6f0982f01766528c4a3be97305`**, confirmados nos metadados de assets do GitHub. O hash do build local acima continua sendo sua própria evidência de smoke; ele não é o hash do instalador distribuído.

Smoke usa mesmo payload/config com AppId, mutex, Run e pasta próprios. Silencioso não abre app; uninstall remove binário. Evidência `release/installer-final-validation.json` e `release/installer-smoke-final-*.log`. Valida empacotamento/instalação isolada; **não valida upgrade operacional 0.1.4, Windows limpo, login/startup, SmartScreen ou térmica real**. Installer não força taskkill e preserva config/histórico na desinstalação.

Inspeção local posterior à publicação, somente leitura: nenhum registro de instalação Zelo em HKCU/HKLM (incluindo 32 bits), nenhum processo Zelo e nenhum job em `Win32_PrintJob`. Binário ausente dos diretórios padrão. Existe configuração antiga em `%APPDATA%\Zelo Impressao` e entrada de inicialização `Zelo Impressao` apontando para o binário ausente em `%LOCALAPPDATA%\Programs\Zelo Impressao`. Essa entrada não foi alterada; nenhum instalador operacional foi executado. Uma eventual reinstalação deve preservar/revisar a configuração existente.

## Pendências reais

1. Homologar térmica USB/rede com falta de papel, desconexão, erro parcial e desligamento, conferindo papel/fila Windows. Driver/corte e segunda via exigem teste físico.
2. Concluir atualização operacional dos agentes Windows e homologação dos clientes publicados em conjunto. A release 0.2.0 já está disponível; sua publicação não atualiza instalações existentes automaticamente.
3. Assinar com certificado de distribuição, testar Windows limpo e upgrade 0.1.4 → 0.2.0 preservando config/tokens/journal, startup e janela via atalho.
4. Validar Chrome/Edge/PWA, CSP e permissão de rede local publicados, sem desligar proteções globais. HTTP/CORS testados; navegador não usado nesta etapa. [Chrome Local Network Access](https://developer.chrome.com/blog/local-network-access).
5. Medir RSS/WMI/spooler durante turno em máquina de balcão antes de decidir cache/isolamento. HTML continua texto extraído: não há fidelidade de imagens/tabelas/CSS; usar texto/RAW para cupons.

ESM/IIFE mantêm implementações semelhantes cobertas pela mesma matriz. Não houve redesign, mudança comercial ou impressão real. A publicação foi conduzida pelo coordenador; o agente Printer não instalou o programa operacional neste PC.
