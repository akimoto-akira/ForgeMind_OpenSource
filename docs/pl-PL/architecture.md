# Architektura ForgeMind

ForgeMind to **platforma sterowania silnikami gier oparta na MCP**: serwer (host) udostępnia klientom AI narzędzia przez protokół MCP; funkcje narzędzi są głównie wstrzykiwane przez wtyczki. Po stronie silnika wtyczka adaptera odbiera operacje przez lokalny bridge TCP. UE to obecna implementacja; platforma jest rozszerzalna na inne silniki. Ten dokument opiera się na kodzie widocznym w części **publicznej**; funkcje komercyjne są opisane tylko ogólnie w §7.

## 1. Przegląd

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ Klient AI   │ ◄────────────────► │ ForgeMindServer (host)│
└─────────────┘                     │  • Narzędzia wbudowane ×4│
                                    │  • PluginLoader             │
                                    │  • Serwer bridge TCP       │
                                    │  • Rejestr silników        │
                                    └──────────┬───────────┘
                                               │ Lokalny bridge TCP (4-bajtowy prefiks + koperta JSON)
                                    ┌──────────▼───────────┐
                                    │ Wtyczka adaptera UE  │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): rdzeń serwera MCP.
- **Wtyczki** (`OpenSource\ForgeMind.*Plugin`): rejestrują narzędzia przez `IPlugin`. Host zawiera tylko minimalny zestaw narzędzi.
- **Adapter silnika** (`UEProject\...\Plugins\ForgeMindForUE`): wtyczka w edytorze. Implementuje funkcje po stronie silnika jako serwer bridge.
- **Skanowanie/pakowanie** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): zapewniają indeks offline funkcji silnika i pakowanie SPK.
- **Projekt przykładowy** (`UEProject\ForgeMind_5_0`): do wspólnych testów i debugowania.

## 2. Host (ForgeMindServer)

- **Punkt wejścia**: `Program.cs` domyślnie uruchamia rezydentną usługę HTTP na `127.0.0.1:8765` (Streamable HTTP + kompatybilność SSE). `--stdio` przełącza na tryb spawn-per-client. Port można nadpisać przez `--port` / `FORGEMIND_PORT`.
- **Narzędzia wbudowane** (tabela narzędzi `ForgeMindServerHost`, tylko 4):
  - `settings` (`SettingsTool`, odczyt/zapis konfiguracji);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (zapytanie i czyszczenie długich zadań).
