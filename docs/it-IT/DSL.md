# ForgeMind DSL (Domain-Specific Language)

> Questo documento spiega cos'è ForgeMind DSL, come funziona e quale ruolo svolge nel sistema generale. È rivolto agli sviluppatori che vogliono comprendere o utilizzare ForgeMind DSL.

---

## 1. Cos'è DSL

**DSL (Domain-Specific Language)** è un **canale di codice dichiarativo** in ForgeMind per l'IA per creare o modificare in batch le risorse del motore di gioco.

Consente all'IA di utilizzare testo leggibile scritto in uno stile sintattico simile al C# per descrivere le risorse da creare o modificare (come Blueprint, Materiali, Behaviour Tree, ecc.), quindi tradurli in IR (Intermediate Representation) strutturata tramite traduttori e inviarli all'editor del motore per l'applicazione.

### 1.1 Modello Fondamentale

La chiave per comprendere ForgeMind DSL è il seguente flusso:

```
IA
  ↓  emette stringa DSL
DSL
  ↓  tradotto dal traduttore
IR (Rappresentazione Intermedia)
  ↓  trasmesso via Bridge
Companion (adattatore motore)
  ↓  applicato
Risorsa motore
```

Questo flusso è diviso in due fasi:

- **Fase di Traduzione** (lato server MCP): Il testo DSL viene tradotto in IR JSON dal parser. Il traduttore esegue solo analisi sintattica; non compila, non risolve riferimenti esterni, non accede al disco. Qualsiasi errore di traduzione termina l'intero batch, e l'IR non viene inviata al motore.
- **Fase di Applicazione** (lato motore): Companion riceve l'IR JSON e applica operazioni di creazione/aggiornamento/eliminazione risorsa per risorsa. Un errore su una risorsa non influisce sulle altre risorse nello stesso batch.

### 1.2 Relazione tra DSL e MCP Action

Le capacità di controllo IA di ForgeMind provengono da due tipi di strumenti:

| Dimensione | MCP Action | DSL |
|-----------|-----------|-----|
| Granularità | Controllo preciso di singola entità | Creazione/modificazione dichiarativa in batch |
| Esempio | Aggiungere una variabile, collegare un filo | Un blocco di testo che crea una risorsa Blueprint intera |
| Canale | Inviato direttamente al motore via Bridge | Tradotto prima in IR, poi inviato via Bridge |
| Ruolo | Regolazioni fini | Impostazione architettura generale |

I due sono **complementari** — IL DSL gestisce la costruzione in batch "da zero", mentre Action gestisce la regolazione precisa "da buono a eccellente".

---

## 2. Dove DSL si Posiziona nel Sistema

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  Client IA    │ ◄────► │  ForgeMindServer (host)   │
└──────────────┘         │  • Riceve la stringa DSL    │
                         │  • Invoca il traduttore     │
                         │    per produrre IR          │
                         │  • Invia IR via Bridge      │
                         └────────────┬─────────────┘
                                      │ ponte TCP locale
                         ┌────────────▼─────────────┐
                         │  Companion (adatt. motore) │
                         │  • Riceve IR              │
                         │  • Applica per risorsa    │
                         │  • Compila / Salva         │
                         └──────────────────────────┘
