# Démarrer avec Agent Relay

[English](README.md)

**Utility:** démarrer avec Agent Relay en français, accompagné par son agent habituel.  
**Usage:** choisis le prompt adapté à ta situation ; les guides détaillés restent en anglais.

Agent Relay relie ton client agent Windows à des agents dans **WSL — Windows Subsystem for Linux**, actuellement Codex ou Claude, via **MCP — Model Context Protocol**. Il permet de leur confier du travail et de recevoir leurs messages/résultats. L'application Windows, en français, rassemble le suivi des projets et les diagnostics.

Tu conserves tes instructions, ton organisation et tes habitudes avec tes agents. Le relais ajoute la connexion et le suivi, sans imposer une méthode de travail.

## Partir de zéro : Agent Relay n'est pas installé

Il te faut déjà un client agent et un accès au dépôt privé **Sowapps/agent-relay**. Pour préparer le relais, l'agent utilise ses outils locaux autorisés ou te guide manuellement. « Partir de zéro » concerne le relais et ses prérequis.

Copie ce prompt dans ton client habituel :

> Aide-moi à démarrer avec Agent Relay, dépôt privé Sowapps/agent-relay : https://github.com/Sowapps/agent-relay. J'ai déjà ce client agent ; le relais et ses prérequis peuvent être absents. Lis le README et le guide de démarrage, vérifie tes outils et accès, puis propose brièvement les changements et emplacements nécessaires. Conserve mes instructions, mon organisation et mes règles d'autorisation ; clarifie les ambiguïtés avant d'agir. Après mon GO, récupère, vérifie et extrais la release stable, prépare les prérequis manquants autorisés, lance l'application, connecte ce client par MCP et crée avec moi un premier projet relayé dans les dossiers existants que je désigne. Vérifie la connexion et un petit échange convenu, puis montre-moi où le suivre. Demande les choix, permissions ou authentifications manquants ; guide les étapes que tes outils ne permettent pas d'effectuer.

Les [releases Windows](https://github.com/Sowapps/agent-relay/releases) incluent .NET : aucun SDK à installer pour lancer l'application. Ton agent vérifiera les autres prérequis nécessaires, notamment WebView2 pour l'interface et ceux du travail dans WSL.

## Ajouter un projet : le relais est déjà prêt

Indique le but du travail et les dossiers existants à utiliser, puis copie ce prompt :

> Aide-moi à utiliser Agent Relay pour ce travail : [objectif et dossiers existants]. Le relais est déjà installé. Consulte le dépôt privé Sowapps/agent-relay : https://github.com/Sowapps/agent-relay. Lis son README et son guide de démarrage. Identifie l'installation, ses données et la portée de la connexion MCP. Respecte mon organisation et les règles du projet. Propose uniquement les participants, espaces de travail, permissions et réglages manquants ; attends mon GO avant modification. Préserve les projets et l'historique existants. Vérifie le projet attendu avec list_projects, puis montre-moi où suivre son activité.

## Comprendre le projet et les chemins

Un **Projet relayé / Relay project** associe un travail à ses participants, leurs **Espaces de travail / Workspaces** et leurs permissions, éventuellement dans plusieurs dossiers Windows et Linux. Ouvrir un dossier dans un client ne crée pas automatiquement cette association : l'agent doit la vérifier.

Trois emplacements ont des usages distincts :

| Emplacement | Ce qu'il contient |
| --- | --- |
| **Dossier d'installation / Installation directory** | L'application et ses lanceurs, dans un emplacement pérenne accessible en écriture. |
| **Dossier de données / Data directory** | Configuration et historique ; par défaut, `data` dans l'installation portable lancée avec ses scripts. |
| **Espace de travail / Workspace** | Les fichiers du projet. Un chemin Windows et un chemin Linux peuvent désigner des copies différentes. |

Si une installation existe, ton agent doit retrouver ses emplacements réels plutôt qu'en créer une seconde par défaut. Tes fichiers de travail ne vont pas dans les dossiers de l'application ou de ses données.

Dans l'application, sélectionne un projet pour ses tâches, messages et résultats ; **Sessions → Conversation** affiche ses échanges. **Vue générale** rassemble l'activité. Fermer masque la fenêtre ; l'icône près de l'horloge la rouvre et **Quitter** arrête le relais. Garde-le actif pour le MCP.

## Continuer si nécessaire

Les références détaillées sont en anglais ; ton agent peut t'en expliquer les étapes en français :

- [Démarrage accompagné](apps/agent-relay/doc/using/getting-started.md) : préparation, chemins, permissions et premier accès vérifié.
- [Guide fonctionnel](apps/agent-relay/doc/using/mvp.md) : suivi dans l'application et interventions utiles.
- [Limites et pistes d'évolution](apps/agent-relay/doc/using/limits.md) : périmètre pris en charge et possibilités futures.
