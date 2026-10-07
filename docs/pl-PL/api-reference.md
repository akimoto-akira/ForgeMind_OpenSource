# Referencja API ForgeMind

Ten dokument opisuje publiczną część zewnętrznych interfejsów ForgeMind: połączenie MCP, protokół bridge, kontrakty narzędzi i mechanizm długich zadań. Funkcje komercyjne (DSL) są opisane tylko ogólnie w §6.

## 1. Połączenie MCP

- **Tryb HTTP** (domyślny): `http://127.0.0.1:8765/mcp`, Streamable HTTP (kompatybilny ze starym SSE). Proces hosta jest rezydentny; jedno połączenie obsługuje wiele sesji klientów.
- **Tryb stdio**: `ForgeMindServer.exe --stdio`, osobny proces jest uruchamiany dla każdego klienta.
- Port: `--port N` > zmienna środowiskowa `FORGEMIND_PORT` > domyślnie 8765.

Obsługiwane standardowe metody MCP: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled` itp. Schemat narzędzi jest dostarczany przez `ITool.GetParameterSchema()`.

## 2. Kontrakt narzędzi

Narzędzia serwera implementują ujednolicony interfejs (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: identyfikator i nazwa wyświetlana narzędzia;
- `Description`: tekst opisowy dla klienta AI;
- `GetParameterSchema()`: deklaracja parametrów (styl JSON Schema);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: zwraca `ToolResult` po wykonaniu (wynik sukcesu lub komunikat błędu).

Przepływ wywołania: klient `tools/call` → host identyfikuje `ITool` → walidacja parametrów → `Execute` → zwrot wyniku. Narzędzia wymagające funkcji silnika wysyłają wewnętrznie komendy przez bridge TCP.

## 3. Protokół bridge (host ↔ adapter silnika)

- Transport: lokalne TCP. Format ramki = 4-bajtowy prefiks długości + koperta JSON (`BridgeMessage`, patrz `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Odkrywanie i autoryzacja: adapter czyta `%USERPROFILE%\.forgemind\bridge.json` (zawiera host, port i poświadczenia), wykonuje połączenie → handshake → pętlę komunikatów.
- Semantyka komend: żądanie zawiera nazwę komendy i jej parametry. Serwer kieruje komendę do implementacji adaptera; odpowiedź zawiera kod stanu, komunikat i dane. Komendy po stronie silnika są wykonywane w głównym wątku silnika (wątek gry UE).
- Cykl życia: host zaczyna nasłuchiwać bridge przy starcie. Adapter ponownie łączy się z wykładniczym backoffem po rozłączeniu.

## 4. Mechanizm długich zadań

- Długie zadania (`launch`, `build` itp.) są wykonywane asynchronicznie i nie blokują wywołania narzędzia.
- Powiązane narzędzia wbudowane:
  - `get_task_result`: zapytanie o wynik zadania;
  - `list_executing_tasks`: lista bieżących zadań;
  - `cleanup_tasks`: czyszczenie rekordów zadań.
- Wewnętrznie `LongTaskManager` zarządza cyklem życia zadań (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Narzędzia wbudowane (część hosta, bez wtyczek)

| Narzędzie | Opis |
|---|---|
| `settings` | Odczyt/zapis konfiguracji serwera (`McpSettingsManager`, natychmiastowa trwałość) |
| `get_task_result` | Zapytanie o wynik długich zadań |
| `list_executing_tasks` | Zapytanie o bieżące zadania |
| `cleanup_tasks` | Czyszczenie rekordów zadań |

Pełna lista narzędzi wstrzykiwanych przez wtyczki jest w `tools-reference.md`.

## 6. Funkcje komercyjne (DSL, przegląd)

- OpenSource dostarcza **kontrakty rozszerzeń DSL** (interfejsy i rejestry `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` w `ForgeMindServer\DSL\` są publiczne). Konkretne implementacje składni DSL, Parser, Translator, reguł IR i logiki generowania assetów należą do Commercial i nie są w OpenSource.
- Narzędzia DSL są wstrzykiwane przez wtyczkę komercyjną. Konkretne nazwy i API podlegają dokumentacji komercyjnej. Lista narzędzi OpenSource nie zawiera żadnego narzędzia DSL.

| Funkcja | Przynależność |
|---|---|
| Interfejsy rozszerzeń DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Reguły IR | Commercial 🔒 |
| Logika generowania assetów | Commercial 🔒 |

## 7. Uwagi o zgodności

- Format ramki protokołu bridge i schematy narzędzi podlegają temu dokumentowi. Zmiany łamiące zgodność będą ogłaszane z wyprzedzeniem z notą migracyjną.
