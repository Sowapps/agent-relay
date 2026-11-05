"""Own native failure envelopes; human text is diagnostic, never an authority signal."""


class NativeFailure(PermissionError):
    def __init__(self, code, message, effects_possible=True):
        super().__init__(message)
        self.code = code
        self.effects_possible = effects_possible


def envelope(error):
    if isinstance(error, NativeFailure):
        code, effects = error.code, error.effects_possible
    else:
        # Compatibility with existing explicit machine prefixes at native call sites.
        prefix = str(error).partition(':')[0]
        aliases = {'approval_required': 'approval_required', 'destination_exists': 'destination_exists',
                   'credential_reference_unavailable': 'credential_reference_unavailable',
                   'authentication_required': 'agent_authentication_required', 'unavailable': 'unavailable',
                   'command_denied': 'command_denied', 'context_lost': 'context_lost',
                   'execution_unknown': 'execution_unknown', 'client_unavailable': 'client_unavailable'}
        code = aliases.get(prefix)
        if code is None:
            code = 'capability_denied' if isinstance(error, PermissionError) else 'destination_exists' if isinstance(error, FileExistsError) else 'not_found' if isinstance(error, FileNotFoundError) else 'invalid_request' if isinstance(error, ValueError) else 'native_error'
        # Approval is its own retained state; it does not assert either rollback or no effects.
        effects = None if code == 'approval_required' else True
    return {'error': str(error), 'type': type(error).__name__, 'code': code,
            'effectsPossible': effects, 'recovery': 'inspect_before_retry'}
