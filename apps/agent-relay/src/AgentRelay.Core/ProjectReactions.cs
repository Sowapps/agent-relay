namespace AgentRelay.Core;

public sealed record ProjectReaction(Guid Id, Guid? SessionId, Guid ActorId, Guid EndpointId, Guid WorkspaceId,
    string CommandId, string Mode, bool Enabled, string Destination, string Environment);
public sealed record ProjectReactions(long Revision, ProjectReaction[] Rules);
public sealed record ReactionRun(Guid Id, Guid ProjectId, Guid SourceRequestId, Guid SourceSessionId, ProjectReaction Rule,
    DateTimeOffset CreatedAt, string State = "waiting_for_release", int? ExitCode = null, string? Diagnostic = null)
{
    public bool Pending => State is "waiting_for_release" or "ready";
    public bool Rejects => Rule.Mode == "blocking" && ExitCode == 2;
}

public interface IProjectReactionStore
{
    Task<ProjectReactions> ReadAsync(Guid project, CancellationToken token);
    Task<ProjectReactions> SaveAsync(Guid project, ProjectReactions replacement, CancellationToken token);
    Task<IReadOnlyList<ReactionRun>> ScheduleAsync(Guid? project, Guid? source, CancellationToken token);
    Task<IReadOnlyList<ReactionRun>> PendingAsync(CancellationToken token);
    Task<bool> TransitionAsync(ReactionRun previous, ReactionRun next, CancellationToken token);
}

public interface IReactionRelease
{
    Task<bool> ConfirmAsync(Guid session, Guid request, CancellationToken token);
}

