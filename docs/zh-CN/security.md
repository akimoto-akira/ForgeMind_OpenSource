# ForgeMind 安全

本文描述公开切片中的安全设计与使用建议。

## 1. 本地通信边界

- **服务端默认仅监听回环**：HTTP 模式监听 `127.0.0.1:8765`，不对外网开放。
- **桥为本机 TCP**：宿主与引擎适配器之间为本地 TCP 连接；适配器需读取 `%USERPROFILE%\.forgemind\bridge.json`（含主机、端口、凭据）完成发现与鉴权后握手。
- 请勿将服务端端口暴露到公网；若需远程使用，请置于受信网络并通过安全通道（如 SSH 隧道）转发。

## 2. 插件完整性

- 宿主 `PluginLoader` 加载插件时执行**清单完整性校验**（`PluginIntegrityVerifier`）与签名验证（`PluginSigningKeys`）：插件包需含主 DLL + 同名 `.manifest.json`，缺失或不匹配将被拒绝 / 跳过。
- 插件注入工具发生在宿主内部进程；请只安装可信来源的插件（构建自公开切片或官方发布）。

## 3. 授权模型

**可公开的安全机制**：
- `IAuthorizationService`：宿主授权入口抽象，默认实现为 `OpenAuthorizationService`（社区固定能力，不解析任何 license 文件，不生成试用授权）。
- 受信任的 ServiceOverride：插件可通过 manifest 的 ServiceOverride 按抽象类型替换宿主服务（宿主不感知「商业」概念，仅按类型替换）。
- 插件身份验证与 manifest 校验：`PluginIntegrityVerifier` / `PluginSigningKeys` 校验插件清单与签名，保证只加载可信插件。
- 桥连接鉴权：适配器通过 `bridge.json` 完成本地发现与凭据握手，确认连接来源可信。

**商业授权**：Core 提供 `IAuthorizationService` 抽象及受信任的 ServiceOverride 扩展机制。商业授权实现属于闭源插件；具体授权协议、签发、验证和绑定逻辑不属于公开切片。

## 4. 工具权限

- 引擎操作类工具受 `ServerPermissionChecker`（各插件内）约束，执行前检查能力授权。
- 工具 `Execute` 失败统一返回 `ToolResult` 失败消息，不泄露内部堆栈细节。

## 5. 使用建议

- 仅对受信客户端开放 MCP 端口；客户端具备引擎操作能力，等同本机管理权限。
- 保持插件、宿主与示例工程更新，及时获取安全修复。
- 不要在共享机器上把 `bridge.json` 凭据复制给不受信进程。
- 发现安全问题请按发布方渠道报告，避免公开披露未修复漏洞细节。
