# Référence des outils ForgeMind

Tous les outils sont divisés en « intégrés à l'hôte » et « injectés par les plugins ». La version open source actuelle contient 13 outils MCP. Le plugin commercial peut fournir d'autres outils (génération d'assets DSL, etc., voir §3 ; non inclus dans la liste open source).

## 1. Outils intégrés de l'hôte

L'hôte (`OpenSource\ForgeMindServer`) contient les outils suivants sans plugins :

| Outil | Description |
|---|---|
| `settings` | Lecture/écriture de la configuration serveur (`McpSettingsManager`, persistance immédiate) |
| `get_task_result` | Interrogation du résultat des tâches longues (launch / build, etc.) |
| `list_executing_tasks` | Liste des tâches longues en cours |
| `cleanup_tasks` | Nettoyage des enregistrements de tâches terminées |

## 2. Outils injectés par les plugins

Les plugins injectent des outils via `PluginContext.RegisterTool` dans `OnLoad` ; ils apparaissent après la connexion du client. Après compilation, placez la DLL du plugin + `.manifest.json` du même nom dans le répertoire `plugins\` de l'hôte (ou le répertoire défini par `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7)

| Outil | Description |
|---|---|
| `engine` | Interrogation des moteurs installés localement (Launcher / builds source, y compris version et nom d'application) |
| `project` | Interrogation/gestion des projets UE (identification du fichier projet, interrogation des informations projet) |
| `project_files` | Liste des fichiers du projet (filtrage par répertoire/type) |
| `switch_version` | Changement de la version du projet vers le moteur spécifié (via UnrealVersionSelector) |
| `launch` | Lancement de l'éditeur ou d'exécutables empaquetés (configuration de débogage prise en charge) |
| `build` | Compilation du projet (cible par défaut / cible Editor) |
| `editor` | Opérations dans l'éditeur (scène, viewport, assets, etc., envoyées via le bridge) |

Les schémas de paramètres suivent `GetParameterSchema()` du code source de chaque outil (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Certaines opérations moteur sont soumises aux autorisations de `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Outil | Description |
|---|---|
| `asset` | Lecture hors ligne de la structure `.uasset` (noms, objets, propriétés, graphes Blueprint, interrogation par graphe/propriété) |

L'implémentation repose sur `UAsset\UAssetSerializer` / `BlueprintGraphReader`, etc. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Outil | Description |
|---|---|
| `knowledge` | Recherche de connaissances : interrogation des entrées de connaissances projet/moteur (sources : source de dossier `FolderKnowledgeSource`, source de paquet d'index SPK `IndexPackSource`) |

Implémentation dans `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Outils de la version commerciale (aperçu)

La version commerciale peut ajouter la génération d'assets DSL et des outils d'interrogation associés, injectés par le plugin commercial. Les noms, API et fonctions DSL concrets suivent la documentation commerciale. OpenSource ne contient aucun outil DSL.

## 4. Conventions d'appel des outils

- Les outils de tâches longues renvoient un ID de tâche. Le résultat s'obtient via `get_task_result`.
- Les erreurs d'outil renvoient un message d'erreur `ToolResult` (affichable directement au client).
- Les descriptions et schémas de paramètres des outils correspondent à ce que `tools/list` de MCP affiche et concordent avec le code source.
