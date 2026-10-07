# Guida allo sviluppo di ForgeMind

Per gli sviluppatori che desiderano estendere ForgeMind: come compilare, eseguire il debug, aggiungere nuovi strumenti MCP, aggiungere adattatori per nuovi motori ed eseguire i test. Questo documento è basato sul codice reale dello **slice pubblico** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Panoramica del progetto

| Progetto | Ruolo |
|---|---|
| `OpenSource\ForgeMindServer` | Host del server MCP: integra 4 strumenti, avvia il bridge TCP e carica i plugin |
| `OpenSource\ForgeMind.OfflineEngineKit` | Plugin: strumenti engine / project / project_files / switch_version / launch / build / editor |
| `OpenSource\ForgeMind.Assets` | Plugin: strumento asset (lettura offline di `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Plugin: strumento knowledge (ricerca nella knowledge base) |
| `OpenSource\ForgeMind.EngineScanner` | Scanner da riga di comando: enumera i motori e crea i pacchetti `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Indice di scansione: astrazione, packaging, implementazione UE (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | Formato di packaging SPK (file pacchetto ad alte prestazioni) |
| `tests\ForgeMind.PublicTests` | Test unitari pubblici (nessuna dipendenza dal motore) |
| `tests\ForgeMindServerTest` | Test di integrazione del server (richiede l'editor UE) |
| `UEProject\ForgeMind_5_0` | Progetto UE di esempio (5.0–5.8) |

## 2. Compilazione

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Progetti di plugin e librerie: vedi contributing.md §3 (non compilare direttamente l'intero ForgeMind.sln, contiene riferimenti a progetti commerciali)
```

- Punto di ingresso dell'host: `OpenSource\ForgeMindServer\Program.cs` (HTTP in ascolto su `127.0.0.1:8765` per impostazione predefinita, passaggio a `--stdio`, override della porta con `--port` / `FORGEMIND_PORT`).
- Log: `LogManager`; gli strumenti restituiscono `ToolResult` (successo / errore con messaggio).

## 3. Meccanismo dei plugin e nuovi strumenti MCP

L'host integra solo 4 strumenti: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. Gli altri strumenti sono forniti dai **plugin nella directory plugins**:

- Directory dei plugin: `<directory_host>\plugins\` (override tramite la variabile d'ambiente `FORGEMIND_PLUGINS_DIR`).
- Layout del pacchetto plugin: all'interno di `plugins\<nome-plugin>\` si trovano la DLL principale, l'`.manifest.json` con lo stesso nome e le dipendenze; sono supportate anche DLL sparse nella directory radice (drop-in manuale).
- Il `PluginLoader` dell'host esegue la scansione dei plugin → iniezione del `PluginContext` (tabella degli strumenti, registry dei motori, bridge) → il plugin chiama `RegisterTool` in `OnLoad` per registrare gli strumenti.
- Integrità dei plugin: `PluginIntegrityVerifier` / `PluginSigningKeys` verificano manifest e firme; i plugin attendibili possono estendere i servizi tramite ServiceOverride (vedi §7).

Per aggiungere un nuovo strumento MCP:

1. Implementare il contratto `ITool` (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: nome e nome visualizzato dello strumento;
   - `Description`: descrizione destinata ai client AI;
   - `GetParameterSchema()`: dichiarazione dei parametri in stile JSON Schema;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: restituisce `ToolResult`.
2. Creare un nuovo progetto plugin (o aggiungerlo a uno esistente, ad esempio OfflineEngineKit), implementare `IPlugin` e chiamare `RegisterTool` in `OnLoad`.
3. Nella stessa directory posizionare l'`.manifest.json` con lo stesso nome (descrizione del plugin / dichiarazione delle dipendenze).
4. Per funzionalità lato motore, inviare i comandi tramite il bridge e implementare il gestore dei comandi lato adattatore (vedi §5).
5. Scrivere i casi di test (§6) e aggiornare `tools-reference.md`.

## 4. Come aggiungere un nuovo adattatore per motore

ForgeMind gestisce la logica specifica del motore tramite «adattatori»; un nuovo motore segue lo stesso protocollo bridge (prefisso di 4 byte per la lunghezza + envelope JSON, vedi `api-reference.md`):

1. Implementare il client bridge: leggere `%USERPROFILE%\.forgemind\bridge.json` per discovery e autenticazione, connettersi → handshake → loop dei messaggi.
2. Registrare i gestori nella whitelist in base ai nomi dei comandi; tutti i comandi vengono eseguiti sul thread principale del motore (corrispondente al game thread di UE).
3. Implementare le funzionalità lato motore (lettura/scrittura asset, operazioni sul progetto, ecc.) e mapparle ai risultati degli strumenti del server.
4. Aggiungere i casi di test e la documentazione per quel motore.

La prima implementazione è l'adattatore UE `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (plugin modulo Editor, client bridge + applicatore IR).

## 5. Comandi bridge lato motore (implementazione UE attuale)

- Il plugin `ForgeMindForUE` lato adattatore contiene il client bridge; i gestori dei comandi vengono eseguiti sul game thread.
- Le operazioni a livello di asset possono riutilizzare i gestori IR del plugin, evitando di duplicare la logica dell'editor.
- Debug: categoria `LogForgeMindBridge` nei log dell'editor UE; in caso di disconnessione, riconnessione con backoff esponenziale.

## 6. Esecuzione dei test

### Test unitari pubblici (senza editor)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Coprono il round-trip di packaging SPK, SPK multiversione e i provider di dati di scansione (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Test di integrazione del server (richiede editor UE + progetto di esempio)

```powershell
# Predefinito: compila host e plugin, li staging nella directory plugins, avvia l'host, carica i plugin ed esegue riflessivamente tutti gli ITestCase
ForgeMindServerTest.exe

# Filtro: per gruppo + ordine
ForgeMindServerTest.exe [group] [order]          # o --Test [group] [order]
# Salta la pre-compilazione
ForgeMindServerTest.exe --NoBuild
```

Meccanismo:

- Pre-compilazione: compila prima `ForgeMindServer.csproj`, poi individua i progetti plugin con `.manifest.json` omonimo sotto `ForgeMind.*` e li compila singolarmente, effettuando lo staging in `<directory_ingresso>\plugins\<nome-plugin>\`.
- Esecuzione: rilevamento riflessivo degli `ITestCase`, ordinati per `Group` / `Order`, **fail-fast**; «avvio editor / chiusura editor» sono anch'essi casi ordinari (`Order=0` avvio, `Order=int.MaxValue` chiusura), stessa sessione condivisa per gruppo.
- I casi chiamano direttamente gli strumenti tramite `ITool.Execute` (senza passare dal protocollo MCP); internamente gli strumenti comunicano con UE tramite il bridge.
- Output: al termine viene generato un report JSON e un summary leggibile (la directory di output è indicata nel risultato di esecuzione).
- Asset di test: 30+ `Blueprint_*_Tests.cs` sotto `Task\UE5_0\` (vari domini dei blueprint: variabili / funzioni / eventi / materiali / macro / interfacce ecc.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` incapsula i comandi bridge, la distribuzione del testo e il confronto degli snapshot IR (per prevenire modifiche accidentali).

> Nota: i casi che coinvolgono «path testuali (DSL)» dipendono dalle funzionalità DSL commerciali; i test di integrazione dello slice pubblico si basano principalmente sui comandi bridge, come definito dai casi reali in `Task\` dello slice.

## 7. Autorizzazione e funzionalità commerciali (panoramica)

- Predefinito `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`): capacità fisse della community, non analizza alcun file di licenza.
- L'autorizzazione commerciale viene fornita sostituendo `IAuthorizationService` tramite ServiceOverride nel manifest del plugin (l'host applica `ApplyServiceOverride`). Lo slice pubblico non contiene quel plugin commerciale, mantiene solo il meccanismo di sostituzione (l'host non è a conoscenza del concetto «commerciale», sostituisce semplicemente in base al tipo astratto).

## 8. Convenzioni di codifica

- L'intestazione dei file include l'identificativo SPDX della licenza e la dichiarazione di copyright (Apache-2.0).
- Non fare riferimento a moduli commerciali; i nuovi plugin rispettano i confini dello slice definiti in `contributing.md`.
