using System.Globalization;
using AgentRelay.Windows;

internal static class DateDisplayScenarios
{
    internal static void Run()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var midnight = new DateTimeOffset(new DateTime(2026, 1, 2, 0, 5, 9, DateTimeKind.Local));
            var evening = new DateTimeOffset(new DateTime(2026, 12, 31, 23, 6, 2, DateTimeKind.Local));
            foreach (var culture in new[] { "fr-FR", "en-US", "th-TH" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                if (DisplayDateTime.Format(midnight) != "02/01/2026 00:05" || DisplayDateTime.Format(midnight, seconds: true) != "02/01/2026 00:05:09" ||
                    DisplayDateTime.Format(evening, seconds: true) != "31/12/2026 23:06:02") throw new Exception("Date/time display depends on culture or omits required padding.");
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
        Console.WriteLine("DATE_DISPLAY=PASS (full Gregorian date, 24-hour clock, padding, optional seconds, culture independence)");
    }
}
