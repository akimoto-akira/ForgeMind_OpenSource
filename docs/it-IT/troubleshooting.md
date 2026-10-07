# Risoluzione dei problemi di ForgeMind

Problemi comuni e soluzioni (basati sui meccanismi reali dello slice pubblico).

## 1. Errori di compilazione

**La compilazione diretta di `ForgeMind.sln` segnala «progetto Commercial\... non trovato»**
Causa: `ForgeMind.sln` contiene voci di riferimento a progetti commerciali (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`), ma la directory commerciale non viene pubblicata con lo slice open source.
Soluzione: compilare singolarmente i progetti (contributing.md §3), non l'intera soluzione; oppure usare lo script di compilazione del publisher.

**Dopo la compilazione del plugin, l'host non vede ancora lo strumento**
- Verificare che la DLL del plugin e l'`.manifest.json` omonimo siano stati inseriti in `plugins\<nome-plugin>\` dell'host (o nella directory indicata da `FORGEMIND_PLUGINS_DIR`);
- controllare nei log dell'host se la scansione plugin ha saltato quel pacchetto (`PluginLoader` salta solo il pacchetto in errore senza compromettere l'host);
- riavviare l'host (la tabella degli strumenti viene caricata lazy al primo accesso).

## 2. Problemi di avvio del server

**Porta occupata**
Quando `http://127.0.0.1:8765` è già occupato viene segnalato `HttpListenerException`.
Soluzione: cambiare porta con `--port N` o la variabile d'ambiente `FORGEMIND_PORT`; aggiornare di conseguenza l'indirizzo del client.

**Il client non si connette / tools/list è vuoto**
- Verificare che il server sia in ascolto (log `Listening on http://127.0.0.1:<port>/mcp`);
- verificare che l'URL del client sia `http://127.0.0.1:8765/mcp` (modalità HTTP);
- verificare che il plugin sia stato caricato (vedi sopra).

## 3. Problemi di connessione motore / bridge

**L'editor è aperto, ma gli strumenti segnalano «motore non connesso / bridge non disponibile»**
- Verificare che il plugin `ForgeMindForUE` sia abilitato nel progetto (`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`);
- l'adattatore bridge legge `%USERPROFILE%\.forgemind\bridge.json` per discovery e autenticazione; se il file manca o le credenziali non corrispondono, la connessione fallisce;
- controllare il log dell'editor UE `LogForgeMindBridge`; in caso di disconnessione c'è riconnessione con backoff esponenziale, attendere la riconnessione.

**`launch` / `build` bloccati**
Le attività lunghe vengono eseguite in modo asincrono; usare `get_task_result` per interrogare il risultato, `list_executing_tasks` per vedere le attività in corso; `cleanup_tasks` può ripulire i record.

## 4. Problemi dei test

**La pre-compilazione di `ForgeMindServerTest` fallisce**
`--NoBuild` salta la pre-compilazione (da usare quando gli artefatti sono già garantiti freschi dalla CI); il flusso predefinito compila l'host e fa lo staging dei plugin in `<ingresso>\plugins\`, in caso di errore termina senza eseguire i casi.

**Interruzione fail-fast dei test**
I test di integrazione vengono eseguiti per Group / Order in ordine, fail-fast: fermo al primo errore (i casi must-run di chiusura editor vengono comunque eseguiti nel gruppo). Dopo la correzione, rieseguire il gruppo con `ForgeMindServerTest.exe [group]`.

**I test dipendono dall'editor / dal progetto di esempio**
I casi in `Task\UE5_0\` richiedono l'editor UE e la sessione; i casi di pura elaborazione dati (SPK / scansione) vengono eseguiti in `ForgeMind.PublicTests`, senza dipendenze dall'editor.

## 5. Problemi di dati / asset

**La lettura di `asset` fallisce**
La lettura offline di `.uasset` richiede la corrispondenza della versione del motore (`UAssetVersions`); se la versione non corrisponde, l'analisi può fallire. Verificare la versione del motore e l'ambito di supporto dello strumento.

**Impossibile caricare `.spk`**
Verificare che il file sia stato generato da `ForgeMind.EngineScanner <spkPath>` e non sia danneggiato; SPK include il controllo CRC (`Speedy\Internal\Crc32`), i file corrotti vengono rifiutati.

## 6. Altro

**Come attivare i log**
I log del server sono emessi da `LogManager` (categorie incluse: `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer` ecc.); lato bridge guardare i log di output dell'editor UE.

**Problemi relativi alle funzionalità commerciali**
Per sintassi DSL, autorizzazione e questioni relative alle licenze contattare il publisher; la versione OpenSource non analizza i file di licenza, le capacità sono fisse e aperte (vedi `security.md`).
