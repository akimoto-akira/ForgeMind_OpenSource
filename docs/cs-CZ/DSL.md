# ForgeMind DSL (Domain-Specific Language)

> Tento dokument vysvětluje, co je ForgeMind DSL, jak funguje a jakou roli hraje v celém systému. Je určen pro vývojáří, kteří chtějí porozumět nebo používat ForgeMind DSL.

---

## 1. Co je DSL

**DSL (Domain-Specific Language)** je **deklarativní kódový kanál** ve ForgeMind pro AI k hromadnému vytváření nebo úpravě herních aktiv engine.

Umožňuje AI používat čitelný text napsaný v syntaktickém stylu podobném C# k popisu aktiv, která mají být vytvořena nebo upravena (jako jsou Blueprints, Materiály, Behavior Trees atd.), pak je převést do strukturované IR (Intermediate Representation) pomocí překladačů a odeslat do editoru engine k aplikaci.

### 1.1 Základní model

Klíčem k pochopení ForgeMind DSL je následující pipeline:

```
AI
  ↓  vydá řetězec DSL
DSL
  ↓  překlad překladačem
IR (Mezilehlá reprezentace)
  ↓  přenos přes Bridge
Companion (adaptér engine)
  ↓  aplikace
Aktivum engine
```

Tento pipeline je rozdělen do dvou fází:

- **Překladová fáze** (strana serveru MCP): Text DSL je převeden do IR JSON analyzeem. Překladač pouze provádí syntaktickou analýzu; nekompiluje, neřeší vnější reference a nepřistupuje na disk. Jakákoliv chyba překladu ukončí celou dávku a IR nebude odeslána do engine.
- **Aplikační fáze** (strana engine): Companion přijme IR JSON a aplikuje operace vytvoření/aktualizace/smazání aktivum po aktivu. Selhání jednoho aktiva neovlivní ostatní aktiva ve stejné dávce.

### 2.2 Vztah mezi DSL a MCP Action

Schopnosti AI řízení ve ForgeMind pocházejí ze dvou typů nástrojů:

| Rozměr | MCP Action | DSL |
|--------|-----------|-----|
| Granularita | Přesné řízení jedné entity | Hromadné deklarativní vytvoření/úprava |
| Přiklad | Přidat proměnnou, připojit drát | Blok textu, který vytvoří celé aktivum Blueprint |
| Kanál | Odesláno přímo do engine přes Bridge | Nejprve přeloženo do IR, pak odesláno přes Bridge |
| Role | Jemné ladění | Celková architektura |

Obě jsou **doplňkové** — DSL se stará o hromadnou konstrukci „od nuly", zatímco Action se stará o přesné ladění „dobré k vynikající".

---

## 2. Kde DSL Zapadá do Systému

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  Klient AI    │ ◄────► │  ForgeMindServer (host)   │
└──────────────┘         │  • Přijímá řetězec DSL     │
                         │  • Vyvolá přeoběl k výrobě │
                         │    IR                      │
                         │  • OdešleIR přes Bridge    │
                         └────────────┬─────────────┘
                                      │ lokální TCP most
                         ┌────────────▼─────────────┐
                         │  Companion (adapt. enginu)│
                         │  • Přijímá IR            │
                         │  • Aplikuje na aktivum   │
                         │  • Konpiluje / Ukládá      │
                         └──────────────────────────┘
