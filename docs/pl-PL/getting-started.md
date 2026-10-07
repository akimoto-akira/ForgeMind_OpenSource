# Szybki start ForgeMind

ForgeMind to **platforma sterowania silnikami gier oparta na MCP**: poprzez protokół MCP udostępnia klientom AI zarządzanie projektami Unreal Engine, odczyt assetów oraz operacje uruchamiania/budowania/edycji. Obecna implementacja to UE; platforma jest rozszerzalna. Wersja open source zawiera zestaw narzędzi silnika i funkcje bridge; wersja komercyjna dodaje generowanie assetów DSL (§5).

Wszystkie kroki tego przewodnika można wykonać z częścią **open source** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Wymagania środowiskowe

- SDK .NET (do budowania hosta i wtyczek).
- Unreal Engine 5.0–5.8 (z edytorem). Projekt przykładowy to `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- Klient zgodny z MCP (Claude Desktop / Cursor / klient niestandardowy).

## 2. Budowanie serwera i wtyczek

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Wtyczki (po zbudowaniu umieść DLL + .manifest.json o tej samej nazwie w katalogu plugins hosta)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Nie buduj bezpośrednio całego `ForgeMind.sln` (zawiera odwołania do projektów komercyjnych; katalog komercyjny nie jest w repozytorium). Wtyczki można również używać z artefaktów budowania dostarczonych przez dystrybutora.

## 3. Uruchamianie

1. **Uruchom serwer** (domyślnie: HTTP rezydentne, port 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Lub uruchom skompilowany ForgeMindServer.exe; konfigurowalny przez --port N / --stdio / FORGEMIND_PORT
   ```

2. **Otwórz edytor UE i projekt przykładowy**: uruchom `ForgeMind_5_0.uproject` w UE; wtyczka `ForgeMindForUE` automatycznie uruchamia klienta bridge w edytorze i łączy się z hostem.

3. **Skonfiguruj klienta**: dodaj serwer do klienta MCP:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Po połączeniu pojawia się lista narzędzi (4 wbudowane + wstrzyknięte wtyczki).

## 4. Pierwsze użycie

- `engine` — wyświetlanie informacji o bieżącym silniku;
- `project` / `project_files` — wyświetlanie projektu przykładowego i jego plików;
- `launch` — uruchamianie edytora, `editor` — operacje w edytorze;
- `asset` — odczyt struktury assetów projektu;
- `knowledge` — wyszukiwanie w wiedzy projektu.

> Długie zadania (launch/build itp.) są wykonywane asynchronicznie. Wyniki uzyskuje się przez `get_task_result`, bieżące zadania przez `list_executing_tasks`, czyszczenie przez `cleanup_tasks`.

## 5. Wersja komercyjna (DSL)

Moduł Commercial to oddzielna wersja komercyjna i nie jest częścią repozytorium open source. Wersja komercyjna oferuje **generowanie assetów DSL**: pisz Blueprinty, materiały itp. jako czytelny tekst i konwertuj je na assety silnika. Konkretne narzędzia DSL, funkcje konwersji tekstu i szczegóły składni podlegają dokumentacji komercyjnej. Funkcje licencji komercyjnych również należą do Commercial. OpenSource nie zawiera żadnego narzędzia DSL.

## 6. Następne kroki

- Architektura i mechanizmy: `architecture.md`
- Lista narzędzi i parametry: `tools-reference.md`
- Rozwój i kontrybucje: `development-guide.md` / `contributing.md`
- Częste pytania: `troubleshooting.md`
