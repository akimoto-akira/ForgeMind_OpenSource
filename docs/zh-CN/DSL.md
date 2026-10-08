# ForgeMind DSL（领域特定语言）

> 本文档介绍 ForgeMind DSL 是什么、它如何工作，以及它在整个系统中的角色。面向希望了解或使用 ForgeMind DSL 的开发者。

---

## 1. DSL 是什么

**DSL（Domain-Specific Language，领域特定语言）** 是 ForgeMind 中 AI 批量创建/修改游戏引擎资产的**声明式代码通道**。

它允许 AI 用接近 C# 语法风格的可读文本，描述要创建或修改的引擎资产（如蓝图、材质、行为树等），然后通过翻译器将其转换为结构化的中间表示（IR，Intermediate Representation），再发送到引擎编辑器中应用。

### 1.1 核心模型

理解 ForgeMind DSL 的核心，是理解以下这条管线：

```
AI
  ↓  输出 DSL 字符串
DSL
  ↓  翻译器翻译
IR（中间表示）
  ↓  Bridge 传输
Companion（引擎适配器）
  ↓  应用
Engine Asset（引擎资产）
```

这条管线分为两个阶段：

- **翻译期**（MCP 服务端侧）：DSL 文本被解析器翻译为 IR JSON。翻译器只做语法分析，不编译、不解析外部引用、不访问磁盘。任何翻译错误都会终止整批，IR 不会发送到引擎。
- **应用期**（引擎侧）：Companion 接收 IR JSON，逐资产应用创建/更新/删除操作。某一资产的失败不影响同批次的其他资产。

### 1.2 DSL 与 MCP Action 的关系

ForgeMind 的 AI 控制能力来自两类工具：

| 维度 | MCP Action | DSL |
|------|-----------|-----|
| 粒度 | 单实体精确控制 | 批量声明式创建/修改 |
| 示例 | 添加一个变量、连接一根线 | 一段文本创建整个蓝图资产 |
| 通道 | 直接通过 Bridge 发往引擎 | 先翻译为 IR，再通过 Bridge 发送 |
| 定位 | 细节调整 | 整体架构搭建 |

两者是**互补**关系——DSL 负责"从无到有"批量构建，Action 负责"从有到优"精确调整。

---

## 2. DSL 在系统中的位置

```
┌─────────────┐   MCP   ┌──────────────────────────┐
│  AI 客户端    │ ◄────► │  ForgeMindServer（宿主）   │
└─────────────┘         │  • 接收 DSL 字符串           │
                        │  • 调用翻译器产出 IR          │
                        │  • 通过 Bridge 发送 IR       │
                        └────────────┬─────────────┘
                                     │ 本地 TCP 桥
                        ┌────────────▼─────────────┐
                        │  Companion（引擎适配器）    │
                        │  • 接收 IR                │
                        │  • 逐资产应用              │
                        │  • 编译 / 保存             │
                        └──────────────────────────┘
```

DSL 在 ForgeMind 中的位置：
- AI 输出 DSL 声明文本
- MCP `dsl` 工具接收文本，调用翻译器产出 IR JSON
- IR JSON 通过 TCP Bridge 发送到在线引擎编辑器
- Companion（引擎适配器）在编辑器中应用，完成资产生成

---

## 3. DSL 基本语法风格

DSL 借用 C# 的语法形状，但**不经过 C# 编译器**。它模仿 C# 的书写习惯，但对编译器内部的语义分析（类型检查、重载决议等）由翻译器自行接管。

### 3.1 声明块结构

每个 DSL 声明块是一个带有 `[Attribute]` 的顶层类（或 struct/enum）声明：

```csharp
[DomainAttr(Path = "/Game/Path/AssetName", Mode = Create)]
[AdditionalAttributes]
public class AssetName
{
    // 变量、组件、函数等成员声明
}
```

### 3.2 Create / Update / Delete 模式

所有域统一支持三种操作模式：

| Mode 值 | 含义 | 载荷 |
|---------|------|------|
| `Create` | 创建新资产 | `definition`（完整资产定义） |
| `Update` | 修改已有资产 | `patches`（成员级目标状态数组） |
| `Delete` | 删除整个资产 | 空 `patches` |

