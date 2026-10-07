# Przewodnik programisty ForgeMind

Dla programistów, którzy chcą rozszerzać ForgeMind: jak budować, debugować, dodawać nowe narzędzia MCP, dodawać adaptery silników i uruchamiać testy. Dokument jest oparty na rzeczywistym kodzie **publicznego wycinka** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Przegląd projektu

| Projekt | Zakres |
|---|---|
| `OpenSource\ForgeMindServer` | Host serwera MCP: wbudowane 4 narzędzia, uruchamia most TCP, ładuje wtyczki |
| `OpenSource\ForgeMind.OfflineEngineKit` | Wtyczka: narzędzia engine / project / project_files / switch_version / launch / build / editor |
| `OpenSource\ForgeMind.Assets` | Wtyczka: narzędzie asset (offline odczyt `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Wtyczka: narzędzie knowledge (wyszukiwanie w bazie wiedzy) |
| `OpenSource\ForgeMind.EngineScanner` | Skaner wiersza poleceń: wylicza silniki i pakuje `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Indeks skanowania: abstrakcja, pakowanie, implementacja UE (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | Format pakowania SPK (wysokowydajny plik pakietu) |
| `tests\ForgeMind.PublicTests` | Publiczne testy jednostkowe (bez zależności od silnika) |
| `tests\ForgeMindServerTest` | Testy integracyjne serwera (wymagają edytora UE) |
| `UEProject\ForgeMind_5_0` | Przykładowy projekt UE (5.0–5.8) |

## 2. Budowanie

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Projekty wtyczek i bibliotek: patrz contributing.md §3 (nie buduj bezpośrednio całego ForgeMind.sln — zawiera odwołania do projektów komercyjnych)
```

- Punkt wejścia hosta: `OpenSource\ForgeMindServer\Program.cs` (domyślnie HTTP nasłuchuje na `127.0.0.1:8765`, przełączenie na `--stdio`, nadpisanie portu przez `--port` / `FORGEMIND_PORT`).
- Logi: `LogManager`; narzędzia zwracają `ToolResult` (sukces / błąd z komunikatem).

## 3. Mechanizm wtyczek i nowe narzędzia MCP

Host ma wbudowane tylko 4 narzędzia: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. Pozostałe narzędzia dostarczają **wtyczki w katalogu plugins**:

- Katalog wtyczek: `<katalog_hosta>\plugins\` (nadpisanie przez zmienną środowiskową `FORGEMIND_PLUGINS_DIR`).
- Układ pakietu wtyczki: w `plugins\<nazwa_wtyczki>\` znajdują się główna DLL, plik `.manifest.json` o tej samej nazwie i zależności; obsługiwane są też luźne DLL w katalogu głównym (ręczny drop-in).
- `PluginLoader` hosta skanuje wtyczki → wstrzykuje `PluginContext` (tabela narzędzi, rejestr silników, most) → wtyczka wywołuje `RegisterTool` w `OnLoad`, aby zarejestrować narzędzia.
- Integralność wtyczek: `PluginIntegrityVerifier` / `PluginSigningKeys` weryfikują manifesty i podpisy; zaufane wtyczki mogą rozszerzać usługi przez ServiceOverride (patrz §7).

Aby dodać nowe narzędzie MCP:

1. Zaimplementuj kontrakt `ITool` (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: nazwa i nazwa wyświetlana narzędzia;
   - `Description`: opis przeznaczony dla klientów AI;
   - `GetParameterSchema()`: deklaracja parametrów w stylu JSON Schema;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: zwraca `ToolResult`.
2. Utwórz nowy projekt wtyczki (lub dodaj do istniejącego, np. OfflineEngineKit), zaimplementuj `IPlugin` i wywołaj `RegisterTool` w `OnLoad`.
3. W tym samym katalogu umieść `.manifest.json` o tej samej nazwie (opis wtyczki / deklaracja zależności).
4. Do funkcji po stronie silnika wysyłaj polecenia przez most i implementuj obsługę poleceń po stronie adaptera (patrz §5).
5. Napisz przypadki testowe (§6) i zaktualizuj `tools-reference.md`.

## 4. Jak dodać nowy adapter silnika

ForgeMind obsługuje logikę specyficzną dla silnika przez «adaptery»; nowy silnik korzysta z tego samego protokołu mostu (4-bajtowy prefiks długości + koperta JSON, patrz `api-reference.md`):

1. Zaimplementuj klienta mostu: odczytaj `%USERPROFILE%\.forgemind\bridge.json` w celu discovery i uwierzytelnienia, połącz → handshake → pętla komunikatów.
2. Zarejestruj procedury obsługi na whitelist według nazw poleceń; wszystkie polecenia są wykonywane na głównym wątku silnika (odpowiednik game thread w UE).
3. Zaimplementuj funkcje po stronie silnika (odczyt/zapis assetów, operacje na projekcie itd.) i odwzoruj je na wyniki narzędzi serwera.
4. Dodaj przypadki testowe i dokumentację dla tego silnika.

Pierwszą implementacją jest adapter UE `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (wtyczka modułu Editor, klient mostu + aplikator IR).

## 5. Polecenia mostu po stronie silnika (aktualna implementacja UE)

- Wtyczka `ForgeMindForUE` po stronie adaptera zawiera klienta mostu; procedury obsługi poleceń są wykonywane w game thread.
- Operacje na poziomie assetów mogą ponownie używać procedur IR wtyczki, unikając dublowania logiki edytora.
- Debugowanie: kategoria `LogForgeMindBridge` w logach edytora UE; przy rozłączeniu następuje ponowne łączenie z wykładniczym backoff.

## 6. Uruchamianie testów

### Publiczne testy jednostkowe (bez edytora)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Obejmują round-trip pakowania SPK, SPK wielowersyjne i dostawców danych skanowania (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Testy integracyjne serwera (wymagają edytora UE + projektu przykładowego)

```powershell
# Domyślnie: buduje host i wtyczki, robi staging w katalogu plugins, uruchamia host, ładuje wtyczki i refleksyjnie wykonuje wszystkie ITestCase
ForgeMindServerTest.exe

# Filtr: wg grupy + kolejności
ForgeMindServerTest.exe [group] [order]          # lub --Test [group] [order]
# Pomija pre-budowanie
ForgeMindServerTest.exe --NoBuild
```

Mechanizm:

- Pre-budowanie: najpierw buduje `ForgeMindServer.csproj`, następnie znajduje projekty wtyczek z `.manifest.json` o tej samej nazwie pod `ForgeMind.*` i buduje je pojedynczo, robiąc staging w `<katalog_wejściowy>\plugins\<nazwa_wtyczki>\`.
- Wykonanie: refleksyjne wykrywanie `ITestCase`, sortowanie wg `Group` / `Order`, **fail-fast**; «uruchom edytor / zamknij edytor» również są zwykłymi przypadkami (`Order=0` uruchamia, `Order=int.MaxValue` zamyka), sesja współdzielona w grupie.
- Przypadki wywołują narzędzia bezpośrednio przez `ITool.Execute` (bez protokołu MCP); wewnętrznie narzędzia komunikują się z UE przez most.
- Wynik: po zakończeniu generowany jest raport JSON i czytelne podsumowanie (katalog wyników jest podany w rezultacie uruchomienia).
- Zasoby testowe: 30+ `Blueprint_*_Tests.cs` pod `Task\UE5_0\` (różne dziedziny blueprintów: zmienne / funkcje / zdarzenia / materiały / makra / interfejsy itd.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` enkapsuluje polecenia mostu, dystrybucję tekstu i porównanie snapshotów IR (w celu zapobiegania przypadkowym zmianom).

> Uwaga: przypadki dotyczące «ścieżek tekstowych (DSL)» zależą od komercyjnych funkcji DSL; testy integracyjne publicznego wycinka opierają się głównie na poleceniach mostu, zgodnie z rzeczywistymi przypadkami w `Task\` wycinka.

## 7. Autoryzacja i funkcje komercyjne (przegląd)

- Domyślnie `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`): stałe funkcje społeczności, nie analizuje żadnego pliku licencji.
- Autoryzacja komercyjna jest dostarczana przez zastąpienie `IAuthorizationService` za pomocą ServiceOverride w manifeście wtyczki (host stosuje `ApplyServiceOverride`). Publiczny wycinek nie zawiera tej komercyjnej wtyczki, zachowuje tylko mechanizm zastępowania (host nie zna pojęcia «komercyjny», zastępuje po prostu wg typu abstrakcyjnego).

## 8. Zasady kodowania

- Nagłówek plików zawiera identyfikator SPDX licencji i deklarację praw autorskich (Apache-2.0).
- Nie odwołuj się do modułów komercyjnych; nowe wtyczki przestrzegają granic wycinka określonych w `contributing.md`.
