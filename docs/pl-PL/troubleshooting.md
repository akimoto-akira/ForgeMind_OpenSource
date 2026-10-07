# Rozwiązywanie problemów ForgeMind

Częste problemy i rozwiązania (oparte na rzeczywistych mechanizmach publicznego wycinka).

## 1. Błędy kompilacji

**Bezpośrednia kompilacja `ForgeMind.sln` zgłasza «nie znaleziono projektu Commercial\...»**
Przyczyna: `ForgeMind.sln` zawiera wpisy odwołań do projektów komercyjnych (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`), ale katalog komercyjny nie jest publikowany razem z wycinkiem open source.
Rozwiązanie: buduj projekty pojedynczo (contributing.md §3), nie całe rozwiązanie; albo użyj skryptu budowania wydawcy.

**Po zbudowaniu wtyczki host nadal nie widzi narzędzia**
- Sprawdź, czy DLL wtyczki i `.manifest.json` o tej samej nazwie zostały umieszczone w `plugins\<nazwa_wtyczki>\` hosta (lub w katalogu wskazanym przez `FORGEMIND_PLUGINS_DIR`);
- sprawdź w logach hosta, czy skanowanie wtyczek pominęło ten pakiet (`PluginLoader` pomija tylko pakiet z błędem, nie wpływa na host);
- zrestartuj host (tabela narzędzi jest ładowana leniwie przy pierwszym dostępie).

## 2. Problemy ze startem serwera

**Zajęty port**
Gdy `http://127.0.0.1:8765` jest już zajęty, zgłaszany jest `HttpListenerException`.
Rozwiązanie: zmień port przez `--port N` lub zmienną środowiskową `FORGEMIND_PORT`; odpowiednio zaktualizuj adres klienta.

**Klient nie łączy się / tools/list jest puste**
- Sprawdź, czy serwer nasłuchuje (log `Listening on http://127.0.0.1:<port>/mcp`);
- sprawdź, czy URL klienta to `http://127.0.0.1:8765/mcp` (tryb HTTP);
- sprawdź, czy wtyczka została załadowana (patrz wyżej).

## 3. Problemy z połączeniem silnika / mostu

**Edytor jest otwarty, ale narzędzia zgłaszają «silnik niepołączony / most niedostępny»**
- Sprawdź, czy wtyczka `ForgeMindForUE` jest włączona w projekcie (`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`);
- adapter mostu odczytuje `%USERPROFILE%\.forgemind\bridge.json` w celu discovery i uwierzytelnienia; jeśli pliku brak lub dane uwierzytelniające nie pasują, połączenie zawodzi;
- zobacz log edytora UE `LogForgeMindBridge`; przy rozłączeniu następuje ponowne łączenie z wykładniczym backoff, poczekaj na ponowne połączenie.

**`launch` / `build` zawieszone**
Długie zadania są wykonywane asynchronicznie; użyj `get_task_result`, aby sprawdzić wynik, `list_executing_tasks`, aby zobaczyć trwające zadania; `cleanup_tasks` może wyczyścić zapisy.

## 4. Problemy z testami

**Pre-budowanie `ForgeMindServerTest` kończy się niepowodzeniem**
`--NoBuild` pomija pre-budowanie (użyj, gdy artefakty są już na pewno świeże z CI); domyślny przepływ najpierw buduje host i robi staging wtyczek w `<wejście>\plugins\`, w razie błędu kończy bez uruchamiania przypadków.

**Przerwanie testów fail-fast**
Testy integracyjne są wykonywane wg Group / Order z fail-fast: zatrzymują się na pierwszym błędzie (przypadki must-run zamykania edytora są nadal wykonywane w grupie). Po poprawce uruchom grupę ponownie przez `ForgeMindServerTest.exe [group]`.

**Testy zależą od edytora / projektu przykładowego**
Przypadki pod `Task\UE5_0\` wymagają edytora UE i sesji; przypadki czystego przetwarzania danych (SPK / skanowanie) są wykonywane w `ForgeMind.PublicTests`, bez zależności od edytora.

## 5. Problemy z danymi / zasobami

**Odczyt `asset` kończy się niepowodzeniem**
Offline odczyt `.uasset` wymaga zgodności wersji silnika (`UAssetVersions`); jeśli wersja nie pasuje, parsowanie może zawieść. Potwierdź wersję silnika i zakres wsparcia narzędzia.

**Nie można załadować `.spk`**
Sprawdź, czy plik został wygenerowany przez `ForgeMind.EngineScanner <spkPath>` i nie jest uszkodzony; SPK zawiera weryfikację CRC (`Speedy\Internal\Crc32`), uszkodzone pliki są odrzucane.

## 6. Inne

**Jak włączyć logi**
Logi serwera są emitowane przez `LogManager` (kategorie m.in.: `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer`); po stronie mostu patrz logi wyjściowe edytora UE.

**Problemy związane z funkcjami komercyjnymi**
W kwestiach składni DSL, autoryzacji i licencji skontaktuj się z wydawcą; wersja OpenSource nie analizuje plików licencji, funkcje są stałe i otwarte (patrz `security.md`).
