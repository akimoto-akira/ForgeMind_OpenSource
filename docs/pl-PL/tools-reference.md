# Referencja narzędzi ForgeMind

Wszystkie narzędzia są podzielone na «wbudowane w host» i «wstrzykiwane przez wtyczki». Obecna wersja open source zawiera 13 narzędzi MCP. Wtyczka komercyjna może dostarczać inne narzędzia (generowanie assetów DSL itp., patrz §3; nieuwzględnione w liście open source).

## 1. Narzędzia wbudowane hosta

Host (`OpenSource\ForgeMindServer`) zawiera następujące narzędzia bez wtyczek:

| Narzędzie | Opis |
|---|---|
| `settings` | Odczyt/zapis konfiguracji serwera (`McpSettingsManager`, natychmiastowa trwałość) |
| `get_task_result` | Zapytanie o wynik długich zadań (launch / build itp.) |
| `list_executing_tasks` | Lista bieżących długich zadań |
| `cleanup_tasks` | Czyszczenie rekordów zakończonych zadań |

## 2. Narzędzia wstrzykiwane przez wtyczki

Wtyczki wstrzykują narzędzia przez `PluginContext.RegisterTool` w `OnLoad`; pojawiają się po połączeniu klienta. Po zbudowaniu umieść DLL wtyczki + `.manifest.json` o tej samej nazwie w katalogu `plugins\` hosta (lub katalogu zdefiniowanym przez `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7)

| Narzędzie | Opis |
|---|---|
| `engine` | Zapytanie o lokalnie zainstalowane silniki (Launcher / buildy źródłowe, w tym wersja i nazwa aplikacji) |
| `project` | Zapytanie/zarządzanie projektami UE (identyfikacja pliku projektu, zapytanie o informacje projektu) |
| `project_files` | Lista plików projektu (filtr katalogu/typu) |
| `switch_version` | Zmiana wersji projektu na wskazany silnik (przez UnrealVersionSelector) |
| `launch` | Uruchamianie edytora lub spakowanych plików wykonywalnych (obsługiwana konfiguracja debugowania) |
| `build` | Budowanie projektu (domyślny target / target Editor) |
| `editor` | Operacje w edytorze (scena, viewport, assety itp., wysyłane przez bridge) |

Schematy parametrów podlegają `GetParameterSchema()` z kodu źródłowego każdego narzędzia (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Niektóre operacje silnika podlegają uprawnieniom `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Narzędzie | Opis |
|---|---|
| `asset` | Odczyt offline struktury `.uasset` (nazwy, obiekty, właściwości, grafy Blueprint, zapytanie po grafie/właściwości) |

Implementacja opiera się na `UAsset\UAssetSerializer` / `BlueprintGraphReader` itp. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Narzędzie | Opis |
|---|---|
| `knowledge` | Wyszukiwanie wiedzy: zapytanie o wpisy wiedzy projektu/silnika (źródła: źródło katalogu `FolderKnowledgeSource`, źródło pakietu indeksu SPK `IndexPackSource`) |

Implementacja w `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Narzędzia wersji komercyjnej (przegląd)

Wersja komercyjna może dodawać generowanie assetów DSL i powiązane narzędzia zapytań, wstrzykiwane przez wtyczkę komercyjną. Konkretne nazwy, API i funkcje DSL podlegają dokumentacji komercyjnej. OpenSource nie zawiera żadnego narzędzia DSL.

## 4. Konwencje wywoływania narzędzi

- Narzędzia długich zadań zwracają ID zadania. Wynik uzyskuje się przez `get_task_result`.
- Błędy narzędzi zwracają `ToolResult` błędu (wyświetlany bezpośrednio klientowi).
- Opisy i schematy parametrów narzędzi odpowiadają temu, co pokazuje `tools/list` MCP, i są zgodne z kodem źródłowym.
