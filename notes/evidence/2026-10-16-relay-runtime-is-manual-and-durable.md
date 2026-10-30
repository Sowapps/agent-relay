# Relay runtime is manual and durable

**Date:** 2026-10-16  
**Source:** Florent, initial request supplied as an attached text in this conversation.

> Florent: « - le worker WSL peut être arrêté, WSL peut être fermé ou instable ; »
>
> Florent: « - le worker doit pouvoir être redémarré à la demande ou automatiquement selon une politique explicite ; »
>
> Florent: « 1. Le futur relais doit être une application Windows ouverte manuellement par défaut, avec une icône dans la zone de notification. »
>
> Florent: « 2. Dans ses paramètres, Florent doit pouvoir choisir un lancement automatique à l’ouverture de session Windows. »
>
> Florent: « 3. Lors de la fermeture de session Windows, l’application doit se fermer proprement. »
>
> Florent: « 4. L’application doit conserver durablement les tâches, messages, états, résultats et erreurs afin de survivre à l’arrêt de WSL et au redémarrage de l’application. »

**Settled:** the Windows application has an opt-in sign-in launch and durable exchanges across interruptions. Stated in the system-io target, sections "Windows application lifecycle", "Durable storage" and "Worker availability".
