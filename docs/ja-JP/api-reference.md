# ForgeMind API リファレンス

このドキュメントは ForgeMind 外部インターフェースの公開部分を説明します：MCP プロトコル接続、ブリッジプロトコル、ツール契約、長時間タスクのメカニズム。商用機能（DSL）は §6 で概要のみ説明します。

## 1. MCP 接続

- **HTTP モード**（既定）：`http://127.0.0.1:8765/mcp`、Streamable HTTP（旧 SSE と互換）。ホストプロセスは常駐し、1 回の接続で複数のクライアントセッションをまたげます。
- **stdio モード**：`ForgeMindServer.exe --stdio`、クライアントごとにプロセス起動。
- ポート：`--port N` > 環境変数 `FORGEMIND_PORT` > 既定 8765。

標準 MCP メソッドをサポート：`initialize`、`tools/list`、`tools/call`、`notifications/cancelled` など。ツール Schema は `ITool.GetParameterSchema()` が提供します。

## 2. ツール契約

サーバーツールは統一インターフェース（`OpenSource\ForgeMindServer\Compat\ITool.cs`）を実装します：

- `Name` / `DisplayName`：ツール識別子と表示名；
- `Description`：AI クライアント向けの説明テキスト；
- `GetParameterSchema()`：パラメータ宣言（JSON Schema スタイル）；
- `Execute(Guid callerId, Dictionary<string, object> parameters)`：実行して `ToolResult`（成功結果または失敗メッセージ）を返します。

呼び出しフロー：クライアント `tools/call` → ホストが `ITool` を特定 → パラメータ検証 → `Execute` → 結果を返却。エンジン側機能を伴うツールは内部的に TCP ブリッジ経由でコマンドを発行します。

## 3. ブリッジプロトコル（ホスト ↔ エンジンアダプタ）

- トランスポート：ローカル TCP。フレーム形式 = 4 バイト長プレフィックス + JSON エンベロープ（`BridgeMessage`、`Bridge\BridgeMessage.cs` / `FrameCodec.cs` 参照）。
- ディスカバリと認証：アダプタは `%USERPROFILE%\.forgemind\bridge.json`（ホスト、ポート、資格情報を含む）を読み取り、接続 → ハンドシェイク → メッセージループ。
- コマンド意味論：リクエストはコマンド名とパラメータを含みます。サーバーはコマンドをアダプタ実装へルーティングし、レスポンスはステータスコード、メッセージ、データを含みます。エンジン側コマンドはエンジンメインスレッド（UE ゲームスレッド）で実行されます。
- ライフサイクル：ホストは起動と同時にブリッジをリッスン。アダプタは切断時に指数バックオフで再接続します。

## 4. 長時間タスクのメカニズム

- 長時間タスク（`launch`、`build` など）は非同期実行され、ツール呼び出しをブロックしません。
- 関連する組み込みツール：
  - `get_task_result`：タスク結果の照会；
  - `list_executing_tasks`：進行中タスクの一覧；
  - `cleanup_tasks`：タスクレコードのクリーンアップ。
- 内部の `LongTaskManager` がタスクのライフサイクルを管理します（`LongTaskManager.cs` / `LongTaskTools.cs`）。

## 5. 組み込みツール（ホスト同梱、プラグイン不要）

| ツール | 説明 |
|---|---|
| `settings` | サーバー設定の読み書き（`McpSettingsManager`、変更は即時永続化） |
| `get_task_result` | 長時間タスク結果の照会 |
| `list_executing_tasks` | 進行中タスクの確認 |
| `cleanup_tasks` | タスクレコードのクリーンアップ |

プラグイン注入の完全なツール一覧は `tools-reference.md` を参照。

## 6. 商用機能（DSL、概要）

- OpenSource は **DSL 拡張基盤契約**を提供します（`ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` などの抽象インターフェースは公開利用可能）。具体的な DSL 構文実装、Parser、Translator、IR ルール、アセット生成ロジックは Commercial に属し、OpenSource には同梱されません。
- DSL ツールは商業プラグインが注入します。具体的なツール名と API は商用版ドキュメントに従います。OpenSource のツール一覧に DSL ツールは含まれません。

| 機能 | 帰属 |
|---|---|
| DSL 拡張インターフェース | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR ルール | Commercial 🔒 |
| アセット生成ロジック | Commercial 🔒 |

## 7. 互換性に関する注意

- ブリッジプロトコルのフレーム形式とツール Schema は本ドキュメントに従います。破壊的変更は事前に移行通知を公開します。
