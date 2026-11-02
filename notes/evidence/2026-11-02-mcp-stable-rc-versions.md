# Stable and RC MCP versions

**Date:** 2026-11-02  
**Source:** Florent.

> « Une nouvelle version doit pouvoir remonter. Ainsi un nouveau contrat a forcément une nouvelle version, même si c'est alpha, beta ou rc (ou autres, une liste définie) »
>
> « RC suffit je pense, tu ne fais qu'une version de dev et qu'une RC à la fois, non »
>
> « GO pour appliquer le plan de correction MCP avec uniquement stable et RC. Enchaîne, teste et documente, puis commit, push, fusionne et déploie. Aucune donnée utilisée sur l'app actuellement. »

**Settled:** only stable and RC versions, one current RC line; a newly deployed contract revision requires a new version. No bump for each unrelated internal commit. Build identity supplements, never replaces, the surfaced version.
