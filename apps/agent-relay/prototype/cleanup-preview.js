// Fictional design surface only. No filesystem, process, MCP or native cleanup access.
(() => {
 'use strict';
 const C=window.RelayComponents,e=C.escape,original=window.createRelayRenderer;
 const categories=[['logs','Journaux techniques','Anciens journaux dont les journées ne sont plus protégées.'],['diagnostics','Diagnostics de connexions','Traces de connexions dont la fin a été confirmée.'],['temporary','Fichiers temporaires','Résidus identifiés, sans opération ni récupération en attente.'],['runtime','Anciennes versions','Versions inutilisées après vérification du relais, des clients MCP, du démarrage et de la restauration.']];
 const initialClock='2026-11-02T01:30';
 let clock=initialClock,plan=null,result=null,notice='',detailId=null,failNext=false,getData=null;
 let selected=new Set(['logs','diagnostics','temporary']),showProtected=false;
 const seed=()=>[
  {id:'log-old',name:'Journaux techniques du 30/10/2026',kind:'logs',bytes:2097152,created:'2026-10-30T09:00',modified:'2026-10-30T23:00',path:'exemple/logs/mcp/20261030.log'},
  {id:'log-yesterday',name:'Journaux techniques du 01/11/2026',kind:'logs',bytes:1048576,created:'2026-11-01T09:00',modified:'2026-11-01T23:15',path:'exemple/logs/mcp/20261101.log'},
  {id:'log-today',name:'Journaux techniques du 02/11/2026',kind:'logs',bytes:180000,created:'2026-11-02T00:00',modified:'2026-11-02T01:15',path:'exemple/logs/mcp/20261102.log'},
  {id:'lease-old',name:'Diagnostic d’une connexion terminée',kind:'diagnostics',bytes:2000,created:'2026-10-29T10:00',modified:'2026-10-29T11:00',path:'exemple/mcp-connections/connexion-terminee.json'},
  {id:'lease-live',name:'Diagnostic d’une connexion active',kind:'diagnostics',bytes:2200,created:'2026-10-29T10:00',modified:'2026-10-29T11:00',active:true,path:'exemple/mcp-connections/connexion-active.json'},
  {id:'temp-old',name:'Fichier temporaire abandonné et identifié',kind:'temporary',bytes:300000,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',path:'exemple/staging/residu-identifie.tmp'},
  {id:'transfer',name:'Transfert partiel à vérifier',kind:'temporary',bytes:4194304,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',required:true,path:'exemple/staging/transfers/resultat-incertain.part'},
  {id:'runtime-unused',name:'Version 1.0.0 inutilisée',kind:'runtime',bytes:268435456,reclaimable:134217728,created:'2026-10-25T12:00',modified:'2026-10-25T12:00',references:[],path:'run/version-1.0.0-inutilisee'},
  {id:'runtime-current',name:'Version utilisée par le relais',kind:'runtime',bytes:134217728,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',references:['Relais en cours'],path:'run/version-du-relais'},
  {id:'runtime-mcp',name:'Version encore utilisée par un client',kind:'runtime',bytes:134217728,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',references:['Client MCP toujours connecté'],path:'run/version-du-client-mcp'},
  {id:'runtime-startup',name:'Version utilisée au démarrage',kind:'runtime',bytes:134217728,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',references:['Démarrage automatique'],path:'exemple/startup-launcher'},
  {id:'runtime-rollback',name:'Version de restauration conservée',kind:'runtime',bytes:134217728,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',references:['Restauration conservée'],path:'run/version-de-restauration'},
  {id:'runtime-unknown',name:'Version dont l’utilisation reste incertaine',kind:'runtime',bytes:134217728,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',references:[],unknown:true,path:'run/version-non-verifiable'},
  {id:'history',name:'Historique, configurations et permissions',kind:'history',bytes:524288,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',required:true,path:'exemple/donnees-metier'}
 ];
 let items=seed();
 const bytes=n=>new Intl.NumberFormat('fr-FR',{maximumFractionDigits:1}).format(n/ (n>=1073741824?1073741824:n>=1048576?1048576:1024))+' '+(n>=1073741824?'Gio':n>=1048576?'Mio':'Kio');
 const sum=(rows,key='bytes')=>rows.reduce((n,i)=>n+(key==='reclaimable'?(i.reclaimable??i.bytes):i.bytes),0);
 const boundary=value=>{const d=new Date(value),cutoff=new Date(d.getFullYear(),d.getMonth(),d.getDate());if(d.getHours()<2)cutoff.setDate(cutoff.getDate()-1);return cutoff;};
 function protection(item){
  if(item.required)return item.kind==='history'?'Historique et permissions toujours conservés':'Transfert ou récupération à vérifier';
  if(item.active)return 'Connexion encore active';
  if(item.references?.length)return item.references.join(' · ');
  if(item.unknown)return 'Utilisation impossible à vérifier : version conservée';
  const dates=[item.created,item.modified].map(v=>new Date(v));
  if(dates.some(d=>Number.isNaN(d.getTime())))return 'Date non vérifiable';
  if(dates.some(d=>d>=boundary(clock)))return 'Journée protégée';
  return '';
 }
 const reason=item=>protection(item)||(!selected.has(item.kind)?'Catégorie non sélectionnée':'');
 const eligible=()=>items.filter(i=>!reason(i));
 const fingerprint=rows=>JSON.stringify(rows);
 const redraw=()=>{if(getData&&window.RelayUI)window.RelayUI.receive({type:'snapshot',data:structuredClone(getData())});};
 const open=()=>{window.RelayUI?.route({view:'settings'});document.querySelector('[data-value=storage]')?.click();};
 const protectedDates=()=>{const now=new Date(clock),today=new Date(now.getFullYear(),now.getMonth(),now.getDate()),previous=new Date(today);previous.setDate(previous.getDate()-1);return C.formatDate(today.toISOString())+(now.getHours()<2?' et '+C.formatDate(previous.toISOString()):'');};
 const action=(label,id,cls='',extra='')=>`<button class="btn ${cls}" data-clean-action="${id}" ${extra}>${label}</button>`;
 const metrics=entries=>`<div class="stats storage-stats">${entries.map(([value,label,help])=>`<div class="card stat"><div><strong>${e(value)}</strong><small>${e(label)}</small><p>${e(help)}</p></div></div>`).join('')}</div>`;
 function row(item,status,why){return `<div class="storage-item" data-key="cleanup-${e(item.id)}"><div><strong>${e(item.name)}</strong><small>${e(why)}</small></div><div class="storage-item-end"><strong title="Taille apparente">${bytes(item.bytes)}</strong><span class="badge ${['Proposé','Retiré'].includes(status)?'ready':status==='Erreur'?'failed':'idle'}">${e(status)}</span>${C.iconButton('Voir les détails et protections de '+item.name,'cleanup-detail','arrow',`data-clean-id="${e(item.id)}"`)}</div></div>`;}
 function drawer(){
  const item=items.find(i=>i.id===detailId)||result?.entries.find(i=>i.item.id===detailId)?.item;if(!item)return '';
  const why=protection(item),runtime=item.kind==='runtime';
  const checks=runtime?C.section('Utilisation de cette version','Vérifications fictives : une seule utilisation ou un contrôle impossible suffit à conserver la version.',3)+C.facts(['Relais en cours','Client MCP toujours connecté','Démarrage automatique','Restauration conservée'].map(label=>[e(label),item.unknown?'Non vérifié':item.references?.includes(label)?'Utilisation détectée':'Aucune utilisation dans cet exemple'])):'';
  const outcome=result?.entries.find(i=>i.item.id===item.id);
  return C.detailShell('DÉTAIL DU STOCKAGE',item.name,`<div class="info-block"><strong>${why?'Élément protégé':'Potentiellement nettoyable'}</strong>${e(why||'Sous réserve de sa sélection, de l’analyse et de la revérification finale.')}</div>`+C.facts([['Catégorie',e(categories.find(c=>c[0]===item.kind)?.[1]||'Données métier')],['Taille apparente',bytes(item.bytes)],['Estimation récupérable',why?'Aucune suppression proposée':bytes(item.reclaimable??item.bytes)],['Création',C.formatDateTime(item.created)],['Dernière modification',C.formatDateTime(item.modified)]])+checks+(outcome?C.section('Résultat de la simulation',outcome.reason,3):'')+`<details class="advanced"><summary>Identifiant et chemin fictifs</summary><pre>${e(JSON.stringify({id:item.id,path:'var/apps/agent-relay/'+item.path,references:item.references||[],checks:'Simulation uniquement'},null,2))}</pre></details>`,action('Fermer','close-detail'));
 }
 function storage(){
  const breadcrumb=document.querySelector('#app-topbar .breadcrumb');if(breadcrumb)breadcrumb.innerHTML='<strong>Réglages</strong><span>/</span><strong>Stockage et nettoyage</strong>';
  const candidates=eligible(),protectedItems=items.filter(protection);
  const controls=C.tabs([['general','Général'],['projects','Projets'],['mcp','Connexions MCP'],['storage','Stockage et nettoyage']],'storage','settings-scope','Réglages généraux');
  const categoryRows=categories.map(([id,label,description])=>{const rows=items.filter(i=>i.kind===id),blocked=rows.filter(protection);return `<div class="storage-category-row"><label><input type="checkbox" data-clean-category="${id}" ${selected.has(id)?'checked':''}><span><strong>${e(label)}</strong><small>${e(description)}</small></span></label><div><strong>${bytes(sum(rows))}</strong><small>${rows.length-blocked.length} admissible(s) · ${blocked.length} protégé(s)</small></div></div>`;}).join('');
  const list=plan?`<div class="storage-plan-head"><p>Analyse du ${C.formatDateTime(plan.at)} · ${plan.entries.length} élément(s) proposé(s) · ${bytes(plan.bytes)} estimés.</p>${action('Refaire l’analyse','analyze')}</div><div class="card">${plan.entries.map(i=>row(i,'Proposé',i.kind==='runtime'?'Aucune utilisation détectée dans les vérifications fictives':'Ancien et sans utilisation en cours dans cet exemple')).join('')||C.empty('Rien à nettoyer','Aucun élément de la sélection ne remplit les conditions de nettoyage.','check')}</div>`:result?'<p class="status-explanation">L’analyse précédente est terminée. Vous pouvez préparer une nouvelle sélection.</p>':C.empty('Commencez par analyser','L’analyse prépare une liste précise ; elle ne supprime rien.','search');
  const protectedList=showProtected?`<div class="card">${items.filter(i=>reason(i)).map(i=>row(i,protection(i)?'Protégé':'Non sélectionné',reason(i))).join('')}</div>`:'';
  const summary=result?'<div id="cleanup-result">'+C.section('Bilan du nettoyage simulé','Les données fictives ont seules été modifiées. Aucun fichier réel touché.')+metrics([[String(result.entries.filter(i=>i.status==='Retiré').length),'Éléments retirés','De la simulation uniquement'],[String(result.entries.filter(i=>i.status!=='Retiré').length),'Éléments conservés','Non retirés, y compris en cas d’erreur'],[String(result.entries.filter(i=>i.status==='Erreur').length),'Erreurs à examiner','Parmi les éléments conservés']])+`<div class="card">${result.entries.map(entry=>row(entry.item,entry.status,entry.reason)).join('')}</div><p class="status-explanation">${result.protectedCount} élément(s) protégé(s) conservé(s). Estimation retirée : ${bytes(result.bytes)} ; aucun espace disque réel libéré.</p></div>`:'';
  return C.pageTitle('RÉGLAGES','Réglages généraux','Paramètres de cette installation du relais.')+controls+C.panel('settings-scope','storage',
   '<div class="info-block"><strong>Maquette · données et contrôles fictifs</strong>Cette vue ne lit ni ne supprime aucun fichier réel. Le nettoyage n’est pas implémenté dans l’application.</div>'+C.section('Stockage et nettoyage','Comprendre l’espace occupé et choisir un nettoyage manuel, après analyse et confirmation.')+
   metrics([[bytes(sum(items)),'Espace apparent occupé','Les fichiers partagés peuvent être comptés plusieurs fois.'],[bytes(sum(candidates,'reclaimable')),'Potentiellement récupérable','Estimation de la sélection, sous réserve de l’analyse.'],[bytes(sum(protectedItems)),'Données protégées','Conservées indépendamment de leur ancienneté.']])+
   `<div class="card storage-policy"><strong>Dates protégées : ${e(protectedDates())}</strong><p>Heure locale de référence : ${C.formatDateTime(clock)}. La journée en cours reste protégée ; avant 02:00, la veille l’est aussi.</p><p>Les données utilisées, nécessaires ou impossibles à vérifier sont conservées. Historique, configurations et permissions restent exclus.</p></div>`+
   C.section('Catégories à analyser','Sélectionnez ce que vous souhaitez examiner. Une ancienne version n’est admissible que si toutes ses utilisations ont été écartées.')+`<div class="card">${categoryRows}</div><div class="actions storage-actions">${action('Analyser la sélection','analyze','primary')}</div>`+
   '<div id="cleanup-preview">'+C.section('Aperçu avant nettoyage','Seuls les éléments de cette liste figée peuvent être proposés. Leur état sera revérifié avant confirmation. Tailles affichées : apparentes ; estimation récupérable disponible dans le détail.')+list+`</div><div class="actions storage-actions">${action(showProtected?'Masquer les éléments conservés':'Voir les éléments protégés et non sélectionnés','protected')}${action('Simuler le nettoyage proposé','confirm','primary',plan?.entries.length?'':'disabled')}</div>`+protectedList+`<p class="status-explanation" role="status">${e(notice)}</p>`+summary);
 }
 window.createRelayRenderer=(data,ui)=>{getData=data;const renderer=original(data,ui);return {...renderer,settings:()=>ui().settingScope==='storage'?storage():renderer.settings().replace('</div><section id="panel-settings-scope"','<button id="tab-settings-scope-storage" role="tab" aria-selected="false" tabindex="-1" aria-controls="panel-settings-scope" data-action="settings-scope" data-value="storage">Stockage et nettoyage</button></div><section id="panel-settings-scope"'),detail:()=>ui().view==='settings'&&ui().settingScope==='storage'&&detailId?drawer():renderer.detail()};};
 function modal(){document.querySelector('#app-modal').innerHTML=`<div class="app-modal-backdrop"><section class="app-modal-card" role="dialog" aria-modal="true" aria-label="Confirmer la simulation"><h2>Confirmer le nettoyage simulé ?</h2><p>${plan.entries.length} élément(s), estimation de ${bytes(plan.bytes)}. Seule cette sélection sera revérifiée.</p><p>Les journées protégées, versions utilisées, données métier et permissions seront conservées.</p><p>Aucun fichier réel ne sera touché.</p><div class="actions">${action('Annuler','cancel')}${action('Confirmer la simulation','execute','primary')}</div></section></div>`;}
 function revalidate(){const rows=plan?.entries||[];return rows.length&&rows.every(before=>{const current=items.find(i=>i.id===before.id);return current&&!reason(current)&&fingerprint(current)===fingerprint(before);});}
 function blockedResult(){result={entries:plan.entries.map(item=>({item,status:'Conservé',reason:'Le plan a changé : aucune suppression simulée. Refaire l’analyse.'})),bytes:0,protectedCount:items.filter(protection).length};plan=null;notice='La revérification a détecté un changement. Aucun nettoyage simulé ; refaites l’analyse.';}
 document.addEventListener('change',event=>{
  if(event.target.id==='cleanup-clock'){clock=event.target.value||clock;plan=null;result=null;notice='Horloge modifiée : refaire l’analyse.';redraw();}
  if(event.target.dataset.cleanCategory){const key=event.target.dataset.cleanCategory;if(event.target.checked)selected.add(key);else selected.delete(key);plan=null;result=null;notice='Sélection modifiée : refaire l’analyse.';redraw();}
 });
 document.addEventListener('click',event=>{
  const element=event.target.closest('[data-clean-action],[data-action]');if(!element)return;
  if(element.dataset.action==='close-detail')detailId=null;
  if(['navigate','project','settings-scope'].includes(element.dataset.action)&&element.dataset.value!=='storage')detailId=null;
  if(element.dataset.action==='cleanup-detail'){detailId=element.dataset.cleanId;redraw();return;}
  const action=element.dataset.cleanAction;if(!action)return;
  if(action==='close-detail'){detailId=null;redraw();}
  if(action==='protected'){showProtected=!showProtected;redraw();}
  if(action==='analyze'){const rows=eligible();plan={at:clock,entries:structuredClone(rows),bytes:sum(rows,'reclaimable')};result=null;notice=rows.length?'Analyse fictive prête. Les éléments protégés sont conservés.':'Aucun élément de la sélection n’est admissible.';redraw();document.querySelector('#cleanup-preview')?.scrollIntoView({block:'start'});}
  if(action==='confirm'&&plan){if(!revalidate()){blockedResult();redraw();document.querySelector('#cleanup-result')?.scrollIntoView({block:'start'});}else modal();}
  if(action==='cancel')document.querySelector('#app-modal').replaceChildren();
  if(action==='execute'&&plan){document.querySelector('#app-modal').replaceChildren();if(!revalidate())blockedResult();else{const removed=new Set(),entries=plan.entries.map((item,index)=>{if(failNext&&index===0)return {item,status:'Erreur',reason:'Échec simulé : accès refusé. Élément conservé ; aucun nouvel essai automatique.'};removed.add(item.id);return {item,status:'Retiré',reason:'Retiré de la simulation après revérification.'};});result={entries,bytes:sum(entries.filter(i=>i.status==='Retiré').map(i=>i.item),'reclaimable'),protectedCount:items.filter(protection).length};items=items.filter(i=>!removed.has(i.id));failNext=false;plan=null;notice='Simulation terminée. Aucun fichier réel touché.';}redraw();document.querySelector('#cleanup-result')?.scrollIntoView({block:'start'});}
 });
 function scenario(name){
  if(name==='runtime-use'){const old=items.find(i=>i.id==='runtime-unused');if(old)old.references=['Client MCP toujours connecté'];notice='Simulation : une ancienne version est désormais utilisée. Le plan devra être revérifié.';}
  if(name==='unknown'){const old=items.find(i=>i.id==='runtime-unused');if(old)old.unknown=true;notice='Simulation : un contrôle d’utilisation est impossible. La version sera conservée.';}
  if(name==='failure'){failNext=true;notice='Simulation : un élément du prochain nettoyage échouera ; les autres résultats seront détaillés.';}
  redraw();
 }
 window.CleanupPreview={boundary:value=>boundary(value).getTime(),eligibleAt:value=>{const before=clock;clock=value;const ids=eligible().map(i=>i.id);clock=before;return ids;},open,scenario,state:()=>structuredClone({clock,plan,result,items,selected:[...selected]}),reset:()=>{clock=initialClock;items=seed();plan=null;result=null;notice='';detailId=null;failNext=false;showProtected=false;selected=new Set(['logs','diagnostics','temporary']);const input=document.querySelector('#cleanup-clock');if(input)input.value=clock;}};
})();
