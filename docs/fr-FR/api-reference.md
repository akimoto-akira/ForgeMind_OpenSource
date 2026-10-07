# Référence API ForgeMind

Ce document décrit la partie publique des interfaces externes de ForgeMind : connexion MCP, protocole bridge, contrats d'outils et mécanisme des tâches longues. Les fonctions commerciales (DSL) ne sont décrites qu'à titre général au §6.

## 1. Connexion MCP

- **Mode HTTP** (défaut) : `http://127.0.0.1:8765/mcp`, Streamable HTTP (compatible avec l'ancien SSE). Le processus hôte est résident ; une connexion dessert plusieurs sessions clientes.
- **Mode stdio** : `ForgeMindServer.exe --stdio`, un processus séparé est lancé par client.
- Port : `--port N` > variable d'environnement `FORGEMIND_PORT` > défaut 8765.

Méthodes MCP standard prises en charge : `initialize`, `tools/list`, `tools/call`, `notifications/cancelled`, etc. Le schéma des outils est fourni par `ITool.GetParameterSchema()`.

## 2. Contrat d'outils

Les outils du serveur implémentent une interface unifiée (`OpenSource\ForgeMindServer\Compat\ITool.cs`) :

- `Name` / `DisplayName` : identifiant et nom d'affichage de l'outil ;
- `Description` : texte descriptif pour le client IA ;
- `GetParameterSchema()` : déclaration des paramètres (style JSON Schema) ;
- `Execute(Guid callerId, Dictionary<string, object> parameters)` : renvoie `ToolResult` après exécution (résultat de succès ou message d'erreur).

Flux d'appel : client `tools/call` → l'hôte identifie `ITool` → validation des paramètres → `Execute` → retour du résultat. Les outils nécessitant des fonctions moteur envoient en interne des commandes via le bridge TCP.

## 3. Protocole bridge (hôte ↔ adaptateur moteur)

- Transport : TCP local. Format de trame = préfixe de longueur 4 octets + enveloppe JSON (`BridgeMessage`, voir `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Découverte et authentification : l'adaptateur lit `%USERPROFILE%\.forgemind\bridge.json` (contient hôte, port et informations d'identification), effectue connexion → handshake → boucle de messages.
- Sémantique des commandes : une requête contient le nom de la commande et ses paramètres. Le serveur route la commande vers l'implémentation de l'adaptateur ; la réponse contient le code d'état, le message et les données. Les commandes côté moteur s'exécutent dans le thread principal du moteur (thread de jeu UE).
- Cycle de vie : l'hôte commence à écouter le bridge au démarrage. L'adaptateur se reconnecte avec backoff exponentiel en cas de déconnexion.

## 4. Mécanisme des tâches longues

- Les tâches longues (`launch`, `build`, etc.) s'exécutent de manière asynchrone et ne bloquent pas l'appel d'outil.
- Outils intégrés associés :
  - `get_task_result` : interrogation du résultat de la tâche ;
  - `list_executing_tasks` : liste des tâches en cours ;
  - `cleanup_tasks` : nettoyage des enregistrements de tâches.
- En interne, `LongTaskManager` gère le cycle de vie des tâches (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Outils intégrés (partie de l'hôte, sans plugins)

| Outil | Description |
|---|---|
| `settings` | Lecture/écriture de la configuration serveur (`McpSettingsManager`, persistance immédiate) |
| `get_task_result` | Interrogation du résultat des tâches longues |
| `list_executing_tasks` | Interrogation des tâches en cours |
| `cleanup_tasks` | Nettoyage des enregistrements de tâches |

La liste complète des outils injectés par les plugins figure dans `tools-reference.md`.

## 6. Fonctions commerciales (DSL, aperçu)

- OpenSource fournit les **contrats d'extension DSL** (interfaces et registres `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` dans `ForgeMindServer\DSL\` sont publiquement disponibles). Les implémentations concrètes de syntaxe DSL, Parser, Translator, règles IR et la logique de génération d'assets appartiennent à Commercial et ne sont pas dans OpenSource.
- Les outils DSL sont injectés par le plugin commercial. Les noms et API concrets suivent la documentation commerciale. La liste d'outils OpenSource ne contient aucun outil DSL.

| Fonction | Appartenance |
|---|---|
| Interfaces d'extension DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Règles IR | Commercial 🔒 |
| Logique de génération d'assets | Commercial 🔒 |

## 7. Notes de compatibilité

- Le format de trame du protocole bridge et les schémas d'outils suivent ce document. Les changements cassants seront annoncés à l'avance avec un avis de migration.
