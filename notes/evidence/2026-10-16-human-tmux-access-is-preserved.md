# Human tmux access is preserved

**Date:** 2026-10-16  
**Source:** Florent, initial request supplied as an attached text in this conversation.

> Florent: « - Florent doit pouvoir reprendre manuellement la main sur le worker WSL. »
>
> Florent: « 5. tmux est une solution déjà utilisée et acceptée pour garder un agent CLI WSL persistant et accessible humainement. »
>
> Florent: « 6. Il est acceptable que tmux soit le premier adaptateur de communication avec le worker. Ne pas le rejeter sans raison : l’envoi et la lecture de messages fonctionnent déjà suffisamment bien dans l’usage réel. »

**Settled:** the worker remains human-accessible and tmux is acceptable as the first adapter, not mandated for every endpoint. Stated in the agent-session target, section "Human control".
