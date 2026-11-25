// The prototype uses the production editor with fictional destinations and local simulation only.
(() => {
 const stored=new Map();
 const targets=['windows','wsl'].map(destination=>({
  actorId:'qualification',actor:'Qualification',destination,
  environment:destination==='windows'?'Poste local':'Ubuntu-24.04 · qualification',
  endpointId:destination,endpoint:destination==='windows'?'Commandes Windows':'Commandes WSL',
  workspaceId:'dossier-'+destination,workspace:'Espace de qualification',
  commandId:'signal-remise',command:'Signaler la remise'
 }));
 window.simulateProjectReactions=(action,values,receive)=>{
  if(!['project-reactions-read','project-reactions-save'].includes(action))return false;
  const {project,settings}=values;
  const current=stored.get(project)||{revision:0,rules:[]};
  const reply={type:action,project,targets,settings:current};
  if(action==='project-reactions-save'){
   if(settings.revision!==current.revision)reply.error='Les réglages ont changé. Actualisez avant d’enregistrer.';
   else{
    reply.settings={revision:current.revision+1,rules:structuredClone(settings.rules)};
    stored.set(project,reply.settings);
   }
  }
  queueMicrotask(()=>receive(structuredClone(reply)));
  return true;
 };
})();
