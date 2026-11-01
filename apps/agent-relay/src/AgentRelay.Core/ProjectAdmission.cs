namespace AgentRelay.Core;

// Short admission/lifecycle leases, not execution leases. Unrelated projects remain independent.
public sealed class ProjectAdmission
{
    private readonly object sync = new();
    private readonly HashSet<Guid> projects = [];
    private bool configuring;
    public IDisposable Enter(Guid projectId)
    {
        lock (sync)
        {
            if (configuring || !projects.Add(projectId)) throw new RelayException("busy", "A project lifecycle or admission change is in progress. Inspect its state before retrying.");
            return new Lease(() => { lock (sync) projects.Remove(projectId); });
        }
    }
    public IDisposable Configure()
    {
        lock (sync)
        {
            if (configuring || projects.Count != 0) throw new RelayException("busy", "Wait for project admission/lifecycle changes before replacing configuration.");
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
