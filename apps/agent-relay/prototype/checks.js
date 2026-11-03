// Focused fictional review flow. This never invokes a filesystem/native cleanup operation.
window.CleanupChecks=(()=>{
 const click=selector=>document.querySelector(selector).click();
 const runtime=()=>{const box=document.querySelector('[data-clean-category=runtime]');box.checked=true;box.dispatchEvent(new Event('change',{bubbles:true}));};
 const reset=()=>{CleanupPreview.reset();CleanupPreview.open();};
 return {
  summary:()=>{reset();return document.querySelectorAll('.storage-stats .stat').length===3&&!document.body.textContent.includes('Ã')&&!CleanupPreview.eligibleAt('2026-11-02T01:59').includes('log-yesterday')&&CleanupPreview.eligibleAt('2026-11-02T02:00').includes('log-yesterday')&&!CleanupPreview.eligibleAt('2026-11-02T02:00').includes('log-today');},
  preview:()=>{runtime();click('[data-clean-action=analyze]');const state=CleanupPreview.state();return state.plan.entries.some(i=>i.id==='runtime-unused')&&!state.plan.entries.some(i=>i.references?.length||i.unknown||i.required||i.active);},
  details:()=>{click('[data-clean-id=runtime-unused]');return document.querySelector('.detail-panel').textContent.includes('Démarrage automatique')&&document.querySelector('.detail-panel').textContent.includes('Aucune utilisation dans cet exemple');},
  changed:()=>{click('[data-clean-action=close-detail]');CleanupPreview.scenario('runtime-use');click('[data-clean-action=confirm]');const state=CleanupPreview.state();return state.result.entries.every(i=>i.status==='Conservé')&&state.items.some(i=>i.id==='runtime-unused')&&!state.result.bytes;},
  partial:()=>{reset();click('[data-clean-action=analyze]');CleanupPreview.scenario('failure');click('[data-clean-action=confirm]');click('[data-clean-action=execute]');const state=CleanupPreview.state();document.querySelector('#app-content').scrollTop=10000;return state.result.entries.some(i=>i.status==='Erreur')&&state.result.entries.some(i=>i.status==='Retiré')&&state.items.some(i=>i.id==='log-today')&&state.items.some(i=>i.id==='history');},
  compact:()=>{reset();runtime();CleanupPreview.scenario('unknown');click('[data-clean-action=analyze]');const state=CleanupPreview.state();return !state.plan.entries.some(i=>i.id==='runtime-unused')&&document.querySelector('#app-content').scrollWidth<=document.querySelector('#app-content').clientWidth+1;}
 };
})();
