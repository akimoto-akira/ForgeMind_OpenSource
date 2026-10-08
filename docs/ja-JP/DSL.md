# ForgeMind DSL（ドメイン固有言語）

> このドキュメントでは、ForgeMind DSL の概要、動作原理、およびシステム全体における役割について説明します。ForgeMind DSL を理解または使用する開発者向けです。

---

## 1. DSL とは

**DSL（Domain-Specific Language、ドメイン固有言語）**は、ForgeMind で AI がゲームエンジンのアセットを一括作成/変更するための**宣言型コードチャネル**です。

AI は C# に近い構文スタイルで記述可能なテキストを使用して、作成または変更するエンジンアセット（ブループリント、マテリアル、ビヘイビアツリーなど）を記述し、それをトランスレータを通じて構造化された IR（Intermediate Representation）に変換し、エンジンエディターに送信して適用します。

### 1.1 コアモデル

ForgeMind DSL を理解するには、次のパイプラインを把握することが重要です：

```
AI
  ↓  DSL文字列を出力
DSL
  ↓  トランスレータによる翻訳
IR（中間表現）
  ↓  Bridge による転送
Companion（エンジンアダプター）
  ↓  適用
Engine Asset
```

このパイプラインは2つのフェーズに分かれています：

- **翻訳フェーズ**（MCP サーバー側）：DSL テキストがパーサーによって IR JSON に翻訳されます。トランスレンタは構文解析のみを行い、コンパイル、外部参照の解決、ディスクアクセスは行いません。翻訳エラーが発生した場合、バッチ全体は終了し、IR はエンジンに送信されません。
- **アプリケーションフェーズ**（エンジン側）：Companion が IR JSON を受信し、アセットごとに作成/更新/削除操作を適用します。単一のアセットの失敗は、同じバッチ内の他のアセットには影響しません。

### 1.2 DSL と MCP Action の関係

ForgeMind の AI 制御機能は、2つのツールタイプから提供されます：

| ディメンション | MCP Action | DSL |
|--------------|-----------|-----|
| きめ細かさ | 単一エンティティの精密制御 | 一括宣言型作成/変更 |
| 例 | 変数の追加、ワイヤーの接続 | ブループリントアセット全体を生成するテキストブロック |
| チャネル | Bridge 経由でエンジンに直接送信 | まず IR に翻訳し、その後 Bridge 経由で送信 |
| 役割 | 細部の調整 | 全体的なアーキテクチャの構築 |

両者は**補完的**な関係にあります。DSL は「ゼロからの一括構築」を担当し、Action は「より良くするための精密調整」を担当します。

---

## 2. DSL のシステム内での位置づけ

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  AI クライアント  │ ◄────► │  ForgeMindServer（ホスト）   │
└──────────────┘         │  • DSL 文字列を受信           │
                         │  • トランスレータを呼び出して  │
                         │    IR を生成               │
                         │  • Bridge 経由で IR を送信  │
                         └────────────┬─────────────┘
                                      │ ローカル TCP ブリッジ
                         ┌────────────▼─────────────┐
                         │  Companion（エンジンアダプ）│
                         │  • IR を受信              │
                         │  • アセットごとに適用      │
                         │  • コンパイル / 保存        │
                         └──────────────────────────┘
```

ForgeMind における DSL の位置：
- AI が DSL 宣言テキストを出力
- MCP `dsl` ツールがテキストを受信し、トランスレータを呼び出して IR JSON を生成
- IR JSON が TCP Bridge 経由でライブエンジンエディターに送信
- Companion（エンジンアダプター）がエディター内で適用し、アセット生成を完了

---

## 3. DSL の基本構文スタイル

DSL は C# の構文形式を借用していますが、**C# コンパイラは通りません**。C# の記述慣習を模倣しますが、コンパイラ内部の意味解析（型チェック、オーバーロード解決など）はトランスレータ自身が処理します。

### 3.1 宣言ブロック構造

各 DSL 宣言ブロックは、`[Attribute]` を持つ上位レベルクラス（または struct/enum）宣言です：

```csharp
[DomainAttr(Path = "/Game/Path/AssetName", Mode = Create)]
[AdditionalAttributes]
public class AssetName
{
    // メンバー宣言：変数、コンポーネント、関数など
}
```

### 3.2 Create / Update / Delete モード

すべてのドメインは3つの操作モードを統一的にサポートします：

| モード値 | 意味 | ペイロード |
|---------|------|----------|
| `Create` | 新しいアセットを作成 | `definition`（完全なアセット定義） |
| `Update` | 既存のアセットを変更 | `patches`（メンバーごとの目標状態配列） |
| `Delete` | アセット全体を削除 | 空の `patches` |

**Create 例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Update 例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // アセットに存在しない → 追加
    [Variable(Category = "Stats")]
    float Health = 90f;                    // アセットに存在する → 置換
    [Delete]                              // 削除をマーク
    float LegacyVariable;                  // → 既存メンバーを削除
}
```

