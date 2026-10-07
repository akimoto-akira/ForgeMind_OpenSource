# ForgeMind-Fahrplan

Die Richtung wird aus öffentlicher Perspektive dargestellt und nach Meilensteinen sortiert; konkrete Termine richten sich nach der Veröffentlichung.

## Kurzfristig

- **Vervollständigung des Open-Source-Slices**: Build-Skripte und Ein-Klick-Debugging-Anleitungen ergänzen, Einstiegskosten für neue Mitwirkende senken.
- **Dokumentationssynchronisierung**: `docs\zh-CN\` mit den Quellcode-Änderungen pflegen, Konsistenz mit dem öffentlichen Slice sicherstellen.
- **Testabdeckung**: Fälle in `ForgeMind.PublicTests` und `ForgeMindServerTest` erweitern (Bridge-Befehle, Scan, SPK-Grenzfälle).

## Mittelfristig

- **Erweiterung der Engine-Adapter**: Aufbauend auf dem bestehenden UE-Adapter (5.0–5.8) weitere Engine-/Editor-Anbindungen erkunden (gleiches Bridge-Protokoll).
- **Verbesserte Scan- und Wissensfähigkeiten**: die Abdeckung von DocumentEngine für Header-Dateien / Blueprints / Regeldateien verbessern, Effizienz von SPK-Paketierung und inkrementellen Updates steigern.
- **Plugin-Ökosystem**: Plugin-API und Manifest-Spezifikation vervollständigen, vielfältigere Drittanbieter-Tool-Plugins unterstützen.

## Langfristig

- **Öffnung der DSL-Nutzungsdokumentation**: nach Stabilisierung der kommerziellen DSL können je nach Produktstrategie Teile der Nutzungsdokumentation, Beispiele und Lehrmaterialien veröffentlicht werden; DSL-Implementierung, Translator, IR-Regeln und Asset-Generierungslogik bleiben kommerzielle Module.
- **Plattformisierung**: ForgeMind als engine-übergreifendes MCP-Steuerungsplatttform positionieren, engine-unabhängige Protokoll- und Abstraktionsschichten konsolidieren.

> Die kommerzielle Richtung (Autorisierungssystem, Enterprise-Support usw.) wird in diesem öffentlichen Fahrplan nicht ausgeführt; bitte wenden Sie sich an den Herausgeber.
