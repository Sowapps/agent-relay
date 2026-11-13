using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

// One registration owns each published schema and its translation into a host action.
public static class McpToolCatalog
{
    private static object Text(int maximum = 200) => new { type = "string", maxLength = maximum };
    private static object Id() => new { type = "string", format = "uuid" };
    private static Registration Tool(string name, string description, Func<Arguments, ProjectAction> route, object properties, params string[] required)
        => Register(name, description, properties, required, route, false);

    private static Registration Status(string name, string description, object properties)
        => Register(name, description, properties, [], null, true);

    private static Registration Register(string name, string description, object properties, string[] required,
        Func<Arguments, ProjectAction>? route, bool allowStaleCatalog, int revision = 1)
    {
        var schema = new { type = "object", properties, required, additionalProperties = false };
        return new(name, new { name, description, inputSchema = schema }, JsonSerializer.SerializeToElement(schema, RelayJson.Options), route, allowStaleCatalog, revision, description);
    }
    private static object Session() => new { session_id = Id() };
    private static object Message() => new { session_id = Id(), text = Text(8000), request_id = Id(), task_id = Id() };
    private static object Query() => new { project_id = Id(), endpoint_id = Id(), session_id = Id(), task_id = Id(),
        kind = Text(), state = Text(), search = Text(), since = Text(), until = Text(),
        cursor = new { type = "integer", minimum = 0 }, limit = new { type = "integer", minimum = 1, maximum = 100 } };
    private static object Location() => new { type = "object", properties = new { endpoint_id = Id(), workspace_id = Id(), path = Text(500) },
        required = new[] { "endpoint_id", "workspace_id", "path" }, additionalProperties = false };
    private static readonly Registration[] Registrations =
    [
        Status("get_connection_status", "Read this MCP connection's current host version, contract, build generation and client-refresh requirement. No model or privilege change.", new { }),
        Register("list_projects", "Discover only projects, participants, workspace contexts and explicit rights authorized for this bound connection.",
            new { }, [], Discover, allowStaleCatalog: true),
        Tool("set_project_state", "Archive or reactivate an authorized project with explicit project.manage permission. Archival suspends new work, persists its request and waits for existing work before automatic finalization; no task/session is stopped automatically. Archived history stays readable.",
            a => new("set_project_state", a.Id("project_id"), State: a.Text("state"), ExpectedState: a.Text("expected_state")), new { project_id = Id(), state = Text(8), expected_state = Text(8) }, "project_id", "state", "expected_state"),
        Tool("inspect_host", "Read authorized endpoint user/platform/tool versions and selected service presence. No model, secret dump or configuration change.",
            a => new("inspect_host", a.Id("project_id"), a.Id("endpoint_id")), new { project_id = Id(), endpoint_id = Id() }, "project_id", "endpoint_id"),
        Tool("list_profiles", "Compatibility discovery alias for authorized project contexts; no unscoped global profiles are returned.",
            Discover, new { }),
        Tool("execute_command", "Submit an explicitly authorized direct endpoint command in the selected project workspace; no model and no shell string.",
            a => new("execute_command", a.Id("project_id"), a.Id("endpoint_id"), a.Id("workspace_id"), RequestId: a.Id("request_id"), CommandId: a.Text("command_id")), new { project_id = Id(), endpoint_id = Id(), workspace_id = Id(), command_id = Text(), request_id = Id() }, "project_id", "endpoint_id", "workspace_id", "command_id"),
        Tool("open_session", "Name/create an isolated native conversation attached to an existing authorized project workspace; start without a model prompt.",
            a => new("open_session", a.Id("project_id"), a.Id("endpoint_id"), a.Id("workspace_id"), a.Id("session_id"), DisplayName: a.Text("name")), new { project_id = Id(), endpoint_id = Id(), workspace_id = Id(), session_id = Id(), name = Text(SessionNames.MaximumLength) }, "project_id", "endpoint_id", "workspace_id", "session_id", "name"),
        Tool("list_sessions", "Page retained project sessions owned by this participant; inspect one for actual readiness.",
            a => new("list_sessions", Query: a.Query()), Query(), "project_id"),
        Tool("get_session", "Read authorized session context, native readiness and retained conversation identity.",
            GetSession, Session(), "session_id"),
        Tool("get_agent_status", "Compatibility alias for get_session using an explicit session identifier.",
            GetSession, Session(), "session_id"),
        Tool("rename_session", "Rename a session without changing its project, owner, workspace or conversation.",
            a => a.Control("rename:" + a.Text("name")), new { session_id = Id(), name = Text(SessionNames.MaximumLength) }, "session_id", "name"),
        Tool("start_session", "Explicit model-free session startup/reconnection; inspect readiness before sending.",
            a => a.Control("start"), Session(), "session_id"),
        Tool("start_agent", "Compatibility alias for start_session; no implicit project/profile selection.",
            a => a.Control("start"), Session(), "session_id"),
        Tool("reopen_session", "Reopen the same retained native conversation; never silently substitute a new one.",
            a => a.Control("reopen"), Session(), "session_id"),
        Tool("send_session_message", "Continue this participant's project session. Independent sessions may work concurrently; retain request_id and poll get_result.",
            SendMessage, Message(), "session_id", "text"),
        Tool("send_agent_message", "Compatibility alias for sending in an explicit authorized session.",
            SendMessage, Message(), "session_id", "text"),
        Tool("interrupt_session", "Request interruption in this owned session. Effects may remain unknown; inspect before any new attempt.",
            a => a.Control("interrupt"), Session(), "session_id"),
        Tool("close_session", "Close an idle owned native terminal; keep the session identity and conversation history.",
            a => a.Control("close"), Session(), "session_id"),
        Tool("get_result", "Read an authorized durable operation, preserving uncertainty and never replaying work.",
            a => new("get_result", RequestId: a.Id("request_id")), new { request_id = Id() }, "request_id"),
        Tool("cancel_operation", "Request cancellation only for this owned session operation. Never implies rollback of effects; inspect the retained result.",
            a => new("cancel_operation", RequestId: a.Id("request_id")), new { request_id = Id() }, "request_id"),
        Tool("inspect_transfer", "Inspect only the owned temporary partial of a retained transfer in an idle session; no final file changes.",
            a => new("inspect_transfer", RequestId: a.Id("request_id")), new { request_id = Id() }, "request_id"),
        Tool("cleanup_transfer", "Explicitly remove only this retained transfer's owned temporary partial after the session is idle. Never removes its final destination or replays a copy.",
            a => new("cleanup_transfer", RequestId: a.Id("request_id")), new { request_id = Id() }, "request_id"),
        Tool("list_results", "Page project operation previews, newest first; filter endpoint/session/task/type/state/content/date. get_result returns full details.",
            a => new("list_results", Query: a.Query()), Query(), "project_id"),
        Tool("list_events", "Page durable project/task/attempt state transitions, newest first. Use task_id to follow related operations without exposing other projects.",
            a => new("list_events", Query: a.Query()), Query(), "project_id"),
        Tool("read_inbox", "Import bounded native pages and read messages addressed to this participant, oldest first after its own cursor. Never executes message content.",
            a => new("read_inbox", Query: a.Query()), Query(), "project_id"),
        Tool("transfer_file", "Submit a project-authorized single-file transfer between explicit source/destination artifacts. Relative paths, explicit replacement, poll get_result for receipt.",
            a => new("transfer_file", SessionId: a.Id("session_id"), RequestId: a.Id("request_id"), Transfer: new(a.Location("source"), a.Location("destination"), a.Replace)),
            new { session_id = Id(), source = Location(), destination = Location(), replace = new { type = "boolean" }, request_id = Id() }, "session_id", "source", "destination")
    ];

