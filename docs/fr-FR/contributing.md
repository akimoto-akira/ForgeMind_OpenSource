# Guide de contribution ForgeMind

Merci de votre intérêt pour ForgeMind. Le dépôt public correspond au slice de publication OpenSource de ForgeMind et ne contient que les projets open source publiés, les tests et les projets d'exemple. Les modules commerciaux (traducteurs de domaine DSL, systèmes de licence, etc.) ne sont pas dans le dépôt public ; veuillez d'abord comprendre la répartition avant de contribuer.

## 1. Structure du dépôt public

Structure de premier niveau du slice open source (le dépôt que vous clonez) :

```
ForgeMind.sln                          # Solution (contient des références à des projets commerciaux, voir ci-dessous)
OpenSource\                            # Projets open source
  ForgeMindServer\                     # Serveur MCP (hôte)
  ForgeMind.OfflineEngineKit\          # Plugin d'outils hors ligne du moteur (engine/project/launch/build/editor, etc.)
  ForgeMind.Assets\                    # Plugin de lecture d'assets (asset)
  ForgeMind.Knowledge\                 # Plugin de recherche de connaissances (knowledge)
  ForgeMind.EngineScanner\             # Scanner de moteurs (ligne de commande)
  ForgeMind.Scan\                      # Empaquetage de scan
  ForgeMind.Scan.Abstractions\         # Abstraction de scan
  ForgeMind.Scan.Unreal\               # Implémentation de scan UE (DocumentEngine)
  ForgeMind.Speedy\                    # Format de paquet SPK
UEProject\
  ForgeMind_5_0\                       # Projet UE d'exemple (compatible 5.0–5.8)
tests\
  ForgeMind.PublicTests\               # Tests unitaires publics (SPK / données de scan)
  ForgeMindServerTest\                 # Tests d'intégration du serveur (nécessitent l'éditeur UE)
```

> Remarque : le fichier de solution du dépôt de développement actuel (`ForgeMind.sln`) contient des références à des projets Commercial (par ex. `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`) ; la publication publique **n'inclut pas le répertoire `Commercial/`**. Les utilisateurs OpenSource doivent donc compiler les projets publics un par un (voir §3) et ne pas exécuter directement `dotnet build ForgeMind.sln`.

## 2. Limites open source

- **OpenSource\** : entièrement open source (Apache-2.0), principale zone de contribution.
- **UEProject\** : le projet d'exemple est open source, mais les assets binaires `*.uasset` sont exclus lors de l'export du slice (sous Content ne restent que les assets textuels comme les cartes et le squelette de répertoires).
- **tests\** : open source, les contributions nécessitent des tests d'accompagnement.
- **Commercial n'est pas dans le dépôt public** : traducteurs de domaine DSL, licence commerciale, etc. n'apparaissent pas dans le slice ; le code open source ne doit pas référencer les types de modules commerciaux.
- L'export exclut automatiquement `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`, etc. (artefacts de compilation et répertoires temporaires) ; l'identification de licence en tête de fichier est remplacée uniformément par Apache-2.0 lors de l'export.

## 3. Compilation

```powershell
# Compiler un par un (ne pas compiler directement toute la sln, voir ci-dessus)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Projets de plugins (après compilation, placer les artefacts + le .manifest.json homonyme dans le répertoire plugins de l'hôte)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

L'hôte charge les plugins depuis `<répertoire de l'hôte>\plugins\` (remplaçable par la variable d'environnement `FORGEMIND_PLUGINS_DIR`) ; un paquet de plugin = DLL principale + `.manifest.json` homonyme + dépendances (voir `development-guide.md` §3).

## 4. Normes de codage

- Tous les fichiers source conservent en tête l'identification de licence SPDX et l'avis de droit d'auteur (actuellement Apache-2.0).
- Les espaces de noms sont organisés par module (`ForgeMindServer.*` / `ForgeMind.*`), les contrats publics passent par des interfaces abstraites comme `ITool` / `IPlugin`.
- Les classes d'outils du serveur doivent fournir une `Description` complète et un schéma de paramètres (ces textes sont exposés directement aux clients IA).
- Le code des adaptateurs moteur respecte les normes de codage du moteur correspondant (actuellement côté UE : préfixes de types F / U / T, macro `TEXT()`, contraintes de sécurité du thread de jeu).
- Le nouveau code ne doit référencer ou dépendre d'aucun module commercial.

## 5. Processus PR

1. Créer une branche depuis `main`, nom suggéré `feat/<description>` ou `fix/<description>`.
2. Message de commit : `<type> : <description en une phrase>`.
3. Auto-vérification avant le commit :
   - Compiler un par un selon §3, tous réussis ;
   - En cas de modification du comportement des commandes bridge / outils : les groupes `ForgeMindServerTest` concernés passent (mode d'exécution voir `development-guide.md` §6) ;
   - En cas de modification de SPK / données de scan : `ForgeMind.PublicTests` passe ;
   - Aucune référence aux modules commerciaux introduite ;
   - Mettre à jour en synchronisation la page correspondante sous `docs\zh-CN\`.
4. Lors de l'ouverture de la PR, renseigner : étendue des modifications, résumé des résultats de test, si un module spécifique doit être revu par le mainteneur.
5. Points de revue du mainteneur : limites open source, compatibilité API, couverture des tests.

## 6. Exigences de tests

- Les modifications de commandes bridge / comportement d'outils / logique d'adaptateur doivent être accompagnées de cas de test (ajouter `*_Tests.cs` sous `tests\ForgeMindServerTest\Task\`).
- Les tests d'intégration du serveur nécessitent un environnement d'éditeur UE ; les tests de traitement de données purs (SPK, scan) sont dans `tests\ForgeMind.PublicTests\`, sans dépendance moteur.
- Les cas doivent passer de manière stable dans l'ordre par défaut (fail-fast, arrêt au premier échec).

## 7. Contribution à la documentation

- La documentation en chinois se trouve sous `docs\zh-CN\` ; le contenu doit correspondre aux faits du code source, les points incertains sont marqués « à confirmer ».
- Lors de la modification de la liste des outils externes, mettre à jour `tools-reference.md` en synchronisation.
- Les capacités commerciales (syntaxe DSL, mécanisme de licence) ne sont décrites dans la documentation que de manière sommaire, sans détails internes.

## 8. Code de conduite

- Respecter les décisions de conception existantes (protocole bridge, contrats d'outils, format SPK) ; pour les changements majeurs, lancer d'abord une discussion.
- Ne pas dénigrer publiquement les concurrents ; le contenu promotionnel suit la discipline externe de `promo.md`.
