using AgentRelay.Core;

namespace AgentRelay.Windows;

internal static class UserMessages
{
    public static string WorkState(string state) => state switch { "pending" => "En attente", "running" => "En cours", "retry_wait" => "Reprise planifiée", "completed" => "Terminé", "failed" => "Échec", "awaiting_approval" => "Autorisation attendue", "approval_expired" => "Approbation expirée", _ => "À confirmer" };
    public static string Error(string? code) => code switch
    {
        WorkExecution.RetryCancelled => "La reprise planifiée a été annulée avant l’envoi.",
        "project_draining" => "Archivage en cours : les nouveaux travaux sont suspendus. Le travail engagé peut se terminer.",
            "project_archived" => "Ce projet est archivé. Son historique reste consultable ; réactivez-le avant une nouvelle opération.",
        "archive_blocked" => "Le projet n’a pas été archivé : terminez ou vérifiez les opérations en cours et les résultats incertains, puis fermez ses sessions.",
        "archive_unverified" => "Les vérifications d’archivage ne sont pas complètes. Aucun changement d’état n’a été effectué.",
        "state_conflict" => "L’état du projet a changé. Actualisez la liste avant de choisir une nouvelle action.",
        "project_lifecycle_required" => "Conservez les projets existants et utilisez les actions Archiver ou Réactiver pour changer leur état.",
        "unavailable" => "L’agent n’est pas encore prêt. Vérifiez son état avant de renvoyer le message.",
        "authentication_required" => "Cette connexion au relais n’est pas authentifiée ou a été révoquée. Vérifiez son association au projet.",
        "agent_authentication_required" => "Le compte utilisé par l’agent n’est pas authentifié dans cet environnement. Vérifiez l’état du compte avant une nouvelle demande.",
        "credential_reference_unavailable" => "Agent Relay ne peut pas utiliser la référence aux identifiants dans cet environnement. Cela ne prouve pas que la connexion habituelle au compte a expiré.",
        "legacy_unassigned" => "Cette donnée antérieure n’est pas attribuée à un projet. Consultez l’historique en lecture seule ou configurez un projet.",
        "adapter_unavailable" => "L’adaptateur ou sa configuration n’est pas disponible pour cette destination.",
        "invalid_path" => "Choisissez un chemin relatif dans l’espace de travail de cette session.",
        "destination_exists" => "Ce fichier existe déjà. Son remplacement doit être demandé explicitement.",
        "file_too_large" => "Transfert refusé : le fichier dépasse la taille maximale autorisée.",
        "relay_timeout" or "timeout" => "Le délai d’attente est dépassé. L’action peut être encore en cours ; vérifiez son état avant de réessayer.",
        "busy" => "Une opération est déjà en cours sur cette destination.",
        "review_stale" => "L’opération a évolué. Consultez son état actuel avant de la marquer comme examinée.",
        "operation_already_finished" => "L’opération est terminée. Son résultat est conservé.",
        "operation_cancellation_required" => "Demandez d’abord l’annulation de l’opération en attente.",
        "approval_already_resolved" => "Une réponse finale est arrivée. Consultez le résultat ou les messages.",
        "approval_stop_mismatch" => "Cette opération n’attend plus de fermeture dans cette session. Consultez son résultat.",
        "cancellation_unconfirmed" => "L’arrêt reste à vérifier avant de réutiliser cette session.",
        "approval_expired" => "Le délai d’approbation est écoulé.",
        "server_overloaded" => "Le modèle Codex est saturé. Vérifiez si une réponse à cette demande arrive.",
        "native_provider_rate_limited" => "Le service a renvoyé HTTP 429 après les tentatives de Codex.",
        "native_turn_failed" => "Codex a signalé une erreur sur le tour associé à cette demande.",
        "native_reply_missing" => "Le tour Codex est terminé. Aucune réponse liée à cette demande n’a été reçue.",
        "transfer_destination_busy" => "Un transfert écrit déjà vers ce fichier.",
        "cancelled_effects_unconfirmed" => "L’arrêt est confirmé. Les effets déjà produits restent à vérifier.",
        "not_found" => "Cette opération ou cette destination n’est plus disponible.",
        "result_too_large" => "Le détail dépasse la taille autorisée. Utilisez les aperçus filtrés ou la référence du fichier produit.",
        "command_denied" or "capability_denied" => "Cette action n’est pas autorisée par les permissions de ce projet.",
        "invalid_work" => "Vérifiez la destination et le contenu de votre demande.",
        _ => "L’action n’a pas pu être confirmée. Les détails techniques permettent d’en examiner la cause."
    };

    public static string Error(Exception error) => error is RelayException relay ? Error(relay.Code) :
        error is TimeoutException ? Error("timeout") : Error((string?)null);

    public static string Cancellation(string state) => state switch
    {
        "close_required" => "Annulation enregistrée. Vous pouvez fermer la session liée pour vérifier l’arrêt.",
        "approval_expired" => "Approbation expirée. Inspectez l’invite avant de décider de la suite.",
        "stop_unconfirmed" => "L’arrêt du travail lié reste à vérifier.",
        "stop_confirmed" => "Arrêt confirmé. Ouvrez une nouvelle session pour poursuivre.",
        "finished" => "L’opération a atteint un résultat final.",
        _ => "Annulation demandée. Consultez le résultat pour suivre sa confirmation."
    };

    public static string AgentState(string state) => state switch
    {
        "ready" => "Agent prêt à recevoir un message.",
        "starting" => "Démarrage en cours. Utilisez « État de l’agent » pour vérifier sa disponibilité.",
        "not_started" => "Agent non démarré. Utilisez « Démarrer l’agent ».",
        "attention_required" => "L’agent attend une intervention dans son terminal dédié.",
        "awaiting_approval" => "Une autorisation humaine est attendue dans le terminal dédié. Aucune approbation automatique n’est donnée.",
        "unavailable" => "L’agent ou son environnement est indisponible. Consultez les détails techniques.",
        _ => "État non confirmé. Vérifiez sa disponibilité avant de relancer une action."
    };
}
