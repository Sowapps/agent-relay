using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Windows;
using AgentRelay.Windows.Presentation;

internal static partial class UiRenderScenarios
{
    public static async Task ProjectSettings(string data, string directory)
    {
        await MonitorScenarios.Seed(data);
        var settings = new ProjectSettingsStore(data); var configuration = settings.Load();
        var archivedId = Guid.NewGuid(); var first = configuration.Projects[0]; var owner = configuration.Endpoints[0].Id;
        var privateWorkspace = new RelayWorkspace(Guid.NewGuid(),owner,"Espace du premier projet","memory://first-project-only");
        configuration = configuration with { Workspaces=configuration.Workspaces.Append(privateWorkspace).ToArray(),Memberships=configuration.Memberships.Select(member=>member.ProjectId==first.Id&&member.EndpointId==owner?member with {Workspaces=member.Workspaces.Append(new(privateWorkspace.Id,true)).ToArray()}:member).ToArray() };
        settings.Save(configuration with { Projects=configuration.Projects.Append(new(archivedId,"Projet à conserver")).ToArray(),
            Memberships=configuration.Memberships.Append(new(archivedId,owner,"main",[RelayCapabilities.ReadHistory,RelayCapabilities.ManageProject],[])).ToArray() });
        var store = new SqliteWorkStore(new RelayDatabase(data)); var now = DateTimeOffset.UtcNow;
        var retained = new WorkRecord(new(Guid.NewGuid(),owner.ToString("D"),"command",Scope:new(archivedId,owner,configuration.Endpoints[1].Id),TaskId:Guid.NewGuid()),"completed",now,now,Result:new("fixture","Historique conservé"));
        await store.SaveAsync(retained,CancellationToken.None);
        var errors = new List<Exception>(); await using var host = RelayRuntime.Start(data,errors.Add);
        await OnWindow(async window=>
        {
            Directory.CreateDirectory(directory);
            using var project = new ProjectWindow(data){TopLevel=false,Dock=DockStyle.Fill};window.Controls.Add(project);project.Show();await project.InitializePresentation();
            var view=project.Presentation;var id=JsonSerializer.Serialize(archivedId.ToString("D"));var firstId=JsonSerializer.Serialize(first.Id.ToString("D"));
            if(await view.EvaluateForVerificationAsync("document.querySelector('.stat strong').textContent==='3'")!="true")throw new Exception("Active-project count conflates configured state and running work.");
            string stateSelector=JsonSerializer.Serialize("[data-action=project-state][data-project=\""+archivedId+"\"]"),navSelector=JsonSerializer.Serialize("#app-nav [data-project=\""+archivedId+"\"]"),openSelector=JsonSerializer.Serialize(".project-directory [data-action=project][data-project=\""+archivedId+"\"]"),firstStateSelector=JsonSerializer.Serialize("[data-action=project-state][data-project=\""+first.Id+"\"]"),operationSelector=JsonSerializer.Serialize("[data-action=operation][data-id=\""+retained.Request.Id+"\"]");
            foreach(var width in new[]{1120,816})
            {
                window.ClientSize=new Size(width,728);
                await view.EvaluateForVerificationAsync("window.RelayUI.route({project:"+firstId+",tab:'settings'})");
                if(await view.EvaluateForVerificationAsync("document.querySelector('#app-nav [data-action=project].active')?.dataset.project==="+firstId+"&&!document.querySelector('.settings-nav')&&document.querySelector('main').textContent.includes('Réglages de ')")!="true")throw new Exception("Project settings lost project navigation/context.");
                await Frame(view);await view.CaptureAsync(Path.Combine(directory,"project-settings-"+width+".png"));
                await view.EvaluateForVerificationAsync("document.querySelector('#app-nav [data-view=settings]').click()");
                if(await view.EvaluateForVerificationAsync("document.querySelector('h1').textContent==='Réglages généraux'&&!document.querySelector('.settings-nav').textContent.includes('Projet :')&&!document.querySelector('#app-nav [data-action=project].active')&&!document.querySelector('main').textContent.includes("+JsonSerializer.Serialize(first.Name)+")")!="true")throw new Exception("General settings retained hidden project context.");
                await Frame(view);await view.CaptureAsync(Path.Combine(directory,"general-settings-"+width+".png"));
                await view.EvaluateForVerificationAsync("document.querySelector('[data-action=settings-scope][data-value=projects]').click()");
                if(await view.EvaluateForVerificationAsync("document.querySelectorAll('.project-directory-row').length===3&&!document.querySelector('main input,main select,main textarea')&&document.querySelector('.app-scroll').scrollWidth<=document.querySelector('.app-scroll').clientWidth+1")!="true")throw new Exception("General project directory contains configuration forms or overflows.");
                await Frame(view);await view.CaptureAsync(Path.Combine(directory,"project-directory-"+width+".png"));
            }
            var action="document.querySelector("+stateSelector+").click()";
            await view.EvaluateForVerificationAsync(action);
            await view.EvaluateForVerificationAsync("document.querySelector('[data-action=close-modal]').click()");
            if(!settings.Load().Projects.Single(p=>p.Id==archivedId).Enabled)throw new Exception("Cancelled archive changed state.");
            await view.EvaluateForVerificationAsync(action+";document.querySelector('[data-action=confirm-project-state]').click()");
            await WaitFor(view,"!document.querySelector("+navSelector+")&&document.querySelector("+stateSelector+").textContent==='Réactiver'");
            if(settings.Load().Projects.Single(p=>p.Id==archivedId).Enabled)throw new Exception("Native archive was not persisted.");
            await Frame(view);await view.CaptureAsync(Path.Combine(directory,"archived-directory.png"));
            await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'general'})");
            if(await view.EvaluateForVerificationAsync("document.querySelector('.stat strong').textContent==='2'")!="true")throw new Exception("Archived project remains in the active-project count.");
            await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'settings',scope:'projects'})");
            await view.EvaluateForVerificationAsync("document.querySelector("+openSelector+").click();document.querySelector('[data-action=tab][data-value=activity]').click()");
            if(await view.EvaluateForVerificationAsync("document.querySelector('.info-block').textContent.includes('Projet archivé')&&!!document.querySelector("+operationSelector+")")!="true")throw new Exception("Archived project could not be opened with retained history.");
            await Frame(view);await view.CaptureAsync(Path.Combine(directory,"archived-history.png"));
            await view.EvaluateForVerificationAsync("document.querySelector('[data-action=tab][data-value=settings]').click()");
            if(await view.EvaluateForVerificationAsync("!document.querySelector('main').textContent.includes('memory://first-project-only')")!="true")throw new Exception("Shared participant leaked another project's workspace into settings.");
            await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'settings',scope:'projects'});"+action+";document.querySelector('[data-action=confirm-project-state]').click()");
            await WaitFor(view,"!!document.querySelector("+navSelector+")&&document.querySelector("+stateSelector+").textContent==='Archiver'");
            if(!settings.Load().Projects.Single(p=>p.Id==archivedId).Enabled)throw new Exception("Reactivation did not persist.");
            await view.EvaluateForVerificationAsync("document.querySelector("+firstStateSelector+").click();document.querySelector('[data-action=confirm-project-state]').click()");
            await WaitFor(view,"!!document.querySelector('.app-modal-card [data-action=project-state-refresh]')");
            if(!settings.Load().Projects.Single(p=>p.Id==first.Id).Enabled)throw new Exception("Uncertain project was archived.");
            await Frame(view);await view.CaptureAsync(Path.Combine(directory,"archive-refused.png"));
            if((await store.FindAsync(retained.Request.Id,CancellationToken.None))!=retained)throw new Exception("Lifecycle UI altered retained work.");
            await File.WriteAllTextAsync(Path.Combine(directory,"verification.json"),JsonSerializer.Serialize(new {passed=true,generalProjectSeparation=true,noConfigurationFormsInDirectory=true,archiveReactivate=true,historyRetained=true,refusalForUncertainWork=true,nativeCalls=0,modelCalls=0}));
        });
        if(errors.Count>0)throw new AggregateException(errors);
    }
    private static Task<string> Frame(RelayWebView view)=>view.EvaluateForVerificationAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))");
}
