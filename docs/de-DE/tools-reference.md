# ForgeMind-Tool-Referenz

Alle Tools werden in 'im Host integriert' und 'von Plugins injiziert' unterteilt. Die aktuelle Open-Source-Version enthält 13 MCP-Tools. Das kommerzielle Plugin kann weitere Tools bereitstellen (Generierung von DSL-Assets usw., siehe §3; nicht Teil der Open-Source-Liste).

## 1. Integrierte Host-Tools

Der Host (`OpenSource\ForgeMindServer`) enthält die folgenden Tools ohne Plugins:

| Tool | Beschreibung |
|---|---|
| `settings` | Lesen/Schreiben der Serverkonfiguration (`McpSettingsManager`, sofortige Persistenz) |
| `get_task_result` | Abfrage des Ergebnisses langer Aufgaben (launch / build usw.) |
| `list_executing_tasks` | Liste laufender langer Aufgaben |
| `cleanup_tasks` | Bereinigung abgeschlossener Aufgabendatensätze |

## 2. Von Plugins injizierte Tools

Plugins injizieren Tools über `PluginContext.RegisterTool` in `OnLoad`; sie erscheinen nach der Client-Verbindung. Nach dem Build legen Sie die Plugin-DLL + `.manifest.json` mit gleichem Namen in das `plugins\`-Verzeichnis des Hosts (oder das über `FORGEMIND_PLUGINS_DIR` festgelegte Verzeichnis).

### ForgeMind.OfflineEngineKit (7)

| Tool | Beschreibung |
|---|---|
| `engine` | Abfrage lokal installierter Engines (Launcher / Source-Builds, einschließlich Version und App-Name) |
| `project` | Abfrage/Verwaltung von UE-Projekten (Projektdatei-Identifikation, Projektinfo-Abfrage) |
| `project_files` | Auflistung der Projektdateien (Filterung nach Verzeichnis/Typ) |
| `switch_version` | Wechsel der Projektversion auf die angegebene Engine (über UnrealVersionSelector) |
| `launch` | Starten des Editors oder gepackter ausführbarer Dateien (Debug-Konfiguration unterstützt) |
| `build` | Projekt-Build (Standardziel / Editor-Ziel) |
| `editor` | Editor-interne Operationen (Szene, Viewport, Assets usw., über Bridge gesendet) |

Die Parameterschemata folgen `GetParameterSchema()` des jeweiligen Tool-Quellcodes (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Einige Engine-Operationen unterliegen den Berechtigungen von `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Tool | Beschreibung |
|---|---|
| `asset` | Offline-Lesen der `.uasset`-Struktur (Namen, Objekte, Eigenschaften, Blueprint-Graphen, Abfrage nach Graph/Eigenschaft) |

Die Implementierung basiert auf `UAsset\UAssetSerializer` / `BlueprintGraphReader` usw. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Tool | Beschreibung |
|---|---|
| `knowledge` | Wissenssuche: Abfrage von Wissenseinträgen zu Projekt/Engine (Quellen: Ordnerquelle `FolderKnowledgeSource`, SPK-Indexpaketquelle `IndexPackSource`) |

Implementierung in `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Kommerzielle Version-Tools (Überblick)

Die kommerzielle Version kann die Generierung von DSL-Assets und zugehörige Abfragetools hinzufügen, die vom kommerziellen Plugin injiziert werden. Konkrete Namen, APIs und DSL-Funktionen richten sich nach der kommerziellen Dokumentation. OpenSource enthält keine DSL-Tools.

## 4. Tool-Aufrufkonventionen

- Tools für lange Aufgaben geben eine Aufgaben-ID zurück. Das Ergebnis erhalten Sie über `get_task_result`.
- Tool-Fehler geben eine `ToolResult`-Fehlermeldung zurück (direkt an den Client anzeigbar).
- Tool-Beschreibungen und Parameterschemata entsprechen dem, was `tools/list` von MCP anzeigt, und stimmen mit dem Quellcode überein.
