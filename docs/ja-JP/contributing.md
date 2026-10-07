# ForgeMind コントリビューションガイド

ForgeMind に興味を持っていただきありがとうございます。公開リポジトリは ForgeMind の OpenSource リリーススライスに対応し、公開されたオープンソースプロジェクト、テスト、サンプルプロジェクトのみを含みます。商業モジュール（DSL ドメイントランスレータ、ライセンスシステムなど）は公開リポジトリにありません。コントリビュート前に範囲の区分を理解してください。

## 1. 公開リポジトリ構造

オープンソーススライス（clone したリポジトリ）のトップレベル構造：

```
ForgeMind.sln                          # ソリューション（商業プロジェクト参照を含む、下記参照）
OpenSource\                            # オープンソースプロジェクト
  ForgeMindServer\                     # MCP サーバー（ホスト）
  ForgeMind.OfflineEngineKit\          # エンジンオフラインツールプラグイン（engine/project/launch/build/editor など）
  ForgeMind.Assets\                    # アセット読み取りプラグイン（asset）
  ForgeMind.Knowledge\                 # ナレッジ検索プラグイン（knowledge）
  ForgeMind.EngineScanner\             # エンジンスキャナー（コマンドライン）
  ForgeMind.Scan\                      # スキャンパッケージング
  ForgeMind.Scan.Abstractions\         # スキャン抽象
  ForgeMind.Scan.Unreal\               # UE スキャン実装（DocumentEngine）
  ForgeMind.Speedy\                    # SPK パッケージング形式
UEProject\
  ForgeMind_5_0\                       # サンプル UE プロジェクト（5.0–5.8 互換）
tests\
  ForgeMind.PublicTests\               # 公開ユニットテスト（SPK / スキャンデータ）
  ForgeMindServerTest\                 # サーバー統合テスト（UE エディタ必須）
```

> 注意：現在の開発リポジトリのソリューションファイル（`ForgeMind.sln`）は Commercial プロジェクト参照（`Commercial\ForgeMindLicenseGenerator`、`Commercial\ForgeMindLicenseServer` など）を含みます。公開リリース**には `Commercial/` ディレクトリが含まれない**ため、OpenSource ユーザーは公開プロジェクトを個別にビルドし（§3 参照）、`dotnet build ForgeMind.sln` を直接実行してはいけません。

## 2. オープンソース境界

- **OpenSource\**：すべてオープンソース（Apache-2.0）。コントリビューションの主要領域。
- **UEProject\**：サンプルプロジェクトはオープンソースですが、スライスエクスポート時に `*.uasset` バイナリアセットを除外（Content 配下はマップなどのテキスト資産とディレクトリスケルトンのみ保持）。
- **tests\**：オープンソース。コントリビューションにはテストを添付。
- **Commercial は公開リポジトリにない**：DSL ドメイントランスレータ、商用ライセンスなどはスライスに登場しない。オープンソースコードは商業モジュール型を参照禁止。
- エクスポートは `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache` などのビルド成果物と一時ディレクトリを自動除外。ファイルヘッダのライセンス識別子はエクスポート時に Apache-2.0 へ統一置換。

## 3. ビルド

```powershell
# 個別にビルド（sln 全体を直接ビルドしない、上記参照）
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# プラグインプロジェクト（ビルド後、出力 + 同名 .manifest.json をホストの plugins ディレクトリへ配置）
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

ホストは `<ホストディレクトリ>\plugins\` からプラグインを読み込み（環境変数 `FORGEMIND_PLUGINS_DIR` で上書き）。プラグインパッケージ = メイン DLL + 同名 `.manifest.json` + 依存（`development-guide.md` §3 参照）。

## 4. コード規約

- すべてのソースファイルの先頭に SPDX ライセンス識別子と著作権表示を保持（現在 Apache-2.0）。
- 名前空間はモジュール単位で整理（`ForgeMindServer.*` / `ForgeMind.*`）。公開契約は `ITool` / `IPlugin` などの抽象インターフェース経由。
- サーバーツールクラスは完全な `Description` とパラメータ Schema を提供必須（これらのテキストは AI クライアントに直接表示される）。
- エンジンアダプタコードは対応エンジンのコーディング規約に従う（現在の UE 側：F/U/T 型プレフィックス、`TEXT()` マクロ、ゲームスレッド安全制約）。
- 新コードは商業モジュールを参照・依存してはならない。

## 5. PR プロセス

1. `main` からブランチを作成。命名提案：`feat/<説明>` または `fix/<説明>`。
2. コミットメッセージ：`<type>: <一行説明>`。
3. コミット前の自己チェック：
   - §3 に従い個別ビルドがすべて成功；
   - ブリッジコマンド / ツール動作変更時、関連する `ForgeMindServerTest` Group が成功（実行方法は `development-guide.md` §6）；
   - SPK / スキャンデータ変更時、`ForgeMind.PublicTests` が成功；
   - 商業モジュール参照を導入していない；
   - `docs\zh-CN\` の対応ドキュメントを同期更新。
4. PR を開く際に記入：変更内容、テスト結果サマリ、特定モジュールのメンテナレビューが必要か。
5. メンテナのレビュー重点：オープンソース境界、API 互換性、テストカバレッジ。

## 6. テスト要件

- ブリッジコマンド / ツール動作 / アダプタロジックの変更にはテストケースを添付必須（`tests\ForgeMindServerTest\Task\` 配下に `*_Tests.cs` を追加）。
- サーバー統合テストは UE エディタ環境が必要。純データ処理テスト（SPK、スキャン）は `tests\ForgeMind.PublicTests\` に配置、エンジン非依存。
- ケースは既定順序で安定して成功すること（fail-fast、最初の失敗で停止）。

## 7. ドキュメントコントリビューション

- 中国語ドキュメントは `docs\zh-CN\` にあります。内容はソースコードの事実と一致させ、不確定項目は「待確認」と標記。
- 対外ツール一覧を変更する場合は `tools-reference.md` を同期更新。
- 商用機能（DSL 構文、ライセンスメカニズム）はドキュメントで概要のみ説明し、内部詳細は展開しない。

## 8. 行動規範

- 既存の設計決定（ブリッジプロトコル、ツール契約、SPK 形式）を尊重。大きな変更は先に議論を開始。
- 対外的に同種製品を貶めない。プロモーション内容は `promo.md` の対外規律に従う。
