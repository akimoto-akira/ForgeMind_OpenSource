# Sicurezza di ForgeMind

Questo documento descrive il design di sicurezza e i suggerimenti d'uso dello slice pubblico.

## 1. Confini della comunicazione locale

- **Il server ascolta per impostazione predefinita solo su loopback**: in modalità HTTP ascolta su `127.0.0.1:8765`, non è esposto alla rete esterna.
- **Il bridge è TCP locale**: tra host e adattatore del motore è una connessione TCP locale; l'adattatore deve leggere `%USERPROFILE%\.forgemind\bridge.json` (host, porta, credenziali) per completare discovery e autenticazione prima dell'handshake.
- Non esporre la porta del server sulla rete pubblica; per l'uso remoto, collocarlo in una rete fidata e inoltrare tramite un canale sicuro (ad esempio tunnel SSH).

## 2. Integrità dei plugin

- Il `PluginLoader` dell'host esegue la **verifica di integrità del manifest** (`PluginIntegrityVerifier`) e la verifica della firma (`PluginSigningKeys`) al caricamento dei plugin: il pacchetto deve contenere la DLL principale + l'`.manifest.json` omonimo; se mancante o non corrispondente, viene rifiutato / saltato.
- L'iniezione degli strumenti dei plugin avviene nel processo interno dell'host; installare solo plugin da fonti attendibili (compilati dallo slice pubblico o da release ufficiali).

## 3. Modello di autorizzazione

**Meccanismi di sicurezza divulgabili**:
- `IAuthorizationService`: astrazione del punto di ingresso dell'autorizzazione dell'host; l'implementazione predefinita è `OpenAuthorizationService` (capacità fisse della community, non analizza alcun file di licenza e non genera autorizzazioni di prova).
- ServiceOverride attendibile: tramite ServiceOverride nel manifest, un plugin può sostituire i servizi dell'host in base al tipo astratto (l'host non è a conoscenza del concetto «commerciale», sostituisce solo per tipo).
- Autenticazione dell'identità dei plugin e verifica del manifest: `PluginIntegrityVerifier` / `PluginSigningKeys` verificano manifest e firma dei plugin, garantendo il caricamento dei soli plugin attendibili.
- Autenticazione della connessione bridge: l'adattatore completa discovery e handshake delle credenziali tramite `bridge.json`, confermando l'affidabilità dell'origine della connessione.

**Autorizzazione commerciale**: Core fornisce l'astrazione `IAuthorizationService` e il meccanismo di estensione ServiceOverride attendibile. L'implementazione dell'autorizzazione commerciale è un plugin closed source; protocollo di autorizzazione specifico, emissione, verifica e logica di binding non fanno parte dello slice pubblico.

## 4. Permessi degli strumenti

- Gli strumenti di operazione sul motore sono vincolati da `ServerPermissionChecker` (all'interno dei plugin) e verificano l'autorizzazione delle capacità prima dell'esecuzione.
- Un `Execute` fallito degli strumenti restituisce uniformemente il messaggio di errore di `ToolResult`, senza esporre dettagli interni dello stack.

## 5. Suggerimenti d'uso

- Aprire la porta MCP solo a client attendibili; i client dispongono di capacità di operazione sul motore, equivalenti ai permessi di amministrazione locale.
- Mantenere aggiornati plugin, host e progetto di esempio per ricevere tempestivamente le correzioni di sicurezza.
- Non copiare le credenziali di `bridge.json` a processi non attendibili su macchine condivise.
- In caso di problemi di sicurezza, segnalarli tramite i canali del publisher, evitando di divulgare pubblicamente i dettagli delle vulnerabilità non corrette.
