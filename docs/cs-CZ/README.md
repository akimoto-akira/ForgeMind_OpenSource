# Dokumentace ForgeMind (česky)

ForgeMind — platforma pro ovládání herních enginů s rozhraním MCP: prostřednictvím protokolu MCP zpřístupňuje AI klientům správu projektů Unreal Engine, čtení assetů a operace spuštění/sestavení/editoru. Aktuální implementace cílí na UE (5.0–5.8); návrh platformy je rozšiřitelný na další enginy.

> Tato dokumentace se zaměřuje na **open source část**: `OpenSource\` (host a pluginy), `UEProject\` (ukázkový projekt) a `tests\` (testy). Komerční funkce (generování DSL assetů, implementace komerčních licencí) jsou popsány pouze obecně a v Open Source verzi nejsou obsaženy. OpenSource zachovává nezbytné licenční abstrakce a otevřenou implementaci.

## Rejstřík dokumentace

| Dokument | Obsah |
|---|---|
| [Rychlý start](getting-started.md) | Požadavky prostředí, sestavení, spuštění, první použití |
| [Architektura](architecture.md) | Host / pluginy / bridge protokol / skenování a SPK |
| [Referenční příručka API](api-reference.md) | MCP protokol, bridge protokol, kontrakty nástrojů |
| [Referenční příručka nástrojů](tools-reference.md) | Všechny MCP nástroje a parametry |
| [Průvodce vývojem](development-guide.md) | Sestavení, ladění, přidávání nástrojů/adaptérů, testy |
| [Průvodce přispíváním](contributing.md) | Hranice open source, PR proces, požadavky na testy |
| [Řešení problémů](troubleshooting.md) | Běžné problémy a řešení |
| [Plán](roadmap.md) | Plánovaný směr |
| [Propagace](promo.md) | Představení projektu a pravidla komunikace |
| [Bezpečnost](security.md) | Bezpečnostní návrh a doporučené postupy |

## Přehled rozsahu open source

```
OpenSource\
  ForgeMindServer               MCP host (4 vestavěné nástroje + načítání pluginů + TCP bridge)
  ForgeMind.OfflineEngineKit    Nástroje engine/projekt/spuštění/sestavení/editor (plugin)
  ForgeMind.Assets              Offline čtení .uasset (plugin)
  ForgeMind.Knowledge           Znalostní vyhledávání (plugin)
  ForgeMind.Scan.Abstractions   Abstrakce dat a providerů skenování
  ForgeMind.Scan                Čtení a balení SPK
  ForgeMind.Scan.Unreal         Implementace skenování Unreal Engine
  ForgeMind.EngineScanner       Samostatný CLI skener, který přímo generuje .spk
  ForgeMind.Speedy              Nízkoúrovňová základna SPK
UEProject\ForgeMind_5_0         Ukázkový projekt UE (5.0–5.8)
tests\                          Veřejné jednotkové testy + integrační testy serveru
```

## Licence

Obsah open source části je licencován Apache-2.0. Komerční moduly nejsou součástí open source části a jsou poskytovány pod samostatnou komerční licencí.
