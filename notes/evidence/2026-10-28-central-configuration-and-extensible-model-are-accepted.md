# Central configuration and an extensible model are accepted

**Date:** 2026-10-28  
**Source:** the assistant's scope and configuration proposal, followed by Florent's acceptance in this conversation.

> Assistant: « Pour **plusieurs espaces de travail et agents**, je recommande de les prévoir dans le modèle, tout en limitant le MVP à deux extrémités et une tâche active. »
>
> Assistant: « **Je propose une configuration centralisée pour le MVP**, administrée depuis Agent Relay. Elle contiendrait les projets relayés, les espaces de travail autorisés, les extrémités et leurs permissions. Les secrets seraient conservés séparément dans un stockage protégé. »
>
> Assistant: « Plus tard, un fichier de workspace pourrait **proposer** une association ou des préférences partageables. Son import demanderait une validation dans Agent Relay ; il ne pourrait pas accorder de droits ni déclencher un worker à lui seul. »
>
> Florent: « Ok, j'accepte tes suggestions »

**Settled:** the model supports multiple participants, the MVP limits active capacity, and configuration authority remains centralized with separate secrets. Stated in the configuration target and agent-session section "Execution capacity".