```

Posizione del DSL in ForgeMind:
- L'IA emette testo di dichiarazione DSL
- Lo strumento MCP `dsl` riceve il testo e invoca il traduttore per produrre IR JSON
- L'IR JSON viene inviato all'editor del motore live tramite TCP Bridge
- Companion (adattatore motore) lo applica nell'editor per completare la generazione delle risorse

---

## 3. Stile Sintattico Base del DSL

DSL prende in prestito la forma sintattica del C#, ma **non passa attraverso il compilatore C#**. Imita le convenzioni di scrittura del C#, ma gestisce l'analisi semantica interna del compilatore (controllo dei tipi, risoluzione dell'overload, ecc.) all'interno del traduttore stesso.

### 3.1 Struttura del Blocco di Dichiarazione

Ogni blocco di dichiarazione DSL è una dichiarazione di classe di livello superiore (o struct/enum) con un `[Attribute]`:

```csharp
[DomainAttr(Path = "/Game/Percorso/NomeRisorsa", Mode = Create)]
[AttributiAggiuntivi]
public class NomeRisorsa
{
    // dichiarazioni di membri: variabili, componenti, funzioni, ecc.
}
```

### 3.2 Modalità Create / Update / Delete

Tutti i domini supportano uniformemente tre modalità operative:

| Valore Modalità | Significato | Payload |
|-----------------|-------------|---------|
| `Create` | Crea una nuova risorsa | `definition` (definizione completa della risorsa) |
| `Update` | Modifica una risorsa esistente | `patches` (array di stato target per membro) |
| `Delete` | Elimina l'intera risorsa | `patches` vuoto |

**Esempio Create:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Esempio Update:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // non nella risorsa → aggiungi
    [Variable(Category = "Stats")]
    float Health = 90f;                    // già nella risorsa → sostituisci
    [Delete]                              // segna per eliminazione
    float LegacyVariable;                  // → elimina membro esistente
}
```

Il tipo di operazione effettivo (add / replace / delete) viene dedotto da Companion in base all'esistenza del membro nella risorsa target e alla presenza dell'annotazione `[Delete]` — non dichiarato esplicitamente in DSL.

**Esempio Delete:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Fare Riferimento ad Altre Risorse — alias using

DSL supporta la sintassi di alias `using` per semplici riferimenti tra risorse:

```csharp
// using a livello di file (si applica a tutti i blocchi)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// using a livello di blocco (si applica solo a quel blocco)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

Questa è una convenzione sintattica DSL — `using` mappa identificatori a stringhe di percorso risorsa per facilitare i riferimenti nelle dichiarazioni.

### 3.4 Riferimenti di Tipo Tra Blocchi

Oltre all'`using` esplicito, DSL supporta anche riferimenti di tipo impliciti tra blocchi:

Se una dichiarazione di livello superiore è `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, l'identificatore `BP_Hunter` può essere usato direttamente nei blocchi successivi, e si risolverà automaticamente nel percorso risorsa `"/Game/AI/BP_Hunter"`.

### 3.5 Commenti

DSL supporta commenti in stile C#:
- `//` commenti su una singola riga
- `/* */` commenti di blocco

I commenti non producono output IR.

---

## 4. IR (Rappresentazione Intermedia)

IR è l'artefatto di traduzione tra il traduttore e Companion — una descrizione di risorsa strutturata in formato JSON. È il "linguaggio ponte" tra DSL e il motore.

### 4.1 Batch IR vs. IR a Risorsa Singola

**Batch IR** (batch multi-risorsa):
```json
{
  "version": "5.6",
  "batch": true,
  "assets": [
    { "assetType": "blueprint", "path": "/Game/BP_A", "mode": "create", ... },
    { "assetType": "material",  "path": "/Game/M_Wood", "mode": "create", ... }
  ]
}
```

**IR a Risorsa Singola**:
```json
{
  "version": "5.6",
  "assetType": "blueprint",
  "path": "/Game/Blueprints/BP_Chest",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { ... }
}
```

