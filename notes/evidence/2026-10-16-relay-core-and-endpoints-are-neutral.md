# Relay core and endpoints are neutral

**Date:** 2026-10-16  
**Source:** Florent, initial request supplied as an attached text in this conversation.

> Florent: « 7. L’architecture doit être hexagonale. »
>
> Florent: « 8. Le cœur métier doit rester neutre : il ne doit connaître ni Codex, ni Claude, ni Windows, ni WSL, ni tmux. »
>
> Florent: « 9. Les deux extrémités sont neutres à la base. Les rôles — pilote, worker, relecteur, observateur — sont définis par la configuration d’un projet et non figés par la technologie ou l’OS. »
>
> Florent: « 10. La compatibilité future avec Codex et Claude Code est un objectif. MCP est une piste privilégiée pour l’interface, mais n’est pas une décision d’architecture détaillée déjà validée. »

**Settled:** endpoint roles and the hexagonal core are technology-neutral; MCP remains a candidate. Stated in the agent-session target, sections "Endpoint roles", "Hexagonal architecture" and "Client compatibility".
