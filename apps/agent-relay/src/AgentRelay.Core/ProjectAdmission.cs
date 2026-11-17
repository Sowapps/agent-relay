namespace AgentRelay.Core;

// Short admission/lifecycle leases, not execution leases. Unrelated projects remain independent.
public sealed class ProjectAdmission
{
    private readonly object sync = new();
    private readonly HashSet<Guid> projects = [];
    private readonly Dictionary<Guid, int> work = [];
    private bool configuring;
    public IDisposable Enter(Guid projectId)
    {
        lock (sync)
        {
            if (configuring || work.ContainsKey(projectId) || !projects.Add(projectId)) throw new RelayException("busy", "A project lifecycle or admission change is in progress. Inspect its state before retrying.", false);
            return new Lease(() => { lock (sync) projects.Remove(projectId); });
        }
    }

    public IDisposable EnterWork(Guid projectId)
    {
        lock (sync)
        {
            if (configuring || projects.Contains(projectId))
                throw new RelayException("busy", "A project lifecycle or configuration change is in progress.", false);
            work[projectId] = work.GetValueOrDefault(projectId) + 1;
            return new Lease(() => ReleaseWork(projectId));
        }
    }

    private void ReleaseWork(Guid projectId)
    {
        lock (sync)
        {
            if (work[projectId] == 1) work.Remove(projectId);
            else work[projectId]--;
        }
    }
    public IDisposable Configure()
    {
        lock (sync)
        {
            if (configuring || projects.Count != 0 || work.Count != 0) throw new RelayException("busy", "Wait for project admission/lifecycle changes before replacing configuration.");
            configuring = true;
            return new Lease(() => { lock (sync) configuring = false; });
        }
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? callback = release;
        public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
    }
}
