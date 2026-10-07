# Documentazione ForgeMind (italiano)

ForgeMind — piattaforma di controllo di motori di gioco basata su MCP: tramite il protocollo MCP mette a disposizione dei client AI la gestione di progetti Unreal Engine, la lettura di asset e le operazioni di avvio/build/editor. L'implementazione attuale è UE (5.0–5.8); il design della piattaforma è estendibile ad altri motori.

> Questa documentazione si concentra sulla parte **open source**: `OpenSource\` (host e plugin), `UEProject\` (progetto di esempio) e `tests\` (test). Le funzionalità commerciali (generazione asset DSL, implementazione licenze commerciali) sono descritte solo a livello generale e non sono incluse nella versione open source. OpenSource conserva le astrazioni di licenza necessarie e un'implementazione aperta.

## Indice della documentazione

| Documento | Contenuto |
|---|---|
| [Guida rapida](getting-started.md) | Requisiti ambiente, compilazione, avvio, primo utilizzo |
| [Architettura](architecture.md) | Host / plugin / protocollo bridge / scan e SPK |
| [Riferimento API](api-reference.md) | Protocollo MCP, protocollo bridge, contratti strumenti |
| [Riferimento strumenti](tools-reference.md) | Tutti gli strumenti MCP e i loro parametri |
| [Guida sviluppo](development-guide.md) | Compilazione, debug, aggiunta strumenti/adattatori, test |
| [Guida contributi](contributing.md) | Limiti open source, processo PR, requisiti test |
| [Risoluzione problemi](troubleshooting.md) | Problemi comuni e soluzioni |
| [Roadmap](roadmap.md) | Direzione pianificata |
| [Promozione](promo.md) | Presentazione del progetto e regole di comunicazione |
| [Sicurezza](security.md) | Progettazione della sicurezza e buone pratiche |

## Panoramica dell'ambito open source

```
OpenSource\
  ForgeMindServer               Host MCP (4 strumenti integrati + caricamento plugin + bridge TCP)
  ForgeMind.OfflineEngineKit    Strumenti motore/progetto/avvio/build/editor (plugin)
  ForgeMind.Assets              Lettura offline .uasset (plugin)
  ForgeMind.Knowledge           Ricerca conoscenza (plugin)
  ForgeMind.Scan.Abstractions   Astrazioni dati scan e provider
  ForgeMind.Scan                Lettura e impacchettamento SPK
  ForgeMind.Scan.Unreal         Implementazione scan Unreal Engine
  ForgeMind.EngineScanner       Scanner CLI autonomo che genera direttamente .spk
  ForgeMind.Speedy              Base SPK di basso livello
UEProject\ForgeMind_5_0         Progetto UE di esempio (5.0–5.8)
tests\                          Test unitari pubblici + test di integrazione del server
```

## Licenza

Il contenuto della parte open source è concesso in licenza Apache-2.0. I moduli commerciali non fanno parte della parte open source e sono forniti con licenza commerciale separata.
