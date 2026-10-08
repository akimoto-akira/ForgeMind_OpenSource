# ForgeMind DSL (Domain-Specific Language)

> This document explains what ForgeMind DSL is, how it works, and its role in the overall system. It is intended for developers who want to understand or use ForgeMind DSL.

---

## 1. What Is DSL

**DSL (Domain-Specific Language)** is a **declarative code channel** in ForgeMind for AI to batch create or modify game engine assets.

It allows AI to use readable text written in a C#-like syntax style to describe engine assets to be created or modified (such as Blueprints, Materials, Behavior Trees, etc.), then translate them into structured IR (Intermediate Representation) via translators, and send them to the engine editor for application.

### 1.1 Core Model

The key to understanding ForgeMind DSL is the following pipeline:

```
AI
  ↓  outputs DSL string
DSL
  ↓  translated by translator
IR (Intermediate Representation)
  ↓  transmitted via Bridge
Companion (engine adapter)
  ↓  applied
Engine Asset
```

This pipeline is divided into two phases:

- **Translation Phase** (MCP server side): DSL text is translated into IR JSON by the parser. The translator only performs syntax analysis; it does not compile, resolve external references, or access the disk. Any translation error terminates the entire batch, and the IR is not sent to the engine.
- **Application Phase** (engine side): Companion receives the IR JSON and applies create/update/delete operations asset by asset. A failure on one asset does not affect other assets in the same batch.

### 1.2 Relationship Between DSL and MCP Action

ForgeMind's AI control capabilities come from two types of tools:

| Dimension | MCP Action | DSL |
|-----------|-----------|-----|
| Granularity | Single-entity precise control | Batch declarative create/modify |
| Example | Add a variable, connect a wire | A block of text that creates an entire Blueprint asset |
| Channel | Sent directly to engine via Bridge | Translated into IR first, then sent via Bridge |
| Role | Fine-grained adjustment | High-level architecture scaffolding |

The two are **complementary** — DSL handles "from scratch" batch construction, while Action handles "from good to great" precise adjustments.

---

## 2. Where DSL Fits in the System

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  AI Client    │ ◄────► │  ForgeMindServer (host)   │
└──────────────┘         │  • Receives DSL string     │
                         │  • Invokes translator to   │
                         │    produce IR              │
                         │  • Sends IR via Bridge     │
                         └────────────┬─────────────┘
                                      │ local TCP Bridge
                         ┌────────────▼─────────────┐
                         │  Companion (engine adap.) │
                         │  • Receives IR            │
                         │  • Applies per-asset      │
                         │  • Compiles / Saves        │
                         └──────────────────────────┘
```

DSL's position in ForgeMind:
- AI outputs DSL declaration text
- The MCP `dsl` tool receives the text and invokes the translator to produce IR JSON
- IR JSON is sent to the live engine editor via TCP Bridge
- Companion (engine adapter) applies it in the editor to complete asset generation

---

## 3. Basic DSL Syntax Style

DSL borrows the syntactic shape of C#, but it **does not go through the C# compiler**. It mimics C# writing conventions, but handles compiler-level semantic analysis (type checking, overload resolution, etc.) within the translator itself.

### 3.1 Declaration Block Structure

Each DSL declaration block is a top-level class (or struct/enum) declaration carrying an `[Attribute]`:

```csharp
[DomainAttr(Path = "/Game/Path/AssetName", Mode = Create)]
[AdditionalAttributes]
public class AssetName
{
    // member declarations: variables, components, functions, etc.
}
```

### 3.2 Create / Update / Delete Modes

All domains uniformly support three operation modes:

| Mode Value | Meaning | Payload |
|------------|---------|---------|
| `Create` | Create a new asset | `definition` (complete asset definition) |
| `Update` | Modify an existing asset | `patches` (member-level target state array) |
| `Delete` | Delete the entire asset | empty `patches` |

**Create Example:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Update Example:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // not in asset → add
    [Variable(Category = "Stats")]
    float Health = 90f;                    // already in asset → replace
    [Delete]                              // mark for deletion
    float LegacyVariable;                  // → delete existing member
}
```

The actual operation type (add / replace / delete) is inferred by Companion based on whether the member exists in the target asset and whether `[Delete]` is annotated — not declared explicitly in DSL.

**Delete Example:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Referencing Other Assets — using Aliases

DSL supports `using` alias syntax to simplify cross-asset references:

```csharp
// File-level using (applies to all blocks)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// Block-level using (applies only to that block)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
}
```

This is a DSL syntax convention — `using` maps identifiers to asset path strings for convenient reference in declarations.

### 3.4 Cross-Block Type References

In addition to explicit `using`, DSL also supports implicit cross-block type references:

If a top-level declaration is `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, the identifier `BP_Hunter` can be used directly in subsequent blocks, and it will automatically resolve to the asset path `"/Game/AI/BP_Hunter"`.

### 3.5 Comments

DSL supports C# style comments:
- `//` single-line comments
- `/* */` block comments

Comments produce no IR output.

---

