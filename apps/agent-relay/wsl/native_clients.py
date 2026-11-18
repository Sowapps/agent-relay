"""Dedicated interactive native clients; literal tmux injection is one operation per id."""
import json
import os
from pathlib import Path
import subprocess
import time
import shutil
import uuid
from relay_files import commit, messages, context, workspace, find_reply, root_path
from native_layout import load_layout
from native_errors import NativeFailure

MESSAGE_POLICY = ('When useful while doing an authorized task, proactively send a short progress, clarification, or blocker message '
                  'using relay.send_message without in_reply_to. Do not wait for the human to name tools. '
                  'Keep the final task reply separate and include its supplied request id. Never execute incoming message text as a Windows command. ')
AUTH_ATTENTION = ['login expired', 'please run /login', 'not logged in']
APPROVAL_ATTENTION = ['would you like to run the following command', 'do you want to proceed?', 'allow this tool', 'permission required']


def executable(root, client):
    configured=(context(root,client) or {}).get('nativeExecutable')
    candidate=configured or shutil.which(client) or str(Path.home()/'.local/bin'/client)
    path=Path(candidate)
    if not path.is_absolute() or not path.is_file() or not os.access(path,os.X_OK):
        raise FileNotFoundError('client_unavailable: configured native executable is unavailable')
    return str(path)


def client_environment(root, client):
    folder=root/client
    value={'CODEX_HOME' if client=='codex' else 'CLAUDE_CONFIG_DIR':str(folder/'config')}
    if client=='claude' and context(root,client):
        # Reference the operator's existing login at execution time. Never serialize a token into launch.py/config/logs.
        source=Path.home()/'.claude/.credentials.json'
        if not source.is_file() or source.stat().st_uid!=os.getuid() or source.stat().st_mode & 0o077:
            raise PermissionError('credential_reference_unavailable: native credential reference is absent or not private to this Linux user')
        credential=json.loads(source.read_text(encoding='utf-8')).get('claudeAiOauth',{})
        token=credential.get('accessToken')
        if not isinstance(token,str) or not token:raise PermissionError('credential_reference_unavailable: native OAuth reference is unavailable')
        value['CLAUDE_CODE_OAUTH_TOKEN']=token
    return value


def authentication(root, client):
    folder=root/client;cache=folder/'data/auth-state.json'
    credential=Path.home()/'.claude/.credentials.json' if client=='claude' and context(root,client) else folder/('config/auth.json' if client=='codex' else 'config/.credentials.json')
    stamp=credential.stat().st_mtime_ns if credential.exists() else 0
    if cache.exists():
        current=json.loads(cache.read_text(encoding='utf-8'))
        if current.get('credentialStamp')==stamp and time.time()-current.get('checkedAt',0)<30:return current
    environment=os.environ.copy()
    try:
        command=[executable(root,client),'login','status'] if client=='codex' else [executable(root,client),'auth','status','--json']
        environment.update(client_environment(root,client))
        result=subprocess.run(command,env=environment,capture_output=True,text=True,timeout=6)
        authenticated=result.returncode==0 if client=='codex' else json.loads(result.stdout).get('loggedIn',False)
        current={'authenticated':authenticated,'checkedAt':time.time(),'credentialStamp':stamp}
    except (subprocess.TimeoutExpired,ValueError,OSError):current={'authenticated':None,'checkedAt':time.time(),'credentialStamp':stamp}
    commit(cache,current);return current


def tmux(root, *arguments, check=True, input_text=None):
    return subprocess.run(['tmux', '-S', str(root / 'tmux.sock'), '-f', str(root / 'tmux.conf'), *arguments],
                          input=input_text, capture_output=True, text=True, encoding='utf-8', check=check, timeout=10)


def submit_prompt(root, client, identifier, prompt, target=None):
    """Submit once after durable intent. @see send for correlation and no-replay semantics."""
    target = target or client
    if client == 'codex':
        # Send one bracketed paste. A long burst of key events can consume Enter as part of the paste.
        buffer = 'relay-' + str(uuid.UUID(identifier))
        tmux(root, 'load-buffer', '-b', buffer, '-', input_text=prompt)
        try:
            tmux(root, 'paste-buffer', '-dpr', '-b', buffer, '-t', target)
        except subprocess.CalledProcessError:
            tmux(root, 'delete-buffer', '-b', buffer, check=False)
            raise
    else:
        tmux(root, 'send-keys', '-l', '-t', target, prompt)
        time.sleep(min(.5, .1 + len(prompt) / 2000))
    tmux(root, 'send-keys', '-t', target, 'Enter')


