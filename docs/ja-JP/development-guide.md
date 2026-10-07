# ForgeMind 開発ガイド

ForgeMind を拡張したい開発者向け：ビルド方法、デバッグ方法、MCP ツール追加、エンジンアダプタ追加、テスト実行。本ドキュメントは**公開スライス**（`OpenSource\`、`UEProject\`、`tests\`）の実コードに基づきます。

## 1. プロジェクト概要

| プロジェクト | 責務 |
|---|---|
| `OpenSource\ForgeMindServer` | MCP サーバーホスト：組み込みツール 4 つ、TCP ブリッジ起動、プラグイン読み込み |
| `OpenSource\ForgeMind.OfflineEngineKit` | プラグイン：engine / project / project_files / switch_version / launch / build / editor ツール |
| `OpenSource\ForgeMind.Assets` | プラグイン：asset ツール（オフライン `.uasset` 読み取り） |
| `OpenSource\ForgeMind.Knowledge` | プラグイン：knowledge ツール（ナレッジ検索） |
| `OpenSource\ForgeMind.EngineScanner` | コマンドラインスキャナー：エンジン列挙と `.spk` パッケージング |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | スキャンインデックス：抽象、パッケージング、UE 実装（DocumentEngine） |
| `OpenSource\ForgeMind.Speedy` | SPK パッケージング形式（高性能パックファイル） |
| `tests\ForgeMind.PublicTests` | 公開ユニットテスト（エンジン非依存） |
| `tests\ForgeMindServerTest` | サーバー統合テスト（UE エディタ必須） |
| `UEProject\ForgeMind_5_0` | サンプル UE プロジェクト（5.0–5.8） |

## 2. ビルド

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# プラグインとライブラリプロジェクト：contributing.md §3 参照（ForgeMind.sln 全体を直接ビルドしない。商業プロジェクト参照を含むため）
```

- ホストエントリ：`OpenSource\ForgeMindServer\Program.cs`（既定 HTTP 常駐 `127.0.0.1:8765`、`--stdio` で切替、`--port` / `FORGEMIND_PORT` でポート上書き）。
- ログ：`LogManager`。ツールは `ToolResult`（成功 / 失敗メッセージ付き）を返却。

## 3. プラグインメカニズムと MCP ツール追加

ホストの組み込みツールは 4 つだけ：`settings`、`get_task_result`、`list_executing_tasks`、`cleanup_tasks`。残りは **plugins ディレクトリのプラグイン**が注入します：

- プラグインディレクトリ：`<ホストディレクトリ>\plugins\`（環境変数 `FORGEMIND_PLUGINS_DIR` で上書き）。
- プラグインパッケージレイアウト：メイン DLL + 同名 `.manifest.json` + 依存を `plugins\<プラグイン名>\` に配置。ルートの疎な DLL もサポート（手動配置）。
- ホスト `PluginLoader` がプラグインをスキャン → `PluginContext` 注入（ツールテーブル、エンジンレジストリ、ブリッジ）→ プラグイン `OnLoad` が `RegisterTool` でツールを登録。
- プラグイン整合性：`PluginIntegrityVerifier` / `PluginSigningKeys` がプラグイン manifest と署名を検証。信頼されたプラグインは ServiceOverride でサービスを拡張可能（§7 参照）。

新しい MCP ツールを追加するには：

1. `ITool` 契約（`OpenSource\ForgeMindServer\Compat\ITool.cs`）を実装：
   - `Name` / `DisplayName`：ツール名と表示名；
   - `Description`：AI クライアント向けの説明；
   - `GetParameterSchema()`：JSON Schema スタイルのパラメータ宣言；
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`：`ToolResult` を返却。
2. 新しいプラグインプロジェクトを作成（または既存プラグイン例 OfflineEngineKit に参加）、`IPlugin` を実装し、`OnLoad` で `RegisterTool` を呼び出す。
3. 同ディレクトリに同名 `.manifest.json` を配置（プラグイン説明 / 依存宣言）。
4. エンジン側機能が必要な場合はブリッジコマンドで送信し、アダプタ側でコマンドハンドラを実装（§5 参照）。
5. テストケース（§6）を作成し、`tools-reference.md` を更新。

## 4. エンジンアダプタの追加方法