実際の操作タイプ（add / replace / delete）は、ターゲットアセットにメンバーが存在するかどうか、および `[Delete]` が付注されているかどうかを Companion が推論します。DSL 宣言ではありません。

**Delete 例：**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 他のアセットの参照 — using エイリアス

DSL は using エイリアス構文をサポートし、アセット間参照を簡素化します：

```csharp
// ファイルレベルの using（すべてのブロックに適用）
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ブロックレベルの using（そのブロックのみに適用）
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

これは DSL の構文規則です — `using` を使用して識別子をアセットパス文字列にマップし、宣言内の参照を容易にします。

### 3.4 ブロック間型参照

明示的な `using` に加えて、DSL は暗黙的なブロック間型参照もサポートします：

上位レベル宣言が `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }` である場合、後続のブロックで `BP_Hunter` 識別子を直接使用でき、アセットパス `"/Game/AI/BP_Hunter"` に自動的に解決されます。

### 3.5 コメント

DSL は C# スタイルのコメントをサポートします：
- `//` 単一行コメント
- `/* */` ブロックコメント

コメントは IR 出力を生成しません。

---

## 4. IR（中間表現）

IR はトランスレータと Companion 間の中間翻訳成果物 — JSON フォーマットの構造化アセット記述です。DSL とエンジン間の「橋渡し言語」です。

### 4.1 バッチ IR と単一アセット IR

**バッチ IR**（マルチアセットバッチ）：
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

**単一アセット IR**：
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

### 4.2 単一アセット IR フィールドリファレンス

| フィールド | 型 | 説明 |
|----------|-----|------|
| `version` | string | エンジンバージョン（例：`"5.6"`）、エンジンのメジャーバージョンと正確に一致する必要があります |
| `assetType` | string | ドメイン型（ブロックの Attribute によって決定） |
| `path` | string | アセットパス（必須） |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | 適用後にコンパイルするかどうか（デフォルト true） |
| `save` | bool | 適用後にディスクに保存するかどうか |
| `patches` | array | update モードで必須（メンバーごとの目標状態、op フィールドなし） |
| `definition` | object | create モードで必須（完全なアセット定義） |

> **モード/ペイロード制約**：`mode=create` は `definition` を含み、`patches` を含めてはいけません。`mode=update` は `patches` を含み、`definition` を含めてはいけません。`mode=delete` は空の `patches` を含みます。不整合は無効なペイロードです。

### 4.3 双方向対称性

ForgeMind の IR は、「書き込み方向」（`apply_asset_ir`）と「読み取り方向」（`get_asset_ir`）の両方で**同じペイロード構造**を使用します：

| 方向 | コマンド | `mode` 値 |
|------|---------|----------|
| 書き込み | `apply_asset_ir` | `create` / `update` / `delete` |
| 読み取り | `get_asset_ir` | `read` |

これにより、AI は同じ IR を「読み取り → mode 変更 → 書き戻し」することができ、スキーマ変換は不要です。

---

## 5. ドメイン認識とルーティング

### 5.1 Attribute がドメインを決定

DSL 文字列内の各宣言ブロックは、`[Attribute]` マーカーを通じて自動的にアセットタイプにルーティングされます：

```csharp
[Blueprint(Path = "...")]          // → ブループリントトランスレータ → assetType = "blueprint"
[Material(Path = "...")]           // → マテリアルトランスレータ  → assetType = "material"
[BehaviorTree(Path = "...")]       // → ビヘイビアツリートランスレータ → assetType = "behavior_tree"
```

**Attribute → assetType マッピングテーブル：**

| Attribute | アセットタイプ |
|-----------|-------------|
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

> 上記は現在知られているすべてのドメインのマッピングです。新しいドメインの追加には、翻訳側で新しい Attribute → トランスレータマッピングを登録する必要があるだけで、ホストフレームワークの変更は不要です。

### 5.2 ルーティングルール

- パーサーは各宣言ブロックに対して正確な名前一致を行います（トークンレベル、部分一致ではない）
- 複数の Attribute がある場合、マッピングテーブルの順序で最初にヒットしたものが優先されます（`[WidgetBlueprint]` が `[Blueprint]` より優先）
- **認識されない Attribute はサイレントにフォールバックしません** — エラーを報告し、バッチ全体を終了します

### 5.3 1つの DSL での複数ドメインの混合

