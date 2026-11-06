"""Read retained reports from the isolated delivery commands.

@see isolated-delivery.py for the report writer.
"""

from datetime import datetime
import json

from release_artifact import ordinary_directory, ordinary_file


REPORT_FILE = 'result.json'
STATES = ('running', 'passed', 'failed', 'interrupted')
SUMMARY_FIELDS = ('action', 'status', 'startedUtc', 'seconds', 'commit', 'candidate', 'error')


def operation_directory(root, action):
    return ordinary_directory(root / action)


def read_summary(receipt, action):
    with ordinary_file(receipt).open(encoding='utf-8-sig') as source:
        report = json.load(source)
    started = datetime.fromisoformat(report['startedUtc'])
    if report['action'] != action or started.tzinfo is None:
        raise ValueError('Report action or start time is invalid')
    if report['status'] not in STATES:
        raise ValueError('Unknown report status')
    if report.get('seconds') is not None and type(report['seconds']) not in (float, int):
        raise ValueError('Invalid duration')
    if report.get('commit') is not None and not isinstance(report['commit'], str):
        raise ValueError('Invalid source commit')
    return started, {key: report.get(key) for key in SUMMARY_FIELDS} | {'report': str(receipt)}


def latest_reports(root, actions):
    """Order by start time so an older run finishing later stays older."""
    latest = []
    unreadable = []
    for action in actions:
        newest = None
        directory = operation_directory(root, action)
        for folder in directory.iterdir() if directory.exists() else ():
            receipt = folder / REPORT_FILE
            try:
                started, summary = read_summary(receipt, action)
            except (OSError, ValueError, TypeError, KeyError) as error:
                unreadable.append({'report': str(receipt), 'reason': str(error)})
                continue
            if newest is None or (started, folder.name) > newest[:2]:
                newest = (started, folder.name, summary)
        if newest:
            latest.append(newest[2])
    return {'latest': latest, 'unreadable': unreadable}
