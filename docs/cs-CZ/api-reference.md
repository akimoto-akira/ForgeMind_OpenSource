# Referenční příručka API ForgeMind

Tento dokument popisuje veřejnou část externích rozhraní ForgeMind: připojení MCP protokolem, bridge protokol, kontrakty nástrojů a mechanismus dlouhých úloh. Komerční funkce (DSL) jsou popsány pouze obecně v §6.

## 1. Připojení MCP

- **HTTP režim** (výchozí): `http://127.0.0.1:8765/mcp`, Streamable HTTP (kompatibilní s předchozím SSE). Host proces je rezidentní, jedno připojení obslouží více klientských relací.
- **stdio režim**: `ForgeMindServer.exe --stdio`, pro každého klienta se spustí samostatný proces.
- Port: `--port N` > proměnná prostředí `FORGEMIND_PORT` > výchozí 8765.

Podporované standardní metody MCP: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled` atd. Schéma nástrojů poskytuje `ITool.GetParameterSchema()`.

## 2. Kontrakt nástrojů

Nástroje serveru implementují jednotné rozhraní (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: identifikátor a zobrazovaný název nástroje;
- `Description`: popisný text pro AI klienta;
- `GetParameterSchema()`: deklarace parametrů (styl JSON Schema);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: po provedení vrací `ToolResult` (výsledek úspěchu nebo chybovou zprávu).

Tok volání: klient `tools/call` → host identifikuje `ITool` → validace parametrů → `Execute` → vrácení výsledku. Nástroje vyžadující enginové funkce interně vysílají příkazy přes TCP bridge.

## 3. Bridge protokol (host ↔ adaptér enginu)

- Transport: lokální TCP. Formát rámce = 4bajtový prefix délky + JSON obálka (`BridgeMessage`, viz `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Objev a autentizace: adaptér čte `%USERPROFILE%\.forgemind\bridge.json` (obsahuje host, port a pověření), dokončí připojení → handshake → smyčku zpráv.
- Sémantika příkazů: požadavek obsahuje název příkazu a parametry. Server směruje příkaz do implementace adaptéru; odpověď obsahuje stavový kód, zprávu a data. Příkazy na straně enginu se provádějí v hlavním vlákně enginu (UE herní vlákno).
- Životní cyklus: host začne naslouchat bridge při spuštění. Adaptér se při odpojení znovu připojuje s exponenciálním backoffem.

## 4. Mechanismus dlouhých úloh

- Dlouhé úlohy (`launch`, `build` atd.) se provádějí asynchronně a neblokují volání nástroje.
- Související vestavěné nástroje:
  - `get_task_result`: dotaz na výsledek úlohy;
  - `list_executing_tasks`: seznam probíhajících úloh;
  - `cleanup_tasks`: vyčištění záznamů úloh.
- Interní `LongTaskManager` spravuje životní cyklus úloh (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Vestavěné nástroje (součást hostu, bez pluginů)

| Nástroj | Popis |
|---|---|
| `settings` | Čtení/zápis konfigurace serveru (`McpSettingsManager`, okamžitá persistence) |
| `get_task_result` | Dotaz na výsledek dlouhé úlohy |
| `list_executing_tasks` | Dotaz na probíhající úlohy |
| `cleanup_tasks` | Vyčištění záznamů úloh |

Úplný seznam nástrojů injektovaných pluginy viz `tools-reference.md`.

## 6. Komerční funkce (DSL, přehled)

- OpenSource poskytuje **kontrakty pro rozšíření DSL** (rozhraní a registry `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` v `ForgeMindServer\DSL\` jsou veřejně dostupné). Konkrétní implementace DSL syntaxe, Parser, Translator, IR pravidel a logiky generování assetů patří Commercial a v OpenSource není.
- DSL nástroje injektuje komerční plugin. Konkrétní názvy a API se řídí dokumentací komerční verze. Seznam nástrojů OpenSource DSL nástroje neobsahuje.

| Funkce | Příslušnost |
|---|---|
| Rozhraní rozšíření DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| IR pravidla | Commercial 🔒 |
| Logika generování assetů | Commercial 🔒 |

## 7. Poznámky ke kompatibilitě

- Formát rámce bridge protokolu a schéma nástrojů se řídí tímto dokumentem. Rušivé změny budou předem oznámeny s migrační upozorněním.
