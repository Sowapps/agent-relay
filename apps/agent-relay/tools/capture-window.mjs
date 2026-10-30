// Run from node_repl with its initialized @oai/sky client. Never reuse a prior window handle.
export async function captureRelayWindow(sky, { app, title } = {}) {
    const candidates = (await sky.list_windows()).filter(window =>
        /^Agent Relay(?: · |$)/.test(window.title || '') &&
        (!app || window.app === app) && (!title || window.title === title));
    if (candidates.length !== 1) throw new Error(`Expected one Agent Relay window; found ${candidates.length}. No capture taken.`);
    const selected = candidates[0];
    const window = await sky.get_window({ id: selected.id, app: selected.app });
    if (window.app !== selected.app || window.title !== selected.title)
        throw new Error('Window identity changed during selection. No capture taken.');
    await sky.activate_window({ window });
    return await sky.get_window_state({ window, include_screenshot: true, include_text: false });
}
