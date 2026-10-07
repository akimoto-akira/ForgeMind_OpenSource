# ForgeMind 快速上手

ForgeMind 是一个**以 MCP 为界面的游戏引擎控制平台**：通过 MCP 协议把 Unreal Engine 的工程管理、资产读取、启动/构建/编辑器操作等能力开放给 AI 客户端；当前实现为 UE，平台可扩展。OpenSource 版本覆盖引擎工具集与桥能力；商业版追加 DSL 资产生成等（见 §5）。

本指南的所有步骤均可基于**开源切片**完成（`OpenSource\` + `UEProject\` + `tests\`）。

## 1. 环境要求

- .NET SDK（宿主与插件构建）。
- Unreal Engine 5.0–5.8（含编辑器），示例工程为 `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`。
- 支持 MCP 的客户端（Claude Desktop / Cursor / 自定义客户端均可）。

## 2. 构建服务端与插件

```powershell
# 宿主
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# 插件（构建后把 DLL + 同名 .manifest.json 放入宿主 plugins 目录）
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> 不要直接构建整个 `ForgeMind.sln`（其含商业项目引用，商业目录不随切片发布）。插件亦可由发布方提供预构建产物。

## 3. 启动

1. **启动服务端**（默认 HTTP 常驻，端口 8765）：

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # 或运行编译产物 ForgeMindServer.exe；可用 --port N / --stdio / FORGEMIND_PORT 调整
   ```

2. **打开 UE 编辑器与示例工程**：用 UE 启动 `ForgeMind_5_0.uproject`，编辑器内 `ForgeMindForUE` 插件会自动启动桥客户端并连接宿主。

3. **配置客户端**：在 MCP 客户端中添加服务端：

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   连接后即可看到工具列表（4 个内置 + 插件注入）。

## 4. 第一次使用

- 用 `engine` 工具查看当前引擎信息；
- 用 `project` / `project_files` 查看示例工程与文件；
- 用 `launch` 启动编辑器、`editor` 进行编辑器操作；
- 用 `asset` 读取工程资产结构；
- 用 `knowledge` 检索工程知识。

> 长任务（launch/build 等）异步执行，通过 `get_task_result` 查询结果、`list_executing_tasks` 查看进行中任务、`cleanup_tasks` 清理。

## 5. 商业版（DSL）

Commercial 模块属于独立商业发布内容，不包含在 OpenSource 仓库中。商业版提供 **DSL 资产生成**：用人类可读文本描述蓝图、材质等内容，并将其转换为引擎资产；具体 DSL 工具、文本转换能力与语法细节以商业版文档为准。商业授权能力同样属于 Commercial。OpenSource 不包含 DSL 工具。

## 6. 下一步

- 架构与机制：`architecture.md`
- 工具清单与参数：`tools-reference.md`
- 开发与贡献：`development-guide.md` / `contributing.md`
- 常见问题：`troubleshooting.md`
