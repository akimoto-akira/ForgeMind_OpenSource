# ForgeMind Development Guide

For developers who want to extend ForgeMind: how to build, debug, add MCP tools, add engine adapters, and run tests. This document is written based on the real code in the **public slice** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Project Overview

| Project | Responsibility |
|---|---|
| `OpenSource\ForgeMindServer` | MCP server host: 4 built-in tools, starts the TCP bridge, loads plugins |
| `OpenSource\ForgeMind.OfflineEngineKit` | Plugin: engine / project / project_files / switch_version / launch / build / editor tools |
| `OpenSource\ForgeMind.Assets` | Plugin: asset tool (offline `.uasset` reading) |
| `OpenSource\ForgeMind.Knowledge` | Plugin: knowledge tool (knowledge retrieval) |
| `OpenSource\ForgeMind.EngineScanner` | Command-line scanner: enumerates engines and packages `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Scan indexing: abstractions, packaging, UE implementation (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | SPK packaging format (high-performance pack files) |
| `tests\ForgeMind.PublicTests` | Public unit tests (no engine dependency) |
| `tests\ForgeMindServerTest` | Server integration tests (requires UE editor) |
| `UEProject\ForgeMind_5_0` | Sample UE project (5.0–5.8) |

## 2. Build

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugin and library projects: see contributing.md §3 (do not build the whole ForgeMind.sln directly; it contains commercial project references)
```

- Host entry: `OpenSource\ForgeMindServer\Program.cs` (HTTP resident default `127.0.0.1:8765`, `--stdio` switches, `--port` / `FORGEMIND_PORT` override the port).
- Logging: `LogManager`; tools return `ToolResult` (success / failure with message).

## 3. Plugin Mechanism and Adding MCP Tools

The host only has 4 built-in tools: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. The rest are injected by **plugins in the plugins directory**:

- Plugin directory: `<host directory>\plugins\` (overridable by the environment variable `FORGEMIND_PLUGINS_DIR`).
- Plugin package layout: put the main DLL + same-named `.manifest.json` + dependencies in `plugins\<plugin name>\`; loose DLLs in the root directory are also supported (manual drop-in).
- The host `PluginLoader` scans plugins → `PluginContext` injects (tool table, engine registry, bridge) → the plugin's `OnLoad` calls `RegisterTool` to register tools.
- Plugin integrity: `PluginIntegrityVerifier` / `PluginSigningKeys` handle plugin manifest and signature verification; trusted plugins can extend services via ServiceOverride (see §7).

To add a new MCP tool:

1. Implement the `ITool` contract (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: tool name and display name;
   - `Description`: description aimed at AI clients;
   - `GetParameterSchema()`: JSON Schema style parameter declaration;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: returns `ToolResult`.
2. Create a new plugin project (or join an existing plugin, e.g. OfflineEngineKit), write an `IPlugin` implementation, and call `RegisterTool` in `OnLoad`.
3. Place a same-named `.manifest.json` in the same directory (plugin description / dependency declaration).
4. When engine-side capability is needed, send it via a bridge command and implement the command handler on the adapter side (see §5).
5. Write test cases (§6) and update `tools-reference.md`.

## 4. How to Add an Engine Adapter

ForgeMind uses "adapters" to carry engine-specific logic; adding a new engine follows the same bridge protocol (4-byte length prefix + JSON envelope, see `api-reference.md`):

1. Implement a bridge client: read `%USERPROFILE%\.forgemind\bridge.json` for discovery and authentication, then connect → handshake → message loop.
2. Register whitelisted handlers by command name; all commands execute on the engine main thread (the UE game thread equivalent).
3. Implement engine-side capabilities (asset read/write, project operations, etc.) and map them to server tool returns.
4. Add test cases and documentation for that engine.

The first implementation is the UE adapter `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (Editor module plugin, bridge client + IR applier).

## 5. Engine-Side Bridge Commands (Current UE Implementation)

- The adapter-side `ForgeMindForUE` plugin contains the bridge client; command handlers execute on the game thread.
- Asset-level operations can reuse the in-plugin IR domain handlers, avoiding duplicated editor logic.
- Debugging: the `LogForgeMindBridge` category in UE editor logs; exponential backoff reconnect on disconnect.

## 6. Running Tests

### Public unit tests (no editor required)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Covers SPK packaging round-trip, multi-version SPK, scan data providers, etc. (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Server integration tests (requires UE editor + sample project)

```powershell
# Default: prebuilds the host and plugins, stages them into the plugins directory, starts the host to load plugins, and executes all ITestCase via reflection
ForgeMindServerTest.exe

# Filter: by group + order
ForgeMindServerTest.exe [group] [order]          # or --Test [group] [order]
# Skip prebuild
ForgeMindServerTest.exe --NoBuild
```

Mechanism:

- Prebuild: first builds `ForgeMindServer.csproj`, then discovers plugin projects with a same-named `.manifest.json` under the `ForgeMind.*` directories and builds each one, staging them to `<entry directory>\plugins\<plugin name>\`.
- Execution: discovers `ITestCase` via reflection, sorts by `Group` / `Order`, **fail-fast**; "launch editor / close editor" are also ordinary cases (`Order=0` launch, `Order=int.MaxValue` close), sharing a session within the same group.
- Cases invoke tools directly through `ITool.Execute` (not via the MCP protocol); tools communicate with UE through the bridge internally.
- Output: after execution, outputs a JSON report and a readable summary (output directory is shown in the run result message).
- Test assets: 30+ `Blueprint_*_Tests.cs` under `Task\UE5_0\` (blueprint domains: variables / functions / events / materials / macros / interfaces, etc.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` wraps bridge commands, text dispatch and IR snapshot comparison (prevents accidental modifications).

> Note: test cases involving "text paths (DSL)" depend on commercial DSL capabilities; the integration tests in the public slice mainly use bridge command paths, subject to the actual `Task\` cases in the slice.

## 7. Licensing and Commercial Capabilities (Overview)

- Default `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`): community fixed capability, does not parse any license files.
- Commercial licensing replaces the `IAuthorizationService` implementation via ServiceOverride in the plugin Manifest (host `ApplyServiceOverride`). The public slice does not contain that commercial plugin; it only retains the replacement mechanism (the host does not perceive the concept of "commercial", it only replaces by abstract type).

## 8. Coding Standards

- Header comments include the SPDX license identifier and copyright notice (Apache-2.0).
- Do not reference commercial modules; new plugins follow the slice boundary in `contributing.md`.
