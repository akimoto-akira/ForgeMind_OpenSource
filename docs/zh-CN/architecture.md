# ForgeMind 架构

ForgeMind 是一个**以 MCP 为界面的游戏引擎控制平台**：服务端（宿主）通过 MCP 协议对 AI 客户端暴露工具，工具能力主要由插件注入；引擎侧由适配器插件通过本地 TCP 桥承接操作。UE 是当前实现，平台能力可扩展至其他引擎。本文基于**公开切片**可见代码描述，商业能力仅在 §7 概述。

## 1. 总体视图

```
┌─────────────┐   MCP(HTTP/stdio)   ┌──────────────────────┐
│ AI 客户端     │ ◄────────────────► │ ForgeMindServer(宿主)  │
└─────────────┘                     │  • 内置工具 ×4         │
                                    │  • PluginLoader       │
                                    │  • TCP 桥服务器         │
                                    │  • 引擎注册表           │
                                    └──────────┬───────────┘
                                               │ 本地 TCP 桥（4 字节长度 + JSON 信封）
                                    ┌──────────▼───────────┐
                                    │ UE 适配器插件          │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **宿主**（`OpenSource\ForgeMindServer`）：MCP 服务端本体。
- **插件**（`OpenSource\ForgeMind.*Plugin`）：通过 `IPlugin` 注册工具，宿主只内置最小工具集。
- **引擎适配器**（`UEProject\...\Plugins\ForgeMindForUE`）：编辑器内插件，作为桥的服务端实现引擎侧能力。
- **扫描 / 打包**（`ForgeMind.Scan.Abstractions`、`ForgeMind.Scan`、`ForgeMind.Scan.Unreal`、`ForgeMind.Speedy`、`ForgeMind.EngineScanner`）：为引擎离线能力提供索引与 SPK 打包。
- **示例工程**（`UEProject\ForgeMind_5_0`）：供测试与联调。

## 2. 宿主（ForgeMindServer）

- **入口**：`Program.cs` 默认以 HTTP 常驻服务运行于 `127.0.0.1:8765`（Streamable HTTP + 兼容 SSE），`--stdio` 切换 spawn-per-client 模式；端口由 `--port` / `FORGEMIND_PORT` 覆盖。
- **内置工具**（`ForgeMindServerHost` 的工具表，仅 4 个）：
  - `settings`（`SettingsTool`，读/写设置）；
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks`（长任务查询与清理）。
- **插件加载**：`PluginLoader` 扫描 `plugins\`（默认 `<宿主目录>\plugins\`，`FORGEMIND_PLUGINS_DIR` 覆盖），经 `PluginContext` 注入工具表、引擎注册表与桥，插件 `OnLoad` 调 `RegisterTool` 注入工具。插件包支持「子目录 = 插件包」（主 DLL + 同名 manifest + 依赖）与「根目录松散 DLL」两种布局。加载采用延迟求值（首次访问工具表时触发），失败仅跳过该插件，不影响宿主。
- **引擎注册表**：`EngineRegistry` 在启动时枚举本机引擎（优先 Launcher 安装 `UE_x.y`，回退源码构建），只读快照注入插件。
- **TCP 桥服务器**：`ForgeMindServerBridgeServer`，供引擎适配器发现与连接（见 §5）。

## 3. 插件与工具注入

| 插件 | 提供工具 | 职责 |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | 引擎与工程管理、启动/构建/编辑器操作 |
| `ForgeMind.Assets` | `asset` | 离线读取 `.uasset` 结构（`UAssetArchive` / `BlueprintGraphReader` 等） |
| `ForgeMind.Knowledge` | `knowledge` | 知识检索（文件夹源 / 索引包源，`KnowledgeStore`） |
| `ForgeMind.EngineScanner` | （命令行工具，非 MCP 工具） | 枚举引擎 → `UnrealScanner` 扫描 → `ScanResultPacker` 打包 `.spk` |

宿主与插件通过 `ITool` 契约交互（`Compat\ITool.cs`）：名称 / 描述 / 参数 Schema / `Execute` 返回 `ToolResult`。`ForgeMindServerTest` 也基于同一 `ITool` 直调工具（不经 MCP 协议）验证行为。

## 4. 扫描与 SPK（离线能力底座）

- `ForgeMind.Scan.Abstractions`：`IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` 等抽象。
- `ForgeMind.Scan.Unreal`：`UnrealScanner` + `DocumentEngine`（`Ue5DocumentEngine`、`HeaderScanner`、`RulesCompiler`、`UAssetScanner`、`ScanIndexStore` 等）——把 UE 头文件、规则文件与蓝图资产解析为扫描索引。`ScanIndexStore` 是运行时内存索引，不落盘；它**不等同于旧版 ScanCache**——当前流程已不再使用 ScanCache 预加载 / 散落缓存（源码中仅保留少量历史兼容痕迹，不构成现行机制）。
- `ForgeMind.Scan`：`ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` 组装与读取扫描包。
- `ForgeMind.Speedy`：`SpeedyPack` 提供高性能打包格式（`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`，内部含 CRC、目录映射、写前日志 `WriteAheadLog` 等实现）。
- 使用方式：`ForgeMind.EngineScanner <spkPath>` 输出 `.spk`；knowledge 等能力通过 `SpkScanDataProvider` 消费。

## 5. 桥协议与引擎适配

- 桥：本地 TCP，消息为 4 字节长度前缀 + JSON 信封（`BridgeMessage` / `BridgeSession` / `FrameCodec`）。适配器先读 `%USERPROFILE%\.forgemind\bridge.json` 完成发现与鉴权，再连接 → 握手 → 消息循环。
- 服务端侧按命令名调用适配器实现；命令处理器在引擎主线程（UE 游戏线程）执行，保证与编辑器状态一致。
- 引擎适配器（UE 侧）位于 `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`：Editor 模块，含桥客户端与 IR 应用器。UE 示例工程为 5.0–5.8 兼容。
- 新增引擎只需实现同构桥客户端与命令处理器（见 `development-guide.md` §4），宿主与插件无需改动。

## 6. 授权与安全（公开可见部分）

- 默认授权：`OpenAuthorizationService`（`Authorization\Open\`）——社区固定能力，**不解析 license 文件**。
- 插件安全：`PluginIntegrityVerifier`（清单完整性校验）、`PluginSigningKeys`（签名验证）；`ManifestLoader` 解析插件 manifest，支持 ServiceOverride（按抽象类型替换宿主服务，如 `IAuthorizationService`）。
- 桥鉴权：适配器连接需读取本机 `bridge.json` 凭据完成握手（详见 `security.md`）。

## 7. 商业能力（概述）

- **DSL 语言与领域翻译器**（闭源）：以人类可读文本生成引擎资产的商业能力。公开切片仅含 DSL 框架层（`ForgeMindServer\DSL\` 下的 `ParserFactory`、`ITranslator`、`TranslatorRegistry`、`IIrConverter`、`IrConverterRegistry`、`DiagnosticReporter` 等接口与注册表）；具体语法、领域翻译器与 IR 规则由商业模块提供，不在切片内。
- **商业授权体系**（闭源）：包括授权签发、授权验证、授权服务及可选的环境绑定能力。具体实现、签名算法、授权文件格式和服务端逻辑不属于公开切片；对外只呈现「提供额外能力」。

> OpenSource 版本提供全部开源工具集与桥能力；Commercial 版本基于相同的宿主、插件机制与桥协议扩展商业能力（DSL 资产生成、商业授权与企业支持）。

## 8. 能力边界表

| 能力 | 归属 |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| DSL 扩展接口 | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR 规则 | Commercial 🔒 |
| DSL Asset Generator | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| 商业授权实现 | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| 私钥 / 签发系统 | Commercial 🔒 |
| 授权协议内部细节 | Commercial 🔒 |
