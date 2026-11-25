"""Recognize actionable Codex approval frames from the owned live viewport."""

import re

MAX_VIEWPORT = 8000
HEADING = re.compile(
    r'Would you like to run the following command\?'
    r'|Do you want to approve network access to ".+"\?'
    r'|Allow [A-Za-z0-9_-]+\.[A-Za-z0-9_.-]+\?'
)
FOOTER = re.compile(r'Press enter to confirm or esc to cancel(?: or .+ to open thread)?', re.IGNORECASE)
APPROVE = re.compile(r'\d+\. Yes, (?:proceed|just this once|allow once) \(y\)')
REFUSE = re.compile(r"\d+\. No, (?:and tell Codex what to do differently|continue without running it|don['’]t allow) \((esc|n)\)")

def approval_pending(text):
    # A partial live prompt can prevent new dispatch without authorizing any key.
    return any(HEADING.fullmatch(line.strip()) for line in text.splitlines())


def codex_approval(text):
    if len(text) > MAX_VIEWPORT:
        return None
    lines = [line.strip().lstrip('›❯> ').rstrip() for line in text.splitlines() if line.strip()]
    if not lines or not FOOTER.fullmatch(lines[-1]):
        return None
    heading = next((index for index in range(len(lines) - 2, -1, -1) if HEADING.fullmatch(lines[index])), None)
    if heading is None:
        return None
    body = lines[heading:]
    yes = [line for line in body if APPROVE.fullmatch(line)]
    no = [match for line in body if (match := REFUSE.fullmatch(line))]
    if len(yes) != 1 or len(no) != 1 or body.index(yes[0]) >= body.index(no[0][0]):
        return None
    return '\n'.join(body), 'Escape' if no[0][1] == 'esc' else 'n'
