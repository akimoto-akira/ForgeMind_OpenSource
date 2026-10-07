# Dépannage ForgeMind

Problèmes courants et solutions (sur la base des mécanismes réels du slice public).

## 1. Erreurs de compilation

**`dotnet build ForgeMind.sln` indique « projet Commercial\... introuvable »**
Cause : `ForgeMind.sln` contient des références à des projets commerciaux (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`), mais le répertoire Commercial n'est pas publié avec le slice open source.
Solution : compiler les projets un par un (`contributing.md` §3), ne pas compiler toute la sln ; ou utiliser le script de compilation du mainteneur.

**Après la compilation du plugin, l'hôte ne voit toujours pas les outils**
- Vérifier que la DLL du plugin et le `.manifest.json` homonyme se trouvent dans `plugins\<nom du plugin>\` de l'hôte (ou dans le répertoire indiqué par `FORGEMIND_PLUGINS_DIR`) ;
- Vérifier dans le journal de l'hôte si le scan de plugins a ignoré ce paquet (le `PluginLoader` ignore uniquement le paquet en échec sans affecter l'hôte) ;
- Redémarrer l'hôte (la table des outils est chargée paresseusement au premier accès).

## 2. Problèmes de démarrage du serveur

**Le port est occupé**
Si `http://127.0.0.1:8765` est occupé, une `HttpListenerException` est signalée.
Solution : utiliser `--port N` ou la variable d'environnement `FORGEMIND_PORT` pour changer de port ; mettre à jour l'adresse du client en conséquence.

**Le client ne se connecte pas / tools/list est vide**
- Vérifier que le serveur écoute (journal « Listening on http://127.0.0.1:<port>/mcp ») ;
- Vérifier que l'URL du client est `http://127.0.0.1:8765/mcp` (mode HTTP) ;
- Vérifier que les plugins sont chargés (voir ci-dessus).

## 3. Problèmes moteur / connexion bridge

**L'éditeur est ouvert, mais l'outil indique « moteur non connecté / bridge indisponible »**
- Vérifier que le plugin `ForgeMindForUE` est activé dans le projet (`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`) ;
- L'adaptateur bridge lit `%USERPROFILE%\.forgemind\bridge.json` pour la découverte et l'authentification ; en cas de fichier manquant ou d'identifiants incorrects, la connexion échoue ;
- Voir `LogForgeMindBridge` dans le journal de l'éditeur UE ; en cas de déconnexion, la reconnexion se fait avec backoff exponentiel, attendre la reconnexion.

**`launch` / `build` semblent bloqués**
Les tâches de longue durée s'exécutent de manière asynchrone ; interroger le résultat avec `get_task_result`, afficher les tâches en cours avec `list_executing_tasks` ; `cleanup_tasks` peut nettoyer les enregistrements.

## 4. Problèmes de tests

**La précompilation de `ForgeMindServerTest` échoue**
Utiliser `--NoBuild` pour sauter la précompilation (si le CI garantit déjà des artefacts frais) ; le flux par défaut compile l'hôte et stage les plugins vers `<entrée>\plugins\`, en cas d'échec il s'arrête sans exécuter les cas de test.

**Les tests s'arrêtent à cause de fail-fast**
Les tests d'intégration s'exécutent par Group / Order avec fail-fast : arrêt au premier échec (les cas obligatoires comme « fermer l'éditeur » dans le même groupe sont quand même exécutés). Après correction, relancer le groupe avec `ForgeMindServerTest.exe [group]`.

**Les tests nécessitent l'éditeur / le projet d'exemple**
Les cas sous `Task\UE5_0\` nécessitent l'éditeur UE et une session ; les cas de traitement de données purs (SPK / scan) s'exécutent dans `ForgeMind.PublicTests` sans dépendance à l'éditeur.

## 5. Problèmes de données / assets

**La lecture `asset` échoue**
La lecture hors ligne de `.uasset` nécessite que la version du moteur corresponde (`UAssetVersions`) ; en cas de conflit de versions, l'analyse peut échouer. Vérifier la version du moteur et la portée prise en charge par l'outil.

**`.spk` ne peut pas être chargé**
Vérifier que le fichier a bien été généré par `ForgeMind.EngineScanner <spkPath>` et n'est pas endommagé ; SPK contient une vérification CRC (`Speedy\Internal\Crc32`), les fichiers endommagés sont rejetés.

## 6. Divers

**Comment activer les journaux**
Les journaux du serveur sont émis par `LogManager` (catégories dont `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer`) ; côté bridge, voir le journal de sortie de l'éditeur UE.

**Questions sur les capacités commerciales**
Pour la syntaxe DSL, la licence et les questions de licence, contacter le mainteneur ; la version OpenSource n'analyse aucun fichier de licence, les capacités sont ouvertes de manière fixe (voir `security.md`).
