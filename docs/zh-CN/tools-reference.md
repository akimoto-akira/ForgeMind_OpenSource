# ForgeMind 工具参考

全部工具按「宿主内置」与「插件注入」划分。当前 OpenSource 发布切片包含 13 个 MCP 工具；商业插件可以在此基础上提供额外工具（如 DSL 资产生成，见 §3，不在开源列表）。

## 1. 宿主内置工具

宿主（`OpenSource\ForgeMindServer`）自带以下工具，无需插件：

| 工具 | 说明 |
|---|---|
| `settings` | 读写服务端设置（`McpSettingsManager`，改动即时持久化） |
| `get_task_result` | 查询长任务（launch / build 等）的执行结果 |
| `list_executing_tasks` | 列出当前进行中的长任务 |
| `cleanup_tasks` | 清理已结束的任务记录 |

## 2. 插件注入工具

插件在 `OnLoad` 时经 `PluginContext.RegisterTool` 注入；客户端连接后即可看到。构建后需把插件 DLL + 同名 `.manifest.json` 放入宿主 `plugins\` 目录（或 `FORGEMIND_PLUGINS_DIR` 指定目录）。

### ForgeMind.OfflineEngineKit（7 个）

| 工具 | 说明 |
|---|---|
| `engine` | 查询本机已安装引擎（Launcher / 源码构建，含版本与应用名） |
| `project` | 查询 / 管理 UE 工程（定位工程文件、查看工程信息） |
| `project_files` | 列出工程文件（按目录 / 类型过滤） |
| `switch_version` | 切换工程到指定引擎版本（经 UnrealVersionSelector） |
| `launch` | 启动编辑器或打包后的可执行文件（支持 debug 配置） |
| `build` | 构建工程（默认目标 / Editor 目标） |
| `editor` | 编辑器内操作（场景、视口、资产等，经桥下发） |

参数 Schema 见各工具源码 `GetParameterSchema()`（`OpenSource\ForgeMind.OfflineEngineKit\*.cs`）。部分引擎操作受 `ServerPermissionChecker` 权限约束。

### ForgeMind.Assets（1 个）

| 工具 | 说明 |
|---|---|
| `asset` | 离线读取 `.uasset` 结构（名称、对象、属性、蓝图图，支持按图/属性查询） |

实现基于 `UAsset\UAssetSerializer` / `BlueprintGraphReader` 等（`OpenSource\ForgeMind.Assets\`）。

### ForgeMind.Knowledge（1 个）

| 工具 | 说明 |
|---|---|
| `knowledge` | 知识检索：查询工程 / 引擎知识条目（来源：文件夹源 `FolderKnowledgeSource`、SPK 索引包源 `IndexPackSource`） |

实现见 `OpenSource\ForgeMind.Knowledge\Knowledge\`。

## 3. 商业版工具（概述）

商业版可追加 DSL 资产生成及相关查询工具，由商业插件注入；具体工具名称、API 与 DSL 能力以商业版文档为准。OpenSource 不包含 DSL 工具。

## 4. 工具调用约定

- 长任务工具返回任务 ID；用 `get_task_result` 轮询结果。
- 工具失败返回 `ToolResult` 失败消息（可直接展示给客户端）。
- 工具描述与参数 Schema 即 MCP `tools/list` 所见内容，保持与源码一致。
