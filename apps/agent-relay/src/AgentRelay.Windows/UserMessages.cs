using AgentRelay.Core;

namespace AgentRelay.Windows;

internal static class UserMessages
{
    public static string WorkState(string state) => state switch { "pending" => "En attente", "running" => "En cours", "completed" => "Terminé", "failed" => "Échec", "awaiting_approval" => "Autorisation attendue", _ => "À confirmer" };
    public static string Error(string? code) => code switch
    {
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
        "not_found" => "Cette opération ou cette destination n’est plus disponible.",
        "result_too_large" => "Le détail dépasse la taille autorisée. Utilisez les aperçus filtrés ou la référence du fichier produit.",
        "command_denied" or "capability_denied" => "Cette action n’est pas autorisée par les permissions de ce projet.",
        "invalid_work" => "Vérifiez la destination et le contenu de votre demande.",
        _ => "L’action n’a pas pu être confirmée. Les détails techniques permettent d’en examiner la cause."
    };

    public static string Error(Exception error) => error is RelayException relay ? Error(relay.Code) :
        error is TimeoutException ? Error("timeout") : Error((string?)null);

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
