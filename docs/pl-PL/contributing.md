# Przewodnik kontrybucji ForgeMind

Dziękujemy za zainteresowanie ForgeMind. Publiczne repozytorium odpowiada wydawniczemu wycinkowi OpenSource ForgeMind i zawiera wyłącznie opublikowane projekty open source, testy oraz projekty przykładowe. Moduły komercyjne (tłumacze domenowe DSL, system autoryzacji itd.) nie należą do publicznego repozytorium: przed kontrybucją wyjaśnij podział zakresów.

## 1. Struktura publicznego repozytorium

Struktura najwyższego poziomu wycinka open source (czyli repozytorium, które klonujesz):

```
ForgeMind.sln                          # Rozwiązanie (zawiera odwołania do projektów komercyjnych, patrz niżej)
OpenSource\                            # Projekty open source
  ForgeMindServer\                     # Serwer MCP (host)
  ForgeMind.OfflineEngineKit\          # Wtyczka narzędzi offline silnika (engine/project/launch/build/editor itd.)
  ForgeMind.Assets\                    # Wtyczka odczytu assetów (asset)
  ForgeMind.Knowledge\                 # Wtyczka wyszukiwania knowledge (knowledge)
  ForgeMind.EngineScanner\             # Skaner silników (wiersz poleceń)
  ForgeMind.Scan\                      # Pakowanie skanowania
  ForgeMind.Scan.Abstractions\         # Abstrakcja skanowania
  ForgeMind.Scan.Unreal\               # Implementacja skanowania UE (DocumentEngine)
  ForgeMind.Speedy\                    # Format pakowania SPK
UEProject\
  ForgeMind_5_0\                       # Przykładowy projekt UE (zgodność 5.0–5.8)
tests\
  ForgeMind.PublicTests\               # Publiczne testy jednostkowe (SPK / dane skanowania)
  ForgeMindServerTest\                 # Testy integracyjne serwera (wymagają edytora UE)
```

> Uwaga: plik rozwiązania obecnego repozytorium deweloperskiego (`ForgeMind.sln`) zawiera odwołania do projektów Commercial (np. `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`); wydawany produkt **nie zawiera katalogu `Commercial/`**, dlatego użytkownicy OpenSource powinni budować projekty publiczne pojedynczo (patrz §3) i nie uruchamiać `dotnet build ForgeMind.sln`.

## 2. Granice OpenSource

- **OpenSource\**: wszystko jest open source (Apache-2.0), główny obszar kontrybucji.
- **UEProject\**: projekt przykładowy jest open source, ale w eksporcie wycinka wykluczane są binarne assety `*.uasset` (w Content pozostają tylko assety tekstowe, np. mapy, oraz struktura katalogów).
- **tests\**: open source; kontrybucje powinny być dołączane z testami.
- **Commercial nie ma w publicznym repozytorium**: tłumacze domenowe DSL, autoryzacja komercyjna itd. nie występują w wycinku; kod open source nie może odwoływać się do typów z modułów komercyjnych.
- W eksporcie automatycznie wykluczane są katalogi build i tymczasowe, np. `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`; identyfikatory licencji w plikach są w eksporcie jednolicie zastępowane przez Apache-2.0.

## 3. Budowanie

```powershell
# Buduj pojedynczo (nie buduj bezpośrednio całego rozwiązania, patrz wyżej)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Projekty wtyczek (po zbudowaniu umieść artefakty + .manifest.json o tej samej nazwie w katalogu plugins hosta)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

Host ładuje wtyczki z `<katalog_hosta>\plugins\` (nadpisanie przez zmienną środowiskową `FORGEMIND_PLUGINS_DIR`); pakiet wtyczki = główna DLL + `.manifest.json` o tej samej nazwie + zależności (patrz `development-guide.md` §3).

## 4. Konwencje kodowania

- Wszystkie pliki źródłowe zachowują w nagłówku identyfikator SPDX licencji i deklarację praw autorskich (obecnie Apache-2.0).
- Przestrzenie nazw są zorganizowane wg modułów (`ForgeMindServer.*` / `ForgeMind.*`); publiczne kontrakty przechodzą przez abstrakcyjne interfejsy, np. `ITool` / `IPlugin`.
- Klasy narzędzi serwera muszą dostarczać pełny `Description` i schemat parametrów (te teksty są bezpośrednio udostępniane klientom AI).
- Kod adapterów silników przestrzega konwencji kodowania odpowiedniego silnika (aktualna strona UE: prefiksy typów F / U / T, makro `TEXT()`, ograniczenia bezpieczeństwa game thread).
- Nowy kod nie może odwoływać się ani zależeć od żadnego modułu komercyjnego.

## 5. Przepływ PR

1. Utwórz branch z `main`, sugerowana nazwa `feat/<opis>` lub `fix/<opis>`.
2. Wiadomość commita: `<typ>: <opis w jednym zdaniu>`.
3. Samodzielna weryfikacja przed commitem:
   - buduj pojedynczo wg §3, wszystko musi przechodzić;
   - jeśli zmiana dotyczy poleceń mostu / zachowania narzędzi, odpowiednie grupy `ForgeMindServerTest` muszą przechodzić (sposób uruchamiania w `development-guide.md` §6);
   - jeśli dotyczy SPK / danych skanowania, `ForgeMind.PublicTests` muszą przechodzić;
   - brak nowych odwołań do modułów komercyjnych;
   - zsynchronizowana aktualizacja odpowiednich stron w `docs\zh-CN\`.
4. Otwierając PR, uzupełnij: zakres zmiany, podsumowanie wyników testów, ewentualną potrzebę review określonych modułów przez maintainera.
5. Punkty review maintainera: granice open source, zgodność API, pokrycie testami.

## 6. Wymagania testowe

- Zmiany w poleceniach mostu / zachowaniu narzędzi / logice adapterów powinny być dołączane z przypadkami testowymi (dodaj `*_Tests.cs` pod `tests\ForgeMindServerTest\Task\`).
- Testy integracyjne serwera wymagają środowiska edytora UE; testy czystego przetwarzania danych (SPK, skanowanie) trafiają do `tests\ForgeMind.PublicTests\`, bez zależności od silnika.
- Przypadki muszą stabilnie przechodzić w domyślnej kolejności (fail-fast, zatrzymanie na pierwszym błędzie).

## 7. Kontrybucja dokumentacji

- Dokumentacja chińska znajduje się w `docs\zh-CN\`; treść powinna być zgodna z faktami kodu źródłowego; w razie niepewności oznaczaj jako «do potwierdzenia».
- Zmieniając listę publicznych narzędzi, zaktualizuj także `tools-reference.md`.
- Funkcje komercyjne (składnia DSL, mechanizm autoryzacji) w dokumentacji to tylko przegląd, bez wewnętrznych szczegółów.

## 8. Kodeks postępowania

- Szanuj istniejące decyzje projektowe (protokół mostu, kontrakty narzędzi, format SPK); ważne zmiany należy najpierw przedyskutować.
- Nie oczerniaj publicznie konkurencyjnych produktów; treści promocyjne przestrzegają zasad komunikacji publicznej z `promo.md`.
