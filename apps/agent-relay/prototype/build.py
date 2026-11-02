"""Build the navigable fictional prototype from one presentation source, with an explicit draft override."""
import argparse
import hashlib
import json
from pathlib import Path

folder=Path(__file__).resolve().parent
root=folder.parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--assets',type=Path,default=root/'apps/agent-relay/src/AgentRelay.Windows/Presentation/Assets')
parser.add_argument('--output',type=Path,default=root/'local/apps/agent-relay/prototypes/interface-v1/agent-relay-maquette.html')
parser.add_argument('--record-reference',action='store_true')
args=parser.parse_args()
assets=args.assets.resolve()
styles='\n'.join((assets/name).read_text(encoding='utf-8-sig') for name in ['tokens.css','presentation.css','desktop.css'])
scripts='\n'.join((assets/name).read_text(encoding='utf-8-sig') for name in ['components.js','model.js','views.js','renderer.js'])
html=(folder/'shell.html').read_text(encoding='utf-8')
fixture=json.loads((folder/'fixtures.json').read_text(encoding='utf-8'))
boot='window.PrototypeFixtures='+json.dumps(fixture,ensure_ascii=False)+';\n'+(folder/'simulation.js').read_text(encoding='utf-8')
html=html.replace('/* PRESENTATION */',styles).replace('// PRESENTATION',scripts).replace('// SIMULATION',boot).replace('// CONTROLLER',(assets/'controller.js').read_text(encoding='utf-8-sig'))
args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(html,encoding='utf-8',newline='\n')
if args.record_reference:
 (folder/'reviewed.html').write_text(html,encoding='utf-8',newline='\n')
 (folder/'reference.json').write_text(json.dumps({'sha256':hashlib.sha256(html.encode()).hexdigest(),'source':'shared presentation + prototype shell/fixtures/simulation','status':'qualified implementation reference; operator visual acceptance separate'},indent=2)+'\n',encoding='utf-8')
print(str(args.output))
