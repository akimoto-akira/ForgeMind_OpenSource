# Sécurité ForgeMind

Ce document décrit la conception de sécurité et les recommandations d'utilisation du slice public.

## 1. Frontière de communication locale

- **Le serveur écoute par défaut uniquement sur loopback** : en mode HTTP sur `127.0.0.1:8765`, non ouvert aux réseaux externes.
- **Le bridge est un TCP local** : la connexion entre l'hôte et l'adaptateur moteur est un TCP local ; l'adaptateur doit lire `%USERPROFILE%\.forgemind\bridge.json` (contenant hôte, port, identifiants) pour la découverte et l'authentification, puis effectuer la poignée de main.
- Ne pas exposer le port du serveur sur le réseau public ; pour un accès distant, le placer sur un réseau de confiance et le transférer via un canal sécurisé (par ex. tunnel SSH).

## 2. Intégrité des plugins

- Le `PluginLoader` de l'hôte effectue une **vérification d'intégrité du manifeste** (`PluginIntegrityVerifier`) et une vérification de signature (`PluginSigningKeys`) lors du chargement des plugins : un paquet de plugin doit contenir la DLL principale + le `.manifest.json` homonyme ; en cas de manque ou de non-concordance, il est rejeté / ignoré.
- L'injection d'outils par les plugins se produit dans le processus de l'hôte ; n'installer que des plugins de sources fiables (compilés depuis le slice public ou publiés officiellement).

## 3. Modèle d'autorisation

**Mécanismes de sécurité visibles publiquement** :
- `IAuthorizationService` : abstraction du point d'entrée d'autorisation de l'hôte, l'implémentation par défaut est `OpenAuthorizationService` (capacités fixes de la communauté, n'analyse aucun fichier de licence, ne génère aucune licence de test).
- ServiceOverride de confiance : les plugins peuvent remplacer des services de l'hôte via ServiceOverride dans le manifeste, par type abstrait (l'hôte ne connaît pas de notion « commerciale », il remplace uniquement par type).
- Vérification d'identité et validation du manifeste des plugins : `PluginIntegrityVerifier` / `PluginSigningKeys` vérifient le manifeste et la signature des plugins, garantissant que seuls les plugins de confiance sont chargés.
- Authentification de connexion bridge : l'adaptateur effectue la découverte locale et la poignée de main d'identifiants via `bridge.json`, confirmant la fiabilité de la source de connexion.

**Licence commerciale** : Core fournit l'abstraction `IAuthorizationService` et le mécanisme d'extension ServiceOverride de confiance. L'implémentation de la licence commerciale appartient au plugin closed source ; les protocoles concrets de licence, la délivrance, la vérification et la logique de liaison ne font pas partie du slice public.

## 4. Autorisations des outils

- Les outils d'opérations moteur sont limités par `ServerPermissionChecker` (dans les plugins concernés) ; l'autorisation de capacité est vérifiée avant l'exécution.
- Les appels `Execute` d'outils en échec renvoient uniformément un message d'erreur `ToolResult`, sans exposer les détails internes de la pile.

## 5. Recommandations d'utilisation

- N'ouvrir le port MCP qu'à des clients de confiance ; les clients disposent de capacités d'opérations moteur, équivalant à des droits d'administration locaux.
- Maintenir à jour les plugins, l'hôte et le projet d'exemple pour recevoir rapidement les correctifs de sécurité.
- Sur les machines partagées, ne pas transmettre les identifiants de `bridge.json` à des processus non fiables.
- Signaler les problèmes de sécurité via le canal du mainteneur, ne pas divulguer publiquement les détails de vulnérabilités non publiées.
