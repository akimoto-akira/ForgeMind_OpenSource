# ForgeMind クイックスタート

ForgeMind は**MCP をインターフェースとするゲームエンジン制御プラットフォーム**です：MCP プロトコルを通じて Unreal Engine のプロジェクト管理、アセット読み取り、起動/ビルド/エディタ操作などの機能を AI クライアントに公開します。現在の実装は UE で、プラットフォームは拡張可能です。OpenSource バージョンはエンジンツールセットとブリッジ機能をカバーし、商用バージョンは DSL アセット生成などを追加します（§5 参照）。

このガイドのすべての手順は**オープンソーススライス**（`OpenSource\` + `UEProject\` + `tests\`）で完結できます。

## 1. 環境要件

- .NET SDK（ホストとプラグインのビルド用）。
- Unreal Engine 5.0–5.8（エディタ含む）。サンプルプロジェクトは `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`。
- MCP 対応クライアント（Claude Desktop / Cursor / カスタムクライアントいずれも可）。

## 2. サーバーとプラグインのビルド

```powershell
# ホスト
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# プラグイン（ビルド後、DLL + 同名 .manifest.json をホストの plugins ディレクトリへ配置）
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> `ForgeMind.sln` 全体を直接ビルドしないでください（商業プロジェクト参照を含み、商業ディレクトリはスライスに同梱されません）。プラグインは公開元が提供するビルド済み成果物でも構いません。

## 3. 起動

1. **サーバーを起動**（既定は HTTP 常駐、ポート 8765）：

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # またはビルド成果物 ForgeMindServer.exe を実行；--port N / --stdio / FORGEMIND_PORT で調整可能
   ```

2. **UE エディタとサンプルプロジェクトを開く**：UE で `ForgeMind_5_0.uproject` を起動すると、エディタ内の `ForgeMindForUE` プラグインがブリッジクライアントを自動起動しホストへ接続します。

3. **クライアントを設定**：MCP クライアントにサーバーを追加：

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   接続後、ツール一覧（組み込み 4 つ + プラグイン注入）が表示されます。

## 4. 初回利用

- `engine` ツールで現在のエンジン情報を確認；
- `project` / `project_files` でサンプルプロジェクトとファイルを確認；
- `launch` でエディタ起動、`editor` でエディタ操作；
- `asset` でプロジェクトのアセット構造を読み取り；
- `knowledge` でプロジェクトのナレッジを検索。

> 長時間タスク（launch/build など）は非同期実行されます。`get_task_result` で結果を照会、`list_executing_tasks` で進行中タスクを確認、`cleanup_tasks` でクリーンアップします。

## 5. 商用版（DSL）

Commercial モジュールは独立した商用リリースであり、OpenSource リポジトリには含まれません。商用版は **DSL アセット生成**を提供します：人間が読めるテキストでブループリントやマテリアルなどを記述し、エンジンアセットに変換します。具体的な DSL ツール、テキスト変換機能、構文の詳細は商用版ドキュメントに従います。商用ライセンス機能も Commercial に属します。OpenSource には DSL ツールは含まれません。

## 6. 次のステップ

- アーキテクチャとメカニズム：`architecture.md`
- ツール一覧とパラメータ：`tools-reference.md`
- 開発とコントリビューション：`development-guide.md` / `contributing.md`
- よくある質問：`troubleshooting.md`
