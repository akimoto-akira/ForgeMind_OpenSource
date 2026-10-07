# ForgeMind-Schnellstart

ForgeMind ist eine **MCP-basierte Plattform zur Steuerung von Game-Engines**: Über das MCP-Protokoll stellt sie KI-Clients die Verwaltung von Unreal-Engine-Projekten, das Lesen von Assets sowie Start-/Build-/Editor-Operationen bereit. Die aktuelle Implementierung ist UE; die Plattform ist erweiterbar. Die Open-Source-Version enthält eine Reihe von Engine-Tools und Bridge-Funktionen; die kommerzielle Version fügt die Generierung von DSL-Assets hinzu (§5).

Alle Schritte dieser Anleitung lassen sich mit dem **Open-Source-Teil** (`OpenSource\` + `UEProject\` + `tests\`) durchführen.

## 1. Umgebungsanforderungen

- .NET SDK (zum Erstellen von Host und Plugins).
- Unreal Engine 5.0–5.8 (einschließlich Editor). Das Beispielprojekt ist `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- MCP-kompatibler Client (Claude Desktop / Cursor / eigener Client).

## 2. Erstellen von Server und Plugins

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugins (nach dem Build DLL + .manifest.json mit gleichem Namen in das Plugins-Verzeichnis des Hosts legen)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Die gesamte `ForgeMind.sln` nicht direkt erstellen (sie enthält Verweise auf kommerzielle Projekte; das kommerzielle Verzeichnis ist nicht Teil des Repos). Plugins können auch aus vom Distributor bereitgestellten Build-Artefakten verwendet werden.

## 3. Start

1. **Server starten** (Standard: residentes HTTP, Port 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Oder die kompilierte ForgeMindServer.exe ausführen; konfigurierbar über --port N / --stdio / FORGEMIND_PORT
   ```

2. **UE-Editor und Beispielprojekt öffnen**: `ForgeMind_5_0.uproject` in UE starten; das Plugin `ForgeMindForUE` startet im Editor automatisch den Bridge-Client und verbindet sich mit dem Host.

3. **Client konfigurieren**: Server zum MCP-Client hinzufügen:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Nach der Verbindung erscheint die Tool-Liste (4 integrierte + injizierte Plugins).

## 4. Erste Verwendung

- `engine` — Anzeige der aktuellen Engine-Informationen;
- `project` / `project_files` — Anzeige des Beispielprojekts und seiner Dateien;
- `launch` — Starten des Editors, `editor` — Editor-Operationen;
- `asset` — Lesen der Projekt-Asset-Struktur;
- `knowledge` — Suche im Projektwissen.

> Langlaufende Aufgaben (launch/build usw.) werden asynchron ausgeführt. Ergebnisse erhalten Sie über `get_task_result`, laufende Aufgaben sehen Sie mit `list_executing_tasks`, aufräumen mit `cleanup_tasks`.

## 5. Kommerzielle Version (DSL)

Das Commercial-Modul ist eine separate kommerzielle Version und nicht Teil des Open-Source-Repos. Die kommerzielle Version bietet die **Generierung von DSL-Assets**: Schreiben Sie Blueprints, Materialien usw. als lesbaren Text und konvertieren Sie sie in Engine-Assets. Konkrete DSL-Tools, Textkonvertierungsfunktionen und Syntaxdetails richten sich nach der kommerziellen Dokumentation. Kommerzielle Lizenzfunktionen gehören ebenfalls zu Commercial. OpenSource enthält keine DSL-Tools.

## 6. Nächste Schritte

- Architektur und Mechanismen: `architecture.md`
- Tool-Liste und Parameter: `tools-reference.md`
- Entwicklung und Mitwirkung: `development-guide.md` / `contributing.md`
- Häufige Fragen: `troubleshooting.md`
