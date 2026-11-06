using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record McpFailureData(string Code, string Message, string Recovery, string Execution, string? DiagnosticId);
public sealed record McpFailureEnvelope(McpFailureData Error);
public sealed record McpConnectionStatus(string Version, string ContractHash, string Generation, McpHostStatus Host, string? InstanceId, string? BrokerVersion, bool ClientRefreshRequired, bool AutomaticClientReload, string Recovery);
public sealed record McpHostStatus(McpIdentity Identity, Guid ConnectionId, Guid EndpointId, Guid[] ProjectIds);
public sealed record McpIdentity(string Version, string ContractHash, string Generation)
{
    private static readonly Lazy<McpIdentity> current = new(Create);
    public static McpIdentity Current => current.Value;
    private static McpIdentity Create()
    {
        var version = typeof(McpIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-rc\.[1-9]\d*)?$")) throw new InvalidDataException("Only stable and numbered RC versions are supported.");
        var contract = Hash(JsonSerializer.Serialize(new { revision = 2, envelopes = EnvelopeSchema(), tools = McpFacade.ToolDefinitions, guides = McpFacade.GuideContents() }, RelayJson.Options));
        var assemblies = new[] { "AgentRelay.Core.dll", "AgentRelay.Infrastructure.dll", "AgentRelay.Windows.dll" };
        var generation = Hash(version + contract + string.Join("", assemblies.Select(name => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name)))))));
        return new(version, contract, generation);
    }
    private static object EnvelopeSchema()
    {
        var types = new SortedDictionary<string, object>(StringComparer.Ordinal);
        void Visit(Type type)
        {
            if (type.IsArray) { Visit(type.GetElementType()!); return; }
            if (type.IsGenericType) { foreach (var item in type.GetGenericArguments()) Visit(item); return; }
            if (type.Namespace is not ("AgentRelay.Core" or "AgentRelay.Infrastructure") || types.ContainsKey(type.FullName!)) return;
            types[type.FullName!] = new { };
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
            var nullable = new NullabilityInfoContext();
            types[type.FullName!] = properties.Select(item => new { name = RelayJson.Options.PropertyNamingPolicy!.ConvertName(item.Name), type = item.PropertyType.ToString(), nullable = nullable.Create(item).ReadState.ToString() }).ToArray();
            foreach (var property in properties) Visit(property.PropertyType);
        }
        foreach (var type in new[] { typeof(PipeResponse), typeof(ProjectReply), typeof(McpConnectionStatus), typeof(McpFailureEnvelope) }) Visit(type);
        return types;
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class McpErrors
{
    public static object Result(Exception error, string? diagnosticId = null)
    {
        var code = error is RelayException relay ? relay.Code : error is UnauthorizedAccessException ? "storage_unavailable" : error is IOException ? "storage_unavailable" : "internal_error";
        var message = code switch
        {
            "relay_migrated" => "This relay data scope was migrated. Reload the MCP connection using the current installation; do not recreate the old database.",
            "connection_outdated" => "The relay contract has changed. Refresh this client's MCP tools before continuing. No new work was submitted by this check.",
            "runtime_unconfigured" => "The relay installation is not prepared. Initialize its runtime and explicit participant connection.",
            "relay_unavailable" or "relay_timeout" or "mcp_transport_lost" => "The relay host is unavailable or its response was lost. Start/check the relay and inspect retained results before resubmitting work.",
            "storage_unavailable" => "Relay storage cannot be read. Check the configured installation/data location and its permissions; do not create a replacement database.",
            "internal_error" => "The relay request could not be completed. Inspect the local diagnostic before retrying; completion is not confirmed.",
            _ => error is RelayException ? error.Message : "The relay request could not be completed."
        };
        var recovery = code switch
        {
            "client_catalog_outdated" or "connection_outdated" or "relay_migrated" => "reload_client_connection",
            "runtime_contract_mismatch" => "inspect_relay_runtime",
            _ => "inspect_before_retry"
        };
        return new { isError = true, structuredContent = new McpFailureEnvelope(new(code, message, recovery, error is RelayException { EffectsPossible: false } ? "not_started" : "unconfirmed", diagnosticId)), content = new[] { new { type = "text", text = message + " [" + code + "]" } } };
    }
}