```

Pozice DSL ve ForgeMind:
- AI vydá deklarační text DSL
- Nástroj MCP `dsl` přijme text a vyvolá překladač k výrobě IR JSON
- IR JSON je odesláno do editoru živého engine přes TCP Bridge
- Companion (adaptér engine) to aplikuje v editoru k dokončení generace aktiv

---

## 3. Základní Syntaktický Styl DSL

DSP si půjčuje syntaktickou formu C#, ale **neprochází C# kompilátorem**. Napodobuje konvence psaní C#, ale spravuje vnitřní sémantickou analýzu kompilátoru kontrola typu, řešení přetížení atd. v samotném překladačí.

### 3.1 Struktura Bloků Deklarace

Každý blok deklarace DSL je deklarace třídy nejvyššího úrovne (nebo struct/enum) s `[Atribut]`:

```csharp
[DomainAttr(Path = "/Game/Path/ResourceName", Mode = Create)]
[AdditionalAttributes]
public class ResourceName
{
    // deklarace členů: proměnné, komponenty, funkce atd.
}
```

### 3.2 Režimy Create / Update / Delete

Všechny domény jednotně podporují tři provozní režimy:

| Hodnota režimu | Význam | Užitečné zatížení |
|----------------|--------|-------------------|
| `Create` | Vytvořit nové aktivum | `definition` (kompletní definice aktiva) |
| `Update` | Upravit existující aktivum | `patches` (pole cílového stavu podle členů) |
| `Delete` | Smazat celé aktivum | prázdné `patches` |

**Příklad Create:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Příklad Update:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // není v aktivu → přidej
    [Variable(Category = "Stats")]
    float Health = 90f;                    // již je v aktivu → nahraď
    [Delete]                              // označit ke smazání
    float LegacyVariable;                  // → smazat existujícího člena
}
```

Skutečný typ operace (add / replace / delete) je odvozen Companionem na základě existence člena v cílovém aktivu a přítomnosti anotace `[Delete]` — nikoliv deklarováno explicitně v DSP.

**Příklad Delete:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Odkazování na Další Aktiva — using alias

DSP podporuje syntaxi aliasů `using` pro zjednodušení křížových odkazů:

```csharp
// using na úrovni souboru (platí pro všechny bloky)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// using na úrovni bloku (platí pouze pro tento blok)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

Toto je syntaktická konvence DSL — `using` mapuje identifikátory na řetězce cesty aktivu pro snadné odkazy v deklaracích.

### 3.4 Odkazy Typů Mezi Bloky

Kromě explicitního `using` také DSP podporuje implicitní odkazy typů mezi bloky:

Pokud je deklarace nejvyššího úrovně `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, identifikátor `BP_Hunter` může být použit přímo v následujících blocích a automaticky se přeloží do cesty aktiva `"/Game/AI/BP_Hunter"`.

### 3.5 Komentáře

DSP podporuje komentáře ve stylu C#:
- `//` jednořádkové komentáře
- `/* */` blokové komentáře

Komentáře nevytvářejí žádný výstup IR.

---

## 4. IR (Zprostředkující Reprezentace)

IR je mezilehlý překladatelský artefakt mezi překladačem a Companionem — strukturovaný popis aktiva ve formátu JSON. Je to "mostový jazyk" mezi DSP a enginem.

### 4.1 Dávkové IR vs. IR Jednoho Aktiva

**Dávkové IR** (více aktiv):
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

**IR Jednoho Aktiva:**
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

### 4.2 Tabulka polí IR jednoho aktiva

| Pole | Typ | Popis |
|------|-----|-------|
| `version` | string | Verze engine (např. `"5.6"`), musí přesně odpovídat hlavní verzi engine |
| `assetType` | string | Typ domény (blokem Atributu určeném) |
| `path` | string | Cesta k aktivu (vyžadováno) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Zda kompilovat po aplikaci (výchozí true) |
| `save` | bool | Zda uložit na disk po aplikaci |
| `patches` | array | Vyžadováno pro režim update (cílový stav člena, bez pole op) |
| `definition` | object | Vyžadováno pro režim create (kompletní definice aktiva) |

> **Omezení režimu/užitečného zatížení**: `mode=create` musí nést `definition` a nesmí obsahovat `patches`; `mode=update` musí nést `patches` a nesmí obsahovat `definition`; `mode=delete` nese prázdné `patches`. Jakákoliv nekonzistence je neplatné užitečné zatížení.

### 4.3 Obousměrná Symetrie

IR ForgeMind používá **stejnou strukturu užitečného zatížení** jak ve "směru zápisu" (`apply_asset_ir`), tak ve "směru čtení" (`get_asset_ir`):

