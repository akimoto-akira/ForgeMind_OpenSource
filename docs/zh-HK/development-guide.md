# ForgeMind 開發指南

面向希望擴充 ForgeMind 的開發者：如何建置、除錯、新增 MCP 工具、新增引擎介面卡、執行測試。本文基於**公開切片**（`OpenSource\`、`UEProject\`、`tests\`）的真實程式碼撰寫。

## 1. 專案總覽

| 專案 | 職責 |
|---|---|
| `OpenSource\ForgeMindServer` | MCP 伺服器宿主：4 個內建工具、啟動 TCP 橋、載入外掛 |
| `OpenSource\ForgeMind.OfflineEngineKit` | 外掛：engine / project / project_files / switch_version / launch / build / editor 工具 |
| `OpenSource\ForgeMind.Assets` | 外掛：asset 工具（離線 `.uasset` 讀取） |
| `OpenSource\ForgeMind.Knowledge` | 外掛：knowledge 工具（知識檢索） |
| `OpenSource\ForgeMind.EngineScanner` | 命令列掃描器：列舉引擎並打包 `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | 掃描索引：抽象、打包、UE 實作（DocumentEngine） |
| `OpenSource\ForgeMind.Speedy` | SPK 打包格式（高效能包檔） |
| `tests\ForgeMind.PublicTests` | 公用單元測試（不依賴引擎） |
| `tests\ForgeMindServerTest` | 伺服器整合測試（需 UE 編輯器） |
| `UEProject\ForgeMind_5_0` | 範例 UE 專案（5.0–5.8） |

## 2. 建置

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# 外掛與程式庫專案：見 contributing.md §3（不要直接建置整個 ForgeMind.sln，其含商業專案參考）
```

- 宿主入口：`OpenSource\ForgeMindServer\Program.cs`（預設 HTTP 常駐 `127.0.0.1:8765`，`--stdio` 切換，`--port` / `FORGEMIND_PORT` 覆蓋連接埠）。
- 記錄：`LogManager`；工具回傳 `ToolResult`（成功 / 失敗附訊息）。

## 3. 外掛機制與新增 MCP 工具

宿主只有 4 個內建工具：`settings`、`get_task_result`、`list_executing_tasks`、`cleanup_tasks`，其餘由 **plugins 目錄下的外掛**注入：

- 外掛目錄：`<宿主目錄>\plugins\`（環境變數 `FORGEMIND_PLUGINS_DIR` 覆蓋）。
- 外掛包佈局：主 DLL + 同名 `.manifest.json` + 依賴放入 `plugins\<外掛名>\`；根目錄鬆散 DLL 亦支援（手動放入）。
- 宿主 `PluginLoader` 掃描外掛 → `PluginContext` 注入（工具表、引擎註冊表、橋）→ 外掛 `OnLoad` 呼叫 `RegisterTool` 註冊工具。
- 外掛完整性：`PluginIntegrityVerifier` / `PluginSigningKeys` 負責外掛 manifest 與簽章驗證；受信任外掛可透過 ServiceOverride 擴充服務（見 §7）。

新增一個 MCP 工具：

1. 實作 `ITool` 契約（`OpenSource\ForgeMindServer\Compat\ITool.cs`）：
   - `Name` / `DisplayName`：工具名稱與展示名；
   - `Description`：面向 AI 用戶端的說明；
   - `GetParameterSchema()`：JSON Schema 風格參數宣告；
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`：回傳 `ToolResult`。
2. 新建外掛專案（或併入既有外掛如 OfflineEngineKit），實作 `IPlugin`，在 `OnLoad` 呼叫 `RegisterTool`。
3. 同目錄放置同名 `.manifest.json`（外掛描述 / 依賴宣告）。
4. 需要引擎側能力時，經橋命令下發，並在介面卡側實作命令處理器（見 §5）。
5. 編寫測試案例（§6），並更新 `tools-reference.md`。

## 4. 如何新增引擎介面卡

ForgeMind 用「介面卡」承接引擎特有邏輯；新增引擎按同一橋協定（4 位元組長度前綴 + JSON 信封，見 `api-reference.md`）：

1. 實作橋用戶端：讀 `%USERPROFILE%\.forgemind\bridge.json` 完成探索與驗證，再連接 → 握手 → 訊息迴圈。
2. 按命令名稱註冊白名單處理器；所有命令在引擎主執行緒（相當於 UE 遊戲執行緒）執行。
3. 實作引擎側能力（資產讀寫、專案操作等），對映到伺服器工具回傳。
4. 為該引擎補測試案例與文件。

首個實作即 UE 介面卡 `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`（Editor 模組外掛，橋用戶端 + IR 應用器）。

## 5. 引擎側橋命令（目前 UE 實作）

- 介面卡側 `ForgeMindForUE` 外掛含橋用戶端；命令處理器在遊戲執行緒執行。
- 資產層級操作可複用外掛內的 IR 領域處理器，避免重複編輯器邏輯。
- 除錯：UE 編輯器日誌 `LogForgeMindBridge` 類別；斷線按指數退避重連。

## 6. 執行測試

### 公用單元測試（無需編輯器）

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

覆蓋 SPK 打包往返、多版本 SPK、掃描資料 Provider 等（`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`）。

### 伺服器整合測試（需 UE 編輯器 + 範例專案）

```powershell
# 預設：預建置宿主與外掛、分發到 plugins 目錄、啟動宿主載入外掛，經反射執行全部 ITestCase
ForgeMindServerTest.exe

# 過濾：按 group + order
ForgeMindServerTest.exe [group] [order]          # 或 --Test [group] [order]
# 跳過預建置
ForgeMindServerTest.exe --NoBuild
```

機制：

- 預建置：先建置 `ForgeMindServer.csproj`，再掃描 `ForgeMind.*` 目錄下含同名 `.manifest.json` 的外掛專案逐個建置，分發到 `<入口目錄>\plugins\<外掛名>\`。
- 執行：反射發現 `ITestCase`，按 `Group` / `Order` 排序，**fail-fast**；「啟動編輯器 / 關閉編輯器」也是普通案例（`Order=0` 啟動、`Order=int.MaxValue` 關閉），同一 Group 內共用工作階段。
- 案例經 `ITool.Execute` 直呼工具（不經 MCP 協定）；工具內部經橋與 UE 通訊。
- 輸出：執行完輸出 JSON 報告與可讀摘要（輸出目錄見執行結果訊息）。
- 測試資產：`Task\UE5_0\` 下 30+ 個 `Blueprint_*_Tests.cs`（藍圖領域：變數 / 函式 / 事件 / 材質 / 巨集 / 介面等）+ `Task\Base\Engine_List_Tests.cs`；`BlueprintTestHelper` 封裝橋命令、文字派發與 IR 快照比對（防止意外修改）。

> 注意：涉及「文字路徑（DSL）」的測試案例依賴商業 DSL 能力；公開切片內的整合測試主要走橋命令路徑，以切片內實際 `Task\` 案例為準。

## 7. 授權與商業能力（概述）

- 預設 `OpenAuthorizationService`（`ForgeMindServer\Authorization\Open\`）：社群固定能力，不解析任何 license 檔案。
- 商業授權在 plugin Manifest 中透過 ServiceOverride 替換 `IAuthorizationService` 實作（宿主 `ApplyServiceOverride`）。公開切片不含該商業外掛，只保留替換機制（宿主不感知「商業」概念，只按抽象類型替換）。

## 8. 編碼規範

- 檔頭註釋含 SPDX 授權標識與版權聲明（Apache-2.0）。
- 不得引用商業模組；新外掛遵循 `contributing.md` 的切片邊界。
