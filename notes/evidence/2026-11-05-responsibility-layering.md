# Coherent responsibility levels

**Date:** 2026-11-05  
**Source:** Florent, clarification of SOLID and separation of concerns.

> Florent: « Généralement, on fait qu'un service n'a pas une responsabilité trop précise sinon ça n'a aucun sens. par contre, une stratégie a généralement une responsabilité très très précise. »
>
> Florent: « Par exemple, poru certains projets, Un service de gestion des tickets qui appelle un service de gestion des entités, qui appelle le service ORM, qui appelle l'adapter MySQL qui appelle PDO (pour php). C'es tout une hiérarchie bien conçue, qui permet une réutilisabilité sans créer 20.000 services »

**Settled:** select cohesive responsibility levels and reusable layers appropriate to the application. Services own coherent capabilities, strategies precise variations; do not create a service per method or force an ORM/entity-service hierarchy on every project. The PHP chain illustrates the principle, not a requested .NET design.