**Create 示例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Update 示例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // 资产中不存在 → 添加
    [Variable(Category = "Stats")]
    float Health = 90f;                    // 资产已存在 → 替换
    [Delete]                              // 标注删除
    float LegacyVariable;                  // → 删除已有成员
}
```

Delete 的实际操作类型（add / replace / delete）由 Companion 根据成员在目标资产中是否存在以及是否标注了 `[Delete]` 来推断，而非由 DSL 声明。

**Delete 示例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 引用其他资产 — using 别名

DSL 支持 using 别名语法，用于简化跨资产引用：

```csharp
// 文件级 using（对所有块生效）
using WoodTex = "/Game/Textures/T_Wood.uasset";

// 块内 using（仅该块有效）
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

这是 DSL 的语法约定——可以使用 `using` 将标识符映射为资产路径字符串，方便在声明中引用。

### 3.4 跨块类型引用

除显式 using 外，DSL 还支持隐式的跨块类型引用：

如果一个顶层声明是 `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`，则后续块中可以直接使用 `BP_Hunter` 标识符，它会自动解析为资产路径 `"/Game/AI/BP_Hunter"`。

### 3.5 注释

DSL 支持 C# 风格注释：
- `//` 单行注释
- `/* */` 块注释

注释不产生 IR 输出。

---

## 4. IR（中间表示）

IR 是翻译器与 Companion 之间的中间翻译产物——JSON 格式的结构化资产描述。它是 DSL 和引擎之间的"桥梁语言"。

### 4.1 Batch IR 与单资产 IR

**Batch IR**（多资产批量）：
```json
{
  "version": "5.6",
  "batch": true,
  "assets": [
    { "assetType": "blueprint", "path": "/Game/BP_A", "mode": "create", ... },
    { "assetType": "material",  "path": "/Game/M_Wood", "mode": "create", ... }
  ]
}
```

**单资产 IR**：
```json
{
  "version": "5.6",
  "assetType": "blueprint",
  "path": "/Game/Blueprints/BP_Chest",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { ... }
}
```

### 4.2 单资产 IR 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `version` | string | 引擎版本（如 `"5.6"`），必须精确匹配引擎主版本 |
| `assetType` | string | 域类型（由块上的 Attribute 决定） |
| `path` | string | 资产路径（必填） |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | 应用后是否编译（默认 true） |
| `save` | bool | 应用后是否保存到磁盘 |
| `patches` | array | update 模式必填（成员级目标状态，无 op 字段） |
| `definition` | object | create 模式必填（资产完整定义） |

> **模式与载荷的强约束**：`mode=create` 必须携带 `definition`、不得出现 `patches`；`mode=update` 必须携带 `patches`、不得出现 `definition`；`mode=delete` 携带空 `patches`。不一致为无效载荷。

### 4.3 双向对称性

ForgeMind 的 IR 在"写方向"（`apply_asset_ir`）和"读方向"（`get_asset_ir`）使用**相同的 payload 结构**：

| 方向 | 命令 | `mode` 值 |
|------|------|----------|
| 写 | `apply_asset_ir` | `create` / `update` / `delete` |
| 读 | `get_asset_ir` | `read` |

这让 AI 可以"读取 → 修改 mode → 写回"同一条 IR，无需 schema 转换。

---

## 5. 域识别与路由

### 5.1 Attribute 决定域

DSL 字符串中的每个声明块通过其携带的 `[Attribute]` 标记自动确定资产类型：

```csharp
[Blueprint(Path = "...")]          // → 蓝图翻译器 → assetType = "blueprint"
[Material(Path = "...")]           // → 材质翻译器 → assetType = "material"
[BehaviorTree(Path = "...")]       // → 行为树翻译器 → assetType = "behavior_tree"
```

**Attribute → assetType 映射表：**

| Attribute | 资产类型 |
|-----------|----------|
| `[Blueprint]` | `blueprint` |
| `[WidgetBlueprint]` | `widget_blueprint` |
| `[Material]` | `material` / `material_instance_constant` |
| `[MaterialFunction]` | `material_function` |
| `[BehaviorTree]` | `behavior_tree` |
| `[Blackboard]` | `blackboard` |
| `[AnimBlueprint]` | `anim_blueprint` |
| `[Enum]` | `user_defined_enum` |
| `[Struct]` | `user_defined_struct` |
| `[InputAction]` | `input_action` |
| `[InputMappingContext]` | `input_mapping_context` |
| `[NiagaraEmitter]` | `niagara_emitter` |
| `[NiagaraSystem]` | `niagara_system` |
| `[MetaSoundSource]` | `meta_sound_source` |
| `[ChaosSolver]` | `chaos_solver_actor` |
| `[GroomBinding]` | `groom_binding_asset` |
| `[Landscape]` | `landscape` |
| `[FoliageType]` | `foliage_type` |
| `[PhysicsAsset]` | `physics_asset` |
| `[PaperFlipbook]` | `paper_flipbook` |
| `[LevelSequence]` | `level_sequence` |
| … | … |

