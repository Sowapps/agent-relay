using System.IO.Pipes;
using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class RawPipeScenarios
{
    internal static async Task Run(string data)
    {
        var name = RelayLocation.PipeName(data);
        var request = new PipeRequest("project", AccessToken: "isolated-fixture");
        using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            var reply = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync(); await PipeFrames.ReadAsync<PipeRequest>(server, CancellationToken.None);
                await PipeFrames.WriteAsync(server, JsonSerializer.SerializeToElement(new { project = new { futureField = new { preserved = true } } }), CancellationToken.None);
            });
            var response = await new RelayPipeClient(data).RequestRawAsync(request, CancellationToken.None);
            if (!response.GetProperty("project").GetProperty("futureField").GetProperty("preserved").GetBoolean()) throw new Exception("Raw transport discarded an unknown response field.");
            await reply;
        }
        var admitted = 0;
        var broken = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var lost = Task.Run(async () =>
        {
            await broken.WaitForConnectionAsync(); await PipeFrames.ReadAsync<PipeRequest>(broken, CancellationToken.None);
            Interlocked.Increment(ref admitted); broken.Dispose(); // Effect accepted, response lost.
        });
        try { await new RelayPipeClient(data).RequestRawAsync(request, CancellationToken.None); throw new Exception("A lost reply became success."); }
        catch (RelayException error) when (error.Code == "relay_unavailable" && error.EffectsPossible == true) { }
        await lost;
        if (admitted != 1) throw new Exception("Ambiguous work was replayed.");
        Console.WriteLine("RAW_PIPE=PASS (future fields preserved; lost accepted reply remains uncertain without replay)");
    }
}
