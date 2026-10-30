using System.Runtime.CompilerServices;
using System.Text.Json;

// Qualification fixture only. No Agent Relay application code is inferred from it.
var request = new ProbeMessage("probe-001", "windows", "fixture", "ping");
var wire = JsonSerializer.Serialize(request);
var decoded = JsonSerializer.Deserialize<ProbeMessage>(wire)
    ?? throw new InvalidOperationException("Message could not be decoded.");
var response = MakeReply(decoded);
if (args.Length == 1 && args[0] == "--self-test")
{
    if (response.Id != request.Id || response.To != request.From || response.Body != "ack:ping")
    {
        Console.Error.WriteLine("NOMINAL_MESSAGE_ROUNDTRIP=FAIL");
        return 1;
    }
    Console.WriteLine("NOMINAL_MESSAGE_ROUNDTRIP=PASS");
    return 0;
}
Console.WriteLine(JsonSerializer.Serialize(response));
return 0;

[MethodImpl(MethodImplOptions.NoInlining)]
static ProbeMessage MakeReply(ProbeMessage input)
{
    // Debugging gate: stop here, inspect input.Id/input.Body, step and continue.
    var reply = new ProbeMessage(input.Id, input.To, input.From, "ack:" + input.Body);
    return reply;
}

internal sealed record ProbeMessage(string Id, string From, string To, string Body);
