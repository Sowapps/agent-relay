using System.Collections.Concurrent;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

internal sealed class WorkspaceLease : IDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly List<SemaphoreSlim> held = [];
    public static WorkspaceLease Acquire(params string[] resources)
    {
        var lease = new WorkspaceLease();
        try
        {
            foreach (var resource in resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var gate = Locks.GetOrAdd(resource, _ => new(1, 1));
                if (!gate.Wait(0)) throw new RelayException("busy", "Another relay operation is using the same workspace resource.", effectsPossible: false);
                lease.held.Add(gate);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public static string Native(WslProfile profile, string location) => "native:" + profile.Distribution.ToUpperInvariant() + ":" + profile.User + ":" + location.TrimEnd('/');
    public static string Local(string location) => "local:" + Path.GetFullPath(location).TrimEnd('\\').ToUpperInvariant();
    public void Dispose() { foreach (var gate in held) gate.Release(); held.Clear(); }
}
