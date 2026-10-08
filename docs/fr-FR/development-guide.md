# Guide de développement ForgeMind

Destiné aux développeurs qui souhaitent étendre ForgeMind : comment compiler, déboguer, ajouter des outils MCP, ajouter des adaptateurs moteur et exécuter les tests. Ce document est rédigé sur la base du code réel du **slice public** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Vue d'ensemble du projet

| Projet | Rôle |
|---|---|
| `OpenSource\ForgeMindServer` | Hôte du serveur MCP : 4 outils intégrés, démarre le bridge TCP, charge les plugins |
| `OpenSource\ForgeMind.OfflineEngineKit` | Plugin : outils engine / project / project_files / switch_version / launch / build / editor |
| `OpenSource\ForgeMind.Assets` | Plugin : outil asset (lecture hors ligne de `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Plugin : outil knowledge (recherche de connaissances) |
| `OpenSource\ForgeMind.EngineScanner` | Scanner en ligne de commande : énumère les moteurs et empaquette `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Index de scan : abstraction, empaquetage, implémentation UE (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | Format de paquet SPK (fichier de paquet haute performance) |
| `tests\ForgeMind.PublicTests` | Tests unitaires publics (sans dépendance moteur) |
| `tests\ForgeMindServerTest` | Tests d'intégration du serveur (nécessitent l'éditeur UE) |
| `UEProject\ForgeMind_5_0` | Projet UE d'exemple (5.0–5.8) |

## 2. Compilation

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Projets de plugins et de bibliothèques : voir contributing.md §3 (ne pas compiler directement toute la ForgeMind.sln ; elle contient des références à des projets commerciaux)
```

- Point d'entrée de l'hôte : `OpenSource\ForgeMindServer\Program.cs` (mode HTTP permanent par défaut sur `127.0.0.1:8765`, `--stdio` pour basculer, `--port` / `FORGEMIND_PORT` pour remplacer le port).
- Journalisation : `LogManager` ; les outils renvoient un `ToolResult` (succès / échec avec message).

## 3. Mécanisme des plugins et ajout d'outils MCP

L'hôte ne contient que 4 outils intégrés : `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. Tous les autres outils sont injectés par les plugins du répertoire plugins :

- Répertoire des plugins : `<répertoire de l'hôte>\plugins\` (la variable d'environnement `FORGEMIND_PLUGINS_DIR` le remplace).
- Disposition du paquet de plugin : `plugins\<nom du plugin>\` contient la DLL principale + le `.manifest.json` homonyme + les dépendances ; les DLL libres à la racine sont également prises en charge (drop-in manuel).
- Le `PluginLoader` de l'hôte scanne les plugins → `PluginContext` injecte (tables d'outils, registre des moteurs, bridge) → le plugin appelle `RegisterTool` dans `OnLoad` pour enregistrer les outils.
- Intégrité des plugins : `PluginIntegrityVerifier` / `PluginSigningKeys` sont chargés de la validation du manifeste et de la signature ; les plugins de confiance peuvent étendre les services via ServiceOverride (voir §7).

Ajouter un nouvel outil MCP :

1. Implémenter le contrat `ITool` (`OpenSource\ForgeMindServer\Compat\ITool.cs`) :
   - `Name` / `DisplayName` : nom de l'outil et nom d'affichage ;
   - `Description` : description destinée aux clients IA ;
   - `GetParameterSchema()` : déclaration de paramètres au format JSON Schema ;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)` : renvoie un `ToolResult`.
2. Créer un nouveau projet de plugin (ou l'ajouter à un plugin existant comme OfflineEngineKit), implémenter `IPlugin` et appeler `RegisterTool` dans `OnLoad`.
3. Placer le `.manifest.json` homonyme dans le même répertoire (description du plugin / déclaration des dépendances).
4. Si des capacités côté moteur sont nécessaires, les envoyer via des commandes bridge et implémenter un gestionnaire de commandes côté adaptateur (voir §5).
5. Écrire des cas de test (§6) et mettre à jour `tools-reference.md`.

## 4. Ajouter un nouvel adaptateur moteur

ForgeMind utilise des « adaptateurs » pour la logique spécifique au moteur ; les nouveaux moteurs suivent le même protocole bridge (préfixe de longueur de 4 octets + enveloppe JSON, voir `api-reference.md`) :

1. Implémenter le client bridge : lire `%USERPROFILE%\.forgemind\bridge.json` pour la découverte et l'authentification, se connecter → poignée de main → boucle de messages.
2. Enregistrer des gestionnaires sur liste blanche par nom de commande ; toutes les commandes s'exécutent sur le thread principal du moteur (correspond au thread de jeu pour UE).
3. Implémenter les capacités côté moteur (lecture/écriture d'assets, opérations de projet, etc.) et les mapper aux retours d'outils du serveur.
4. Compléter les cas de test et la documentation de ce moteur.

La première implémentation est l'adaptateur UE `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (plugin de module éditeur, client bridge + applicateur IR).

## 5. Commandes bridge côté moteur (implémentation UE actuelle)

- Le plugin adaptateur `ForgeMindForUE` contient le client bridge ; les gestionnaires de commandes s'exécutent sur le thread de jeu.
- Les opérations au niveau des assets peuvent réutiliser les gestionnaires de domaine IR du plugin, évitant une logique d'éditeur dupliquée.
- Débogage : catégorie `LogForgeMindBridge` dans le journal de l'éditeur UE ; reconnexion avec backoff exponentiel en cas de déconnexion.

## 6. Exécution des tests

### Tests unitaires publics (aucun éditeur requis)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Couverture : aller-retour de paquet SPK, SPK multi-versions, fournisseurs de données de scan, etc. (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Tests d'intégration du serveur (éditeur UE + projet d'exemple requis)

```powershell
# Par défaut : précompiler l'hôte et les plugins et les staging dans le répertoire plugins, puis démarrer l'hôte, charger les plugins, exécuter tous les ITestCase par réflexion
ForgeMindServerTest.exe

# Filtrer : par groupe + ordre
ForgeMindServerTest.exe [group] [order]          # ou --Test [group] [order]
# Sauter la précompilation
ForgeMindServerTest.exe --NoBuild
```

Mécanisme :

- Précompilation : d'abord compiler `ForgeMindServer.csproj`, puis trouver les projets de plugins sous `ForgeMind.*` avec un `.manifest.json` homonyme, les compiler un par un et les staging vers `<répertoire d'entrée>\plugins\<nom du plugin>\`.
- Exécution : trouver `ITestCase` par réflexion, trier par `Group` / `Order`, **fail-fast** ; « démarrer l'éditeur / fermer l'éditeur » sont également des cas normaux (`Order=0` démarre, `Order=int.MaxValue` ferme), les groupes partagent une session.
- Les cas de test appellent directement les outils via `ITool.Execute` (sans passer par le protocole MCP) ; en interne, les outils communiquent avec UE via le bridge.
- Sortie : rapport JSON et résumé lisible après l'exécution (répertoire de sortie indiqué dans le résultat d'exécution).
- Assets de test : plus de 30 `Blueprint_*_Tests.cs` sous `Task\UE5_0\` (domaines Blueprint : variables / fonctions / événements / matériaux / macros / interfaces, etc.) + `Task\Base\Engine_List_Tests.cs` ; `BlueprintTestHelper` encapsule les commandes bridge, la distribution de texte et la comparaison de snapshots IR (protection contre les modifications involontaires).

> Remarque : les cas de test impliquant des « chemins textuels (DSL) » dépendent de la capacité DSL commerciale ; les tests d'intégration du slice public se concentrent principalement sur les chemins de commandes bridge, selon les cas réels dans `Task\` du slice.

## 7. Licence et capacités commerciales (aperçu)

- Par défaut `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`) : capacités fixes de la communauté, n'analyse aucun fichier de licence.
- La licence commerciale remplace l'implémentation de `IAuthorizationService` via ServiceOverride dans le manifeste du plugin (hôte `ApplyServiceOverride`). Le slice public ne contient pas ce plugin commercial, seulement le mécanisme de remplacement (l'hôte ne connaît pas de notion « commerciale », il remplace uniquement par type abstrait).

## 8. Normes de codage

- Les en-têtes contiennent l'identification de licence SPDX et l'avis de droit d'auteur (Apache-2.0).
- Aucune référence aux modules commerciaux ; les nouveaux plugins respectent les limites du slice de `contributing.md`.
