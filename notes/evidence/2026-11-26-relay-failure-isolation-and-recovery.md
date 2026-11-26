# Relay failure isolation and recovery

**Source:** Florent, during the correction of a retained approval blocking the reopening of a closed session.

> Il faut que la fermeture vérifiée lève le blocage de la session -> c'est à toi de travailler alors, corrige ça
>
> Agent Relay ne doit jamais bloquer et il doit gérer toutes les erreurs !!! C'est la demande de base du projet !! Tu as oublié ? Tu dois t'imposer les demandes comme des règles obligatoires
>
> Tu dois t'assurer de ne JAMAIS oublier ce qui a été demandé !! ça doit être inscrit avec preuve et une spec cible nommant la preuve !!

**Settled:** Relay resolves or exposes each failure with actionable evidence and limits the impact to the operation that depends on it. A verified closure must release the session when the original conversation can be reopened safely. See the functional system I/O target, *Failure isolation and recovery*.
