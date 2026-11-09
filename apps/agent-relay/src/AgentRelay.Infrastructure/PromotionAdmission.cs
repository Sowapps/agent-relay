using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

/// <summary>Admission barrier for a host replacement while the deployment marker is held.</summary>
internal sealed class PromotionAdmission(string data)
{
    private readonly object sync = new();
    private readonly string marker = Marker(data);
    private int active;

    public IDisposable? TryEnter()
    {
        lock (sync)
        {
            if (File.Exists(marker)) return null;
            active++;
            return new Lease(this);
        }
    }

    public IDisposable Enter() => TryEnter() ?? throw new RelayException("maintenance",
        "A relay update is in progress. New work is suspended until the host is available.", effectsPossible: false);

    internal static void DemandStartupAllowed(string data)
    {
        if (File.Exists(Marker(data)) || File.Exists(Path.Combine(data, "maintenance.json")))
            throw new RelayException("maintenance", "The relay is under maintenance. Wait for the operator to complete it before sending work.", false);
    }

    private static string Marker(string data) => Path.Combine(data, "promotion.lock");

    public void Confirm(Func<bool> workActive)
    {
        lock (sync)
        {
            if (!File.Exists(marker))
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
