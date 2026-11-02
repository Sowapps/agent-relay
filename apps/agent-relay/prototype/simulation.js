// Explicitly fictitious host bridge. Never executes native operations or reads user files.
let fixture=structuredClone(window.PrototypeFixtures),failNext=false,timer=null;
const listeners=[],emit=message=>listeners.forEach(fn=>fn({data:message}));
const snapshot=()=>emit({type:'snapshot',data:structuredClone(fixture)});
const report=text=>document.querySelector('#sim-result').textContent=text;
const update=()=>{for(const c of fixture.mcpConnections)c.lastSeen=new Date().toISOString();fixture.observedAt=new Date().toISOString();snapshot();report('Données actualisées. Aucun travail réel exécuté.');};
window.chrome||={};window.chrome.webview={addEventListener:(_,fn)=>listeners.push(fn),postMessage:message=>{
 if(message.action==='ready'){queueMicrotask(()=>{snapshot();window.RelayUI.route(Object.fromEntries(new URLSearchParams(location.search)));});return;}
 if(['stop-session','copy'].includes(message.action)){report(message.action==='copy'?'Copie simulée.':'Suivi fictif arrêté.');return;}
 if(failNext){failNext=false;if(message.action==='project-state')emit({type:'project-state',project:message.project,error:'L’état du projet a changé depuis votre ouverture. Actualisez avant de choisir une nouvelle action.'});else emit({type:'action-result',success:false,text:'La donnée a changé depuis votre ouverture. Votre saisie est conservée ; consultez l’état actuel avant de réessayer.'});report('Conflit simulé.');return;}
 if(message.action==='project-state'){const p=fixture.projects.find(p=>p.id===message.project);p.archiveRequested=message.state==='archived';if(message.state==='active')p.state='active';emit({type:'project-state',project:p.id,state:p.state,archiveRequested:p.archiveRequested});}
 else if(message.action==='session-control'){const s=fixture.sessions.find(s=>s.id===message.id);if(message.control.startsWith('rename:'))s.name=message.control.slice(7);else s.state=message.control==='close'?'closed':'ready';emit({type:'action-result',success:true,text:'Modification fictive confirmée.'});}
 else if(message.action==='close-mcp'){fixture.mcpConnections=fixture.mcpConnections.filter(c=>c.instanceId!==message.id);emit({type:'notice',text:'Connexion fictive fermée.'});}
 else if(message.action==='startup-preference'){fixture.startup={enabled:message.enabled};emit({type:'startup-preference',data:fixture.startup});}
 else if(message.action==='mark-reviewed'){const op=fixture.operations.find(o=>o.id===message.id);op.reviewed=true;emit({type:'operation-review',id:op.id,reviewToken:op.reviewToken});}
 else if(message.action==='operation'){const op=fixture.operations.find(o=>o.id===message.id);if(op)emit({type:'operation',data:{...op,previewOnly:false}});}
 else if(message.action==='load-session')fixture.sessionHistory[message.id]={loaded:true,hasMore:false};
 else if(message.action==='open-console')emit({type:'notice',text:'Console simulée · Connexion : WSL · Distribution : Ubuntu-24.04 · Utilisateur : sowapps · Espace de travail : /workspace'});
 else if(!['refresh','load-more'].includes(message.action))report('Action représentée uniquement dans la maquette.');
 snapshot();
}};
document.querySelector('#sim-update').onclick=update;
document.querySelector('#sim-conflict').onclick=()=>{failNext=true;report('La prochaine action échouera avec un conflit explicite.');};
document.querySelector('#sim-auto').onclick=event=>{if(timer){clearInterval(timer);timer=null;}else timer=setInterval(update,2000);event.target.textContent='Actualisation continue : '+(timer?'oui':'non');};
document.querySelector('#sim-reset').onclick=()=>{fixture=structuredClone(window.PrototypeFixtures);failNext=false;clearInterval(timer);timer=null;document.querySelector('#sim-auto').textContent='Actualisation continue : non';snapshot();window.RelayUI.route({view:'general'});report('Données fictives réinitialisées.');};
document.querySelector('#sim-state').onchange=event=>{const explanations={declared:'Configuration déclarée. Aucun contrôle lancé.',ready:'Contrôle fictif réussi sur cet environnement uniquement.',stale:'Le dernier contrôle fictif est ancien. Sa disponibilité actuelle reste à vérifier.',unsupported:'Cet adaptateur ne fournit pas de contrôle de disponibilité.',failed:'Le contrôle fictif a échoué. Vérifiez la configuration avant de réessayer.'};fixture.connections[0].state=event.target.value;fixture.connections[0].explanation=explanations[event.target.value];snapshot();};
document.querySelector('#sim-size').onchange=event=>document.querySelector('.prototype-frame .app-window').style.maxWidth=event.target.value+'px';
document.querySelector('#toggle-tools').onclick=()=>{const tools=document.querySelector('.simulation-tools');tools.hidden=!tools.hidden;};
