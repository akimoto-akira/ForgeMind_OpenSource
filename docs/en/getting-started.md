# ForgeMind Getting Started

ForgeMind is a **game engine control platform with MCP as its interface**: it exposes Unreal Engine project management, asset reading, launch/build/editor operations and other capabilities to AI clients through the MCP protocol; the current implementation targets UE and the platform is extensible. The OpenSource version covers the engine toolset and bridge capabilities; the commercial version adds DSL asset generation and more (see §5).

All steps in this guide can be completed with the **open-source slice** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Environment Requirements

- .NET SDK (for host and plugin builds).
- Unreal Engine 5.0–5.8 (including the editor); the sample project is `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- An MCP-capable client (Claude Desktop / Cursor / custom clients all work).

## 2. Building the Server and Plugins

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugins (after building, place the DLL + same-named .manifest.json into the host plugins directory)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Do not build the entire `ForgeMind.sln` directly (it references commercial projects, and the commercial directory is not shipped with the slice). Plugins can also be provided by the publisher as prebuilt artifacts.

## 3. Launch

1. **Start the server** (default HTTP resident, port 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Or run the build output ForgeMindServer.exe; use --port N / --stdio / FORGEMIND_PORT to adjust
   ```

2. **Open the UE editor and the sample project**: launch `ForgeMind_5_0.uproject` with UE; the `ForgeMindForUE` plugin inside the editor automatically starts the bridge client and connects to the host.

3. **Configure the client**: add the server in your MCP client:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   After connecting, the tool list is visible (4 built-in + plugin-injected).

## 4. First Use

- Use the `engine` tool to view current engine information;
- Use `project` / `project_files` to view the sample project and its files;
- Use `launch` to start the editor and `editor` for editor operations;
- Use `asset` to read the project asset structure;
- Use `knowledge` to retrieve project knowledge.

> Long-running tasks (launch/build etc.) run asynchronously; use `get_task_result` to query results, `list_executing_tasks` to view in-progress tasks, and `cleanup_tasks` to clean up.

## 5. Commercial Edition (DSL)

The Commercial module is a separate commercial release and is not included in the OpenSource repository. The commercial edition provides **DSL asset generation**: describing blueprints, materials and other content in human-readable text and converting them into engine assets; the specific DSL tools, text conversion capabilities and syntax details are subject to the commercial edition documentation. Commercial licensing capabilities also belong to Commercial. OpenSource does not include DSL tools.

## 6. Next Steps

- Architecture and mechanisms: `architecture.md`
- Tool list and parameters: `tools-reference.md`
- Development and contribution: `development-guide.md` / `contributing.md`
- FAQ: `troubleshooting.md`
