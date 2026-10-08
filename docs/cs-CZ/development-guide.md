# Vývojářský průvodce ForgeMind

Pro vývojáře, kteří chtějí rozšiřovat ForgeMind: jak sestavit, ladit, přidávat nové nástroje MCP, přidávat adaptéry enginů a spouštět testy. Dokument vychází z reálného kódu **veřejného výřezu** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Přehled projektu

| Projekt | Odpovědnost |
|---|---|
| `OpenSource\ForgeMindServer` | Host MCP serveru: vestavěné 4 nástroje, spuštění TCP bridge, načítání pluginů |
| `OpenSource\ForgeMind.OfflineEngineKit` | Plugin: nástroje engine / project / project_files / switch_version / launch / build / editor |
| `OpenSource\ForgeMind.Assets` | Plugin: nástroj asset (offline čtení `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Plugin: nástroj knowledge (vyhledávání v znalostní bázi) |
| `OpenSource\ForgeMind.EngineScanner` | Skener příkazového řádku: enumeruje enginy a balí `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Index skenování: abstrakce, balení, implementace UE (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | Formát balení SPK (výkonný soubor balíčku) |
| `tests\ForgeMind.PublicTests` | Veřejné jednotkové testy (bez závislosti na enginu) |
| `tests\ForgeMindServerTest` | Integrační testy serveru (vyžadují editor UE) |
| `UEProject\ForgeMind_5_0` | Ukázkový projekt UE (5.0–5.8) |

## 2. Sestavení

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Projekty pluginů a knihoven: viz contributing.md §3 (nesestavovat přímo celé ForgeMind.sln — obsahuje odkazy na komerční projekty)
```

- Vstupní bod hostu: `OpenSource\ForgeMindServer\Program.cs` (ve výchozím stavu HTTP naslouchá na `127.0.0.1:8765`, přepnutí na `--stdio`, přepsání portu přes `--port` / `FORGEMIND_PORT`).
- Logy: `LogManager`; nástroje vracejí `ToolResult` (úspěch / chyba se zprávou).

## 3. Mechanismus pluginů a nové nástroje MCP

Host má vestavěné pouze 4 nástroje: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. Ostatní nástroje poskytují **pluginy v adresáři plugins**:

- Adresář pluginů: `<adresář_hostu>\plugins\` (přepsání proměnnou prostředí `FORGEMIND_PLUGINS_DIR`).
- Rozložení balíčku pluginu: uvnitř `plugins\<název_pluginu>\` leží hlavní DLL, stejnojmenný `.manifest.json` a závislosti; podporovány jsou i volné DLL v kořenovém adresáři (ruční drop-in).
- `PluginLoader` hostu skenuje pluginy → injekce `PluginContext` (tabulka nástrojů, registr enginů, bridge) → plugin volá `RegisterTool` v `OnLoad` pro registraci nástrojů.
- Integrita pluginů: `PluginIntegrityVerifier` / `PluginSigningKeys` ověřují manifesty a podpisy; důvěryhodné pluginy mohou rozšiřovat služby přes ServiceOverride (viz §7).

Pro přidání nového nástroje MCP:

1. Implementujte kontrakt `ITool` (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: název a zobrazovaný název nástroje;
   - `Description`: popis určený pro AI klienty;
   - `GetParameterSchema()`: deklarace parametrů ve stylu JSON Schema;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: vrací `ToolResult`.
2. Vytvořte nový projekt pluginu (nebo přidejte do existujícího, např. OfflineEngineKit), implementujte `IPlugin` a zavolejte `RegisterTool` v `OnLoad`.
3. Do stejného adresáře umístěte stejnojmenný `.manifest.json` (popis pluginu / deklarace závislostí).
4. Pro funkce na straně enginu posílejte příkazy přes bridge a implementujte zpracování příkazů na straně adaptéru (viz §5).
5. Napište testovací případy (§6) a aktualizujte `tools-reference.md`.

## 4. Jak přidat nový adaptér enginu

ForgeMind zpracovává logiku specifickou pro engine přes „adaptéry"; nový engine se řídí stejným bridge protokolem (4bajtový prefix délky + JSON obálka, viz `api-reference.md`):

1. Implementujte klienta bridge: přečtěte `%USERPROFILE%\.forgemind\bridge.json` pro discovery a autentizaci, připojení → handshake → smyčka zpráv.
2. Zaregistrujte obsluhy do whitelistu podle názvů příkazů; všechny příkazy se provádějí v hlavním vlákně enginu (obdoba game thread v UE).
3. Implementujte funkce na straně enginu (čtení/zápis assetů, operace s projektem atd.) a namapujte je na výsledky nástrojů serveru.
4. Přidejte testovací případy a dokumentaci pro tento engine.

První implementací je UE adaptér `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (plugin modulu Editor, klient bridge + aplikátor IR).

## 5. Bridge příkazy na straně enginu (aktuální implementace UE)

- Plugin `ForgeMindForUE` na straně adaptéru obsahuje klienta bridge; obsluhy příkazů se provádějí v game thread.
- Operace na úrovni assetů mohou znovu použít IR obsluhy pluginu, čímž se vyhnete duplikaci logiky editoru.
- Ladění: kategorie `LogForgeMindBridge` v logech editoru UE; při odpojení dochází k opětovnému připojení s exponenciálním backoff.

## 6. Spouštění testů

### Veřejné jednotkové testy (bez editoru)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Pokrývají round-trip balení SPK, víceverzní SPK a poskytovatele dat skenování (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Integrační testy serveru (vyžadují editor UE + ukázkový projekt)

```powershell
# Ve výchozím stavu: sestaví host a pluginy, provede staging do adresáře plugins, spustí host, načte pluginy a reflexivně provede všechny ITestCase
ForgeMindServerTest.exe

# Filtr: podle skupiny + pořadí
ForgeMindServerTest.exe [group] [order]          # nebo --Test [group] [order]
# Přeskočit předběžné sestavení
ForgeMindServerTest.exe --NoBuild
```

Mechanismus:

- Předběžné sestavení: nejprve sestaví `ForgeMindServer.csproj`, pak najde projekty pluginů se stejnojmenným `.manifest.json` pod `ForgeMind.*` a sestaví je jeden po druhém, přičemž provede staging do `<vstupní_adresář>\plugins\<název_pluginu>\`.
- Provádění: reflexivní objevování `ITestCase`, řazení podle `Group` / `Order`, **fail-fast**; „otevřít editor / zavřít editor" jsou také běžné případy (`Order=0` otevírá, `Order=int.MaxValue` zavírá), session je sdílená ve skupině.
- Případy volají nástroje přímo přes `ITool.Execute` (bez protokolu MCP); interně nástroje komunikují s UE přes bridge.
- Výstup: na konci je generována JSON zpráva a čitelné shrnutí (výstupní adresář je uveden ve výsledku spuštění).
- Testovací assety: 30+ `Blueprint_*_Tests.cs` pod `Task\UE5_0\` (různé domény blueprintů: proměnné / funkce / události / materiály / makra / rozhraní atd.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` zapouzdřuje bridge příkazy, distribuci textu a porovnání IR snímků (pro ochranu před náhodnými změnami).

> Poznámka: případy týkající se „textových cest (DSL)" závisí na komerčních DSL funkcích; integrační testy veřejného výřezu se opírají hlavně o bridge příkazy, jak ukazují reálné případy ve `Task\` výřezu.

## 7. Autorizace a komerční funkce (přehled)

- Ve výchozím stavu `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`): pevné komunitní schopnosti, neanalyzuje žádný licenční soubor.
- Komerční autorizace je poskytována nahrazením `IAuthorizationService` přes ServiceOverride v manifestu pluginu (host aplikuje `ApplyServiceOverride`). Veřejný výřez tento komerční plugin neobsahuje, zachovává pouze mechanismus nahrazování (host nezná pojem „komerční", pouze nahrazuje podle abstraktního typu).

## 8. Konvence kódování

- Záhlaví souborů obsahuje SPDX identifikátor licence a prohlášení o autorských právech (Apache-2.0).
- Neodkazujte na komerční moduly; nové pluginy respektují hranice výřezu definované v `contributing.md`.
