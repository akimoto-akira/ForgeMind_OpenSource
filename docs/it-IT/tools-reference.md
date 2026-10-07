# Riferimento strumenti ForgeMind

Tutti gli strumenti sono divisi in «integrati nell'host» e «iniettati dai plugin». La versione open source attuale contiene 13 strumenti MCP. Il plugin commerciale può fornire altri strumenti (generazione asset DSL, ecc., vedi §3; non inclusi nell'elenco open source).

## 1. Strumenti integrati dell'host

L'host (`OpenSource\ForgeMindServer`) contiene i seguenti strumenti senza plugin:

| Strumento | Descrizione |
|---|---|
| `settings` | Lettura/scrittura configurazione server (`McpSettingsManager`, persistenza immediata) |
| `get_task_result` | Interrogazione del risultato delle attività lunghe (launch / build, ecc.) |
| `list_executing_tasks` | Elenco delle attività lunghe in corso |
| `cleanup_tasks` | Pulizia dei record delle attività completate |

## 2. Strumenti iniettati dai plugin

I plugin iniettano strumenti tramite `PluginContext.RegisterTool` in `OnLoad`; compaiono dopo la connessione del client. Dopo la compilazione, posizionare la DLL del plugin + `.manifest.json` con lo stesso nome nella cartella `plugins\` dell'host (o la cartella definita da `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7)

| Strumento | Descrizione |
|---|---|
| `engine` | Interrogazione dei motori installati localmente (Launcher / build sorgente, inclusi versione e nome applicazione) |
| `project` | Interrogazione/gestione progetti UE (identificazione file di progetto, interrogazione informazioni progetto) |
| `project_files` | Elenco dei file del progetto (filtro per cartella/tipo) |
| `switch_version` | Cambio versione del progetto al motore specificato (tramite UnrealVersionSelector) |
| `launch` | Avvio dell'editor o degli eseguibili pacchettizzati (supportata configurazione di debug) |
| `build` | Compilazione del progetto (target default / target Editor) |
| `editor` | Operazioni nell'editor (scena, viewport, asset, ecc., inviate tramite bridge) |

Gli schemi dei parametri seguono `GetParameterSchema()` del codice sorgente di ogni strumento (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Alcune operazioni motore sono soggette alle autorizzazioni di `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Strumento | Descrizione |
|---|---|
| `asset` | Lettura offline della struttura `.uasset` (nomi, oggetti, proprietà, grafi Blueprint, interrogazione per grafo/proprietà) |

L'implementazione si basa su `UAsset\UAssetSerializer` / `BlueprintGraphReader`, ecc. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Strumento | Descrizione |
|---|---|
| `knowledge` | Ricerca conoscenza: interrogazione delle voci di conoscenza progetto/motore (fonti: sorgente cartella `FolderKnowledgeSource`, sorgente pacchetto indice SPK `IndexPackSource`) |

Implementazione in `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Strumenti della versione commerciale (panoramica)

La versione commerciale può aggiungere la generazione asset DSL e strumenti di interrogazione correlati, iniettati dal plugin commerciale. I nomi, le API e le funzioni DSL concrete seguono la documentazione commerciale. OpenSource non contiene alcuno strumento DSL.

## 4. Convenzioni di chiamata degli strumenti

- Gli strumenti di attività lunghe restituiscono un ID attività. Il risultato si ottiene con `get_task_result`.
- Gli errori degli strumenti restituiscono un `ToolResult` di errore (visualizzabile direttamente dal client).
- Descrizioni e schemi dei parametri degli strumenti corrispondono a ciò che `tools/list` di MCP mostra e concordano con il codice sorgente.