| Směr | Příkaz | Hodnota `mode` |
|------|--------|-------------|
| Zápis | `apply_asset_ir` | `create` / `update` / `delete` |
| Čtení | `get_asset_ir` | `read` |

To umožňuje AI „číst → upravit mode → zpět zapsat" bez převodu schématu.

---

## 5. Rozpoznání a Směrování Domény

### 5.1 Atributy Určují Domény

Každý blok deklarace v řetězci DSL je automaticky směrován ke svému typu aktiva pomocí značky `[Atribut]`:

```csharp
[Blueprint(Path = "...")]          // → překladač Blueprint → assetType = "blueprint"
[Material(Path = "...")]           // → překladač Materiálůu  → assetType = "material"
[BehaviorTree(Path = "...")]       // → překladač Behavior Tree → assetType = "behavior_tree"
```

**Mapovací tabulka Atributů → assetType:**

| Atribut | Typ Aktiva |
|---------|------------|
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

> Výše uvedená tabulka mapuje všechny aktuálně známé domény. Pro přidání nové domény je pouze potřeba registrovat nového mapování Atributů → překladač na straně převodu — hostitelský rámec nevyžaduje žádné změny.

### 5.2 Pravidla Směrování

- Analyzátor provádí přesné porovnání jména pro každý blok deklarace (na úrovni tokenu, nikoliv podřetězec)
- Pokud je přítomno více Atributů, první v mapovací tabulce vítězí (`[WidgetBlueprint]` má přednost před `[Blueprint]`)
- **Nerozpoznaný Atribut se tišě nevrátí zpět** — hlásí chybu a ukončí celou dávku

### 5.3 Míchání Něikaet Domén v Jednom DSP

Jednotlivý řetězec DSP může obsahovat **libovolný počet deklaračních bloků libovolné domény**:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Blok 1: doména Blueprint ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Blok 2: doména Behavior Tree ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Blok 3: doména Materiálů ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Všechny deklarační bloky ve stejném DSP jsou překládány nezávisle, každý vytváří svou vlastní IR, smontované do jedné dávkové IR odeslané do engine společně přes Bridge.

---

## 6. Bridge a Companion

### 6.1 Přenos Bridge

IR je odeslána do editoru engine přes lokální TCP most:
- Vrstva mostu je zodpovědná za spolehlivé doručení IR na stranu enginu
- Bridge je lokální infrastrukturou meziprocesové komunikace ForgeMind: strana hostitele udržuje server mostu, strana adaptéru enginu udržuje klienta mostu

### 6.2 Povinnosti Companion

Companion je plugin adaptér uvnitř enginu (např. ForgeMindForUE). Jeho povinnosti zahrnují:

1. **Přijmout IR JSON** → odeslat do obsluhy domény enginu na základě `assetType`
2. **Create**: Vytvořit kompletní aktivum z `definition` → zaregistrovat v registru aktiv → volitelně kompilovat → volitelně uložit na disk
3. **Update**: Použít změny na úrovni člena z `patches` (add / replace / delete odvozeno Companionem)
4. **Delete**: Vyložit balíček aktiva → odstranit z registru aktiv → smazat soubory na disku
5. **Hodnocení výsledků**: Vrátit úspěch / chybu na aktivum; směr zápisu vrací `patchResults[].actualOp`

> Selhání jednoho aktiva neovlivní ostatní aktiva ve stejné dávce (bez transakčního vrácení). Toto je záměrná volba designu ve ForgeMind — stavový prostor aktiv editoru je velký, ceny transakcí jsou vysoké a částečný úspěch je vhodnější než všechno nebo nic.

---

## 7. Diagnostika a Zpracování Chyb

Zpracování chyb DSP je rozděleno do dvou fází:

