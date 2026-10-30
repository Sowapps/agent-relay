# MVP supports local work and both agent clients

**Date:** 2026-10-28  
**Source:** the assistant's first MVP questions and Florent's selected answers.

> Assistant: « Pour le MVP, que pourra faire le worker dans son workspace / espace de travail ? »
>
> Florent: « Modifier des fichiers et lancer des commandes locales, selon les autorisations du projet »
>
> Assistant: « Quelle paire d’agents faut-il prendre en charge en premier, en conservant une architecture compatible avec les autres ? »
>
> Florent: « Codex Windows → Codex ou Claude WSL dès le MVP (qualification plus importante) »

**Settled:** the MVP includes both WSL agent-client profiles and project-authorized local changes/commands; it is not restricted to a read-only agent or one client family. Stated in agent-session section "MVP client profiles" and configuration section "Authorized local work".
