# ForgeMind 中文文档

ForgeMind —— 以 MCP 为界面的游戏引擎控制平台：通过 MCP 协议把 Unreal Engine 的工程管理、资产读取、启动/构建/编辑器操作等能力开放给 AI 客户端。当前实现为 UE（5.0–5.8），平台设计可扩展至其他引擎。

> 本文档面向**公开开源切片**：`OpenSource\`（宿主与插件）、`UEProject\`（示例工程）、`tests\`（测试）。商业能力（DSL 资产生成、商业授权实现）仅作概述，不在 OpenSource 发布物内；OpenSource 保留必要的授权抽象与开放实现。

## 文档索引

| 文档 | 内容 |
|---|---|
| [快速上手](getting-started.md) | 环境要求、构建、启动、第一次使用 |
| [架构](architecture.md) | 宿主 / 插件 / 桥协议 / 扫描与 SPK |
| [API 参考](api-reference.md) | MCP 协议、桥协议、工具契约 |
| [工具参考](tools-reference.md) | 全部 MCP 工具与参数 |
| [开发指南](development-guide.md) | 构建、调试、新增工具 / 适配器、测试 |
| [贡献指南](contributing.md) | 开源边界、PR 流程、测试要求 |
| [故障排查](troubleshooting.md) | 常见问题与解决办法 |
| [路线图](roadmap.md) | 计划方向 |
| [对外宣传](promo.md) | 项目介绍与对外纪律 |
| [安全](security.md) | 安全设计与最佳实践 |

## 开源范围速览

```
OpenSource\
  ForgeMindServer               MCP 宿主（内置 4 工具 + 插件加载 + TCP 桥）
  ForgeMind.OfflineEngineKit    引擎/工程/启动/构建/编辑器工具（插件）
  ForgeMind.Assets              .uasset 离线读取（插件）
  ForgeMind.Knowledge           知识检索（插件）
  ForgeMind.Scan.Abstractions   扫描数据与 Provider 抽象
  ForgeMind.Scan               SPK 读取与打包
  ForgeMind.Scan.Unreal        Unreal Engine 扫描实现
  ForgeMind.EngineScanner      独立扫描 CLI，直接输出 .spk
  ForgeMind.Speedy             SPK 底层基础设施
UEProject\ForgeMind_5_0         示例 UE 工程（5.0–5.8）
tests\                          公共单元测试 + 服务端集成测试
```

## 许可证

OpenSource 发布内容采用 Apache-2.0。Commercial 模块不包含在 OpenSource 发布物中，并按照独立商业许可授权。
