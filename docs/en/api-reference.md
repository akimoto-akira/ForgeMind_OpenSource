# ForgeMind API Reference

This document describes the public parts of the ForgeMind external interface: MCP protocol access, bridge protocol, tool contracts, and the long-running task mechanism. Commercial capabilities (DSL) are only outlined in §6.

## 1. MCP Access

- **HTTP mode** (default): `http://127.0.0.1:8765/mcp`, Streamable HTTP (backward compatible with legacy SSE). The host process is resident; one connection can span multiple client sessions.
- **stdio mode**: `ForgeMindServer.exe --stdio`, spawn-per-client.
- Port: `--port N` > environment variable `FORGEMIND_PORT` > default 8765.

Supports standard MCP methods: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled`, etc.; tool Schemas are provided by `ITool.GetParameterSchema()`.

## 2. Tool Contracts

Server tools implement a unified interface (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: tool identifier and display name;
- `Description`: description text aimed at AI clients;
- `GetParameterSchema()`: parameter declaration (JSON Schema style);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: executes and returns a `ToolResult` (success result or failure message).

Call flow: client `tools/call` → host locates the `ITool` → parameter validation → `Execute` → result returned. Tools involving engine-side capabilities dispatch commands internally over the TCP bridge.

## 3. Bridge Protocol (Host ↔ Engine Adapter)

- Transport: local TCP; frame format = 4-byte length prefix + JSON envelope (`BridgeMessage`, see `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Discovery and authentication: the adapter reads `%USERPROFILE%\.forgemind\bridge.json` (containing host, port and credentials), connects → handshake → message loop.
- Command semantics: requests contain a command name and parameters; the server routes commands to adapter implementations; responses contain a status code, message and data. Engine-side commands execute on the engine main thread (UE game thread).
- Lifecycle: the host listens on the bridge as soon as it starts; the adapter reconnects with exponential backoff on disconnect.

## 4. Long-Running Task Mechanism

- Long-running tasks (such as `launch`, `build`) execute asynchronously and do not block tool calls.
- Related built-in tools:
  - `get_task_result`: queries task results;
  - `list_executing_tasks`: lists in-progress tasks;
  - `cleanup_tasks`: cleans up task records.
- The internal `LongTaskManager` manages the task lifecycle (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Built-in Tools (bundled with the host, no plugin required)

| Tool | Description |
|---|---|
| `settings` | Reads/writes server settings (`McpSettingsManager`, changes are persisted immediately) |
| `get_task_result` | Queries long-running task results |
| `list_executing_tasks` | Views in-progress tasks |
| `cleanup_tasks` | Cleans up task records |

The full list of plugin-injected tools is in `tools-reference.md`.

## 6. Commercial Capabilities (DSL, Overview)

- OpenSource provides the **DSL extension base contracts** (`ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` and other abstract interfaces, which remain publicly usable); the concrete DSL syntax implementation, Parser, Translator, IR rules and asset generation logic belong to Commercial and are not shipped with OpenSource.
- DSL tools are injected by commercial plugins; specific tool names and APIs are subject to the commercial edition documentation; the OpenSource tool list does not include DSL tools.

| Capability | Ownership |
|---|---|
| DSL extension interfaces | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR rules | Commercial 🔒 |
| Asset generation logic | Commercial 🔒 |

## 7. Compatibility Notes

- The bridge protocol frame format and tool Schemas follow this document; breaking changes are announced with a migration notice first.
