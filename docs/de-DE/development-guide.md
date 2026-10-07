# ForgeMind-Entwicklungsleitfaden

Für Entwickler, die ForgeMind erweitern möchten: wie man baut, debuggt, neue MCP-Tools hinzufügt, neue Engine-Adapter hinzufügt und Tests ausführt. Dieser Artikel basiert auf dem echten Code des **öffentlichen Slices** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Projektübersicht

| Projekt | Verantwortung |
|---|---|
| `OpenSource\ForgeMindServer` | MCP-Server-Host: 4 integrierte Tools, startet TCP-Bridge, lädt Plugins |
| `OpenSource\ForgeMind.OfflineEngineKit` | Plugin: engine / project / project_files / switch_version / launch / build / editor Tools |
| `OpenSource\ForgeMind.Assets` | Plugin: asset Tool (Offline-Lesen von `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Plugin: knowledge Tool (Wissensabruf) |
| `OpenSource\ForgeMind.EngineScanner` | CLI-Scanner: zählt Engines auf und packt `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Scan-Index: Abstraktion, Paketierung, UE-Implementierung (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | SPK-Paketformat (Hochleistungs-Paketdatei) |
| `tests\ForgeMind.PublicTests` | Öffentliche Unit-Tests (ohne Engine-Abhängigkeit) |
| `tests\ForgeMindServerTest` | Server-Integrationstests (benötigen UE-Editor) |
| `UEProject\ForgeMind_5_0` | Beispiel-UE-Projekt (5.0–5.8) |

## 2. Build

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugin- und Bibliotheksprojekte: siehe contributing.md §3 (nicht die gesamte ForgeMind.sln bauen; sie enthält kommerzielle Projektverweise)
```

- Host-Einstieg: `OpenSource\ForgeMindServer\Program.cs` (HTTP-Dauerbetrieb standardmäßig auf `127.0.0.1:8765`, `--stdio` zum Umschalten, `--port` / `FORGEMIND_PORT` überschreiben den Port).
- Logging: `LogManager`; Tools geben `ToolResult` zurück (Erfolg / Fehler mit Meldung).

## 3. Plugin-Mechanismus und Hinzufügen von MCP-Tools

Der Host enthält nur 4 integrierte Tools: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. Alle weiteren Tools werden von Plugins im plugins-Verzeichnis injiziert:

- Plugin-Verzeichnis: `<Host-Verzeichnis>\plugins\` (die Umgebungsvariable `FORGEMIND_PLUGINS_DIR` überschreibt es).
- Plugin-Paketlayout: `plugins\<Plugin-Name>\` enthält Haupt-DLL + gleichnamige `.manifest.json` + Abhängigkeiten; lose DLLs im Stammverzeichnis werden ebenfalls unterstützt (manuelles Drop-in).
- Der Host-`PluginLoader` scannt Plugins → `PluginContext` injiziert (Tool-Tabellen, Engine-Registrierung, Bridge) → das Plugin ruft in `OnLoad` `RegisterTool` auf, um Tools zu registrieren.
- Plugin-Integrität: `PluginIntegrityVerifier` / `PluginSigningKeys` sind für Manifest- und Signaturprüfung zuständig; vertrauenswürdige Plugins können Dienste über ServiceOverride erweitern (siehe §7).

Ein neues MCP-Tool hinzufügen:

1. Den `ITool`-Vertrag implementieren (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: Tool-Name und Anzeigename;
   - `Description`: Beschreibung für AI-Clients;
   - `GetParameterSchema()`: JSON-Schema-artige Parameterdeklaration;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: gibt `ToolResult` zurück.
2. Ein neues Plugin-Projekt erstellen (oder zu einem bestehenden Plugin wie OfflineEngineKit hinzufügen), `IPlugin` implementieren und in `OnLoad` `RegisterTool` aufrufen.
3. Die gleichnamige `.manifest.json` im selben Verzeichnis ablegen (Plugin-Beschreibung / Abhängigkeitsdeklaration).
4. Wenn Engine-seitige Fähigkeiten benötigt werden, über Bridge-Befehle senden und im Adapter einen Befehls-Handler implementieren (siehe §5).
5. Testfälle schreiben (§6) und `tools-reference.md` aktualisieren.

## 4. Hinzufügen eines neuen Engine-Adapters

ForgeMind nutzt „Adapter“ für engine-spezifische Logik; neue Engines folgen demselben Bridge-Protokoll (4-Byte-Längenpräfix + JSON-Envelope, siehe `api-reference.md`):

1. Bridge-Client implementieren: `%USERPROFILE%\.forgemind\bridge.json` lesen für Discovery und Authentifizierung, verbinden → Handshake → Nachrichtenschleife.
2. Whitelist-Handler nach Befehlsnamen registrieren; alle Befehle werden im Engine-Hauptthread (entspricht dem Game-Thread bei UE) ausgeführt.
3. Engine-seitige Fähigkeiten implementieren (Asset-Lesen/-Schreiben, Projektoperationen usw.) und auf Server-Tool-Rückgaben abbilden.
4. Testfälle und Dokumentation für diese Engine ergänzen.

Die erste Implementierung ist der UE-Adapter `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (Editor-Modul-Plugin, Bridge-Client + IR-Anwender).

## 5. Engine-seitige Bridge-Befehle (aktuelle UE-Implementierung)

- Das Adapter-Plugin `ForgeMindForUE` enthält den Bridge-Client; Befehls-Handler laufen im Game-Thread.
- Asset-Operationen können die IR-Domain-Handler im Plugin wiederverwenden, um doppelte Editor-Logik zu vermeiden.
- Debugging: `LogForgeMindBridge`-Kategorie im UE-Editor-Log; bei Verbindungsabbruch erneutes Verbinden mit exponentiellem Backoff.

## 6. Tests ausführen

### Öffentliche Unit-Tests (kein Editor erforderlich)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Abdeckung: SPK-Paket-Roundtrip, Multi-Version-SPK, Scan-Datenanbieter usw. (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Server-Integrationstests (UE-Editor + Beispielprojekt erforderlich)

```powershell
# Standard: Host und Plugins vorbauen und in das plugins-Verzeichnis staging, dann Host starten, Plugins laden, alle ITestCase per Reflexion ausführen
ForgeMindServerTest.exe

# Filtern: nach Gruppe + Reihenfolge
ForgeMindServerTest.exe [group] [order]          # oder --Test [group] [order]
# Vorbau überspringen
ForgeMindServerTest.exe --NoBuild
```

Mechanismus:

- Vorbau: zuerst `ForgeMindServer.csproj` bauen, dann Plugin-Projekte unter `ForgeMind.*` mit gleichnamiger `.manifest.json` finden, einzeln bauen und nach `<Einstiegsverzeichnis>\plugins\<Plugin-Name>\` staging.
- Ausführung: `ITestCase` per Reflexion finden, nach `Group` / `Order` sortieren, **fail-fast**; „Editor starten / Editor schließen“ sind ebenfalls normale Testfälle (`Order=0` startet, `Order=int.MaxValue` schließt), Gruppen teilen sich eine Session.
- Testfälle rufen Tools direkt über `ITool.Execute` auf (nicht über das MCP-Protokoll); intern kommunizieren die Tools über die Bridge mit UE.
- Ausgabe: nach der Ausführung ein JSON-Report und eine lesbare Zusammenfassung (Ausgabeverzeichnis siehe Hinweis im Ausführungsergebnis).
- Test-Assets: 30+ `Blueprint_*_Tests.cs` unter `Task\UE5_0\` (Blueprint-Bereiche: Variablen / Funktionen / Events / Materialien / Makros / Interfaces usw.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` kapselt Bridge-Befehle, Textverteilung und IR-Snapshot-Vergleich (Schutz vor unbeabsichtigten Änderungen).

> Hinweis: Testfälle, die „Textpfade (DSL)“ betreffen, hängen von der kommerziellen DSL-Fähigkeit ab; die Integrationstests des öffentlichen Slices konzentrieren sich hauptsächlich auf Bridge-Befehlspfade, Einzelheiten siehe die tatsächlichen Fälle in `Task\` des Slices.

## 7. Lizenzierung und kommerzielle Fähigkeiten (Überblick)

- Standard `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`): feste Community-Fähigkeiten, parst keine Lizenzdateien.
- Kommerzielle Lizenzierung ersetzt die `IAuthorizationService`-Implementierung über ServiceOverride im Plugin-Manifest (Host `ApplyServiceOverride`). Der öffentliche Slice enthält dieses kommerzielle Plugin nicht, sondern nur den Ersetzungsmechanismus (der Host kennt keinen „kommerziellen“ Begriff, er ersetzt nur nach abstraktem Typ).

## 8. Codierungsstandards

- Kopfkommentare enthalten SPDX-Lizenzkennzeichnung und Copyright-Hinweis (Apache-2.0).
- Keine Referenzen auf kommerzielle Module; neue Plugins halten die Slice-Grenzen von `contributing.md` ein.
