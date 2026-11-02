using System.Text.Json;
using AgentRelay.Infrastructure;
using AgentRelay.Windows;

internal static partial class UiRenderScenarios
{
    public static async Task McpClients(string data, string directory)
    {
        await MonitorScenarios.Seed(data);
        var current = new McpConnectionLease(Guid.NewGuid(), Guid.NewGuid(), "Client courant", "1.1.0-rc.1", "1.1.0-rc.1", "contract", "build", "contract", "current", DateTimeOffset.UtcNow);
        var stale = current with { InstanceId = Guid.NewGuid(), Client = "Client à recharger", State = "client_reload_required", CatalogContract = "old" };
        var offline = current with { InstanceId = Guid.NewGuid(), Client = "Client absent", LastSeen = DateTimeOffset.UtcNow.AddMinutes(-1) };
        McpConnections.Write(data, current); McpConnections.Write(data, stale); McpConnections.Write(data, offline);
        var errors = new List<Exception>(); await using var host = RelayRuntime.Start(data, errors.Add);
        await OnWindow(async window =>
        {
            Directory.CreateDirectory(directory);
            using var project = new ProjectWindow(data) { TopLevel = false, Dock = DockStyle.Fill };
            window.Controls.Add(project); project.Show(); await project.InitializePresentation(); var view = project.Presentation;
            foreach (var width in new[] { 1120, 816 })
            {
                window.ClientSize = new Size(width, 728);
                await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'settings',scope:'mcp'})");
                if (await view.EvaluateForVerificationAsync("document.querySelector('main').textContent.includes('Client courant')&&document.querySelector('main').textContent.includes('Client à recharger')&&document.querySelector('main').textContent.includes('Hors ligne')&&document.querySelectorAll('[data-action=close-mcp]:disabled').length===1&&!document.querySelector('#app-nav [data-action=project].active')") != "true") throw new Exception("MCP connection states or independent general scope are incorrect.");
                await Frame(view); await view.CaptureAsync(Path.Combine(directory, "mcp-connections-" + width + ".png"));
            }
            var selector = JsonSerializer.Serialize("[data-action=close-mcp][data-id=\"" + stale.InstanceId + "\"]");
            await view.EvaluateForVerificationAsync("document.querySelector(" + selector + ").click();document.querySelector('[data-action=close-modal]').click()");
            if (McpConnections.CloseRequested(data, stale.InstanceId)) throw new Exception("Cancelled close changed a connection.");
            await view.EvaluateForVerificationAsync("document.querySelector(" + selector + ").click();document.querySelector('[data-action=confirm-close-mcp]').click()");
            for (var i = 0; i < 100 && !McpConnections.CloseRequested(data, stale.InstanceId); i++) await Task.Delay(20);
            if (!McpConnections.CloseRequested(data, stale.InstanceId) || McpConnections.CloseRequested(data, current.InstanceId)) throw new Exception("Native close targeted the wrong instance.");
            await File.WriteAllTextAsync(Path.Combine(directory, "verification.json"), JsonSerializer.Serialize(new { passed = true, scopedGeneralView = true, states = true, cancellation = true, nativeCloseMarker = true, modelCalls = 0 }));
        });
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
