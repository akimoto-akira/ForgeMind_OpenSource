# Architettura ForgeMind

ForgeMind è una **piattaforma di controllo di motori di gioco basata su MCP**: il server (host) mette a disposizione dei client AI strumenti tramite il protocollo MCP; le funzioni degli strumenti sono principalmente iniettate dai plugin. Lato motore, il plugin adattatore riceve le operazioni tramite un bridge TCP locale. UE è l'implementazione attuale; la piattaforma è estendibile ad altri motori. Questo documento si basa sul codice visibile nella parte **pubblica**; le funzioni commerciali sono descritte solo a livello generale al §7.

## 1. Panoramica

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ Client AI   │ ◄────────────────► │ ForgeMindServer (host)│
└─────────────┘                     │  • Strumenti integrati ×4│
                                    │  • PluginLoader             │
                                    │  • Server bridge TCP       │
                                    │  • Registro motori         │
                                    └──────────┬───────────┘
                                               │ Bridge TCP locale (prefisso 4 byte + envelope JSON)
                                    ┌──────────▼───────────┐
                                    │ Plugin adattatore UE  │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): nucleo del server MCP.
- **Plugin** (`OpenSource\ForgeMind.*Plugin`): registrano strumenti tramite `IPlugin`. L'host contiene solo una suite minima di strumenti.
- **Adattatore motore** (`UEProject\...\Plugins\ForgeMindForUE`): plugin nell'editor. Implementa le funzioni lato motore come server bridge.
- **Scan/impacchettamento** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): forniscono l'indice offline delle funzioni motore e l'impacchettamento SPK.
- **Progetto di esempio** (`UEProject\ForgeMind_5_0`): per test e debug comuni.

## 2. Host (ForgeMindServer)

- **Punto di ingresso**: `Program.cs` avvia per default un servizio HTTP residente su `127.0.0.1:8765` (Streamable HTTP + compatibilità SSE). `--stdio` passa alla modalità spawn-per-client. La porta può essere sovrascritta con `--port` / `FORGEMIND_PORT`.
- **Strumenti integrati** (tabella strumenti `ForgeMindServerHost`, solo 4):
  - `settings` (`SettingsTool`, lettura/scrittura configurazione);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (interrogazione e pulizia attività lunghe).
