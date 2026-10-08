# ForgeMind DSL（領域特定語言）

> 這份文檔介紹 ForgeMind DSL 是什麼、它如何運作，以及它在整個系統中的角色。面向希望了解或使用 ForgeMind DSL 的開發者。

---

## 1. DSL 是什麼

**DSL（Domain-Specific Language，領域特定語言）** 是 ForgeMind 中 AI 批次建立/修改遊戲引擎資產的**宣告式程式碼通道**。

它讓 AI 用接近 C# 語法風格的可讀文字，描述要建立或修改的引擎資產（如藍圖、材質、行為樹等），然後透過翻譯器將其轉換為結構化的中間表示（IR，Intermediate Representation），再傳送到編輯器中套用。

### 1.1 核心模型

理解 ForgeMind DSL 的核心，是理解以下這條管線：

```
AI
  ↓  輸出 DSL 字串
DSL
  ↓  翻譯器翻譯
IR（中間表示）
  ↓  Bridge 傳輸
Companion（引擎配接器）
  ↓  套用
Engine Asset（引擎資產）
```

這條管線分為兩個階段：

- **翻譯期**（MCP 服務端側）：DSL文字被解析器翻譯為 IR JSON。翻譯器只做語法分析，不編譯、不解析外部引用、不存取磁碟。任何翻譯錯誤都會終止整批，IR 不會傳送到引擎。
- **應用期**（引擎側）：Companion 接收 IR JSON，逐資產套用建立/更新/刪除操作。某一資產的失敗不影響同批次的其他資產。

### 1.2 DSL 與 MCP Action 的關係

ForgeMind 的 AI 控制能力來自兩類工具：

| 維度 | MCP Action | DSL |
|------|-----------|-----|
| 粒度 | 單實體精確控制 | 批次宣告式建立/修改 |
| 範例 | 新增一個變數、連線一根針腳 | 一段文字建立整個藍圖資產 |
| 通道 | 直接透過 Bridge 送往引擎 | 先翻譯為 IR，再透過 Bridge 傳送 |
| 定位 | 細節調整 | 整體架構搭建 |

兩者是**互補**關係——DSL 負責「從無到有」批次建構，Action 負責「從有到優」精確調整。

---

## 2. DSL 在系統中的位置

```
┌─────────────┐   MCP   ┌──────────────────────────┐
│  AI 客戶端    │ ◄────► │  ForgeMindServer（宿主）   │
└─────────────┘         │  • 接收 DSL 字串           │
                        │  • 呼叫翻譯器產出 IR          │
                        │  • 透過 Bridge 傳送 IR       │
                        └────────────┬─────────────┘
                                     │ 本地 TCP 橋
                        ┌────────────▼─────────────┐
                        │  Companion（引擎配接器）    │
                        │  • 接收 IR                │
                        │  • 逐資產套用              │
                        │  • 編譯 / 儲存             │
                        └──────────────────────────┘
```

DSL 在 ForgeMind 中的位置：
- AI 輸出 DSL 宣告文字
- MCP `dsl` 工具接收文字，呼叫翻譯器產出 IR JSON
- IR JSON 透過 TCP Bridge 傳送到連線中的引擎編輯器
- Companion（引擎配接器）在編輯器中套用，完成資產產生

---

## 3. DSL 基本語法風格

DSL 借用 C# 的語法形狀，但**不經過 C# 編譯器**。它模仿 C# 的書寫習慣，但對編譯器內部語意分析（型別檢查、多載決議等）由翻譯器自行接管。

### 3.1 宣告區塊結構

每個 DSL 宣告區塊是一個帶有 `[Attribute]` 的頂層類別（或 struct/enum）宣告：

```csharp
[DomainAttr(Path = "/Game/Path/AssetName", Mode = Create)]
[AdditionalAttributes]
public class AssetName
{
    // 變數、元件、函式等成員宣告
}
```

### 3.2 Create / Update / Delete 模式

所有網域統一支援三種作業模式：

