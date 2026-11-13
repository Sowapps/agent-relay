window.CleanupChecks=(()=>{
 const click=s=>document.querySelector(s).click();
 const show=()=>{click('[data-action="navigate"][data-view="settings"]');click('[data-action="settings-scope"][data-value="mcp"]');};
 return {
 summary:()=>{show();return document.body.textContent.includes('Mise à jour de cette installation')&&document.body.textContent.includes('Transport générique');},
 preview:()=>{document.querySelector('[data-key="mcp-10000000-0000-0000-0000-000000000001"]').open=true;return true;},
 details:()=>{document.querySelector('[data-key="update-technical"]').open=true;return true;},
 changed:()=>{const f=structuredClone(__fixture);f.mcpTransports.connections[0].seenAt='2026-11-13T16:00:01+01:00';RelayUI.receive({type:'snapshot',data:f});return document.querySelector('[data-key="update-technical"]').open&&document.querySelector('[data-key="mcp-10000000-0000-0000-0000-000000000001"]').open;},
 partial:()=>{const f=structuredClone(__fixture);f.installationUpdate.phase='unverified';f.installationUpdate.message='Le suivi ne peut pas être vérifié. Contacter le mainteneur du relais.';RelayUI.receive({type:'snapshot',data:f});return document.body.textContent.includes('État à vérifier');},
 compact:()=>{document.querySelectorAll('details').forEach(d=>d.open=false);return document.querySelector('#app-content').scrollWidth<=document.querySelector('#app-content').clientWidth+1;}
 };
})();
