# Guida ai contributi di ForgeMind

Grazie per il tuo interesse in ForgeMind. Il repository pubblico corrisponde allo slice di rilascio OpenSource di ForgeMind e contiene solo i progetti open source pubblicati, i test e i progetti di esempio. I moduli commerciali (traduttori di dominio DSL, sistema di autorizzazione, ecc.) non fanno parte del repository pubblico: prima di contribuire, chiarisci la divisione degli ambiti.

## 1. Struttura del repository pubblico

Struttura di primo livello dello slice open source (cioè il repository che cloni):

```
ForgeMind.sln                          # Soluzione (contiene riferimenti a progetti commerciali, vedi sotto)
OpenSource\                            # Progetti open source
  ForgeMindServer\                     # Server MCP (host)
  ForgeMind.OfflineEngineKit\          # Plugin strumenti offline del motore (engine/project/launch/build/editor ecc.)
  ForgeMind.Assets\                    # Plugin lettura asset (asset)
  ForgeMind.Knowledge\                 # Plugin ricerca knowledge (knowledge)
  ForgeMind.EngineScanner\             # Scanner motori (riga di comando)
  ForgeMind.Scan\                      # Packaging di scansione
  ForgeMind.Scan.Abstractions\         # Astrazione di scansione
  ForgeMind.Scan.Unreal\               # Implementazione scansione UE (DocumentEngine)
  ForgeMind.Speedy\                    # Formato di packaging SPK
UEProject\
  ForgeMind_5_0\                       # Progetto UE di esempio (compatibile 5.0–5.8)
tests\
  ForgeMind.PublicTests\               # Test unitari pubblici (SPK / dati di scansione)
  ForgeMindServerTest\                 # Test di integrazione del server (richiede editor UE)
```

> Nota: il file di soluzione del repository di sviluppo corrente (`ForgeMind.sln`) contiene riferimenti a progetti Commercial (ad esempio `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`); il prodotto pubblicato **non include la directory `Commercial/`**, quindi gli utenti OpenSource devono compilare singolarmente i progetti pubblici (vedi §3) e non eseguire `dotnet build ForgeMind.sln`.

## 2. Confini dell'OpenSource

- **OpenSource\**: tutto open source (Apache-2.0), è l'area principale dei contributi.
- **UEProject\**: il progetto di esempio è open source, ma all'esportazione dello slice vengono esclusi gli asset binari `*.uasset` (in Content restano solo gli asset testuali come le mappe e la struttura delle directory).
- **tests\**: open source; i contributi devono essere accompagnati da test.
- **Commercial non è nel repository pubblico**: i traduttori di dominio DSL, l'autorizzazione commerciale ecc. non compaiono nello slice; il codice open source non deve fare riferimento a tipi di moduli commerciali.
- All'esportazione vengono escluse automaticamente le directory di build e temporanee come `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`; gli identificativi di licenza nei file vengono sostituiti uniformemente con Apache-2.0 all'esportazione.

## 3. Compilazione

```powershell
# Compila singolarmente (non compilare direttamente l'intera soluzione, vedi sopra)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Progetti plugin (dopo la compilazione, inserire gli artefatti + .manifest.json omonimo nella directory plugins dell'host)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

L'host carica i plugin da `<directory_host>\plugins\` (override con la variabile d'ambiente `FORGEMIND_PLUGINS_DIR`); il pacchetto plugin = DLL principale + `.manifest.json` omonimo + dipendenze (vedi `development-guide.md` §3).

## 4. Convenzioni di codice

- Tutti i file sorgente mantengono in testata l'identificativo SPDX della licenza e la dichiarazione di copyright (attualmente Apache-2.0).
- I namespace sono organizzati per modulo (`ForgeMindServer.*` / `ForgeMind.*`); i contratti pubblici passano da interfacce astratte come `ITool` / `IPlugin`.
- Le classi degli strumenti del server devono fornire `Description` completa e schema dei parametri (questi testi vengono esposti direttamente ai client AI).
- Il codice degli adattatori per motore segue le convenzioni di codifica del motore corrispondente (lato UE attuale: prefissi di tipo F / U / T, macro `TEXT()`, vincoli di sicurezza del game thread).
- Il nuovo codice non deve fare riferimento o dipendere da alcun modulo commerciale.

## 5. Flusso delle PR

1. Creare un branch da `main`, denominazione suggerita `feat/<descrizione>` o `fix/<descrizione>`.
2. Messaggio di commit: `<tipo>: <descrizione in una frase>`.
3. Autoverifica prima del commit:
   - compilare singolarmente secondo §3, tutto deve passare;
   - se la modifica riguarda comandi bridge / comportamento degli strumenti, devono passare i Group rilevanti di `ForgeMindServerTest` (modalità di esecuzione in `development-guide.md` §6);
   - se riguarda SPK / dati di scansione, `ForgeMind.PublicTests` deve passare;
   - nessun riferimento introdotto a moduli commerciali;
   - aggiornare sincronicamente le pagine corrispondenti in `docs\zh-CN\`.
4. All'apertura della PR compilare: contenuto della modifica, riepilogo dei risultati dei test, necessità di revisione di moduli specifici da parte del maintainer.
5. Punti di revisione del maintainer: confini open source, compatibilità API, copertura dei test.

## 6. Requisiti dei test

- Le modifiche a comandi bridge / comportamento degli strumenti / logica degli adattatori devono essere accompagnate da casi di test (aggiungere `*_Tests.cs` sotto `tests\ForgeMindServerTest\Task\`).
- I test di integrazione del server richiedono l'ambiente editor UE; i test di pura elaborazione dati (SPK, scansione) vanno in `tests\ForgeMind.PublicTests\`, senza dipendenze dal motore.
- I casi devono superare stabilmente l'ordine predefinito (fail-fast, fermo al primo errore).

## 7. Contributi alla documentazione

- La documentazione in cinese si trova in `docs\zh-CN\`; il contenuto deve essere coerente con i fatti del codice sorgente; in caso di incertezza, contrassegnare con «da confermare».
- Quando si modifica l'elenco degli strumenti pubblici, aggiornare contestualmente `tools-reference.md`.
- Le funzionalità commerciali (sintassi DSL, meccanismo di autorizzazione) nella documentazione sono solo una panoramica, senza dettagli interni.

## 8. Codice di comportamento

- Rispettare le decisioni di progettazione esistenti (protocollo bridge, contratti degli strumenti, formato SPK); le modifiche importanti vanno prima discusse.
- Non denigrare pubblicamente i prodotti concorrenti; i contenuti promozionali seguono le regole di comunicazione pubblica in `promo.md`.
