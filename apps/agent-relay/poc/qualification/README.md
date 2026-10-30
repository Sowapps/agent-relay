# Four-case Qualification

This isolated harness qualifies direct WSL execution, native interactive Codex and Claude replies, and WSL-originated messages. It is not the production Windows application.

## Components

The Windows stdio facade exposes direct-command and message operations. The WSL stdio endpoint exposes only send_message. A Linux controller manages only run-owned tmux sessions and reads only
their isolated native transcripts. The explicit-step runner prepares, exercises and cleans each identified run.

Read the [qualification reference](../../doc/developing/qualification-reference.md) for implemented interfaces and limitations. Dated results live in the project observation reports.

## Authorization

Each qualification workstream requires an explicit GO. Native clients are started without a model prompt; send steps each create an exclusive marker before the one short prompt is typed. A
failure does not authorize another prompt. The runner does not select or interrupt existing agent sessions and does not stop the WSL distribution.

## Isolation and credentials

Workspace, data, configuration and socket are under one persistent, private run directory outside the home project tree. Native credential files are referenced by symbolic links for native-client
authentication; the controller never reads or prints their values. Cleanup removes the experiment-owned references, not their targets. No user configuration file is edited.
