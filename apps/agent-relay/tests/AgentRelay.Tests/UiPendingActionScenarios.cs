using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Windows;
using AgentRelay.Windows.Presentation;

internal static partial class UiRenderScenarios
{
    public static Task PendingActions(string fixture, string output) => OnWindow(async window =>
    {
        window.Text = "Agent Relay · Test de rendu des actions";
        Directory.CreateDirectory(output);
        using var view = new RelayWebView();
        window.Controls.Add(view);
        var sent = new List<JsonElement>();
        string? refreshCall = null;
        view.Command += message =>
        {
            var action = message.GetProperty("action").GetString();
            if (action == "mark-reviewed") sent.Add(message);
            if (action == "refresh") refreshCall = message.GetProperty("call").GetString();
        };
        await view.InitializeAsync(Path.Combine(output, "browser-data"));
        var snapshot = JsonNode.Parse(File.ReadAllText(fixture))!;
        var operation = snapshot["operations"]![0]!;
        operation["state"] = "failed";
        operation["reviewed"] = false;
        operation["reviewable"] = true;
        operation["reviewToken"] = "pending-review-test";
        var project = operation["project"]!.GetValue<string>();
        var route = "window.RelayUI.route({project:" + JsonSerializer.Serialize(project) + ",tab:'activity'})";
        const string selector = "'.attention-box [data-action=mark-reviewed]'";
        var refresh = "window.RelayUI.receive({type:'snapshot',data:" + snapshot.ToJsonString() + "});";
        await view.EvaluateForVerificationAsync(refresh + route);
        await WaitFor(view, "!!document.querySelector(" + selector + ")");
        if (await view.EvaluateForVerificationAsync($"(()=>{{const b=document.querySelector({selector});b.click();b.click();return b.disabled&&b.dataset.progress==='Enregistrement…'&&b.getAttribute('aria-busy')==='true'}})()") != "true")
            throw new InvalidOperationException("The first activation did not show and lock the action immediately.");
        await WaitForCalls(1);
        var call = sent[0].GetProperty("call").GetString();
        await view.EvaluateForVerificationAsync(refresh + "window.RelayUI.route({view:'general'});" + route);
        if (await view.EvaluateForVerificationAsync($"document.querySelector({selector}).disabled") != "true")
            throw new InvalidOperationException("A snapshot or view change unlocked the pending action.");
        if (await view.EvaluateForVerificationAsync("""
            (() => {
                const alert = document.querySelector('.attention-box');
                const label = alert.querySelector('.action-progress-label');
                return !label.hidden && label.textContent === 'Enregistrement…'
                    && label.getBoundingClientRect().right <= alert.getBoundingClientRect().right
                    && alert.scrollWidth <= alert.clientWidth;
            })()
            """) != "true")
            throw new InvalidOperationException("The pending status was missing or overflowed its alert.");
        await view.CaptureAsync(Path.Combine(output, "review-pending.png"));
        view.Send(new { type = "action-complete", call = Guid.NewGuid(), success = true });
        await Task.Delay(60);
        if (await view.EvaluateForVerificationAsync($"document.querySelector({selector}).disabled") != "true")
            throw new InvalidOperationException("An unrelated acknowledgement unlocked the action.");
        if (await view.EvaluateForVerificationAsync($"(()=>{{const b=document.querySelector({selector});b.focus();return document.activeElement===b}})()") != "false")
            throw new InvalidOperationException("A disabled review button remained in the keyboard's focus order.");
        await view.VerificationKeyAsync("keyDown", " ", "Space", 32);
        await view.VerificationKeyAsync("keyUp", " ", "Space", 32);
        await Task.Delay(60);
        if (sent.Count != 1) throw new InvalidOperationException("The disabled action accepted a second keyboard activation.");

        view.Send(new { type = "action-complete", call, success = false, error = "Erreur de test. Actualisez avant de réessayer." });
        await WaitFor(view, $"!document.querySelector({selector}).disabled&&document.querySelector('#toast').textContent.includes('Erreur de test')");
        if (await view.EvaluateForVerificationAsync($"(()=>{{const b=document.querySelector({selector});b.focus();return document.activeElement===b}})()") != "true")
            throw new InvalidOperationException("The review button was not keyboard reachable after the failure.");
        await view.VerificationKeyAsync("keyDown", "Enter", "Enter", 13);
        await view.VerificationKeyAsync("keyUp", "Enter", "Enter", 13);
        await WaitForCalls(2);
        if (sent[1].GetProperty("call").GetString() == call)
            throw new InvalidOperationException("A deliberate retry was not independently correlated.");
        view.Send(new { type = "action-complete", call = sent[1].GetProperty("call").GetString(), success = false,
            unconfirmed = true, error = "État incertain." });
        await WaitFor(view, $"document.querySelector({selector}).disabled&&document.querySelector({selector}).dataset.progress==='État à vérifier'");
        await view.EvaluateForVerificationAsync(refresh + "document.querySelector('#toast [data-action=refresh]').click()");
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            while (refreshCall is null) await Task.Delay(15, deadline.Token);
        view.Send(new { type = "action-complete", call = refreshCall, success = true });
        await WaitFor(view, $"!document.querySelector({selector}).disabled");

        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeErrors = new List<Exception>();
        var native = new UiActionStates(nativeErrors.Add);
        string? selected = "one";
        using var first = native.Button("Première", () => selected, "En cours…", () => active.Task);
        using var same = native.Button("Équivalente", () => "one", "En cours…", () => active.Task);
        using var independent = native.Button("Indépendante", () => "two", "En cours…", () => Task.CompletedTask);
        var calls = 0;
        var running = native.RunAsync("one", "En cours…", async () =>
        {
            calls++;
            await active.Task;
            throw new IOException("fixture error");
        });
        await native.RunAsync("one", "En cours…", () => { calls++; return Task.CompletedTask; });
        if (first.Enabled || same.Enabled || !independent.Enabled || calls != 1 || first.Text != "En cours…")
            throw new InvalidOperationException("Native pending state did not isolate equivalent actions.");
        selected = null;
        active.TrySetResult();
        await running;
        if (first.Enabled || !same.Enabled || !independent.Enabled || nativeErrors.Count != 1)
            throw new InvalidOperationException("Native completion did not re-evaluate eligibility after failure.");
        Console.WriteLine("UI_PENDING_ACTIONS=PASS (double click, keyboard, refresh, stale acknowledgement, error, native resource groups)");

        async Task WaitForCalls(int count)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (sent.Count < count) await Task.Delay(15, deadline.Token);
            if (sent.Count != count) throw new InvalidOperationException("Unexpected number of native requests.");
        }
    });
}
