"""Exercise only documented bootstrap discovery/read routes, with no agent/model/WSL call."""
import argparse
import json
from pathlib import Path
from relay_mcp import RelayMcp
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--data',required=True,type=Path)
p.add_argument('--expected-project',required=True)
p.add_argument('--foreign-project',required=True)
p.add_argument('--output',required=True,type=Path)
a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
runtime=json.loads((a.data/'mcp-runtime.json').read_text(encoding='utf-8-sig'))
with (a.output/'stderr.log').open('w',encoding='utf-8') as errors:
    client=RelayMcp(runtime,a.data,Path(__file__).resolve().parents[3],errors)
    try:
        discovery=client.tool('list_projects')
        assert [x['id'] for x in discovery['configuration']['projects']]==[a.expected_project]
        assert client.tool('list_results',{'project_id':a.expected_project})['operations']['items']==[]
        assert client.tool('list_sessions',{'project_id':a.expected_project})['sessions']['items']==[]
        try:client.tool('list_results',{'project_id':a.foreign_project})
        except RuntimeError:pass
        else:raise AssertionError('Fresh connection exposed another project')
        resources=client.call('resources/list')['resources']
        bootstrap=client.call('resources/read',{'uri':'agent-relay://docs/bootstrap'})['contents'][0]['text']
        assert 'new-project-request.ps1' in bootstrap and 'configure-project.request.json' in bootstrap
        names={item['name'] for item in client.call('tools/list')['tools']}
        assert len(names)==24 and 'configure_projects' not in names
    finally:client.close()
result={'passed':True,'scopedProject':a.expected_project,'foreignProjectRefused':True,'emptyResultsAndSessions':True,'documentationResource':True,'toolCount':24,'nativeModelCalls':0}
(a.output/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result))
