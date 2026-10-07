# Feuille de route ForgeMind

La direction est présentée d'un point de vue public, triée par jalons ; les dates concrètes dépendent de la publication.

## Court terme

- **Compléter le slice open source** : ajouter des scripts de compilation et des guides de débogage en un clic, réduire le coût d'entrée pour les nouveaux contributeurs.
- **Synchronisation de la documentation** : maintenir `docs\zh-CN\` en cohérence avec les modifications du code source et avec le slice public.
- **Couverture des tests** : étendre les cas dans `ForgeMind.PublicTests` et `ForgeMindServerTest` (commandes bridge, scan, cas limites SPK).

## Moyen terme

- **Extension des adaptateurs moteur** : sur la base de l'adaptateur UE existant (5.0–5.8), explorer d'autres connexions moteur/éditeur (même protocole bridge).
- **Amélioration du scan et des connaissances** : améliorer la couverture de DocumentEngine pour les fichiers d'en-tête / blueprints / fichiers de règles, accroître l'efficacité de l'empaquetage SPK et des mises à jour incrémentales.
- **Écosystème de plugins** : compléter l'API des plugins et la spécification du manifeste, prendre en charge des plugins d'outils tiers plus variés.

## Long terme

- **Ouverture de la documentation d'utilisation DSL** : après stabilisation du DSL commercial, selon la stratégie produit, publier une partie de la documentation d'utilisation, des exemples et des supports pédagogiques ; l'implémentation DSL, les traducteurs, les règles IR et la logique de génération d'assets restent des modules commerciaux.
- **Plateformisation** : positionner ForgeMind comme une plateforme de pilotage moteur MCP multi-moteurs, consolider les couches de protocole et d'abstraction indépendantes du moteur.

> La direction commerciale (système d'autorisation, support entreprise, etc.) n'est pas détaillée dans cette feuille de route publique ; veuillez contacter le mainteneur.
