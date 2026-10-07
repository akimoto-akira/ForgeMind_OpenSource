# ForgeMind-Architektur

ForgeMind ist eine **MCP-basierte Plattform zur Steuerung von Game-Engines**: Der Server (Host) stellt KI-Clients Tools über das MCP-Protokoll bereit; Tool-Funktionen injizieren hauptsächlich Plugins. Auf Engine-Seite empfängt das Adapter-Plugin Operationen über eine lokale TCP-Bridge. UE ist die aktuelle Implementierung; die Plattform ist auf weitere Engines erweiterbar. Dieses Dokument basiert auf dem im **öffentlichen Teil** sichtbaren Code; kommerzielle Funktionen werden in §7 nur allgemein beschrieben.

## 1. Überblick

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ KI-Client   │ ◄────────────────► │ ForgeMindServer (Host) │
└─────────────┘                     │  • Integrierte Tools ×4 │
                                    │  • PluginLoader             │
                                    │  • TCP-Bridge-Server        │
                                    │  • Engine-Registry          │
                                    └──────────┬───────────┘
                                               │ Lokale TCP-Bridge (4-Byte-Präfix + JSON-Hülle)
                                    ┌──────────▼───────────┐
                                    │ UE-Adapter-Plugin     │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): MCP-Server-Kern.
- **Plugins** (`OpenSource\ForgeMind.*Plugin`): registrieren Tools über `IPlugin`. Der Host enthält nur eine minimale Tool-Suite.
- **Engine-Adapter** (`UEProject\...\Plugins\ForgeMindForUE`): Plugin im Editor. Implementiert Engine-seitige Funktionen als Bridge-Server.
- **Scannen/Verpacken** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): stellen Offline-Engine-Funktionsindex und SPK-Verpackung bereit.
- **Beispielprojekt** (`UEProject\ForgeMind_5_0`): für Tests und gemeinsames Debugging.

## 2. Host (ForgeMindServer)

- **Einstiegspunkt**: `Program.cs` startet standardmäßig einen residenten HTTP-Dienst auf `127.0.0.1:8765` (Streamable HTTP + SSE-Kompatibilität). `--stdio` wechselt in den Spawn-per-Client-Modus. Der Port kann über `--port` / `FORGEMIND_PORT` überschrieben werden.
- **Integrierte Tools** (Tool-Tabelle `ForgeMindServerHost`, nur 4):
  - `settings` (`SettingsTool`, Konfiguration lesen/schreiben);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (Abfrage und Bereinigung langer Aufgaben).
- **Plugin-Laden**: `PluginLoader` scannt `plugins\` (Standard `<Host-Verzeichnis>\plugins\`, überschreibbar mit `FORGEMIND_PLUGINS_DIR`) und injiziert über `PluginContext` die Tool-Tabelle, Engine-Registry und Bridge. Plugin `OnLoad` ruft `RegisterTool` zum Injizieren von Tools auf. Plugin-Pakete unterstützen zwei Layouts: 'Unterverzeichnis = Plugin-Paket' (Haupt-DLL + Manifest gleichen Namens + Abhängigkeiten) und 'freie DLLs im Stamm'. Das Laden ist lazy (aktiviert beim ersten Zugriff auf die Tool-Tabelle); bei Fehlern wird nur das betroffene Plugin übersprungen, der Host ist nicht betroffen.
- **Engine-Registry**: `EngineRegistry` listet beim Start lokale Engines auf (bevorzugt Launcher-Installationen `UE_x.y`, Fallback auf Source-Builds) und injiziert Plugins einen schreibgeschützten Snapshot.
- **TCP-Bridge-Server**: `ForgeMindServerBridgeServer`. Für Discovery und Verbindung des Engine-Adapters (§5).

## 3. Plugins und Tool-Injektion

| Plugin | Tools | Verantwortung |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Engine- und Projektverwaltung, Start/Build/Editor-Operationen |
| `ForgeMind.Assets` | `asset` | Offline-Lesen der `.uasset`-Struktur (`UAssetArchive` / `BlueprintGraphReader` usw.) |
| `ForgeMind.Knowledge` | `knowledge` | Wissenssuche (Ordnerquelle / Indexpaketquelle, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (CLI-Tool, kein MCP) | Engine-Enumeration → `UnrealScanner`-Scan → `.spk`-Verpackung mit `ScanResultPacker` |

Host und Plugins interagieren über den `ITool`-Vertrag (`Compat\ITool.cs`): Name/Beschreibung/Parameterschema/`Execute` gibt `ToolResult` zurück. `ForgeMindServerTest` ruft Tools auch direkt über `ITool` auf (ohne MCP-Protokoll), um das Verhalten zu verifizieren.

## 4. Scannen und SPK (Basis der Offline-Funktionen)

- `ForgeMind.Scan.Abstractions`: Abstraktionen `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` usw.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore` usw.) — analysiert UE-Header-Dateien, Regeldateien und Blueprint-Assets zu Scan-Indizes. `ScanIndexStore` ist ein Laufzeit-In-Memory-Index und wird nicht auf die Festplatte geschrieben. Dies **unterscheidet sich vom alten ScanCache** — die aktuelle Pipeline verwendet keinen ScanCache-Preload/verteilten Cache (im Code sind nur historische Kompatibilitätsspuren vorhanden; sie stellen keinen aktuellen Mechanismus dar).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` bauen und lesen Scan-Pakete.
- `ForgeMind.Speedy`: `SpeedyPack` stellt ein Hochleistungs-Paketformat bereit (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, mit internem CRC, Verzeichniszuordnung, Write-Ahead-Log `WriteAheadLog` usw.).
- Verwendung: `ForgeMind.EngineScanner <spkPath>` erzeugt `.spk`. Funktionen wie Knowledge konsumieren diese über `SpkScanDataProvider`.

## 5. Bridge-Protokoll und Engine-Adapter

- Bridge: lokales TCP. Nachrichten verwenden 4-Byte-Längenpräfix + JSON-Hülle (`BridgeMessage` / `BridgeSession` / `FrameCodec`). Der Adapter liest zuerst `%USERPROFILE%\.forgemind\bridge.json` für Discovery und Authentifizierung, dann Verbindung → Handshake → Nachrichtenschleife.
- Server-Seite leitet nach Befehlsnamen an die Adapterimplementierung weiter. Befehlshandler werden im Hauptthread der Engine (UE-Gamethread) ausgeführt, um Konsistenz mit dem Editorzustand sicherzustellen.
- Engine-Adapter (UE-Seite) befindet sich in `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, ein Editor-Modul mit Bridge-Client und IR-Applikator. Das Beispiel-UE-Projekt ist mit 5.0–5.8 kompatibel.
- Für eine neue Engine genügt es, einen isomorphen Bridge-Client und Befehlshandler zu implementieren (`development-guide.md` §4). Host- oder Plugin-Änderungen sind nicht erforderlich.

