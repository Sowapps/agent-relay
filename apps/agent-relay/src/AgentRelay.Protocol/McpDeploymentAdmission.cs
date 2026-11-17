namespace AgentRelay.Protocol;

public sealed class McpDeploymentException(string? deploymentId) : Exception("A relay deployment is in progress. Wait for completion; do not replay work.")
{
    public string? DeploymentId { get; } = deploymentId;
}

/// <summary>Owns the durable deployment barrier and the cross-process connection admission boundary.</summary>
/// <remarks>Presence is diagnostic, not authority. The updater closes admission before taking its live-connection snapshot.</remarks>
public sealed class McpDeploymentAdmission(string data)
{
    public static string StateDirectoryFor(string data) => Path.Combine(Path.GetFullPath(data), "transport");
    public static string MarkerFor(string data) => Path.Combine(Path.GetFullPath(data), "promotion.lock");
    public static McpDeploymentAdmission ForStateDirectory(string state) => new(Path.GetDirectoryName(Path.GetFullPath(state))!);
    public string Marker { get; } = MarkerFor(data);

    public IDisposable Acquire()
    {
        var path = Path.Combine(StateDirectoryFor(data), "admission.lock");
        McpPrivateFiles.Demand(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && Environment.TickCount64 < deadline)
            { Thread.Sleep(20); }
        }
    }

    public bool Suspended => File.Exists(Marker);
    public string? DeploymentId => Suspended ? McpPrivateFiles.ReadText(Marker, 256).Trim() : null;

    /// <summary>Call under Acquire and retain ownership until the admitted presence/request is recorded.</summary>
    /// <seealso cref="Suspend"/>
    public void DemandOpen()
    {
        if (Suspended) throw new McpDeploymentException(DeploymentId);
    }

    /// <summary>Closes admission and snapshots admitted clients in one boundary; the marker survives process exit.</summary>
    /// <seealso cref="DemandOpen"/>
    /// <seealso cref="Release"/>
    public void Suspend(string id, Action snapshot)
    {
        using var boundary = Acquire();
        if (Suspended)
        {
            if (DeploymentId != id) throw new IOException("Deployment admission belongs to another transaction.");
        }
        else
        {
            McpPrivateFiles.Demand(Marker);
            using var marker = new FileStream(Marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            marker.Write(System.Text.Encoding.UTF8.GetBytes(id));
            marker.Flush(true);
        }
        snapshot();
    }

    /// <summary>Only the owning completed update or verified rollback may reopen admission.</summary>
    /// <seealso cref="Suspend"/>
    public void Release(string id)
    {
        using var boundary = Acquire();
        if (!Suspended) return;
        if (DeploymentId != id) throw new IOException("Deployment admission belongs to another transaction.");
        File.Delete(Marker);
    }
}
