# ForgeMind-Mitwirkungsleitfaden

Vielen Dank für Ihr Interesse an ForgeMind. Das öffentliche Repository entspricht dem OpenSource-Veröffentlichungsslice von ForgeMind und enthält nur die veröffentlichten Open-Source-Projekte, Tests und Beispielprojekte. Kommerzielle Module (DSL-Domänenübersetzer, Autorisierungssystem usw.) sind nicht im öffentlichen Repository enthalten. Bitte verschaffen Sie sich vor einem Beitrag einen Überblick über die Aufgabenteilung.

## 1. Aufbau des öffentlichen Repositorys

Die oberste Struktur des Open-Source-Slices (das von Ihnen geklonte Repository):

```
ForgeMind.sln                          # Lösung (enthält Verweise auf kommerzielle Projekte, siehe unten)
OpenSource\                            # Open-Source-Projekte
  ForgeMindServer\                     # MCP-Server (Host)
  ForgeMind.OfflineEngineKit\          # Engine-Offline-Tool-Plugin (engine/project/launch/build/editor usw.)
  ForgeMind.Assets\                    # Asset-Lese-Plugin (asset)
  ForgeMind.Knowledge\                 # Wissensabruf-Plugin (knowledge)
  ForgeMind.EngineScanner\             # Engine-Scanner (Kommandozeile)
  ForgeMind.Scan\                      # Scan-Paketierung
  ForgeMind.Scan.Abstractions\         # Scan-Abstraktion
  ForgeMind.Scan.Unreal\               # UE-Scan-Implementierung (DocumentEngine)
  ForgeMind.Speedy\                    # SPK-Paketformat
UEProject\
  ForgeMind_5_0\                       # Beispiel-UE-Projekt (5.0–5.8 kompatibel)
tests\
  ForgeMind.PublicTests\               # Öffentliche Unit-Tests (SPK / Scandaten)
  ForgeMindServerTest\                 # Server-Integrationstests (benötigen UE-Editor)
```

> Hinweis: Die Lösungsdatei des aktuellen Entwicklungsrepositorys (`ForgeMind.sln`) enthält Verweise auf Commercial-Projekte (z. B. `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`); die öffentliche Veröffentlichung **enthält das `Commercial/`-Verzeichnis nicht**. OpenSource-Nutzer sollten daher die öffentlichen Projekte einzeln bauen (siehe §3) und nicht direkt `dotnet build ForgeMind.sln` ausführen.

## 2. Open-Source-Grenzen

- **OpenSource\**: vollständig Open Source (Apache-2.0), Hauptbereich für Beiträge.
- **UEProject\**: Beispielprojekt ist Open Source, aber beim Slice-Export werden binäre `*.uasset`-Assets ausgeschlossen (unter Content bleiben nur textuelle Assets wie Karten und das Verzeichnisgerüst).
- **tests\**: Open Source, Beiträge benötigen begleitende Tests.
- **Commercial ist nicht im öffentlichen Repository**: DSL-Domänenübersetzer, kommerzielle Lizenzierung usw. erscheinen nicht im Slice; Open-Source-Code darf keine kommerziellen Modultypen referenzieren.
- Beim Export werden automatisch `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache` usw. (Build-Artefakte und temporäre Verzeichnisse) ausgeschlossen; die Lizenzkennzeichnung im Dateikopf wird beim Export einheitlich durch Apache-2.0 ersetzt.

## 3. Build

```powershell
# Einzeln bauen (nicht die gesamte sln direkt bauen, siehe oben)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Plugin-Projekte (nach dem Build Artefakte + gleichnamige .manifest.json in das plugins-Verzeichnis des Hosts legen)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

Der Host lädt Plugins aus `<Host-Verzeichnis>\plugins\` (mit der Umgebungsvariable `FORGEMIND_PLUGINS_DIR` überschreibbar); ein Plugin-Paket = Haupt-DLL + gleichnamige `.manifest.json` + Abhängigkeiten (siehe `development-guide.md` §3).

## 4. Codierungsstandards

- Alle Quelldateien behalten im Kopf die SPDX-Lizenzkennzeichnung und den Copyright-Hinweis (derzeit Apache-2.0).
- Namespaces sind nach Modulen organisiert (`ForgeMindServer.*` / `ForgeMind.*`), öffentliche Verträge laufen über abstrakte Schnittstellen wie `ITool` / `IPlugin`.
- Service-Tool-Klassen müssen vollständige `Description` und Parameter-Schema bereitstellen (diese Texte werden direkt AI-Clients ausgesetzt).
- Engine-Adapter-Code befolgt die Codierungsstandards der jeweiligen Engine (derzeit UE-seitig: Typ-Präfixe F / U / T, `TEXT()`-Makro, Game-Thread-Sicherheitsbeschränkungen).
- Neuer Code darf keine kommerziellen Module referenzieren oder von ihnen abhängen.

## 5. PR-Prozess

1. Von `main` einen Branch ziehen, Namensvorschlag `feat/<Beschreibung>` oder `fix/<Beschreibung>`.
2. Commit-Nachricht: `<Typ>: <Satzweise Beschreibung>`.
3. Selbstprüfung vor dem Commit:
   - Einzeln nach §3 bauen, alle erfolgreich;
   - Bei Änderungen an Bridge-Befehlen / Tool-Verhalten: die zugehörigen `ForgeMindServerTest`-Gruppen bestehen (Ausführungsweise siehe `development-guide.md` §6);
   - Bei Änderungen an SPK / Scandaten: `ForgeMind.PublicTests` bestehen;
   - Keine Verweise auf kommerzielle Module eingeführt;
   - Die entsprechende Seite unter `docs\zh-CN\` synchron aktualisieren.
4. Beim Eröffnen der PR angeben: Änderungsumfang, Testergebnis-Zusammenfassung, ob ein bestimmtes Modul vom Maintainer erneut geprüft werden muss.
5. Prüfpunkte des Maintainers: Open-Source-Grenzen, API-Kompatibilität, Testabdeckung.

## 6. Testanforderungen

- Änderungen an Bridge-Befehlen / Tool-Verhalten / Adapter-Logik müssen Testfälle mitbringen (unter `tests\ForgeMindServerTest\Task\` neue `*_Tests.cs` hinzufügen).
- Server-Integrationstests benötigen eine UE-Editor-Umgebung; reine Datenverarbeitungstests (SPK, Scan) liegen in `tests\ForgeMind.PublicTests\`, ohne Engine-Abhängigkeit.
- Testfälle müssen unter der Standard-Reihenfolge stabil bestehen (fail-fast, Stopp beim ersten Fehler).

## 7. Dokumentationsbeiträge

- Chinesische Dokumentation liegt unter `docs\zh-CN\`; der Inhalt muss mit den Quellcode-Fakten übereinstimmen, unsichere Punkte mit „noch zu bestätigen“ markieren.
- Beim Ändern der externen Tool-Liste `tools-reference.md` synchron aktualisieren.
- Kommerzielle Fähigkeiten (DSL-Syntax, Lizenzierungsmechanismus) werden in der Dokumentation nur überblicksweise beschrieben, ohne interne Details.

## 8. Verhaltenskodex

- Bestehende Designentscheidungen respektieren (Bridge-Protokoll, Tool-Verträge, SPK-Format); bei größeren Änderungen zuerst eine Diskussion anstoßen.
- Konkurrenzprodukte nicht öffentlich herabwürdigen; werblicher Inhalt folgt der externen Disziplin in `promo.md`.