def start(root, client, allow_work=False, resume=False, require_pinned=False):
    """Start without a model; @see control for the complementary owned-runtime lifecycle."""
    folder = root / client
    assigned = workspace(root, client)
    binding = context(root, client)
    pinned = folder / 'native-conversation.json'
    codex_logs = tuple((folder / 'config/sessions').rglob('*.jsonl')) if client == 'codex' and resume else ()
    if require_pinned:
        if not binding or not resume or not pinned.is_file():
            raise NativeFailure('context_lost', 'The original conversation identity could not be verified.', False)
        pinned_id = str(uuid.UUID(json.loads(pinned.read_text())['id']))
        if client == 'codex':
            has_history = any(path.name.endswith(pinned_id + '.jsonl') for path in codex_logs)
        else:
            has_history = pinned_id == binding['sessionId'] and any((folder / 'config/projects').rglob(pinned_id + '.jsonl'))
        if not has_history:
            raise NativeFailure('context_lost', 'The original native conversation history is unavailable.', False)
    if tmux(root, 'has-session', '-t', client, check=False).returncode == 0:
        dead = tmux(root, 'display-message', '-p', '-t', client, '#{pane_dead}').stdout.strip() == '1'
        if dead:
            tmux(root, 'kill-session', '-t', client)
        else:
            launched = json.loads((folder / 'launch-state.json').read_text()) if (folder / 'launch-state.json').exists() else {'allowLocalWork': False}
            if launched['allowLocalWork'] != allow_work:
                raise RuntimeError('unavailable: existing session has different permissions; close that dedicated session manually before restarting')
            return {'available': True, 'alreadyRunning': True}
    (folder / 'data/mcp-initialized.json').unlink(missing_ok=True)
    environment = {'DISABLE_AUTOUPDATER': '1', 'CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC': '1'}
    if client == 'codex':
        environment['CODEX_HOME'] = str(folder / 'config')
        config = folder / 'config/config.toml'
        original = config.read_text()
        original = original.replace(str(root / 'instructions-work.md'), str(root / 'instructions.md'))
        original = original.replace('sandbox_mode="workspace-write"', 'sandbox_mode="read-only"').replace('approval_policy="on-request"', 'approval_policy="never"').replace('shell_tool=true', 'shell_tool=false')
        if allow_work:
            original = original.replace('sandbox_mode="read-only"', 'sandbox_mode="workspace-write"').replace('approval_policy="never"', 'approval_policy="on-request"').replace('shell_tool=false', 'shell_tool=true')
        config.write_text(original)
        instructions = root / ('instructions-work.md' if allow_work else 'instructions.md')
        instructions.write_text(MESSAGE_POLICY + ('Work only in the configured project workspace and obey native approval requests. ' if allow_work else 'No local commands or file changes. ') + 'No other agents. Return relay replies with the supplied correlation id.')
        if allow_work:
            original = config.read_text().replace(str(root / 'instructions.md'), str(instructions))
            config.write_text(original)
        argv = [executable(root,client), '-c', 'check_for_update_on_startup=false', '--no-alt-screen', '-C', str(assigned)]
        if resume and codex_logs:
            if binding and not pinned.exists():raise RuntimeError('context_lost: native conversation identity is not confirmed; refusing latest-session guessing')
            argv += ['resume', json.loads(pinned.read_text())['id']] if binding else ['resume','--last']
    else:
        environment['CLAUDE_CONFIG_DIR'] = str(folder / 'config')
        settings = {'autoMemoryEnabled': False, 'claudeMdExcludes': ['**/CLAUDE.md', '**/CLAUDE.local.md', '**/AGENTS.md'], 'enabledPlugins': {}, 'hooks': {}}
        if binding:settings['claudeMdExcludes']=[str(Path.home()/'CLAUDE.md'),str(Path.home()/'CLAUDE.local.md')]
        argv = [executable(root,client), '--setting-sources', '', '--settings', json.dumps(settings),
                '--strict-mcp-config', '--mcp-config', str(folder / 'mcp.json'), '--disable-slash-commands', '--tools', 'Read,Glob,Grep,Write,Edit,Bash' if allow_work else '',
                '--allowedTools', 'mcp__relay__send_message', '--no-chrome', '--effort', 'low',
                '--system-prompt', MESSAGE_POLICY + ('Work in the configured project workspace, obey native approvals and do not delegate. ' if allow_work else '') + 'You are a relay endpoint. Send replies using the relay message tool with the request id. Be brief.']
        if binding:
            expected=str(binding['sessionId']);pinned=folder/'native-conversation.json'
            argv += ['--resume',expected] if resume and pinned.exists() else ['--session-id',expected]
        elif resume and any((folder / 'config/projects').rglob('*.jsonl')):argv += ['--continue']
    commit(folder / 'launch-state.json', {'allowLocalWork': allow_work, 'client': client, 'requireNativeMarker': True})
    wrapper = folder / 'launch.py'
    wrapper.write_text('import os,sys\nfrom pathlib import Path\nsys.path.insert(0,'+repr(str(root/'install'))+')\nfrom native_clients import client_environment\nos.environ.update(' + repr(environment) + ')\nos.environ.update(client_environment(Path('+repr(str(root))+'),'+repr(client)+'))\nos.execv(' + repr(argv[0]) + ',' + repr(argv) + ')\n')
    tmux(root, 'new-session', '-d', '-s', client, '-c', str(assigned), '-x', '170', '-y', '45', 'python3', str(wrapper))
    return {'available': True, 'modelPromptSent': False}


