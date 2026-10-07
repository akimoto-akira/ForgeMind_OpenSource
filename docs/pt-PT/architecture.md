# Arquitetura ForgeMind

ForgeMind é uma **plataforma de controlo de motores de jogos baseada em MCP**: o servidor (host) fornece ferramentas aos clientes de IA através do protocolo MCP; as funcionalidades das ferramentas são maioritariamente injetadas por plugins. No lado do motor, o plugin adaptador recebe operações através de um bridge TCP local. UE é a implementação atual; a plataforma é extensível a outros motores. Este documento baseia-se no código visível na parte **pública**; as funcionalidades comerciais são descritas apenas de forma geral em §7.

## 1. Visão geral

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ Cliente de IA│ ◄────────────────► │ ForgeMindServer (host)│
└─────────────┘                     │  • Ferramentas incorporadas ×4│
                                    │  • PluginLoader             │
                                    │  • Servidor bridge TCP      │
                                    │  • Registo de motores      │
                                    └──────────┬───────────┘
                                               │ Bridge TCP local (prefixo de 4 bytes + envelope JSON)
                                    ┌──────────▼───────────┐
                                    │ Plugin adaptador UE   │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): núcleo do servidor MCP.
- **Plugins** (`OpenSource\ForgeMind.*Plugin`): registam ferramentas através de `IPlugin`. O host contém apenas um conjunto mínimo de ferramentas.
- **Adaptador de motor** (`UEProject\...\Plugins\ForgeMindForUE`): plugin no editor. Implementa as funcionalidades do lado do motor como servidor bridge.
- **Scan/empacotamento** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): fornecem o índice offline de funcionalidades do motor e o empacotamento SPK.
- **Projeto de exemplo** (`UEProject\ForgeMind_5_0`): para testes e depuração conjuntos.

## 2. Host (ForgeMindServer)

- **Ponto de entrada**: `Program.cs` inicia por predefinição um serviço HTTP residente em `127.0.0.1:8765` (Streamable HTTP + compatibilidade SSE). `--stdio` alterna para o modo spawn-per-client. A porta pode ser substituída por `--port` / `FORGEMIND_PORT`.
- **Ferramentas incorporadas** (tabela de ferramentas `ForgeMindServerHost`, apenas 4):
  - `settings` (`SettingsTool`, leitura/escrita de configuração);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (consulta e limpeza de tarefas longas).
