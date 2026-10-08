# ForgeMind DSL (Domain-Specific Language)

> Ce document explique ce qu'est ForgeMind DSL, comment il fonctionne et quel est son rôle dans le système global. Il s'adresse aux développeurs souhaitant comprendre ou utiliser ForgeMind DSL.

---

## 1. Qu'est-ce que le DSL

**DSL (Domain-Specific Language)** est un **canal de code déclaratif** dans ForgeMind permettant à l'IA de créer ou modifier en lot des assets de moteurs de jeu.

Il permet à l'IA d'utiliser un texte lisible dans un style syntaxique proche du C# pour décrire les assets à créer ou modifier (tels que Blueprints, Matériaux, arbres de comportement, etc.), puis de les traduire en IR (Intermediate Representation) structurée via des traducteurs, et de les envoyer à l'éditeur du moteur pour application.

### 1.1 Modèle fondamental

La clé pour comprendre ForgeMind DSL est le pipeline suivant :

```
IA
  ↓  produit une chaîne DSL
DSL
  ↓  traduit par le traducteur
IR (Représentation Intermédiaire)
  ↓  transmis via Bridge
Companion (adaptateur moteur)
  ↓  appliqué
Asset moteur
```

Ce pipeline se divise en deux phases :

- **Phase de traduction** (côté serveur MCP) : Le texte DSL est traduit en IR JSON par le parseur. Le traducteur ne fait que de l'analyse syntaxique ; il ne compile pas, ne résout pas les références externes et n'accède pas au disque. Toute erreur de traduction termine l'ensemble du lot, et l'IR n'est pas envoyé au moteur.
- **Phase d'application** (côté moteur) : Companion reçoit l'IR JSON et applique les opérations de création/modification/suppression actif par actif. Une défaillance sur un actif n'affecte pas les autres actifs du même lot.

### 1.2 Relation entre DSL et MCP Action

Les capacités de contrôle IA de ForgeMind proviennent de deux types d'outils :

| Dimension | MCP Action | DSL |
|-----------|-----------|-----|
| Granularité | Contrôle précis d'une seule entité | Création/modification déclarative par lot |
| Exemple | Ajouter une variable, connecter un fil | Un bloc de texte qui crée un actif Blueprint entier |
| Canal | Envoyé directement au moteur via Bridge | Traduit d'abord en IR, puis envoyé via Bridge |
| Rôle | Ajustements fins | Structuration d'architecture globale |

Les deux sont **complémentaires** — le DSL gère la création par lot « depuis zéro », tandis que l'Action gère l'ajustement précis « de bon à excellent ».

---

## 2. Position du DSL dans le système

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  Client IA    │ ◄────► │  ForgeMindServer (hôte)   │
└──────────────┘         │  • Reçoit la chaîne DSL     │
                         │  • Invoque le traducteur    │
                         │    pour produire l'IR       │
                         │  • Envoie l'IR via Bridge   │
                         └────────────┬─────────────┘
                                      │ pont TCP local
                         ┌────────────▼─────────────┐
                         │  Companion (adapt moteur) │
                         │  • Reçoit l'IR            │
                         │  • Applique par actif     │
                         │  • Compile / Sauvegarde    │
                         └──────────────────────────┘
