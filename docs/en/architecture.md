# ForgeMind Architecture

ForgeMind is a **game engine control platform with MCP as its interface**: the server (host) exposes tools to AI clients through the MCP protocol, with tool capabilities mainly injected by plugins; on the engine side, adapter plugins carry out operations over a local TCP bridge. UE is the current implementation, and the platform capabilities are extensible to other engines. This document describes the code visible in the **public slice**; commercial capabilities are only outlined in §7.

## 1. Overall View

```
┌─────────────┐   MCP(HTTP/stdio)   ┌──────────────────────┐
│ AI Client    │ ◄────────────────► │ ForgeMindServer(Host) │
└─────────────┘                     │  • Built-in tools ×4  │
                                    │  • PluginLoader       │
                                    │  • TCP bridge server  │
                                    │  • Engine registry    │
                                    └──────────┬───────────┘
                                               │ Local TCP bridge (4-byte length + JSON envelope)
                                    ┌──────────▼───────────┐
                                    │ UE Adapter Plugin     │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): the MCP server itself.
- **Plugins** (`OpenSource\ForgeMind.*Plugin`): register tools through `IPlugin`; the host only bundles a minimal toolset.
- **Engine Adapter** (`UEProject\...\Plugins\ForgeMindForUE`): an in-editor plugin that implements engine-side capabilities as the bridge server.
- **Scan / Packaging** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): provide indexing and SPK packaging for engine offline capabilities.
- **Sample Project** (`UEProject\ForgeMind_5_0`): for testing and joint debugging.

## 2. Host (ForgeMindServer)

- **Entry**: `Program.cs` runs by default as an HTTP resident service on `127.0.0.1:8765` (Streamable HTTP + SSE compatible); `--stdio` switches to spawn-per-client mode; the port can be overridden by `--port` / `FORGEMIND_PORT`.
- **Built-in tools** (tool table of `ForgeMindServerHost`, only 4):
  - `settings` (`SettingsTool`, reads/writes settings);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (long-running task queries and cleanup).
- **Plugin loading**: `PluginLoader` scans `plugins\` (default `<host directory>\plugins\`, overridable by `FORGEMIND_PLUGINS_DIR`), injects the tool table, engine registry and bridge via `PluginContext`; the plugin's `OnLoad` calls `RegisterTool` to inject tools. Plugin packages support two layouts: "subdirectory = plugin package" (main DLL + same-named manifest + dependencies) and "loose DLLs in root". Loading uses lazy evaluation (triggered on first access to the tool table); a failure only skips that plugin and does not affect the host.
- **Engine registry**: `EngineRegistry` enumerates local engines at startup (prefers Launcher-installed `UE_x.y`, falls back to source builds) and injects a read-only snapshot into plugins.
- **TCP bridge server**: `ForgeMindServerBridgeServer`, for engine adapters to discover and connect (see §5).

## 3. Plugins and Tool Injection

| Plugin | Provided tools | Responsibility |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Engine and project management, launch/build/editor operations |
| `ForgeMind.Assets` | `asset` | Offline reading of `.uasset` structure (`UAssetArchive` / `BlueprintGraphReader` etc.) |
| `ForgeMind.Knowledge` | `knowledge` | Knowledge retrieval (folder source / index pack source, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (command-line tool, not an MCP tool) | Enumerates engines → `UnrealScanner` scans → `ScanResultPacker` packages `.spk` |

The host and plugins interact through the `ITool` contract (`Compat\ITool.cs`): name / description / parameter Schema / `Execute` returning `ToolResult`. `ForgeMindServerTest` also invokes tools directly through the same `ITool` (without the MCP protocol) to verify behavior.

## 4. Scan and SPK (Offline Capability Foundation)

- `ForgeMind.Scan.Abstractions`: `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` and other abstractions.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore`, etc.) — parses UE header files, rule files and blueprint assets into a scan index. `ScanIndexStore` is a runtime in-memory index and is not persisted to disk; it is **not equivalent to the legacy ScanCache** — the current pipeline no longer uses ScanCache preloading / scattered caches (the source retains only a small amount of historical compatibility traces, which do not constitute the current mechanism).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` assemble and read scan packages.
- `ForgeMind.Speedy`: `SpeedyPack` provides a high-performance packaging format (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, internally including CRC, directory mapping, write-ahead log `WriteAheadLog`, etc.).
- Usage: `ForgeMind.EngineScanner <spkPath>` outputs `.spk`; capabilities such as knowledge consume it through `SpkScanDataProvider`.

## 5. Bridge Protocol and Engine Adapter

- Bridge: local TCP; messages are a 4-byte length prefix + JSON envelope (`BridgeMessage` / `BridgeSession` / `FrameCodec`). The adapter first reads `%USERPROFILE%\.forgemind\bridge.json` for discovery and authentication, then connects → handshake → message loop.
- On the server side, commands are dispatched to adapter implementations by command name; command handlers run on the engine main thread (UE game thread), ensuring consistency with editor state.
- The engine adapter (UE side) is located at `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`: an Editor module containing the bridge client and IR applier. The UE sample project is compatible with 5.0–5.8.
- To add a new engine, only implement an isomorphic bridge client and command handlers (see `development-guide.md` §4); the host and plugins need no changes.

## 6. Licensing and Security (Publicly Visible Parts)

- Default authorization: `OpenAuthorizationService` (`Authorization\Open\`) — community fixed capability, **does not parse license files**.
- Plugin security: `PluginIntegrityVerifier` (manifest integrity check), `PluginSigningKeys` (signature verification); `ManifestLoader` parses plugin manifests and supports ServiceOverride (replacing host services by abstract type, e.g. `IAuthorizationService`).
- Bridge authentication: adapters must read the local `bridge.json` credentials to complete the handshake (see `security.md`).

## 7. Commercial Capabilities (Overview)

- **DSL language and domain translators** (closed source): a commercial capability that generates engine assets from human-readable text. The public slice contains only the DSL framework layer (the `ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter` interfaces and registries under `ForgeMindServer\DSL\`); the concrete syntax, domain translators and IR rules are provided by commercial modules and are not in the slice.
- **Commercial licensing system** (closed source): includes license issuance, license verification, licensing services and optional environment binding. The concrete implementation, signature algorithms, license file format and server-side logic are not part of the public slice; only "provides additional capabilities" is presented externally.

> The OpenSource version provides the full open-source toolset and bridge capabilities; the Commercial version extends commercial capabilities (DSL asset generation, commercial licensing and enterprise support) based on the same host, plugin mechanism and bridge protocol.

## 8. Capability Boundary Table

| Capability | Ownership |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| DSL extension interfaces | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR rules | Commercial 🔒 |
| DSL Asset Generator | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Commercial licensing implementation | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Private keys / signing system | Commercial 🔒 |
| Internal details of the licensing protocol | Commercial 🔒 |
