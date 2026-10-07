# ForgeMind-Dokumentation (Deutsch)

ForgeMind — eine MCP-basierte Plattform zur Steuerung von Game-Engines: Über das MCP-Protokoll stellt sie KI-Clients die Verwaltung von Unreal-Engine-Projekten, das Lesen von Assets sowie Start-/Build-/Editor-Operationen bereit. Die aktuelle Implementierung zielt auf UE (5.0–5.8); das Plattformdesign ist auf weitere Engines erweiterbar.

> Diese Dokumentation konzentriert sich auf den **Open-Source-Teil**: `OpenSource\` (Host und Plugins), `UEProject\` (Beispielprojekt) und `tests\` (Tests). Kommerzielle Funktionen (Generierung von DSL-Assets, Implementierung kommerzieller Lizenzen) werden nur allgemein beschrieben und sind in der Open-Source-Version nicht enthalten. OpenSource behält die erforderlichen Lizenzabstraktionen und eine offene Implementierung bei.

## Dokumentationsindex

| Dokument | Inhalt |
|---|---|
| [Schnellstart](getting-started.md) | Umgebungsanforderungen, Build, Start, erste Verwendung |
| [Architektur](architecture.md) | Host / Plugins / Bridge-Protokoll / Scannen und SPK |
| [API-Referenz](api-reference.md) | MCP-Protokoll, Bridge-Protokoll, Tool-Verträge |
| [Tool-Referenz](tools-reference.md) | Alle MCP-Tools und Parameter |
| [Entwicklungsleitfaden](development-guide.md) | Build, Debugging, Hinzufügen von Tools/Adaptern, Tests |
| [Mitwirkungsleitfaden](contributing.md) | Open-Source-Grenzen, PR-Prozess, Testanforderungen |
| [Fehlerbehebung](troubleshooting.md) | Häufige Probleme und Lösungen |
| [Roadmap](roadmap.md) | Geplante Richtung |
| [Werbung](promo.md) | Projektvorstellung und Kommunikationsregeln |
| [Sicherheit](security.md) | Sicherheitsdesign und empfohlene Vorgehensweisen |

## Übersicht des Open-Source-Umfangs

```
OpenSource\
  ForgeMindServer               MCP-Host (4 integrierte Tools + Plugin-Laden + TCP-Bridge)
  ForgeMind.OfflineEngineKit    Engine-/Projekt-/Start-/Build-/Editor-Tools (Plugin)
  ForgeMind.Assets              Offline-Lesen von .uasset (Plugin)
  ForgeMind.Knowledge           Wissenssuche (Plugin)
  ForgeMind.Scan.Abstractions   Abstraktionen für Scandaten und Provider
  ForgeMind.Scan                SPK-Lesen und -Verpacken
  ForgeMind.Scan.Unreal         Unreal-Engine-Scan-Implementierung
  ForgeMind.EngineScanner       Eigenständiger CLI-Scanner, der direkt .spk erzeugt
  ForgeMind.Speedy              Low-Level-SPK-Basis
UEProject\ForgeMind_5_0         UE-Beispielprojekt (5.0–5.8)
tests\                          Öffentliche Unit-Tests + Server-Integrationstests
```

## Lizenz

Der Inhalt des Open-Source-Teils ist unter Apache-2.0 lizenziert. Kommerzielle Module sind nicht Teil des Open-Source-Teils und werden unter einer separaten kommerziellen Lizenz bereitgestellt.