## 6. Lizenz und Sicherheit (öffentlicher Teil)

- Standardautorisierung: `OpenAuthorizationService` (`Authorization\Open\`) — feste Community-Funktionen, **analysiert keine Lizenzdateien**.
- Plugin-Sicherheit: `PluginIntegrityVerifier` (Manifest-Integritätsprüfung), `PluginSigningKeys` (Signaturprüfung); `ManifestLoader` parst Plugin-Manifeste und unterstützt ServiceOverride (Ersetzen von Host-Diensten durch abstrakte Typen, z. B. `IAuthorizationService`).
- Bridge-Authentifizierung: Adapterverbindungen müssen lokale Anmeldedaten aus `bridge.json` lesen, um den Handshake abzuschließen (`security.md`).

## 7. Kommerzielle Funktionen (Überblick)

- **DSL-Sprache und domänenspezifische Translator** (Closed Source): kommerzielle Funktionen, die Engine-Assets aus lesbarem Text generieren. Der öffentliche Teil enthält nur die DSL-Framework-Schicht (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, Schnittstellen und Registries in `ForgeMindServer\DSL\`); konkrete Syntax, domänenspezifische Translator und IR-Regeln liefern kommerzielle Module und sind nicht im Repo enthalten.
- **Kommerzielles Lizenzsystem** (Closed Source): umfasst Lizenzausstellung, Lizenzprüfung, Lizenzdienst und optionale Umgebungsbindung. Konkrete Implementierung, Signaturalgorithmen, Lizenzdateiformat und Server-seitige Logik gehören nicht zum öffentlichen Teil und werden extern nur als 'Bereitstellung zusätzlicher Funktionen' dargestellt.

> Die Open-Source-Version bietet eine vollständige Suite von Open-Source-Tools und Bridge-Funktionen. Die Commercial-Version baut auf demselben Host, Plugin-Mechanismus und Bridge-Protokoll auf und erweitert kommerzielle Funktionen (DSL-Asset-Generierung, kommerzielle Lizenzen, Enterprise-Support).

## 8. Funktionsgrenzen-Tabelle

| Funktion | Zugehörigkeit |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| DSL-Erweiterungsschnittstellen | OpenSource ✅ |
| DSL-Parser | Commercial 🔒 |
| DSL-Translator | Commercial 🔒 |
| IR-Regeln | Commercial 🔒 |
| DSL-Asset-Generator | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Kommerzielle Lizenzimplementierung | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Schlüssel-/Signatursystem | Commercial 🔒 |
| Interne Lizenzprotokoll-Details | Commercial 🔒 |
