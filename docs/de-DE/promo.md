# ForgeMind-Öffentlichkeitsarbeit

## Einleitung in einem Satz

ForgeMind ist eine Game-Engine-Steuerungsplatttform mit MCP als Schnittstelle: Es eröffnet AI-Clients das Projektmanagement, Asset-Lesen, Starten / Builden / Editor-Operationen von Unreal Engine, sodass AI die Engine wie ein Werkzeug bedienen kann.

## Kern-Highlights

- **Native MCP-Anbindung**: Standard-MCP-Protokoll (HTTP / stdio), gängige AI-Clients sofort nutzbar.
- **Open-Source-Slice selbst baubar**: Das öffentliche Repository enthält Host, alle Open-Source-Plugins, Beispiel-UE-Projekt und Tests; Apache-2.0, selbst baubar und erweiterbar.
- **Engine-Adapter-Architektur**: Host und Engine sind über eine lokale TCP-Bridge entkoppelt und auf Engines außerhalb von UE erweiterbar.
- **Offline-Asset-Lesen**: `.uasset`-Strukturen ohne Öffnen des Editors analysieren, zusammen mit dem Scan-Index (SPK) Wissensabruf bereitstellen.
- **Plugin-basierte Fähigkeitsinjektion**: Der Host enthält nur ein minimales Tool-Set, Fähigkeiten werden durch Plugins erweitert, klare Grenzen und zuschneidbar.

## Kommerzielle Version (ein Satz)

Die kommerzielle Version bietet auf Basis von OpenSource DSL-Asset-Generierung, kommerzielle Autorisierungsfähigkeiten und Enterprise-Support; Details zur DSL-Syntax und Autorisierungsweise erfragen Sie bitte beim Herausgeber.

## Externe Disziplin

- Keine Zusagen für unveröffentlichte Funktionen; der Fahrplan richtet sich nach `roadmap.md`.
- Keine Herabwürdigung ähnlicher Werkzeuge; keine Offenlegung interner Implementierungen kommerzieller Module (Autorisierungsalgorithmen, interne DSL-IR-Regeln usw.).
- Quellcode-bezogenes Verhalten richtet sich stets nach dem öffentlichen Slice; unsichere Punkte mit „noch zu bestätigen“ markieren.
