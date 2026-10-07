# ForgeMind ドキュメント（日本語）

ForgeMind —— MCP をインターフェースとするゲームエンジン制御プラットフォーム：MCP プロトコルを通じて Unreal Engine のプロジェクト管理、アセット読み取り、起動/ビルド/エディタ操作などの機能を AI クライアントに公開します。現在の実装は UE（5.0–5.8）を対象とし、プラットフォーム設計は他のエンジンへ拡張可能です。

> 本ドキュメントは**公開オープンソーススライス**を対象としています：`OpenSource\`（ホストとプラグイン）、`UEProject\`（サンプルプロジェクト）、`tests\`（テスト）。商用機能（DSL アセット生成、商用ライセンス実装）は概要のみ説明し、OpenSource リリースには含まれません。OpenSource は必要なライセンス抽象化とオープンな実装を保持します。

## ドキュメント索引

| ドキュメント | 内容 |
|---|---|
| [クイックスタート](getting-started.md) | 環境要件、ビルド、起動、初回利用 |
| [アーキテクチャ](architecture.md) | ホスト / プラグイン / ブリッジプロトコル / スキャンと SPK |
| [API リファレンス](api-reference.md) | MCP プロトコル、ブリッジプロトコル、ツール契約 |
| [ツールリファレンス](tools-reference.md) | 全 MCP ツールとパラメータ |
| [開発ガイド](development-guide.md) | ビルド、デバッグ、ツール / アダプタ追加、テスト |
| [コントリビューションガイド](contributing.md) | オープンソース境界、PR プロセス、テスト要件 |
| [トラブルシューティング](troubleshooting.md) | よくある問題と解決策 |
| [ロードマップ](roadmap.md) | 計画中の方向性 |
| [プロモ](promo.md) | プロジェクト紹介と対外コミュニケーション規律 |
| [セキュリティ](security.md) | セキュリティ設計とベストプラクティス |

## オープンソース範囲の概要

```
OpenSource\
  ForgeMindServer               MCP ホスト（組み込みツール 4 つ + プラグイン読み込み + TCP ブリッジ）
  ForgeMind.OfflineEngineKit    エンジン/プロジェクト/起動/ビルド/エディタツール（プラグイン）
  ForgeMind.Assets              .uasset オフライン読み取り（プラグイン）
  ForgeMind.Knowledge           ナレッジ検索（プラグイン）
  ForgeMind.Scan.Abstractions   スキャンデータと Provider 抽象化
  ForgeMind.Scan               SPK 読み取りとパッケージング
  ForgeMind.Scan.Unreal        Unreal Engine スキャン実装
  ForgeMind.EngineScanner       .spk を直接出力するスタンドアロンスキャン CLI
  ForgeMind.Speedy             SPK 低レベル基盤
UEProject\ForgeMind_5_0         サンプル UE プロジェクト（5.0–5.8）
tests\                          公開ユニットテスト + サーバー統合テスト
```

## ライセンス

OpenSource リリース内容は Apache-2.0 です。Commercial モジュールは OpenSource リリースに含まれず、別途商用ライセンスで提供されます。
