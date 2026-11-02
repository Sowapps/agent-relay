// Stateless UI primitives shared by the application preview and its simulation tools.
window.RelayComponents = (() => {
 const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const pad=(value,width=2)=>String(value).padStart(width,'0');
 const parsedDate=value=>{
  if(typeof value!=='string'||!value)return null;
  const date=new Date(/^\d{4}-\d{2}-\d{2}$/.test(value)?value+'T00:00:00':value);
  return Number.isNaN(date.getTime())?null:date;
 };
 const formatDate=value=>{const date=parsedDate(value);return date?`${pad(date.getDate())}/${pad(date.getMonth()+1)}/${pad(date.getFullYear(),4)}`:'Date indisponible';};
 const formatTime=(value,seconds=false)=>{const date=parsedDate(value);return date?`${pad(date.getHours())}:${pad(date.getMinutes())}${seconds?':'+pad(date.getSeconds()):''}`:'Heure indisponible';};
 const formatDateTime=(value,seconds=false)=>`${formatDate(value)} · ${formatTime(value,seconds)}`;
 const paths={relay:'M4 8h16l-5-5M20 16H4l5 5',overview:'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',activity:'M3 12h4l3-7 4 14 3-7h4',message:'M4 4h16v12H9l-5 4V4z',terminal:'m5 6 5 5-5 5M13 17h6',transfer:'M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4',arrow:'m9 5 7 7-7 7',check:'m5 12 4 4L19 6',warning:'m12 3 10 18H2L12 3zm0 6v5m0 3h.01',settings:'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zm-8 4H2m20 0h-2M12 4V2m0 20v-2M6 6 4 4m16 16-2-2M6 18l-2 2M20 4l-2 2',close:'m6 6 12 12M6 18 18 6',clock:'M12 7v5l3 2M22 12a10 10 0 1 0-20 0 10 10 0 0 0 20 0',search:'M21 21l-5-5M18 10a8 8 0 1 0-16 0 8 8 0 0 0 16 0',copy:'M8 8h13v13H8zM16 4V2H2v14h2',archive:'M3 3h18v5H3zM5 8v13h14V8M9 12h6',folder:'M3 6h7l2 3h9v11H3V6z',plus:'M12 4v16M4 12h16',reset:'M4 10a8 8 0 1 1 1 8M4 3v7h7',server:'M3 3h18v7H3zM3 14h18v7H3zM7 6h.01M7 17h.01',pause:'M8 5v14M16 5v14',layers:'m12 2 10 6-10 6L2 8l10-6zM2 13l10 6 10-6M2 18l10 6 10-6',book:'M4 3h7v17H4zM13 3h7v17h-7z'};
 const icon=name=>`<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="${paths[name]||paths.activity}"/></svg>`;
 const labels={running:'En cours',completed:'Terminée',attention:'Autorisation attendue',blocked:'Bloquée',unknown:'Résultat incertain',failed:'Échec',ready:'Disponible',idle:'En veille',offline:'Indisponible',closed:'Fermée',pending:'En attente',created:'Créée',preparing:'Préparation',unverified:'Non vérifié',declared:'Déclaré',stale:'À actualiser',unsupported:'Non disponible',checking:'Vérification en cours'};
 const badge=state=>`<span class="badge ${escape(state)}"><i class="dot ${escape(state)}"></i>${labels[state]||escape(state)}</span>`;
 const button=(label,action,attrs='',cls='',symbol='')=>`<button class="btn ${cls}" data-action="${action}" ${attrs}>${symbol?icon(symbol):''}${label}</button>`;
 const iconButton=(label,action,symbol,attrs='')=>`<button class="icon-button" data-action="${escape(action)}" title="${escape(label)}" aria-label="${escape(label)}" ${attrs}>${icon(symbol)}</button>`;
 const empty=(title,text,symbol='activity')=>`<div class="empty">${icon(symbol)}<h3>${escape(title)}</h3><p>${escape(text)}</p></div>`;
 const facts=rows=>`<div class="card">${rows.map(([label,value])=>`<div class="fact-row"><span class="fact-label">${label}</span><strong>${value}</strong></div>`).join('')}</div>`;
 const pageTitle=(eyebrow,title,subtitle,actions='')=>`<div class="page-title"><div><div class="eyebrow">${eyebrow}</div><h1>${escape(title)}</h1><p>${escape(subtitle)}</p></div>${actions}</div>`;
 const detailShell=(label,title,body,footer='')=>`<div class="detail-backdrop" data-action="close-detail"></div><aside class="detail-panel" role="dialog" aria-modal="false" aria-label="${escape(label)}"><div class="detail-head"><div><small>${label}</small><h2>${escape(title)}</h2></div><button class="icon-button" data-action="close-detail" aria-label="Fermer le détail">${icon('close')}</button></div><div class="detail-body">${body}</div>${footer?`<div class="detail-footer">${footer}</div>`:''}</aside>`;

 const section=(title,intro='',level=2)=>`<header class="section-intro"><h${level}>${escape(title)}</h${level}>${intro?`<p>${escape(intro)}</p>`:''}</header>`;
 const tabs=(items,selected,action,label)=>`<div class="tabs" role="tablist" aria-label="${escape(label)}">${items.map(([id,text,count])=>`<button id="tab-${action}-${escape(id)}" role="tab" aria-controls="panel-${action}" tabindex="${id===selected?'0':'-1'}" aria-selected="${id===selected}" class="${id===selected?'active':''}" data-action="${action}" data-value="${escape(id)}">${escape(text)}${count?`<span title="${count} éléments chargés avant filtrage">${count}</span>`:''}</button>`).join('')}</div>`;
 const panel=(action,selected,body)=>`<section id="panel-${action}" role="tabpanel" aria-labelledby="tab-${action}-${escape(selected)}">${body}</section>`;
 const connectionFacts=connection=>`<span class="connection-facts"><span class="tag">${escape(connection?.kind||'Connexion non renseignée')}</span>${(connection?.facts||[]).map(f=>`<span><b>${escape(f.label)} :</b> ${escape(f.value)}</span>`).join(' ')||`<span>${escape(connection?.name||'Contexte non renseigné')}</span>`}</span>`;
 // Preserve identity and native browser interaction state; no library or full-page refresh.
 const key=node=>node.nodeType!==1?null:node.id||node.dataset.key||(node.tagName==='DETAILS'?'details:'+node.querySelector('summary')?.textContent:null)||(node.dataset.action?[node.tagName,node.dataset.action,node.dataset.id,node.dataset.project,node.dataset.value,node.dataset.view].join('|'):null);
 function reconcile(current,next){
  if(current.nodeType!==next.nodeType||current.nodeName!==next.nodeName){current.replaceWith(next.cloneNode(true));return;}
  if(current.nodeType!==1){if(current.nodeValue!==next.nodeValue)current.nodeValue=next.nodeValue;return;}
  for(const a of [...current.attributes])if(!next.hasAttribute(a.name)&&!(current.tagName==='DETAILS'&&a.name==='open'))current.removeAttribute(a.name);
  for(const a of next.attributes)if(current.getAttribute(a.name)!==a.value&&!(current.tagName==='DETAILS'&&a.name==='open'))current.setAttribute(a.name,a.value);
  const pool=[...current.childNodes],used=new Set();
  let cursor=current.firstChild;
  for(const child of [...next.childNodes]){
   const k=key(child);let match=k?pool.find(n=>!used.has(n)&&key(n)===k):pool.find(n=>!used.has(n)&&!key(n)&&n.nodeType===child.nodeType&&n.nodeName===child.nodeName);
   if(!match){match=child.cloneNode(true);current.insertBefore(match,cursor);}else{used.add(match);if(match!==cursor)current.insertBefore(match,cursor);reconcile(match,child);}
   cursor=match.nextSibling;
  }
  for(const n of pool)if(!used.has(n)&&n.parentNode===current)current.removeChild(n);
  if(current instanceof HTMLInputElement&&current!==document.activeElement){if(current.type==='checkbox')current.checked=next.checked;else if(current.value!==next.value)current.value=next.value;}
  if(current instanceof HTMLSelectElement&&current!==document.activeElement)current.value=next.value;
 }
 function patch(element,html,reset=false){const template=document.createElement('template');template.innerHTML=html;if(reset){element.replaceChildren(template.content.cloneNode(true));return;}const shell=element.cloneNode(false);shell.append(template.content);reconcile(element,shell);}
 return {escape,icon,badge,button,iconButton,empty,facts,pageTitle,detailShell,formatDate,formatTime,formatDateTime,section,tabs,panel,connectionFacts,patch};
})();