1つの DSL 文字列に**任意の数の任意のドメイン**の宣言ブロックを含めることができます：

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── ブロック1：ブループリントドメイン ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── ブロック2：ビヘイビアツリードメイン ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── ブロック3：マテリアルドメイン ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

同じ DSL 内のすべての宣言ブロックは個別に翻訳され、それぞれが独自の IR を生成し、1つのバッチ IR に組み立てられて Bridge 経由でエンジンに送信されます。

---

## 6. Bridge と Companion

### 6.1 Bridge 転送

IR はローカル TCP ブリッジ経由でエンジンエディターに送信されます：
- ブリッジレイヤーは IR をエンジン側に確実に配信する役割を担います
- Bridge は ForgeMind のローカルプロセス間通信インフラストラクチャです。ホスト側がブリッジサーバーを、エンジンアダプター側がブリッジクライアントを管理します

### 6.2 Companion の役割

Companion はエンジン内のアダプタープラグインです（例：ForgeMindForUE）。その役割は以下の通りです：

1. **IR JSON を受信** → `assetType` に基づいてエンジンドメインディスパッチャーに送信
2. **Create**：`definition` から完全なアセットを構築 → アセットレジストリに登録 → オプションでコンパイル → オプションでディスクに保存
3. **Update**：`patches` からメンバーごとの変更を適用（add / replace / delete を Companion が推論）
4. **Delete**：アセットパッケージをアンロード → アセットレジストリから削除 → ディスクファイルを削除
5. **結果報告**：アセットごとに success / error を返す。書き込み方向では `patchResults[].actualOp` を返します

> 1つのアセットの失敗は、同じバッチ内の他のアセットには影響しません（トランザクションロールバックなし）。これは ForgeMind の意図的な設計選択です - エディターアセットの状態空間は大きく、トランザクションコストは高く、一部成功は全部より優先されます。

---

## 7. 診断とエラー処理

DSL のエラー処理は2つのフェーズに分かれています：

| フェーズ | エラータイプ | 処理 |
|--------|------------|------|
| 翻訳フェーズ（MCP 側） | 構文エラー、認識されない Attribute、必須パラメータの欠落など | いずれかのエラー → バッチ全体が終了、IR は Bridge に送信されない |
| アプリケーションフェーズ（Companion 側） | アセットが見つからない、タイプ不一致、コンパイル失敗など | 単一アセットの失敗は、バッチ内の他のアセットに影響しない |

翻訳フェーズ中の警告（Warn）はバッチを終了しませんが、AI に返されるレスポンステキストに添付されます。

### 7.1 一般的な翻訳フェーズエラーコード

一部のエラーコード例（詳細はドキュメントを参照）：

| エラーコード | 意味 |
|------------|------|
| `FM1004` | 無効な Mode 値 |
| `FM1005` | 必須の Path パラメータがありません |
| `FM1002` | ドメイン翻訳失敗（汎用フォールバックコード） |

### 7.2 主なアレベーレベルエラーコード（アプリケーションフェーズ）

| エラーコード | 意味 |
|------------|------|
| `ASSET_NOT_FOUND` | アセットパスが存在しません（update / delete シナリオ） |
| `ASSET_ALREADY_EXISTS` | パスは既に存在します（create シナリオ、デフォルトでは上書きしない） |
| `ASSET_TYPE_MISMATCH` | タイプ不一致 |
| `ASSET_LOCKED` | アセットがエディターでロックされています |

---

## 8. DSL ドメインの拡張

### 8.1 ドメインの構成要素

完全な DSL ドメインは、次の部分で構成されています：

1. **Attribute マーカー** — 例：`[Blueprint]`、`[Material]`、ルーティング認識に使用
2. **ドメイントランスレータ実装** — そのドメインの DSL 宣言を IR JSON に翻訳する役割を担う
3. **DSL 構文ドキュメント** — ドメインの宣言ルールと Attribute パラメータを定義
4. **IR スキーマ規約** — ドメインの `definition` / `patches` の JSON 構造を定義
5. **Companion ドメインハンドラ** — エンジン側の対応する実装で、IR からアセットをエディターに適用

### 8.2 新しいドメインを追加する基本ステップ

1. ドメインのルート Attribute 名を定義する（例：`[NewDomain]`）
2. ドメイントランスレータを実装する（トランスレータ基底クラスを継承し、構文ツリービジターメソッドをオーバーライドしてドメインセマンティクスを抽出し、IR JSON を出力）
3. フレームワークに登録し、パーサーが `assetType` でトランスレータを見つけられるようにする
4. DSL 構文ドキュメントと IR スキーマ規約を作成
5. エンジンの Companion 側で対応するドメインハンドラを実装する

