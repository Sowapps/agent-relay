# Limited Relay POC

This experimental harness exercises a Windows Codex CLI MCP caller and one non-interactive Claude response inside a dedicated WSL tmux session. It is not the tray application or the MVP.

## Authorization

Running any step requires an applicable explicit GO. The approved run is limited to one short Claude response and recovery without another model request. The repository's agent working rules
govern execution. A saved executable or documented command is not standing authorization to run it.

## Components

`relay.py` exposes two stdio MCP tools and a single fixed route. `worker.py` invokes Claude once, writes a correlated result atomically and exits. `run.py` prepares an isolated workspace, invokes a
native Windows Codex MCP caller, checks recovery and cleans experiment-owned Linux resources. Python standard libraries are sufficient; no dependency installation is required.

The [POC reference](../doc/developing/poc-reference.md) describes its current interfaces and limitations. Dated outcomes belong to the observation reports, not this document.

## Explicit steps

The runner has separate prepare, pilot, recover and cleanup steps. A preparation allocates a unique run identifier. Subsequent steps require that identifier and never choose the latest run.
Recovery reads a committed result; it never launches Claude. The pilot step refuses repetition, except for one explicitly requested approval-configuration correction when no task was dispatched.

The Linux run root is outside the home project tree. Workspace, data and socket are separate children of that root. Preparation verifies instruction ancestry; cleanup validates the exact root
before removal. A preflight can verify the prepared files across a distribution restart without invoking a model.

Preparation and the live run require WSL access outside the Windows execution sandbox. No global Codex or Claude configuration file is written. Native clients may maintain their own runtime caches.

## Extended qualification

The separate [four-case qualification](qualification/README.md) exercises direct commands, native interactive clients and independently addressed WSL messages. It does not replace the limited
print-mode POC or retroactively broaden the claims supported by that earlier experiment.
