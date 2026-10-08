# Rychlý start ForgeMind

ForgeMind je **platforma pro ovládání herních enginů s rozhraním MCP**: prostřednictvím protokolu MCP zpřístupňuje AI klientům správu projektů Unreal Engine, čtení assetů a operace spuštění/sestavení/editoru. Aktuální implementace je UE; platforma je rozšiřitelná. Open source verze obsahuje sadu enginových nástrojů a bridge funkce; komerční verze přidává generování DSL assetů (§5).

Všechny kroky této příručky lze dokončit pomocí **open source části** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Požadavky prostředí

- .NET SDK (pro sestavení hostu a pluginů).
- Unreal Engine 5.0–5.8 (včetně editoru). Ukázkový projekt je `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- Klient kompatibilní s MCP (Claude Desktop / Cursor / vlastní klient).

## 2. Sestavení serveru a pluginů

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Pluginy (po sestavení umístěte DLL + .manifest.json stejného názvu do adresáře plugins hostu)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Nesestavujte celý `ForgeMind.sln` přímo (obsahuje odkazy na komerční projekty; komerční adresář není součástí repozitáře). Pluginy lze také použít z buildovacích artefaktů dodaných distributorem.

## 3. Spuštění

1. **Spusťte server** (výchozí rezidentní HTTP, port 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Nebo spusťte zkompilovaný ForgeMindServer.exe; nastavitelné --port N / --stdio / FORGEMIND_PORT
   ```

2. **Otevřete editor UE a ukázkový projekt**: spusťte `ForgeMind_5_0.uproject` v UE; plugin `ForgeMindForUE` v editoru automaticky spustí bridge klienta a připojí se k hostu.

3. **Nakonfigurujte klienta**: přidejte server do MCP klienta:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Po připojení se zobrazí seznam nástrojů (4 vestavěné + injektované pluginy).

## 4. První použití

- `engine` — zobrazení informací o aktuálním enginu;
- `project` / `project_files` — zobrazení ukázkového projektu a jeho souborů;
- `launch` — spuštění editoru, `editor` — operace v editoru;
- `asset` — čtení struktury assetů projektu;
- `knowledge` — vyhledávání znalostí projektu.

> Dlouhé úlohy (launch/build atd.) se provádějí asynchronně. Výsledky získáte pomocí `get_task_result`, probíhající úlohy zobrazíte pomocí `list_executing_tasks` a vyčistíte pomocí `cleanup_tasks`.

## 5. Komerční verze (DSL)

Modul Commercial je samostatná komerční verze a není součástí open source repozitáře. Komerční verze nabízí **generování DSL assetů**: zapisujte Blueprinty, materiály atd. jako čitelný text a převádějte je na enginové assety. Konkrétní DSL nástroje, funkce textového převodu a podrobnosti syntaxe se řídí dokumentací komerční verze. Komerční licenční funkce rovněž patří do Commercial. OpenSource DSL nástroje neobsahuje.

## 6. Další kroky

- Architektura a mechanismy: `architecture.md`
- Seznam nástrojů a parametrů: `tools-reference.md`
- Vývoj a přispívání: `development-guide.md` / `contributing.md`
- Časté otázky: `troubleshooting.md`
