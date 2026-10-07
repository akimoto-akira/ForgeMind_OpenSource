# ForgeMind 开发指南

面向希望扩展 ForgeMind 的开发者：如何构建、调试、新增 MCP 工具、新增引擎适配器以及运行测试。本文基于**公开切片**（`OpenSource\`、`UEProject\`、`tests\`）中的真实代码编写。

## 1. 项目总览

| 项目 | 职责 |
|---|---|
| `OpenSource\ForgeMindServer` | MCP 服务端宿主：内置 4 个工具，启动 TCP 桥，加载插件 |
| `OpenSource\ForgeMind.OfflineEngineKit` | 插件：engine / project / project_files / switch_version / launch / build / editor 工具 |
| `OpenSource\ForgeMind.Assets` | 插件：asset 工具（离线读取 `.uasset`） |
| `OpenSource\ForgeMind.Knowledge` | 插件：knowledge 工具（知识检索） |
| `OpenSource\ForgeMind.EngineScanner` | 命令行扫描器：枚举引擎并打包 `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | 扫描索引：抽象、打包、UE 实现（DocumentEngine） |
| `OpenSource\ForgeMind.Speedy` | SPK 打包格式（高性能包文件） |
| `tests\ForgeMind.PublicTests` | 公共单元测试（无引擎依赖） |
| `tests\ForgeMindServerTest` | 服务端集成测试（需 UE 编辑器） |
| `UEProject\ForgeMind_5_0` | 示例 UE 工程（5.0–5.8） |

## 2. 构建

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# 插件与库项目：见 contributing.md §3（不要直接构建整个 ForgeMind.sln，其含商业项目引用）
```

- 宿主入口：`OpenSource\ForgeMindServer\Program.cs`（HTTP 常驻默认 `127.0.0.1:8765`，`--stdio` 切换，`--port` / `FORGEMIND_PORT` 覆盖端口）。
- 日志：`LogManager`；工具返回 `ToolResult`（成功 / 失败含消息）。

## 3. 插件机制与新增 MCP 工具

宿主仅内置 4 个工具：`settings`、`get_task_result`、`list_executing_tasks`、`cleanup_tasks`。其余工具由 **plugins 目录下的插件**注入：

- 插件目录：`<宿主目录>\plugins\`（环境变量 `FORGEMIND_PLUGINS_DIR` 覆盖）。
- 插件包布局：`plugins\<插件名>\` 内放主 DLL + 同名 `.manifest.json` + 依赖；也支持根目录松散 DLL（手动 drop-in）。
- 宿主 `PluginLoader` 扫描插件 → `PluginContext` 注入（工具表、引擎注册表、桥）→ 插件 `OnLoad` 调用 `RegisterTool` 注册工具。
- 插件完整性：`PluginIntegrityVerifier` / `PluginSigningKeys` 负责插件清单与签名校验；受信任插件可通过 ServiceOverride 扩展服务（见 §7）。

新增一个 MCP 工具：

1. 实现 `ITool` 契约（`OpenSource\ForgeMindServer\Compat\ITool.cs`）：
   - `Name` / `DisplayName`：工具名与展示名；
   - `Description`：面向 AI 客户端的说明；
   - `GetParameterSchema()`：JSON Schema 风格参数声明；
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`：返回 `ToolResult`。
2. 新建插件项目（或加入现有插件，如 OfflineEngineKit），编写 `IPlugin` 实现，在 `OnLoad` 中 `RegisterTool`。
3. 同目录放置同名 `.manifest.json`（插件描述 / 依赖声明）。
4. 需要引擎侧能力时，通过桥命令发送并在适配器侧实现命令处理器（见 §5）。
5. 编写测试用例（§6），更新 `tools-reference.md`。

## 4. 如何新增引擎适配器

ForgeMind 以「适配器」承接引擎特有逻辑，新增引擎遵循同一套桥协议（4 字节长度前缀 + JSON 信封，见 `api-reference.md`）：

1. 实现桥客户端：读取 `%USERPROFILE%\.forgemind\bridge.json` 完成发现与鉴权，连接 → 握手 → 消息循环。
2. 按命令名注册白名单处理器；所有命令在引擎主线程（对应 UE 的游戏线程）上执行。
3. 实现引擎侧能力（资产读写、工程操作等），映射到服务端工具返回。
4. 补充该引擎的测试用例与文档。

当前首个实现为 UE 适配器 `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`（Editor 模块插件，桥客户端 + IR 应用器）。

## 5. 引擎侧桥命令（当前 UE 实现）

- 适配器侧 `ForgeMindForUE` 插件内含桥客户端；命令处理器在游戏线程执行。
- 资产级操作可复用插件内 IR 域处理器，避免重复实现编辑器逻辑。
- 调试：UE 编辑器日志中 `LogForgeMindBridge` 类别；断线指数退避重连。

## 6. 运行测试

### 公共单元测试（无需编辑器）

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

覆盖 SPK 打包往返、多版本 SPK、扫描数据提供器等（`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`）。

### 服务端集成测试（需 UE 编辑器 + 示例工程）

```powershell
# 默认：预构建宿主与插件并 staging 到 plugins 目录，然后启动宿主加载插件，反射执行全部 ITestCase
ForgeMindServerTest.exe

# 过滤：按组 + 顺序
ForgeMindServerTest.exe [group] [order]          # 或 --Test [group] [order]
# 跳过预构建
ForgeMindServerTest.exe --NoBuild
```

机制：

- 预构建：先构建 `ForgeMindServer.csproj`，再发现 `ForgeMind.*` 目录下带同名 `.manifest.json` 的插件项目并逐项构建、staging 到 `<入口目录>\plugins\<插件名>\`。
- 执行：反射发现 `ITestCase`，按 `Group` / `Order` 排序，**fail-fast**；「启动编辑器 / 关闭编辑器」也是普通用例（`Order=0` 启动、`Order=int.MaxValue` 关闭），同组共享会话。
- 用例通过 `ITool.Execute` 直接调用工具（不走 MCP 协议），工具内部经桥与 UE 通信。
- 输出：执行结束后输出 JSON 报告与可读 summary（输出目录见运行结果提示）。
- 测试资产：`Task\UE5_0\` 下 30+ `Blueprint_*_Tests.cs`（蓝图各领域：变量 / 函数 / 事件 / 材质 / 宏 / 接口等）+ `Task\Base\Engine_List_Tests.cs`；`BlueprintTestHelper` 封装桥命令、文本分发与 IR 快照比对（防误改）。

> 说明：测试中涉及「文本路径（DSL）」的用例依赖商业 DSL 能力；公开切片的集成测试以桥命令路径为主，具体以切片内 `Task\` 实际用例为准。

## 7. 授权与商业能力（概述）

- 默认 `OpenAuthorizationService`（`ForgeMindServer\Authorization\Open\`）：社区固定能力，不解析任何 license 文件。
- 商业授权通过插件 Manifest 的 ServiceOverride 替换 `IAuthorizationService` 实现（宿主 `ApplyServiceOverride`）。公开切片不含该商业插件，仅保留替换机制（宿主不感知"商业"概念，只按抽象类型替换）。

## 8. 编码规范

- 头注释含 SPDX 许可证标识与版权声明（Apache-2.0）。
- 不引用商业模块；新增插件遵守 `contributing.md` 的切片边界。
