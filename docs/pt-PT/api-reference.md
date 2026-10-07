# Referência de API ForgeMind

Este documento descreve a parte pública das interfaces externas do ForgeMind: ligação MCP, protocolo bridge, contratos de ferramentas e mecanismo de tarefas longas. As funcionalidades comerciais (DSL) são descritas apenas de forma geral em §6.

## 1. Ligação MCP

- **Modo HTTP** (predefinição): `http://127.0.0.1:8765/mcp`, Streamable HTTP (compatível com SSE antigo). O processo do host é residente; uma ligação suporta várias sessões de clientes.
- **Modo stdio**: `ForgeMindServer.exe --stdio`, é iniciado um processo separado por cliente.
- Porta: `--port N` > variável de ambiente `FORGEMIND_PORT` > predefinição 8765.

Métodos MCP padrão suportados: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled`, etc. O esquema das ferramentas é fornecido por `ITool.GetParameterSchema()`.

## 2. Contrato de ferramentas

As ferramentas do servidor implementam uma interface unificada (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: identificador e nome apresentado da ferramenta;
- `Description`: texto descritivo para o cliente de IA;
- `GetParameterSchema()`: declaração de parâmetros (estilo JSON Schema);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: devolve `ToolResult` após a execução (resultado de sucesso ou mensagem de erro).

Fluxo de chamada: cliente `tools/call` → host identifica `ITool` → validação de parâmetros → `Execute` → devolução do resultado. As ferramentas que necessitam de funcionalidades do motor enviam internamente comandos através do bridge TCP.

## 3. Protocolo bridge (host ↔ adaptador de motor)

- Transporte: TCP local. Formato de frame = prefixo de 4 bytes de comprimento + envelope JSON (`BridgeMessage`, ver `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Descoberta e autenticação: o adaptador lê `%USERPROFILE%\.forgemind\bridge.json` (contém host, porta e credenciais), executa ligação → handshake → ciclo de mensagens.
- Semântica de comandos: o pedido contém nome do comando e parâmetros. O servidor encaminha o comando para a implementação do adaptador; a resposta contém código de estado, mensagem e dados. Os comandos do lado do motor são executados no thread principal do motor (thread do jogo UE).
- Ciclo de vida: o host começa a escutar o bridge no arranque. O adaptador volta a ligar-se com backoff exponencial após a desligação.

## 4. Mecanismo de tarefas longas

- As tarefas longas (`launch`, `build`, etc.) são executadas de forma assíncrona e não bloqueiam a chamada da ferramenta.
- Ferramentas incorporadas relacionadas:
  - `get_task_result`: consulta do resultado da tarefa;
  - `list_executing_tasks`: lista das tarefas atuais;
  - `cleanup_tasks`: limpeza dos registos de tarefas.
- Internamente, o `LongTaskManager` gere o ciclo de vida das tarefas (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Ferramentas incorporadas (parte do host, sem plugins)

| Ferramenta | Descrição |
|---|---|
| `settings` | Leitura/escrita da configuração do servidor (`McpSettingsManager`, persistência imediata) |
| `get_task_result` | Consulta do resultado de tarefas longas |
| `list_executing_tasks` | Consulta das tarefas atuais |
| `cleanup_tasks` | Limpeza dos registos de tarefas |

A lista completa das ferramentas injetadas pelos plugins está em `tools-reference.md`.

## 6. Funcionalidades comerciais (DSL, visão geral)

- O OpenSource fornece **contratos de extensão DSL** (interfaces e registos `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` em `ForgeMindServer\DSL\` são públicos). As implementações concretas de sintaxe DSL, Parser, Translator, regras IR e lógica de geração de ativos pertencem ao Commercial e não estão no OpenSource.
- As ferramentas DSL são injetadas pelo plugin comercial. Os nomes e APIs concretos estão sujeitos à documentação comercial. A lista de ferramentas do OpenSource não contém nenhuma ferramenta DSL.

| Funcionalidade | Pertinência |
|---|---|
| Interfaces de extensão DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Regras IR | Commercial 🔒 |
| Lógica de geração de ativos | Commercial 🔒 |

## 7. Notas de compatibilidade

- O formato de frame do protocolo bridge e os esquemas das ferramentas estão sujeitos a este documento. Alterações que quebrem a compatibilidade serão anunciadas com antecedência com uma nota de migração.
