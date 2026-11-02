# Navigable interface prototype

**Utility:** owns the reproducible fictional review surface and its explicit baseline.  
**Usage:** run `python apps/agent-relay/prototype/build.py` from the checkout to refresh the existing local preview; no application data or native agent is used.

The shell, fixtures and simulation bridge are versioned here. Presentation components have one owner in `src/AgentRelay.Windows/Presentation/Assets`; the builder embeds those exact files into a standalone HTML page. Simulation controls remain outside the application window. They can reset fictitious data, update it repeatedly, simulate a conflict or change a diagnostic state.

For UI corrections, copy presentation assets to an explicit developer draft, modify the draft first and pass `--assets <draft-directory>` to the builder. Review and qualify that prototype before promoting the same files to production. `--record-reference` deliberately records `reviewed.html` and `reference.json` after inspection; do not use it merely to silence a baseline mismatch. Rebuild the prototype after promotion and verify byte equality with the reviewed HTML.

Default output is the operator's existing `local/apps/agent-relay/prototypes/interface-v1/agent-relay-maquette.html` preview. This is an explicit developer export, not an application runtime dependency. A normal checkout can reproduce it without historical ignored prototype sources. No network, account, WSL or model is involved.

The targeted native `monitor-feedback` scenario accepts an asset directory and tests expansion, DOM identity, focus/selection, tabs, modal spacing and conflict drafts while capturing only its own WebView. A passed capture remains an observation, separate from operator visual acceptance.
