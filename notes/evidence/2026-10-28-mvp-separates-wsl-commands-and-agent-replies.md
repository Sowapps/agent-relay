# MVP separates direct WSL commands and agent replies

**Date:** 2026-10-28  
**Source:** Florent's correction of the MVP framing and concrete acceptance scenario.

> Florent: « Je ne comprends pas ta dernière question, je ne comprends pas ce qu'est le worker. C'est à toi de définir ce qu'il faut pour le MVP. Mais je dirais, envoyer un message et faire un ls sur le WSL pour faire simple »
>
> Florent: « Le worker devra qualifier aussi qu'un agent sur le WSL est capable de renvoyer un message. »
>
> Assistant: « Quand tu choisis « Quitter Agent Relay » alors qu’un agent WSL travaille, que doit-il se passer ? Fermer simplement la fenêtre laisserait l’icône active. »
>
> Florent: « C'es tpas une quesiton du MVp »
>
> Florent: « Tu peux lui demandeer un LS mais c'est surtout qu'en dehors de l'agent, tu as surement besoin de faire des LS directement sur le serveur aussi, ou équivalent hein. »
>
> Florent: « Le protocole implique que normalement tu peux exécuter des commandes sur le WSL »

**Settled:** frame the MVP around direct WSL command execution and separate real-agent message replies, without making broader agent-lifecycle questions prerequisites. Stated in system-io section "Direct command execution" and agent-session section "Agent message replies".
