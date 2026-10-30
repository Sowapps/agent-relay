using AgentRelay.Core;
using AgentRelay.Infrastructure;
using System.IO.Pipes;

if (args.Length == 2 && args[0] == "monitor-fixture") { await MonitorScenarios.Seed(Path.GetFullPath(args[1])); return; }
if (args.Length == 1 && args[0] == "monitor-projection") { MonitorScenarios.Run(); return; }
if (args.Length == 3 && args[0] == "monitor-render") { await UiRenderScenarios.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); Console.WriteLine("MONITOR_RENDER=PASS"); return; }
if (args.Length == 4 && args[0] == "monitor-conformance") { await UiRenderScenarios.Conformance(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3])); Console.WriteLine("NATIVE_CONFORMANCE=PASS"); return; }
if (args.Length == 3 && args[0] == "monitor-review") { await UiRenderScenarios.Review(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])); Console.WriteLine("NATIVE_REVIEW=PASS"); return; }
if (args.Length == 3 && args[0] == "monitor-usability") { await UiRenderScenarios.Usability(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])); Console.WriteLine("NATIVE_USABILITY=PASS"); return; }
var data = Path.GetFullPath(args.Single());
MonitorScenarios.Run();
AttentionReviewScenarios.Run(Path.Combine(data,"attention-reviews"));
var errors = new List<Exception>();
var request = LocalDemo.Message("Bonjour depuis le test local.");
RelayExchange first;
await using (var host = RelayRuntime.Start(data, errors.Add))
{
    var client = new RelayPipeClient(data);
    first = await client.SendAsync(request, CancellationToken.None);
    Check(first.Request == request && first.Reply.InReplyTo == request.Id && first.Reply.Body == request.Body, "correlated local exchange");
    Check(await client.SendAsync(request, CancellationToken.None) == first, "duplicate returns original exchange");
    await Refuses("id_conflict", () => client.SendAsync(request with { Body = "different" }, CancellationToken.None));
    await Refuses("invalid_message", () => client.SendAsync(request with { Id = Guid.Empty }, CancellationToken.None));
    await Refuses("route_denied", () => client.SendAsync(request with { Id = Guid.NewGuid(), Recipient = "unconfigured" }, CancellationToken.None));
    await Refuses("not_found", () => client.ReadAsync(Guid.NewGuid(), CancellationToken.None));
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await using (var malformed = new NamedPipeClientStream(".", RelayLocation.PipeName(data), PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        await malformed.ConnectAsync(deadline.Token);
        await malformed.WriteAsync(new byte[] { 255, 255, 255, 127 }, deadline.Token);
        var error = await PipeFrames.ReadAsync<PipeResponse>(malformed, deadline.Token);
        Check(error.Error?.Code == "invalid_frame", "oversized malformed frame refused");
    }
    Check(await client.ReadAsync(request.Id, CancellationToken.None) == first, "listener survives malformed frame");
    await using (var slow = new NamedPipeClientStream(".", RelayLocation.PipeName(data), PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        await slow.ConnectAsync(deadline.Token);
        using var frame = new MemoryStream();
        await PipeFrames.WriteAsync(frame, new PipeRequest("read", RequestId: request.Id, AccessToken: new ConnectionStore(data).Secret(null)), deadline.Token);
        var bytes = frame.ToArray();
        await slow.WriteAsync(bytes.AsMemory(0, 2), deadline.Token);
        Check(await client.ReadAsync(request.Id, deadline.Token).WaitAsync(TimeSpan.FromSeconds(2)) == first,
            "partial client does not block another request");
        await slow.WriteAsync(bytes.AsMemory(2), deadline.Token);
        Check((await PipeFrames.ReadAsync<PipeResponse>(slow, deadline.Token)).Exchange == first, "partial client completes independently");
    }
}
await using (var restarted = RelayRuntime.Start(data, errors.Add))
{
    var client = new RelayPipeClient(data);
    Check(await client.ReadAsync(request.Id, CancellationToken.None) == first, "persisted exchange after host restart");
    Check(await client.SendAsync(request, CancellationToken.None) == first, "restart duplicate remains idempotent");
    try { await using var duplicate = RelayRuntime.Start(data, errors.Add); throw new Exception("Second host was accepted."); }
    catch (IOException) { Console.WriteLine("PASS: second host rejected"); }
}
Check(errors.Count == 0, "no hidden host errors");
var paused = new PausedResponder();
using (var busy = new ExchangeService(new SqliteExchangeStore(Path.Combine(data, "busy")), paused, [LocalDemo.Route]))
{
    var pending = busy.ExchangeAsync(LocalDemo.Message("first"), CancellationToken.None);
    await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try { await Refuses("busy", () => busy.ExchangeAsync(LocalDemo.Message("second"), CancellationToken.None)); }
    finally { paused.Release.TrySetResult(); }
    await pending;
}
Console.WriteLine("APPLICATION_INTEGRATION=PASS");
await WorkScenarios.Run(data);
await SessionScenarios.Run(data + "-sessions");
await ProjectScenarios.Run(data + "-projects");
await ProjectApplicationScenarios.Run(data + "-application");
var boundedInput = new BoundedLines(new StringReader(new string('x', 8193) + "\n{\"ok\":true}\n"), 8192);
try { await boundedInput.ReadAsync(); throw new Exception("Oversized unterminated input was accepted."); }
catch (InvalidDataException) { }
Check(await boundedInput.ReadAsync() == "{\"ok\":true}" && await boundedInput.ReadAsync() is null, "bounded MCP input drains oversized frames and preserves the next request");

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS: " + name);
}

static async Task Refuses(string code, Func<Task<RelayExchange>> action)
{
    try { await action(); throw new InvalidOperationException("Expected refusal: " + code); }
    catch (RelayException error) when (error.Code == code) { Console.WriteLine("PASS: refused " + code); }
}

internal sealed class PausedResponder : IMessageResponder
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<RelayReply> ReplyAsync(RelayMessage message, CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return await new LocalEchoResponder().ReplyAsync(message, cancellationToken);
    }
}
