"""Read and validate packaged MCP documentation with a fresh, model-free stdio client."""
import argparse
import ast
import json
from pathlib import Path
import re
import sys
import uuid
from relay_mcp import RelayMcp

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',required=True,type=Path)
parser.add_argument('--assembly',required=True,type=Path)
parser.add_argument('--output',required=True,type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parents[3]
output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
with (output/'facade.stderr.log').open('w',encoding='utf-8') as errors:
    client=RelayMcp({'dotnet':str(args.dotnet.resolve()),'assembly':str(args.assembly.resolve())},output/'facade-data',root,errors)
    try:
        resources=client.call('resources/list')['resources']
        guides={resource['uri']:client.call('resources/read',{'uri':resource['uri']})['contents'][0]['text'] for resource in resources}
        assert set(guides)=={'agent-relay://docs/quickstart','agent-relay://docs/tools','agent-relay://docs/bootstrap','agent-relay://docs/new-project-request','agent-relay://docs/portable-release'}
        example=(root/'apps/agent-relay/doc/using/examples/new-project-request.ps1').read_text(encoding='utf-8-sig')
        assert guides['agent-relay://docs/new-project-request'].replace('\r\n','\n')==example.replace('\r\n','\n')
        assert next(resource for resource in resources if resource['uri']=='agent-relay://docs/new-project-request')['mimeType']=='text/plain'
        assert 'agent-relay://docs/quickstart' in client.initialization['instructions']
        tools={tool['name']:tool['inputSchema'] for tool in client.call('tools/list')['tools']}
        table=guides['agent-relay://docs/tools']
        rows=[line.split('|')[1:4] for line in table.splitlines() if line.startswith('| ') and line.split('|')[1].strip() in tools]
        assert {cells[0].strip() for cells in rows}==set(tools),'Guide tool inventory differs from live schema'
        for name,required,_ in rows:
            required=required.strip()
            assert ([] if required=='none' else [x.strip() for x in required.split(',')])==tools[name.strip()]['required'],name
        examples=re.findall(r'```json\s*(.*?)```',guides['agent-relay://docs/quickstart'],re.S)
        def check(value,schema):
            kind=schema['type'];assert isinstance(value,{'object':dict,'string':str,'integer':int,'boolean':bool}[kind])
            if kind=='object':
                assert set(schema.get('required',[]))<=set(value)
                assert not set(value)-set(schema.get('properties',{}))
                for key,item in value.items():check(item,schema['properties'][key])
            if kind=='string':
                assert len(value)<=schema.get('maxLength',10**9)
                if schema.get('format')=='uuid':uuid.UUID(value)
            if kind=='integer':assert schema.get('minimum',value)<=value<=schema.get('maximum',value)
        for example in examples:
            prepared=re.sub(r'<[^>]+>',lambda _:str(uuid.uuid4()),example)
            call=json.loads(prepared);check(call['arguments'],tools[call['tool']])
        try:client.call('resources/read',{'uri':'agent-relay://docs/../../connections'})
        except RuntimeError:pass
        else:raise AssertionError('Unknown resource path accepted')
    finally:client.close()
# Native endpoint enforces Linux /srv ownership; do not bypass its root guard on Windows.
module=ast.parse((root/'apps/agent-relay/wsl/message_server.py').read_text(encoding='utf-8'))
constants={node.targets[0].id:ast.literal_eval(node.value) for node in module.body if isinstance(node,ast.Assign) and len(node.targets)==1 and isinstance(node.targets[0],ast.Name) and node.targets[0].id in {'native_guide','guide_uri','tool'}}
assert constants['guide_uri']=='agent-relay://docs/native-messages'
assert 'independent' in constants['native_guide'] and 'send_message' in constants['native_guide']
assert constants['tool']['name']=='send_message' and constants['tool']['inputSchema']['required']==['text']
result={'passed':True,'tools':len(tools),'examples':len(examples),'resources':list(guides),'nativeMessageGuide':'Static AST/schema validation; Linux runtime not invoked','modelCalls':0,'agentSessionCalls':0,'scope':'Fresh documentation-only clients; example arguments validated against live schemas, not executed'}
(output/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result))