新しいドメインの追加には**ホストコードの変更は不要** — フレームワークは登録メカニズムを通じて新しいドメイントランスレータを発見します。

### 8.3 トランスレータの実装原則

トランスレータは次の動作規約に従い、DSL ドメインを拡張する際に遵守する必要があります：

- **純関数**：トランスレータは DSL 文字列を入力とし、IR JSON を出力とします。副作用や残留状態はありません。同じ DSL は常に同じ IR を生成します。
- **構文解析のみ**：トランスレータは C# コンパイルを実行せず、ディスクにアクセスせず、外部アセットを解決しません。DSL の構文形状を IR に翻訳するだけです。意味チェックはエンジン側の役割です。
- **エラー回復**：翻訳を中断させることなく、非標準的な DSL 構文（using エイリアスへの文字列代入など）を許容できます。
- **不明な構文の下位変換**：正確に翻訳できない構文に遭遇した場合、エラーではなく警告を報告し、エンジンのアプリケーションフェーズに任せます。

---

## 9. オープンソースと商用の境界

ForgeMind の DSL 機能は、2つの部分に分かれています：

### 9.1 オープンソース（パブリックコア）

オープンソースコアは DSL ホストフレームワークを提供します：

- **DSL フレームワークレイヤー**：DSL 文字列の解析（上位レベル宣言ブロックでの分割、Attribute の認識およびトランスレータへのルーティング）、トランスレータインターフェースと登録メカニズム、診断報告などのインフラストラクチャを担当。
- **プラグイン規約と登録メカニズム**：新しいドメイントランスレータは登録を通じて発見されます。ホストフレームワークにドメイン固有の分岐ロジックを組み込む必要はありません。
- **オープンソースはドメイントランスレータを含まない**：特定のドメイン翻訳ロジック（ブループリント、マテリアル、ビヘイビアツリーなど）はオープンソースコアの一部ではありません。

### 9.2 商用（有料拡張）

商用拡張モジュールはオープンソースフレームワークの上に構築され、以下を提供します：

- すべての特定ドメイントランスレータ実装（ブループリント、マテリアル、ビヘイビアツリー、アニメーションブループリント、Niagara、MetaSound、Chaos、Groom、Landscape、Foliage、Physics、Paper2D、シーケンサー、…）
- DSL の翻訳と送信の実行ポイントとなる MCP `dsl` ツールエントリポイント
- IR がエンジンに送信される前にドメイン固有の処理を行うごとの IR コンバーター

> オープンソースコアはフレームワークと拡張メカニズムを定義します。特定のドメイン機能（ブループリント、マテリアル、ビヘイビアツリーなどの DSL 構文とトランスレータ）は商用拡張モジュールによって提供されます。オープンソースユーザーはフレームワークに基づいてカスタムドメイントランスレータを登録し、独自の DSL 機能を拡張できます。

---

## 10. 動作制約と設計原則

| タグ | DSL 動作 | 意味 |
|-----|---------|------|
| 構文スタイル | C# に倣う | C# は AI トレーニングデータが最も多く認知的負担を軽減 |
| Node 座標 | DSL で表現しない | 「何がどのように接続されているか」トポロジーセマンティクスのみ。レイアウトはエンジンが自動生成 |
| Path 必須 | 是的 | 命名規則とモジュール所有権は AI が明確に決定 |
| バージョン一致 | 正確なメジャーバージョン | IR の `version` はエンジンのメジャーバージョンと一致する必要があります |
| トランザクション方針 | ロールバックなし | バッチ内の一部アセットの失敗は他のアセットに影響しません |
| ドライラン | 存在しない | IR はエンジンに送信されるだけで、呼び出し元に返されません |
| 認識されない Attribute | バッチを終了 | サイレントにフォールバックすると、AI は不知觉に間違ったアセットタイプを作成してしまいます |
| Update パッチ | ターゲット状態を表現 | パッチに `op` フィールドはありません。操作は Companion がメンバーの存在に基づいて推論します |

---

## 11. クイックリファレンス

### DSL 宣言ブロックテンプレート

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

### 単一アセット IR テンプレート

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

### パイプライン概要

```
AI クライアント
  → DSL（1つ以上のドメイン宣言ブロックを含むテキスト）
  → MCP dsl ツール（翻訳フェーズ）
    → パーサーが Attribute に基づいてドメイントランスレータにルーティング
    → トランスレータが IR JSON を生成
  → バッチ IR（{ version, batch, assets }）
  → Bridge（ローカル TCP）
  → Companion（アプリケーションフェーズ）
    → アセットごとに Create / Update / Delete / Compile / Save
  → Engine Asset
```