| Mode 值 | 意涵 | 承載 |
|---------|------|------|
| `Create` | 建立新資產 | `definition`（完整資產定義） |
| `Update` | 修改已有資產 | `patches`（成員級目標狀態陣列） |
| `Delete` | 刪除整個資產 | 空的 `patches` |

**Create 範例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Update 範例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // 資產中不存在 → 新增
    [Variable(Category = "Stats")]
    float Health = 90f;                    // 資產已存在 → 替換
    [Delete]                              // 標註刪除
    float LegacyVariable;                  // → 刪除已有成員
}
```

刪除的實際作業類型（add / replace / delete）由 Companion 根據成員在目標資產中是否存在以及是否標註了 `[Delete]` 來推斷，而非由 DSL 宣告。

**Delete 範例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 參考其他資產 — using 別名

DSL 支援 using 別名語法，用於簡化跨資產參考：

```csharp
// 檔案級 using（對所有區塊生效）
using WoodTex = "/Game/Textures/T_Wood.uasset";

// 區塊內 using（僅該區塊有效）
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

這是 DSL 的語法約定——可以使用 `using` 將識別字對應為資產路徑字串，方便在宣告中參考。

### 3.4 跨區塊型別參考

除顯式 using 外，DSL 還支援隱式的跨區塊型別參考：

如果一個頂層宣告是 `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`，則後續區塊中可以直接使用 `BP_Hunter` 識別字，它會自動解析為資產路徑 `"/Game/AI/BP_Hunter"`。

### 3.5 註解

DSL 支援 C# 風格註解：
- `//` 單行註解
- `/* */` 區塊註解

註解不產生 IR 輸出。

---

## 4. IR（中間表示）

IR 是翻譯器與 Companion 之間的中間轉譯產物——JSON 格式的結構化資產描述。它是 DSL 和引擎之間的「橋樑語言」。

### 4.1 Batch IR 與單資產 IR

**Batch IR**（多資產批次）：
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

**單資產 IR**：
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

### 4.2 單資產 IR 欄位說明

| 欄位 | 型別 | 說明 |
|------|------|------|
| `version` | string | 引擎版本（如 `"5.6"`），必須精確匹配引擎主版本 |
| `assetType` | string | 網域型別（由區塊上的 Attribute 決定） |
| `path` | string | 資產路徑（必填） |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | 套用後是否編譯（預設 true） |
| `save` | bool | 套用後是否儲存到磁碟 |
| `patches` | array | update 模式必填（成員級目標狀態，無 op 欄位） |
| `definition` | object | create 模式必填（資產完整定義） |

