# ForgeMind-Sicherheit

Dieser Artikel beschreibt das Sicherheitsdesign und Nutzungsempfehlungen im öffentlichen Slice.

## 1. Lokale Kommunikationsgrenze

- **Der Server lauscht standardmäßig nur auf Loopback**: Im HTTP-Modus auf `127.0.0.1:8765`, nicht für externe Netzwerke geöffnet.
- **Die Bridge ist ein lokales TCP**: Die Verbindung zwischen Host und Engine-Adapter ist eine lokale TCP-Verbindung; der Adapter muss `%USERPROFILE%\.forgemind\bridge.json` (enthält Host, Port, Anmeldedaten) für Discovery und Authentifizierung lesen und dann den Handshake durchführen.
- Server-Port nicht ins öffentliche Netz exponiert; für Fernzugriff in ein vertrauenswürdiges Netz stellen und über einen sicheren Kanal (z. B. SSH-Tunnel) weiterleiten.

## 2. Plugin-Integrität

- Der Host-`PluginLoader` führt beim Laden von Plugins eine **Manifest-Integritätsprüfung** (`PluginIntegrityVerifier`) und Signaturverifizierung (`PluginSigningKeys`) durch: Ein Plugin-Paket muss Haupt-DLL + gleichnamige `.manifest.json` enthalten; fehlend oder nicht passend wird es abgelehnt / übersprungen.
- Die Tool-Injektion durch Plugins findet im Host-Prozess statt; nur Plugins aus vertrauenswürdigen Quellen installieren (aus dem öffentlichen Slice gebaut oder offizielle Veröffentlichung).

## 3. Autorisierungsmodell

**Öffentlich sichtbare Sicherheitsmechanismen**:
- `IAuthorizationService`: Abstraktion des Autorisierungseinstiegs des Hosts, Standardimplementierung ist `OpenAuthorizationService` (feste Community-Fähigkeiten, parst keine Lizenzdateien, erzeugt keine Testlizenzen).
- Vertrauenswürdiges ServiceOverride: Plugins können Host-Dienste über ServiceOverride im Manifest nach abstraktem Typ ersetzen (der Host kennt keinen „kommerziellen“ Begriff, ersetzt nur nach Typ).
- Plugin-Identitätsprüfung und Manifest-Validierung: `PluginIntegrityVerifier` / `PluginSigningKeys` prüfen Plugin-Manifest und Signatur, um sicherzustellen, dass nur vertrauenswürdige Plugins geladen werden.
- Bridge-Verbindungsauthentifizierung: Der Adapter führt über `bridge.json` lokale Discovery und Anmelde-Handshake durch, um die Vertrauenswürdigkeit der Verbindungsquelle zu bestätigen.

**Kommerzielle Lizenzierung**: Core bietet die `IAuthorizationService`-Abstraktion und den vertrauenswürdigen ServiceOverride-Erweiterungsmechanismus. Die Implementierung der kommerziellen Lizenzierung gehört zum Closed-Source-Plugin; konkrete Lizenzprotokolle, Ausstellung, Verifizierung und Bindungslogik gehören nicht zum öffentlichen Slice.

## 4. Tool-Berechtigungen

- Engine-Operationstools werden durch `ServerPermissionChecker` (in den jeweiligen Plugins) eingeschränkt; vor der Ausführung wird die Fähigkeitsautorisierung geprüft.
- Fehlgeschlagene Tool-`Execute`-Aufrufe geben einheitlich eine `ToolResult`-Fehlermeldung zurück, ohne interne Stack-Details preiszugeben.

## 5. Nutzungsempfehlungen

- Den MCP-Port nur vertrauenswürdigen Clients öffnen; Clients besitzen Engine-Operationsfähigkeiten und entsprechen lokalen Verwaltungsrechten.
- Plugins, Host und Beispielprojekt aktuell halten, um zeitnah Sicherheitsfixes zu erhalten.
- Auf gemeinsam genutzten Rechnern die `bridge.json`-Anmeldedaten nicht an nicht vertrauenswürdige Prozesse weitergeben.
- Sicherheitsprobleme über den Kanal des Herausgebers melden, Details unveröffentlichter Schwachstellen nicht öffentlich preisgeben.
