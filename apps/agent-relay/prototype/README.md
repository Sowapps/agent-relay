# Navigable interface prototype

**Utility:** owns the reproducible fictional review surface and its explicit baseline.  
**Usage:** run `python apps/agent-relay/prototype/build.py` from the checkout to refresh the existing local preview; no application data or native agent is used.

The shell, fixtures and simulation bridge are versioned here. Presentation components have one owner in `src/AgentRelay.Windows/Presentation/Assets`; the builder embeds those exact files into a standalone HTML page. Simulation controls remain outside the application window. They can reset fictitious data, update it repeatedly, simulate a conflict or change a diagnostic state.

For UI corrections, copy presentation assets to an explicit developer draft, modify the draft first and pass `--assets <draft-directory>` to the builder. Review and qualify that prototype before promoting the same files to production. `--record-reference` deliberately records `reviewed.html` and `reference.json` after inspection; do not use it merely to silence a baseline mismatch. Rebuild the prototype after promotion and verify byte equality with the reviewed HTML.

Default output is the operator's existing `local/apps/agent-relay/prototypes/interface-v1/agent-relay-maquette.html` preview. This is an explicit developer export, not an application runtime dependency. A normal checkout can reproduce it without historical ignored prototype sources. No network, account, WSL or model is involved.

The targeted native `monitor-feedback` scenario accepts an asset directory and tests expansion, DOM identity, focus/selection, tabs, modal spacing and conflict drafts while capturing only its own WebView. A passed capture remains an observation, separate from operator visual acceptance.

The cleanup tab is a design-only extension in cleanup-preview.js, embedded by the prototype builder but never copied to production assets. Exterior tools let the operator simulate local time around02:00. Analyze, confirm and recheck operate only on a small in-memory fictional list; they cannot inspect/delete real files or call native cleanup. Its design is recorded in the manual-cleanup proposal; implementing it requires a separate GO.

Storage review includes three volume summaries, selectable categories (old versions opt-in), frozen candidates, excluded/protected entries, the common details drawer, confirmation/revalidation and a per-item result. Exterior tools simulate a newly used runtime, an unverifiable dependency and partial failure. All runtime usage checks are fictional; no installed process is queried. The protected-day rule is never overridden.

Run `./apps/agent-relay/prototype/check.ps1` for six bounded own-WebView checks/captures. It reuses the already-built application control through an isolated prototype checker and the existing private SDK/cache; it never rebuilds/deploys the app or runs native cleanup. Reports stay in the explicitly requested developer local/ scope. Human visual acceptance remains separate from passed checks.
