# ForgeMind ツールリファレンス

すべてのツールは「ホスト組み込み」と「プラグイン注入」に分かれます。現在の OpenSource リリーススライスには 13 個の MCP ツールが含まれます。商業プラグインはこれに加えて追加ツール（DSL アセット生成など、§3 参照、オープンソース一覧には含まれません）を提供できます。

## 1. ホスト組み込みツール

ホスト（`OpenSource\ForgeMindServer`）は以下のツールを同梱しており、プラグイン不要です：

| ツール | 説明 |
|---|---|
| `settings` | サーバー設定の読み書き（`McpSettingsManager`、変更は即時永続化） |
| `get_task_result` | 長時間タスク（launch / build など）の実行結果の照会 |
| `list_executing_tasks` | 現在進行中の長時間タスクの一覧 |
| `cleanup_tasks` | 終了済みタスクレコードのクリーンアップ |

## 2. プラグイン注入ツール

プラグインは `OnLoad` で `PluginContext.RegisterTool` により注入し、クライアント接続後に表示されます。ビルド後はプラグイン DLL + 同名 `.manifest.json` をホストの `plugins\` ディレクトリ（または `FORGEMIND_PLUGINS_DIR` 指定ディレクトリ）に配置してください。

### ForgeMind.OfflineEngineKit（7 個）

| ツール | 説明 |
|---|---|
| `engine` | ローカルにインストールされたエンジンの照会（Launcher / ソースビルド、バージョンとアプリ名を含む） |
| `project` | UE プロジェクトの照会 / 管理（プロジェクトファイルの特定、プロジェクト情報の確認） |
| `project_files` | プロジェクトファイルの一覧（ディレクトリ / タイプでフィルタ） |
| `switch_version` | プロジェクトを指定エンジンバージョンへ切り替え（UnrealVersionSelector 経由） |
| `launch` | エディタまたはパッケージ済み実行ファイルの起動（デバッグ構成対応） |
| `build` | プロジェクトのビルド（既定ターゲット / Editor ターゲット） |
| `editor` | エディタ内操作（シーン、ビューポート、アセットなど、ブリッジ経由で発行） |

パラメータ Schema は各ツールのソース `GetParameterSchema()`（`OpenSource\ForgeMind.OfflineEngineKit\*.cs`）を参照。一部のエンジン操作は `ServerPermissionChecker` の権限制約を受けます。

### ForgeMind.Assets（1 個）

| ツール | 説明 |
|---|---|
| `asset` | `.uasset` 構造のオフライン読み取り（名前、オブジェクト、プロパティ、ブループリントグラフ、グラフ / プロパティによる照会に対応） |

実装は `UAsset\UAssetSerializer` / `BlueprintGraphReader` など（`OpenSource\ForgeMind.Assets\`）に基づきます。

### ForgeMind.Knowledge（1 個）

| ツール | 説明 |
|---|---|
| `knowledge` | ナレッジ検索：プロジェクト / エンジンのナレッジエントリの照会（ソース：フォルダソース `FolderKnowledgeSource`、SPK インデックスパックソース `IndexPackSource`） |

実装は `OpenSource\ForgeMind.Knowledge\Knowledge\`。

## 3. 商用版ツール（概要）

商用版は DSL アセット生成および関連照会ツールを追加でき、商業プラグインが注入します。具体的なツール名、API、DSL 機能は商用版ドキュメントに従います。OpenSource には DSL ツールは含まれません。

## 4. ツール呼び出しの規約

- 長時間タスクツールはタスク ID を返します。`get_task_result` で結果をポーリングします。
- ツール失敗時は `ToolResult` 失敗メッセージを返します（クライアントに直接表示可能）。
- ツールの説明とパラメータ Schema は MCP `tools/list` で表示される内容そのものであり、ソースコードと一致します。