ForgeMind は「アダプタ」でエンジン固有ロジックを引き受けます。新エンジンの追加は同じブリッジプロトコル（4 バイト長プレフィックス + JSON エンベロープ、`api-reference.md` 参照）に従います：

1. ブリッジクライアントを実装：`%USERPROFILE%\.forgemind\bridge.json` を読んでディスカバリと認証を完了し、接続 → ハンドシェイク → メッセージループ。
2. コマンド名でホワイトリストハンドラを登録。すべてのコマンドはエンジンメインスレッド（UE ゲームスレッド相当）で実行。
3. エンジン側機能（アセット読み書き、プロジェクト操作など）を実装し、サーバーツールの返却にマッピング。
4. そのエンジンのテストケースとドキュメントを追加。

最初の実装は UE アダプタ `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`（Editor モジュールプラグイン、ブリッジクライアント + IR アプライヤ）。

## 5. エンジン側ブリッジコマンド（現在の UE 実装）

- アダプタ側 `ForgeMindForUE` プラグインはブリッジクライアントを含み、コマンドハンドラはゲームスレッドで実行。
- アセットレベルの操作はプラグイン内の IR ドメインハンドラを再利用でき、エディタロジックの重複を回避。
- デバッグ：UE エディタログ `LogForgeMindBridge` カテゴリ。切断時は指数バックオフで再接続。

## 6. テスト実行

### 公開ユニットテスト（エディタ不要）

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

SPK パッケージング往復、多バージョン SPK、スキャンデータ Provider などをカバー（`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`）。

### サーバー統合テスト（UE エディタ + サンプルプロジェクト必須）

```powershell
# 既定：ホストとプラグインをプリビルド、plugins ディレクトリへステージング、ホスト起動でプラグイン読み込み、リフレクションで全 ITestCase を実行
ForgeMindServerTest.exe

# フィルタ：group + order
ForgeMindServerTest.exe [group] [order]          # または --Test [group] [order]
# プリビルドをスキップ
ForgeMindServerTest.exe --NoBuild
```

メカニズム：

- プリビルド：まず `ForgeMindServer.csproj` をビルドし、`ForgeMind.*` ディレクトリ配下で同名 `.manifest.json` を持つプラグインプロジェクトを発見して個別ビルドし、`<エントリディレクトリ>\plugins\<プラグイン名>\` にステージング。
- 実行：リフレクションで `ITestCase` を発見、`Group` / `Order` でソート、**fail-fast**。「エディタ起動 / エディタ終了」も通常ケース（`Order=0` 起動、`Order=int.MaxValue` 終了）、同一 Group 内でセッション共有。
- ケースは `ITool.Execute` でツールを直接呼び出し（MCP プロトコルを経由しない）。ツールは内部でブリッジ経由で UE と通信。
- 出力：実行後、JSON レポートと読みやすいサマリを出力（出力ディレクトリは実行結果メッセージに表示）。
- テスト資産：`Task\UE5_0\` 配下の 30+ の `Blueprint_*_Tests.cs`（ブループリントドメイン：変数 / 関数 / イベント / マテリアル / マクロ / インターフェースなど）+ `Task\Base\Engine_List_Tests.cs`；`BlueprintTestHelper` がブリッジコマンド、テキストディスパッチ、IR スナップショット比較をカプセル化（誤変更防止）。

> 注意：「テキストパス（DSL）」に関わるテストケースは商業 DSL 機能に依存します。公開スライス内の統合テストは主にブリッジコマンドパスを使用し、スライス内の実際の `Task\` ケースに従います。

## 7. ライセンスと商用機能（概要）

- 既定の `OpenAuthorizationService`（`ForgeMindServer\Authorization\Open\`）：コミュニティ固定機能、ライセンスファイルを一切解析しない。
- 商用ライセンスは plugin Manifest の ServiceOverride で `IAuthorizationService` 実装を置換（ホスト `ApplyServiceOverride`）。公開スライスにはその商業プラグインは含まれず、置換メカニズムのみ保持（ホストは「商業」概念を認識せず、抽象型でのみ置換）。

## 8. コーディング規約

- ファイルヘッダコメントには SPDX ライセンス識別子と著作権表示（Apache-2.0）。
- 商業モジュールを参照しない。新プラグインは `contributing.md` のスライス境界に従う。
