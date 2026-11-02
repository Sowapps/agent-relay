using System.Text.Json;
using AgentRelay.Infrastructure;
using AgentRelay.Windows.Presentation;

internal static partial class UiRenderScenarios
{
    // Three reported regressions plus the design-only calendar boundary; no actual cleanup is invoked.
    public static Task Polish(string assets,string fixture,string output) => OnWindow(async window =>
    {
        Directory.CreateDirectory(output);window.ClientSize=new Size(816,728);
        var leases=Path.Combine(output,"lease-order");Directory.CreateDirectory(leases);
        var first=new McpConnectionLease(Guid.Parse("00000000-0000-0000-0000-000000000001"),null,"same client","1","1","hash","generation","hash","current",DateTimeOffset.UtcNow);
        var second=first with {InstanceId=Guid.Parse("00000000-0000-0000-0000-000000000002")};
        McpConnections.Write(leases,first);McpConnections.Write(leases,second);
        var order=McpConnections.List(leases).Select(x=>x.InstanceId).ToArray();
        McpConnections.Write(leases,first);
        if(!McpConnections.List(leases).Select(x=>x.InstanceId).SequenceEqual(order))throw new Exception("Native lease ordering follows the heartbeat instead of stable identity");
        using var view=new RelayWebView();window.Controls.Add(view);await view.InitializeAsync(Path.Combine(output,"browser-data"),assets);
        await view.EvaluateForVerificationAsync("window.__fixture="+File.ReadAllText(fixture)+";__fixture.mcpConnections.forEach(c=>c.client='codex-mcp-client');window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});window.RelayUI.route({view:'settings',scope:'mcp'});");
        async Task Check(string script,string message){if(await view.EvaluateForVerificationAsync(script)!="true")throw new Exception(message);}
        await Check("(()=>{const tabs=document.querySelector('.tabs'),tab=tabs.querySelector('button');return tab.getBoundingClientRect().top-tabs.getBoundingClientRect().top>=5&&parseFloat(getComputedStyle(tab).paddingTop)>=8})()","Tab top padding/focus clearance missing");
        await Check("(()=>{const b=document.querySelector('[data-action=refresh]');return b.textContent==='Actualiser la liste'&&!b.classList.contains('quiet')&&parseFloat(getComputedStyle(b).borderTopWidth)>=1})()","Refresh action is not a standard secondary button");
        await Check("(()=>{const rows=document.querySelectorAll('.archive-diagnostic');rows[1].open=true;window.__panel=rows[1];const content=document.querySelector('#app-content');content.scrollTop=100;const y=__panel.getBoundingClientRect().top;for(let n=0;n<3;n++){__fixture.mcpConnections.reverse();__fixture.mcpConnections.forEach(c=>c.lastSeen=new Date().toISOString());window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});}return __panel.isConnected&&__panel.open&&Math.abs(__panel.getBoundingClientRect().top-y)<1})()","Expanded panel moved when heartbeat order changed");
        await view.CaptureAsync(Path.Combine(output,"mcp-stable-816.png"));
        var preview=await view.EvaluateForVerificationAsync("typeof window.CleanupPreview==='object'");
        if(preview=="true")
        {
            await Check("!CleanupPreview.eligibleAt('2026-11-02T01:59').includes('log-yesterday')&&CleanupPreview.eligibleAt('2026-11-02T02:00').includes('log-yesterday')&&!CleanupPreview.eligibleAt('2026-11-02T02:00').includes('log-today')&&!CleanupPreview.eligibleAt('2026-11-02T02:00').includes('lease-live')&&!CleanupPreview.eligibleAt('2026-11-02T02:00').includes('transfer')","Local calendar/active-data protection incorrect");
            window.ClientSize=new Size(1120,728);
            await view.EvaluateForVerificationAsync("CleanupPreview.open();document.querySelector('[data-clean-action=analyze]').click()");
            await Check("document.querySelector('[data-clean-action=confirm]:not(:disabled)')!==null&&document.querySelector('#app-content').textContent.includes('01/11/2026')","Cleanup design preview or protection explanation missing");
            await view.CaptureAsync(Path.Combine(output,"cleanup-design-1120.png"));
            await view.EvaluateForVerificationAsync("document.querySelector('[data-clean-action=confirm]').click();document.querySelector('[data-clean-action=execute]').click()");
            await Check("document.querySelector('#app-content').textContent.includes('Aucun fichier réel touché')&&document.querySelector('#app-content').textContent.includes('Journaux techniques du 02/11/2026')","Fictitious cleanup removed protected data or lacked its scope label");
        }
        else await Check("!document.querySelector('[data-value=storage]')&&!document.querySelector('[data-clean-action]')","Design-only cleanup leaked into production");
        await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,tabSpacing=true,secondaryRefresh=true,stablePositionAcrossReorderedHeartbeats=true,cleanupDesign=preview=="true",realDeletions=0,modelCalls=0}));
    });
}