> **模式與承載的強約束**：`mode=create` 必須攜帶 `definition`、不得出現 `patches`；`mode=update` 必須攜帶 `patches`、不得出現 `definition`；`mode=delete` 攜帶空 `patches》。不一致為無效承載。

### 4.3 雙向對稱性

ForgeMind 的 IR 在「寫方向」（`apply_asset_ir`）和「讀方向」（`get_asset_ir`）使用**相同的承載結構**：

| 方向 | 命令 | `mode` 值 |
|------|------|----------|
| 寫 | `apply_asset_ir` | `create` / `update` / `delete` |
| 讀 | `get_asset_ir` | `read` |

這讓 AI 可以「讀取 → 修改 mode → 寫回」同一條 IR，無需 schema 轉換。

---

## 5. 網域辨識與路由

### 5.1 Attribute 決定網域

DSL 字串中的每個宣告區塊透過其攜帶的 `[Attribute]` 標記自動確定資產型別：

```csharp
[Blueprint(Path = "...")]          // → 藍圖翻譯器 → assetType = "blueprint"
[Material(Path = "...")]           // → 材質翻譯器 → assetType = "material"
[BehaviorTree(Path = "...")]       // → 行為樹翻譯器 → assetType = "behavior_tree"
```

**Attribute → assetType 對應表：**

| Attribute | 資產型別 |
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

> 以上為目前已知網域的對應。新網域的加入僅需在翻譯側註冊新的 Attribute → 翻譯器對應——宿主框架無需更動。

### 5.2 路由規則

- 解析器對每個宣告區塊做精確的名稱匹配（按 token 層級，非子字串匹配）
- 多個 Attribute 時按對應表順序第一個命中即止（`[WidgetBlueprint]` 優先於 `[Blueprint]`）
- **未辨識的 Attribute 不會靜默回退**，而是回報錯誤並終止整批

### 5.3 一份 DSL 中混合多網域

一份 DSL 字串可以包含**任意數量、任意網域**的宣告區塊：

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── 區塊 1：藍圖網域 ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── 區塊 2：行為樹網域 ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── 區塊 3：材質網域 ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

同一份 DSL 中的所有宣告區塊各自獨立翻譯、各自產出 IR，拼裝為一條 Batch IR 透過 Bridge 一併傳送到引擎。

---

## 6. Bridge 與 Companion

### 6.1 Bridge 傳輸

IR 透過本地 TCP 橋傳送到引擎編輯器：
- 橋接層負責將 IR 可靠傳達到引擎側
- Bridge 為 ForgeMind 的本地處理程序間通訊基礎設施，宿主側維護橋伺服器，引擎配接器側維護橋客戶端

### 6.2 Companion 的職責

Companion 是引擎內的配接器外掛（如 ForgeMindForUE）。它的職責包括：

1. **接收 IR JSON** → 按 `assetType` 分派到引擎網域處理器
2. **Create**：按 `definition` 建構完整資產 → 註冊到資產登錄檔 → 可選編譯 → 可選儲存磁碟
3. **Update**：按 `patches` 套用成員級修改（add / replace / delete 由 Companion 推斷）
4. **Delete**：卸載資產包 → 從資產登錄檔移除 → 刪除磁碟檔案
5. **結果回報**：逐資產傳回 success / error，write 方向傳回 `patchResults[].actualOp`

> 某一資產的失敗不影響同批次其他資產（無交易rollback），這是 ForgeMind 的明確設計選擇——編輯器資產的狀態空間大、交易成本高，部分成功優於全部放棄。

---

## 7. 診斷與錯誤處理

DSL 的錯誤處理分兩個階段：

| 階段 | 錯誤型別 | 處理方式 |
|------|----------|----------|
| 翻譯期（MCP 側） | 語法錯誤、未辨識的 Attribute、缺少必要參數等 | 任何 Error → 整批終止，IR 不上 Bridge |
| 應用期（Companion 側） | 資產不存在、型別不匹配、編譯失敗等 | 單個資產失敗，不影響同批次其他資產 |

翻譯期的警告（Warn）不影響整批，但會附加在回應文字中傳回給 AI。

### 7.1 常見翻譯期錯誤碼

部分範例錯誤碼（具體以網域文件為準）：

| 錯誤碼 | 意涵 |
|--------|------|
| `FM1004` | 非法的 Mode 值 |
| `FM1005` | 缺少的必要 Path 參數 |
| `FM1002` | 網域翻譯失敗（通用後備碼） |

### 7.2 主要資產級錯誤碼（應用期）

| 錯誤碼 | 意涵 |
|--------|------|
| `ASSET_NOT_FOUND` | 資產路徑不存在（update / delete 場景） |
| `ASSET_ALREADY_EXISTS` | 路徑已存在（create 場景，預設不覆蓋） |
| `ASSET_TYPE_MISMATCH` | 型別不匹配 |
| `ASSET_LOCKED` | 資產在編輯器中被鎖定 |

---

## 8. DSL 網域的擴充

### 8.1 網域的組成

一個完整的 DSL 網域由以下部分組成：

1. **Attribute 標記** — 如 `[Blueprint]`、`[Material]`，用於路由辨識
2. **網域翻譯器實作** — 負責將該網域的 DSL 宣告翻譯為 IR JSON
3. **DSL 語法文件** — 定義該網域的宣告規則和 Attribute 參數
4. **IR schema 約定** — 定義該網域 `definition` / `patches` 的 JSON 結構
5. **Companion 網域處理器** — 引擎側的對應實作，按 IR 在編輯器中套用資產

### 8.2 新增網域的基本步驟

1. 定義網域的根 Attribute 名稱（如 `[NewDomain]`）
2. 實作網域翻譯器（繼承翻譯器基底類別，override 語法樹存取方法以擷取網域語意，產出 IR JSON）
3. 向框架註冊，使解析器能透過 `assetType` 找到該翻譯器
4. 編寫 DSL 語法文件和 IR schema 約定
5. 在引擎 Companion 側實作對應網域處理器

新網域的加入**不需要修改宿主程式碼**——框架透過註冊機制發現新網域翻譯器。

### 8.3 翻譯器的實作原則

翻譯器遵循以下行為契約，擴充 DSL 網域時應遵守：

- **純函式**：翻譯器的輸入是 DSL 字串，輸出是 IR JSON，無副作用、無狀態殘留。同一段 DSL 始終產出相同的 IR。
- **僅做語法分析**：翻譯器不執行 C# 編譯、不存取磁碟、不解析外部資產，只將 DSL 的語法形狀翻譯為 IR。語意驗證由引擎側負責。
- **錯誤復原**：能容忍 DSL 中的非標準寫法（如字串賦值給 using 別名），不因此中斷翻譯。
- **未知語法降級**：遇到無法精確翻譯的語法時回報警告而非錯誤留給引擎套用階段處理。

---

## 9. 開源與商業邊界

ForgeMind 的 DSL 能力分為兩部分：

### 9.1 OpenSource（公開核心）

開源核心提供 DSL 的宿主框架：

- **DSL 框架層**：負責 DSL 字串的解析（按頂層宣告區塊切割、辨識 Attribute 並路由到翻譯器）、翻譯器介面與註冊機制、診斷報告等基礎設施。
- **外掛契約與註冊機制**：新網域翻譯器透過註冊發現，宿主框架不需要內建具體的網域分支邏輯。
- **開源不包含網域翻譯器**：具體網域的翻譯邏輯（藍圖、材質、行為樹等）不屬於開源核心部分。

### 9.2 Commercial（商業擴充）

商業擴充模組在開源框架之上提供：

- 全部具體網域翻譯器實作（藍圖、材質、行為樹、動畫藍圖、Niagara、MetaSound、Chaos、Groom、Landscape、Foliage、Physics、Paper2D、Sequencer、…）
- MCP `dsl` 工具入口，作為 DSL 翻譯與分派的執行點
- 各網域的 IR 轉換器，在 IR 分派引擎前做網域專屬處理

> 開源核心定義框架與擴充機制；具體領域能力（藍圖、材質、行為樹等的 DSL 語法與翻譯器）由商業擴充模組提供。開源使用者可以基於框架註冊自訂網域翻譯器，擴充自己的 DSL 能力。

---

## 10. 行為約束與設計原則

| 標籤 | DSL 行為 | 意涵 |
|------|----------|------|
| 語法風格 | 模仿 C# | AI 訓練資料中 C# 最多，降低心智負擔 |
| 節點坐標 | DSL 不表達 | 只關注「有什麼、怎麼連」的語意；佈局由引擎自動產生 |
| Path 必填 | 是 | 命名規範與模組歸屬由 AI 明確決定 |
| 版本匹配 | 精確主版本 | IR 的 `version` 必須匹配引擎主版本 |
| 交易策略 | 無 rollback | 同一批次內部分資產失敗不影響其他資產 |
| 試執行 | 不存在 | IR 只分派引擎不回應給呼叫方 |
| 未辨識 Attribute | 終止整批 | 靜默回退會導致 AI 在不知不覺中建立錯誤型別的資產 |
| Update patch | 表達目標狀態 | patch 不攜帶 op 欄位，操作由 Companion 按成員存在性推斷 |

---

## 11. 快速參考

### DSL 宣告區塊模板

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

### IR 單資產模板

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

### 管線速覽

```
AI 客戶端
  → DSL（含一個或多個網域的宣告區塊文字）
  → MCP dsl 工具（翻譯期）
    → 解析器按 Attribute 路由到網域翻譯器
    → 各翻譯器產出 IR JSON
  → Batch IR（{ version, batch, assets })
  → Bridge（本地 TCP）
  → Companion（套用期）
    → 逐資產 Create / Update / Delete / Compile / Save
  → Engine Asset
```
