# ForgeMind 貢獻指南

感謝你對 ForgeMind 感興趣。公開倉庫對應 ForgeMind 的 OpenSource 發佈切片，僅包含對外公開的開源專案、測試與範例專案。商業模組（DSL 領域翻譯器、授權體系等）不在公開倉庫內，請先理解範圍劃分再參與貢獻。

## 1. 公開倉庫結構

開源切片（你 clone 的倉庫）頂層結構如下：

```
ForgeMind.sln                          # 解決方案（含商業專案參考，見下）
OpenSource\                            # 開源專案
  ForgeMindServer\                     # MCP 伺服器（宿主）
  ForgeMind.OfflineEngineKit\          # 引擎離線工具外掛（engine/project/launch/build/editor 等）
  ForgeMind.Assets\                    # 資產讀取外掛（asset）
  ForgeMind.Knowledge\                 # 知識檢索外掛（knowledge）
  ForgeMind.EngineScanner\             # 引擎掃描器（命令列）
  ForgeMind.Scan\                      # 掃描打包
  ForgeMind.Scan.Abstractions\         # 掃描抽象
  ForgeMind.Scan.Unreal\               # UE 掃描實作（DocumentEngine）
  ForgeMind.Speedy\                    # SPK 打包格式
UEProject\
  ForgeMind_5_0\                       # 範例 UE 專案（5.0–5.8 相容）
tests\
  ForgeMind.PublicTests\               # 公用單元測試（SPK / 掃描資料）
  ForgeMindServerTest\                 # 伺服器整合測試（需 UE 編輯器）
```

> 注意：目前開發倉庫的解決方案檔（`ForgeMind.sln`）包含 Commercial 專案參考（如 `Commercial\ForgeMindLicenseGenerator`、`Commercial\ForgeMindLicenseServer`）；公開發佈**不含 `Commercial/` 目錄**，因此 OpenSource 使用者應逐個建置公開專案（見 §3），不要直接 `dotnet build ForgeMind.sln`。

## 2. 開源邊界

- **OpenSource\**：全部開源（Apache-2.0），是貢獻的主要區域。
- **UEProject\**：範例專案開源，但切片導出時排除 `*.uasset` 二進位資產（Content 下只保留地圖等文字資產與目錄骨架）。
- **tests\**：開源，貢獻需附帶測試。
- **Commercial 不在公開倉庫**：DSL 領域翻譯器、商業授權等不出現在切片中；開源程式碼禁止引用商業模組型別。
- 導出自動排除 `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache` 等建置產物與臨時目錄；檔頭授權標識在導出時統一替換為 Apache-2.0。

## 3. 建置

```powershell
# 逐個建置（不要直接建置整個 sln，見上）
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# 外掛專案（建置後把輸出 + 同名 .manifest.json 放入宿主 plugins 目錄）
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

宿主從 `<宿主目錄>\plugins\` 載入外掛（環境變數 `FORGEMIND_PLUGINS_DIR` 覆蓋）；外掛包 = 主 DLL + 同名 `.manifest.json` + 依賴（見 `development-guide.md` §3）。

## 4. 程式碼規範

- 所有原始檔頂部保留 SPDX 授權標識與版權聲明（目前 Apache-2.0）。
- 命名空間按模組組織（`ForgeMindServer.*` / `ForgeMind.*`）；公共契約走抽象介面如 `ITool` / `IPlugin`。
- 伺服器工具類必須提供完整 `Description` 與參數 Schema（這些文字會直接展示給 AI 用戶端）。
- 引擎介面卡程式碼遵循對應引擎編碼規範（目前 UE 側：F/U/T 型別前綴、`TEXT()` 巨集、遊戲執行緒安全約束）。
- 新程式碼不得引用或依賴任何商業模組。

## 5. PR 流程

1. 從 `main` 建立分支，命名建議 `feat/<描述>` 或 `fix/<描述>`。
2. 提交訊息：`<type>: <一行描述>`。
3. 提交前自檢：
   - 按 §3 逐個建置全部通過；
   - 橋命令 / 工具行為變更時，相關 `ForgeMindServerTest` Group 通過（執行方式見 `development-guide.md` §6）；
   - SPK / 掃描資料變更時，`ForgeMind.PublicTests` 通過；
   - 未引入商業模組引用；
   - 同步更新 `docs\zh-CN\` 對應文件。
4. 開 PR 時填寫：變更內容、測試結果摘要、是否需要維護者重點review特定模組。
5. 維護者review重點：開源邊界、API 相容性、測試覆蓋。

## 6. 測試要求

- 橋命令 / 工具行為 / 介面卡邏輯變更必須附帶測試案例（在 `tests\ForgeMindServerTest\Task\` 下新增 `*_Tests.cs`）。
- 伺服器整合測試需要 UE 編輯器環境；純資料處理測試（SPK、掃描）放在 `tests\ForgeMind.PublicTests\`，無引擎依賴。
- 案例必須在預設順序下穩定通過（fail-fast，遇第一個失敗即停）。

## 7. 文件貢獻

- 中文文檔位於 `docs\zh-CN\`；內容必須與原始碼事實一致，不確定項標註「待確認」。
- 變更對外工具清單時，同步更新 `tools-reference.md`。
- 商業能力（DSL 語法、授權機制）在文件中只作概述，不展開內部細節。

## 8. 行為準則

- 尊重既有設計決策（橋協定、工具契約、SPK 格式）；重大變更先發起討論。
- 對外不貶低同類產品；宣傳內容遵循 `promo.md` 的對外紀律。
