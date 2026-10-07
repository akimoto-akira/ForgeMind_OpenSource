# ForgeMind-Fehlerbehebung

Häufige Probleme und Lösungen (basierend auf den realen Mechanismen des öffentlichen Slices).

## 1. Build-Fehler

**`dotnet build ForgeMind.sln` meldet „Projekt Commercial\... nicht gefunden“**
Ursache: `ForgeMind.sln` enthält Verweise auf kommerzielle Projekte (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`), aber das Commercial-Verzeichnis wird nicht mit dem Open-Source-Slice veröffentlicht.
Lösung: Projekte einzeln bauen (`contributing.md` §3), nicht die gesamte sln bauen; oder das Build-Skript des Herausgebers verwenden.

**Nach dem Plugin-Build sieht der Host die Tools immer noch nicht**
- Prüfen, ob Plugin-DLL und gleichnamige `.manifest.json` im Host-`plugins\<Plugin-Name>\` liegen (oder im mit `FORGEMIND_PLUGINS_DIR` angegebenen Verzeichnis);
- Im Host-Log prüfen, ob der Plugin-Scan dieses Paket übersprungen hat (`PluginLoader` überspringt bei einem fehlgeschlagenen Paket nur dieses, ohne den Host zu beeinträchtigen);
- Host neu starten (die Tool-Tabelle wird beim ersten Zugriff verzögert geladen).

## 2. Startprobleme des Servers

**Port ist belegt**
Wenn `http://127.0.0.1:8765` belegt ist, wird eine `HttpListenerException` gemeldet.
Lösung: mit `--port N` oder der Umgebungsvariable `FORGEMIND_PORT` einen anderen Port verwenden; die Client-Adresse entsprechend aktualisieren.

**Client kann sich nicht verbinden / tools/list ist leer**
- Prüfen, ob der Server lauscht (Log „Listening on http://127.0.0.1:<port>/mcp“);
- Prüfen, ob die Client-URL `http://127.0.0.1:8765/mcp` ist (HTTP-Modus);
- Prüfen, ob die Plugins geladen sind (siehe oben).

## 3. Probleme mit Engine / Bridge-Verbindung

**Der Editor ist geöffnet, aber das Tool meldet „Engine nicht verbunden / Bridge nicht verfügbar“**
- Prüfen, ob das `ForgeMindForUE`-Plugin im Projekt aktiviert ist (`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`);
- Der Bridge-Adapter liest `%USERPROFILE%\.forgemind\bridge.json` für Discovery und Authentifizierung; bei fehlender Datei / falschen Anmeldedaten schlägt die Verbindung fehl;
- Im UE-Editor-Log nach `LogForgeMindBridge` sehen; bei Verbindungsabbruch erfolgt erneutes Verbinden mit exponentiellem Backoff, einfach auf die Wiederverbindung warten.

**`launch` / `build` hängen**
Langzeitaufgaben laufen asynchron; mit `get_task_result` das Ergebnis abfragen, mit `list_executing_tasks` laufende Aufgaben anzeigen; `cleanup_tasks` kann Aufzeichnungen bereinigen.

## 4. Testprobleme

**Vorbau von `ForgeMindServerTest` schlägt fehl**
Mit `--NoBuild` kann der Vorbau übersprungen werden (wenn CI bereits frische Artefakte garantiert); der Standardablauf baut den Host und staggt Plugins nach `<Einstieg>\plugins\`, bei Fehler wird ohne Ausführen der Testfälle beendet.

**Tests brechen durch fail-fast ab**
Integrationstests laufen nach Group / Order mit fail-fast: Stopp beim ersten Fehler (Must-Run-Fälle wie „Editor schließen“ innerhalb derselben Gruppe werden trotzdem ausgeführt). Nach der Korrektur mit `ForgeMindServerTest.exe [group]` die Gruppe erneut ausführen.

**Tests benötigen Editor / Beispielprojekt**
Fälle unter `Task\UE5_0\` benötigen den UE-Editor und eine Session; reine Datenverarbeitungsfälle (SPK / Scan) laufen in `ForgeMind.PublicTests` ohne Editor-Abhängigkeit.

## 5. Daten- / Asset-Probleme

**`asset`-Lesen schlägt fehl**
Beim Offline-Lesen von `.uasset` muss die Engine-Version passen (`UAssetVersions`); bei Versionskonflikt kann die Analyse fehlschlagen. Engine-Version und unterstützten Tool-Bereich prüfen.

**`.spk` kann nicht geladen werden**
Prüfen, ob die Datei von `ForgeMind.EngineScanner <spkPath>` erzeugt und nicht beschädigt wurde; SPK enthält eine CRC-Prüfung (`Speedy\Internal\Crc32`), beschädigte Dateien werden abgelehnt.

## 6. Sonstiges

**Wie aktiviert man das Log**
Server-Logs werden von `LogManager` ausgegeben (Kategorien u. a. `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer`); auf der Bridge-Seite im UE-Editor-Ausgabelog nachsehen.

**Fragen zu kommerziellen Fähigkeiten**
DSL-Syntax, Lizenzierung und License-Fragen bitte an den Herausgeber wenden; die OpenSource-Version parst keine Lizenzdateien, die Fähigkeiten sind fest geöffnet (siehe `security.md`).