```

Position du DSL dans ForgeMind :
- L'IA produit le texte de déclaration DSL
- L'outil MCP `dsl` reçoit le texte et invoque le traducteur pour produire l'IR JSON
- L'IR JSON est envoyé à l'éditeur du moteur en ligne via TCP Bridge
- Companion (adaptateur moteur) l'applique dans l'éditeur pour terminer la génération d'actifs

---

## 3. Style syntaxique de base du DSL

DSL emprunte la forme syntaxique du C#, mais **ne passe pas par le compilateur C#**. Il imite les conventions d'écriture du C#, mais prend en charge l'analyse sémantique interne du compilateur (vérification de type, résolution de surcharge, etc.) dans le traducteur lui-même.

### 3.1 Structure de bloc de déclaration

Chaque bloc de déclaration DSL est une déclaration de classe de niveau supérieur (ou struct/enum) portant un `[Attribute]` :

```csharp
[DomainAttr(Path = "/Game/Chemin/NomActif", Mode = Create)]
[AttributsSupplementaires]
public class NomActif
{
    // déclarations de membres : variables, composants, fonctions, etc.
}
```

### 3.2 Modes Create / Update / Delete

Tous les domaines supportent uniformément trois modes d'opération :

| Valeur du mode | Signification | Charge utile |
|----------------|---------------|--------------|
| `Create` | Créer un nouvel actif | `definition` (définition complète de l'actif) |
| `Update` | Modifier un actif existant | `patches` (tableau d'état cible par membre) |
| `Delete` | Supprimer l'actif entier | `patches` vide |

**Exemple de création :**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Exemple de mise à jour :**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // absent de l'actif → ajout
    [Variable(Category = "Stats")]
    float Health = 90f;                    // déjà présent → remplacement
    [Delete]                              // marquer pour suppression
    float LegacyVariable;                  // → supprimer le membre existant
}
```

Le type d'opération réel (ajouter / remplacer / supprimer) est déduit par Companion en fonction de l'existence du membre dans l'actif cible et de la présence de l'annotation `[Delete]` — et non déclaré explicitement dans le DSL.

**Exemple de suppression :**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Référencer d'autres actifs — alias using

DSL supporte la syntaxe d'alias `using` pour simplifier les références inter-actifs :

```csharp
// using au niveau du fichier (s'applique à tous les blocs)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// using au niveau du bloc (s'applique uniquement à ce bloc)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

C'est une convention syntaxique du DSL — `using` mappe les identifiants vers des chaînes de chemin d'actif pour faciliter les références dans les déclarations.

### 3.4 Références de type inter-blocs

En plus du `using` explicite, DSL supporte aussi les références de type inter-blocs implicites :

Si une déclaration de niveau supérieur est `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, l'identificateur `BP_Hunter` peut être utilisé directement dans les blocs suivants ; il se résoudra automatiquement en chemin d'actif `"/Game/AI/BP_Hunter"`.

### 3.5 Commentaires

DSL supporte les commentaires de style C# :
- `//` commentaires sur une seule ligne
- `/* */` commentaires de bloc

Les commentaires ne produisent aucune sortie IR.

---

## 4. IR (Représentation Intermédiaire)

IR est l'artéfact de traduction entre le traducteur et Companion — une description d'actif structurée au format JSON. C'est la « langue pont » entre DSL et le moteur.

### 4.1 IR par lot vs. IR par actif

**IR par lot** (lot multi-actifs) :
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

**IR par actif** :
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

### 4.2 Référence des champs de l'IR par actif

