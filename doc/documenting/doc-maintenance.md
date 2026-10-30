# Documentation Maintenance

**Utility:** defines shared documentation surfaces and application source ownership.  
**Usage:** read before writing requirements, recording an arbitration or publishing a technical observation.

## Documentation sources

The operator supplies the generic
[target and documentation guide](<//wsl.localhost/Ubuntu-24.04/home/sowapps/projects/local/specs/agent-knowledge-base/guide-target-spec-et-doc.md>) and
[evidence guide](<//wsl.localhost/Ubuntu-24.04/home/sowapps/projects/local/specs/agent-knowledge-base/guide-preuves.md>). Read them before structuring documentation or recording a decision. These
external references require access to the named WSL distribution. This project does not copy their generic rules or install an upstream package portal.

Use the existing domain names from the canonical
[target maintenance reference](<//wsl.localhost/Ubuntu-24.04/home/sowapps/projects/web-toolkit/doc/documenting/target-spec-maintenance.md#domain-nomenclature>).

## Surface ownership

Root documentation owns common practices. Each application's documentation owns its product and agreed target without dates or historical narratives.
Application history belongs under its own notes. Operator evidence remains canonical at root `notes/evidence/`, with one file per decision and stable identifiers.
This organization is an operator requirement (evidence `2026-10-28-documentation-and-history-have-separate-surfaces`).

The documentation index reaches every documentation file through the indexes of its subdirectories. The root project introduction points to that index without reproducing it.

## Target ownership

The target contains only settled requirements and validated design levels. A mechanism awaiting arbitration stays outside the target, even when recommended in a discussion. A technical volume may
state a decided architectural constraint without choosing the implementation technology.

In Agent Relay, agent-session owns endpoint roles and delegation, configuration owns project membership and authorization, and system I/O owns lifecycle and storage guarantees.
Other applications define their own domains. Shared statements link to their owner rather than being repeated.

## Evidence and observations

Operator decisions retain the exact exchange needed to establish their scope, including the assistant's proposal when an acceptance depends on it. Quotes remain in their original language.
Evidence references use an identifier; they do not create links outside the documentation tree.

An agent's measurement remains an observation awaiting operator validation. Tool output alone does not enter the evidence directory. Validation of an observation does not imply approval of a
technical architecture, installation or implementation.

## Source interpretation

The supplied [tmux agent guide](<//wsl.localhost/Ubuntu-24.04/home/sowapps/projects/local/specs/agent-knowledge-base/guide-agents-tmux.md>) is technical knowledge, including measurements tied to
particular versions and practices from other projects. It is not authorization to send prompts to existing agents, install hooks, bypass permissions, enable automatic retries or create persistent
processes.

The fixed [glossary](../../apps/agent-relay/doc/developing/glossary.md) owns bilingual terminology. Repository prose is English; exact operator quotations retain French.