- **Caricamento plugin**: `PluginLoader` analizza `plugins\` (default `<cartella host>\plugins\`, sovrascrivibile con `FORGEMIND_PLUGINS_DIR`) e inietta la tabella strumenti, il registro motori e il bridge tramite `PluginContext`. Il plugin `OnLoad` chiama `RegisterTool` per iniettare strumenti. I pacchetti plugin supportano due layout: «sottocartella = pacchetto plugin» (DLL principale + manifest con lo stesso nome + dipendenze) e «DLL libere alla radice». Il caricamento è lazy (attivato al primo accesso alla tabella strumenti); in caso di errore, viene ignorato solo il plugin interessato, l'host non viene influenzato.
- **Registro motori**: `EngineRegistry` enumera all'avvio i motori locali (preferenza alle installazioni Launcher `UE_x.y`, ripiego sui build sorgente) e inietta ai plugin uno snapshot in sola lettura.
- **Server bridge TCP**: `ForgeMindServerBridgeServer`. Per la scoperta e la connessione dell'adattatore motore (§5).

## 3. Plugin e iniezione di strumenti

| Plugin | Strumenti | Responsabilità |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Gestione motori e progetti, avvio/compilazione/operazioni editor |
| `ForgeMind.Assets` | `asset` | Lettura offline della struttura `.uasset` (`UAssetArchive` / `BlueprintGraphReader`, ecc.) |
| `ForgeMind.Knowledge` | `knowledge` | Ricerca conoscenza (sorgente cartella / sorgente pacchetto indice, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (strumento CLI, non MCP) | Enumerazione motori → scan `UnrealScanner` → impacchettamento `.spk` con `ScanResultPacker` |

L'host e i plugin interagiscono tramite il contratto `ITool` (`Compat\ITool.cs`): nome/descrizione/schema parametri/`Execute` restituisce `ToolResult`. `ForgeMindServerTest` chiama anche strumenti direttamente tramite `ITool` (senza protocollo MCP) per verificare il comportamento.

## 4. Scan e SPK (base delle funzioni offline)

- `ForgeMind.Scan.Abstractions`: astrazioni `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures`, ecc.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore`, ecc.) — analizza file header UE, file di regole e asset Blueprint in un indice di scan. `ScanIndexStore` è un indice in memoria a runtime e non viene scritto su disco. Questo **differisce dal vecchio ScanCache** — la pipeline attuale non usa preload/cache distribuita ScanCache (restano solo tracce storiche di compatibilità nel codice; non costituiscono un meccanismo attuale).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` costruiscono e leggono i pacchetti di scan.
- `ForgeMind.Speedy`: `SpeedyPack` fornisce un formato di impacchettamento ad alte prestazioni (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, con CRC interno, mapping directory, journal pre-write `WriteAheadLog`, ecc.).
- Utilizzo: `ForgeMind.EngineScanner <spkPath>` genera `.spk`. Funzioni come knowledge li consumano tramite `SpkScanDataProvider`.

## 5. Protocollo bridge e adattatore motore

- Bridge: TCP locale. I messaggi usano prefisso di lunghezza 4 byte + envelope JSON (`BridgeMessage` / `BridgeSession` / `FrameCodec`). L'adattatore legge prima `%USERPROFILE%\.forgemind\bridge.json` per la scoperta e l'autenticazione, poi connessione → handshake → ciclo di messaggi.
- Lato server, il routing si basa sul nome del comando verso l'implementazione dell'adattatore. I gestori di comando vengono eseguiti nel thread principale del motore (game thread UE) per garantire coerenza con lo stato dell'editor.
- L'adattatore motore (lato UE) si trova in `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, un modulo Editor contenente il client bridge e l'applicatore IR. Il progetto UE di esempio è compatibile con 5.0–5.8.
- Per aggiungere un nuovo motore, basta implementare un client bridge isomorfo e dei gestori di comando (`development-guide.md` §4). Non sono necessarie modifiche all'host né ai plugin.

## 6. Licenza e sicurezza (parte pubblica)

- Autorizzazione default: `OpenAuthorizationService` (`Authorization\Open\`) — funzioni community fisse, **non analizza i file di licenza**.
- Sicurezza plugin: `PluginIntegrityVerifier` (verifica integrità manifest), `PluginSigningKeys` (verifica firme); `ManifestLoader` analizza i manifest dei plugin e supporta ServiceOverride (sostituzione dei servizi host con tipi astratti, per es. `IAuthorizationService`).
- Autenticazione bridge: le connessioni degli adattatori devono leggere le credenziali locali da `bridge.json` per completare l'handshake (`security.md`).

## 7. Funzioni commerciali (panoramica)

- **Linguaggio DSL e translator di dominio** (codice chiuso): funzioni commerciali che generano asset motore da testo leggibile. La parte pubblica contiene solo il framework DSL (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, interfacce e registri in `ForgeMindServer\DSL\`); la sintassi concreta, i translator di dominio e le regole IR sono forniti dai moduli commerciali e non sono nel repository.
- **Sistema licenze commerciali** (codice chiuso): include emissione licenze, verifica licenze, servizio licenze e binding opzionale all'ambiente. L'implementazione concreta, gli algoritmi di firma, il formato dei file di licenza e la logica lato server non fanno parte della parte pubblica e all'esterno vengono presentati solo come «fornitura di funzioni aggiuntive».

> La versione open source offre una suite completa di strumenti open source e funzioni bridge. La versione Commercial si basa sullo stesso host, meccanismo plugin e protocollo bridge, e estende le funzioni commerciali (generazione asset DSL, licenze commerciali, supporto enterprise).

## 8. Tabella dei limiti di funzioni

| Funzione | Appartenenza |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Interfacce di estensione DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Regole IR | Commercial 🔒 |
| Generatore asset DSL | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Implementazione licenze commerciali | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Sistema chiavi/firme | Commercial 🔒 |
| Dettagli interni protocollo licenze | Commercial 🔒 |
