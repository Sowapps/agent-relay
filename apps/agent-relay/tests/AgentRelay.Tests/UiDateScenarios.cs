using System.Text.Json;
using AgentRelay.Windows;
using AgentRelay.Windows.Presentation;

internal static partial class UiRenderScenarios
{
    public static async Task Dates(string data, string directory)
    {
        DateDisplayScenarios.Run();
        await MonitorScenarios.Seed(data);
        var errors = new List<Exception>();
        await using var host = AgentRelay.Infrastructure.RelayRuntime.Start(data, errors.Add);
        await OnWindow(async window =>
        {
            Directory.CreateDirectory(directory);
            using var project = new ProjectWindow(data) { TopLevel = false, Dock = DockStyle.Fill };
            window.Controls.Add(project); project.Show(); await project.InitializePresentation();
            var view = project.Presentation;
            var formats = await view.EvaluateForVerificationAsync("(()=>{const c=window.RelayComponents,a=new Date(2026,0,2,0,5,9).toISOString(),b=new Date(2026,11,31,23,6,2).toISOString();return c.formatDate(a)==='02/01/2026'&&c.formatDate('2026-01-02')==='02/01/2026'&&c.formatTime(a)==='00:05'&&c.formatTime(a,true)==='00:05:09'&&c.formatTime(b,true)==='23:06:02'&&c.formatDate(null)!=='01/01/1970'&&c.formatDate('unavailable')===c.formatDate(null)})()");
            if (formats != "true") throw new Exception("Browser date format/padding/local-day semantics failed.");
            var projectId = await view.EvaluateForVerificationAsync("document.querySelector('[data-action=project]').dataset.project");
            var checks = new List<object>();
            foreach (var width in new[] { 1120, 816 })
            {
                window.ClientSize = new Size(width, 728);
                foreach (var tab in new[] { "general", "tasks", "activity", "messages", "sessions" })
                {
                    var route = tab == "general" ? "{view:'general'}" : "{project:" + projectId + ",tab:'" + tab + "'}";
                    await view.EvaluateForVerificationAsync("window.RelayUI.route(" + route + ")");
                    await view.EvaluateForVerificationAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))");
                    var valid = await view.EvaluateForVerificationAsync("(()=>{const main=document.querySelector('.app-scroll');const date=/^\\d{2}\\/\\d{2}\\/\\d{4}$/;return main.scrollWidth<=main.clientWidth+1&&[...main.querySelectorAll('time')].every(t=>date.test(t.textContent.trim()))&&[...main.querySelectorAll('.message-meta>span:last-child')].every(t=>/^\\d{2}\\/\\d{2}\\/\\d{4} · \\d{2}:\\d{2}(?!:)/.test(t.textContent))&&[...main.querySelectorAll('.session-card p')].every(t=>/\\d{2}\\/\\d{2}\\/\\d{4}$/.test(t.textContent)||t.textContent.includes('non disponible'))})()");
                    if (valid != "true") throw new Exception("Incomplete date or overflowing layout: " + tab + " at " + width);
                    var separated = await view.EvaluateForVerificationAsync("[...document.querySelectorAll('.activity-row time')].every(t=>!t.getClientRects().length||(t.scrollWidth<=t.clientWidth+1&&t.getBoundingClientRect().right<=t.nextElementSibling.getBoundingClientRect().left-2))");
                    if (separated != "true") throw new Exception("Full date overlaps its navigation icon: " + tab + " at " + width);
                    await view.CaptureAsync(Path.Combine(directory, tab + "-" + width + ".png"));
                    checks.Add(new { tab, width });
                }
            }
            await view.EvaluateForVerificationAsync("document.querySelector('[data-action=session-thread]').click()");
            await WaitFor(view, "!!document.querySelector('.conversation-entry time')&&!document.querySelector('main').textContent.includes('Chargement de l’historique de cette session…')");
            if (await view.EvaluateForVerificationAsync("[...document.querySelectorAll('.conversation-entry time')].every(t=>/^\\d{2}\\/\\d{2}\\/\\d{4} · \\d{2}:\\d{2}:\\d{2}$/.test(t.textContent))") != "true") throw new Exception("Conversation date/seconds formatting failed.");
            await view.CaptureAsync(Path.Combine(directory, "conversation.png"));
            await File.WriteAllTextAsync(Path.Combine(directory, "verification.json"), JsonSerializer.Serialize(new { passed = true, checks, localTime = true, optionalSeconds = true, relativeLabels = false, modelCalls = 0 }));
        });
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
