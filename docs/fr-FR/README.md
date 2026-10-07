# Documentation ForgeMind (français)

ForgeMind — une plateforme de pilotage de moteurs de jeu basée sur MCP : via le protocole MCP, elle met à disposition des clients IA la gestion de projets Unreal Engine, la lecture d'assets et les opérations de lancement/compilation/éditeur. L'implémentation actuelle cible UE (5.0–5.8) ; la conception de la plateforme est extensible à d'autres moteurs.

> Cette documentation se concentre sur la partie **open source** : `OpenSource\` (hôte et plugins), `UEProject\` (projet exemple) et `tests\` (tests). Les fonctionnalités commerciales (génération d'assets DSL, implémentation des licences commerciales) ne sont décrites que de façon générale et ne sont pas incluses dans la version open source. OpenSource conserve les abstractions de licence nécessaires et une implémentation ouverte.

## Index de la documentation

| Document | Contenu |
|---|---|
| [Démarrage rapide](getting-started.md) | Exigences d'environnement, compilation, démarrage, première utilisation |
| [Architecture](architecture.md) | Hôte / plugins / protocole bridge / analyse et SPK |
| [Référence API](api-reference.md) | Protocole MCP, protocole bridge, contrats d'outils |
| [Référence des outils](tools-reference.md) | Tous les outils MCP et leurs paramètres |
| [Guide de développement](development-guide.md) | Compilation, débogage, ajout d'outils/adaptateurs, tests |
| [Guide de contribution](contributing.md) | Limites open source, processus PR, exigences de tests |
| [Résolution de problèmes](troubleshooting.md) | Problèmes courants et solutions |
| [Feuille de route](roadmap.md) | Direction planifiée |
| [Promotion](promo.md) | Présentation du projet et règles de communication |
| [Sécurité](security.md) | Conception de sécurité et bonnes pratiques |

## Vue d'ensemble du périmètre open source

```
OpenSource\
  ForgeMindServer               Hôte MCP (4 outils intégrés + chargement de plugins + bridge TCP)
  ForgeMind.OfflineEngineKit    Outils moteur/projet/lancement/compilation/éditeur (plugin)
  ForgeMind.Assets              Lecture hors ligne des .uasset (plugin)
  ForgeMind.Knowledge           Recherche de connaissances (plugin)
  ForgeMind.Scan.Abstractions   Abstractions des données et fournisseurs d'analyse
  ForgeMind.Scan                Lecture et empaquetage SPK
  ForgeMind.Scan.Unreal         Implémentation de l'analyse Unreal Engine
  ForgeMind.EngineScanner       Analyseur CLI autonome qui génère directement des .spk
  ForgeMind.Speedy              Base SPK de bas niveau
UEProject\ForgeMind_5_0         Projet exemple UE (5.0–5.8)
tests\                          Tests unitaires publics + tests d'intégration du serveur
```

## Licence

Le contenu de la partie open source est sous licence Apache-2.0. Les modules commerciaux ne font pas partie de la partie open source et sont fournis sous une licence commerciale séparée.