/// <summary>Selects and settles reactions. Platform adapters own release evidence and command dispatch.</summary>
public sealed class ProjectReactionService(IProjectReactionStore store, ProjectPolicy policy, WorkService work, IReactionRelease release,
    Action<RelayEndpoint, ProjectReaction> validateTarget, Func<Guid, IDisposable> enterProject, IOperationRecovery? recovery = null)
{
    private static readonly TimeSpan ReleaseDeadline = TimeSpan.FromMinutes(2);

    public Task<ProjectReactions> ReadAsync(Guid project, CancellationToken token) => store.ReadAsync(project, token);

    public async Task<ProjectReactions> SaveAsync(Guid project, ProjectReactions replacement, CancellationToken token)
    {
        using var snapshot = policy.Pin();
        if (!policy.Configuration().Projects.Any(item => item.Id == project))
            throw new RelayException("not_found", "Unknown project.", false);
        if (replacement.Rules is not { Length: <= 32 } ||
            replacement.Rules.Any(item => item is null || item.Id == Guid.Empty || item.SessionId == Guid.Empty || item.ActorId == Guid.Empty ||
                item.EndpointId == Guid.Empty || item.WorkspaceId == Guid.Empty || item.Mode is not ("async" or "blocking") ||
                item.Destination is not ("windows" or "wsl") || string.IsNullOrWhiteSpace(item.Environment) ||
                item.Environment.Length > 64 || string.IsNullOrWhiteSpace(item.CommandId) || item.CommandId.Length > 100) ||
            replacement.Rules.DistinctBy(item => item.Id).Count() != replacement.Rules.Length)
            throw new RelayException("invalid_request", "Expected up to 32 distinct project reactions with explicit command targets and mode.", false);
        foreach (var rule in replacement.Rules.Where(item => item.Enabled)) DemandCommand(project, rule);
        return await store.SaveAsync(project, replacement, token);
    }

    internal void DemandCommand(Guid project, ProjectReaction rule)
    {
        using var snapshot = policy.Pin();
        var member = policy.Demand(new(rule.ActorId, [project]), project, RelayCapabilities.RunCommand);
        if (!(member.CommandIds ?? []).Contains(rule.CommandId, StringComparer.Ordinal))
            throw new RelayException("command_denied", "The reaction actor has no grant for the declared command.", false);
        policy.Workspace(new(rule.ActorId, [project]), project, rule.WorkspaceId, false, rule.EndpointId);
        var endpoint = policy.Configuration().Endpoints.Single(item => item.Id == rule.EndpointId);
        validateTarget(endpoint, rule);
    }

    public async Task<IReadOnlyList<ReactionRun>> ResultsAsync(Guid project, Guid source, CancellationToken token) =>
        await store.ScheduleAsync(project, source, token);

    public async Task DemandContinuationAsync(Guid project, Guid source, CancellationToken token)
    {
        var runs = await ResultsAsync(project, source, token);
        if (runs.Any(item => item.Rejects))
            throw new RelayException("reaction_rejected", "A linked blocking reaction returned exit code 2. Read the project reaction result.", false);
        if (runs.Any(item => item.Rule.Mode == "blocking" && item.Pending))
            throw new RelayException("reaction_pending", "A linked blocking reaction is still pending. Read its result before trying this continuation again.", false);
    }

    public async Task SweepAsync(DateTimeOffset now, CancellationToken token)
    {
        await store.ScheduleAsync(null, null, token);
        var verified = new Dictionary<Guid, bool>();
        foreach (var run in await store.PendingAsync(token))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var current = await store.ReadAsync(run.ProjectId, token);
                if (run.State == "waiting_for_release")
                {
                    if (!current.Rules.Contains(run.Rule))
                        await Settle(run, null, "reaction_reconfigured", token);
                    else
                    {
                        if (!verified.TryGetValue(run.SourceRequestId, out var confirmed))
                            verified[run.SourceRequestId] = confirmed = await release.ConfirmAsync(run.SourceSessionId, run.SourceRequestId, token);
                        if (confirmed) await store.TransitionAsync(run, run with { State = "ready" }, token);
                        else if (now - run.CreatedAt >= ReleaseDeadline)
                            await Settle(run, null, "release_unverified", token);
                    }
                    continue;
                }
                var rule = run.Rule;
                var request = new WorkRequest(run.Id, rule.EndpointId.ToString("D"), "command", CommandId: rule.CommandId,
                    Scope: new(run.ProjectId, rule.ActorId, rule.EndpointId, rule.WorkspaceId), TaskId: run.SourceRequestId, AttemptId: run.Id,
                    ExecutionBinding: rule.Destination + ":" + rule.Environment);
                WorkRecord command;
                try { command = await work.ReadAsync(run.Id, token); }
                catch (RelayException error) when (error.Code == "not_found")
                {
                    using var admission = enterProject(run.ProjectId);
                    if (!(await store.ReadAsync(run.ProjectId, token)).Rules.Contains(rule))
                    {
                        await Settle(run, null, "reaction_reconfigured", token);
                        continue;
                    }
                    DemandCommand(run.ProjectId, run.Rule);
                    command = await work.SubmitAsync(request, token, new("Project reaction", rule.CommandId));
                }
                if (command.Request != request)
                {
                    await Settle(run, null, "reaction_dispatch_conflict", token);
                    continue;
                }
                if (command.State == "unknown" && recovery is not null && rule.Destination == "wsl")
                    command = await recovery.ReconcileAsync(command, token);
                if (command.State is "completed" or "failed" or "unknown")
                {
                    var exitCode = command.State == "unknown" ? null : command.Result?.ExitCode;
                    await Settle(run, exitCode, command.ErrorCode ?? (exitCode is null or 0 ? null : "reaction_nonzero_exit"), token);
                }
            }
            catch (RelayException error) when (error.Code == "busy")
            {
                if (now - run.CreatedAt >= ReleaseDeadline) await Settle(run, null, "reaction_capacity_timeout", token);
            }
            catch (Exception error) when (error is RelayException or IOException or TimeoutException)
            {
                if (run.State != "waiting_for_release" || now - run.CreatedAt >= ReleaseDeadline)
                    await Settle(run, null, error is RelayException relay ? relay.Code : "reaction_unconfirmed", token);
            }
        }
    }

    private Task<bool> Settle(ReactionRun run, int? exitCode, string? diagnostic, CancellationToken token) =>
        store.TransitionAsync(run, run with { State = "settled", ExitCode = exitCode, Diagnostic = diagnostic }, token);
}
