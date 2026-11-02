// Design only: all data is fictitious and all removals affect this in-memory array, never the app/filesystem.
(() => {
 const C=window.RelayComponents,e=C.escape;
 const original=window.createRelayRenderer;
 let clock='2026-11-02T01:30',plan=null,notice='',serial=0;
 const seed=()=>[
  {id:'log-old',name:'Journaux techniques du 30/10/2026',kind:'logs',bytes:2097152,created:'2026-10-30T09:00',modified:'2026-10-30T23:00'},
  {id:'log-yesterday',name:'Journaux techniques du 01/11/2026',kind:'logs',bytes:1048576,created:'2026-11-01T09:00',modified:'2026-11-01T23:15'},
  {id:'log-today',name:'Journaux techniques du 02/11/2026',kind:'logs',bytes:180000,created:'2026-11-02T00:00',modified:'2026-11-02T01:15'},
  {id:'lease-old',name:'Diagnostic dâ€™une connexion terminÃ©e',kind:'diagnostics',bytes:2000,created:'2026-10-29T10:00',modified:'2026-10-29T11:00'},
  {id:'lease-live',name:'Diagnostic dâ€™une connexion active',kind:'diagnostics',bytes:2200,created:'2026-10-29T10:00',modified:'2026-10-29T11:00',active:true},
  {id:'temp-old',name:'Fichier temporaire abandonnÃ© et identifiÃ©',kind:'temporary',bytes:300000,created:'2026-10-28T12:00',modified:'2026-10-28T12:00'},
  {id:'transfer',name:'Transfert partiel Ã  vÃ©rifier',kind:'temporary',bytes:4194304,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',required:true},
  {id:'binary',name:'Version dâ€™application et lanceurs conservÃ©s',kind:'runtime',bytes:134217728,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',required:true},
  {id:'history',name:'Historique des projets et permissions',kind:'history',bytes:524288,created:'2026-10-28T12:00',modified:'2026-10-28T12:00',required:true}
 ];
 let items=seed(),selected=new Set(['logs','diagnostics','temporary']);
 const bytes=n=>n>=1048576?(n/1048576).toFixed(1)+' Mio':Math.ceil(n/1024)+' Kio';
 function boundary(value){const date=new Date(value),cutoff=new Date(date.getFullYear(),date.getMonth(),date.getDate());if(date.getHours()<2)cutoff.setDate(cutoff.getDate()-1);return cutoff;}
 function reason(item){
  if(item.required)return item.kind==='history'?'Historique et permissions conservÃ©s':'NÃ©cessaire, utilisÃ© ou Ã©tat Ã  vÃ©rifier';
  if(item.active)return 'Connexion encore active';
  const dates=[item.created,item.modified].map(v=>new Date(v));
  if(dates.some(v=>Number.isNaN(v.getTime())))return 'Date non vÃ©rifiable';
  if(dates.some(v=>v>=boundary(clock)))return 'JournÃ©e protÃ©gÃ©e';
  if(!selected.has(item.kind))return 'CatÃ©gorie non sÃ©lectionnÃ©e';
  return '';
 }
 const eligible=()=>items.filter(i=>!reason(i));
 const redraw=()=>{window.RelayUI?.route({view:'settings'});document.querySelector('[data-value=storage]')?.click();};
 const describe=()=>{const now=new Date(clock),today=new Date(now.getFullYear(),now.getMonth(),now.getDate()),previous=new Date(today);previous.setDate(previous.getDate()-1);return 'JournÃ©e protÃ©gÃ©e : '+C.formatDate(today.toISOString())+(now.getHours()<2?' et '+C.formatDate(previous.toISOString())+' (avant 02:00).':'. La journÃ©e prÃ©cÃ©dente est Ã©ligible sous rÃ©serve des autres protections.');};
 function storage(){
  const rows=eligible(),total=rows.reduce((n,i)=>n+i.bytes,0);
  return C.pageTitle('RÃ‰GLAGES','Stockage et nettoyage','Examiner les donnÃ©es conservÃ©es avant de dÃ©cider dâ€™un nettoyage manuel.')+C.tabs([['general','GÃ©nÃ©ral'],['projects','Projets'],['mcp','Connexions MCP'],['storage','Stockage et nettoyage']],'storage','settings-scope','RÃ©glages gÃ©nÃ©raux')+C.panel('settings-scope','storage',
   '<div class="info-block"><strong>Maquette de conception Â· donnÃ©es fictives</strong>Aucune lecture ni suppression de fichier rÃ©el. Cette fonctionnalitÃ© nâ€™est pas implÃ©mentÃ©e dans lâ€™application.</div>'+C.section('Protection des donnÃ©es rÃ©centes','Le jour est dÃ©terminÃ© selon lâ€™heure locale de lâ€™installation. Les Ã©lÃ©ments utilisÃ©s ou nÃ©cessaires restent protÃ©gÃ©s, quelle que soit leur date.')+
   `<div class="card storage-policy"><strong>Heure locale de rÃ©fÃ©rence : ${C.formatDateTime(clock)}</strong><p>${e(describe())}</p><p>Le pÃ©rimÃ¨tre inclut la crÃ©ation et la derniÃ¨re modification ; une date inconnue protÃ¨ge lâ€™Ã©lÃ©ment. Lâ€™Ã©ligibilitÃ© nâ€™entraÃ®ne aucune suppression automatique.</p></div>`+
   C.section('CatÃ©gories Ã  analyser','Les journaux et rÃ©sidus identifiÃ©s peuvent Ãªtre examinÃ©s. Historique mÃ©tier, configurations, permissions, versions utilisÃ©es et transferts incertains restent exclus.')+
   `<div class="card">${[['logs','Journaux techniques'],['diagnostics','Diagnostics de connexions terminÃ©es'],['temporary','Fichiers temporaires abandonnÃ©s']].map(([id,label])=>`<label class="storage-category"><input type="checkbox" data-clean-category="${id}" ${selected.has(id)?'checked':''}>${label}</label>`).join('')}</div><div class="actions storage-actions"><button class="btn" data-clean-action="analyze">Analyser les donnÃ©es fictives</button></div>`+
   C.section('AperÃ§u avant nettoyage','Liste figÃ©e aprÃ¨s analyse, puis revÃ©rifiÃ©e au moment de confirmer. Les volumes sont illustratifs ; ils ne mesurent pas lâ€™espace rÃ©ellement rÃ©cupÃ©rable.')+
   (plan?`<p class="status-explanation">${plan.ids.length} Ã©lÃ©ment(s) proposÃ©(s) Â· ${bytes(plan.bytes)} Â· analyse ${C.formatDateTime(clock)}.</p>`:'<p class="status-explanation">Aucune analyse confirmÃ©e. Aucun Ã©lÃ©ment ne peut Ãªtre supprimÃ©.</p>')+
   `<div class="card">${items.map(i=>`<div class="fact-row"><span class="fact-label">${e(i.name)}<small class="diagnostic-note">${e(reason(i)||'Ã‰ligible aprÃ¨s analyse et confirmation')}</small></span><strong>${bytes(i.bytes)}<small class="diagnostic-note">${reason(i)?'ProtÃ©gÃ© / exclu':'Ã‰ligible'}</small></strong></div>`).join('')}</div><div class="actions storage-actions"><button class="btn primary" data-clean-action="confirm" ${plan?.ids.length?'':'disabled'}>Simuler le nettoyage proposÃ©</button></div><p class="status-explanation" role="status">${e(notice)}</p>`);
 }
 window.createRelayRenderer=(getData,getUI)=>{const renderer=original(getData,getUI);return {...renderer,settings:()=>getUI().settingScope==='storage'?storage():renderer.settings().replace('</div><section id="panel-settings-scope"','<button id="tab-settings-scope-storage" role="tab" aria-selected="false" tabindex="-1" aria-controls="panel-settings-scope" data-action="settings-scope" data-value="storage">Stockage et nettoyage</button></div><section id="panel-settings-scope"')};};
 document.addEventListener('change',event=>{
  if(event.target.id==='cleanup-clock'){clock=event.target.value||clock;plan=null;notice='Horloge modifiÃ©e : refaire lâ€™analyse.';redraw();}
  if(event.target.dataset.cleanCategory){const key=event.target.dataset.cleanCategory;if(event.target.checked)selected.add(key);else selected.delete(key);plan=null;redraw();}
 });
 document.addEventListener('click',event=>{
  const action=event.target.closest('[data-clean-action]')?.dataset.cleanAction;if(!action)return;
  if(action==='analyze'){const rows=eligible();plan={id:++serial,ids:rows.map(i=>i.id),bytes:rows.reduce((n,i)=>n+i.bytes,0),fingerprint:JSON.stringify(rows)};notice='Analyse fictive prÃªte. Les Ã©lÃ©ments protÃ©gÃ©s sont conservÃ©s.';redraw();}
  if(action==='confirm'&&plan){const rows=items.filter(i=>plan.ids.includes(i.id));if(rows.some(reason)||JSON.stringify(rows)!==plan.fingerprint){plan=null;notice='Les donnÃ©es ont changÃ©. Aucun nettoyage simulÃ© ; refaire lâ€™analyse.';redraw();return;}
   const modal=document.querySelector('#app-modal');modal.innerHTML=`<div class="app-modal-backdrop"><section class="app-modal-card" role="dialog" aria-modal="true" aria-label="Confirmer la simulation"><h2>Confirmer le nettoyage simulÃ© ?</h2><p>${rows.length} Ã©lÃ©ment(s) fictif(s), ${bytes(plan.bytes)}. Les journÃ©es protÃ©gÃ©es et les donnÃ©es nÃ©cessaires seront conservÃ©es.</p><div class="actions"><button class="btn" data-clean-action="cancel">Annuler</button><button class="btn primary" data-clean-action="execute">Confirmer la simulation</button></div></section></div>`;}
  if(action==='cancel')document.querySelector('#app-modal').replaceChildren();
  if(action==='execute'&&plan){const rows=items.filter(i=>plan.ids.includes(i.id));document.querySelector('#app-modal').replaceChildren();if(rows.some(reason)||JSON.stringify(rows)!==plan.fingerprint){notice='La revÃ©rification a changÃ©. Aucun nettoyage simulÃ© ; refaire lâ€™analyse.';}else{items=items.filter(i=>!plan.ids.includes(i.id));notice=rows.length+' Ã©lÃ©ment(s) fictif(s) retirÃ©(s) de la simulation. Aucun fichier rÃ©el touchÃ©.';}plan=null;redraw();}
 });
 window.CleanupPreview={boundary:value=>boundary(value).getTime(),eligibleAt:value=>{const previous=clock;clock=value;const ids=eligible().map(i=>i.id);clock=previous;return ids;},open:redraw,reset:()=>{clock='2026-11-02T01:30';const input=document.querySelector('#cleanup-clock');if(input)input.value=clock;items=seed();plan=null;notice='';selected=new Set(['logs','diagnostics','temporary']);}};
})();
