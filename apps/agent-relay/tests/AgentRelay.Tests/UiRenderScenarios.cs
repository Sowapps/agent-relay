using System.Text.Json;
using AgentRelay.Windows;
using AgentRelay.Windows.Presentation;

internal static partial class UiRenderScenarios
{
    public static Task Run(string data,string directory) => OnWindow(async window =>
    {
        using var project = new ProjectWindow(data) { TopLevel=false, Dock=DockStyle.Fill };
        window.Controls.Add(project); project.Show(); await project.InitializePresentation();
        if (!project.Presentation.IsReady) throw new Exception("Native presentation unavailable");
        await project.Reload(true);
        await Verify(project.Presentation,directory,null);
        var projectId=await project.Presentation.EvaluateForVerificationAsync("document.querySelector('[data-action=project]')?.dataset.project");
        if(projectId!="null")
        {
            await project.Presentation.EvaluateForVerificationAsync("window.RelayUI.route({project:"+projectId+"})");
            await project.Presentation.EvaluateForVerificationAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))");
            await project.Presentation.CaptureAsync(Path.Combine(directory,"project-real.png"));
            var hasConversation=await project.Presentation.EvaluateForVerificationAsync("document.querySelector('[data-action=tab][data-value=sessions]').click();(()=>{const button=document.querySelector('[data-action=session-thread]');if(!button)return false;button.click();return true})()");
            if(hasConversation=="true")
            {
                await WaitFor(project.Presentation,"!!document.querySelector('.conversation-context')&&!document.querySelector('main').textContent.includes('Chargement de l’historique de cette session…')");
                await project.Presentation.CaptureAsync(Path.Combine(directory,"conversation-real.png"));
            }
        }
    });

    public static async Task Review(string data,string directory)
    {
        await MonitorScenarios.Seed(data);
        var errors=new List<Exception>();
        await using var host=AgentRelay.Infrastructure.RelayRuntime.Start(data,errors.Add);
        await OnWindow(async window=>
        {
            Directory.CreateDirectory(directory); window.ClientSize=new Size(960,648);
            using(var project=new ProjectWindow(data){TopLevel=false,Dock=DockStyle.Fill})
            {
                window.Controls.Add(project);project.Show();await project.InitializePresentation();
                var view=project.Presentation;
                await view.CaptureAsync(Path.Combine(directory,"before-review.png"));
                var alertShape=await view.EvaluateForVerificationAsync("(()=>{const button=document.querySelector('.attention-box [data-action=mark-reviewed]');return !!button&&button.title.startsWith('Marquer comme examiné.')&&button.title.includes('Activité')&&button.title.includes('Tout l’historique')&&button.getAttribute('aria-label')==='Marquer comme examiné'&&button.textContent.trim()===''&&!!button.querySelector('svg')&&button.getBoundingClientRect().width>=32})()");
                if(alertShape!="true")throw new Exception("Alert review must be an accessible titled icon-only cross");
                await view.EvaluateForVerificationAsync("document.querySelector('.attention-box [data-action=operation]').click()");
                var drawerShape=await view.EvaluateForVerificationAsync("(()=>{const button=document.querySelector('.detail-footer [data-action=mark-reviewed]');if(!button)return false;const style=getComputedStyle(button);return button.textContent==='Marquer comme examiné'&&button.classList.contains('primary')&&parseFloat(style.borderTopWidth)>=1&&style.backgroundColor!=='rgba(0, 0, 0, 0)'&&style.color!==style.backgroundColor&&button.getBoundingClientRect().height>=32})()");
                if(drawerShape!="true")throw new Exception("Drawer review must remain a contrasted bordered text button");
                await view.CaptureAsync(Path.Combine(directory,"drawer-review.png"));
                await view.EvaluateForVerificationAsync("document.querySelector('.detail-head [data-action=close-detail]').click()");
                var clicked=await view.EvaluateForVerificationAsync("(()=>{const button=document.querySelector('.attention-box [data-action=mark-reviewed]');if(!button)return null;const id=button.dataset.id;button.click();return id})()");
                if(clicked=="null")throw new Exception("Native review action missing");
                var operationId=JsonSerializer.Deserialize<string>(clicked)!;
                var confirmed=false;
                for(var attempt=0;attempt<160;attempt++)
                {
                    confirmed=await view.EvaluateForVerificationAsync("!document.querySelector('.attention-box [data-action=mark-reviewed][data-id=\""+operationId+"\"]')")=="true";
                    if(confirmed&&File.Exists(Path.Combine(data,"attention-reviews.json")))break;
                    await Task.Delay(50);
                }
                if(!confirmed||!File.Exists(Path.Combine(data,"attention-reviews.json")))
                {
                    await File.WriteAllTextAsync(Path.Combine(directory,"failure-state.json"),await view.EvaluateForVerificationAsync("document.body.textContent"));
                    throw new Exception("Native review action was not durably acknowledged");
                }
                await view.CaptureAsync(Path.Combine(directory,"after-review.png"));
                window.Controls.Remove(project);
            }
            using var reopened=new ProjectWindow(data){TopLevel=false,Dock=DockStyle.Fill};
            window.Controls.Add(reopened);reopened.Show();await reopened.InitializePresentation();
            var retained=new AttentionReviews(data);
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"attention-reviews.json")));
            var key=document.RootElement.GetProperty("entries").EnumerateObject().Single().Name;
            var id=key.Split(':')[1];
            var history=await new AgentRelay.Infrastructure.SqliteWorkStore(new AgentRelay.Infrastructure.RelayDatabase(data)).FindAsync(Guid.Parse(id),CancellationToken.None);
            if(history is null||history.State!="unknown"||!retained.IsReviewed(history))throw new Exception("Native review changed the journal outcome or failed to bind its revision");
            await reopened.Presentation.EvaluateForVerificationAsync("window.RelayUI.route({project:"+JsonSerializer.Serialize(key.Split(':')[0])+",tab:'activity'})");
            var persisted=await reopened.Presentation.EvaluateForVerificationAsync("document.querySelector('[data-action=operation][data-id=\""+id+"\"]').textContent.includes('examiné')&&!document.querySelector('.attention-box [data-action=mark-reviewed][data-id=\""+id+"\"]')");
            if(persisted!="true")throw new Exception("Native review did not survive window restart or history row disappeared");
            await reopened.Presentation.CaptureAsync(Path.Combine(directory,"reopened-history.png"));
            await File.WriteAllTextAsync(Path.Combine(directory,"review-verification.json"),JsonSerializer.Serialize(new {passed=true,scenario="Actual controller to native handler, passive retained read, atomic review file, window restart and preserved history",operation=id}));
        });
        if(errors.Count>0)throw new AggregateException(errors);
    }

    public static Task Conformance(string fixture,string cases,string directory) => OnWindow(async window =>
    {
        using var view = new RelayWebView(); window.Controls.Add(view);
        await view.InitializeAsync(Path.Combine(directory,"browser-data"));
        var snapshot=JsonDocument.Parse(File.ReadAllText(fixture)).RootElement.Clone();
        view.Send(new {type="snapshot",data=snapshot});
        await Verify(view,directory,JsonDocument.Parse(File.ReadAllText(cases)).RootElement.Clone(),window,snapshot);
    });

    private static async Task Verify(RelayWebView view,string directory,JsonElement? cases,Form? window=null,JsonElement? fixture=null)
    {
        Directory.CreateDirectory(directory); var checks=new List<object>();
        var routes=cases is { } specification ? specification.GetProperty("cases").EnumerateArray().Select(x=>x.Clone()).ToArray() : [JsonSerializer.SerializeToElement(new {name="general"}),JsonSerializer.SerializeToElement(new {name="settings",view="settings"}),JsonSerializer.SerializeToElement(new {name="diagnostics",view="diagnostics"})];
        foreach(var size in cases is null ? new[] { new Size(1120,728) } : new[] { new Size(1120,728),new Size(960,648) })
        {
            if(window is not null) window.ClientSize=size;
            foreach(var route in routes)
            {
                var name=route.GetProperty("name").GetString()!;
                await view.EvaluateForVerificationAsync("window.RelayUI.route("+route.GetRawText()+");");
                await view.EvaluateForVerificationAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))");
                var json=await view.EvaluateForVerificationAsync("(()=>{const stat=document.querySelector('.stats'),panel=document.querySelector('.detail-panel'),main=document.querySelector('.app-scroll');return {width:innerWidth,height:innerHeight,heading:document.querySelector('h1')?.textContent,drawer:!!panel,backdrop:!!document.querySelector('.detail-backdrop'),metricsContained:!stat||[...stat.children].every(x=>x.getBoundingClientRect().bottom<=stat.getBoundingClientRect().bottom+1),scrollWidth:main.scrollWidth,clientWidth:main.clientWidth,mainTop:main.scrollTop,content:main.textContent.length}})()");
                using var result=JsonDocument.Parse(json);var layout=result.RootElement;
                if(name=="general" && !(await view.EvaluateForVerificationAsync("document.querySelector('[data-action=general-feed].active')?.dataset.value")).Contains("tasks"))throw new Exception("General feed leaked from previous route");
                if(layout.GetProperty("content").GetInt32()<20||!layout.GetProperty("metricsContained").GetBoolean()||layout.GetProperty("scrollWidth").GetInt32()>layout.GetProperty("clientWidth").GetInt32()+1)throw new Exception("Blank, clipped or horizontally overflowing view: "+name);
                if(route.TryGetProperty("detail",out _)&&(!layout.GetProperty("drawer").GetBoolean()||!layout.GetProperty("backdrop").GetBoolean()))throw new Exception("Missing overlay: "+name);
                if(window is not null&&(layout.GetProperty("width").GetInt32()!=size.Width||layout.GetProperty("height").GetInt32()!=size.Height))throw new Exception("Actual client viewport differs from comparison size");
                await view.CaptureAsync(Path.Combine(directory,name+"-"+size.Width+".png"));checks.Add(new {name,layout=layout.Clone()});
            }
        }
        if(cases is not null)
        {
            var stability="(()=>{window.RelayUI.route({project:'atelier',tab:'activity'});const input=document.getElementById('activity-search');input.focus();input.value='structure';input.dispatchEvent(new Event('input',{bubbles:true}));const scroll=document.querySelector('.app-scroll');scroll.scrollTop=50;const before=scroll.scrollTop;window.RelayUI.receive({type:'snapshot',data:__FIXTURE__});return document.activeElement.id==='activity-search'&&document.querySelector('.app-scroll').scrollTop===before})()";
            if(await view.EvaluateForVerificationAsync(stability.Replace("__FIXTURE__",fixture!.Value.GetRawText(),StringComparison.Ordinal))!="true")throw new Exception("Search focus or scroll was lost");
            var reviewed=System.Text.Json.Nodes.JsonNode.Parse(fixture.Value.GetRawText())!;
            var checkOperation=reviewed["operations"]![0]!;
            checkOperation["state"]="failed";checkOperation["reviewToken"]="revision-one";checkOperation["reviewable"]=true;checkOperation["reviewed"]=false;
            var before=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+reviewed.ToJsonString()+"});window.RelayUI.route({project:'atelier',tab:'activity'});document.querySelector('[data-action=mark-reviewed]')!==null");
            if(before!="true")throw new Exception("Review action missing from attention alert");
            var retained=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'operation-review',id:'op1',reviewToken:'revision-one'});!document.querySelector('.attention-box')&&document.querySelector('[data-action=operation][data-id=op1]').textContent.includes('Échec')");
            if(retained!="true")throw new Exception("Review removed history or left alert visible");
            var shortcut=await view.EvaluateForVerificationAsync("document.querySelector('[data-action=history-operation]').click();document.querySelector('[data-history=period]').value==='all'&&!!document.querySelector('.detail-panel')");
            if(shortcut!="true")throw new Exception("Reviewed operation cannot be found from the success notice");
            await view.EvaluateForVerificationAsync("document.querySelector('[data-action=close-detail]').click()");
            var filter=await view.EvaluateForVerificationAsync("document.querySelector('[data-action=filter][data-value=follow]').click();!document.querySelector('[data-action=operation][data-id=op1]')");
            if(filter!="true")throw new Exception("Reviewed operation remains in follow-up filter");
            checkOperation["reviewToken"]="revision-two";checkOperation["reviewed"]=false;
            var reappeared=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+reviewed.ToJsonString()+"});window.RelayUI.route({project:'atelier',tab:'activity'});document.querySelector('.attention-box')!==null");
            if(reappeared!="true")throw new Exception("Updated operation did not reappear");
            var historical=System.Text.Json.Nodes.JsonNode.Parse(fixture.Value.GetRawText())!;
            historical["referenceDate"]="2040-01-01";
            foreach(var tab in new[]{"tasks","activity","messages","sessions"})
            {
                var restore=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+historical.ToJsonString()+"});window.RelayUI.route({project:'atelier',tab:'"+tab+"'});(()=>{const rows=()=>document.querySelectorAll('.activity-row,.message-preview,.session-card').length;const period=document.querySelector('[data-history=period]');if(period.value!=='all'||!rows())return false;period.value='today';period.dispatchEvent(new Event('change',{bubbles:true}));if(rows()||!document.querySelector('.empty').textContent.includes('filtres'))return false;document.querySelector('[data-action=clear-history]').click();return document.querySelector('[data-history=period]').value==='all'&&rows()>0})()");
                if(restore!="true")throw new Exception("Historical collection period/reset is inconsistent: "+tab);
            }
            foreach(var feed in new[]{"tasks","operations","messages"})
            {
                var general=await view.EvaluateForVerificationAsync("window.RelayUI.route({feed:'"+feed+"'});document.querySelector('[data-history=period]').value==='all'&&document.querySelectorAll('.activity-row,.message-preview').length>0");
                if(general!="true")throw new Exception("General history is inconsistent: "+feed);
            }
            var isolated=System.Text.Json.Nodes.JsonNode.Parse(fixture.Value.GetRawText())!;
            var foreign=isolated["operations"]![0]!.DeepClone();foreign["id"]="foreign-operation";foreign["project"]="catalogue";foreign["request"]="FOREIGN_PROJECT_SENTINEL";isolated["operations"]!.AsArray().Add(foreign);
            var independent=isolated["messages"]![0]!.DeepClone();independent["id"]="independent-thread-message";independent["text"]="Independent conversation report";independent["kind"]="independent";independent["operation"]=null;independent["taskId"]=null;isolated["messages"]!.AsArray().Add(independent);
            var conversation=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+isolated.ToJsonString()+"});window.RelayUI.route({project:'atelier',session:'s1'});window.__qaConversationText=document.querySelector('.conversation-thread').textContent;window.__qaConversationText.includes('Independent conversation report')&&!window.__qaConversationText.includes('FOREIGN_PROJECT_SENTINEL')");
            if(conversation!="true")throw new Exception("Conversation invented linkage or mixed another project");
            var unsafeData=System.Text.Json.Nodes.JsonNode.Parse(fixture.Value.GetRawText())!;
            unsafeData["messages"]![0]!["text"]="<img src=x onerror=alert(1)> · <script>unexpected()</script>";
            var safety=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+unsafeData.ToJsonString()+"});window.RelayUI.route({project:'atelier',tab:'messages',detail:'message',id:'m1'});document.querySelectorAll('img[src=x]').length===0&&document.querySelector('.chat-bubble').textContent.includes('<script>')");
            if(safety!="true")throw new Exception("Untrusted message was not rendered literally");
            var empty=System.Text.Json.Nodes.JsonNode.Parse(fixture.Value.GetRawText())!;
            foreach(var key in new[]{"projects","participants","sessions","operations","tasks","messages","connections","connectors"})empty[key]=new System.Text.Json.Nodes.JsonArray();
            var emptyState=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+empty.ToJsonString()+"});window.RelayUI.route({view:'settings'});document.querySelector('h1').textContent==='Configuration'");
            if(emptyState!="true")throw new Exception("Empty configuration cannot render settings");
            empty["relay"]="offline";empty["relayError"]="Relais local injoignable · vérification";
            var offline=await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:"+empty.ToJsonString()+"});window.RelayUI.route({view:'diagnostics'});document.querySelector('main').textContent.includes('Relais local injoignable')");
            if(offline!="true")throw new Exception("Offline diagnostics hides the actual cause");
        }
        await File.WriteAllTextAsync(Path.Combine(directory,"render-verification.json"),JsonSerializer.Serialize(new {passed=true,runtime=view.RuntimeVersion,surface="Production C# RelayWebView own capture; no desktop capture or input",checks},new JsonSerializerOptions {WriteIndented=true}));
    }

    private static async Task WaitFor(AgentRelay.Windows.Presentation.RelayWebView view,string predicate)
    {
        for(var attempt=0;attempt<200;attempt++){if(await view.EvaluateForVerificationAsync(predicate)=="true")return;await Task.Delay(40);}
        throw new Exception("Native UI condition did not settle: "+predicate);
    }

    private static Task OnWindow(Func<Form,Task> verify)
    {
        var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using var window=new Form {Text="Agent Relay · Render verification",ClientSize=new Size(1120,728)};
            window.Shown+=async(_,_)=>{try{await verify(window);complete.TrySetResult();}catch(Exception error){complete.TrySetException(error);}finally{window.Close();}};
            Application.Run(window);
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();return complete.Task;
    }
}
