using System.Text.Json;
using AgentRelay.Infrastructure;
using AgentRelay.Windows;

internal static partial class UiRenderScenarios
{
    public static async Task WindowOpening(string data, string directory)
    {
        await MonitorScenarios.Seed(data);
        var errors = new List<Exception>();
        await using var host = RelayRuntime.Start(data, errors.Add);
        await OnWindow(async owner =>
        {
            owner.Hide();
            Directory.CreateDirectory(directory);
            using var project = new ProjectWindow(data);
            project.OpenFromTray();
            await project.InitializePresentation();
            await Task.Delay(200);
            if (project.WindowState != FormWindowState.Maximized) throw new Exception("Initial tray opening was not maximized.");
            var area = Screen.FromControl(project).WorkingArea;
            var expected = new Rectangle(area.Left + (area.Width - area.Width * 85 / 100) / 2,
                area.Top + (area.Height - area.Height * 85 / 100) / 2, area.Width * 85 / 100, area.Height * 85 / 100);
            static void BoundsMatch(Rectangle actual, Rectangle expected)
            {
                if (Math.Abs(actual.X - expected.X) > 1 || Math.Abs(actual.Y - expected.Y) > 1 ||
                    Math.Abs(actual.Width - expected.Width) > 1 || Math.Abs(actual.Height - expected.Height) > 1)
                    throw new Exception("Restoration bounds differ from screen working area: " + actual + " expected " + expected);
            }
            BoundsMatch(project.RestoreBounds, expected);
            await project.Presentation.CaptureAsync(Path.Combine(directory, "maximized.png"));
            project.Hide(); project.OpenFromTray();
            if (project.WindowState != FormWindowState.Maximized) throw new Exception("Tray reopening removed maximization.");
            project.WindowState = FormWindowState.Normal;
            await Task.Delay(200);
            BoundsMatch(project.Bounds, expected);
            await project.Presentation.CaptureAsync(Path.Combine(directory, "restored.png"));
            project.WindowState = FormWindowState.Minimized; project.OpenFromTray();
            if (project.WindowState != FormWindowState.Maximized) throw new Exception("Minimized tray window was not recovered.");
            project.WindowState = FormWindowState.Normal;
            project.Size = new Size(Math.Max(project.MinimumSize.Width, expected.Width - 40), Math.Max(project.MinimumSize.Height, expected.Height - 40));
            project.Dispose();
            using var fresh = new ProjectWindow(data);
            fresh.OpenFromTray(); await fresh.InitializePresentation();
            if (fresh.WindowState != FormWindowState.Maximized) throw new Exception("A fresh window retained the old state.");
            BoundsMatch(fresh.RestoreBounds, expected);
            await File.WriteAllTextAsync(Path.Combine(directory, "verification.json"), JsonSerializer.Serialize(new
            {
                passed = true, initialMaximized = true, centered85PercentRestore = true, trayRetainsMaximization = true,
                minimizedRecovery = true, freshWindowIgnoresPreviousSize = true, dpi = fresh.DeviceDpi,
                workingArea = new { area.X, area.Y, area.Width, area.Height },
                restore = new { expected.X, expected.Y, expected.Width, expected.Height }, modelCalls = 0, nativeAgentCalls = 0
            }, new JsonSerializerOptions { WriteIndented = true }));
        });
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