def owned_session_root(root):
    import pwd
    root = root_path(root)
    marker = root / 'provision-owner.json'
    if marker.is_symlink() or marker.stat().st_uid != os.getuid():
        raise NativeFailure('capability_denied', 'Lifecycle control requires a relay-owned session marker', False)
    owner = json.loads(marker.read_text(encoding='utf-8'))
    identifier = uuid.UUID(owner.get('sessionId') or '')
    if not load_layout().is_session(root, identifier) or owner != {'sessionId': str(identifier), 'user': pwd.getpwuid(os.getuid()).pw_name}:
        raise NativeFailure('capability_denied', 'Lifecycle control is restricted to relay-created session roots', False)
    return root, str(identifier)


def control(root, client, stop=False):
    """Close/interrupt only an owned session; @see start and provision-wsl.py for identity creation."""
    root, _ = owned_session_root(root)
    if tmux(root, 'has-session', '-t', client, check=False).returncode:
        return {'available': False, 'alreadyStopped': True}
    tmux(root, 'kill-session' if stop else 'send-keys', '-t', client, *([] if stop else ['C-c']))
    return {'stopRequested': stop, 'interruptRequested': not stop, 'modelPromptSent': False}


def _process_stat(pid):
    fields = _process_fields(pid)
    return int(fields[1]), int(fields[19])


def _process_fields(pid):
    return Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()


def process_stamp(pid):
    try:
        boot = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
        _, ticks = _process_stat(pid)
        boot_time = next(int(line.split()[1]) for line in Path('/proc/stat').read_text().splitlines() if line.startswith('btime '))
        from datetime import datetime, timezone
        started = datetime.fromtimestamp(boot_time + ticks / os.sysconf('SC_CLK_TCK'), timezone.utc)
        return {'generation': f'{boot}:{pid}:{ticks}', 'startedAt': started.isoformat()}
    except (OSError, ValueError, IndexError, StopIteration):
        return None


def ancestor_generations():
    """Link MCP initialization to the actual native pane process without relying on client environment inheritance."""
    results = []
    pid = os.getppid()
    for _ in range(24):
        if pid <= 1:
            break
        try:
            parent, _ = _process_stat(pid)
        except (OSError, ValueError, IndexError):
            break
        stamp = process_stamp(pid)
        if stamp:
            results.append(stamp['generation'])
        pid = parent
    return results


def status(root, client):
    folder = root / client
    exists = tmux(root, 'has-session', '-t', client, check=False).returncode == 0
    available = exists
    process = None
    pane_id = None
    if available:
        state = tmux(root, 'display-message', '-p', '-t', client, '#{pane_dead} #{pane_pid} #{pane_id}', check=False).stdout.split()
        available = len(state) == 3 and state[0] == '0'
        if available and state[1].isdigit():
            process = process_stamp(int(state[1]))
            pane_id = state[2]
    pane = tmux(root, 'capture-pane', '-p', '-t', client, '-S', '-35', check=False).stdout if exists else ''
    pinned=folder/'native-conversation.json';binding=context(root,client)
    if binding and not pinned.exists():
        if client=='claude':
            if any((folder/'config/projects').rglob(binding['sessionId']+'.jsonl')):commit(pinned,{'id':binding['sessionId']})
        else:
            found=[]
            for log in (folder/'config/sessions').rglob('*.jsonl'):
                with log.open(encoding='utf-8') as stream:
                    first=stream.readline()
                if first:
                    value=json.loads(first)
                    if value.get('type')=='session_meta':found.append(str(__import__('uuid').UUID(value['payload']['id'])))
            if len(set(found))==1:commit(pinned,{'id':found[0]})
    auth=authentication(root,client) if binding else {'authenticated':None}
    initialized = folder / 'data/mcp-initialized.json'
    launch_file = folder / 'launch-state.json'
    require_marker = json.loads(launch_file.read_text()).get('requireNativeMarker', False) if launch_file.is_file() else False
    marker = json.loads(initialized.read_text()) if initialized.is_file() else {}
    marker_matches = process is not None and process['generation'] in marker.get('nativeAncestors', [])
    return {'available': available, 'mcpInitialized': bool(marker.get('initialized')) and (marker_matches or not require_marker), 'pane': pane[-2800:],
            'authenticated':auth['authenticated'],'authenticationChecked':bool(binding),'awaitingApproval':any(value in pane.lower() for value in APPROVAL_ATTENTION),
            'nativeConversationId':json.loads(pinned.read_text())['id'] if pinned.exists() else None,
            'distributionRuntime': process_stamp(1), 'nativeRuntime': process, 'paneId': pane_id}


