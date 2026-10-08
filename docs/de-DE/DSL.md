# ForgeMind DSL (Domain-Specific Language)

> Dieses Dokument erklärt, was ForgeMind DSL ist, wie es funktioniert und welche Rolle es im Gesamtsystem spielt. Es richtet sich an Entwickler, die ForgeMind DSL verstehen oder nutzen möchten.

---

## 1. Was ist DSL

**DSL (Domain-Specific Language)** ist ein **deklarativer Kanal** in ForgeMind, über den KI Spielressourcen in Batch erstellt oder ändern kann.

Er ermöglicht es KI, lesbaren Text in einem C#-ähnlichen Syntaxstil zu verwenden, um Ressourcen (wie Blueprints, Materialien, Behavior Trees usw.), die erstellt oder geändert werden sollen, zu beschreibend. Diese werden dann über Übersetzer in eine strukturierte IR (Intermediate Representation) übersetzt und an den Engine-Editor zur Anwendung gesendet.

### 1.1 Kernmodell

Der Schlüssel zum Verständnis von ForgeMind DSL ist die folgende Pipeline:

```
KI
  ↓  gibt DSL-String aus
DSL
  ↓  wird vom Übersetzer übersetzt
IR (Zwischendarstellung)
  ↓  wird über Bridge übertragen
Companion (Engine-Adapter)
  ↓  wird angewendet
Engine Asset
```

Diese Pipeline besteht aus zwei Phasen:

- **Übersetzungsphase** (MCP-Serverseite): DSL-Text wird vom Parser in IR-JSON übersetzt. Der Übersetzer führt lediglich eine Syntaxanalyse durch; er kompiliert nicht, löst keine externen Referenzen auf und greift nicht auf die Festplatte zu. Jeder Übersetzungsfehler beendet den gesamten Batch, und die IR wird nicht an die Engine gesendet.
- **Anwendungsphase** (Engine-Seite): Companion empfängt die IR-JJSON und wendet Erstellen-/Aktualisieren-/Löschen-Operationen assetweise an. Ein Fehler eines Assets wirkt sich nicht auf andere Assets im selben Batch aus.

### 1.2 Beziehung zwischen DSL und MCP Action

ForgeMind's KI-Steuerungsfähigkeiten stammen von zwei Werkzeugtypen:

| Dimension | MCP Action | DSL |
|-----------|-----------|-----|
| Granularität | Präzise Einzelentitäts-Steuerung | Batchweise deklarative Erstellung/Änderung |
| Beispiel | Variable hinzufügen, Leitung verbinden | Ein Textblock, der ein gesamtes Blueprint-Asset erstellt |
| Kanal | Direkt über Bridge an Engine senden | Erst in IR übersetzt, dann über Bridge senden |
| Rolle | Feinabstimmung | Übergeordnete Architektur |

Die beiden sind **komplementär** — DSL übernimmt die Batch-Erstellung "von Grund auf", während Action die präzise Optimierung "von gut auf hervorragend" übernimmt.

---

## 2. Wo DSL im System platziert ist

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  KI-Client    │ ◄────► │  ForgeMindServer (Host)   │
└──────────────┘         │  • Empfängt DSL-String     │
                         │  • Ruft Übersetzer auf,    │
                         │    um IR zu erstellen      │
                         │  • Sendet IR über Bridge   │
                         └────────────┬─────────────┘
                                      │ lokale TCP-Brücke
                         ┌────────────▼─────────────┐
                         │  Companion (Engine-Adapter)│
                         │  • Empfängt IR            │
                         │  • Wendet assetweise an   │
                         │  • Kompiliert / Speichert   │
                         └──────────────────────────┘
