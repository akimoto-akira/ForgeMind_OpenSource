# ForgeMind API 参考

本文描述 ForgeMind 对外接口的公开部分：MCP 协议接入、桥协议、工具契约与长任务机制。商业能力（DSL）仅 §6 概述。

## 1. MCP 接入

- **HTTP 模式**（默认）：`http://127.0.0.1:8765/mcp`，Streamable HTTP（兼容旧版 SSE）。宿主进程常驻，一次连接可跨多个客户端会话。
- **stdio 模式**：`ForgeMindServer.exe --stdio`，spawn-per-client。
- 端口：`--port N` > 环境变量 `FORGEMIND_PORT` > 默认 8765。

支持标准 MCP 方法：`initialize`、`tools/list`、`tools/call`、`notifications/cancelled` 等；工具 Schema 由 `ITool.GetParameterSchema()` 提供。

## 2. 工具契约

服务端工具实现统一接口（`OpenSource\ForgeMindServer\Compat\ITool.cs`）：

- `Name` / `DisplayName`：工具标识与展示名；
- `Description`：面向 AI 客户端的说明文本；
- `GetParameterSchema()`：参数声明（JSON Schema 风格）；
- `Execute(Guid callerId, Dictionary<string, object> parameters)`：执行并返回 `ToolResult`（成功结果或失败消息）。

调用流程：客户端 `tools/call` → 宿主定位 `ITool` → 参数校验 → `Execute` → 结果回传。涉及引擎侧能力的工具内部经 TCP 桥下发命令。

## 3. 桥协议（宿主 ↔ 引擎适配器）

- 传输：本地 TCP；帧格式 = 4 字节长度前缀 + JSON 信封（`BridgeMessage`，见 `Bridge\BridgeMessage.cs` / `FrameCodec.cs`）。
- 发现与鉴权：适配器读取 `%USERPROFILE%\.forgemind\bridge.json`（含主机、端口与凭据），连接 → 握手 → 消息循环。
- 命令语义：请求含命令名与参数；服务端按命令路由到适配器实现；响应含状态码、消息与数据。引擎侧命令在引擎主线程（UE 游戏线程）执行。
- 生命周期：宿主启动即监听桥；适配器断线按指数退避重连。

## 4. 长任务机制

- 长任务（如 `launch`、`build`）异步执行，不阻塞工具调用。
- 相关内置工具：
  - `get_task_result`：查询任务结果；
  - `list_executing_tasks`：列出进行中的任务；
  - `cleanup_tasks`：清理任务记录。
- 宿主内部 `LongTaskManager` 管理任务生命周期（`LongTaskManager.cs` / `LongTaskTools.cs`）。

## 5. 内置工具（宿主自带，无需插件）

| 工具 | 说明 |
|---|---|
| `settings` | 读写服务端设置（`McpSettingsManager`，改动即时持久化） |
| `get_task_result` | 查询长任务结果 |
| `list_executing_tasks` | 查看进行中任务 |
| `cleanup_tasks` | 清理任务记录 |

插件注入的完整工具清单见 `tools-reference.md`。

## 6. 商业能力（DSL，概述）

- OpenSource 提供 **DSL 扩展基础契约**（`ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` 等抽象接口，可继续公开使用）；具体 DSL 语法实现、Parser、Translator、IR 规则及资产生成逻辑属于 Commercial，不随 OpenSource 发布。
- DSL 工具由商业插件注入，具体工具名称与 API 以商业版文档为准；OpenSource 工具列表不含 DSL 工具。

| 能力 | 归属 |
|---|---|
| DSL 扩展接口 | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR 规则 | Commercial 🔒 |
| 资产生成逻辑 | Commercial 🔒 |

## 7. 兼容性说明

- 桥协议帧格式与工具 Schema 以本文为准；破坏性变更会先发布迁移说明。
