# Průvodce přispíváním do ForgeMind

Děkujeme za zájem o ForgeMind. Veřejný repozitář odpovídá vydavatelskému výřezu OpenSource projektu ForgeMind a obsahuje pouze publikované open source projekty, testy a ukázkové projekty. Komerční moduly (doménové překladače DSL, systém autorizace atd.) nejsou součástí veřejného repozitáře: před přispěním si vyjasněte rozdělení oblastí.

## 1. Struktura veřejného repozitáře

Struktura nejvyšší úrovně open source výřezu (tedy repozitáře, který klonujete):

```
ForgeMind.sln                          # Řešení (obsahuje odkazy na komerční projekty, viz níže)
OpenSource\                            # Open source projekty
  ForgeMindServer\                     # MCP server (host)
  ForgeMind.OfflineEngineKit\          # Plugin offline nástrojů enginu (engine/project/launch/build/editor atd.)
  ForgeMind.Assets\                    # Plugin čtení assetů (asset)
  ForgeMind.Knowledge\                 # Plugin vyhledávání knowledge (knowledge)
  ForgeMind.EngineScanner\             # Skener enginů (příkazový řádek)
  ForgeMind.Scan\                      # Balení skenování
  ForgeMind.Scan.Abstractions\         # Abstrakce skenování
  ForgeMind.Scan.Unreal\               # Implementace skenování UE (DocumentEngine)
  ForgeMind.Speedy\                    # Formát balení SPK
UEProject\
  ForgeMind_5_0\                       # Ukázkový projekt UE (kompatibilita 5.0–5.8)
tests\
  ForgeMind.PublicTests\               # Veřejné jednotkové testy (SPK / data skenování)
  ForgeMindServerTest\                 # Integrační testy serveru (vyžadují editor UE)
```

> Poznámka: soubor řešení aktuálního vývojového repozitáře (`ForgeMind.sln`) obsahuje odkazy na projekty Commercial (např. `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`); publikovaný produkt **nezahrnuje adresář `Commercial/`**, proto by uživatelé OpenSource měli sestavovat veřejné projekty jednotlivě (viz §3) a nespouštět `dotnet build ForgeMind.sln`.

## 2. Hranice OpenSource

- **OpenSource\**: vše je open source (Apache-2.0), hlavní oblast přispívání.
- **UEProject\**: ukázkový projekt je open source, ale při exportu výřezu jsou vyloučeny binární assety `*.uasset` (v Content zůstávají pouze textové assety, např. mapy, a struktura adresářů).
- **tests\**: open source; příspěvky by měly být doplněny testy.
- **Commercial není ve veřejném repozitáři**: doménové překladače DSL, komerční autorizace atd. se ve výřezu nevyskytují; open source kód nesmí odkazovat na typy z komerčních modulů.
- Při exportu jsou automaticky vyloučeny buildovací a dočasné adresáře, např. `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`; identifikátory licencí v souborech jsou při exportu jednotně nahrazeny za Apache-2.0.

## 3. Sestavení

```powershell
# Sestavujte jednotlivě (nesestavujte přímo celé řešení, viz výše)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Projekty pluginů (po sestavení umístěte artefakty + stejnojmenný .manifest.json do adresáře plugins hostu)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

Host načítá pluginy z `<adresář_hostu>\plugins\` (přepsání proměnnou prostředí `FORGEMIND_PLUGINS_DIR`); balíček pluginu = hlavní DLL + stejnojmenný `.manifest.json` + závislosti (viz `development-guide.md` §3).

## 4. Konvence kódování

- Všechny zdrojové soubory zachovávají v záhlaví SPDX identifikátor licence a prohlášení o autorských právech (aktuálně Apache-2.0).
- Jmenné prostory jsou organizovány podle modulů (`ForgeMindServer.*` / `ForgeMind.*`); veřejné kontrakty procházejí přes abstraktní rozhraní, jako je `ITool` / `IPlugin`.
- Třídy nástrojů serveru musí poskytovat úplný `Description` a schéma parametrů (tyto texty jsou přímo vystavovány AI klientům).
- Kód adaptérů enginů se řídí konvencemi kódování příslušného enginu (aktuální strana UE: prefixy typů F / U / T, makro `TEXT()`, omezení bezpečnosti game thread).
- Nový kód nesmí odkazovat na komerční moduly ani na nich záviset.

## 5. Proces PR

1. Vytvořte větev z `main`, doporučený název `feat/<popis>` nebo `fix/<popis>`.
2. Zpráva commitu: `<typ>: <popis jednou větou>`.
3. Vlastní ověření před commitem:
   - sestavujte jednotlivě podle §3, vše musí projít;
   - pokud změna zasahuje mostní příkazy / chování nástrojů, musí projít příslušné skupiny `ForgeMindServerTest` (způsob spuštění v `development-guide.md` §6);
   - pokud zasahuje SPK / data skenování, musí projít `ForgeMind.PublicTests`;
   - nepřidávejte odkazy na komerční moduly;
   - synchronně aktualizujte odpovídající stránky v `docs\zh-CN\`.
4. Při otevírání PR vyplňte: rozsah změny, shrnutí výsledků testů, případnou potřebu review konkrétních modulů maintainerem.
5. Body review maintainera: hranice open source, kompatibilita API, pokrytí testy.

## 6. Požadavky na testy

- Změny mostních příkazů / chování nástrojů / logiky adaptérů by měly být doplněny testovacími případy (přidávejte `*_Tests.cs` pod `tests\ForgeMindServerTest\Task\`).
- Integrační testy serveru vyžadují prostředí editoru UE; testy čistého zpracování dat (SPK, skenování) jdou do `tests\ForgeMind.PublicTests\`, bez závislosti na enginu.
- Případy musí stabilně procházet ve výchozím pořadí (fail-fast, zastavení na první chybě).

## 7. Přispívání do dokumentace

- Čínská dokumentace je v `docs\zh-CN\`; obsah by měl odpovídat faktům zdrojového kódu; při nejistotě označujte jako «čeká na potvrzení».
- Při změně seznamu veřejných nástrojů aktualizujte také `tools-reference.md`.
- Komerční funkce (syntaxe DSL, mechanismus autorizace) jsou v dokumentaci pouze přehledem, bez vnitřních detailů.

## 8. Kodex chování

- Respektujte existující návrhová rozhodnutí (mostní protokol, kontrakty nástrojů, formát SPK); důležité změny je třeba nejprve prodiskutovat.
- Nehanobte veřejně konkurenční produkty; propagační obsah se řídí pravidly veřejné komunikace z `promo.md`.
