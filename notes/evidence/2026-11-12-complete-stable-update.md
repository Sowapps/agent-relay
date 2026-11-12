# Complete and idempotent stable update

**Date:** 12/11/2026  
**Source:** Florent, clarification during stable 1.1.1 rollout.

> Florent: « Lors du déploiement d'une nouvelle version, pas installation, ou mise à jour distante (release git en ligne ?) ou locale (repos git local?). tout doit se mettre à jour, y'a pas de mise à jour à moitié faite, il n'y a qu'un dossier sable et qu'une version stable. L'idempotence est à garantir. »

**Settled:** an update targets one stable installation and one stable version. The source of the candidate, such as a remote release or the local checkout, does not justify a partial update or another stable installation. Updating must cover the application-managed components and integrations. Repeating the same operation must preserve the resulting state without another installation or unnecessary restart. A runtime swap alone does not establish complete deployment while a required MCP client remains outdated.
