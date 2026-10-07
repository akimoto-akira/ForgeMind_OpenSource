# ForgeMind API 參考

本文描述 ForgeMind 對外介面的公開部分：MCP 協定接入、橋協定、工具契約與長任務機制。商業能力（DSL）僅 §6 概述。

## 1. MCP 接入

- **HTTP 模式**（預設）：`http://127.0.0.1:8765/mcp`，Streamable HTTP（相容舊版 SSE）。宿主程序常駐，一次連接可跨多個用戶端工作階段。
- **stdio 模式**：`ForgeMindServer.exe --stdio`，spawn-per-client。
- 連接埠：`--port N` > 環境變數 `FORGEMIND_PORT` > 預設 8765。

支援標準 MCP 方法：`initialize`、`tools/list`、`tools/call`、`notifications/cancelled` 等；工具 Schema 由 `ITool.GetParameterSchema()` 提供。

## 2. 工具契約

伺服器工具實作統一介面（`OpenSource\ForgeMindServer\Compat\ITool.cs`）：

- `Name` / `DisplayName`：工具識別與展示名；
- `Description`：面向 AI 用戶端的說明文字；
- `GetParameterSchema()`：參數宣告（JSON Schema 風格）；
- `Execute(Guid callerId, Dictionary<string, object> parameters)`：執行並回傳 `ToolResult`（成功結果或失敗訊息）。

呼叫流程：用戶端 `tools/call` → 宿主定位 `ITool` → 參數驗證 → `Execute` → 結果回傳。涉及引擎側能力的工具內部經 TCP 橋下發命令。

## 3. 橋協定（宿主 ↔ 引擎介面卡）

- 傳輸：本機 TCP；訊框格式 = 4 位元組長度前綴 + JSON 信封（`BridgeMessage`，見 `Bridge\BridgeMessage.cs` / `FrameCodec.cs`）。
- 探索與驗證：介面卡讀取 `%USERPROFILE%\.forgemind\bridge.json`（含主機、連接埠與憑證），連接 → 握手 → 訊息迴圈。
- 命令語意：請求含命令名稱與參數；伺服器按命令路由到介面卡實作；回應含狀態碼、訊息與資料。引擎側命令在引擎主執行緒（UE 遊戲執行緒）執行。
- 生命週期：宿主啟動即監聽橋；介面卡斷線按指數退避重連。

## 4. 長任務機制

- 長任務（如 `launch`、`build`）非同步執行，不阻塞工具呼叫。
- 相關內建工具：
  - `get_task_result`：查詢任務結果；
  - `list_executing_tasks`：列出進行中的任務；
  - `cleanup_tasks`：清理任務記錄。
- 宿主內部 `LongTaskManager` 管理任務生命週期（`LongTaskManager.cs` / `LongTaskTools.cs`）。

## 5. 內建工具（宿主自帶，無需外掛）

| 工具 | 說明 |
|---|---|
| `settings` | 讀寫伺服器設定（`McpSettingsManager`，變更即時持久化） |
| `get_task_result` | 查詢長任務結果 |
| `list_executing_tasks` | 檢視進行中任務 |
| `cleanup_tasks` | 清理任務記錄 |

外掛注入的完整工具清單見 `tools-reference.md`。

## 6. 商業能力（DSL，概述）

- OpenSource 提供 **DSL 擴充基礎契約**（`ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` 等抽象介面，可繼續公開使用）；具體 DSL 語法實作、Parser、Translator、IR 規則及資產產生邏輯屬於 Commercial，不隨 OpenSource 發佈。
- DSL 工具由商業外掛注入，具體工具名稱與 API 以商業版文件為準；OpenSource 工具清單不含 DSL 工具。

| 能力 | 歸屬 |
|---|---|
| DSL 擴充介面 | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR 規則 | Commercial 🔒 |
| 資產產生邏輯 | Commercial 🔒 |

## 7. 相容性說明

- 橋協定訊框格式與工具 Schema 以本文為準；破壞性變更會先發佈遷移說明。