    private static readonly Dictionary<string, Registration> ByName = Registrations.GroupBy(item => item.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.MaxBy(item => item.Revision)!, StringComparer.Ordinal);
    private static readonly McpOperationRegistry registry = new(Registrations.Where(item => item.Route is not null)
        .Select(item => new McpOperationRegistration(item.Name, item.Revision, item.Description, item.Schema,
            arguments => item.Route!(new(arguments)))));
    public static IReadOnlyList<McpOperationDescriptor> Operations => registry.Descriptors;
    public static string OperationsHash => registry.Hash;
    public static McpOperationManifest Manifest => new(OperationsHash, Operations);
    internal static object[] Definitions => ByName.Values.Select(item => item.Definition).ToArray();

    /// <summary>Resolve schema/revision only; the host must authenticate and apply current project policy.</summary>
    public static ProjectAction ResolveOperation(McpInvocation invocation) => registry.Resolve(invocation);

    internal static Registration Resolve(string name, JsonElement arguments)
    {
        if (!ByName.TryGetValue(name, out var registration)) throw new RelayException("tool_denied", "Unknown tool.");
        McpOperationRegistry.Validate(arguments, registration.Schema);
        return registration;
    }

    internal sealed record Registration(string Name, object Definition, JsonElement Schema,
        Func<Arguments, ProjectAction>? Route, bool AllowStaleCatalog, int Revision, string Description);

    private static ProjectAction Discover(Arguments _) => new("configuration");
    private static ProjectAction GetSession(Arguments a) => new("get_session", SessionId: a.Id("session_id"));
    private static ProjectAction SendMessage(Arguments a) => new("send_message", SessionId: a.Id("session_id"),
        RequestId: a.Id("request_id"), Text: a.Text("text"), TaskId: a.Id("task_id"));

    internal readonly record struct Arguments(JsonElement Value)
    {
        internal string? Text(string key) => Value.TryGetProperty(key, out var value) ? value.GetString() : null;
        internal Guid? Id(string key) => Value.TryGetProperty(key, out var value) ? value.GetGuid() : null;
        internal ProjectAction Control(string action) => new("control_session", SessionId: Id("session_id"), Control: action);
        internal ReadQuery Query() => new(Id("project_id")!.Value, Id("endpoint_id"), Id("session_id"), Id("task_id"),
            Text("kind"), Text("state"), Text("search"), Text("since") is { } since ? DateTimeOffset.Parse(since) : null,
            Text("until") is { } until ? DateTimeOffset.Parse(until) : null, Value.TryGetProperty("cursor", out var cursor) ? cursor.GetInt64() : null,
            Value.TryGetProperty("limit", out var limit) ? limit.GetInt32() : 50);
        internal ArtifactLocation Location(string key)
        {
            var location = Value.GetProperty(key);
            return new(location.GetProperty("endpoint_id").GetGuid(), location.GetProperty("workspace_id").GetGuid(), location.GetProperty("path").GetString()!);
        }
        internal bool Replace => Value.TryGetProperty("replace", out var replace) && replace.GetBoolean();
    }

}
