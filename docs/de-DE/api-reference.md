# ForgeMind-API-Referenz

Dieses Dokument beschreibt den öffentlichen Teil der externen Schnittstellen von ForgeMind: MCP-Verbindung, Bridge-Protokoll, Tool-Verträge und den Mechanismus langer Aufgaben. Kommerzielle Funktionen (DSL) werden in §6 nur allgemein beschrieben.

## 1. MCP-Verbindung

- **HTTP-Modus** (Standard): `http://127.0.0.1:8765/mcp`, Streamable HTTP (kompatibel mit älterem SSE). Der Host-Prozess ist resident; eine Verbindung bedient mehrere Client-Sitzungen.
- **stdio-Modus**: `ForgeMindServer.exe --stdio`, pro Client wird ein eigener Prozess gestartet.
- Port: `--port N` > Umgebungsvariable `FORGEMIND_PORT` > Standard 8765.

Unterstützte Standard-MCP-Methoden: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled` usw. Das Tool-Schema liefert `ITool.GetParameterSchema()`.

## 2. Tool-Vertrag

Die Server-Tools implementieren ein einheitliches Interface (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: Tool-Identifikator und Anzeigename;
- `Description`: Beschreibungstext für den KI-Client;
- `GetParameterSchema()`: Parameterdeklaration (JSON-Schema-Stil);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: gibt nach Ausführung `ToolResult` zurück (Erfolgs- oder Fehlermeldung).

Aufruffluss: Client `tools/call` → Host identifiziert `ITool` → Parametervalidierung → `Execute` → Ergebnis zurückgeben. Tools, die Engine-Funktionen benötigen, senden intern Befehle über die TCP-Bridge.

## 3. Bridge-Protokoll (Host ↔ Engine-Adapter)

- Transport: lokales TCP. Frame-Format = 4-Byte-Längenpräfix + JSON-Hülle (`BridgeMessage`, siehe `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Discovery und Authentifizierung: Der Adapter liest `%USERPROFILE%\.forgemind\bridge.json` (enthält Host, Port und Anmeldedaten), führt Verbindung → Handshake → Nachrichtenschleife aus.
- Befehlsemantik: Eine Anfrage enthält Befehlsnamen und Parameter. Der Server leitet den Befehl an die Adapterimplementierung weiter; die Antwort enthält Statuscode, Nachricht und Daten. Engine-seitige Befehle werden im Hauptthread der Engine (UE-Gamethread) ausgeführt.
- Lebenszyklus: Der Host beginnt beim Start, auf der Bridge zu lauschen. Der Adapter verbindet sich bei Trennung mit exponentiellem Backoff erneut.

## 4. Mechanismus langer Aufgaben

- Lange Aufgaben (`launch`, `build` usw.) werden asynchron ausgeführt und blockieren den Tool-Aufruf nicht.
- Zugehörige integrierte Tools:
  - `get_task_result`: Abfrage des Aufgabenergebnisses;
  - `list_executing_tasks`: Liste laufender Aufgaben;
  - `cleanup_tasks`: Bereinigung von Aufgabendatensätzen.
- Intern verwaltet `LongTaskManager` den Lebenszyklus der Aufgaben (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Integrierte Tools (Teil des Hosts, ohne Plugins)

| Tool | Beschreibung |
|---|---|
| `settings` | Lesen/Schreiben der Serverkonfiguration (`McpSettingsManager`, sofortige Persistenz) |
| `get_task_result` | Abfrage des Ergebnisses langer Aufgaben |
| `list_executing_tasks` | Abfrage laufender Aufgaben |
| `cleanup_tasks` | Bereinigung von Aufgabendatensätzen |

Die vollständige Liste der von Plugins injizierten Tools finden Sie in `tools-reference.md`.

## 6. Kommerzielle Funktionen (DSL, Überblick)

- OpenSource stellt **DSL-Erweiterungsverträge** bereit (Schnittstellen und Registries `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` in `ForgeMindServer\DSL\` sind öffentlich verfügbar). Konkrete DSL-Syntaximplementierungen, Parser, Translator, IR-Regeln und Asset-Generierungslogik gehören zu Commercial und sind nicht in OpenSource enthalten.
- DSL-Tools injiziert das kommerzielle Plugin. Konkrete Namen und APIs richten sich nach der kommerziellen Dokumentation. Die OpenSource-Tool-Liste enthält keine DSL-Tools.

| Funktion | Zugehörigkeit |
|---|---|
| DSL-Erweiterungsschnittstellen | OpenSource ✅ |
| DSL-Parser | Commercial 🔒 |
| DSL-Translator | Commercial 🔒 |
| IR-Regeln | Commercial 🔒 |
| Asset-Generierungslogik | Commercial 🔒 |

## 7. Kompatibilitätshinweise

- Das Frame-Format des Bridge-Protokolls und die Tool-Schemas richten sich nach diesem Dokument. Breaking Changes werden vorab mit Migrationshinweis angekündigt.
