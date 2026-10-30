// Reusable history and task views, independent of provider and transport identities.
window.createRelayViews = (getData,getUI) => {
 const {escape:e,icon,badge,button:b,empty,detailShell}=window.RelayComponents;
 const M=window.RelayModel;
 const project=(data,id)=>data.projects.find(p=>p.id===id);
 const filterContext=ui=>({project:ui.project,participant:ui.participant,period:ui.period,filter:ui.filter,kind:ui.kind,state:ui.state,query:ui.query});
 const options=(items,selected)=>items.map(([v,label])=>`<option value="${e(v)}" ${selected===v?'selected':''}>${e(label)}</option>`).join('');
 const more=(type='tasks')=>{const data=getData(),ui=getUI(),scope=ui.project?project(data,ui.project):data;if(scope?.more)return type==='tasks'?scope.more.operations||scope.more.messages:!!scope.more[type];return scope?.hasMore??data.hasMore;};
 const activeFilters=()=>{const ui=getUI();return ui.period!=='all'||ui.filter!=='all'||ui.participant||ui.kind||ui.state||ui.query||ui.messageFilter&&ui.messageFilter!=='all';};
 function summary(type,total,matched,shown){const feminine=type!=='messages',suffix=(count)=>`${feminine?'e':''}${count>1?'s':''}`;return `<div class="history-count" role="status">${shown} affiché${suffix(shown)} · ${matched} correspondant${suffix(matched)} sur ${total} ${type==='tasks'?'tâches':type==='messages'?'messages':type==='sessions'?'sessions':'opérations'} chargé${suffix(total)}${more(type)?' · historique partiel, éléments précédents disponibles':' · historique chargé'}</div>`;}
 function emptyHistory(type){const filtered=activeFilters(),partial=more(type);return empty('Aucun élément '+(filtered?'correspondant':'enregistré'),filtered?'Les filtres actifs masquent les éléments : vérifiez la période, le participant, l’état ou la recherche.':partial?'Les éléments précédents ne sont pas tous chargés.':'Aucun élément n’est enregistré dans ce contexte.')+(filtered?`<div class="list-footer">${b('Effacer tous les filtres','clear-history','','quiet')}</div>`:'');}
 function pagination(type,matched){const ui=getUI();return matched>ui.limit||more(type)?`<div class="list-footer">${b('Charger plus','more',`data-kind="${type}"`,'quiet')}</div>`:'';}

 const dateLabel=(data,item)=>item.stamp.slice(0,10)===data.referenceDate?item.time:item.stamp.slice(8,10)+'/'+item.stamp.slice(5,7);
 function controls(type='operations'){
  const data=getData(),ui=getUI(),participants=data.participants.filter(p=>!ui.project||project(data,ui.project).participantIds.includes(p.id));
  return `<div class="filter-bar">${type==='messages'?'<span class="muted">Historique des messages</span>':`<div class="segmented" aria-label="État du suivi">${[['all','Tous les états'],['running','En cours'],['follow','À suivre']].map(([v,l])=>`<button data-action="filter" data-value="${v}" class="${ui.filter===v?'active':''}">${l}</button>`).join('')}</div>`}<label class="search-wrap">${icon('search')}<input id="activity-search" aria-label="Rechercher dans l’historique" placeholder="Rechercher dans l’historique…" value="${e(ui.query)}"></label></div><div class="history-controls"><label>Période<select aria-label="Période de l’historique" data-history="period">${options([['today','Aujourd’hui'],['week','7 derniers jours'],['all','Tout l’historique']],ui.period)}</select></label><label>Participant<select aria-label="Participant impliqué" data-history="participant">${options([['','Tous les participants'],...participants.map(p=>[p.id,p.name])],ui.participant)}</select></label>${type==='operations'||type==='tasks'?b(ui.advanced?'Moins de filtres':'Plus de filtres'+([ui.kind,ui.state].filter(Boolean).length?' · '+[ui.kind,ui.state].filter(Boolean).length:''),'advanced-filters','','quiet'):''}${activeFilters()?b('Effacer les filtres','clear-history','','quiet'):''}</div>${ui.advanced&&(type==='operations'||type==='tasks')?`<div class="history-advanced"><label>Type<select aria-label="Type d’opération" data-history="kind">${options([['','Tous les types'],['message','Échange'],['command','Commande'],['transfer','Transfert']],ui.kind)}</select></label><label>État précis<select aria-label="État précis" data-history="state">${options([['','Tous les états'],['running','En cours'],['attention','Autorisation attendue'],['blocked','Bloquée'],['unknown','Résultat incertain'],['failed','Échec'],['completed','Terminée']],ui.state)}</select></label>${b('Effacer les filtres','clear-history','','quiet')}</div>`:''}`;
 }
 function operationRow(op){
  const data=getData(),ui=getUI();
  const agent=op.kind==='command'?'Commande directe':M.actor(data,op);
  return `<button class="activity-row" data-action="operation" data-id="${op.id}" aria-label="Ouvrir : ${e(op.title)}"><span class="activity-symbol">${icon(op.kind==='message'?'message':op.kind==='transfer'?'transfer':'terminal')}</span><span><strong>${e(op.title)}</strong><small>${e(ui.project?agent:project(data,op.project).name+' · '+agent)}${op.reviewed?' · examiné':''}</small></span>${badge(op.state)}<time title="${op.stamp}">${dateLabel(data,op)}</time>${icon('arrow')}</button>`;
 }
 function activity(ops,compact=false){
  const data=getData(),ui=getUI(),rows=ops.filter(o=>M.matches(data,o,filterContext(ui))).sort((a,b)=>b.stamp.localeCompare(a.stamp));
  return (compact?'':controls())+`<div class="card">${rows.length?rows.slice(0,compact?3:ui.limit).map(operationRow).join(''):emptyHistory('operations')}${compact?'':summary('operations',ops.length,rows.length,Math.min(rows.length,ui.limit))+pagination('operations',rows.length)}</div>`;
 }
 function taskList(compact=false){
  const data=getData(),ui=getUI();
  const stamp=task=>M.timeline(data,task.id).at(-1)?.stamp||data.referenceDate;
  const tasks=data.tasks.filter(t=>M.taskMatches(data,t,filterContext(ui))).sort((a,b)=>stamp(b).localeCompare(stamp(a)));
  return (compact?'':controls('tasks'))+`<div class="card">${tasks.slice(0,compact?3:ui.limit).map(task=>{
   const entries=M.timeline(data,task.id),operations=entries.filter(x=>x.eventType==='operation').length,messages=entries.length-operations;
   return `<button class="activity-row task-row" data-action="task" data-id="${task.id}" aria-label="Tâche : ${e(task.title)}"><span class="activity-symbol">${icon('layers')}</span><span><strong>${e(task.title)}</strong><small>${!ui.project?e(project(data,task.project).name)+' · ':''}${operations} opération${operations>1?'s':''} · ${messages} message${messages>1?'s':''}</small></span>${badge(M.taskState(data,task.id))}<time>${entries.length?dateLabel(data,entries.at(-1)):''}</time>${icon('arrow')}</button>`;
  }).join('')||emptyHistory('tasks')}${compact?'':summary('tasks',data.tasks.filter(item=>!ui.project||item.project===ui.project).length,tasks.length,Math.min(tasks.length,ui.limit))+pagination('tasks',tasks.length)}</div>`;
 }
 function taskDetail(id){
  const data=getData(),ui=getUI(),task=data.tasks.find(t=>t.id===id);if(!task)return '';
  const entries=M.timeline(data,id),limit=ui.taskLimit||15,visible=entries.slice(-limit),state=M.taskState(data,id);
  return detailShell('TÂCHE',task.title,badge(state)+`<p style="margin-top:12px">${e(project(data,task.project).name)} · ${e(M.actor(data,task))}</p><div class="detail-section"><h3>Objectif</h3><p>${e(task.description)}</p></div><div class="detail-section"><h3>Un même fil de travail</h3><p class="muted">${entries.filter(x=>x.eventType==='operation').length} opération${entries.filter(x=>x.eventType==='operation').length>1?'s':''} et ${entries.filter(x=>x.eventType==='message').length} message${entries.filter(x=>x.eventType==='message').length>1?'s':''} liés. Les messages indépendants restent distingués des réponses.</p></div><div class="task-timeline">${entries.length>limit?b('Voir les étapes précédentes','task-more','','quiet'):''}${visible.map(item=>`<button class="task-event" data-action="${item.eventType}" data-id="${item.id}"><span class="activity-symbol">${icon(item.eventType==='message'?'message':item.kind==='command'?'terminal':item.kind==='transfer'?'transfer':'activity')}</span><span><strong>${item.eventType==='message'?e(M.actor(data,item))+' · '+(item.kind==='reply'?'réponse':'message indépendant'):e(item.title)}</strong><small>${dateLabel(data,item)} · ${item.eventType==='message'?e(item.text.slice(0,115))+(item.text.length>115?'…':''):e(item.result.slice(0,115))}</small>${item.eventType==='operation'?badge(item.state):''}</span>${icon('arrow')}</button>`).join('')||empty('Aucun élément lié','La tâche est déclarée mais aucune opération n’est enregistrée.')}</div><details class="advanced"><summary>Identité de la tâche</summary><pre>${e(id)}
Projet : ${e(task.project)}
Aucune exécution ni relance automatique.</pre></details>`,b('Voir le projet','task-project',`data-project="${task.project}"`,'quiet'));
 }
 function followUp(op){
  const data=getData(),message=data.messages.find(m=>m.taskId===op.taskId&&m.kind==='independent');
  const descriptor={attention:['Autorisation attendue','La session attend une approbation externe. Cette interface ne l’accorde pas.',b('Examiner la session','session',`data-id="${op.session}"`,'quiet')],blocked:['Information manquante','Le travail est suspendu. Consultez la demande de l’agent avant de décider de la suite.',message?b('Lire le message associé','message',`data-id="${message.id}"`,'quiet'):''],unknown:['Résultat incertain','La demande a été envoyée ; ses effets ne sont pas confirmés. Examinez les traces avant toute nouvelle tentative.',b('Examiner les preuves','show-evidence','','quiet')],failed:['Échec à examiner','Lisez le diagnostic avant de décider de la suite. Aucune relance automatique.',b('Lire le diagnostic','show-evidence','','quiet')]}[op.state];
  return descriptor?`<div class="follow-up ${op.state}"><strong>${descriptor[0]}</strong><p>${descriptor[1]}</p>${descriptor[2]}</div>`:'';
 }
 return {controls,summary,emptyHistory,pagination,more,operationRow,activity,taskList,taskDetail,followUp,filterContext};
};