## 4. IR (Intermediate Representation)

IR is the intermediate translation artifact between the translator and Companion — a JSON-formatted structured asset description. It is the "bridge language" between DSL and the engine.

### 4.1 Batch IR vs. Single-Asset IR

**Batch IR** (multi-asset batch):
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

**Single-Asset IR**:
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

### 4.2 Single-Asset IR Field Reference

| Field | Type | Description |
|-------|------|-------------|
| `version` | string | Engine version (e.g., `"5.6"`), must match engine major version exactly |
| `assetType` | string | Domain type (determined by the block's Attribute) |
| `path` | string | Asset path (required) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Whether to compile after application (default true) |
| `save` | bool | Whether to save to disk after application |
| `patches` | array | Required for update mode (member-level target state, no op field) |
| `definition` | object | Required for create mode (complete asset definition) |

> **Mode / Payload Constraint**: `mode=create` must carry `definition` and must not include `patches`; `mode=update` must carry `patches` and must not include `definition`; `mode=delete` carries an empty `patches`. Any inconsistency is an invalid payload.

### 4.3 Bidirectional Symmetry

ForgeMind's IR uses **the same payload structure** in both the "write direction" (`apply_asset_ir`) and the "read direction" (`get_asset_ir`):

| Direction | Command | `mode` Value |
|-----------|---------|-------------|
| Write | `apply_asset_ir` | `create` / `update` / `delete` |
| Read | `get_asset_ir` | `read` |

This allows AI to "read → modify mode → write back" the same IR without schema conversion.

---

## 5. Domain Recognition and Routing

### 5.1 Attributes Determine Domains

Each declaration block in a DSL string is automatically routed to its asset type via its `[Attribute]` marker:

```csharp
[Blueprint(Path = "...")]          // → Blueprint translator → assetType = "blueprint"
[Material(Path = "...")]           // → Material translator  → assetType = "material"
[BehaviorTree(Path = "...")]       // → Behavior Tree translator → assetType = "behavior_tree"
```

**Attribute → assetType Mapping Table:**

| Attribute | Asset Type |
|-----------|------------|
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

> The above maps all currently known domains. Adding a new domain only requires registering a new Attribute → translator mapping on the translation side — the host framework needs no changes.

### 5.2 Routing Rules

- The parser performs exact name matching on each declaration block (at the token level, not substring matching)
- When multiple Attributes are present, the first match in the mapping table wins (`[WidgetBlueprint]` takes precedence over `[Blueprint]`)
- **An unrecognized Attribute does not silently fall back** — it reports an error and terminates the entire batch

### 5.3 Mixing Multiple Domains in One DSL

A single DSL string can contain **any number of, and any, domain** declaration blocks:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Block 1: Blueprint domain ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Block 2: Behavior Tree domain ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Block 3: Material domain ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

All declaration blocks in the same DSL are translated independently, each producing its own IR, assembled into a single Batch IR and sent to the engine together via the Bridge.

---

## 6. Bridge and Companion

### 6.1 Bridge Transport

IR is sent to the engine editor via a local TCP bridge:
- The bridge layer is responsible for reliably delivering the IR to the engine side
- Bridge is ForgeMind's local inter-process communication infrastructure: the host side runs the bridge server, the engine adapter side runs the bridge client

### 6.2 Responsibilities of Companion

Companion is the adapter plugin inside the engine (e.g., ForgeMindForUE). Its responsibilities include:

1. **Receive IR JSON** → dispatch to the engine domain handler based on `assetType`
2. **Create**: Build the complete asset from `definition` → register in asset registry → optionally compile → optionally save to disk
3. **Update**: Apply member-level modifications from `patches` (add / replace / delete inferred by Companion)
4. **Delete**: Unload the asset package → remove from asset registry → delete disk files
5. **Result Reporting**: Return success / error per asset; write direction returns `patchResults[].actualOp`

> A failure on one asset does not affect other assets in the same batch (no transaction rollback). This is a deliberate design choice in ForgeMind — the state space of editor assets is large, the cost of transactions is high, and partial success is preferred over all-or-nothing.

---

## 7. Diagnostics and Error Handling

DSL error handling is divided into two phases:

| Phase | Error Types | Handling |
|-------|------------|----------|
| Translation Phase (MCP side) | Syntax errors, unrecognized Attributes, missing required parameters, etc. | Any Error → entire batch terminated, IR not sent to Bridge |
| Application Phase (Companion side) | Asset not found, type mismatch, compile failure, etc. | Single asset failure does not affect other assets in the batch |

Warnings (Warn) during the translation phase do not terminate the batch but are attached to the response text returned to the AI.

### 7.1 Common Translation-Phase Error Codes

Some example error codes (refer to domain documentation for specifics):

| Error Code | Meaning |
|------------|---------|
| `FM1004` | Invalid Mode value |
| `FM1005` | Missing required Path parameter |
| `FM1002` | Domain translation failure (generic fallback code) |

### 7.2 Primary Asset-Level Error Codes (Application Phase)

| Error Code | Meaning |
|------------|---------|
| `ASSET_NOT_FOUND` | Asset path does not exist (update / delete scenarios) |
| `ASSET_ALREADY_EXISTS` | Path already exists (create scenario, no overwrite by default) |
| `ASSET_TYPE_MISMATCH` | Type mismatch |
| `ASSET_LOCKED` | Asset is locked in the editor |

---

## 8. Extending DSL Domains

### 8.1 Components of a Domain

A complete DSL domain consists of the following parts:

1. **Attribute Marker** — e.g., `[Blueprint]`, `[Material]`, used for routing recognition
2. **Domain Translator Implementation** — responsible for translating that domain's DSL declarations into IR JSON
3. **DSL Syntax Documentation** — defines the declaration rules and Attribute parameters for the domain
4. **IR Schema Convention** — defines the JSON structure of the domain's `definition` / `patches`
5. **Companion Domain Handler** — the corresponding implementation on the engine side that applies assets from IR in the editor

### 8.2 Basic Steps to Add a New Domain

1. Define the root Attribute name of the domain (e.g., `[NewDomain]`)
2. Implement the domain translator (inherit the translator base class, override syntax tree visitor methods to extract domain semantics, output IR JSON)
3. Register with the framework so that the parser can find the translator via `assetType`
4. Write DSL syntax documentation and IR schema conventions
5. Implement the corresponding domain handler on the engine Companion side

Adding a new domain **requires no changes to the host code** — the framework discovers new domain translators through the registration mechanism.

### 8.3 Translator Implementation Principles

Translators follow these behavior contracts that should be adhered to when extending DSL domains:

- **Pure Function**: The translator takes a DSL string as input and outputs IR JSON, with no side effects and no residual state. The same DSL always produces the same IR.
- **Syntax Analysis Only**: Translators do not execute C# compilation, do not access the disk, and do not resolve external assets; they only translate the syntactic shape of DSL into IR. Semantic validation is the responsibility of the engine side.
- **Error Recovery**: Can tolerate non-standard DSL syntax (such as string assignment to `using` aliases) without aborting translation.
- **Unknown Syntax Degradation**: When encountering syntax that cannot be precisely translated, reports a warning instead of an error, leaving it to the engine application phase.

---

## 9. Open Source and Commercial Boundary

ForgeMind's DSL capabilities are divided into two parts:

### 9.1 Open Source (Public Core)

The open source core provides the DSL host framework:

- **DSL Framework Layer**: Responsible for parsing DSL strings (splitting by top-level declaration blocks, recognizing Attributes, and routing to translators), translator interfaces and registration mechanisms, diagnostic reporting, and other infrastructure.
- **Plugin Contract and Registration Mechanism**: New domain translators are discovered via registration; the host framework does not need built-in domain-specific branching logic.
- **Open Source Excludes Domain Translators**: The specific domain translation logic (Blueprints, Materials, Behavior Trees, etc.) is not part of the open source core.

### 9.2 Commercial (Paid Extension)

The commercial extension module builds on top of the open source framework to provide:

- All specific domain translator implementations (Blueprint, Material, Behavior Tree, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Landscape, Foliage, Physics, Paper2D, Sequencer, …)
- The MCP `dsl` tool entry point, serving as the execution point for DSL translation and dispatch
- Per-domain IR converters that perform domain-specific processing before IR is sent to the engine

> The open source core defines the framework and extension mechanism; specific domain capabilities (DSL syntax and translators for Blueprints, Materials, Behavior Trees, etc.) are provided by the commercial extension module. Open source users can register custom domain translators based on the framework to extend their own DSL capabilities.

---

## 10. Behavioral Constraints and Design Principles

| Tag | DSL Behavior | Meaning |
|-----|-------------|---------|
| Syntax Style | C#-inspired | C# is the language with the most AI training data, reducing mental overhead |
| Node Coordinates | Not expressed in DSL | Only concerns "what exists and how they connect" — topology semantics; layout is auto-generated by the engine |
| Path Required | Yes | Naming conventions and module ownership are explicitly decided by the AI |
| Version Matching | Exact major | IR `version` must match the engine major version |
| Transaction Policy | No rollback | A partial asset failure in a batch does not affect other assets |
| Dry Run | Does not exist | IR is only dispatched to the engine and not echoed back to the caller |
| Unrecognized Attribute | Terminates batch | Silent fallback would cause AI to unknowingly create the wrong asset type |
| Update Patch | Expresses target state | Patch carries no `op` field; operations are inferred by Companion based on member existence |

---

## 11. Quick Reference

### DSL Declaration Block Template

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

### IR Single-Asset Template

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

### Pipeline Overview

```
AI Client
  → DSL (text containing one or more domain declaration blocks)
  → MCP dsl tool (translation phase)
    → parser routes to domain translator based on Attribute
    → translators produce IR JSON
  → Batch IR ({ version, batch, assets })
  → Bridge (local TCP)
  → Companion (application phase)
    → Create / Update / Delete / Compile / Save per asset
  → Engine Asset
```
