# ForgeMind アーキテクチャ

ForgeMind は**MCP をインターフェースとするゲームエンジン制御プラットフォーム**です：サーバー（ホスト）は MCP プロトコルを通じて AI クライアントにツールを公開し、ツール機能は主にプラグインが注入します。エンジン側ではアダプタプラグインがローカル TCP ブリッジを通じて操作を引き受けます。UE が現在の実装であり、プラットフォーム機能は他のエンジンへ拡張可能です。本ドキュメントは**公開スライス**で見えるコードに基づき、商用機能は §7 で概要のみ説明します。

## 1. 全体像

```
┌─────────────┐   MCP(HTTP/stdio)   ┌──────────────────────┐
│ AI クライアント │ ◄────────────────► │ ForgeMindServer(ホスト) │
└─────────────┘                     │  • 組み込みツール ×4    │
                                    │  • PluginLoader       │
                                    │  • TCP ブリッジサーバー │
                                    │  • エンジンレジストリ   │
                                    └──────────┬───────────┘
                                               │ ローカル TCP ブリッジ（4 バイト長 + JSON エンベロープ）
                                    ┌──────────▼───────────┐
                                    │ UE アダプタプラグイン   │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **ホスト**（`OpenSource\ForgeMindServer`）：MCP サーバー本体。
- **プラグイン**（`OpenSource\ForgeMind.*Plugin`）：`IPlugin` を通じてツールを登録。ホストは最小ツールセットのみ同梱。
- **エンジンアダプタ**（`UEProject\...\Plugins\ForgeMindForUE`）：エディタ内プラグイン。ブリッジのサーバーとしてエンジン側機能を実装。
- **スキャン / パッケージング**（`ForgeMind.Scan.Abstractions`、`ForgeMind.Scan`、`ForgeMind.Scan.Unreal`、`ForgeMind.Speedy`、`ForgeMind.EngineScanner`）：エンジンオフライン機能のインデックスと SPK パッケージングを提供。
- **サンプルプロジェクト**（`UEProject\ForgeMind_5_0`）：テストと共同デバッグ用。

## 2. ホスト（ForgeMindServer）

- **エントリ**：`Program.cs` は既定で `127.0.0.1:8765` の HTTP 常駐サービスとして実行（Streamable HTTP + SSE 互換）。`--stdio` で spawn-per-client モードに切替。ポートは `--port` / `FORGEMIND_PORT` で上書き可能。
- **組み込みツール**（`ForgeMindServerHost` のツールテーブル、4 つのみ）：
  - `settings`（`SettingsTool`、設定の読み書き）；
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks`（長時間タスクの照会とクリーンアップ）。
- **プラグイン読み込み**：`PluginLoader` は `plugins\`（既定 `<ホストディレクトリ>\plugins\`、`FORGEMIND_PLUGINS_DIR` で上書き）をスキャンし、`PluginContext` 経由でツールテーブル、エンジンレジストリ、ブリッジを注入。プラグインの `OnLoad` は `RegisterTool` を呼び出してツールを注入。プラグインパッケージは「サブディレクトリ = プラグインパッケージ」（メイン DLL + 同名 manifest + 依存）と「ルートの疎な DLL」の 2 レイアウトをサポート。読み込みは遅延評価（ツールテーブルへの初回アクセス時にトリガー）、失敗時はそのプラグインのみスキップしホストに影響しません。
- **エンジンレジストリ**：`EngineRegistry` は起動時にローカルエンジンを列挙（Launcher インストールの `UE_x.y` 優先、ソースビルドにフォールバック）、読み取り専用スナップショットをプラグインに注入。
- **TCP ブリッジサーバー**：`ForgeMindServerBridgeServer`。エンジンアダプタのディスカバリと接続用（§5 参照）。

## 3. プラグインとツール注入

| プラグイン | 提供ツール | 責務 |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | エンジンとプロジェクト管理、起動/ビルド/エディタ操作 |
| `ForgeMind.Assets` | `asset` | `.uasset` 構造のオフライン読み取り（`UAssetArchive` / `BlueprintGraphReader` など） |
| `ForgeMind.Knowledge` | `knowledge` | ナレッジ検索（フォルダソース / インデックスパックソース、`KnowledgeStore`） |
| `ForgeMind.EngineScanner` | （コマンドラインツール、MCP ツールではない） | エンジン列挙 → `UnrealScanner` スキャン → `ScanResultPacker` で `.spk` パッケージング |

ホストとプラグインは `ITool` 契約（`Compat\ITool.cs`）で連携：名前 / 説明 / パラメータ Schema / `Execute` が `ToolResult` を返却。`ForgeMindServerTest` も同じ `ITool` でツールを直接呼び出し（MCP プロトコルを経由せず）動作を検証します。

## 4. スキャンと SPK（オフライン機能基盤）

- `ForgeMind.Scan.Abstractions`：`IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` などの抽象。
- `ForgeMind.Scan.Unreal`：`UnrealScanner` + `DocumentEngine`（`Ue5DocumentEngine`、`HeaderScanner`、`RulesCompiler`、`UAssetScanner`、`ScanIndexStore` など）——UE ヘッダーファイル、ルールファイル、ブループリントアセットをスキャンインデックスに解析。`ScanIndexStore` は実行時メモリインデックスでディスクに永続化されません。これは**旧 ScanCache とは異なります**——現在のパイプラインは ScanCache プリロード / 分散キャッシュを使用しません（ソースには歴史的な互換痕跡がわずかに残るのみで、現行メカニズムを構成しません）。
- `ForgeMind.Scan`：`ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` がスキャンパッケージを組み立て・読み取り。
- `ForgeMind.Speedy`：`SpeedyPack` は高性能パッケージング形式を提供（`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`、内部に CRC、ディレクトリマッピング、先書きログ `WriteAheadLog` などを含む）。
- 使い方：`ForgeMind.EngineScanner <spkPath>` で `.spk` を出力。knowledge などの機能は `SpkScanDataProvider` を通じて消費。

## 5. ブリッジプロトコルとエンジンアダプタ

- ブリッジ：ローカル TCP。メッセージは 4 バイト長プレフィックス + JSON エンベロープ（`BridgeMessage` / `BridgeSession` / `FrameCodec`）。アダプタはまず `%USERPROFILE%\.forgemind\bridge.json` を読んでディスカバリと認証を完了し、接続 → ハンドシェイク → メッセージループ。
- サーバー側はコマンド名でアダプタ実装にディスパッチ。コマンドハンドラはエンジンメインスレッド（UE ゲームスレッド）で実行され、エディタ状態との一貫性を保証。
- エンジンアダプタ（UE 側）は `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\` にあり、Editor モジュールでブリッジクライアントと IR アプライヤを含みます。UE サンプルプロジェクトは 5.0–5.8 互換。
- 新エンジン追加は同型のブリッジクライアントとコマンドハンドラを実装するだけ（`development-guide.md` §4 参照）。ホストとプラグインの変更は不要。

## 6. ライセンスとセキュリティ（公開部分）

- 既定の認可：`OpenAuthorizationService`（`Authorization\Open\`）——コミュニティ固定機能、**ライセンスファイルを解析しません**。
- プラグインセキュリティ：`PluginIntegrityVerifier`（マニフェスト整合性検証）、`PluginSigningKeys`（署名検証）；`ManifestLoader` はプラグインマニフェストを解析し、ServiceOverride（抽象型によるホストサービスの置換、例：`IAuthorizationService`）をサポート。
- ブリッジ認証：アダプタ接続はローカルの `bridge.json` 資格情報を読み取ってハンドシェイクを完了する必要があります（`security.md` 参照）。

## 7. 商用機能（概要）

- **DSL 言語とドメイントランスレータ**（クローズドソース）：人間が読めるテキストからエンジンアセットを生成する商用機能。公開スライスには DSL フレームワーク層のみ（`ForgeMindServer\DSL\` 配下の `ParserFactory`、`ITranslator`、`TranslatorRegistry`、`IIrConverter`、`IrConverterRegistry`、`DiagnosticReporter` などのインターフェースとレジストリ）；具体的な構文、ドメイントランスレータ、IR ルールは商業モジュールが提供し、スライスにはありません。
- **商用ライセンスシステム**（クローズドソース）：ライセンス発行、ライセンス検証、ライセンスサービス、オプションの環境バインドを含みます。具体的な実装、署名アルゴリズム、ライセンスファイル形式、サーバー側ロジックは公開スライスに属さず、外部には「追加機能を提供」とだけ示されます。

> OpenSource バージョンは完全なオープンソースツールセットとブリッジ機能を提供します。Commercial バージョンは同じホスト、プラグインメカニズム、ブリッジプロトコルに基づき商用機能（DSL アセット生成、商用ライセンス、エンタープライズサポート）を拡張します。

## 8. 機能境界表

| 機能 | 帰属 |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| DSL 拡張インターフェース | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR ルール | Commercial 🔒 |
| DSL Asset Generator | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| 商用ライセンス実装 | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| 秘密鍵 / 署名システム | Commercial 🔒 |
| ライセンスプロトコル内部詳細 | Commercial 🔒 |
