"""Read one owned session's dispatch/receipt/transcript boundary without input or replay.

Usage: --data <owned data> --session-id <UUID> --request-id <UUID> --output <developer report>.
Never reads project files or credentials; captures only this session's private terminal and relevant transcript turn.
"""
import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import load_layout

SCRIPT = r'''
import json,os,pathlib,sqlite3,subprocess,sys,uuid
c=json.load(sys.stdin);root=pathlib.Path(c['session']['root']);identifier=c['requestId'];client=c['session']['template']['client']
assert root.resolve()==root and root.stat().st_uid==os.getuid()
assert json.loads((root/'provision-owner.json').read_text())=={'sessionId':c['session']['id'],'user':c['session']['template']['user']}
folder=root/client;dispatch=folder/'data'/('dispatch-'+identifier+'.json')
marker=json.loads(dispatch.read_text(encoding='utf-8')) if dispatch.exists() else None
pane=subprocess.run(['tmux','-S',str(root/'tmux.sock'),'capture-pane','-p','-t',client,'-S','-160'],capture_output=True,text=True,timeout=10)
receipt=[]
database=folder/'data/messages.sqlite'
if database.exists():
 db=sqlite3.connect(database.as_uri()+'?mode=ro',uri=True)
 try:receipt=[json.loads(row[0]) for row in db.execute('SELECT payload FROM messages WHERE reply_to=?',(identifier,))]
 finally:db.close()
turn=[];found=False;logs=[]
for path in sorted((folder/'config/sessions').rglob('*.jsonl')):
 if path.resolve()!=path:continue
 with path.open(encoding='utf-8') as stream:
  first=stream.readline()
  if not first:continue
  meta=json.loads(first)
  logs.append({'file':path.name,'nativeId':meta.get('payload',{}).get('id')})
  for line in stream:
   try:event=json.loads(line)
   except ValueError:continue
   payload=event.get('payload') or {};serialized=json.dumps(payload,ensure_ascii=False)
   user=payload.get('role')=='user' or event.get('type')=='event_msg' and payload.get('type')=='user_message'
   if user and identifier in serialized:found=True
   elif user and found:break
   if found and event.get('type') in ['event_msg','response_item']:
    turn.append({'timestamp':event.get('timestamp'),'type':event.get('type'),'payload':payload})
 if found:break
result={'sessionId':c['session']['id'],'requestId':identifier,'dispatchPresent':marker is not None,
 'dispatchPromptCharacters':len(marker.get('prompt','')) if marker else 0,'dispatchAllowLocalWork':(marker or {}).get('request',{}).get('allowLocalWork'),
 'pane':pane.stdout[-18000:],'paneExitCode':pane.returncode,'matchingReceipts':receipt,'requestFoundInTranscript':found,
 'transcriptFiles':logs,'turn':turn[-80:],'modelCalls':0,'terminalInputSent':False}
print(json.dumps(result,ensure_ascii=True))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', required=True, type=Path)
    parser.add_argument('--session-id', required=True, type=uuid.UUID)
    parser.add_argument('--request-id', required=True, type=uuid.UUID)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    data = args.data.resolve()
    root = Path(__file__).resolve().parents[3]
    if not any(data.is_relative_to(root / item) for item in ['var/apps/agent-relay', 'local/apps/agent-relay']):
        raise ValueError('Use an explicit owned relay data scope')
    if not args.output.resolve().is_relative_to(root / 'local/apps/agent-relay'):
        raise ValueError('Private diagnostics must stay in the developer output directory')
    with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as db:
        row = db.execute('SELECT payload FROM sessions WHERE id=?', (str(args.session_id),)).fetchone()
        if row is None:
            raise ValueError('Unknown session')
        session = json.loads(row[0])
        original = db.execute('SELECT payload FROM work WHERE id=?', (str(args.request_id),)).fetchone()
    if not load_layout().is_session(session['root'], args.session_id):
        raise ValueError('Retained runtime does not match the selected session')
    if original is None or json.loads(original[0])['request'].get('sessionId') != str(args.session_id):
        raise ValueError('Retained request does not belong to the selected session')
    profile = session['template']
    response = subprocess.run(['wsl.exe', '-d', profile['distribution'], '-u', profile['user'], '--exec', '/usr/bin/python3', '-c', SCRIPT],
                              input=json.dumps({'session': session, 'requestId': str(args.request_id)}),
                              capture_output=True, text=True, encoding='utf-8', timeout=25, creationflags=subprocess.CREATE_NO_WINDOW)
    if response.returncode:
        raise RuntimeError(response.stderr)
    result = json.loads(response.stdout)
    result['originalPayloadSha256'] = hashlib.sha256(original[0].encode('utf-8')).hexdigest() if original else None
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key not in ['pane', 'turn', 'matchingReceipts']}, ensure_ascii=True))


if __name__ == '__main__':
    main()
