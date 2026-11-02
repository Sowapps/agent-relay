// Local presentation controller. Native operations cross one explicit, bounded host bridge.
(() => {
 'use strict';
 const $=selector=>document.querySelector(selector),C=window.RelayComponents;
 let data=null,toastTimer;
 const fullOperations=new Map();
 const ui={view:'general',project:null,tab:'summary',filter:'all',period:'all',participant:'',kind:'',state:'',advanced:false,generalFeed:'tasks',taskLimit:15,query:'',messageFilter:'all',detail:null,settingScope:'general',projectChange:null,limit:5,sessionId:null,conversationLimit:30};
 const R=window.createRelayRenderer(()=>data,()=>ui);
 const host=(action,values={})=>{
  if(!window.chrome?.webview){if(action!=='ready')toast('Action native indisponible dans cet aperçu.');return;}
  window.chrome.webview.postMessage({action,...values});
 };
 function toast(text,operation=null){const element=$('#toast'),message=document.createElement('span');message.className='toast-text';message.textContent=text;element.replaceChildren(message);if(operation)element.insertAdjacentHTML('beforeend',C.button('Voir dans l’historique','history-operation',`data-id="${C.escape(operation.id)}"`,'quiet'));element.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>element.hidden=true,operation?12000:3500);}
 function renderDetail(){
  const scroll=$('.detail-body')?.scrollTop||0;
  const open=[...document.querySelectorAll('#detail-layer details[open]')].map(el=>el.id).filter(Boolean);
  const layer=$('#detail-layer'),key=ui.detail?ui.detail.type+'|'+ui.detail.id:'';C.patch(layer,R.detail()||'',layer.dataset.detail!==key);layer.dataset.detail=key;
  if($('.detail-body'))$('.detail-body').scrollTop=scroll;
  open.forEach(id=>{const el=document.getElementById(id);if(el)el.open=true;});
 }
 function render(){
  if(!data)return;
  const content=$('#app-content'),key=[ui.view,ui.project,ui.tab,ui.generalFeed,ui.sessionId,ui.view==='settings'?ui.settingScope:''].join('|'),changed=content.dataset.view!==key;
  const scroll=changed?0:content.scrollTop,focused=document.activeElement,focusId=focused?.id;
  const selection=focused instanceof HTMLInputElement?[focused.selectionStart,focused.selectionEnd]:null;
  const openDetails=[...document.querySelectorAll('details[open]')].map(el=>el.id).filter(Boolean);
  C.patch($('#app-nav'),R.nav());
  const current=ui.view==='project'?data.projects.find(p=>p.id===ui.project)?.name:ui.view==='diagnostics'?'Diagnostic du relais':ui.view==='settings'?'Réglages':'Vue générale';
  const scope={general:'Général',projects:'Projets',mcp:'Connexions MCP'}[ui.settingScope];
  const crumbs=ui.view==='project'?['Projets',current,{summary:'Synthèse',tasks:'Tâches',activity:'Activité',messages:'Messages',sessions:'Sessions',conversation:'Conversation',settings:'Réglages'}[ui.tab]]:ui.view==='settings'?['Réglages',scope]:[current];
  C.patch($('#app-topbar'),`<div class="breadcrumb">${crumbs.filter(Boolean).map((text,index)=>`${index?'<span>/</span>':''}<strong>${C.escape(text)}</strong>`).join('')}</div><div class="topbar-meta">Suivi local <i class="dot ${C.escape(data.relay)}"></i></div>`);
  C.patch(content,ui.view==='general'?R.general():ui.view==='project'?R.project():ui.view==='diagnostics'?R.diagnostics():R.settings(),changed);
  content.dataset.view=key;content.scrollTop=scroll;
  renderDetail();
  openDetails.forEach(id=>{const el=document.getElementById(id);if(el instanceof HTMLDetailsElement)el.open=true;});
  if(!changed&&focused?.isConnected&&document.activeElement!==focused)focused.focus({preventScroll:true});
  if(focusId&&document.getElementById(focusId)){
   const el=document.getElementById(focusId);el.focus({preventScroll:true});
   if(selection&&el instanceof HTMLInputElement&&['text','search'].includes(el.type))el.setSelectionRange(...selection);
  }
 }
 function navigate(view,project=null,tab='summary'){
  if(ui.sessionId)host('stop-session');
  if(view==='settings')ui.settingScope='general';
  Object.assign(ui,{view,project,tab,filter:'all',period:'all',participant:'',kind:'',state:'',query:'',detail:null,limit:5,sessionId:null,advanced:false,messageFilter:'all'});render();
 }
 function detail(type,id){
  ui.detail={type,id};if(type==='task')ui.taskLimit=15;
  if(type==='message'&&data.unreadAvailable!==false){const message=data.messages.find(m=>m.id===id);if(message)message.unread=false;render();}else renderDetail();
  if(type==='operation'&&data.operations.find(operation=>operation.id===id)?.previewOnly!==false)host('operation',{id});
 }
 function modal(title,body,buttons){$('#app-modal').innerHTML=`<div class="app-modal-backdrop"><section class="app-modal-card" role="dialog" aria-modal="true" aria-label="${C.escape(title)}"><h2>${C.escape(title)}</h2>${body}<div class="actions">${C.button('Annuler','close-modal','','quiet')}${buttons}</div></section></div>`;}
 function messageText(type,id){
  if(type==='message')return data.messages.find(m=>m.id===id)?.text||'';
  if(type==='operation')return data.operations.find(o=>o.id===id)?.result||'';
  const session=data.sessions.find(s=>s.id===id);
  return session?[session.name,session.workspace,session.participantId].join('\n'):'';
 }
 document.addEventListener('click',event=>{
  const el=event.target.closest('[data-action]');if(!el||!data)return;
  const {action,id,value}=el.dataset;
  if(action==='navigate')navigate(el.dataset.view);
  else if(action==='project')navigate('project',el.dataset.project);
  else if(action==='tab'){if(ui.sessionId)host('stop-session');Object.assign(ui,{period:'all',participant:'',kind:'',state:'',advanced:false,messageFilter:'all',sessionId:null});ui.tab=value;ui.detail=null;ui.query='';ui.filter='all';ui.limit=5;render();}
  else if(action==='general-feed'){Object.assign(ui,{period:'all',participant:'',kind:'',state:'',advanced:false,messageFilter:'all'});ui.generalFeed=value;ui.filter='all';ui.query='';ui.limit=5;render();}
  else if(action==='filter'){ui.filter=value;render();}
  else if(action==='message-filter'){ui.messageFilter=value;render();}
  else if(action==='advanced-filters'){ui.advanced=!ui.advanced;render();}
  else if(['clear-filters','clear-history'].includes(action)){Object.assign(ui,{filter:'all',period:'all',participant:'',kind:'',state:'',query:'',limit:5,messageFilter:'all'});render();}
  else if(action==='more'){ui.limit+=10;if(window.createRelayViews(()=>data,()=>ui).more(el.dataset.kind||'tasks'))host('load-more',{project:ui.project});render();}
  else if(action==='session-thread'){const session=data.sessions.find(item=>item.id===id);if(session){navigate('project',session.project,'conversation');ui.sessionId=id;ui.conversationLimit=30;render();host('load-session',{id});}}
  else if(action==='thread-more'){ui.conversationLimit+=30;if(data.sessionHistory?.[ui.sessionId]?.hasMore)host('load-session',{id:ui.sessionId,older:true});render();}
  else if(action==='thread-refresh')host('load-session',{id:ui.sessionId});
  else if(action==='history-operation'){const operation=data.operations.find(item=>item.id===id);if(operation){navigate('project',operation.project,'activity');detail('operation',id);$('#toast').hidden=true;}}
  else if(action==='task-more'){ui.taskLimit+=15;renderDetail();}
  else if(['operation','task','message','session'].includes(action))detail(action,id);
  else if(action==='context')detail('context');
  else if(action==='close-detail'){ui.detail=null;$('#detail-layer').innerHTML='';}
  else if(action==='task-project')navigate('project',el.dataset.project);
  else if(action==='startup-toggle'||action==='startup-retry'){const state=data.startup||{enabled:data.startupEnabled};if(ui.startupPending||typeof state.enabled!=='boolean')return;ui.startupTarget=action==='startup-retry'?ui.startupTarget:!state.enabled;ui.startupPending=true;ui.startupError=null;render();host('startup-preference',{enabled:ui.startupTarget});}
  else if(action==='close-mcp'){modal('Fermer cette connexion MCP ?',"<p>La requête engagée pourra se terminer. Aucun travail ne sera rejoué et les permissions restent conservées.</p><p>Le client peut nécessiter un rechargement explicite pour se reconnecter.</p>",C.button('Fermer la connexion','confirm-close-mcp',`data-id="${C.escape(id)}"`,'primary'));}
  else if(action==='confirm-close-mcp'){$('#app-modal').innerHTML='';host('close-mcp',{id});}
  else if(action==='settings-scope'){ui.settingScope=value;render();}
  else if(action==='project-settings'){const project=el.dataset.project||ui.project;if(project)navigate('project',project,'settings');}
  else if(action==='manage-projects'){navigate('settings');ui.settingScope='projects';render();}
  else if(action==='project-state'){
   const project=data.projects.find(p=>p.id===el.dataset.project);if(!project||ui.projectChange)return;
   const archived=project.state==='archived',pending=project.archiveRequested,state=archived||pending?'active':'archived';
   modal(pending?'Annuler l’archivage ?':archived?'Réactiver ce projet ?':'Archiver ce projet ?',`<p><strong>${C.escape(project.name)}</strong></p><p>${pending?'Les nouvelles actions seront de nouveau autorisées selon les permissions existantes. Aucun travail ne sera relancé.':archived?'Le projet réapparaîtra dans la navigation. Aucun travail ni aucune session ne sera relancé.':'Les nouvelles actions seront suspendues immédiatement. Le relais attendra la fin du travail engagé, puis archivera le projet automatiquement. Son historique et ses permissions seront conservés.'}</p>${archived||pending?'':'<p>Un résultat incertain ou une vérification impossible maintiendra la demande en attente avec un diagnostic. Aucune interruption ni approbation automatique. Vous pourrez annuler cette demande.</p>'}`,C.button(pending?'Annuler l’archivage':archived?'Réactiver':'Demander l’archivage','confirm-project-state',`data-project="${C.escape(project.id)}" data-value="${state}" data-expected="${archived?'archived':'active'}" data-pending="${Boolean(pending)}"`,'primary'));
  }
  else if(action==='confirm-project-state'){
   if(ui.projectChange)return;const current=data.projects.find(p=>p.id===el.dataset.project);if(!current||Boolean(current.archiveRequested)!==(el.dataset.pending==='true')||(current.state||'active')!==el.dataset.expected){modal('Le projet a changé','<p>Son état ou sa demande d’archivage a changé depuis votre ouverture. Actualisez avant de choisir une nouvelle action.</p>',C.button('Actualiser','project-state-refresh','','primary'));return;}ui.projectChange={project:el.dataset.project,state:value,expectedState:el.dataset.expected};$('#app-modal').innerHTML='';render();host('project-state',ui.projectChange);
  }
  else if(action==='project-state-refresh'){$('#app-modal').innerHTML='';host('refresh');}
  else if(action==='mark-reviewed'){const operation=data.operations.find(item=>item.id===id);if(operation)host('mark-reviewed',{id,project:operation.project,reviewToken:operation.reviewToken});}
  else if(action==='copy')host('copy',{text:messageText(el.dataset.type,id)});
  else if(action==='show-evidence'){const evidence=$('#operation-evidence');if(evidence){evidence.open=true;evidence.scrollIntoView({block:'nearest'});}}
  else if(action==='rename-session'){
   const session=data.sessions.find(s=>s.id===id);if(!session)return;
   modal('Renommer la session',`<label for="session-name">Nom de la session</label><input id="session-name" maxlength="120" value="${C.escape(session.name)}">`,C.button('Renommer','confirm-rename',`data-id="${C.escape(id)}"`,'primary'));
   $('#session-name').focus();
  }
  else if(action==='confirm-rename'){const name=$('#session-name').value.trim();if(name&&!ui.renamePending){ui.renamePending=true;host('session-control',{id,control:'rename:'+name});}}
  else if(action==='session-state'){const session=data.sessions.find(s=>s.id===id);if(session)host('session-control',{id,control:session.state==='closed'?'reopen':'close'});}
  else if(action==='interrupt')host('interrupt-operation',{id});
  else if(action==='inspect-session')host('inspect-session',{id});
  else if(['operation-events','inspect-partial'].includes(action))host(action,{id});
  else if(action==='close-modal')$('#app-modal').innerHTML='';
  else if(['refresh','open-console','edit-projects','legacy-history','quit'].includes(action))host(action);
 });
 document.addEventListener('input',event=>{
  if(event.target.id!=='activity-search')return;
  ui.query=event.target.value;render();
 });
 document.addEventListener('change',event=>{
  const el=event.target;
  if(el.dataset.history){ui[el.dataset.history]=el.value;ui.limit=5;render();}
  if(el.id==='compact-view'){document.body.classList.toggle('compact-rows',el.checked);try{localStorage.setItem('compact-rows',String(el.checked));}catch{} }
 });
 document.addEventListener('keydown',event=>{
  const tab=event.target.closest('[role=tab]');if(tab&&['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){const items=[...tab.closest('[role=tablist]').querySelectorAll('[role=tab]')],i=items.indexOf(tab),n=event.key==='Home'?0:event.key==='End'?items.length-1:(i+(event.key==='ArrowLeft'?-1:1)+items.length)%items.length;event.preventDefault();items[n].click();document.getElementById(items[n].id)?.focus();return;}
  if(event.key==='Tab'&&$('#app-modal').childElementCount){const fields=[...$('#app-modal').querySelectorAll('button:not(:disabled),input:not(:disabled),select:not(:disabled),summary')];if(fields.length){const i=fields.indexOf(document.activeElement);if(event.shiftKey&&i<=0){event.preventDefault();fields.at(-1).focus();}else if(!event.shiftKey&&(i<0||i===fields.length-1)){event.preventDefault();fields[0].focus();}}}
  if(event.key==='Escape'){if($('#app-modal').childElementCount)$('#app-modal').innerHTML='';else{ui.detail=null;$('#detail-layer').innerHTML='';}}
 });
 function receive(message){
  if(!message||typeof message!=='object')return;
  if(message.type==='snapshot'){
   if(message.data?.schemaVersion!==1)return;
   const next=message.data;
   next.operations=next.operations.map(operation=>{const full=fullOperations.get(operation.id);return full&&full.updatedAt===operation.updatedAt&&full.state===operation.state?{...full,reviewed:operation.reviewed,reviewToken:operation.reviewToken}:operation;});
   for(const id of fullOperations.keys())if(!next.operations.some(operation=>operation.id===id))fullOperations.delete(id);
   if(ui.project&&!next.projects.some(p=>p.id===ui.project))Object.assign(ui,{view:'general',project:null,detail:null});
   if(ui.sessionId&&!next.sessions.some(session=>session.id===ui.sessionId))Object.assign(ui,{sessionId:null,tab:'sessions',detail:null});
   data=next;render();
  }else if(message.type==='startup-preference'){
   ui.startupPending=false;ui.startupError=message.data?.error||null;data.startup=message.data;data.startupEnabled=message.data?.enabled===true;render();if(ui.view==='settings'&&ui.settingScope==='general')$('#startup-switch')?.focus({preventScroll:true});
  }else if(message.type==='project-state'){
   ui.projectChange=null;const project=data?.projects.find(p=>p.id===message.project);if(project&&message.state){project.state=message.state;project.archiveRequested=Boolean(message.archiveRequested);}render();
   if(message.error)modal('Changement d’état non confirmé',`<p>${C.escape(message.error)}</p><p>Consultez l’état actuel avant de réessayer.</p>${message.diagnostic?`<details class="advanced"><summary>Détails techniques</summary><pre>${C.escape(message.diagnostic)}</pre></details>`:''}`,C.button('Actualiser','project-state-refresh','','primary'));
   else toast(message.archiveCancelled?'Archivage annulé. Les nouvelles actions sont de nouveau autorisées selon les permissions existantes.':message.archiveRequested?'Archivage demandé. Les nouveaux travaux sont suspendus pendant la fin du travail engagé.':message.state==='archived'?'Projet archivé. Son historique reste accessible dans Réglages → Projets.':'Projet réactivé. Aucun travail n’a été relancé.');
  }else if(message.type==='action-result'){
   ui.renamePending=false;if(message.success){$('#app-modal').innerHTML='';toast(message.text||'Action confirmée.');}else{const card=$('.app-modal-card');if(card){let alert=card.querySelector('[role=alert]');if(!alert){alert=document.createElement('p');alert.setAttribute('role','alert');card.insertBefore(alert,card.querySelector('.actions'));}alert.textContent=message.text;}else modal('Action non confirmée',`<p>${C.escape(message.text)}</p><p>Aucun nouvel essai automatique. Consultez l’état actuel avant de poursuivre.</p>` ,C.button('Actualiser','project-state-refresh','','primary'));}
  }else if(message.type==='operation'){
   const index=data?.operations.findIndex(o=>o.id===message.data?.id)??-1;
   if(index>=0){fullOperations.set(message.data.id,message.data);data.operations[index]=message.data;if(ui.detail?.type==='operation'&&ui.detail.id===message.data.id)renderDetail();}
  }else if(message.type==='operation-review'){const operation=data?.operations.find(item=>item.id===message.id&&item.reviewToken===message.reviewToken);if(operation){operation.reviewed=true;toast('Marqué comme examiné.',operation);const full=fullOperations.get(operation.id);if(full)full.reviewed=true;render();}}else if(message.type==='operation-diagnostic'){const operation=data?.operations.find(item=>item.id===message.id);if(operation){operation.diagnostic=message.text;if(ui.detail?.type==='operation'&&ui.detail.id===message.id)renderDetail();}}else if(message.type==='notice'&&!message.text?.startsWith('Marqué comme examiné.'))toast(message.text||'Action terminée.');
 }
 try{document.body.classList.toggle('compact-rows',localStorage.getItem('compact-rows')==='true');}catch{}
 if(window.chrome?.webview)window.chrome.webview.addEventListener('message',event=>receive(event.data));
 window.RelayUI={receive,route:parameters=>{
  Object.assign(ui,{generalFeed:parameters.feed||'tasks',advanced:false,messageFilter:'all'});
  if(parameters.project)navigate('project',parameters.project,parameters.tab||'summary');else if(parameters.view)navigate(parameters.view);else navigate('general');
  if(parameters.feed){ui.generalFeed=parameters.feed;render();}
  if(parameters.scope&&['general','projects','mcp'].includes(parameters.scope)){ui.settingScope=parameters.scope;render();}
  if(parameters.session){ui.sessionId=parameters.session;ui.tab='conversation';render();}
  if(parameters.detail)detail(parameters.detail,parameters.id);
 }};
 host('ready');
})();
