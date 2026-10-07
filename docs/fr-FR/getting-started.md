# Démarrage rapide ForgeMind

ForgeMind est une **plateforme de pilotage de moteurs de jeu basée sur MCP** : via le protocole MCP, elle met à disposition des clients IA la gestion de projets Unreal Engine, la lecture d'assets et les opérations de lancement/compilation/éditeur. L'implémentation actuelle est UE ; la plateforme est extensible. La version open source contient une suite d'outils moteur et des fonctions bridge ; la version commerciale ajoute la génération d'assets DSL (§5).

Toutes les étapes de ce guide peuvent être réalisées avec la partie **open source** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Exigences d'environnement

- SDK .NET (pour compiler l'hôte et les plugins).
- Unreal Engine 5.0–5.8 (éditeur inclus). Le projet exemple est `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- Client compatible MCP (Claude Desktop / Cursor / client personnalisé).

## 2. Compilation du serveur et des plugins

```powershell
# Hôte
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugins (après compilation, placer la DLL + .manifest.json du même nom dans le répertoire plugins de l'hôte)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Ne pas compiler directement tout `ForgeMind.sln` (il contient des références à des projets commerciaux ; le répertoire commercial ne fait pas partie du dépôt). Les plugins peuvent également être utilisés à partir des artefacts de compilation fournis par le distributeur.

## 3. Démarrage

1. **Démarrer le serveur** (par défaut : HTTP résident, port 8765) :

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Ou exécuter le ForgeMindServer.exe compilé ; configurable via --port N / --stdio / FORGEMIND_PORT
   ```

2. **Ouvrir l'éditeur UE et le projet exemple** : lancer `ForgeMind_5_0.uproject` dans UE ; le plugin `ForgeMindForUE` démarre automatiquement le client bridge dans l'éditeur et se connecte à l'hôte.

3. **Configurer le client** : ajouter le serveur au client MCP :

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Après la connexion, la liste des outils apparaît (4 intégrés + plugins injectés).

## 4. Première utilisation

- `engine` — affichage des informations du moteur actuel ;
- `project` / `project_files` — affichage du projet exemple et de ses fichiers ;
- `launch` — lancement de l'éditeur, `editor` — opérations dans l'éditeur ;
- `asset` — lecture de la structure des assets du projet ;
- `knowledge` — recherche dans les connaissances du projet.

> Les tâches longues (launch/build, etc.) s'exécutent de manière asynchrone. Les résultats s'obtiennent via `get_task_result`, les tâches en cours via `list_executing_tasks`, et le nettoyage via `cleanup_tasks`.

## 5. Version commerciale (DSL)

Le module Commercial est une version commerciale séparée et ne fait pas partie du dépôt open source. La version commerciale propose la **génération d'assets DSL** : écrivez des Blueprints, des matériaux, etc. sous forme de texte lisible et convertissez-les en assets moteur. Les outils DSL concrets, les fonctions de conversion de texte et les détails de syntaxe suivent la documentation commerciale. Les fonctions de licence commerciale appartiennent également à Commercial. OpenSource ne contient aucun outil DSL.

## 6. Prochaines étapes

- Architecture et mécanismes : `architecture.md`
- Liste des outils et paramètres : `tools-reference.md`
- Développement et contribution : `development-guide.md` / `contributing.md`
- Questions fréquentes : `troubleshooting.md`