> 以上为当前已知域的映射。新域的加入仅需在翻译侧注册新的 Attribute → 翻译器映射——宿主框架无需改动。

### 5.2 路由规则

- 解析器对每个声明块做精确的名称匹配（按 token 级别，非子串匹配）
- 多个 Attribute 时按映射表顺序第一个命中即止（`[WidgetBlueprint]` 优先于 `[Blueprint]`）
- **未识别的 Attribute 不会静默回退**，而是报告错误并终止整批

### 5.3 一份 DSL 中混合多域

一份 DSL 字符串可以包含**任意数量、任意域**的声明块：

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── 块 1：蓝图域 ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── 块 2：行为树域 ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── 块 3：材质域 ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

同一份 DSL 中的所有声明块各自独立翻译、各自产出 IR，拼装为一条 Batch IR 通过 Bridge 一并发送到引擎。

---

## 6. Bridge 与 Companion

### 6.1 Bridge 传输

IR 通过本地 TCP 桥发送到引擎编辑器：
- 桥接层负责将 IR 可靠送达引擎侧
- Bridge 为 ForgeMind 的本地进程间通信基础设施，宿主侧维护桥服务器，引擎适配器侧维护桥客户端

### 6.2 Companion 的职责

Companion 是引擎内的适配器插件（如 ForgeMindForUE）。它的职责包括：

1. **接收 IR JSON** → 按 `assetType` 分派到引擎域处理器
2. **Create**：按 `definition` 构建完整资产 → 注册到资产注册表 → 可选编译 → 可选保存磁盘
3. **Update**：按 `patches` 应用成员级修改（add / replace / delete 由 Companion 推断）
4. **Delete**：卸载资产包 → 从资产注册表移除 → 删除磁盘文件
5. **结果回报**：逐资产返回 success / error，write 方向返回 `patchResults[].actualOp`

> 某一资产的失败不影响同批次其他资产（无事务回滚），这是 ForgeMind 的明确设计选择——编辑器资产的状态空间大、事务成本高，部分成功优于全部放弃。

---

## 7. 诊断与错误处理

DSL 的错误处理分两个阶段：

| 阶段 | 错误类型 | 处理方式 |
|------|----------|----------|
| 翻译期（MCP 侧） | 语法错误、未识别的 Attribute、缺少必填参数等 | 任何 Error → 整批终止，IR 不上 Bridge |
| 应用期（Companion 侧） | 资产不存在、类型不匹配、编译失败等 | 单个资产失败，不影响同批次其他资产 |

翻译期的警告（Warn）不影响整批，但会附在响应文本中返回给 AI。

### 7.1 常见翻译期错误码

部分示例错误码（具体以域文档为准）：

| 错误码 | 含义 |
|--------|------|
| `FM1004` | 非法的 Mode 值 |
| `FM1005` | 缺少必填的 Path 参数 |
| `FM1002` | 域翻译失败（通用兜底码） |

### 7.2 主要资产级错误码（应用期）

| 错误码 | 含义 |
|--------|------|
| `ASSET_NOT_FOUND` | 资产路径不存在（update / delete 场景） |
| `ASSET_ALREADY_EXISTS` | 路径已存在（create 场景，默认不覆盖） |
| `ASSET_TYPE_MISMATCH` | 类型不匹配 |
| `ASSET_LOCKED` | 资产在编辑器中被锁定 |

---

## 8. DSL 域的扩展

### 8.1 域的组成

一个完整的 DSL 域由以下部分组成：

1. **Attribute 标记** — 如 `[Blueprint]`、`[Material]`，用于路由识别
2. **域翻译器实现** — 负责将该域的 DSL 声明翻译为 IR JSON
3. **DSL 语法文档** — 定义该域的声明规则和 Attribute 参数
4. **IR schema 约定** — 定义该域 `definition` / `patches` 的 JSON 结构
5. **Companion 域处理器** — 引擎侧的对应实现，按 IR 在编辑器中应用资产

### 8.2 添加新域的基本步骤

