# ForgeMind 安全

本文描述公開切片中的安全設計與使用建議。

## 1. 本機通訊邊界

- **伺服器預設只監聽回環位址**：HTTP 模式監聽 `127.0.0.1:8765`，不對外網暴露。
- **橋是本機 TCP**：位於宿主與引擎介面卡之間；介面卡必須讀取 `%USERPROFILE%\.forgemind\bridge.json`（含主機、連接埠、憑證）完成探索與驗證後才握手。
- 不要將伺服器連接埠暴露到公網；需要遠端使用時放在可信網路並經安全通道（如 SSH 隧道）轉發。

## 2. 外掛完整性

- 載入外掛時，宿主 `PluginLoader` 會做**清單完整性校驗**（`PluginIntegrityVerifier`）與簽章驗證（`PluginSigningKeys`）：外掛包必須含主 DLL + 同名 `.manifest.json`，缺失或不匹配的包會被拒絕 / 跳過。
- 外掛注入的工具在宿主程序內執行；只安裝可信來源的外掛（自公開切片建置或官方發佈）。

## 3. 授權模型

**公開披露的安全機制**：
- `IAuthorizationService`：宿主授權入口抽象；預設實作 `OpenAuthorizationService`（社群固定能力，不解析任何 license 檔案，不產生試用授權）。
- 受信任的 ServiceOverride：外掛可透過 manifest 中的 ServiceOverride 按抽象類型替換宿主服務（宿主不感知「商業」概念，只按類型替換）。
- 外掛身分驗證與 manifest 校驗：`PluginIntegrityVerifier` / `PluginSigningKeys` 驗證外掛 manifest 與簽章，確保只載入受信任外掛。
- 橋連接驗證：介面卡透過 `bridge.json` 完成本機探索與憑證握手，確認連接來源可信。

**商業授權**：Core 提供 `IAuthorizationService` 抽象與受信任的 ServiceOverride 擴充機制。商業授權實作為閉源外掛；具體授權協定、簽發、驗證與綁定邏輯不屬於公開切片。

## 4. 工具權限

- 引擎操作工具受 `ServerPermissionChecker`（位於各外掛）約束，執行前檢查能力授權。
- 工具 `Execute` 失敗統一回傳 `ToolResult` 失敗訊息，不洩漏內部堆疊細節。

## 5. 使用建議

- 只對可信用戶端開放 MCP 連接埠；用戶端具備引擎操作能力，等同本機管理員權限。
- 保持外掛、宿主與範例專案更新，及時獲得安全性修復。
- 共享機器上不要把 `bridge.json` 憑證複製給不可信程序。
- 安全問題透過發佈方管道回報；未修復漏洞細節不對外公開。
