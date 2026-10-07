# ForgeMind 架構

ForgeMind 是一個**以 MCP 為介面的遊戲引擎控制平台**：伺服器（宿主）透過 MCP 協定對 AI 用戶端暴露工具，工具能力主要由外掛注入；引擎側由介面卡外掛透過本機 TCP 橋承接操作。UE 是目前實作，平台能力可擴充至其他引擎。本文基於**公開切片**可見程式碼描述，商業能力僅在 §7 概述。

## 1. 總體檢視

```
┌─────────────┐   MCP(HTTP/stdio)   ┌──────────────────────┐
│ AI 用戶端     │ ◄────────────────► │ ForgeMindServer(宿主)  │
└─────────────┘                     │  • 內建工具 ×4         │
                                    │  • PluginLoader       │
                                    │  • TCP 橋伺服器         │
                                    │  • 引擎註冊表           │
                                    └──────────┬───────────┘
                                               │ 本機 TCP 橋（4 位元組長度 + JSON 信封）
                                    ┌──────────▼───────────┐
                                    │ UE 介面卡外掛          │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **宿主**（`OpenSource\ForgeMindServer`）：MCP 伺服器本體。
- **外掛**（`OpenSource\ForgeMind.*Plugin`）：透過 `IPlugin` 註冊工具，宿主只內建最小工具集。
- **引擎介面卡**（`UEProject\...\Plugins\ForgeMindForUE`）：編輯器內外掛，作為橋的伺服器實作引擎側能力。
- **掃描 / 打包**（`ForgeMind.Scan.Abstractions`、`ForgeMind.Scan`、`ForgeMind.Scan.Unreal`、`ForgeMind.Speedy`、`ForgeMind.EngineScanner`）：為引擎離線能力提供索引與 SPK 打包。
- **範例專案**（`UEProject\ForgeMind_5_0`）：供測試與聯調。

## 2. 宿主（ForgeMindServer）

- **入口**：`Program.cs` 預設以 HTTP 常駐服務執行於 `127.0.0.1:8765`（Streamable HTTP + 相容 SSE），`--stdio` 切換 spawn-per-client 模式；連接埠由 `--port` / `FORGEMIND_PORT` 覆蓋。
- **內建工具**（`ForgeMindServerHost` 的工具表，僅 4 個）：
  - `settings`（`SettingsTool`，讀/寫設定）；
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks`（長任務查詢與清理）。
- **外掛載入**：`PluginLoader` 掃描 `plugins\`（預設 `<宿主目錄>\plugins\`，`FORGEMIND_PLUGINS_DIR` 覆蓋），經 `PluginContext` 注入工具表、引擎註冊表與橋，外掛 `OnLoad` 呼叫 `RegisterTool` 注入工具。外掛包支援「子目錄 = 外掛包」（主 DLL + 同名 manifest + 依賴）與「根目錄鬆散 DLL」兩種佈局。載入採用延遲求值（首次存取工具表時觸發），失敗僅跳過該外掛，不影響宿主。
- **引擎註冊表**：`EngineRegistry` 在啟動時列舉本機引擎（優先 Launcher 安裝 `UE_x.y`，回退原始碼建置），唯讀快照注入外掛。
- **TCP 橋伺服器**：`ForgeMindServerBridgeServer`，供引擎介面卡探索與連接（見 §5）。

## 3. 外掛與工具注入

| 外掛 | 提供工具 | 職責 |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | 引擎與專案管理、啟動/建置/編輯器操作 |
| `ForgeMind.Assets` | `asset` | 離線讀取 `.uasset` 結構（`UAssetArchive` / `BlueprintGraphReader` 等） |
| `ForgeMind.Knowledge` | `knowledge` | 知識檢索（資料夾來源 / 索引包來源，`KnowledgeStore`） |
| `ForgeMind.EngineScanner` | （命令列工具，非 MCP 工具） | 列舉引擎 → `UnrealScanner` 掃描 → `ScanResultPacker` 打包 `.spk` |

宿主與外掛透過 `ITool` 契約互動（`Compat\ITool.cs`）：名稱 / 描述 / 參數 Schema / `Execute` 回傳 `ToolResult`。`ForgeMindServerTest` 也基於同一 `ITool` 直呼工具（不經 MCP 協定）驗證行為。

## 4. 掃描與 SPK（離線能力底座）

- `ForgeMind.Scan.Abstractions`：`IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` 等抽象。
- `ForgeMind.Scan.Unreal`：`UnrealScanner` + `DocumentEngine`（`Ue5DocumentEngine`、`HeaderScanner`、`RulesCompiler`、`UAssetScanner`、`ScanIndexStore` 等）——把 UE 標頭檔、規則檔與藍圖資產解析為掃描索引。`ScanIndexStore` 是執行時期記憶體索引，不落盤；它**不等同於舊版 ScanCache**——目前流程已不再使用 ScanCache 預載入 / 散落快取（原始碼中僅保留少量歷史相容痕跡，不構成現行機制）。
- `ForgeMind.Scan`：`ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` 組裝與讀取掃描包。
- `ForgeMind.Speedy`：`SpeedyPack` 提供高效能打包格式（`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`，內部含 CRC、目錄對映、寫前日誌 `WriteAheadLog` 等實作）。
- 使用方式：`ForgeMind.EngineScanner <spkPath>` 輸出 `.spk`；knowledge 等能力透過 `SpkScanDataProvider` 消費。

## 5. 橋協定與引擎介面

- 橋：本機 TCP，訊息為 4 位元組長度前綴 + JSON 信封（`BridgeMessage` / `BridgeSession` / `FrameCodec`）。介面卡先讀 `%USERPROFILE%\.forgemind\bridge.json` 完成探索與驗證，再連接 → 握手 → 訊息迴圈。
- 伺服器側按命令名稱呼叫介面卡實作；命令處理器在引擎主執行緒（UE 遊戲執行緒）執行，保證與編輯器狀態一致。
- 引擎介面卡（UE 側）位於 `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`：Editor 模組，含橋用戶端與 IR 應用器。UE 範例專案為 5.0–5.8 相容。
- 新增引擎只需實作同構橋用戶端與命令處理器（見 `development-guide.md` §4），宿主與外掛無需變更。

## 6. 授權與安全（公開可見部分）

- 預設授權：`OpenAuthorizationService`（`Authorization\Open\`）——社群固定能力，**不解析 license 檔案**。
- 外掛安全：`PluginIntegrityVerifier`（清單完整性校驗）、`PluginSigningKeys`（簽章驗證）；`ManifestLoader` 解析外掛 manifest，支援 ServiceOverride（按抽象類型替換宿主服務，如 `IAuthorizationService`）。
- 橋驗證：介面卡連接需讀取本機 `bridge.json` 憑證完成握手（詳見 `security.md`）。

## 7. 商業能力（概述）

- **DSL 語言與領域翻譯器**（閉源）：以人類可讀文字產生引擎資產的商業能力。公開切片僅含 DSL 框架層（`ForgeMindServer\DSL\` 下的 `ParserFactory`、`ITranslator`、`TranslatorRegistry`、`IIrConverter`、`IrConverterRegistry`、`DiagnosticReporter` 等介面與註冊表）；具體語法、領域翻譯器與 IR 規則由商業模組提供，不在切片內。
- **商業授權體系**（閉源）：包括授權簽發、授權驗證、授權服務及可選的環境綁定能力。具體實作、簽章演算法、授權檔案格式和伺服器端邏輯不屬於公開切片；對外只呈現「提供額外能力」。

> OpenSource 版本提供全部開源工具集與橋能力；Commercial 版本基於相同的宿主、外掛機制與橋協定擴充商業能力（DSL 資產產生、商業授權與企業支援）。

## 8. 能力邊界表

| 能力 | 歸屬 |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| DSL 擴充介面 | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR 規則 | Commercial 🔒 |
| DSL Asset Generator | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| 商業授權實作 | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| 私鑰 / 簽發系統 | Commercial 🔒 |
| 授權協定內部細節 | Commercial 🔒 |
