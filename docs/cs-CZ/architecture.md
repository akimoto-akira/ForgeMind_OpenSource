# Architektura ForgeMind

ForgeMind je **platforma pro ovládání herních enginů s rozhraním MCP**: server (host) zpřístupňuje nástroje AI klientům prostřednictvím protokolu MCP; funkce nástrojů injektují především pluginy. Na straně enginu přijímá plugin adaptéru operace přes lokální TCP bridge. UE je aktuální implementace; platformu lze rozšířit na další enginy. Tento dokument vychází z kódu viditelného v **veřejné části**; komerční funkce jsou popsány pouze obecně v §7.

## 1. Přehled

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ AI klient   │ ◄────────────────► │ ForgeMindServer (host) │
└─────────────┘                     │  • Vestavěné nástroje ×4 │
                                    │  • PluginLoader             │
                                    │  • TCP bridge server        │
                                    │  • Registr enginů           │
                                    └──────────┬───────────┘
                                               │ Lokální TCP bridge (4bajtový prefix + JSON obálka)
                                    ┌──────────▼───────────┐
                                    │ Plugin adaptéru UE    │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): tělo MCP serveru.
- **Pluginy** (`OpenSource\ForgeMind.*Plugin`): registrují nástroje prostřednictvím `IPlugin`. Host obsahuje pouze minimální sadu nástrojů.
- **Adaptér enginu** (`UEProject\...\Plugins\ForgeMindForUE`): plugin uvnitř editoru. Implementuje funkce na straně enginu jako server bridge.
- **Skenování/balení** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): poskytují offline index enginových funkcí a SPK balení.
- **Ukázkový projekt** (`UEProject\ForgeMind_5_0`): pro testování a společné ladění.

## 2. Host (ForgeMindServer)

- **Vstupní bod**: `Program.cs` ve výchozím stavu spustí rezidentní HTTP službu na `127.0.0.1:8765` (Streamable HTTP + kompatibilita se SSE). `--stdio` přepne do režimu spawn-per-client. Port lze přepsat pomocí `--port` / `FORGEMIND_PORT`.
- **Vestavěné nástroje** (tabulka nástrojů `ForgeMindServerHost`, pouze 4):
  - `settings` (`SettingsTool`, čtení/zápis konfigurace);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (dotaz a vyčištění dlouhých úloh).
