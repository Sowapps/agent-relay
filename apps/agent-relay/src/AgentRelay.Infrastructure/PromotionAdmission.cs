using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

/// <summary>Admission barrier for a host replacement while the deployment marker is held.</summary>
internal sealed class PromotionAdmission(string data)
{
    private readonly object sync = new();
    private readonly McpDeploymentAdmission deployment = new(data);
    private int active;

    public IDisposable? TryEnter()
    {
        lock (sync)
        {
            using var boundary = deployment.Acquire();
            if (deployment.Suspended) return null;
            active++;
            return new Lease(this);
        }
    }

    public IDisposable Enter() => TryEnter() ?? throw new RelayException("deployment_in_progress",
        "A relay update is in progress (" + deployment.DeploymentId + "). Wait for completion; this request was not started.", effectsPossible: false);

    internal static void DemandStartupAllowed(string data)
    {
        if (File.Exists(McpDeploymentAdmission.MarkerFor(data)) || File.Exists(Path.Combine(data, "maintenance.json")))
            throw new RelayException("maintenance", "The relay is under maintenance. Wait for the operator to complete it before sending work.", false);
    }

    public void Confirm(Func<bool> workActive)
    {
        lock (sync)
        {
            if (!deployment.Suspended)
                throw new RelayException("invalid_operation", "No relay promotion owns the admission barrier.", effectsPossible: false);
            if (active != 0 || workActive())
                throw new RelayException("busy", "Admitted requests or operations are still active.", effectsPossible: false);
        }
    }

    private sealed class Lease(PromotionAdmission owner) : IDisposable
    {
        private PromotionAdmission? held = owner;

        public void Dispose()
        {
            var admission = Interlocked.Exchange(ref held, null);
            if (admission is null) return;
            lock (admission.sync) admission.active--;
        }
    }
}