| Champ | Type | Description |
|-------|------|-------------|
| `version` | string | Version du moteur (par exemple, `"5.6"`), doit correspondre exactement à la version majeure |
| `assetType` | string | Type de domaine (déterminé par l'Attribute du bloc) |
| `path` | string | Chemin d'actif (obligatoire) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Compiler après application (par défaut true) |
| `save` | bool | Sauvegarder sur disque après application |
| `patches` | array | Obligatoire pour le mode mise à jour (état cible par membre, sans champ op) |
| `definition` | object | Obligatoire pour le mode création (définition complète de l'actif) |

> **Contrainte Mode/Charge** : `mode=create` doit contenir `definition` et ne doit pas contenir `patches` ; `mode=update` doit contenir `patches` et ne doit pas contenir `definition` ; `mode=delete` contient un `patches` vide. Toute incohérence est une charge utile invalide.

### 4.3 Symétrie bidirectionnelle

L'IR de ForgeMind utilise **la même structure de charge utile** à la fois dans la « direction écriture » (`apply_asset_ir`) et la « direction lecture » (`get_asset_ir`) :

| Direction | Commande | Valeur `mode` |
|-----------|---------|-------------|
| Écriture | `apply_asset_ir` | `create` / `update` / `delete` |
| Lecture | `get_asset_ir` | `read` |

Cela permet à l'IA de « lire → modifier le mode → réécrire » sans conversion de schéma.

---

## 5. Reconnaissance et routage de domaine

### 5.1 Les Attributes déterminent les domaines

Chaque bloc de déclaration dans une chaîne DSL est automatiquement routé vers son type d'actif via son marqueur `[Attribute]` :

```csharp
[Blueprint(Path = "...")]          // → traducteur Blueprint → assetType = "blueprint"
[Material(Path = "...")]           // → traducteur Matériau  → assetType = "material"
[BehaviorTree(Path = "...")]       // → traducteur Arbre de comportement → assetType = "behavior_tree"
```

**Tableau de correspondance Attribute → assetType :**

| Attribute | Type d'actif |
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

> Le tableau ci-dessus recense tous les domaines actuellement connus. Pour ajouter un nouveau domaine, il suffit d'enregistrer une nouvelle correspondance Attribute → traducteur côté traduction — le framework hôte n'a pas besoin de changement.

### 5.2 Règles de routage

- Le parseur effectue une correspondance de nom exacte pour chaque bloc de déclaration (au niveau du jeton, pas de correspondance de sous-chaîne)
- En présence de plusieurs Attributes, le premier trouvé dans le tableau de correspondance gagne (`[WidgetBlueprint]` a priorité sur `[Blueprint]`)
- **Un Attribute non reconnu ne revient pas en arrière silencieusement** — il signale une erreur et termine l'ensemble du lot

### 5.3 Mélanger plusieurs domaines dans un seul DSL

Une seule chaîne DSL peut contenir **n'importe quel nombre de blocs de déclaration de n'importe quel domaine** :

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Bloc 1 : domaine Blueprint ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Bloc 2 : domaine Arbre de comportement ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Bloc 3 : domaine Matériau ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Tous les blocs de déclaration dans le même DSL sont traduits indépendamment, chacun produisant son propre IR, assemblés dans un seul IR par lot envoyé au moteur via le pont.

---

## 6. Bridge et Companion

### 6.1 Transport Bridge

L'IR est envoyé à l'éditeur du moteur via un pont TCP local :
- La couche de pont est responsable de la livraison fiable de l'IR côté moteur
- Bridge est l'infrastructure de communication inter-processus locale de ForgeMind : le côté hôte maintient le serveur pont, le côté adaptateur moteur maintient le client pont

### 6.2 Responsabilités de Companion

Companion est le plugin d'adaptateur au sein du moteur (par exemple, ForgeMindForUE). Ses responsabilités incluent :

1. **Réception de l'IR JSON** → dispatch vers le gestionnaire de domaine moteur basé sur `assetType`
2. **Create** : Construire l'actif complet à partir de `definition` → enregistrer dans le registre d'actifs → compiler éventuellement → sauvegarder éventuellement sur disque
3. **Update** : Appliquer les modifications par membre à partir de `patches` (ajouter / remplacer / supprimer déduit par Companion)
4. **Delete** : Décharger le package d'actifs → supprimer du registre d'actifs → supprimer les fichiers sur disque
5. **Rapport de résultats** : Retourner succès / erreur par actif ; la direction écriture retourne `patchResults[].actualOp`

> Une défaillance sur un actif n'affecte pas les autres actifs du même lot (pas de rollback de transaction). C'est un choix de conception délibéré dans ForgeMind — l'espace d'état des actifs éditeur est grand, le coût de transaction est élevé, et le succès partiel est préférable à tout ou rien.

---

## 7. Diagnostics et gestion des erreurs

La gestion des erreurs DSL se divise en deux phases :

| Phase | Types d'erreurs | Traitement |
|-------|----------------|------------|
| Phase de traduction (côté MCP) | Erreurs de syntaxe, Attributes non reconnus, paramètres requis manquants, etc. | Toute erreur → lot terminé, IR non envoyé au pont |
| Phase d'application (côté Companion) | Actif introuvable, type incompatible, échec de compilation, etc. | Échec d'un actif unique n'affecte pas les autres actifs |

Les avertissements (Warn) pendant la phase de traduction ne terminent pas le lot mais sont attachés au texte de réponse renvoyé à l'IA.

### 7.1 Codes d'erreur courants de la phase de traduction

Quelques exemples de codes (cf. la documentation de domaine pour les détails) :

| Code d'erreur | Signification |
|---------------|---------------|
| `FM1004` | Valeur de Mode invalide |
| `FM1005` | Paramètre requis Path manquant |
| `FM1002` | Échec de traduction de domaine (code de repli générique) |

### 7.2 Principaux codes d'erreur au niveau actif (phase d'application)

| Code d'erreur | Signification |
|---------------|---------------|
| `ASSET_NOT_FOUND` | Chemin d'actif inexistant (scénarios update / delete) |
| `ASSET_ALREADY_EXISTS` | Chemin déjà existant (scénario create, pas de remplacement par défaut) |
| `ASSET_TYPE_MISMATCH` | Type incompatible |
| `ASSET_LOCKED` | Actif verrouillé dans l'éditeur |

---

## 8. Extension des domaines DSL

### 8.1 Composants d'un domaine

Un domaine DSL complet se compose des éléments suivants :

1. **Marqueurs d'Attribute** — par exemple `[Blueprint]`, `[Material]`, utilisés pour la reconnaissance de routage
2. **Implémentation du traducteur de domaine** — responsable de la traduction des déclarations DSL de ce domaine en IR JSON
3. **Documentation syntaxique DSL** — définit les règles de déclaration et les paramètres d'Attribute pour le domaine
4. **Convention de schéma IR** — définit la structure JSON du `definition` / `patches` du domaine
5. **Gestionnaire de domaine Companion** — l'implémentation correspondante côté moteur qui applique les actifs à partir de l'IR dans l'éditeur

### 8.2 Étapes de base pour ajouter un nouveau domaine

1. Définir le nom de l'Attribute racine du domaine (par exemple `[NewDomain]`)
2. Implémenter le traducteur de domaine (hériter de la classe de base traducteur, redéfinir les méthodes de visite de l'arbre syntaxique pour extraire la sémantique du domaine, produire du JSON IR)
3. S'enregistrer auprès du framework pour que le parseur puisse trouver le traducteur via `assetType`
4. Rédiger la documentation syntaxique DSL et les conventions de schéma IR
5. Implémenter le gestionnaire de domaine correspondant sur le côté Companion du moteur

L'ajout d'un nouveau domaine **ne nécessite aucune modification du code hôte** — le framework découvre les nouveaux traducteurs de domaine via le mécanisme d'enregistrement.

### 8.3 Principes d'implémentation des traducteurs

Les traducteurs suivent ces contrats de comportement qui doivent être respectés lors de l'extension de domaines DSL :

- **Fonction pure** : Le traducteur prend une chaîne DSL en entrée et produit du JSON IR en sortie, sans effets secondaires et sans état résiduel. Le même DSL produit toujours la même IR.
- **Analyse syntaxique uniquement** : Les traducteurs n'exécutent pas de compilation C#, n'accèdent pas au disque et ne résolvent pas les actifs externes ; ils ne font que traduire la forme syntaxique du DSL en IR. La validation sémantique est de la responsabilité du côté moteur.
- **Récupération d'erreur** : Peut tolérer une syntaxe DSL non standard (comme l'affectation de chaîne aux alias `using`) sans interrompre la traduction.
- **Dégradation de syntaxe inconnue** : Lors de la rencontre d'une syntaxe qui ne peut pas être traduite avec précision, signale un avertissement au lieu d'une erreur, en laissant à la phase d'application du moteur.

---

## 9. Frontière Open Source et Commercial

Les capacités DSL de ForgeMind se divisent en deux parties :

### 9.1 Open Source (noyau public)

Le noyau open source fournit le framework hôte du DSL :

- **Couche framework DSL** : Responsable de l'analyse des chaînes DSL (découpe par blocs de déclaration de niveau supérieur, reconnaissance des Attributes et routage vers les traducteurs), interfaces de traducteur et mécanismes d'enregistrement, rapports de diagnostic et autres infrastructures.
- **Contrat de plugin et mécanisme d'enregistrement** : Les nouveaux traducteurs de domaine sont découverts par enregistrement ; le framework hôte n'a pas besoin de logique de branchement spécifique au domaine.
- **Open source exclut les traducteurs de domaine** : La logique de traduction spécifique aux domaines (Blueprints, Matériaux, arbres de comportement, etc.) ne fait pas partie du noyau open source.

### 9.2 Commercial (Extension payante)

Le module d'extension commercial s'appuie sur le framework open source pour fournir :

- Toutes les implémentations spécifiques des traducteurs de domaine (Blueprint, Matériaux, arbre de comportement, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Landscape, Foliage, Physics, Paper2D, Sequencer, …)
- Le point d'entrée de l'outil MCP `dsl`, servant de point d'exécution pour la traduction et l'envoi du DSL
- Les convertisseurs IR par domaine qui effectuent un traitement spécifique au domaine avant que l'IR ne soit envoyé au moteur

> Le noyau open source définit le framework et le mécanisme d'extension ; les capacités spécifiques aux domaines (syntaxe et traducteurs DSL pour les Blueprints, Matériaux, arbres de comportement, etc.) sont fournies par le module d'extension commercial. Les utilisateurs open source peuvent enregistrer des traducteurs de domaine personnalisés basés sur le framework pour étendre leurs propres capacités DSL.

---

## 10. Contraintes de comportement et principes de conception

| Tag | Comportement DSL | Signification |
|-----|-----------------|--------------|
| Style syntaxique | Inspiré par le C# | Le C# est le langage avec le plus de données d'entraînement IA, réduisant la charge cognitive |
| Coordonnées de nœud | Non exprimées dans le DSL | Se concentre uniquement sur « ce qui existe et comment ils sont connectés » — sémantique topologique ; la disposition est générée automatiquement par le moteur |
| Chemin obligatoire | Oui | Les conventions de nommage et l'appartenance au module sont explicitement décidées par l'IA |
| Correspondance de version | Version majeure exacte | La version IR `version` doit correspondre à la version majeure du moteur |
| Politique de transaction | Pas de rollback | Un échec d'actif partiel dans un lot n'affecte pas les autres actifs |
| Essai à sec | N'existe pas | L'IR est uniquement envoyé au moteur et non renvoyé à l'appelant |
| Attribute non reconnu | Termine le lot | Un retour silencieux amènerait l'IA à créer involontairement le mauvais type d'actif |
| Correctif de mise à jour | Exprime l'état cible | Le correctif ne porte pas de champ `op` ; les opérations sont déduites par Companion basé sur l'existence du membre |

---

## 11. Référence rapide

### Modèle de bloc de déclaration DSL

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

### Modèle d'IR par actif

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

### Aperçu du pipeline

```
Client IA
  → DSL (texte contenant un ou plusieurs blocs de déclaration de domaine)
  → Outil MCP dsl (phase de traduction)
    → le parseur route vers le traducteur de domaine basé sur l'Attribute
    → les traducteurs produisent du JSON IR
  → IR par lot ({ version, batch, assets })
  → Bridge (TCP local)
  → Companion (phase d'application)
    → Créer / Mettre à jour / Supprimer / Compiler / Sauvegarder par actif
  → Asset moteur
```