| Fáze | Typy Chyb | Zpracování |
|------|----------|----------|
| Překladová fáze (strana MCP) | Syntaktické chyby, nerozpoznané Atributy, chybějící vyžadované parametry | Jakákoliv chyba → celá dávka ukončena, IR není odesláno do Bridge |
| Aplikační fáze (strana Companion) | Aktivum nenalezeno, typ nesoulad, chyba kompilace | Selhání jednotlivého aktiva neovlivní ostatní aktiva |

Varování (Warn) během překladové fáze neukončí dávku, ale budou připojeny k textu odpovědi vrácenému AI.

### 7.1 Obvyklé Kódy Chyb Překladové Fáze

Několik příkladů kódů chyb (podrobnosti viz dokumentace domény):

| Kód Chyby | Význam |
|----------|--------|
| `FM1004` | Neplatná hodnota Mode |
| `FM1005` | Chybí vyžadovaný parametr Path |
| `FM1002` | Selhání překladu domény (obecný záložní kód) |

### 7.2 Hlavní Kódy Chyb na Úrovni Aktiva (Aplikační Fáze)

| Kód Chyby | Význam |
|----------|--------|
| `ASSET_NOT_FOUND` | Cesta aktiva neexistuje (scénáře update / delete) |
| `ASSET_ALREADY_EXISTS` | Cesta již existuje (scénář create, bez přepsání ve výchozím nastavení) |
| `ASSET_TYPE_MISMATCH` | Typ nesoulad |
| `ASSET_LOCKED` | Aktivum je v editoru uzamčeno |

---

## 8. Rozšíření Domén DSP

### 8.1 Součásti Domény

Kompletní doména DSP se skládá z následujících součástí:

1. **Značka Atributu** — např. `[Blueprint]`, `[Material]`, používá se k rozpoznání směrování
2. **Obvedomění Překladače Domény** — odpovědné za překlad deklarací DSP této domény do IR JSON
3. **Dokumentace Syntaxe DSP** — definuje pravidla deklarace a parametry Atributů pro doménu
4. **Dohoda o Schématu IR** — definuje strukturu JSON `definition` / `patches` domény
5. **Dohlížitel Domény Companion** — odpovídající obvedomění na straně enginu, která aplikuje aktivum z IR v editoru

### 8.2 Základní Kroky pro Přidání Nové Domény

1. Definovat kořenový název Atributu domény (např. `[NewDomain]`)
2. Implementovat překladač domény (zdědit základní třídu překladače, přepsat metody návštěvníka syntaktického stromu pro extrakci sémantiky domény, vyrobit IR JSON)
3. Zaregistrovat se v rámci, aby analyzátor mohl najít překladač skrze `assetType`
4. Napsat dokumentaci syntaxe DSP a dohody o schématu IR
5. Implementovat odpovídající dohlížitel domény na straně Companion enginu

Přidání nové domény **nevyžaduje žádné změny v hostitelském kódu** — rámec objeví nové překladače domény prostřednictvím mechanismu registrace.

### 8.3 Principy Implementace Překladačů

Překladače následují těmto smluvním pravidlům chování, která by měla být dodržena při rozšiřování domén DSP:

- **Čistá Funkce**: Překladač přijímá řetězec DSP jako vstup a vydává IR JSON jako výstup, bez vedlejších efektů a bez zbytkového stavu. Stejný DSP vždy produkuje stejný IR.
- **Pouze Syntaktická Analýza**: Překladače neprovádějí kompilaci C#, nepřistupují na disk, neřeší vnější aktiva; pouze překládají syntaktickou formu DSP do IR. Sémantická validace je odpovědností strany enginu.
- **Obnova po Chybách**: Může tolerovat nesyntaktickou syntaxi DSP (např. přiřazení řetězce k aliasům `using`) bez přerušení překladu.
- **Degradace Neznámé Syntaxe**: Při setkání se syntaxí, kterou nelze přesně přeložit, nahlásí varování namísto chyby a nechá to na aplikační fázi enginu.

---

## 9. Open Source a Komerční Hranice

Schopnosti DSP ForgeMind jsou rozděleny do dvou částí:

### 9.1 Open Source (Veřejné Jádro)

