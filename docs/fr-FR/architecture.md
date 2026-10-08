# Architecture ForgeMind

ForgeMind est une **plateforme de pilotage de moteurs de jeu basée sur MCP** : le serveur (hôte) met des outils à disposition des clients IA via le protocole MCP ; les fonctions d'outils sont principalement injectées par des plugins. Côté moteur, le plugin adaptateur reçoit les opérations via un bridge TCP local. UE est l'implémentation actuelle ; la plateforme est extensible à d'autres moteurs. Ce document se base sur le code visible dans la partie **publique** ; les fonctions commerciales ne sont décrites qu'à titre général au §7.

## 1. Vue d'ensemble

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ Client IA   │ ◄────────────────► │ ForgeMindServer (hôte) │
└─────────────┘                     │  • Outils intégrés ×4  │
                                    │  • PluginLoader             │
                                    │  • Serveur bridge TCP       │
                                    │  • Registre des moteurs     │
                                    └──────────┬───────────┘
                                               │ Bridge TCP local (préfixe 4 octets + enveloppe JSON)
                                    ┌──────────▼───────────┐
                                    │ Plugin adaptateur UE  │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Hôte** (`OpenSource\ForgeMindServer`) : noyau du serveur MCP.
- **Plugins** (`OpenSource\ForgeMind.*Plugin`) : enregistrent des outils via `IPlugin`. L'hôte ne contient qu'une suite minimale d'outils.
- **Adaptateur moteur** (`UEProject\...\Plugins\ForgeMindForUE`) : plugin dans l'éditeur. Implémente les fonctions côté moteur en tant que serveur bridge.
- **Analyse/empaquetage** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`) : fournissent l'index hors ligne des fonctions moteur et l'empaquetage SPK.
- **Projet exemple** (`UEProject\ForgeMind_5_0`) : pour les tests et le débogage commun.

## 2. Hôte (ForgeMindServer)

- **Point d'entrée** : `Program.cs` démarre par défaut un service HTTP résident sur `127.0.0.1:8765` (Streamable HTTP + compatibilité SSE). `--stdio` passe en mode spawn-par-client. Le port peut être remplacé via `--port` / `FORGEMIND_PORT`.
- **Outils intégrés** (table d'outils `ForgeMindServerHost`, seulement 4) :
  - `settings` (`SettingsTool`, lecture/écriture de configuration) ;
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (interrogation et nettoyage des tâches longues).
- **Chargement des plugins** : `PluginLoader` analyse `plugins\` (défaut `<répertoire hôte>\plugins\`, remplaçable via `FORGEMIND_PLUGINS_DIR`) et injecte la table d'outils, le registre des moteurs et le bridge via `PluginContext`. Le plugin `OnLoad` appelle `RegisterTool` pour injecter des outils. Les packages de plugins prennent en charge deux dispositions : « sous-répertoire = package de plugin » (DLL principale + manifeste du même nom + dépendances) et « DLL libres à la racine ». Le chargement est paresseux (activé au premier accès à la table d'outils) ; en cas d'erreur, seul le plugin concerné est ignoré, l'hôte n'est pas affecté.
- **Registre des moteurs** : `EngineRegistry` énumère au démarrage les moteurs locaux (préférence aux installations Launcher `UE_x.y`, repli sur les builds source) et injecte aux plugins un instantané en lecture seule.
- **Serveur bridge TCP** : `ForgeMindServerBridgeServer`. Pour la découverte et la connexion de l'adaptateur moteur (§5).

## 3. Plugins et injection d'outils

| Plugin | Outils | Responsabilité |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Gestion moteurs et projets, lancement/compilation/opérations éditeur |
| `ForgeMind.Assets` | `asset` | Lecture hors ligne de la structure `.uasset` (`UAssetArchive` / `BlueprintGraphReader`, etc.) |
| `ForgeMind.Knowledge` | `knowledge` | Recherche de connaissances (source de dossier / source de paquet d'index, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (outil CLI, pas MCP) | Énumération des moteurs → analyse `UnrealScanner` → empaquetage `.spk` avec `ScanResultPacker` |

L'hôte et les plugins interagissent via le contrat `ITool` (`Compat\ITool.cs`) : nom/description/schéma de paramètres/`Execute` renvoie `ToolResult`. `ForgeMindServerTest` appelle également des outils directement via `ITool` (sans protocole MCP) pour vérifier le comportement.

## 4. Analyse et SPK (base des fonctions hors ligne)

- `ForgeMind.Scan.Abstractions` : abstractions `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures`, etc.
- `ForgeMind.Scan.Unreal` : `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore`, etc.) — analyse les fichiers d'en-tête UE, les fichiers de règles et les assets Blueprint en index d'analyse. `ScanIndexStore` est un index en mémoire à l'exécution et n'est pas écrit sur disque. Cela **diffère de l'ancien ScanCache** — le pipeline actuel n'utilise pas de preload/cache distribué ScanCache (seules des traces historiques de compatibilité subsistent dans le code ; elles ne constituent pas un mécanisme actuel).
- `ForgeMind.Scan` : `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` construisent et lisent les paquets d'analyse.
- `ForgeMind.Speedy` : `SpeedyPack` fournit un format d'empaquetage haute performance (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, avec CRC interne, mappage de répertoires, journal de pré-écriture `WriteAheadLog`, etc.).
- Utilisation : `ForgeMind.EngineScanner <spkPath>` génère des `.spk`. Des fonctions comme knowledge les consomment via `SpkScanDataProvider`.

## 5. Protocole bridge et adaptateur moteur

- Bridge : TCP local. Les messages utilisent un préfixe de longueur 4 octets + enveloppe JSON (`BridgeMessage` / `BridgeSession` / `FrameCodec`). L'adaptateur lit d'abord `%USERPROFILE%\.forgemind\bridge.json` pour la découverte et l'authentification, puis connexion → poignée de main → boucle de messages.
- Côté serveur, le routage se fait selon le nom de commande vers l'implémentation de l'adaptateur. Les gestionnaires de commandes s'exécutent dans le thread principal du moteur (thread de jeu UE) pour garantir la cohérence avec l'état de l'éditeur.
- L'adaptateur moteur (côté UE) se trouve dans `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, un module Editor contenant le client bridge et l'applicateur IR. Le projet UE exemple est compatible avec 5.0–5.8.
- Pour ajouter un nouveau moteur, il suffit d'implémenter un client bridge isomorphe et des gestionnaires de commandes (`development-guide.md` §4). Aucune modification de l'hôte ni des plugins n'est nécessaire.

