# WSL and tmux checks

**Date:** 2026-10-28, Europe/Paris morning.  
**Source:** commands executed from the Windows Agent Relay working directory.  
**Validation:** observations presented to Florent; operator validation remains pending.

## Authorization and scope

> Florent: « Actuellement et ça vaut pour la matinée, WSL n'es tpas utilisé, tu peux donc faire des tests dessus et l'interrompre sans interrompre une autre activité. »
>
> Florent: « Sowapps est mon organisation, mon utilisateur wsl est sowapps, certains de mes repos en ligne sont sous son nom. »

The assistant treated this availability information as authorization and ran the checks. Florent subsequently clarified that a separate explicit workstream GO was required; these earlier checks
did not have that GO (evidence `2026-10-28-work-starts-only-after-explicit-go`). The observations remain recorded, but do not constitute an accepted authorization precedent. The experiments launched
no AI agent, consumed no model turn, changed no client settings, installed no software and created no test source file.

## Access and identity

The ordinary sandbox could not read the supplied WSL UNC paths. A WSL command in that sandbox returned `Wsl/E_ACCESSDENIED`. The approved elevated execution path could read all three guides.

The distribution selected was `Ubuntu-24.04`; an explicit `--user sowapps` returned `sowapps`. Its kernel string was `6.18.33.2-microsoft-standard-WSL2`.

WSL inherited the Windows repository as `/mnt/c/EspaceRestreint/Workspaces/Git/agent-relay`. This observation does not select that mounted directory as a worker workspace.

## Executable discovery

The non-login shell resolved `/usr/bin/tmux` and `/usr/local/bin/codex` but did not resolve `claude`. A login shell resolved Claude at `/home/sowapps/.local/bin/claude`.

The version commands returned:

```text
tmux 3.4
codex-cli 0.153.0
2.1.312 (Claude Code)
```

The first tmux invocation used `--version`, which printed usage; the subsequent `tmux -V` supplied the version above. The first failed Claude lookup does not establish that Claude was absent.

A launcher cannot assume that the Windows-to-WSL process environment has the same PATH as an interactive login shell. Client launch, authentication, model availability and hooks were not tested.

## Disposable terminal probe

A dedicated tmux socket named `agent-relay-probe-2a8b1609-c371-4109-baca-03768e8446d3` hosted one detached session running `/bin/cat`, with tmux user configuration disabled for that isolated server.

The probe sent a unique literal line, waited 200 ms, sent Enter separately, waited 300 ms, then captured the pane. Two exact lines were expected: terminal echo and cat output. The probe cleaned up
only its dedicated server, including on failure.

```text
PANE_COMMAND=cat
LITERAL_ECHO_COUNT=2
CLEANUP=CONFIRMED
PROBE=PASS
```

This establishes one basic terminal input/output exchange. It does not qualify Codex or Claude input behavior, delivery acknowledgment, completion detection, human takeover or task durability.

## Distribution interruption probe

A second uniquely named tmux server hosted a detached cat session. A fresh marker confirmed the session existed. The test terminated only the selected distribution, checked that its name was no
longer in the running-distribution list, then started it again with an explicit user.

A fresh post-restart marker, the missing original session and the user identity confirmed the outcome. Return codes alone were not used as confirmation. Relevant output was:

```text
INITIAL_SESSION=CONFIRMED
DISTRIBUTION_STOP=CONFIRMED_BY_LIST
after-d91cdd9c-ea3a-47d5-b219-e9c5b2473904
OLD_SESSION=ABSENT
RESTART_USER=sowapps
LIFECYCLE_PROBE=PASS
```

The cleanup path checked only the test socket and would remove its server if still present. The target distribution was left restarted; the probe left no live test session.

The result demonstrates loss of that tmux session on distribution termination and a successful WSL command afterward. It does not demonstrate resuming an agent conversation or preserving an
Agent Relay task: neither an agent nor a relay implementation was present.

## Source snapshots

All supplied guides were read. The documentation and evidence guides were read in the combined output; the tmux guide was read again separately to obtain its complete content after truncation.

The file names below identify the supplied source snapshots; the sources themselves remain in their owner's knowledge-base directory.

```text
guide-target-spec-et-doc.md
234 lines
SHA256 f078544ef5dd898728b58cfcefa527f83895b650704892af249bd9bb39cf72a4

guide-agents-tmux.md
271 lines
SHA256 71565761258add667d0bb50ff186f5426229d27d4e6d06475d9b284135a7c1b9

guide-preuves.md
140 lines
SHA256 fa36422d36efe0b1a5d2a844d7327424c62192912c6f5cc362a611bd34a252b4
```

The canonical toolkit domain registry was also read. Domain names used in the target were taken from that registry without modifying the toolkit.

## Outstanding validation

Florent can validate the environment inventory and the two probe outcomes independently. Neither validation selects SQLite, MCP HTTP, a result-spool protocol or a particular pilot/worker client.

The end-to-end feasibility gate remains open: real-client delivery, trustworthy task receipt and completion, result durability, agent conversation recovery and human takeover are not established.
