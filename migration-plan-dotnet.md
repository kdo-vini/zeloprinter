# Migração Electron → .NET — decisão histórica

A migração de implementação está concluída. O componente é .NET 8/WinForms + ASP.NET Core + Winspool/PrintDocument; Electron não faz parte do repositório.

A decisão preservou API local, pareamento, SDK e fallback operacional, adequando o runtime ao Windows de balcão e às impressoras instaladas.

Build e publish self-contained foram executados no Windows em 2026-09-04. Impressoras físicas e instalação limpa permanecem pendentes. Consulte [a auditoria corrente](docs/audits/2026-09-04-zeloprinter.md) e [o procedimento de build](native/README.md), sem duplicar outro checklist neste histórico.
