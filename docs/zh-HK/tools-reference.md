# ForgeMind 工具參考

全部工具按「宿主內建」與「外掛注入」劃分。目前 OpenSource 發佈切片包含 13 個 MCP 工具；商業外掛可以在此基礎上提供額外工具（如 DSL 資產產生，見 §3，不在開源清單）。

## 1. 宿主內建工具

宿主（`OpenSource\ForgeMindServer`）自帶以下工具，無需外掛：

| 工具 | 說明 |
|---|---|
| `settings` | 讀寫伺服器設定（`McpSettingsManager`，變更即時持久化） |
| `get_task_result` | 查詢長任務（launch / build 等）的執行結果 |
| `list_executing_tasks` | 列出目前進行中的長任務 |
| `cleanup_tasks` | 清理已結束的任務記錄 |

## 2. 外掛注入工具

外掛在 `OnLoad` 時經 `PluginContext.RegisterTool` 注入；用戶端連接後即可看到。建置後需把外掛 DLL + 同名 `.manifest.json` 放入宿主 `plugins\` 目錄（或 `FORGEMIND_PLUGINS_DIR` 指定目錄）。

### ForgeMind.OfflineEngineKit（7 個）

| 工具 | 說明 |
|---|---|
| `engine` | 查詢本機已安裝引擎（Launcher / 原始碼建置，含版本與應用名稱） |
| `project` | 查詢 / 管理 UE 專案（定位專案檔案、檢視專案資訊） |
| `project_files` | 列出專案檔案（按目錄 / 類型篩選） |
| `switch_version` | 切換專案到指定引擎版本（經 UnrealVersionSelector） |
| `launch` | 啟動編輯器或打包後的可執行檔（支援 debug 設定） |
| `build` | 建置專案（預設目標 / Editor 目標） |
| `editor` | 編輯器內操作（場景、視埠、資產等，經橋下發） |

參數 Schema 見各工具原始碼 `GetParameterSchema()`（`OpenSource\ForgeMind.OfflineEngineKit\*.cs`）。部分引擎操作受 `ServerPermissionChecker` 權限約束。

### ForgeMind.Assets（1 個）

| 工具 | 說明 |
|---|---|
| `asset` | 離線讀取 `.uasset` 結構（名稱、物件、屬性、藍圖圖，支援按圖/屬性查詢） |

實作基於 `UAsset\UAssetSerializer` / `BlueprintGraphReader` 等（`OpenSource\ForgeMind.Assets\`）。

### ForgeMind.Knowledge（1 個）

| 工具 | 說明 |
|---|---|
| `knowledge` | 知識檢索：查詢專案 / 引擎知識條目（來源：資料夾來源 `FolderKnowledgeSource`、SPK 索引包來源 `IndexPackSource`） |

實作見 `OpenSource\ForgeMind.Knowledge\Knowledge\`。

## 3. 商業版工具（概述）

商業版可追加 DSL 資產產生及相關查詢工具，由商業外掛注入；具體工具名稱、API 與 DSL 能力以商業版文件為準。OpenSource 不包含 DSL 工具。

## 4. 工具呼叫約定

- 長任務工具回傳任務 ID；用 `get_task_result` 輪詢結果。
- 工具失敗回傳 `ToolResult` 失敗訊息（可直接展示給用戶端）。
- 工具描述與參數 Schema 即 MCP `tools/list` 所見內容，保持與原始碼一致。
