# ForgeMind 故障排除

常見問題與解決辦法（基於公開切片的真實機制）。

## 1. 建置失敗

**直接建置 `ForgeMind.sln` 報「找不到 Commercial\... 專案」**
原因：`ForgeMind.sln` 含商業專案參考條目（`Commercial\ForgeMindLicenseGenerator`、`Commercial\ForgeMindLicenseServer`），但商業目錄不隨開源切片發佈。
解決：逐個建置各專案（`contributing.md` §3），不要建置整個 sln；或使用發佈方提供的建置腳本。

**建置外掛後宿主仍看不到工具**
- 確認外掛 DLL 與同名 `.manifest.json` 已放入宿主 `plugins\<外掛名>\`（或 `FORGEMIND_PLUGINS_DIR` 指定目錄）；
- 檢查宿主日誌是否外掛掃描跳過了該包（`PluginLoader` 只跳過單個失敗包，不影響宿主）；
- 重啟宿主（工具表在首次存取時觸發延遲載入）。

## 2. 伺服器啟動問題

**連接埠被佔用**
`http://127.0.0.1:8765` 被佔用時報 `HttpListenerException`。
解決：用 `--port N` 或環境變數 `FORGEMIND_PORT` 更換連接埠；相應更新用戶端位址。

**用戶端無法連接 / tools/list 為空**
- 確認伺服器監聽中（日誌 `Listening on http://127.0.0.1:<port>/mcp`）；
- 確認用戶端 URL 為 `http://127.0.0.1:8765/mcp`（HTTP 模式）；
- 確認外掛已載入（見上）。

## 3. 引擎 / 橋連接問題

**編輯器已開啟，但工具報「引擎未連接 / 橋不可用」**
- 確認專案已啟用 `ForgeMindForUE` 外掛（`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`）；
- 橋介面卡讀 `%USERPROFILE%\.forgemind\bridge.json` 完成探索與驗證；檔案缺失 / 憑證不符時連接失敗；
- 檢視 UE 編輯器日誌 `LogForgeMindBridge`；斷線按指數退避重連，請稍候重連。

**`launch` / `build` 卡住**
長任務非同步執行；用 `get_task_result` 查詢結果、`list_executing_tasks` 檢視進行中任務；`cleanup_tasks` 可清理記錄。

## 4. 測試問題

**`ForgeMindServerTest` 預建置失敗**
`--NoBuild` 跳過預建置（CI 已保證產物新鮮時使用）；預設流程會建置宿主並分發外掛到 `<入口>\plugins\`，失敗直接結束不執行案例。

**測試被 fail-fast 中斷**
整合測試按 Group / Order 執行且 fail-fast：遇第一個失敗即停（同 Group 內必跑案例仍會執行）。修復後用 `ForgeMindServerTest.exe [group]` 重跑該組。

**測試依賴編輯器 / 範例專案**
`Task\UE5_0\` 下案例需 UE 編輯器與工作階段；純資料處理案例（SPK / 掃描）在 `ForgeMind.PublicTests` 中執行，無編輯器依賴。

## 5. 資料 / 資產問題

**`asset` 讀取失敗**
`.uasset` 離線讀取必須匹配引擎版本（`UAssetVersions`）；版本不符時解析可能失敗。確認引擎版本在工具支援範圍內。

**`.spk` 無法載入**
檢查是否由 `ForgeMind.EngineScanner <spkPath>` 產生且未損壞；SPK 含 CRC 校驗（`Speedy\Internal\Crc32`），損壞檔案會被拒絕載入。

## 6. 其他

**如何開啟日誌**
伺服器日誌由 `LogManager` 輸出（類別含 `ForgeMindServerEntry`、`ForgeMindServerHost`、`ForgeMindServerBridgeServer` 等）；橋側檢視 UE 編輯器輸出日誌。

**商業能力相關問題**
DSL 語法、授權與 License 問題請聯繫發佈方；OpenSource 版本不解析 license 檔案，能力固定且開放（見 `security.md`）。