1. 定义域的根 Attribute 名（如 `[NewDomain]`）
2. 实现域翻译器（继承翻译器基类，override 语法树访问方法提取域语义，产出 IR JSON）
3. 注册到框架，使解析器能通过 `assetType` 找到该翻译器
4. 编写 DSL 语法文档和 IR schema 约定
5. 在引擎 Companion 侧实现对应域处理器

新域的加入**不需要修改宿主代码**——框架通过注册机制发现新域翻译器。

### 8.3 翻译器的实现原则

翻译器遵循以下行为契约，扩展 DSL 域时应遵守：

- **纯函数**：翻译器的输入是 DSL 字符串，输出是 IR JSON，无副作用、无状态残留。保证同一段 DSL 始终产出相同的 IR。
- **仅做语法分析**：翻译器不执行 C# 编译、不访问磁盘、不解析外部资产，只将 DSL 的语法形状翻译为 IR。语义校验由引擎侧负责。
- **错误恢复**：能容忍 DSL 中的非标准写法（如字符串赋值给 using 别名），不因此中断翻译。
- **未知语法降级**：遇到无法精确翻译的语法时报告警告而非错误，留给引擎应用阶段处理。

---

## 9. 开源与商业边界

ForgeMind 的 DSL 能力分为两部分：

### 9.1 OpenSource（公开核心）

开源核心提供 DSL 的宿主框架：

- **DSL 框架层**：负责 DSL 字符串的解析（按顶层声明块切割、识别 Attribute 并路由到翻译器）、翻译器接口与注册机制、诊断报告等基础设施。
- **插件契约与注册机制**：新域翻译器通过注册发现，宿主框架不需要内置具体的域分支逻辑。
- **开源不包含域翻译器**：具体域的翻译逻辑（蓝图、材质、行为树等）不属于开源核心部分。

### 9.2 Commercial（商业扩展）

商业扩展模块在开源框架之上提供：

- 全部具体域翻译器实现（蓝图、材质、行为树、动画蓝图、Niagara、MetaSound、Chaos、Groom、Landscape、Foliage、Physics、Paper2D、Sequencer、…）
- MCP `dsl` 工具入口，作为 DSL 翻译与下发的执行点
- 各域的 IR 转换器，在 IR 下发引擎前做域专属处理

> 开源核心定义框架与扩展机制；具体领域能力（蓝图、材质、行为树等的 DSL 语法与翻译器）由商业扩展模块提供。开源用户可以基于框架注册自定义域翻译器，扩展自己的 DSL 能力。

---

## 10. 行为约束与设计原则

| 标签 | DSL 行为 | 含义 |
|------|----------|------|
| 语法风格 | 模仿 C# | AI 训练数据中 C# 最多，降低心智负担 |
| 节点坐标 | DSL 不表达 | 只关注"有什么、怎么连"的拓扑语义；布局由引擎自动生成 |
| Path 必填 | 是 | 命名规范与模块归属由 AI 显式决定 |
| 版本匹配 | 精确主版本 | IR 的 `version` 必须匹配引擎主版本 |
| 事务策略 | 无回滚 | 同一批次内部分资产失败不影响其他资产 |
| 试运行 | 不存在 | IR 只下发引擎，不回吐给调用方 |
| 未识别 Attribute | 终止整批 | 静默回退会导致 AI 在不知不觉中创建错误类型的资产 |
| Update patch | 表达目标状态 | patch 不携带 op 字段，操作由 Companion 按成员存在性推断 |

---

## 11. 快速参考

### DSL 声明块模板

```csharp
using SomeAsset = "/Game/Path/SomeAsset.uasset";

[DomainAttr(Path = "/Game/Path/YourAsset", Mode = Create)]
[AdditionalAttribute]
public class YourAsset
{
    using BlockAlias = "/Game/Path/AnotherAsset.uasset";
    [Variable] float YourVariable = 1f;
    [Function(Pure = true)]
    bool YourFunction() => YourVariable > 0;
}
```

### IR 单资产模板

```json
{
  "version": "<major.minor>",
  "assetType": "<snake_case_type>",
  "path": "/Game/Path/Asset",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { }
}
```

### 管线速览

```
AI 客户端
  → DSL（含一个或多个域的声明块文本）
  → MCP dsl 工具（翻译期）
    → 解析器按 Attribute 路由到域翻译器
    → 各翻译器产出 IR JSON
  → Batch IR（{ version, batch, assets })
  → Bridge（本地 TCP）
  → Companion（应用期）
    → 逐资产 Create / Update / Delete / Compile / Save
  → Engine Asset
```