- **Carregamento de plugins**: `PluginLoader` analisa `plugins\` (predefinição `<pasta do host>\plugins\`, substituível por `FORGEMIND_PLUGINS_DIR`) e injeta a tabela de ferramentas, o registo de motores e o bridge através de `PluginContext`. O plugin `OnLoad` chama `RegisterTool` para injetar ferramentas. Os pacotes de plugins suportam dois layouts: «subpasta = pacote de plugin» (DLL principal + manifest com o mesmo nome + dependências) e «DLLs soltas na raiz». O carregamento é preguiçoso (ativado no primeiro acesso à tabela de ferramentas); em caso de erro, apenas o plugin em causa é ignorado, o host não é afetado.
- **Registo de motores**: `EngineRegistry` enumera os motores locais no arranque (preferência por instalações Launcher `UE_x.y`, fallback para compilações de fonte) e injeta um snapshot só de leitura aos plugins.
- **Servidor bridge TCP**: `ForgeMindServerBridgeServer`. Para descoberta e ligação do adaptador de motor (§5).

## 3. Plugins e injeção de ferramentas

| Plugin | Ferramentas | Responsabilidade |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Gestão de motores e projetos, arranque/compilação/operações de editor |
| `ForgeMind.Assets` | `asset` | Leitura offline da estrutura de `.uasset` (`UAssetArchive` / `BlueprintGraphReader`, etc.) |
| `ForgeMind.Knowledge` | `knowledge` | Pesquisa de conhecimento (fonte de pasta / fonte de pacote de índice, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (ferramenta CLI, não MCP) | Enumeração de motores → scan `UnrealScanner` → empacotamento `.spk` via `ScanResultPacker` |

O host e os plugins cooperam através do contrato `ITool` (`Compat\ITool.cs`): nome/descrição/esquema de parâmetros/`Execute` devolve `ToolResult`. O `ForgeMindServerTest` também chama ferramentas diretamente através de `ITool` (sem protocolo MCP) para verificar o comportamento.

## 4. Scan e SPK (base das funcionalidades offline)

- `ForgeMind.Scan.Abstractions`: abstrações `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures`, etc.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore`, etc.) — analisa ficheiros de cabeçalho UE, ficheiros de regras e ativos Blueprint num índice de scan. O `ScanIndexStore` é um índice em memória em tempo de execução e não é gravado em disco. Isto **distingue-se do antigo ScanCache** — o pipeline atual não usa o pré-carregamento/cache distribuído do ScanCache (restam apenas vestígios históricos de compatibilidade no código; não constituem o mecanismo atual).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` constroem e leem pacotes de scan.
- `ForgeMind.Speedy`: `SpeedyPack` fornece o formato de empacotamento de alto desempenho (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, com CRC interno, mapeamento de diretórios, registo pré-escrita `WriteAheadLog`, etc.).
- Utilização: `ForgeMind.EngineScanner <spkPath>` gera `.spk`. Funcionalidades como knowledge consomem-no através de `SpkScanDataProvider`.

## 5. Protocolo bridge e adaptador de motor

- Bridge: TCP local. As mensagens usam prefixo de 4 bytes de comprimento + envelope JSON (`BridgeMessage` / `BridgeSession` / `FrameCodec`). O adaptador lê primeiro `%USERPROFILE%\.forgemind\bridge.json` para descoberta e autenticação, depois ligação → handshake → ciclo de mensagens.
- No lado do servidor, o encaminhamento baseia-se no nome do comando para a implementação do adaptador. Os handlers de comandos são executados no thread principal do motor (thread do jogo UE) para garantir consistência com o estado do editor.
- O adaptador de motor (lado UE) está em `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, módulo Editor com cliente bridge e aplicador IR. O projeto UE de exemplo é compatível com 5.0–5.8.
- Para adicionar um novo motor, basta implementar um cliente bridge isomórfico e os handlers de comandos (`development-guide.md` §4). Não são necessárias alterações no host ou nos plugins.

## 6. Licença e segurança (parte pública)

- Autorização predefinida: `OpenAuthorizationService` (`Authorization\Open\`) — funcionalidades fixas de community, **não analisa ficheiros de licença**.
- Segurança de plugins: `PluginIntegrityVerifier` (verificação de integridade de manifests), `PluginSigningKeys` (verificação de assinaturas); `ManifestLoader` analisa os manifests dos plugins e suporta ServiceOverride (substituição de serviços do host por tipos abstratos, ex. `IAuthorizationService`).
- Autorização bridge: as ligações dos adaptadores devem ler as credenciais locais de `bridge.json` para concluir o handshake (`security.md`).

## 7. Funcionalidades comerciais (visão geral)

- **Linguagem DSL e tradutores de domínio** (código fechado): funcionalidades comerciais que geram ativos de motor a partir de texto legível. A parte pública contém apenas a camada de framework DSL (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, interfaces e registos em `ForgeMindServer\DSL\`); a sintaxe concreta, os tradutores de domínio e as regras IR são fornecidos pelos módulos comerciais e não estão no repositório.
- **Sistema de licença comercial** (código fechado): inclui emissão de licenças, verificação de licenças, serviço de licenças e ligação opcional ao ambiente. A implementação concreta, os algoritmos de assinatura, o formato dos ficheiros de licença e a lógica do lado do servidor não fazem parte da parte pública e são apresentados externamente apenas como «fornecer funcionalidades adicionais».

> A versão open source oferece um conjunto completo de ferramentas open source e funcionalidades de bridge. A versão Commercial baseia-se no mesmo host, mecanismo de plugins e protocolo bridge e estende as funcionalidades comerciais (geração de ativos DSL, licenças comerciais, suporte enterprise).

## 8. Tabela de limites de funcionalidades

| Funcionalidade | Pertinência |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Interfaces de extensão DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Regras IR | Commercial 🔒 |
| Gerador de ativos DSL | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Implementação de licenças comerciais | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Sistema de chaves/assinaturas | Commercial 🔒 |
| Detalhes internos do protocolo de licença | Commercial 🔒 |
