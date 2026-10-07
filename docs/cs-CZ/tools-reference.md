# Referenční příručka nástrojů ForgeMind

Všechny nástroje se dělí na 'vestavěné v hostu' a 'injektované pluginy'. Aktuální Open Source verze obsahuje 13 nástrojů MCP. Komerční plugin může poskytovat další nástroje (generování DSL assetů atd., viz §3; nejsou součástí open source seznamu).

## 1. Vestavěné nástroje hostu

Host (`OpenSource\ForgeMindServer`) obsahuje následující nástroje bez nutnosti pluginů:

| Nástroj | Popis |
|---|---|
| `settings` | Čtení/zápis konfigurace serveru (`McpSettingsManager`, okamžitá persistence) |
| `get_task_result` | Dotaz na výsledek dlouhých úloh (launch / build atd.) |
| `list_executing_tasks` | Seznam probíhajících dlouhých úloh |
| `cleanup_tasks` | Vyčištění záznamů dokončených úloh |

## 2. Nástroje injektované pluginy

Pluginy injektují nástroje prostřednictvím `PluginContext.RegisterTool` v `OnLoad` a zobrazí se po připojení klienta. Po sestavení umístěte DLL pluginu + `.manifest.json` stejného názvu do adresáře `plugins\` hostu (nebo adresáře určeného `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7)

| Nástroj | Popis |
|---|---|
| `engine` | Dotaz na lokálně nainstalované enginy (Launcher / sestavení ze zdrojů, včetně verze a názvu aplikace) |
| `project` | Dotaz/správa projektů UE (identifikace souboru projektu, dotaz na informace o projektu) |
| `project_files` | Výpis souborů projektu (filtrace podle adresáře/typu) |
| `switch_version` | Přepnutí verze projektu na zadaný engine (přes UnrealVersionSelector) |
| `launch` | Spuštění editoru nebo zabaleného spustitelného souboru (podpora konfigurace ladění) |
| `build` | Sestavení projektu (výchozí cíl / cíl Editor) |
| `editor` | Operace uvnitř editoru (scéna, viewport, assety atd., vysílané přes bridge) |

Schémata parametrů se řídí `GetParameterSchema()` zdrojového kódu jednotlivých nástrojů (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Některé enginové operace podléhají oprávněním `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Nástroj | Popis |
|---|---|
| `asset` | Offline čtení struktury `.uasset` (názvy, objekty, vlastnosti, grafy Blueprintu, dotaz podle grafu/vlastnosti) |

Implementace je založena na `UAsset\UAssetSerializer` / `BlueprintGraphReader` atd. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Nástroj | Popis |
|---|---|
| `knowledge` | Znalostní vyhledávání: dotaz na znalostní položky projektu/enginu (zdroje: zdroj složky `FolderKnowledgeSource`, zdroj indexového balíčku SPK `IndexPackSource`) |

Implementace v `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Nástroje komerční verze (přehled)

Komerční verze může přidat generování DSL assetů a související dotazovací nástroje, injektované komerčním pluginem. Konkrétní názvy, API a DSL funkce se řídí dokumentací komerční verze. OpenSource DSL nástroje neobsahuje.

## 4. Konvence volání nástrojů

- Nástroje pro dlouhé úlohy vrací ID úlohy. Výsledek získáte pomocí `get_task_result`.
- Selhání nástroje vrací chybovou zprávu `ToolResult` (lze přímo zobrazit klientovi).
- Popisy a schémata parametrů nástrojů odpovídají tomu, co zobrazuje `tools/list` MCP, a shodují se se zdrojovým kódem.
