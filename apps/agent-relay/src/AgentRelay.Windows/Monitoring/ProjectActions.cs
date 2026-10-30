using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Windows.Presentation;

namespace AgentRelay.Windows;

internal sealed partial class ProjectWindow
{
    private IEnumerable<WorkRecord> Operations => observations.Values.SelectMany(item => item.Operations);
    private IEnumerable<SessionSnapshot> Sessions => observations.Values.SelectMany(item => item.Sessions);
    private static string? Value(JsonElement message, string name) => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private bool Supports(SessionSnapshot session, string capability) => configuration.Projects.Any(item => item.Id == session.Scope.ProjectId && item.Enabled)
        && descriptors.Any(item => item.EndpointId == session.Scope.RecipientId && item.Capabilities?.Contains(capability) == true)
        && configuration.Memberships.Any(item => item.ProjectId == session.Scope.ProjectId && item.EndpointId == session.Scope.SenderId
            && item.Capabilities.Contains(capability == "session.inspect" ? RelayCapabilities.ReadHistory : capability));

    private async Task ReadThread(SessionSnapshot session,bool older,CancellationToken token)
    {
        if(!configuration.Projects.Any(item=>item.Id==session.Scope.ProjectId))throw new RelayException("not_found","Project unavailable");
        if(!threads.TryGetValue(session.Id,out var thread))threads[session.Id]=thread=new(session.Scope.ProjectId,session.Id);
        try{await source.LoadAsync(thread,older,token);}
        catch(Exception error) when(!released){thread.Error=UserMessages.Error(error);}
        var project=observations[session.Scope.ProjectId];
        Monitoring.PipeMonitorSource.Merge(project.Operations,thread.Operations,item=>item.Request.Id,out var operationsChanged);
        Monitoring.PipeMonitorSource.Merge(project.Messages,thread.Messages,item=>item.Id,out var messagesChanged);
        if(operationsChanged||messagesChanged)project.Revision++;
    }

