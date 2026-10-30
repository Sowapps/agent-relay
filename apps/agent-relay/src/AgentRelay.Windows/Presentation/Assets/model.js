// Pure presentation helpers: identities, capabilities, task aggregates and shared filters.
window.RelayModel = (() => {
 const attentionStates=['unknown','blocked','attention','failed'];
 const needsAttention=item=>attentionStates.includes(item.state)&&!item.reviewed;
 const participant=(data,id)=>data.participants.find(p=>p.id===id);
 const session=(data,id)=>data.sessions.find(s=>s.id===id);
 const connector=(data,sessionId)=>data.connectors.find(c=>c.id===participant(data,session(data,sessionId)?.participantId)?.connectorId);
 const connection=(data,sessionId)=>data.connections.find(c=>c.id===participant(data,session(data,sessionId)?.participantId)?.connectionId);
 const can=(data,sessionId,capability)=>Boolean((session(data,sessionId)?.capabilities||connector(data,sessionId)?.capabilities||[]).includes(capability));
 const actor=(data,item)=>participant(data,item.participantId||item.senderId)?.name||'Participant inconnu';
 const sessionState=(data,s)=>s.state==='closed'?'closed':data.operations.some(o=>o.session===s.id&&o.state==='running')?'running':s.state||'unknown';
 const taskState=(data,id)=>{
  const ops=data.operations.filter(o=>o.taskId===id);
  return [...attentionStates,'running','pending'].find(state=>ops.some(o=>o.state===state))||(ops.length?'completed':'idle');
 };
 const timeline=(data,taskId)=>[
  ...data.operations.filter(o=>o.taskId===taskId).map(o=>({...o,eventType:'operation'})),
  ...data.messages.filter(m=>m.taskId===taskId).map(m=>({...m,eventType:'message'}))
 ].sort((a,b)=>a.stamp.localeCompare(b.stamp)||(a.eventType===b.eventType?0:a.eventType==='operation'?-1:1)||a.id.localeCompare(b.id));
 const sessionEntries=(data,id)=>{
  const current=session(data,id);if(!current)return [];
  const operations=data.operations.filter(item=>item.project===current.project&&item.session===id),messages=data.messages.filter(item=>item.project===current.project&&item.session===id);
  const entries=operations.flatMap(operation=>{
   const rows=[{...operation,eventType:'request',entryId:'request:'+operation.id,text:operation.request||operation.title}];
   if((operation.hasResult||operation.hasResult===undefined&&operation.result||attentionStates.includes(operation.state))&&!messages.some(message=>message.operation===operation.id&&message.kind==='reply'))
    rows.push({...operation,eventType:'result',entryId:'result:'+operation.id,stamp:operation.updatedAt||operation.stamp,text:operation.result||'Résultat non confirmé.'});
   return rows;
  });
  entries.push(...messages.map(message=>({...message,eventType:'message',entryId:'message:'+message.id})));
  const order={request:0,message:1,result:2};
  return entries.sort((a,b)=>a.stamp.localeCompare(b.stamp)||order[a.eventType]-order[b.eventType]||a.entryId.localeCompare(b.entryId));
 };
 const sessionItem=(data,item)=>{const entries=sessionEntries(data,item.id);return {...item,title:item.name,description:item.workspace,initiatorId:item.ownerId,stamp:entries.at(-1)?.stamp||null,state:sessionState(data,item)};};
 const matches=(data,item,filters)=>{
  const ownProject=!filters.project||item.project===filters.project;
  const involved=[item.participantId,item.senderId,item.recipientId,item.initiatorId].filter(Boolean);
  const ownParticipant=!filters.participant||involved.includes(filters.participant);
  const age=item.stamp?(Date.parse(data.referenceDate+'T00:00:00Z')-Date.parse(item.stamp.slice(0,10)+'T00:00:00Z'))/86400000:NaN;
  const period=filters.period==='all'||(age>=0&&(filters.period==='week'?age<7:age===0));
  const state=item.state||'completed';
  const quick=filters.filter==='all'||filters.filter==='follow'&&needsAttention(item)||filters.filter==='running'&&['running','pending'].includes(state);
  const kind=!filters.kind||item.kind===filters.kind;
  const preciseState=!filters.state||state===filters.state;
  const text=[item.title,item.text,item.description,item.result,actor(data,item)].filter(Boolean).join(' ').toLocaleLowerCase('fr');
  return ownProject&&ownParticipant&&period&&quick&&kind&&preciseState&&(!filters.query||text.includes(filters.query.toLocaleLowerCase('fr')));
 };
 const taskMatches=(data,task,filters)=>{
  if(filters.project&&task.project!==filters.project)return false;
  const entries=timeline(data,task.id);
  const summary={...task,stamp:entries.at(-1)?.stamp||data.referenceDate+'T00:00:00',state:taskState(data,task.id),participantId:task.participantId,description:task.description};
  const broad={...filters,query:'',filter:'all',state:''};
  const context=entries.length?entries.some(item=>matches(data,item,broad)):matches(data,summary,broad);
  const state=summary.state;
  const stateMatch=(filters.filter==='all'||filters.filter==='follow'&&entries.some(item=>item.eventType==='operation'&&needsAttention(item))||filters.filter==='running'&&entries.some(item=>['running','pending'].includes(item.state)))&&(!filters.state||filters.state===state);
  const text=!filters.query||(task.title+' '+task.description).toLocaleLowerCase('fr').includes(filters.query.toLocaleLowerCase('fr'))||entries.some(item=>matches(data,item,{...filters,filter:'all',state:''}));
  return context&&stateMatch&&text;
 };
 return {attentionStates,needsAttention,participant,session,connector,connection,can,actor,sessionState,sessionEntries,sessionItem,taskState,timeline,matches,taskMatches};
})();