## 6. Licence et sécurité (partie publique)

- Autorisation par défaut : `OpenAuthorizationService` (`Authorization\Open\`) — fonctions communautaires fixes, **n'analyse pas les fichiers de licence**.
- Sécurité des plugins : `PluginIntegrityVerifier` (vérification de l'intégrité des manifestes), `PluginSigningKeys` (vérification des signatures) ; `ManifestLoader` analyse les manifestes de plugins et prend en charge ServiceOverride (remplacement des services de l'hôte par des types abstraits, par ex. `IAuthorizationService`).
- Authentification bridge : les connexions d'adaptateur doivent lire les informations d'identification locales depuis `bridge.json` pour terminer la poignée de main (`security.md`).

## 7. Fonctions commerciales (aperçu)

- **Langage DSL et traducteurs de domaine** (code fermé) : fonctions commerciales générant des assets moteur à partir de texte lisible. La partie publique ne contient que la couche framework DSL (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, interfaces et registres dans `ForgeMindServer\DSL\`) ; la syntaxe concrète, les traducteurs de domaine et les règles IR sont fournis par les modules commerciaux et ne sont pas dans le dépôt.
- **Système de licences commerciales** (code fermé) : comprend l'émission de licences, la vérification de licences, le service de licences et une liaison optionnelle à l'environnement. L'implémentation concrète, les algorithmes de signature, le format des fichiers de licence et la logique côté serveur ne font pas partie de la partie publique et sont présentés à l'extérieur uniquement comme « fourniture de fonctions supplémentaires ».

> La version open source offre une suite complète d'outils open source et de fonctions bridge. La version Commercial s'appuie sur le même hôte, le même mécanisme de plugins et le même protocole bridge, et étend les fonctions commerciales (génération d'assets DSL, licences commerciales, support entreprise).

## 8. Tableau des limites de fonctions

| Fonction | Appartenance |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Interfaces d'extension DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Règles IR | Commercial 🔒 |
| Générateur d'assets DSL | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Implémentation des licences commerciales | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Système de clés/signatures | Commercial 🔒 |
| Détails internes du protocole de licence | Commercial 🔒 |