### 4.2 Riferimento Campi IR a Risorsa Singola

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `version` | string | Versione del motore (ad es. `"5.6"`), deve corrispondere esattamente alla versione maggiore |
| `assetType` | string | Tipo di dominio (determinato dall'Attribute del blocco) |
| `path` | string | Percorso risorsa (obbligatorio) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Se compilare dopo l'applicazione (default true) |
| `save` | bool | Se salvare su disco dopo l'applicazione |
| `patches` | array | Obbligatorio per modalità update (stato target per membro, senza campo op) |
| `definition` | object | Obbligatorio per modalità create (definizione completa della risorsa) |

> **Vincolo Modalità/Payload**: `mode=create` deve portare `definition` e non deve includere `patches`; `mode=update` deve portare `patches` e non deve includere `definition`; `mode=delete` porta `patches` vuoto. Qualsiasi inconsistenza è un payload non valido.

### 4.3 Simmetria Bidirezionale

L'IR di ForgeMind utilizza **la stessa struttura di payload** sia nella "direzione scrittura" (`apply_asset_ir`) che nella "direzione lettura" (`get_asset_ir`):

| Direzione | Comando | Valore `mode` |
|-----------|---------|-------------|
| Scrittura | `apply_asset_ir` | `create` / `update` / `delete` |
| Lettura | `get_asset_ir` | `read` |

Questo consente all'IA di "leggere → modificare mode → riscrivere" la stessa IR senza conversione di schema.

---

## 5. Riconoscimento e Instradamento del Dominio

### 5.1 Gli Attribute Determinano il Dominio

Ogni blocco di dichiarazione in una stringa DSL viene instradato automaticamente al suo tipo di risorsa tramite il suo marker `[Attribute]`:

```csharp
[Blueprint(Path = "...")]          // → traduttore Blueprint → assetType = "blueprint"
[Material(Path = "...")]           // → traduttore Materiale  → assetType = "material"
[BehaviorTree(Path = "...")]       // → traduttore Behaviour Tree → assetType = "behavior_tree"
```

**Tabella di Mapping Attribute → assetType:**

| Attribute | Tipo Risorsa |
|-----------|--------------|
| `[Blueprint]` | `blueprint` |
| `[WidgetBlueprint]` | `widget_blueprint` |
| `[Material]` | `material` / `material_instance_constant` |
| `[MaterialFunction]` | `material_function` |
| `[BehaviorTree]` | `behavior_tree` |
| `[Blackboard]` | `blackboard` |
| `[AnimBlueprint]` | `anim_blueprint` |
| `[Enum]` | `user_defined_enum` |
| `[Struct]` | `user_defined_struct` |
| `[InputAction]` | `input_action` |
| `[InputMappingContext]` | `input_mapping_context` |
| `[NiagaraEmitter]` | `niagara_emitter` |
| `[NiagaraSystem]` | `niagara_system` |
| `[MetaSoundSource]` | `meta_sound_source` |
| `[ChaosSolver]` | `chaos_solver_actor` |
| `[GroomBinding]` | `groom_binding_asset` |
| `[Landscape]` | `landscape` |
| `[FoliageType]` | `foliage_type` |
| `[PhysicsAsset]` | `physics_asset` |
| `[PaperFlipbook]` | `paper_flipbook` |
| `[LevelSequence]` | `level_sequence` |
| … | … |

> La tabella sopra mappa tutti i domini attualmente noti. Aggiungere un nuovo dominio richiede solo la registrazione di un nuovo mapping Attribute → traduttore sul lato traduzione — il framework host non ha bisogno di modifiche.

### 5.2 Regole di Instradamento

- Il parser esegue un esatto nome di corrispondenza per ogni blocco di dichiarazione (a livello di token, non corrispondenza di sottostringa)
- Quando sono presenti più Attribute, il primo trovato nella tabella di mapping vince (`[WidgetBlueprint]` ha la priorità su `[Blueprint]`)
- **Un Attribute non riconosciuto non torna automaticamente alla normalità** — riporta un errore e termina l'intero batch

### 5.3 Mescolare Più Domini in un DSL

Una singola stringa DSL può contenere **un numero qualsiasi di blocchi di dichiarazione di qualsiasi dominio**:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Blocco 1: dominio Blueprint ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Blocco 2: dominio Behaviour Tree ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Blocco 3: dominio Materiale ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Tutti i blocchi di dichiarazione nello stesso DSL vengono tradotti indipendentemente, ciascuno produce la propria IR, assemblati in un unico Batch IR inviato al motore tramite il Bridge.

---

## 6. Bridge e Companion

### 6.1 Trasporto Bridge

L'IR viene inviato all'editor del motore tramite un ponte TCP locale:
- Il layer di ponte è responsabile della consegna affidabile dell'IR al lato motore
- Bridge è l'infrastruttura di comunicazione inter-processo locale di ForgeMind: il lato host mantiene il server ponte, il lato adattatore del motore mantiene il client ponte

### 6.2 Responsabilità di Companion

Companion è il plugin adattatore all'interno del motore (ad es. ForgeMindForUE). Le sue responsabilità includono:

1. **Ricevere IR JSON** → instradare al gestore del dominio del motore basato su `assetType`
2. **Create**: Costruire la risorsa completa da `definition` → registrare nel registro risorse → compilare opzionalmente → salvare opzionalmente su disco
3. **Update**: Applicare modifiche a livello di membro da `patches` (add / replace / delete dedotto da Companion)
4. **Delete**: Scaricare il pacchetto risorsa → rimuovere dal registro risorse → eliminare i file disco
5. **Report sui Risultati**: Restituire successo / errore per risorsa; la direzione scrittura restituisce `patchResults[].actualOp`

> Una fallimento su una risorsa non influisce sulle altre risorse nello stesso batch (nessun rollback dell'operazione). Questa è una scelta progettuale deliberata in ForgeMind — lo spazio di stato delle risorse dell'editor è grande, il costo dell'operazione è alto, e il successo parziale è preferibile al tutto o niente.

---

## 7. Diagnosi e Gestione degli Errori

La gestione degli errori DSL è divisa in due fasi:

| Fase | Tipi di Errore | Gestione |
|------|---------------|----------|
| Fase di traduzione (lato MCP) | Errori sintattici, Attribute non riconosciuti, parametri obbligatori mancanti | Qualsiasi Errore → batch terminato, IR non inviato al Bridge |
| Fase di applicazione (lato Companion) | Risorsa non trovata, tipo non compatibile, errore di compilazione | Errore su singola risorsa non influisce sulle altre risorse |

Gli avvertimenti (Warn) durante la fase di traduzione non terminano il batch ma vengono allegati al testo di risposto restituito all'IA.

### 7.1 Codici Errore Comuni nel Translation Phase

Alcuni codici errore di esempio (fare riferimento alla documentazione del dominio per i dettagli):

| Codice Errore | Significato |
|---------------|-------------|
| `FM1004` | Valore Mode non valido |
| `FM1005` | Parametro obbligatorio Path mancante |
| `FM1002` | Traduzione del dominio fallita (codice di fallback generico) |

### 7.2 Codici Errore a Livello Risorsa (Application Phase)

| Codice Errore | Significato |
|---------------|-------------|
| `ASSET_NOT_FOUND` | Percorso risorsa inesistente (scenari update / delete) |
| `ASSET_ALREADY_EXISTS` | Percorso già esistente (scencreate, senza sovrascrittura predefinita) |
| `ASSET_TYPE_MISMATCH` | Tipo non compatibile |
| `ASSET_LOCKED` | Risorsa bloccata nell'editor |

---

## 8. Estensione dei Domini DSL

### 8.1 Componenti di un Dominio

Un dominio DSL completo è composto dai seguenti componenti:

1. **Contrassegno Attribute** — ad es. `[Blueprint]`, `[Material]`, usato per il riconoscimento dell'instradamento
2. **Implementazione del Traduttore di Dominio** – responsabile della traduzione delle dichiarazioni DSL di quel dominio in IR JSON
3. **Documentazione della Sintassi DSL** — definisce le regole di dichiarazione e i parametri Attribute per il dominio
4. **Convenzione dello Schema IR** – definisce la struttura JSON del `definition` / `patches` del dominio
5. **Gestore del Dominio Companion** – l'implementazione corrispondente sul lato del motore che applica le risorse da IR nell'editor

### 8.2 Passaggi di Base per Aggiungere un Nuovo Dominio

1. Definire il nome Attribute radice del dominio (ad es. `[NewDomain]`)
2. Implementare il traduttore di dominio (ereditare la classe base del traduttore, sovrascrivere i metodi di visita dell'albero sintattico per estrarre la semantica del dominio, produrre IR JSON)
3. Registrarsi al framework in modo che il parser possa trovare il tramite `assetType`
4. Scrivere la documentazione della sintassi DSL e le convenzioni dello schema IR
5. Implementare il gestore del dominio corrispondente sul lato Companion del motore

Aggiungere un nuovo dominio **non richiede alcuna modifica al codice host** — il framework scopre i nuovi traduttori del dominio tramite il meccanismo di registrazione.

### 8.3 Principi di Implementazione dei Traduttori

I traduttori seguono questi contratti di comportamento che devono essere seguiti quando si estendono i domini DSL:

- **Funzione Pure**: Il traduttore prende una stringa DSL come input e IR JSON come output, senza effetti collaterali e senza stato residuo. Lo stesso DSL produce sempre la stessa IR.
- **Solo Analisi Sintattica**: I traduttori non eseguono la compilazione C#, non accedono al disco, non risolvono risorse esterne; traducono solo la forma sintattica del DSL in IR. La validazione semantica è responsabilità del lato motore.
- **Ripristino Errori**: Può tollerare sintassi DSL non standard (come l'assegnazione di stringa agli alias `using`) senza interrompere la traduzione.
- **Degrado della Sintassi Sconosciuta**: Quando trova sintassi che non può tradurre con precisione, riporta un avvertimento al posto di un errore, lasciandolo alla fase di applicazione del motore.

---

## 9. Confine tra Open Source e Commerciale

Le capacità DSL di ForgeMind sono divise in due parti:

### 9.1 Open Source (Nucleo Pubblico)

Il nucleo open source fornisce il framework host DSL:

- **Livello Framework DSL**: Responsabile dell'analisi delle stringhe DSL (suddivisione in blocchi di dichiarazione di livello superiore, riconoscimento degli Attribute e instradamento ai traduttori), interfacce dei traduttori e meccanismi di registrazione, report di diagnosi e altre infrastrutture.
- **Contratto Plugin e Meccanismo di Registrazione**: I nuovi traduttori di dominio vengono scoperti tramite registrazione; il framework host non ha bisogno di logica di ramifica


---

## 10. Vincoli di Comportamento e Principi di Design

| Etichetta | Comportamento DSL | Significato |
|-----------|-------------------|-------------|
| Stile Sintattico | Ispirato al C# | C# è la lingua con maggiori dati di addestramento IA, riducendo il carico cognitivo |
| Coordinate Nodo | Non espresso in DSL | Si concentra solo su "cosa esiste e come sono collegati" – semantica topologica; il layout è generato automaticamente dall'engine |
| Path Obbligatorio | Sì | Le convenzioni di denominazione e l'appartenenza al modulo sono decise esplicitamente dall'IA |
| Corrispondenza Versione | Esatta versione maggiore | La `version` IR deve corrispondere alla versione maggiore dell'engine |
| Politica Transazioni | Nessun rollback | Un errore parziale di risorsa in un lotto non influenza altre risorse |
| Dry Run | Non esiste | L'IR viene solo inviata all'engine, non restituita al chiamante |
| Attribute non riconosciuto | Termina il lotto | Il fallback silenzioso farebbe sì che l'IA creasse inadvertitamente il tipo di risorsa errato |
| Update patch | Esprime lo stato target | La patch non porta il campo `op`; le operazioni sono inferite da Companion in base all'esistenza del membro |

---

## 11. Riferimento Rapido

### Modello di Blocco di Dichiarazione DSL

```csharp
using SomeAsset = "/Game/Path/SomeAsset.uasset";

[DomainAttr(Path = "/Game/Path/YourAsset", Create = true)]
[AdditionalAttribute]
public class YourAsset
{
    using BlockAlias = "/Game/Path/AnotherAsset.uasset";
    [Variable] float YourVariable = 1f;
    [Function(Pure = true)]
    bool YourFunction() => YourVariable > 0;
}
```

### Modello di IR a Risorsa Singola

```json
{
  "version": "<major.minor>",
  "assetType": "<snake_case_type>",
  "path": "/Game/Path/Asset",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { }
}
```

### Panoramica del Flusso

```
Client IA
  → DSL (testo con uno o più blocchi di dichiarazione di dominio)
  → Strumento MCP dsl (fase di traduzione)
    → parser instrada al traduttore di dominio in base all'Attribute
    → traduttori producono IR JSON
  → Batch IR ({ version, batch, assets })
  → Bridge (locale TCP)
  → Commenter (fase di applicazione)
    → Crea / Aggiorna / Elimina / Compila / Salva per risorsa
  → Risorsa Engine
```
