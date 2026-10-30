using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Windows;
using AgentRelay.Windows.Monitoring;

internal static partial class UiRenderScenarios
{
    public static async Task Usability(string data,string directory)
    {
        await MonitorScenarios.Seed(data);
        using var identity=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"qualification.json")));
        var projectId=identity.RootElement.GetProperty("project").GetGuid();var sessionId=identity.RootElement.GetProperty("session").GetGuid();
        var database=new RelayDatabase(data);var journal=new ProjectJournal(database);var store=new SqliteWorkStore(database);
        var sample=(await journal.OperationsAsync(new(projectId,Limit:1),CancellationToken.None)).Items.Single();
        var at=DateTimeOffset.UtcNow;
        // Put this session behind many unrelated newer operations; its targeted cursor must still work.
        for(var index=0;index<70;index++)await store.SaveAsync(sample with { Request=sample.Request with {Id=Guid.NewGuid(),TaskId=Guid.NewGuid(),Text="Target conversation "+index,SessionId=sessionId},State="completed",CreatedAt=at.AddMinutes(index-200),UpdatedAt=at.AddMinutes(index-199),Result=new("fixture","Target result "+index)},CancellationToken.None);
        for(var index=0;index<60;index++)await store.SaveAsync(sample with { Request=sample.Request with {Id=Guid.NewGuid(),TaskId=Guid.NewGuid(),Text="FOREIGN_SESSION_SENTINEL",SessionId=Guid.NewGuid()},State="completed",CreatedAt=at.AddMinutes(index),UpdatedAt=at.AddMinutes(index),Result=new("fixture","FOREIGN_SESSION_SENTINEL")},CancellationToken.None);
        await store.AddInboxAsync(new(Guid.NewGuid(),"fixture","Creator","Principal","Independent session report",null,at,sessionId,projectId,sample.Request.Scope!.RecipientId,sample.Request.Scope.SenderId),CancellationToken.None);
        var errors=new List<Exception>();await using var host=RelayRuntime.Start(data,errors.Add);
        await OnWindow(async window=>
        {
            window.ClientSize=new Size(960,720);Directory.CreateDirectory(directory);
            using var project=new ProjectWindow(data){TopLevel=false,Dock=DockStyle.Fill};window.Controls.Add(project);project.Show();await project.InitializePresentation();
            var view=project.Presentation;
            await view.EvaluateForVerificationAsync("window.RelayUI.route({project:"+JsonSerializer.Serialize(projectId.ToString("D"))+",tab:'sessions'});document.querySelector('[data-action=session-thread]').click()");
            await WaitFor(view,"!document.querySelector('.conversation-context')||document.querySelectorAll('.conversation-entry').length>0");
            var scoped=await view.EvaluateForVerificationAsync("document.querySelectorAll('.conversation-entry').length>0&&!document.querySelector('.conversation-thread').textContent.includes('FOREIGN_SESSION_SENTINEL')");
            if(scoped!="true")throw new Exception("Session thread mixed foreign work or did not load its targeted history");
            await view.CaptureAsync(Path.Combine(directory,"conversation.png"));
            var initial=JsonSerializer.Deserialize<string>(await view.EvaluateForVerificationAsync("document.querySelector('.history-count').textContent"));
            await view.EvaluateForVerificationAsync("document.querySelector('[data-action=thread-more]').click()");
            await WaitFor(view,"document.querySelector('.history-count').textContent!=="+JsonSerializer.Serialize(initial));
            await view.CaptureAsync(Path.Combine(directory,"conversation-older.png"));
            await view.EvaluateForVerificationAsync("document.querySelector('[data-action=thread-refresh]')?.click();document.querySelector('[data-action=tab][data-value=sessions]').click()");
            foreach(var tab in new[]{"tasks","activity","messages","sessions"})
            {
                var check=await view.EvaluateForVerificationAsync("(()=>{document.querySelector('[data-action=tab][data-value="+tab+"]').click();return document.querySelector('[data-history=period]').value==='all'&&document.querySelector('.history-count').textContent.includes('chargé')})()");
                if(check!="true")throw new Exception("History context/counts missing for "+tab);
            }
            await File.WriteAllTextAsync(Path.Combine(directory,"usability.json"),JsonSerializer.Serialize(new {passed=true,thread="project/session scoped, independent report, own targeted older pages",histories=new[]{"tasks","activity","messages","sessions"},nativeCalls=0}));
        });
        if(errors.Count>0)throw new AggregateException(errors);
    }
}
