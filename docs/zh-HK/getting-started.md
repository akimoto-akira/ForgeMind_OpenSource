# ForgeMind 快速上手

ForgeMind 是一個**以 MCP 為介面的遊戲引擎控制平台**：透過 MCP 協定把 Unreal Engine 的專案管理、資產讀取、啟動/建置/編輯器操作等能力開放給 AI 用戶端；目前實作為 UE，平台可擴充。OpenSource 版本覆蓋引擎工具集與橋能力；商業版追加 DSL 資產產生等（見 §5）。

本指南的所有步驟均可基於**開源切片**完成（`OpenSource\` + `UEProject\` + `tests\`）。

## 1. 環境需求

- .NET SDK（宿主與外掛建置）。
- Unreal Engine 5.0–5.8（含編輯器），範例專案為 `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`。
- 支援 MCP 的用戶端（Claude Desktop / Cursor / 自訂用戶端均可）。

## 2. 建置伺服器與外掛

```powershell
# 宿主
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# 外掛（建置後把 DLL + 同名 .manifest.json 放入宿主 plugins 目錄）
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> 不要直接建置整個 `ForgeMind.sln`（其含商業專案參考，商業目錄不隨切片發佈）。外掛亦可由發佈方提供預建置產物。

## 3. 啟動

1. **啟動伺服器**（預設 HTTP 常駐，連接埠 8765）：

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # 或執行編譯產物 ForgeMindServer.exe；可用 --port N / --stdio / FORGEMIND_PORT 調整
   ```

2. **開啟 UE 編輯器與範例專案**：用 UE 啟動 `ForgeMind_5_0.uproject`，編輯器內 `ForgeMindForUE` 外掛會自動啟動橋用戶端並連接宿主。

3. **設定用戶端**：在 MCP 用戶端中新增伺服器：

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   連接後即可看到工具清單（4 個內建 + 外掛注入）。

## 4. 第一次使用

- 用 `engine` 工具檢視目前引擎資訊；
- 用 `project` / `project_files` 檢視範例專案與檔案；
- 用 `launch` 啟動編輯器、`editor` 進行編輯器操作；
- 用 `asset` 讀取專案資產結構；
- 用 `knowledge` 檢索專案知識。

> 長任務（launch/build 等）非同步執行，透過 `get_task_result` 查詢結果、`list_executing_tasks` 檢視進行中任務、`cleanup_tasks` 清理。

## 5. 商業版（DSL）

Commercial 模組屬於獨立商業發佈內容，不包含在 OpenSource 倉庫中。商業版提供 **DSL 資產產生**：用人類可讀文字描述藍圖、材質等內容，並將其轉換為引擎資產；具體 DSL 工具、文字轉換能力與語法細節以商業版文件為準。商業授權能力同樣屬於 Commercial。OpenSource 不包含 DSL 工具。

## 6. 下一步

- 架構與機制：`architecture.md`
- 工具清單與參數：`tools-reference.md`
- 開發與貢獻：`development-guide.md` / `contributing.md`
- 常見問題：`troubleshooting.md`
