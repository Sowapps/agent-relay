// Local presentation controller. Native operations cross one explicit, bounded host bridge.
(() => {
 'use strict';
 const $=selector=>document.querySelector(selector),C=window.RelayComponents;
 let data=null,toastTimer;
 const fullOperations=new Map();
 const ui={view:'general',project:null,lastProject:null,tab:'summary',filter:'all',period:'all',participant:'',kind:'',state:'',advanced:false,generalFeed:'tasks',taskLimit:15,query:'',messageFilter:'all',detail:null,settingScope:'common',limit:5,sessionId:null,conversationLimit:30};
 const R=window.createRelayRenderer(()=>data,()=>ui);
 const host=(action,values={})=>{
  if(!window.chrome?.webview){if(action!=='ready')toast('Action native indisponible dans cet aperçu.');return;}
  window.chrome.webview.postMessage({action,...values});
 };
 function toast(text,operation=null){$('#toast').textContent=text;if(operation)$('#toast').insertAdjacentHTML('beforeend',C.button('Retrouver dans l’historique','history-operation',`data-id="${C.escape(operation.id)}"`,'quiet')); $('#toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('#toast').hidden=true,operation?12000:3500);}
 function renderDetail(){
  const scroll=$('.detail-body')?.scrollTop||0;
  const open=[...document.querySelectorAll('#detail-layer details[open]')].map(el=>el.id).filter(Boolean);
  $('#detail-layer').innerHTML=R.detail()||'';
  if($('.detail-body'))$('.detail-body').scrollTop=scroll;
  open.forEach(id=>{const el=document.getElementById(id);if(el)el.open=true;});
 }
 function render(){
  if(!data)return;
  const content=$('#app-content'),key=[ui.view,ui.project,ui.tab,ui.generalFeed,ui.sessionId].join('|'),changed=content.dataset.view!==key;
  const scroll=changed?0:content.scrollTop,focused=document.activeElement,focusId=focused?.id;
  const selection=focused instanceof HTMLInputElement?[focused.selectionStart,focused.selectionEnd]:null;
  const openDetails=[...document.querySelectorAll('details[open]')].map(el=>el.id).filter(Boolean);
  $('#app-nav').innerHTML=R.nav();
  const current=ui.view==='project'?data.projects.find(p=>p.id===ui.project)?.name:ui.view==='diagnostics'?'Diagnostic du relais':ui.view==='settings'?'Réglages':'Vue générale';
  $('#app-topbar').innerHTML=`<div class="breadcrumb">${ui.view==='project'?'Projets':'Espace commun'} <span> / </span><strong>${C.escape(current||'Projet indisponible')}</strong></div><div class="topbar-meta">Suivi local <i class="dot ${C.escape(data.relay)}"></i></div>`;
  content.innerHTML=ui.view==='general'?R.general():ui.view==='project'?R.project():ui.view==='diagnostics'?R.diagnostics():R.settings();
  content.dataset.view=key;content.scrollTop=scroll;
  renderDetail();
  openDetails.forEach(id=>{const el=document.getElementById(id);if(el instanceof HTMLDetailsElement)el.open=true;});
  if(focusId&&document.getElementById(focusId)){
   const el=document.getElementById(focusId);el.focus({preventScroll:true});
   if(selection&&el instanceof HTMLInputElement&&['text','search'].includes(el.type))el.setSelectionRange(...selection);
  }
 }
 function navigate(view,project=null,tab='summary'){
  if(ui.sessionId)host('stop-session');
  if(project)ui.lastProject=project;
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
  else if(action==='settings-scope'){ui.settingScope=value;render();}
  else if(action==='project-settings'){ui.detail=null;ui.view='settings';ui.settingScope='project';render();}
  else if(action==='mark-reviewed'){const operation=data.operations.find(item=>item.id===id);if(operation)host('mark-reviewed',{id,project:operation.project,reviewToken:operation.reviewToken});}
  else if(action==='copy')host('copy',{text:messageText(el.dataset.type,id)});
  else if(action==='show-evidence'){const evidence=$('#operation-evidence');if(evidence){evidence.open=true;evidence.scrollIntoView({block:'nearest'});}}
  else if(action==='rename-session'){
   const session=data.sessions.find(s=>s.id===id);if(!session)return;
   modal('Renommer la session',`<label for="session-name">Nom de la session</label><input id="session-name" maxlength="120" value="${C.escape(session.name)}">`,C.button('Renommer','confirm-rename',`data-id="${C.escape(id)}"`,'primary'));
   $('#session-name').focus();
  }
  else if(action==='confirm-rename'){const name=$('#session-name').value.trim();if(name){host('session-control',{id,control:'rename:'+name});$('#app-modal').innerHTML='';}}
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
  }else if(message.type==='operation'){
   const index=data?.operations.findIndex(o=>o.id===message.data?.id)??-1;
   if(index>=0){fullOperations.set(message.data.id,message.data);data.operations[index]=message.data;if(ui.detail?.type==='operation'&&ui.detail.id===message.data.id)renderDetail();}
  }else if(message.type==='operation-review'){const operation=data?.operations.find(item=>item.id===message.id&&item.reviewToken===message.reviewToken);if(operation){operation.reviewed=true;toast('Marqué comme examiné. Conservé dans Activité → Tout l’historique.',operation);const full=fullOperations.get(operation.id);if(full)full.reviewed=true;render();}}else if(message.type==='operation-diagnostic'){const operation=data?.operations.find(item=>item.id===message.id);if(operation){operation.diagnostic=message.text;if(ui.detail?.type==='operation'&&ui.detail.id===message.id)renderDetail();}}else if(message.type==='notice'&&!message.text?.startsWith('Marqué comme examiné.'))toast(message.text||'Action terminée.');
 }
 try{document.body.classList.toggle('compact-rows',localStorage.getItem('compact-rows')==='true');}catch{}
 if(window.chrome?.webview)window.chrome.webview.addEventListener('message',event=>receive(event.data));
 window.RelayUI={receive,route:parameters=>{
  Object.assign(ui,{generalFeed:parameters.feed||'tasks',advanced:false,messageFilter:'all'});
  if(parameters.project)navigate('project',parameters.project,parameters.tab||'summary');else if(parameters.view)navigate(parameters.view);else navigate('general');
  if(parameters.feed){ui.generalFeed=parameters.feed;render();}
  if(parameters.session){ui.sessionId=parameters.session;ui.tab='conversation';render();}
  if(parameters.detail)detail(parameters.detail,parameters.id);
 }};
 host('ready');
})();