    private async Task HandleAction(JsonElement message)
    {
        if (released) return;
        var action = Value(message,"action"); var id = Guid.TryParse(Value(message,"id"),out var parsed) ? parsed : Guid.Empty;
        try
        {
            switch (action)
            {
                case "stop-session": activeSession=null; break;
                case "load-session":
                    var threadSession=Sessions.FirstOrDefault(item=>item.Id==id) ?? throw new RelayException("not_found","Session unavailable");
                    activeSession=id;
                    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {timeout.CancelAfter(TimeSpan.FromSeconds(12));await ReadThread(threadSession,message.TryGetProperty("older",out var previous)&&previous.ValueKind==JsonValueKind.True,timeout.Token);}
                    PublishSnapshot(true); break;
                case "refresh": await Reload(true); break;
                case "load-more":
                    var projectText = Value(message,"project");
                    Guid? project = projectText is null ? null : Guid.Parse(projectText);
                    if (project is not null && !observations.ContainsKey(project.Value)) throw new RelayException("not_found","Project unavailable");
                    await Reload(false,true,project); break;
                case "operation":
                    var preview = Operations.FirstOrDefault(item => item.Request.Id == id) ?? throw new RelayException("not_found","Operation unavailable");
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(8));
                        var work = await source.OperationAsync(preview.Request.Scope!.ProjectId,id,timeout.Token);
                        Presentation.Send(new { type="operation", data=PresentationSnapshot.Operation(work,configuration,reviews) });
                    }
                    break;
                case "mark-reviewed":
                    var retained = Operations.FirstOrDefault(item=>item.Request.Id==id && item.Request.Scope!.ProjectId.ToString("D")==Value(message,"project")) ?? throw new RelayException("not_found","Operation unavailable");
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(8));
                        var current = await source.OperationAsync(retained.Request.Scope!.ProjectId,id,timeout.Token);
                        reviews.Mark(current,Value(message,"reviewToken"));
                        Presentation.Send(new { type="operation-review", id=id.ToString("D"), reviewToken=AttentionReviews.Token(current) });
                        Notice("Marqué comme examiné. L’historique est conservé ; toute évolution sera signalée.");
                    }
                    break;
                case "copy":
                    var text = Value(message,"text");
                    if (text is { Length: > 0 and <= 240000 }) { Clipboard.SetText(text); Notice("Contenu copié."); }
                    else Notice("Le contenu est vide ou trop volumineux pour cette copie.");
                    break;
                case "operation-events":
                    var eventWork = Operations.FirstOrDefault(item => item.Request.Id == id) ?? throw new RelayException("not_found","Operation unavailable");
                    var events = (await client.RequestAsync(new("project",ProjectAction:new("observe_events",Query:new(eventWork.Request.Scope!.ProjectId,TaskId:eventWork.Request.TaskId,Limit:50))),lifetime.Token)).Project!.Events!;
                    var entries = events.Items.Where(item=>item.RequestId==id).Select(item=>item.OccurredAt.ToLocalTime().ToString("dd/MM HH:mm:ss")+" · "+item.State+" · "+item.ErrorCode);
                    Presentation.Send(new { type="operation-diagnostic", id=id.ToString("D"), text=string.Join("\n",entries)+(events.HasMore ? "\nPage partielle ; historique complet accessible via MCP." : "") }); break;
                case "inspect-partial":
                    var transfer = Operations.FirstOrDefault(item=>item.Request.Id==id && item.Request.Kind=="transfer") ?? throw new RelayException("not_found","Transfer unavailable");
                    var partial = (await client.RequestAsync(new("project",ProjectAction:new("inspect_transfer",RequestId:id,ActorId:transfer.Request.Scope!.SenderId)),lifetime.Token)).Project!.Partial!;
                    Presentation.Send(new { type="operation-diagnostic", id=id.ToString("D"), text=partial.Exists ? $"Fichier partiel conservé : {partial.Bytes:N0} octets · {partial.RelativePath}. Nettoyage disponible dans la console technique." : "Aucun fichier partiel conservé." }); break;
                case "session-control":
                case "inspect-session":
                    var session = Sessions.FirstOrDefault(item => item.Id == id) ?? throw new RelayException("not_found","Session unavailable");
                    var inspect = action == "inspect-session";
                    if (!Supports(session,inspect ? "session.inspect" : RelayCapabilities.ManageSession)) throw new RelayException("capability_denied","Permission unavailable");
                    var control = Value(message,"control") ?? "";
                    if (!inspect && control is not ("close" or "reopen") && !(control.StartsWith("rename:",StringComparison.Ordinal) && control.Length is > 7 and <= 127)) throw new RelayException("invalid_control","Unsupported action");
                    Notice(inspect ? "Inspection explicite en cours…" : "Modification de la session en cours…");
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(180));
                        var reply = (await client.RequestAsync(new("project",ProjectAction:new(inspect ? "get_session" : "control_session",SessionId:id,ActorId:session.Scope.SenderId,Control:inspect ? "inspect" : control)),timeout.Token)).Project!;
                        if (reply.Session is { } updated)
                        {
                            var records = observations[session.Scope.ProjectId]; var index = records.Sessions.FindIndex(item=>item.Id==id);
                            if (index>=0) { records.Sessions[index]=updated; records.Revision++; }
                        }
                    }
                    await Reload(true); Notice("Session actualisée."); break;
                case "interrupt-operation":
                    var operation = Operations.FirstOrDefault(item=>item.Request.Id==id) ?? throw new RelayException("not_found","Operation unavailable");
                    var owner = Sessions.FirstOrDefault(item=>item.Id==operation.Request.SessionId);
                    if (owner is null || !Supports(owner,RelayCapabilities.ManageSession) || operation.State is not ("pending" or "running")) throw new RelayException("capability_denied","Interruption unavailable");
                    await client.RequestAsync(new("project",ProjectAction:new("control_session",SessionId:owner.Id,ActorId:owner.Scope.SenderId,Control:"interrupt")),lifetime.Token);
                    await Reload(true); Notice("Interruption demandée ; consultez l’état confirmé."); break;
                case "legacy-history": using (var archive = new LegacyHistoryWindow(data)) archive.ShowDialog(this); break;
                case "open-console": OpenConsole(); break;
                case "edit-projects": await EditProjects(); break;
                case "quit": QuitRequested?.Invoke(this,EventArgs.Empty); break;
                default: Notice("Cette action n’est pas disponible."); break;
            }
        }
        catch (OperationCanceledException) when (released) { }
        catch (Exception error) when (!released) { Notice(error is IOException && reviews.Warning is not null ? reviews.Warning : UserMessages.Error(error)); if (error is RelayException { Code: "review_stale" }) await Reload(true); }
    }
}
