using System.Text.Json;
using AgentRelay.Windows.Presentation;

internal static partial class UiRenderScenarios
{
    // One bounded own-surface scenario; runs against the prototype draft before promotion and production afterwards.
    public static Task Feedback(string assets,string fixture,string output,string label) => OnWindow(async window =>
    {
        Directory.CreateDirectory(output);
        using var view=new RelayWebView();window.Controls.Add(view);
        await view.InitializeAsync(Path.Combine(output,"browser-data"),assets);
        await view.EvaluateForVerificationAsync("window.__fixture="+File.ReadAllText(fixture)+";window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});");
        async Task Check(string expression,string reason){if(await view.EvaluateForVerificationAsync(expression)!="true")throw new Exception(reason);}
        await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'settings',scope:'mcp'});window.__opened=document.querySelectorAll('.archive-diagnostic');__opened.forEach(el=>el.open=true);window.__first=__opened[0];");
        await Check("(()=>{for(let n=0;n<3;n++){__fixture.mcpConnections[0].lastSeen=new Date().toISOString();window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});}return document.querySelectorAll('.archive-diagnostic[open]').length===2&&window.__first===document.querySelector('.archive-diagnostic')})()","MCP panels closed or were replaced by a background refresh");
        await Check("(()=>{const survivor=document.querySelectorAll('.archive-diagnostic')[1],next=structuredClone(__fixture);next.mcpConnections.shift();window.RelayUI.receive({type:'snapshot',data:next});const kept=document.querySelector('.archive-diagnostic')===survivor&&survivor.open;window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});return kept})()","Removing another connection lost the surviving panel identity");
        await Check("document.querySelector('.tabs [aria-selected=true]').textContent==='Connexions MCP'&&document.querySelector('[role=tabpanel]').getAttribute('aria-labelledby')==='tab-settings-scope-mcp'","Settings tab composition mismatch");
        await Check("document.querySelector('.breadcrumb').textContent.includes('Réglages')&&!document.querySelector('.breadcrumb').textContent.startsWith('Général')","Artificial breadcrumb parent");
        await view.EvaluateForVerificationAsync("document.querySelector('#tab-settings-scope-mcp').dispatchEvent(new KeyboardEvent('keydown',{key:'Home',bubbles:true}))");
        await Check("document.activeElement.id==='tab-settings-scope-general'&&document.activeElement.getAttribute('aria-selected')==='true'","Tab keyboard/focus mismatch");
        foreach(var width in new[]{1120,816})
        {
            window.ClientSize=new Size(width,728);
            foreach(var route in new[]{"{view:'settings',scope:'mcp'}","{view:'diagnostics'}","{project:'atelier',tab:'settings'}"})
            {
                await view.EvaluateForVerificationAsync("window.RelayUI.route("+route+");");
                await Check("document.querySelector('#app-content').scrollWidth<=document.querySelector('#app-content').clientWidth+1","Content overflows horizontally");
                await view.EvaluateForVerificationAsync("new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))");
                var name=route.Contains("diagnostics")?"diagnostics":route.Contains("atelier")?"project-settings":"mcp";
                await view.CaptureAsync(Path.Combine(output,label+"-"+name+"-"+width+".png"));
            }
        }
        await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'diagnostics'});window.__technical=document.querySelector('[data-key=relay-technical]');__technical.open=true;window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});");
        await Check("window.__technical===document.querySelector('[data-key=relay-technical]')&&__technical.open&&!document.querySelector('#app-content').textContent.includes('2 instances détectées')","Diagnostic expansion or instance claim regression");
        await Check("(()=>{const dot=document.querySelector('.dot.declared');return !!dot&&getComputedStyle(dot).backgroundColor!==getComputedStyle(document.querySelector('.dot.ready')).backgroundColor})()","Declared status has misleading success color");
        await view.EvaluateForVerificationAsync("window.RelayUI.route({project:'atelier',tab:'tasks'});const field=document.querySelector('#activity-search');field.value='page';field.dispatchEvent(new Event('input',{bubbles:true}));window.__field=document.querySelector('#activity-search');__field.focus();__field.setSelectionRange(1,3);window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});");
        await Check("document.activeElement===window.__field&&__field.value==='page'&&__field.selectionStart===1&&__field.selectionEnd===3","Search draft/focus/selection lost");
        await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'settings',scope:'projects'});document.querySelector('[data-action=project-state]').click();");
        await Check("(()=>{const a=document.querySelector('.app-modal-card .actions'),p=a.previousElementSibling;return a.getBoundingClientRect().top-p.getBoundingClientRect().bottom>=18})()","Modal action spacing missing");
        await view.CaptureAsync(Path.Combine(output,label+"-archive.png"));
        await Check("(()=>{const changed=structuredClone(__fixture);changed.projects[0].archiveRequested=true;window.RelayUI.receive({type:'snapshot',data:changed});document.querySelector('[data-action=confirm-project-state]').click();return document.querySelector('.app-modal-card').textContent.includes('Le projet a changé')})()","A stale archive confirmation was submitted without explanation");
        await view.EvaluateForVerificationAsync("window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)})");
        await view.EvaluateForVerificationAsync("document.querySelector('[data-action=close-modal]').click();window.RelayUI.route({project:'atelier',tab:'sessions'});document.querySelector('[data-action=session]').click();document.querySelector('[data-action=rename-session]').click();document.querySelector('#session-name').value='Saisie conservée';document.querySelector('[data-action=confirm-rename]').click();window.RelayUI.receive({type:'action-result',success:false,text:'La session a changé. Votre saisie est conservée.'});window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});");
        await Check("document.querySelector('#session-name').value==='Saisie conservée'&&document.querySelector('.app-modal-card [role=alert]').textContent.includes('session a changé')","Rejected rename lost its draft or useful error");
        await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,label,expandedPanels=true,nodeIdentity=true,focusAndSelection=true,keyboardTabs=true,modalSpacing=true,conflictDraft=true,modelCalls=0}));
    });
}
