# ForgeMind 故障排查

常见问题与解决办法（基于公开切片的真实机制）。

## 1. 构建失败

**直接构建 `ForgeMind.sln` 报错「找不到 Commercial\... 项目」**
原因：`ForgeMind.sln` 含对商业项目（`Commercial\ForgeMindLicenseGenerator`、`Commercial\ForgeMindLicenseServer`）的引用条目，但商业目录不随开源切片发布。
解决：按项目逐个构建（`contributing.md` §3），不要构建整个 sln；或使用发布方构建脚本。

**插件构建后宿主仍看不到工具**
- 确认插件 DLL 与同名 `.manifest.json` 已放入宿主 `plugins\<插件名>\`（或 `FORGEMIND_PLUGINS_DIR` 指定目录）；
- 检查宿主日志中插件扫描是否跳过该包（`PluginLoader` 对单包失败仅跳过，不影响宿主）；
- 重启宿主（工具表在首次访问时触发延迟加载）。

## 2. 服务端启动问题

**端口被占用**
`http://127.0.0.1:8765` 已被占用时报 `HttpListenerException`。
解决：用 `--port N` 或环境变量 `FORGEMIND_PORT` 换端口；客户端地址同步更新。

**客户端连不上 / tools/list 为空**
- 确认服务端已监听（日志 `Listening on http://127.0.0.1:<port>/mcp`）；
- 确认客户端 URL 为 `http://127.0.0.1:8765/mcp`（HTTP 模式）；
- 确认插件已加载（见上）。

## 3. 引擎 / 桥连接问题

**编辑器打开了，但工具报「引擎未连接 / 桥不可用」**
- 确认 `ForgeMindForUE` 插件已在工程启用（`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`）；
- 桥适配器读取 `%USERPROFILE%\.forgemind\bridge.json` 完成发现与鉴权；文件缺失 / 凭据不符时连接失败；
- 看 UE 编辑器日志 `LogForgeMindBridge`；断线会指数退避重连，等待重连即可。

**`launch` / `build` 卡住**
长任务异步执行，用 `get_task_result` 查询结果、`list_executing_tasks` 查看进行中任务；`cleanup_tasks` 可清理记录。

## 4. 测试问题

**`ForgeMindServerTest` 预构建失败**
`--NoBuild` 可跳过预构建（CI 已保证产物新鲜时用）；默认流程会构建宿主并 staging 插件到 `<入口>\plugins\`，失败则直接退出不执行用例。

**测试 fail-fast 中断**
集成测试按 Group / Order 顺序执行、fail-fast：首个失败即停（同组内仍会执行关闭编辑器的 must-run 用例）。修复后用 `ForgeMindServerTest.exe [group]` 重跑该组。

**测试依赖编辑器 / 示例工程**
`Task\UE5_0\` 用例需要 UE 编辑器与会话；纯数据处理用例（SPK / 扫描）在 `ForgeMind.PublicTests` 运行，无编辑器依赖。

## 5. 数据 / 资产问题

**`asset` 读取失败**
离线读取 `.uasset` 需匹配引擎版本（`UAssetVersions`）；版本不匹配时解析可能失败。确认引擎版本与工具支持范围。

**`.spk` 无法加载**
检查是否由 `ForgeMind.EngineScanner <spkPath>` 生成且未被破坏；SPK 含 CRC 校验（`Speedy\Internal\Crc32`），损坏文件会拒绝加载。

## 6. 其他

**日志怎么开**
服务端日志由 `LogManager` 输出（类别含 `ForgeMindServerEntry`、`ForgeMindServerHost`、`ForgeMindServerBridgeServer` 等）；桥侧看 UE 编辑器输出日志。

**商业能力相关问题**
DSL 语法、授权与 License 问题请联系发布方；OpenSource 版本不解析 license 文件，能力固定开放（见 `security.md`）。