- **Načítání pluginů**: `PluginLoader` skenuje `plugins\` (výchozí `<adresář hostu>\plugins\`, přepsatelný `FORGEMIND_PLUGINS_DIR`) a injektuje tabulku nástrojů, registr enginů a bridge prostřednictvím `PluginContext`. Plugin `OnLoad` volá `RegisterTool` pro injektování nástrojů. Balíčky pluginů podporují dva rozvržení: 'podadresář = balíček pluginů' (hlavní DLL + manifest stejného názvu + závislosti) a 'volné DLL v kořeni'. Načítání je líné (aktivuje se při prvním přístupu k tabulce nástrojů); při selhání se přeskočí pouze daný plugin, host není ovlivněn.
- **Registr enginů**: `EngineRegistry` při spuštění vyjmenuje lokální enginy (přednost instalací Launcher `UE_x.y`, fallback na sestavení ze zdrojů) a injektuje pluginům snapshot pouze pro čtení.
- **TCP bridge server**: `ForgeMindServerBridgeServer`. Pro objev a připojení adaptéru enginu (§5).

## 3. Pluginy a injektování nástrojů

| Plugin | Nástroje | Odpovědnost |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Správa enginů a projektů, spuštění/sestavení/operace v editoru |
| `ForgeMind.Assets` | `asset` | Offline čtení struktury `.uasset` (`UAssetArchive` / `BlueprintGraphReader` atd.) |
| `ForgeMind.Knowledge` | `knowledge` | Znalostní vyhledávání (zdroj složky / zdroj indexového balíčku, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (CLI nástroj, ne MCP) | Vyčíslení enginů → skenování `UnrealScanner` → balení `.spk` pomocí `ScanResultPacker` |

Host a pluginy interagují prostřednictvím kontraktu `ITool` (`Compat\ITool.cs`): název/popis/schéma parametrů/`Execute` vrací `ToolResult`. `ForgeMindServerTest` také volá nástroje přímo přes `ITool` (bez MCP protokolu) pro ověření chování.

## 4. Skenování a SPK (základna offline funkcí)

- `ForgeMind.Scan.Abstractions`: abstrakce `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` atd.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore` atd.) — analyzuje UE hlavičkové soubory, soubory pravidel a Blueprint assety do skenovacích indexů. `ScanIndexStore` je runtime paměťový index a neukládá se na disk. To se **liší od starého ScanCache** — aktuální pipeline nepoužívá ScanCache preload/rozptýlenou cache (v kódu zůstaly pouze historické stopy kompatibility; nepředstavují aktuální mechanismus).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` sestavují a čtou skenovací balíčky.
- `ForgeMind.Speedy`: `SpeedyPack` poskytuje vysoce výkonný balicí formát (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, s interním CRC, mapováním adresářů, write-ahead logem `WriteAheadLog` atd.).
- Použití: `ForgeMind.EngineScanner <spkPath>` generuje `.spk`. Funkce jako knowledge je konzumují přes `SpkScanDataProvider`.

## 5. Bridge protokol a adaptér enginu

- Bridge: lokální TCP. Zprávy používají 4bajtový prefix délky + JSON obálku (`BridgeMessage` / `BridgeSession` / `FrameCodec`). Adaptér nejprve čte `%USERPROFILE%\.forgemind\bridge.json` pro dokončení objevu a autentizace, poté připojení → handshake → smyčku zpráv.
- Strana serveru směruje podle názvu příkazu do implementace adaptéru. Obslužné rutiny příkazů se provádějí v hlavním vlákně enginu (UE herní vlákno) pro zajištění konzistence se stavem editoru.
- Adaptér enginu (strana UE) je v `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, modul Editor obsahující bridge klienta a IR aplikátor. Ukázkový UE projekt je kompatibilní s 5.0–5.8.
- Pro přidání nového enginu stačí implementovat isomorphic bridge klienta a obsluhy příkazů (`development-guide.md` §4). Změny hostu ani pluginů nejsou nutné.

## 6. Licence a bezpečnost (veřejná část)

- Výchozí autorizace: `OpenAuthorizationService` (`Authorization\Open\`) — pevné komunitní funkce, **neanalyzuje licenční soubory**.
- Bezpečnost pluginů: `PluginIntegrityVerifier` (ověření integrity manifestu), `PluginSigningKeys` (ověření podpisu); `ManifestLoader` analyzuje manifesty pluginů a podporuje ServiceOverride (náhrada služeb hostu abstraktními typy, např. `IAuthorizationService`).
- Bridge autentizace: připojení adaptéru musí přečíst lokální pověření z `bridge.json` pro dokončení handshake (`security.md`).

## 7. Komerční funkce (přehled)

- **DSL jazyk a doménové translátory** (uzavřený kód): komerční funkce generující enginové assety z čitelného textu. Veřejná část obsahuje pouze DSL framework vrstvu (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, rozhraní a registry v `ForgeMindServer\DSL\`); konkrétní syntaxi, doménové translátory a IR pravidla poskytují komerční moduly a nejsou v části.
- **Systém komerčních licencí** (uzavřený kód): zahrnuje vydávání licencí, ověřování licencí, licenční službu a volitelné vázání na prostředí. Konkrétní implementace, podpisové algoritmy, formát licenčních souborů a logika na straně serveru nepatří do veřejné části a navenek jsou prezentovány pouze jako 'poskytování doplňkových funkcí'.

> Open Source verze nabízí kompletní sadu open source nástrojů a bridge funkcí. Commercial verze staví na stejném hostu, pluginovém mechanismu a bridge protokolu a rozšiřuje komerční funkce (generování DSL assetů, komerční licence, podniková podpora).

## 8. Tabulka hranic funkcí

| Funkce | Příslušnost |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Rozhraní rozšíření DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| IR pravidla | Commercial 🔒 |
| Generátor DSL assetů | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Implementace komerčních licencí | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Klíče / podpisový systém | Commercial 🔒 |
| Interní detaily licenčního protokolu | Commercial 🔒 |
