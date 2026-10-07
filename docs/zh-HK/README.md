# ForgeMind 中文文檔（繁體中文）

ForgeMind —— 以 MCP 為介面的遊戲引擎控制平台：透過 MCP 協定把 Unreal Engine 的專案管理、資產讀取、啟動/建置/編輯器操作等能力開放給 AI 用戶端。目前實作為 UE（5.0–5.8），平台設計可擴充至其他引擎。

> 本文檔面向**公開開源切片**：`OpenSource\`（宿主與外掛）、`UEProject\`（範例專案）、`tests\`（測試）。商業能力（DSL 資產產生、商業授權實作）僅作概述，不在 OpenSource 發佈物內；OpenSource 保留必要的授權抽象與開放實作。

## 文件索引

| 文件 | 內容 |
|---|---|
| [快速上手](getting-started.md) | 環境需求、建置、啟動、第一次使用 |
| [架構](architecture.md) | 宿主 / 外掛 / 橋協定 / 掃描與 SPK |
| [API 參考](api-reference.md) | MCP 協定、橋協定、工具契約 |
| [工具參考](tools-reference.md) | 全部 MCP 工具與參數 |
| [開發指南](development-guide.md) | 建置、除錯、新增工具 / 介面卡、測試 |
| [貢獻指南](contributing.md) | 開源邊界、PR 流程、測試要求 |
| [故障排除](troubleshooting.md) | 常見問題與解決辦法 |
| [路線圖](roadmap.md) | 計劃方向 |
| [對外宣傳](promo.md) | 專案介紹與對外紀律 |
| [安全](security.md) | 安全設計與最佳實務 |

## 開源範圍速覽

```
OpenSource\
  ForgeMindServer               MCP 宿主（內建 4 工具 + 外掛載入 + TCP 橋）
  ForgeMind.OfflineEngineKit    引擎/專案/啟動/建置/編輯器工具（外掛）
  ForgeMind.Assets              .uasset 離線讀取（外掛）
  ForgeMind.Knowledge           知識檢索（外掛）
  ForgeMind.Scan.Abstractions   掃描資料與 Provider 抽象
  ForgeMind.Scan               SPK 讀取與打包
  ForgeMind.Scan.Unreal        Unreal Engine 掃描實作
  ForgeMind.EngineScanner      獨立掃描 CLI，直接輸出 .spk
  ForgeMind.Speedy             SPK 底層基礎設施
UEProject\ForgeMind_5_0         範例 UE 專案（5.0–5.8）
tests\                          公用單元測試 + 伺服器整合測試
```

## 授權

OpenSource 發佈內容採用 Apache-2.0。Commercial 模組不包含在 OpenSource 發佈物中，並按照獨立商業授權授權。