```

DSLs Platz in ForgeMind:
- KI gibt DSL-Deklarationstext aus
- Das MCP-`dsl`-Tool empfängt den Text und ruft den Übersetzer auf, um IR-JSON zu erstellen.
- IR-JשON wird über TCP-Bridge an den Live-Engine-Editor gesendet.
- Companion (Engine-Adapter) wendet es im Editor an und schließt die Ressourcenerstellung ab.

---

## 3. DSL-Grundsyntax

DSL entleiht die syntaktische Form von C#, durchläuft jedoch **nicht den C#-Compiler**. Es imitiert C#-Schreibkonventionen, führt jedoch compilerinterne semantische Analyse (Typprüfung, Überladungsauflösung usw.) im Übersetzer selbst durch.

### 3.1 Deklarationsblock-Struktur

Jeder DSL-Deklarationsblock ist eine Top-Level-Klassen- (bzw. struct/enum-) Deklaration mit einem `[Attribut]`:

```csharp
[DomainAttr(Path = "/Game/Pfad/Ressourcenname", Mode = Create)]
[WeitereAttribute]
public class Ressourcenname
{
    // Member-Deklarationen: Variablen, Komponenten, Funktionen usw.
}
```

### 3.2 Create- / Update- / Delete-Modi

Alle Domänen unterstützen einheitlich drei Betriebsmodi:

| Moduswert | Bedeutung | Nutzlast |
|-----------|-----------|----------|
| `Create` | Neue Ressource erstellen | `Definition` (vollständige Ressourcendefinition) |
| `Update` | Bestehende Ressource ändern | `Patches` (memberbezogene Zielzustands-Array) |
| `Delete` | Gesamte Ressource löschen | leere `patches` |

**Create-Beispiel:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Update-Beispiel:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // nicht im Asset → hinzufügen
    [Variable(Category = "Stats")]
    float Health = 90f;                    // bereits im Asset → ersetzen
    [Delete]                              // zum Löschen markiert
    float LegacyVariable;                  // → bestehendes Member löschen
}
```

Der eigentliche Operationstyp (add / replace / delete) wird von Companion abgeleitet, basierend darauf, ob der Member in der Ressource vorhanden ist und ob `[Delete]` annotiert ist — nicht explizit in DSL deklariert.

**Delete-Beispiel:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Andere Ressourcen referenzieren — using-Aliase

DSL unterstützt die `using`-Alias-Syntax, um Querverweise auf Assets zu vereinfachen:

```csharp
// Datei-Level using (gilt für alle Blöcke)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// Block-Level using (gilt nur für diesen Block)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BPFass_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

Dies ist eine DSL-Syntaxkonvention — `using` bildet Bezeichner auf Ressourcenpfad-Strings ab, um die Referenzierung in Deklarationen zu vereinfachen.

### 3.4 Blockübergreifende Typreferenzen

Zusätzlich zu explizitem `using` unterstützt DSL auch implizite blockübergreifende Typreferenzen:

Wenn eine Top-Level-Deklaration `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }` ist, kann der Bezeichner `BP_Hunter` in nachfolgenden Blöcken direkt verwendet werden. Er wird automatisch zum Ressourcenpfad `"/Game/AI/BP_Hunter"` aufgelöst.

### 3.5 Kommentare

DSL unterstützt C#-ähnliche Kommentare:
- `//` Einzeilige Kommentare
- `/* */` Blockkommentare

Kommentare erzeugen keine IR-Ausgabe.

---

## 4. IR (Zwischendarstellung)

IR ist das Zwischenprodukt zwischen dem Übersetzer und Companion — eine JSON-formatierte strukturierte Ressourcenbeschreibung. Es ist die „Brachensprache" zwischen DSL und der Engine.

### 4.1 Batch-IR vs. Einzel-Asset-IR

**Batch-IR** (Multi-Asset-Batch):
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

**Einzel-Asset-IR**:
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

### 4.2 Einzel-Asset-IR Feldreferenz

| Feld | Typ | Beschreibung |
|------|-----|--------------|
| `version` | string | Engine-Version (z. B. `"5.6"`), muss exakt zur Engine-Hauptversion passen |
| `assetType` | string | Domänentyp (durch die Attribute des Blocks bestimmt) |
| `path` | string | Ressourcenpfad (erforderlich) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Ob nach der Anwendung kompiliert werden soll (Standard true) |
| `save` | bool | Ob nach der Anwendung auf Festplatte gespeichert werden soll |
| `patches` | array | Erforderlich für Update-Modus (memberbezogener Zielzustand, kein op-Feld) |
| `definition` | object | Erforderlich für Create-Modus (vollständige Ressourcendefinition) |

> **Modus- / Nutzlast-Einschränkung**: `mode=create` muss `definition` enthalten und darf keine `patches` enthalten; `mode=update` muss `patches` enthalten und darf keine `definition` enthalten; `mode=delete` enthält leere `patches`. Inkonsistente Kombinationen sind ungültig.

### 4.3 Bidirektionale Symmetrie

ForgeMind IR verwendet **dieselbe Payload-Struktur** sowohl in der „Schreibrichtung" (`apply_asset_ir`) als auch in der „Leserichtung" (`get_asset_ir`):

| Richtung | Befehl | `mode`-Wert |
|----------|--------|-------------|
| Schreiben | `apply_asset_ir` | `create` / `update` / `delete` |
| Lesen | `get_asset_ir` | `read` |

