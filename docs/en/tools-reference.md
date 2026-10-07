# ForgeMind Tools Reference

All tools are divided into "host built-in" and "plugin-injected". The current OpenSource release slice contains 13 MCP tools; commercial plugins can provide additional tools on top of this (such as DSL asset generation, see §3, not in the open-source list).

## 1. Host Built-in Tools

The host (`OpenSource\ForgeMindServer`) bundles the following tools, no plugin required:

| Tool | Description |
|---|---|
| `settings` | Reads/writes server settings (`McpSettingsManager`, changes are persisted immediately) |
| `get_task_result` | Queries the execution result of long-running tasks (launch / build etc.) |
| `list_executing_tasks` | Lists currently in-progress long-running tasks |
| `cleanup_tasks` | Cleans up finished task records |

## 2. Plugin-Injected Tools

Plugins inject tools via `PluginContext.RegisterTool` in `OnLoad`; they are visible to clients after connection. After building, place the plugin DLL + same-named `.manifest.json` into the host `plugins\` directory (or the directory specified by `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7 tools)

| Tool | Description |
|---|---|
| `engine` | Queries locally installed engines (Launcher / source builds, including versions and app names) |
| `project` | Queries / manages UE projects (locates project files, views project information) |
| `project_files` | Lists project files (filtered by directory / type) |
| `switch_version` | Switches a project to a specified engine version (via UnrealVersionSelector) |
| `launch` | Launches the editor or a packaged executable (supports debug configurations) |
| `build` | Builds the project (default target / Editor target) |
| `editor` | In-editor operations (scene, viewport, assets, etc., dispatched over the bridge) |

Parameter Schemas are in each tool's source `GetParameterSchema()` (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Some engine operations are subject to `ServerPermissionChecker` permission constraints.

### ForgeMind.Assets (1 tool)

| Tool | Description |
|---|---|
| `asset` | Reads `.uasset` structure offline (names, objects, properties, blueprint graphs, supports querying by graph/property) |

The implementation is based on `UAsset\UAssetSerializer` / `BlueprintGraphReader` etc. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1 tool)

| Tool | Description |
|---|---|
| `knowledge` | Knowledge retrieval: queries project / engine knowledge entries (sources: folder source `FolderKnowledgeSource`, SPK index pack source `IndexPackSource`) |

Implementation: `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Commercial Edition Tools (Overview)

The commercial edition can add DSL asset generation and related query tools, injected by commercial plugins; specific tool names, APIs and DSL capabilities are subject to the commercial edition documentation. OpenSource does not include DSL tools.

## 4. Tool Calling Conventions

- Long-running task tools return a task ID; poll the result with `get_task_result`.
- Tool failures return a `ToolResult` failure message (can be shown directly to the client).
- Tool descriptions and parameter Schemas are exactly what MCP `tools/list` shows, kept consistent with the source code.
