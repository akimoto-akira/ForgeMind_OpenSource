# Roadmap di ForgeMind

La direzione è esposta dalla prospettiva pubblica, ordinata per milestone; le tempistiche specifiche fanno fede ai rilasci.

## A breve termine

- **Completamento dello slice open source**: integrare gli script di build e le istruzioni di collegamento in un unico comando, per abbassare il costo di ingresso dei nuovi contributori.
- **Sincronizzazione della documentazione**: mantenere le pagine in `docs\zh-CN\` allineate alle modifiche del codice sorgente, garantendo coerenza con lo slice pubblico.
- **Copertura dei test**: ampliare i casi di `ForgeMind.PublicTests` e `ForgeMindServerTest` (comandi bridge, scansione, scenari limite SPK).

## A medio termine

- **Estensione degli adattatori per motore**: sulla base dell'adattatore UE esistente (5.0–5.8), esplorare l'integrazione di altri motori / editor (protocollo bridge omologo).
- **Potenziamento di scansione e knowledge**: migliorare la copertura di parsing di DocumentEngine su file header / blueprint / file di regole, e ottimizzare efficienza e aggiornamento incrementale del packaging SPK.
- **Ecosistema plugin**: perfezionare l'API dei plugin e le specifiche del manifest, supportando plugin di terze parti più ricchi.

## A lungo termine

- **Apertura della documentazione d'uso DSL**: quando il DSL commerciale sarà stabile, in base alla strategia di prodotto potrà essere pubblicata parte della documentazione d'uso, esempi e contenuti didattici; l'implementazione del DSL, i Translator, le regole IR e la logica di generazione degli asset restano moduli commerciali.
- **Piattaforma**: posizionare ForgeMind come piattaforma di controllo MCP multi-motore, consolidando protocollo e livelli di astrazione indipendenti dal motore.

> La direzione della versione commerciale (sistema di autorizzazione, supporto enterprise, ecc.) non viene approfondita in questa roadmap pubblica: contattare il publisher.
