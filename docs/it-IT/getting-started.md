# Guida rapida ForgeMind

ForgeMind è una **piattaforma di controllo di motori di gioco basata su MCP**: tramite il protocollo MCP mette a disposizione dei client AI la gestione di progetti Unreal Engine, la lettura di asset e le operazioni di avvio/build/editor. L'implementazione attuale è UE; la piattaforma è estendibile. La versione open source contiene una suite di strumenti motore e funzioni bridge; la versione commerciale aggiunge la generazione asset DSL (§5).

Tutti i passaggi di questa guida possono essere eseguiti con la parte **open source** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Requisiti ambiente

- SDK .NET (per compilare host e plugin).
- Unreal Engine 5.0–5.8 (incluso editor). Il progetto di esempio è `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- Client compatibile MCP (Claude Desktop / Cursor / client personalizzato).

## 2. Compilazione di server e plugin

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugin (dopo la compilazione, posizionare DLL + .manifest.json con lo stesso nome nella cartella plugins dell'host)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Non compilare direttamente l'intero `ForgeMind.sln` (contiene riferimenti a progetti commerciali; la cartella commerciale non è nel repository). I plugin possono anche essere utilizzati dagli artefatti di build forniti dal distributore.

## 3. Avvio

1. **Avviare il server** (default: HTTP residente, porta 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Oppure eseguire il ForgeMindServer.exe compilato; configurabile con --port N / --stdio / FORGEMIND_PORT
   ```

2. **Aprire l'editor UE e il progetto di esempio**: avviare `ForgeMind_5_0.uproject` in UE; il plugin `ForgeMindForUE` avvia automaticamente il client bridge nell'editor e si collega all'host.

3. **Configurare il client**: aggiungere il server al client MCP:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Dopo la connessione, appare l'elenco degli strumenti (4 integrati + plugin iniettati).

## 4. Primo utilizzo

- `engine` — visualizzazione delle informazioni sul motore corrente;
- `project` / `project_files` — visualizzazione del progetto di esempio e dei suoi file;
- `launch` — avvio dell'editor, `editor` — operazioni nell'editor;
- `asset` — lettura della struttura degli asset del progetto;
- `knowledge` — ricerca nella conoscenza del progetto.

> Le attività lunghe (launch/build, ecc.) vengono eseguite in modo asincrono. I risultati si ottengono con `get_task_result`, le attività in corso con `list_executing_tasks`, la pulizia con `cleanup_tasks`.

## 5. Versione commerciale (DSL)

Il modulo Commercial è una versione commerciale separata e non fa parte del repository open source. La versione commerciale offre la **generazione asset DSL**: scrivi Blueprint, materiali, ecc. come testo leggibile e convertili in asset del motore. Gli strumenti DSL concreti, le funzioni di conversione testo e i dettagli della sintassi seguono la documentazione commerciale. Anche le funzioni di licenza commerciale appartengono a Commercial. OpenSource non contiene alcuno strumento DSL.

## 6. Passi successivi

- Architettura e meccanismi: `architecture.md`
- Elenco strumenti e parametri: `tools-reference.md`
- Sviluppo e contributi: `development-guide.md` / `contributing.md`
- Domande frequenti: `troubleshooting.md`