Dies ermöglicht der KI, dieselbe IR ohne Schema-Konvertierung zu „lesen → mode ändern → zurückzuschreiben".

---

## 5. Domänenerkennung und -routing

### 5.1 Attribute bestimmen die Domäne

Jeder Deklarationsblock in einem DSL-String wird automatisch über seinen `[Attribut]`-Marker an seinen Asset-Typ geroutet:

```csharp
[Blueprint(Path = "...")]          // → Blueprint-Übersetzer → assetType = "blueprint"
[Material(Path = "...")]           // → Material-Übersetzer  → assetType = "material"
[BehaviorTree(Path = "...")]       // → Behavior-Tree-Übersetzer → assetType = "behavior_tree"
```

**Attribut → assetType-Zuordnungstabelle:**

| Attribut | Asset-Typ |
|----------|-----------|
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

> Die obige Tabelle verzeichnet alle bekannten Domänen. Zum Hinzufügen einer neuen Domäne muss lediglich eine neue Attribut → Übersetzer-Zuordnung auf der Übersetzungsseite registriert werden — das Host-Framework bedarf keiner Änderungen.

### 5.2 Routing-Regeln

- Der Parser führt genaue Namensabgleich für jeden Deklarationsblock durch (auf Token-Ebene, nicht Teilstring-Abgleich)
- Bei mehreren Attributen gewinnt der erste Treffer in der Zuordnungstabelle (`[WidgetBlueprint]` hat Vorrang vor `[Blueprint]`)
- **Ein nicht erkanntes Attribut fällt nicht stillschweigend zurück** — es meldet einen Fehler und beendet den gesamten Batch

### 5.3 Mehrere Domänen in einem DSL mischen

Ein einziger DSL-String kann **eine beliebige Anzahl von Deklarationsblöcken beliebiger Domänen** enthalten:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Block 1: Blueprint-Domäne ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Block 2: Behavior-Tree-Domäne ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Block 3: Material-Domäne ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Alle Deklarationsblöcke im selben DSL werden unabhängig übersetzt, jedes erzeugt eine eigene IR, die in einer zusammengesetzten Batch-IR zusammengefasst wird und über die Bridge zusammen an die Engine gesendet wird.

---

## 6. Bridge und Companion

### 6.1 Bridge-Transport

IR wird über eine lokale TCP-Brücke an den Engine-Editor gesendet:
- Die Brücken-Schicht ist für die zuverlässige Zustellung der IR an die Engine-Seite verantwortlich.
- Bridge ist ForgeMind's lokale Prozesskommunikationsinfrastruktur: Die Host-Seite betreibt den Brückenserver, die Engine-Adapter-Seite betreibt den Brückenclient.

### 6.2 Zuständigkeiten von Companion

Companion ist das Adapter-Plugin im Inneren der Engine (z. B. ForgeMindForUE). Seine Zuständigkeiten umfassen:

1. **IR-JSON empfangen** → gemäß `assetType` an den Engine-Domain-Handler dispatchen
2. **Create**: Aus `Definition` die vollständige Ressource erstellen → in der Ressourcenregustrierung anmelden → optional kompilieren → optional auf Festplatte speichern
3. **Update**: Memberbezogene Änderungen aus `Patches` anwenden (add / replace / delete durch Companion abgeleitet)
4. **Delete**: Das Ressourcenpaket entladen → aus der Ressourcenregustrierung entfernen → Festplattendateien löschen
5. **Ergebnisberichterstattung**: Pro Asset success / error zurückgeben; Schreibrückrichtung gibt `patchResults[].actualOp` zurück.

> Ein Fehler eines Assets hat keine Auswirkung auf andere Assets im selben Batch (kein Transaktions-Rollback). Dies ist eine bewusste Designentscheidung in ForgeMind — der Zustandsraum von Editor-Ressourcen ist groß, die Transaktionskosten sind hoch, und ein Teilerfolg ist dem Alles-oder-Nichts-Prinzip vorzuziehen.

---

## 7. Diagnose- und Fehlerbehandlung

Die DSL-Fehlerbehandlung besteht aus zwei Phasen:

| Phase | Fehlerarten | Behandlung |
|-------|------------|------------|
| Übersetzungsphase (MCP-Seite) | Syntaxfehler, nicht erkannte Attribute, fehlende erforderliche Parameter usw. | Jeder Fehler → Batch wird beendet, IR wird nicht an Bridge gesendet |
| Anwendungsphase (Companion-Seite) | Ressource nicht gefunden, Typenübereinstimmung nicht vorhanden, Kompilierungsfehler usw. | Einzelfehler wirken sich nicht auf andere Ressourcen im Batch aus |

