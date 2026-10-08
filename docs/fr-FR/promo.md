# Communication ForgeMind

## Présentation en une phrase

ForgeMind est une plateforme de pilotage de moteurs de jeu avec MCP comme interface : elle offre aux clients IA la gestion de projets, la lecture d'assets, le démarrage / la compilation / les opérations d'éditeur d'Unreal Engine, permettant à l'IA d'utiliser le moteur comme un outil.

## Points forts clés

- **Connexion MCP native** : protocole MCP standard (HTTP / stdio), utilisable immédiatement par les clients IA courants.
- **Slice open source auto-compilable** : le dépôt public contient l'hôte, tous les plugins open source, le projet UE d'exemple et les tests ; Apache-2.0, auto-compilable et extensible.
- **Architecture d'adaptateurs moteur** : l'hôte et le moteur sont découplés via un bridge TCP local, extensible à des moteurs hors UE.
- **Lecture d'assets hors ligne** : analyser les structures `.uasset` sans ouvrir l'éditeur, avec l'index de scan (SPK) pour la recherche de connaissances.
- **Injection de capacités par plugins** : l'hôte ne contient qu'un ensemble minimal d'outils, les capacités sont étendues par plugins, avec des limites claires et une modularité flexible.

## Version commerciale (une phrase)

La version commerciale offre, sur la base d'OpenSource, la génération d'assets DSL, des capacités de licence commerciale et le support entreprise ; pour les détails de la syntaxe DSL et des conditions de licence, contacter le mainteneur.

## Discipline externe

- Aucune promesse sur des fonctionnalités non publiées ; la feuille de route suit `roadmap.md`.
- Ne pas dénigrer des outils similaires ; ne pas divulguer les implémentations internes des modules commerciaux (algorithmes de licence, règles IR internes du DSL, etc.).
- Les comportements liés au code source se réfèrent toujours au slice public ; les points incertains sont marqués « à confirmer ».
