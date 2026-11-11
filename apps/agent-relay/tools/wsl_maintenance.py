"""WSL maintenance transport facade. C# owns process deadlines and diagnostics.

@see WslMaintenance.RunAsync and LocalProcess.RunAsync in AgentRelay.Infrastructure.
"""
import json
import subprocess
import time

from runtime_layout import runtime_command


class WslMaintenanceError(RuntimeError):
    def __init__(self, diagnostic):
        self.diagnostic = diagnostic
        super().__init__('WSL maintenance failed (' + diagnostic.get('phase', 'runtime_unconfirmed') + '): '
                         + (diagnostic.get('error') or diagnostic.get('output') or 'Completion is not confirmed'))


def run_script(runtime, data, distribution, user, script, payload, timeout):
    request = {'distribution': distribution, 'user': user, 'script': script,
               'input': json.dumps(payload), 'timeoutSeconds': timeout}
    started = time.monotonic()
    try:
        result = subprocess.run(runtime_command(runtime) + ['wsl-maintenance', '--data-dir', str(data.resolve())],
                                input=json.dumps(request), text=True, encoding='utf-8', capture_output=True,
                                timeout=timeout + 10, creationflags=subprocess.CREATE_NO_WINDOW)
    except subprocess.TimeoutExpired as error:
        raise WslMaintenanceError({'phase': 'runtime_unconfirmed', 'timedOut': True,
                                   'error': 'The maintenance runtime did not return its diagnostic.',
                                   'output': (error.stdout or b'')[-1800:].decode('utf-8', errors='replace'),
                                   'runtimeError': (error.stderr or b'')[-1800:].decode('utf-8', errors='replace'),
                                   'runtimeWaitSeconds': round(time.monotonic() - started, 3),
                                   'effectsPossible': True}) from error
    try:
        diagnostic = json.loads(result.stdout)
    except ValueError as error:
        raise WslMaintenanceError({'phase': 'runtime_unconfirmed', 'error': result.stderr[-1800:],
                                   'output': result.stdout[-1800:],
                                   'runtimeWaitSeconds': round(time.monotonic() - started, 3),
                                   'effectsPossible': True}) from error
    diagnostic['runtimeWaitSeconds'] = round(time.monotonic() - started, 3)
    if result.returncode:
        raise WslMaintenanceError(diagnostic)
    return diagnostic
