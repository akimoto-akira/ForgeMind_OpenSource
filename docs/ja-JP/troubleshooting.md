# ForgeMind トラブルシューティング

よくある問題と解決策（公開スライスの実際のメカニズムに基づく）。

## 1. ビルド失敗

**`ForgeMind.sln` を直接ビルドすると「Commercial\... プロジェクトが見つからない」**
原因：`ForgeMind.sln` は商業プロジェクト参照エントリ（`Commercial\ForgeMindLicenseGenerator`、`Commercial\ForgeMindLicenseServer`）を含むが、商業ディレクトリはオープンソーススライスに同梱されない。
解決：各プロジェクトを個別にビルド（`contributing.md` §3）。sln 全体をビルドしない。または公開元のビルドスクリプトを使用してください。

**プラグインをビルドしてもホストにツールが見えない**
- プラグイン DLL と同名 `.manifest.json` がホスト `plugins\<プラグイン名>\`（または `FORGEMIND_PLUGINS_DIR` 指定ディレクトリ）に配置されているか確認；
- ホストログでプラグインスキャンがそのパッケージをスキップしていないか確認（`PluginLoader` は失敗した単一パッケージのみスキップ、ホストに影響なし）；
- ホストを再起動（ツールテーブルは初回アクセス時に遅延読み込み）。

## 2. サーバー起動の問題

**ポートが使用中**
`http://127.0.0.1:8765` が使用中の場合、`HttpListenerException` が報告されます。
解決：`--port N` または環境変数 `FORGEMIND_PORT` でポートを変更。クライアントアドレスも更新。

**クライアントが接続できない / tools/list が空**
- サーバーがリスニング中か確認（ログ `Listening on http://127.0.0.1:<port>/mcp`）；
- クライアント URL が `http://127.0.0.1:8765/mcp`（HTTP モード）か確認；
- プラグインが読み込まれているか確認（上記参照）。

## 3. エンジン / ブリッジ接続の問題

**エディタは開いているが「エンジン未接続 / ブリッジ利用不可」**
- プロジェクトで `ForgeMindForUE` プラグインが有効か確認（`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`）；
- ブリッジアダプタは `%USERPROFILE%\.forgemind\bridge.json` を読んでディスカバリと認証を実行。ファイル欠落 / 資格情報不一致で接続失敗；
- UE エディタログ `LogForgeMindBridge` を確認。切断時は指数バックオフで再接続するため、再接続を待つ。

**`launch` / `build` が固まる**
長時間タスクは非同期実行。`get_task_result` で結果を照会、`list_executing_tasks` で進行中タスクを確認、`cleanup_tasks` でレコードをクリーンアップ可能。

## 4. テストの問題

**`ForgeMindServerTest` のプリビルド失敗**
`--NoBuild` はプリビルドをスキップ（CI が新規成果物を保証している場合に使用）。既定フローはホストをビルドしてプラグインを `<エントリ>\plugins\` にステージングし、失敗時はケースを実行せず直接終了。

**テストが fail-fast で中断**
統合テストは Group / Order で実行され fail-fast：最初の失敗で停止（同じ Group 内の必須ケースは実行される）。修正後、`ForgeMindServerTest.exe [group]` でそのグループを再実行。

**テストがエディタ / サンプルプロジェクトに依存**
`Task\UE5_0\` 配下のケースは UE エディタとセッションが必要。純データ処理ケース（SPK / スキャン）は `ForgeMind.PublicTests` で実行、エディタ非依存。

## 5. データ / アセットの問題

**`asset` 読み取り失敗**
`.uasset` のオフライン読み取りはエンジンバージョンと一致する必要あり（`UAssetVersions`）。バージョン不一致で解析が失敗することがあります。エンジンバージョンがツールのサポート範囲内か確認。

**`.spk` を読み込めない**
`ForgeMind.EngineScanner <spkPath>` で生成され、破損していないか確認。SPK は CRC 検証（`Speedy\Internal\Crc32`）を含み、破損ファイルは読み込みを拒否。

## 6. その他

**ログの有効化方法**
サーバーログは `LogManager` が出力（カテゴリ：`ForgeMindServerEntry`、`ForgeMindServerHost`、`ForgeMindServerBridgeServer` など）。ブリッジ側は UE エディタの出力ログを確認。

**商用機能関連の問題**
DSL 構文、ライセンス、License の問題は公開元に連絡。OpenSource バージョンはライセンスファイルを解析せず、機能は固定かつオープン（`security.md` 参照）。
