# Corrected POC excludes the home project tree

**Date:** 2026-10-28  
**Source:** the corrected POC proposal, Florent's isolation constraint and explicit GO.

> Assistant: « La suite proposée : »
>
> Assistant: « 1. Corriger le POC pour utiliser un dossier pérenne isolé sous `/home/sowapps`, avec **workspace / espace de travail** et données séparés. »
>
> Assistant: « 2. Obtenir **une seule réponse courte de Claude**, sans instructions héritées. »
>
> Assistant: « 3. Redémarrer relais et WSL, retrouver cette réponse sans rappeler Claude. »
>
> Assistant: « 4. Nettoyer les fichiers de test et restituer résultat et consommation. »
>
> Assistant: « **GO pour ce POC corrigé ?** »
>
> Florent: « y'a un CLAUDE.md dans /home/sowapps donc il sera influencé, à toi de vérifier. Y'a du monde sous /home/sowapps/projects donc pour le moment, n'y va pas »
>
> Assistant: « Le prochain plan devra prévoir un emplacement pérenne hors de cette arborescence et vérifier les instructions effectivement chargées avant tout appel Claude. »
>
> Assistant: « Je prends cette contrainte en compte. **Aucune vérification ni relance du POC n’est engagée sans GO.** »
>
> Florent: « GO »

**Settled:** the corrected one-response POC is authorized with separate workspace and data, outside the home project tree, with instruction isolation checked first. Applied in the corrected POC report, section "Scope".