Otevřené jádro poskytuje rámec hostitele DSP:

- **Vrstva Rámce DSP**: Odpovídá za analýzu řetězců DSP (rozdělení podle deklarace nejvyššího úrovně, rozpoznání Atributů a směrování k překladačům), rozhraní překladače a mechanismy registrace, diagnostické reporty a další infrastrukturu.
- **Smlouva Pluginu a Registrační Mechanismus**: Nové překladače domény jsou objeveny registrací; hostitelský rámec nepotřebuje vloženou logiku větvení specifickou pro doménu.
- **Open Source vylučuje Překladače Domény**: Specifická logika překladu domény (Blueprinty, Materiály, Stromy chování atd.) není součástí otevřeného jádra.

### 9.2 Komerční (Platené Rozšíření)

Modul komerčního rozšíření staví na otevřeném rámci a poskytuje:

- Veškeré implementace specifických překladačů domén (Blueprint, Materiál, Strom chování, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Krajina, Fólie, Fyzika, Paper2D, Sequencer, …)
- Vstupní bod nástroje MCP `sloužící jako bod spuštění pro překlad a distribuci DSP
- Převodní IR podle domény, které provádějí doménově specifické zpracování, než je IR odesláno do enginu

> Otevřené jádro definuje rámec a mechanismus rozšíření; konkrétní schopnosti domény (syntaxe a překladače DSP pro Blueprinty, Materiály, Stromy chování atd.) jsou poskytovány modulem komerčního rozšíření. Uživatele otevřeného jádra mohou registrovat vlastní překladače domény na základě rámce, aby rozšířili své vlastní schopnosti DSP.

---

## 10. Chování Omezení a Principy Designu

| Značka | Chování DSP | Význam |
|--------|----------|--------|
| Syntaktický Styl | Inspirací C# | C# je jazyk s nejvíce tréninkovými daty AI, snižuje kognitivní zátěž |
| Souřadice Uzlu | Nevyjádřeno v DSP | Zaměřuje se pouze na "co existuje a jak jsou propojeny" — topologická sémantika; rozložení je vygenerováno automaticky enginem |
| Vyžadována Cesta | Ano | Jmenné konvence a příslučnost k modulu jsou explicitně rozhodovány AI |
| Odpovídající Verze | Přesná hlavní verze | `verze` IR musí odpovídat hlavní verzi enginu |
| Transakční Politika | Bez vracení | Částečné selhání aktiva v dávce neovlivní ostatní actives |
| Zkušební Spuštění | Neexistuje | IR je odeslána pouze do enginu, nikoliv vrácena volajícímu |
| Nerozpoznaný Atribut | Ukončí dávku | Tiché navrácení by způsobilo, že AI nevědomky vytvoří nesprávný typ aktiva |
| Update patch | Vyjadřuje cílový stav | Patch nenese pole `op`; operace jsou odvozeny Companionem na základě existence člena |

---

## 11. Rychlý Referenční

### Šablona Bloku deklarace DSP

```csharp
using SomeAsset = "/Game/Path/SomeAsset.uasset";

[DomainAttr(Path = "/Game/Path/YourAsset", Mode = Create)]
[AdditionalAttribute]
public class YourAsset
{
    using BlockAlias = "/Game/Path/AnotherAsset.uasset";
    [Variable] float YourVariable = 1f;
    [Function(Pure = true)]
    bool YourFunction() => YourVariable > 0;
}
```

### Šablona IR jednoho aktiva

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

### Přehled Pipeline

```
Klient AI
  → DSP (text obsahující jeden nebo více deklaračních bloků domény)
  → Nástroj MCP dsl (fáze překladu)
    → analyzátor směruje k překladači domény na základě Atributu
    → překladače produkují IR JSON
  → Dávkové IR ({ version, batch, assets })
  → Bridge (lokální TCP)
  → Companion (fáze aplikace)
    → Vytvořit / Aktualizovat / Smazat / Kompilovat / Ukládat na aktivum
  → Aktivum enginu
```
