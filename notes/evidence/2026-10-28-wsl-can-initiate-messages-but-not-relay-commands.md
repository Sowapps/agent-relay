# WSL can initiate messages but not relay commands

**Date:** 2026-10-28  
**Source:** Florent's additional MVP requirement and GO for the extended qualification.

> Assistant: « **GO pour le premier lot : qualifier ces trois cas dans un dossier isolé, avec un seul échange court par client ?** »
>
> Florent: « le MVP devra ensuite permettre à l'agent wsl de renvoyer lui aussi un message (mais pas de commande) »
>
> Assistant: « Compris : **l’agent WSL doit aussi pouvoir initier un message vers l’agent Windows**, pas seulement répondre. »
>
> Assistant: « - **Windows → WSL** : messages et commandes autorisées. »
>
> Assistant: « - **WSL → Windows** : messages uniquement, sans accès à l’exécution de commandes par le relais. »
>
> Assistant: « Un message reçu reste un message ; il ne déclenche pas automatiquement une commande. Ce sera un **quatrième cas de qualification**. »
>
> Assistant: « Je prends cette exigence en compte ; aucun chantier n’est lancé sans ton GO. »
>
> Florent: « GO »

**Settled:** qualify four cases with one short exchange per client; the MVP WSL endpoint may originate messages but has no relay command capability. Stated in agent-session section "Agent-initiated messages" and configuration section "MVP directional capabilities".
