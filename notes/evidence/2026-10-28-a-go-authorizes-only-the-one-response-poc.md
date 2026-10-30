# A GO authorizes only the one-response POC

**Date:** 2026-10-28  
**Source:** Florent's constraints and the assistant's narrowed proposal, followed by Florent's explicit GO.

> Florent: « Pour évaluer ta consommation, tu dois utiliser des objectifs, et là ça te donne ta conso et des infos intéressantes. Quand tu commences quelque chose, notamment de long, il te faut partir avec un objectif (ici par ex: réaliser un POC). »
>
> Florent: « Là ton plan me donne trop de détails, c'est trop long à lire, ça ne veut pas dire qu'ils n'y sont pas, mais il ne me faut vraiment que ce qui est pleinement pertinent et court. »
>
> Florent: « Attention si tu utilises claude, tu ne dois pas l'engager sur un gros chantier et lui donner un dossier dédié sinon il sera influencé par els CLAUDE.md présents dans l'arborescence. Il n'a plus beaucoup de quotas donc pas de chantier, juste une réponse. »
>
> Assistant: « Le POC proposé devient : »
>
> Assistant: « - **But :** vérifier un aller-retour Codex Windows → relais → Claude WSL. »
>
> Assistant: « - **Claude :** une seule réponse courte, aucun chantier, dans un espace isolé des `CLAUDE.md` existants. »
>
> Assistant: « - **Vérification :** conserver cette réponse et la retrouver après redémarrage du relais et de WSL, sans solliciter Claude à nouveau. »
>
> Assistant: « - **Livrable :** résultat, limites et consommation observée. »
>
> Assistant: « **Me donnes-tu le GO pour ce POC limité ?** Aucun objectif ni test n’est lancé avant ton GO. »
>
> Florent: « GO »

**Settled:** one tracked, bounded POC may obtain one short Claude response and verify its persistence after restarts; this is not an MVP implementation authorization. Stated in the agent working rules, sections "Goals and reporting" and "Agent experiments".
