# ForgeMind 贡献指南

感谢你关注 ForgeMind。公开仓库对应 ForgeMind 的 OpenSource 发布切片，仅包含对外发布的开源项目、测试与示例工程。商业模块（DSL 领域翻译器、授权体系等）不在公开仓库内，贡献前请先了解范围划分。

## 1. 公开仓库结构

开源切片（即你 clone 到的仓库）顶层结构如下：

```
ForgeMind.sln                          # 解决方案（含对商业项目的引用，见下）
OpenSource\                            # 开源项目
  ForgeMindServer\                     # MCP 服务端（宿主）
  ForgeMind.OfflineEngineKit\          # 引擎离线工具插件（engine/project/launch/build/editor 等）
  ForgeMind.Assets\                    # 资产读取插件（asset）
  ForgeMind.Knowledge\                 # 知识检索插件（knowledge）
  ForgeMind.EngineScanner\             # 引擎扫描器（命令行）
  ForgeMind.Scan\                      # 扫描打包
  ForgeMind.Scan.Abstractions\         # 扫描抽象
  ForgeMind.Scan.Unreal\               # UE 扫描实现（DocumentEngine）
  ForgeMind.Speedy\                    # SPK 打包格式
UEProject\
  ForgeMind_5_0\                       # 示例 UE 工程（5.0–5.8 兼容）
tests\
  ForgeMind.PublicTests\               # 公共单元测试（SPK / 扫描数据）
  ForgeMindServerTest\                 # 服务端集成测试（需 UE 编辑器）
```

> 注意：当前开发仓库的解决方案文件（`ForgeMind.sln`）包含 Commercial 项目引用（如 `Commercial\ForgeMindLicenseGenerator`、`Commercial\ForgeMindLicenseServer`）；公开发布物**不包含 `Commercial/` 目录**，因此 OpenSource 用户应按公开项目逐个构建（见 §3），不要直接 `dotnet build ForgeMind.sln`。

## 2. 开源边界

- **OpenSource\**：全部开源（Apache-2.0），是贡献的主要区域。
- **UEProject\**：示例工程开源，但切片导出时排除 `*.uasset` 二进制资产（Content 下仅保留地图等文本资产与目录骨架）。
- **tests\**：开源，贡献需配套测试。
- **Commercial 不在公开仓库**：DSL 领域翻译器、商业授权等不出现于切片；开源代码禁止引用商业模块类型。
- 导出时自动排除 `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache` 等构建产物与临时目录；文件头的许可证标识在导出时统一替换为 Apache-2.0。

## 3. 构建

```powershell
# 逐个构建（不要直接构建整个 sln，见上）
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# 插件项目（构建后需将产物 + 同名 .manifest.json 放入宿主 plugins 目录）
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

宿主从 `<宿主目录>\plugins\` 加载插件（可用环境变量 `FORGEMIND_PLUGINS_DIR` 覆盖）；插件包 = 主 DLL + 同名 `.manifest.json` + 依赖（见 `development-guide.md` §3）。

## 4. 代码规范

- 所有源文件头部保留 SPDX 许可证标识与版权声明（当前为 Apache-2.0）。
- 命名空间按模块组织（`ForgeMindServer.*` / `ForgeMind.*`），公共契约走 `ITool` / `IPlugin` 等抽象接口。
- 服务端工具类必须提供完整 `Description` 与参数 Schema（这些文本会直接暴露给 AI 客户端）。
- 引擎适配器代码遵守对应引擎编码规范（当前 UE 侧：类型前缀 F / U / T、`TEXT()` 宏、游戏线程安全约束）。
- 新增代码不得引用、依赖任何商业模块。

## 5. PR 流程

1. 从 `main` 拉取分支，命名建议 `feat/<描述>` 或 `fix/<描述>`。
2. 提交信息：`<类型>: <一句话说明>`。
3. 提交前自检：
   - 按 §3 逐个构建，全部通过；
   - 涉及桥命令 / 工具行为改动时，`ForgeMindServerTest` 相关 Group 通过（运行方式见 `development-guide.md` §6）；
   - 涉及 SPK / 扫描数据改动时，`ForgeMind.PublicTests` 通过；
   - 未引入对商业模块的引用；
   - 同步更新 `docs\zh-CN\` 对应篇目。
4. 发起 PR 时填写：改动内容、测试结果摘要、是否需要维护者复核特定模块。
5. 维护者审核要点：开源边界、API 兼容、测试覆盖。

## 6. 测试要求

- 桥命令 / 工具行为 / 适配器逻辑变更必须配套测试用例（`tests\ForgeMindServerTest\Task\` 下新增 `*_Tests.cs`）。
- 服务端集成测试需要 UE 编辑器环境；纯数据处理（SPK、扫描）测试放 `tests\ForgeMind.PublicTests\`，无引擎依赖。
- 用例需能在默认顺序下稳定通过（fail-fast，首个失败即停）。

## 7. 文档贡献

- 中文文档位于 `docs\zh-CN\`；内容需与源码事实一致，不确定的标注「待确认」。
- 修改对外工具清单时同步更新 `tools-reference.md`。
- 商业能力（DSL 语法、授权机制）在文档中仅作概述，不展开内部细节。

## 8. 行为准则

- 尊重既有设计决策（桥协议、工具契约、SPK 格式），重大变更先发起讨论。
- 不对外贬低竞品；宣传性内容见 `promo.md` 的对外纪律。