Warnungen (Warn) während der Übersetzungsphase beenden den Batch nicht, werden jedoch als Antworttext an die KI zurückgegeben.

### 7.1 Häufige Fehlercodes der Übersetzungsphase

Einige Beispiel-Fehlercodes (weitere Details siehe Domänendokumentation):

| Fehlercode | Bedeutung |
|------------|-----------|
| `FM1004` | Ungültiger Mode-Wert |
| `FM1005` | Fehlender Pflichtparameter Path |
| `FM1002` | Domänenübersetzungsfehler (generischer Fallback-Code) |

### 7.2 Primäre Asset-Level-Fehlercodes (Anwendungsphase)

| Fehlercode | Bedeutung |
|------------|-----------|
| `ASSET_NOT_FOUND` | Ressourcenpfad existiert nicht (update / delete-Szenarien) |
| `ASSET_ALREADY_EXISTS` | Pfad existiert bereits (create-Standardmäßig keine Überschreibung) |
| `ASSET_TYPE_MISMATCH` | Typ stimmt nicht überein |
| `ASSET_LOCKED` | Ressource ist im Editor gesperrt |

---

## 8. Erweitern von DSL-Domänen

### 8.1 Komponenten einer Domäne

Eine vollständige DSL-Domäne besteht aus folgenden Komponenten:

1. **Attribut-Markierungen** — wie `[Blueprint]`, `[Material]`, werden zur Routingerkennung verwendet.
2. **Domänenübersetzer-Implementierung** — verantwortlich für die Übersetzung der DSL-Deklarationen dieser Domäne in IR-JSON
3. **DSL-Syntaxdokumentation** — definiert die Deklarationsregeln und Attributparameter der Domäne
4. **IR-Schema-Konvention** — definiert die JSON-Struktur der `definition` / `patches` der Domäne
5. **Companion-Domänenprozessor** — die entsprechende Implementierung auf der Engine-Seite, die Ressourcen aus IR im Editor anwendet

### 8.2 Grundlegende Schritte zum Hinzufügen einer neuen Domäne

1. Definieren Sie den Stamm-Attributnamen der Domäne (z. B. `[NewDomain]`)
2. Implementieren Sie den Domänenübersetzer (vom Übersetzer-Basis erben, Syntaxbaum-Besuchermethoden überschreiben, um Domänensemantik zu extrahieren, IR-JSON ausgeben)
3. Bei der Registrierung beim Framework, damit der Parser den Übersetzer über `assetType` finden kann
4. DSL-Syntaxdokumentation und IR-Schema-Konventionen schreiben
5. Den entsprechenden Domänenprozessor auf der Engine-Seite implementieren

Das Hinzufügen einer neuen Domäne **erfordert keine Änderungen am Host-Code** — Das Framework entdeckt neue Domänenübersetzer über den Registrierungsmechanismus.

### 8.3 Umsetzungsprinzipien für Übersetzer

Übersetzer halten sich an folgende Vertragsregeln, die zum Erweitern von DSL-Domänen befolgt werden sollten:

- **Reine Funktion**: Der Übersetzer erhält einen DSL-String als Eingabe und gibt IR-JSON aus, ohne Seiteneffekte und ohne bleibende Zustandsinformationen. Dieselbe DSL erzeugt stets dieselbe IR.
- **Nur Syntaxanalyse**: Übersetzer führen keine C#-Kompilierung aus, greifen nicht auf die Festplatte zu und lösen keine externe Assets auf; sie übersetzen lediglich die syntaktische Form von DSL in IR. Semantische Validierung liegt in der Verantwortung der Engine-Seite.
- **Fehlerwiederherstellung**: Kann nicht-standardisierte DSL-Syntax tolerieren (z. B. String-Zuweisung an `using`-Aliase), ohne die Übersetzung zu unterbrechen.
- **Unbekannte Syntax-Abwertung**: Wenn Syntax, die nicht genau übersetzt werden kann, wird eine Warnung anstatt eines Fehlers gemeldet, und es wird der Anwendungsphase der Engine überlassen.

---

## 9. Open-Source- und kommerzielle Grenze

ForgeMind's DSL-Fähigkeiten sind in zwei Teile gegliedert:

### 9.1 Open-Source (Öffentlicher Kern)

Der Open-Source-Kern stellt das DSL-Host-Framework bereit:

