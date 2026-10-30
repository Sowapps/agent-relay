# Evening MVP and V1 authorization

**Date:** 2026-10-28  
**Source:** Florent's bundled evening authorization exchange and explicit GO.

## Scoped proposal and acceptance

> Assistant: « Oui. Voici les autorisations à regrouper dans **un seul GO** : »
>
> « **Développer et tester** la clôture du MVP, puis la V1 : sessions nommées et persistantes, poursuite des échanges, parallèle, suivi et transferts de fichiers. »
>
> « **Créer les sessions et fichiers de test dédiés**, lancer les commandes nécessaires et nettoyer uniquement ces ressources. Préserver tes autres sessions et `/home/sowapps/projects`. »
>
> « **Faire les essais réels courts Codex/Claude nécessaires**. Aucun gros chantier délégué à Claude. »
>
> « **Ajouter et maintenir uniquement l’entrée MCP `agent_relay` dans la configuration Codex globale**, avec sauvegarde et contrôle des autres réglages. »
>
> « **Utiliser le jeton de reset qui expire demain**, si son activation est accessible, après consommation du quota actuel ; préserver l’autre jeton et viser l’arrêt à **4 % après reset**, sous ton plafond de 5 %. »
>
> « **Un point exige ton choix maintenant : autorises-tu aussi un redémarrage du serveur interne de Codex si indispensable ? Il peut interrompre cette conversation et d’autres conversations actives.** »
>
> « Pour le reste, je prendrai les décisions techniques et poursuivrai sans confirmations intermédiaires. Pas d’achat, d’installation globale ni d’effet externe sur tes dépôts. »
>
> Florent: « Serveur interne de codex ? »
>
> Assistant: « C’est **le processus local qui fait fonctionner Codex sur ton PC** : il gère notamment les conversations et leurs connexions MCP. Ce n’est ni un serveur distant, ni WSL, ni Agent Relay. »
>
> « **Je privilégierai un raccordement sans le redémarrer.** Je demandais cette autorisation uniquement comme recours éventuel, pas comme une étape déjà jugée nécessaire. »
>
> Florent: « Tu es autorisé oui, y'a rien d'autre qui tourne en parallèle. »
>
> Assistant: « Il reste uniquement ton **GO global pour lancer le chantier MVP puis V1**, dans le périmètre et le budget convenus. Ensuite, j’enchaînerai sans confirmations intermédiaires pour les décisions techniques courantes. »
>
> Florent: « GO »

## Settled scope

The final GO authorizes this bundled evening workstream and its necessary routine technical decisions, including only the named global MCP table, dedicated short native qualification and the stated reset cap. It does not authorize unrelated sessions/projects, purchases, global software installation or external repository effects. The internal backend restart is an allowed fallback, not proof that a safe reconnect mechanism exists.