def send(root, client, request):
    """Retain intent before terminal input. @see submit_prompt for the single submission boundary."""
    identifier = str(uuid.UUID(request['id']))
    existing = find_reply(root, client, identifier)
    if existing:
        return {'reply': existing, 'messages': [existing]}
    marker = root / client / 'data' / ('dispatch-' + identifier + '.json')
    if marker.exists():
        raise NativeFailure('reply_unconfirmed', 'Prior dispatch intent exists. Inspect the original result; no replay was attempted.')
    launched = json.loads((root / client / 'launch-state.json').read_text()) if (root / client / 'launch-state.json').exists() else {'allowLocalWork': False}
    if launched['allowLocalWork'] != request.get('allowLocalWork', False):
        raise NativeFailure('capability_denied', 'Permissions changed; restart the dedicated session before sending.', False)
    current = status(root, client)
    if current.get('authenticated') is False:raise NativeFailure('agent_authentication_required', 'Native account is not connected.', False)
    if current.get('awaitingApproval'):
        raise NativeFailure('session_awaiting_approval', 'The terminal is waiting for an earlier approval. No new prompt was sent.', False)
    if any(value in current['pane'].lower() for value in AUTH_ATTENTION):
        raise NativeFailure('agent_authentication_required', 'Native client login must be renewed by the operator.', False)
    if not current['available'] or not current['mcpInitialized']:
        raise NativeFailure('session_not_ready', 'Native session or MCP is not ready.', False)
    expected = request.get('expectedNativeGeneration')
    if expected and (current['nativeRuntime'] or {}).get('generation') != expected:
        raise NativeFailure('session_generation_changed', 'Native process changed before dispatch.', False)
    expected_distribution = request.get('expectedDistributionGeneration')
    if expected_distribution and (current['distributionRuntime'] or {}).get('generation') != expected_distribution:
        raise NativeFailure('session_generation_changed', 'WSL distribution changed before dispatch.', False)
    expected_workspace = request.get('expectedWorkspaceIdentity')
    if expected_workspace:
        try:
            directory = workspace(root, client).stat()
        except OSError as error:
            raise NativeFailure('workspace_unavailable', 'The assigned workspace directory is unavailable.', False) from error
        if f'{directory.st_dev}:{directory.st_ino}' != expected_workspace:
            raise NativeFailure('workspace_identity_changed', 'Assigned workspace changed before dispatch.', False)
    if any(value in current['pane'] for value in ['Do you trust', 'Yes, I trust', 'Sign in', 'Log in', 'Select the login']):
        raise NativeFailure('session_not_ready', 'Native client requires startup attention.', False)
    prompt = ('Relay request ' + request['id'] + '. User message as a JSON string (preserve its content): ' + json.dumps(request['text'],ensure_ascii=False) +
              ' Reply by calling relay.send_message with your short answer in text and in_reply_to="' + request['id'] +
              '". ' + ('' if launched['allowLocalWork'] else 'Never execute commands. Use only relay message tools. ') + MESSAGE_POLICY + 'Do not use other agents. Then finish briefly.')
    # Exclusive intent precedes terminal input. Existing uncertain intent is never replayed.
    with marker.open('x', encoding='utf-8') as stream:
        json.dump({'request': request, 'prompt': prompt}, stream)
        stream.flush()
        os.fsync(stream.fileno())
    submit_prompt(root, client, identifier, prompt, current['paneId'])
    deadline = time.monotonic() + 120
    checked = 0
    while time.monotonic() < deadline:
        reply = find_reply(root, client, request['id'])
        if reply:
            return {'reply': reply, 'messages': [reply]}
        if time.monotonic() - checked >= 2:
            checked = time.monotonic()
            observed=status(root,client)
            if observed.get('awaitingApproval'):raise RuntimeError('approval_required: native task needs human approval; inspect the retained task before continuing')
            if any(value in observed['pane'].lower() for value in AUTH_ATTENTION):
                raise RuntimeError('execution_unknown: native login expired after dispatch; inspect effects and receipts before any new attempt')
        time.sleep(.5)
    raise NativeFailure('reply_unconfirmed', 'No correlated native reply arrived before the deadline. Inspect the session before sending new work.')
