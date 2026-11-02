"""Prepare production captures against the explicitly versioned fictitious reference."""
from pathlib import Path
import hashlib
import json

root=Path(__file__).resolve().parents[3]
reference=root/'apps/agent-relay/prototype'
output=root/'local/apps/agent-relay/qualification/conformance'
output.mkdir(parents=True,exist_ok=True)
expected=json.loads((reference/'reference.json').read_text(encoding='utf-8'))['sha256']
actual=hashlib.sha256((reference/'reviewed.html').read_bytes()).hexdigest()
if expected!=actual:raise RuntimeError('Reviewed prototype baseline changed; review it explicitly before qualification')
fixtures=json.loads((reference/'fixtures.json').read_text(encoding='utf-8'))
(output/'reference-fixtures.json').write_text(json.dumps(fixtures,ensure_ascii=False,indent=2),encoding='utf-8')
cases=[('general',{}),('tasks',{'project':'atelier','tab':'tasks'}),('activity',{'project':'atelier','tab':'activity'}),('messages',{'project':'atelier','tab':'messages'}),('sessions',{'project':'atelier','tab':'sessions'}),('project-settings',{'project':'atelier','tab':'settings'}),('settings',{'view':'settings'}),('projects',{'view':'settings','scope':'projects'}),('mcp',{'view':'settings','scope':'mcp'}),('diagnostics',{'view':'diagnostics'})]
(output/'comparison-cases.json').write_text(json.dumps({'referenceSha256':actual,'viewports':[[1120,728],[960,648]],'cases':[{'name':name,**route} for name,route in cases]},indent=2),encoding='utf-8')
print(f'Prepared {len(cases)} views against versioned reference {actual}')