- **Ładowanie wtyczek**: `PluginLoader` skanuje `plugins\` (domyślnie `<katalog hosta>\plugins\`, nadpisywalne przez `FORGEMIND_PLUGINS_DIR`) i wstrzykuje tabelę narzędzi, rejestr silników i bridge przez `PluginContext`. Wtyczka `OnLoad` wywołuje `RegisterTool`, aby wstrzyknąć narzędzia. Pakiety wtyczek obsługują dwa układy: «podkatalog = pakiet wtyczki» (główna DLL + manifest o tej samej nazwie + zależności) i «wolne DLL w korzeniu». Ładowanie jest leniwe (aktywowane przy pierwszym dostępie do tabeli narzędzi); w przypadku błędu ignorowana jest tylko dana wtyczka, host nie jest naruszony.
- **Rejestr silników**: `EngineRegistry` przy starcie wylicza lokalne silniki (preferencja instalacji Launcher `UE_x.y`, fallback na buildy źródłowe) i wstrzykuje wtyczkom snapshot tylko do odczytu.
- **Serwer bridge TCP**: `ForgeMindServerBridgeServer`. Do odkrywania i połączenia adaptera silnika (§5).

## 3. Wtyczki i wstrzykiwanie narzędzi

| Wtyczka | Narzędzia | Odpowiedzialność |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Zarządzanie silnikami i projektami, uruchamianie/budowanie/operacje edytora |
| `ForgeMind.Assets` | `asset` | Odczyt offline struktury `.uasset` (`UAssetArchive` / `BlueprintGraphReader` itp.) |
| `ForgeMind.Knowledge` | `knowledge` | Wyszukiwanie wiedzy (źródło katalogu / źródło pakietu indeksu, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (narzędzie CLI, nie MCP) | Wyliczanie silników → skan `UnrealScanner` → pakowanie `.spk` przez `ScanResultPacker` |

Host i wtyczki współdziałają przez kontrakt `ITool` (`Compat\ITool.cs`): nazwa/opis/schemat parametrów/`Execute` zwraca `ToolResult`. `ForgeMindServerTest` wywołuje również narzędzia bezpośrednio przez `ITool` (bez protokołu MCP), aby zweryfikować zachowanie.

## 4. Skanowanie i SPK (podstawa funkcji offline)

- `ForgeMind.Scan.Abstractions`: abstrakcje `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` itp.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore` itp.) — analizuje pliki nagłówkowe UE, pliki reguł i assety Blueprint na indeks skanowania. `ScanIndexStore` to indeks w pamięci w czasie wykonywania i nie jest zapisywany na dysku. To **różni się od starego ScanCache** — bieżący potok nie używa preload/cache rozproszonego ScanCache (pozostały tylko historyczne ślady zgodności w kodzie; nie stanowią one bieżącego mechanizmu).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` budują i czytają pakiety skanowania.
- `ForgeMind.Speedy`: `SpeedyPack` zapewnia wysokowydajny format pakowania (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, z wewnętrznym CRC, mapowaniem katalogów, dziennikiem pre-write `WriteAheadLog` itp.).
- Użycie: `ForgeMind.EngineScanner <spkPath>` generuje `.spk`. Funkcje takie jak knowledge konsumują je przez `SpkScanDataProvider`.

## 5. Protokół bridge i adapter silnika

- Bridge: lokalne TCP. Komunikaty używają 4-bajtowego prefiksu długości + koperty JSON (`BridgeMessage` / `BridgeSession` / `FrameCodec`). Adapter najpierw czyta `%USERPROFILE%\.forgemind\bridge.json` w celu odkrycia i autoryzacji, potem połączenie → handshake → pętla komunikatów.
- Po stronie serwera routing opiera się na nazwie komendy do implementacji adaptera. Handlery komend są wykonywane w głównym wątku silnika (wątek gry UE), aby zapewnić spójność ze stanem edytora.
- Adapter silnika (strona UE) znajduje się w `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, module Editor zawierającym klienta bridge i aplikator IR. Przykładowy projekt UE jest kompatybilny z 5.0–5.8.
- Aby dodać nowy silnik, wystarczy zaimplementować izomorficznego klienta bridge i handlery komend (`development-guide.md` §4). Nie są potrzebne żadne zmiany w hoście ani wtyczkach.

## 6. Licencja i bezpieczeństwo (część publiczna)

- Domyślna autoryzacja: `OpenAuthorizationService` (`Authorization\Open\`) — stałe funkcje community, **nie analizuje plików licencji**.
- Bezpieczeństwo wtyczek: `PluginIntegrityVerifier` (weryfikacja integralności manifestów), `PluginSigningKeys` (weryfikacja podpisów); `ManifestLoader` analizuje manifesty wtyczek i obsługuje ServiceOverride (zastępowanie usług hosta typami abstrakcyjnymi, np. `IAuthorizationService`).
- Autoryzacja bridge: połączenia adapterów muszą odczytać lokalne poświadczenia z `bridge.json`, aby zakończyć handshake (`security.md`).

## 7. Funkcje komercyjne (przegląd)

- **Język DSL i translatory domenowe** (zamknięty kod): funkcje komercyjne generujące assety silnika z czytelnego tekstu. Część publiczna zawiera tylko warstwę frameworku DSL (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, interfejsy i rejestry w `ForgeMindServer\DSL\`); konkretna składnia, translatory domenowe i reguły IR są dostarczane przez moduły komercyjne i nie są w repozytorium.
- **System licencji komercyjnych** (zamknięty kod): obejmuje wystawianie licencji, weryfikację licencji, serwis licencji i opcjonalne wiązanie ze środowiskiem. Konkretna implementacja, algorytmy podpisów, format plików licencji i logika po stronie serwera nie są częścią publiczną i na zewnątrz są przedstawiane jedynie jako «dostarczanie dodatkowych funkcji».

> Wersja open source oferuje kompletny zestaw narzędzi open source i funkcji bridge. Wersja Commercial opiera się na tym samym hoście, mechanizmie wtyczek i protokole bridge oraz rozszerza funkcje komercyjne (generowanie assetów DSL, licencje komercyjne, wsparcie enterprise).

## 8. Tabela granic funkcji

| Funkcja | Przynależność |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Interfejsy rozszerzeń DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Reguły IR | Commercial 🔒 |
| Generator assetów DSL | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Implementacja licencji komercyjnych | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| System kluczy/podpisów | Commercial 🔒 |
| Wewnętrzne szczegóły protokołu licencji | Commercial 🔒 |
