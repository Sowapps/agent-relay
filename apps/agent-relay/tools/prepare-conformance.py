"""Prepare frozen-reference and production-presentation pages with identical test-only data."""
from pathlib import Path
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[3]
reference = root / 'local/apps/agent-relay/prototypes/interface-v1'
output = root / 'local/apps/agent-relay/qualification/conformance'
output.mkdir(parents=True, exist_ok=True)
original = reference / 'agent-relay-maquette.html'
digest = hashlib.sha256(original.read_bytes()).hexdigest()
expected = '5de37f5b43aa25c036f23fcc62f2f3fbdf1c6059a18d43f260a368b2b81871c9'
if digest != expected:
    raise RuntimeError('Reviewed prototype changed; inspect and explicitly update the comparison baseline')

frame = """<style id="conformance-frame">html,body{width:100%;height:100%;overflow:hidden}.lab-header,#sim-panel,.stage-caption,.stage-note,.window-chrome,#annotation-layer{display:none!important}.workbench,.stage{display:contents!important}.app-window{position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;min-width:0!important;min-height:0!important;max-width:none!important;border:0!important;border-radius:0!important;box-shadow:none!important;resize:none!important}.app-shell{height:100%!important}</style>"""
setup = """<script>addEventListener('load',()=>{const q=new URLSearchParams(location.search);const click=s=>document.querySelector(s)?.click();const project=q.get('project');if(project&&['atelier','catalogue','exploration'].includes(project))click('[data-action="project"][data-project="'+project+'"]');const tab=q.get('tab');if(['tasks','activity','messages','sessions'].includes(tab))click('[data-action="tab"][data-value="'+tab+'"]');const feed=q.get('feed');if(['tasks','operations','messages'].includes(feed))click('[data-action="general-feed"][data-value="'+feed+'"]');const view=q.get('view');if(['settings','diagnostics'].includes(view))click('[data-action="navigate"][data-view="'+view+'"]');const detail=q.get('detail');if(detail&&['task','operation','message','session','context'].includes(detail)){const id=q.get('id');const selector='[data-action="'+detail+'"]'+(id?'[data-id="'+CSS.escape(id)+'"]':'');click(selector)}document.documentElement.dataset.referenceReady='true'});</script>"""
(reference / 'reference-conformance.html').write_text(original.read_text(encoding='utf-8-sig').replace('</head>', frame + '</head>').replace('</body>', setup + '</body>'), encoding='utf-8')

fixtures = json.loads((reference / 'fixtures.js').read_text(encoding='utf-8-sig').split('=', 1)[1].strip().rstrip(';'))
for operation in fixtures['operations']:
    operation['progressCaption'] = 'avancement fictif'
fixtures['sessionHistory'] = {session['id']: {'loaded': True, 'hasMore': False, 'error': None} for session in fixtures['sessions']}
for session in fixtures['sessions']:
    session['ownerId'] = next((participant['id'] for participant in fixtures['participants'] if participant.get('role') == 'main'), None)
fixtures.update(schemaVersion=1, relay='ready', instances=1, unreadAvailable=True, mcpState='ready', sourceCaption='État simulé · aucun contrôle de votre machine')
(output / 'reference-fixtures.json').write_text(json.dumps(fixtures, ensure_ascii=False, indent=2), encoding='utf-8')
preview = reference / 'conformance-preview'
preview.mkdir(exist_ok=True)
assets = root / 'apps/agent-relay/src/AgentRelay.Windows/Presentation/Assets'
for asset in assets.iterdir():
    if asset.is_file():
        shutil.copy2(asset, preview / asset.name)
html = (preview / 'index.html').read_text(encoding='utf-8')
html = html.replace('<script src="controller.js"></script>', '<script src="qa-fixture.js"></script><script src="controller.js"></script>')
(preview / 'index.html').write_text(html, encoding='utf-8')
fixture_script = "const fixture=" + json.dumps(fixtures, ensure_ascii=False) + ";\n" + """
const scenario=new URLSearchParams(location.search).get('scenario');
if(scenario==='oversize'){Object.assign(fixture.operations[0],{kind:'transfer',title:'Transfert · oversized.bin',state:'failed',errorCode:'file_too_large',result:'Transfert refusé : le fichier dépasse la taille maximale autorisée.'})}
if(scenario==='unsafe'){fixture.messages[0].text='<img src=x onerror=alert(1)> · <script>unexpected()</script>';}
const listeners=[];window.chrome||={};window.qaRequests=[];
window.chrome.webview={addEventListener:(name,listener)=>listeners.push(listener),postMessage:message=>{window.qaRequests.push(message);if(message.action==='ready')queueMicrotask(()=>{listeners.forEach(listener=>listener({data:{type:'snapshot',data:fixture}}));window.RelayUI.route(Object.fromEntries(new URLSearchParams(location.search)));document.documentElement.dataset.previewReady='true'})}};
"""
(preview / 'qa-fixture.js').write_text(fixture_script, encoding='utf-8')

cases = [('general', {}), ('general-operations', {'feed': 'operations'}), ('general-messages', {'feed': 'messages'}),
         ('project', {'project': 'atelier'}), ('project-tasks', {'project': 'atelier', 'tab': 'tasks'}),
         ('project-activity', {'project': 'atelier', 'tab': 'activity'}), ('project-messages', {'project': 'atelier', 'tab': 'messages'}),
         ('project-sessions', {'project': 'atelier', 'tab': 'sessions'}),
         ('task-drawer', {'project': 'atelier', 'detail': 'task', 'id': 't-home'}),
         ('operation-drawer', {'project': 'atelier', 'tab': 'activity', 'detail': 'operation', 'id': 'op1'}),
         ('message-drawer', {'project': 'atelier', 'tab': 'messages', 'detail': 'message', 'id': 'm1'}),
         ('session-drawer', {'project': 'atelier', 'tab': 'sessions', 'detail': 'session', 'id': 's1'}),
         ('context-drawer', {'project': 'atelier', 'detail': 'context'}), ('session-conversation', {'project':'atelier','session':'s1'}), ('settings', {'view': 'settings'}), ('diagnostics', {'view': 'diagnostics'})]
(output / 'comparison-cases.json').write_text(json.dumps({'referenceSha256': digest, 'viewports': [[1120, 728], [960, 648]], 'cases': [{'name': name, **values} for name, values in cases]}, indent=2), encoding='utf-8')
print(f'Prepared {len(cases)} paired views at two client sizes; immutable reference {digest}')