- **DSL-Framework-Ebene**: Verantwortlich für das Parsen von DSL-Strings (Aufteilung nach Top-Level-Deklarationsblöcken, Erkennen von Attributen und Routen zu Übersetzern), Übersetzer-Schnittstellen und Registrierungsmechanismen, Diagnoseberichterstattung und sonstige Infrastruktur.
- **Plugin-Vertrag und Registrierungsmechanismus**: Neue Domänenübersetzer werden über registrierung entdeckt; das Host-Framework benötigt keine domänenspezifische Verzweigungslogik.
- **Open-Source enthält keine Domänenübersetzer**: Die Domänen-spezifische Übersetzungslogik (Blueprints, Materialien, Behavior Trees usw.) ist nicht Teil des Open-Source-Kerns.

### 9.2 Kommerziell (Bezahlte Erweiterung)

Das kommerzielle Erweiterungsmodul baut auf dem Open-Source-Framework auf und bietet:

- Alle spezifischen Domänenübersetzer (Blueprint, Material, Behavior Tree, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Landscape, Foliage, Physics, Paper2D, Sequenzer, …)
- Den MCP-`dsl`-Tool-Einstiegspunkt als Ausführungspunkt für DSL-Übersetzung und -Versand
- Domänen-spezifische IR-Konverter, die domänenspezifische Verarbeitung durchführen, bevor IR an die Engine gesendet wird

> Der Open-Source-Kern definiert das Framework und den Erweiterungsmechanismus; spezifische Domänenfähigkeiten (DSL-Syntax und Übersetzer für Blueprints, Materialien, Behavior Trees usw.) werden vom kommerziellen Erweiterungsmodul bereitgestellt. Open-Source-Nutzer können benutzerdefinierte Domänenübersetzer registrieren, basierend auf dem Framework, um ihre eigenen DSL-Fähigkeiten zu erweitern.

---

## 10. Verhaltensbeschränkungen und Designprinzipien

| Tag | DSL-Verhalten | Bedeutung |
|-----|-------------|---------|
| Syntaxstil | Inspiriert von C# | C# ist die Sprache mit den meisten KI-Schulungsdaten, reduziert kognitive Belastung |
| Node-Koordinaten | Nicht in DSL ausdrücken | Behandelt nur „was existiert und wie sie verbunden sind" — Topologie-Semantik; Layout wird von der Engine automatisch generiert |
| Pfad erforderlich | Ja | Namenskonvenzen und Modulzugehörigkeit werden explizit von der KI entschieden |
| Versionsabgleich | Exakte Hauptversion | IR `version` muss zur Engine-Hauptversion passen |
| Transaktionsrichtlinie | Kein Rollback | Ein Teilfehler eines Batches wirkt sich nicht auf andere Ressourcen aus |
| Dochtlauf | Existiert nicht | IR wird nur an die Engine gesendet und nicht an den Aufrufer zurückgemeldet |
| Nicht erkanntes Attribut | Beendet den Batch | Stille Rückführung würde dazu führen, dass KI unwissentlich den falschen Ressourcentyp erstellt |
| Update Patch | Drückt den Zielzustand aus | Patch hat kein `op`-Feld; Operationen werden von Companion basierend auf der Memberexistenz abgeleitet |

---

## 11. Schnellreferenz

### DSL-Deklarationsblock-Vorlage

```csharp
using SomeAsset = "/Game/Pfad/SomeAsset.uasset";

[DomainAttr(Path = "/Game/Pfad/DeinAsset", Mode = Create)]
[WeiteresAttribut]
public class DeinAsset
{
    using BlockAlias = "/Game/Pfad/AnderesAsset.uasset";
    [Variable] float DeineVariable = 1f;
    [Function(Pure = true)]
    bool DeineFunktion() => DeineVariable > 0;
}
```

### Einzel-Asset-IR-Vorlage

```json
{
  "version": "<Major>.<Minor>",
  "assetType": "<snake_case_type>",
  "path": "/Game/Pfad/Asset",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { }
}
```

### Pipeline-Übersicht

```
KI-Client
  → DSL (Text mit einem oder mehreren Domänendeklarationsblöcken)
  → MCP dsl (Übersetzungsphase)
    → Parser routet zum Domänenübersetzer basierend auf Attribut
    → Übersetzer erzeugen IR-JSON
  → Batch-IR ({ version, batch, assets })
  → Bridge (lokales TCP)
  → Companion (Anwendungsphase)
    → Erstellen / Aktualisieren / Löschen / Kompilieren / Speichern der Ressource
  → Engine-Ressource
```
