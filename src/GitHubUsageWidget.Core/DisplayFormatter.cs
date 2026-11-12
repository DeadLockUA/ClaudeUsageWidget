using System.Globalization;

namespace GitHubUsageWidget.Core;

public static class DisplayFormatter
{
    public static string Label(WindowKind kind) => kind == WindowKind.FiveHour ? "5h" : "Wk";

    /// <summary>
    /// One line of the taskbar strip. No status words: the verdict is shown by the tail color.
    /// 5h: predicted limit time relative to the reset ("5h 71% +1h18m", "5h 12% -30m").
    /// Weekly: predicted limit clock time ("Wk 19% Fri 17:47").
    /// "OK" when there is no burn to forecast from.
    /// </summary>
    public static string StripLine(Forecast f, DateTimeOffset now, TimeZoneInfo tz)
    {
        var head = $"{Label(f.Kind)} {Math.Round(f.RemainingPercent, MidpointRounding.AwayFromZero):0}%";
        var tail = (f.Verdict, f.Kind) switch
        {
            (Verdict.Idle, _) => "idle",
            (Verdict.Exhausted, WindowKind.FiveHour) when f.ResetsAt is { } reset => Gap(now - reset),
            (Verdict.Exhausted, _) when f.ResetsAt is { } reset => "until " + When(reset, now, tz),
            (_, WindowKind.FiveHour) when f.LimitAt is { } at && f.ResetsAt is { } reset => Gap(at - reset),
            (_, _) when f.LimitAt is { } at => When(at, now, tz),
            _ => "OK",
        };
        return head + " " + tail;
    }

    /// <summary>"21:15" for today, "Fri 21:15" for another day (local time).</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(at, tz);
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var format = local.Date == localNow.Date ? "HH:mm" : "ddd HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Signed compact gap rounded to minutes: "+2h15m", "-30m", "+2d2h".</summary>
    public static string Gap(TimeSpan span)
    {
        var totalMinutes = (long)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        var sign = totalMinutes < 0 ? "-" : "+";
        totalMinutes = Math.Abs(totalMinutes);

        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes / 60 % 24;
        var minutes = totalMinutes % 60;

        var body = days > 0
            ? (hours > 0 ? $"{days}d{hours}h" : $"{days}d")
            : hours > 0
                ? (minutes > 0 ? $"{hours}h{minutes}m" : $"{hours}h")
                : $"{minutes}m";
        return sign + body;
    }

    /// <summary>
    /// Verdict line in the details popup. The 5-hour window shows the predicted limit time relative
    /// to the reset ("+" surplus, "-" time you would sit blocked); the weekly window shows clock times.
    /// </summary>
    public static string DetailsVerdict(Forecast f, DateTimeOffset now, TimeZoneInfo tz)
    {
        switch (f.Verdict)
        {
            case Verdict.Idle:
                return "not started";
            case Verdict.Exhausted:
                return "limit reached";
        }

        if (f.Kind == WindowKind.FiveHour)
        {
            var word = f.Verdict switch
            {
                Verdict.Tight => "tight",
                Verdict.RunsOut => "runs out",
                _ => "enough",
            };
            return f.LimitAt is { } at && f.ResetsAt is { } reset ? $"{word} {Gap(at - reset)}" : word;
        }

        return f.Verdict switch
        {
            Verdict.Ok => "enough until reset",
            Verdict.Tight when f.LimitAt is { } at => $"tight: limit ~{When(at, now, tz)}",
            Verdict.RunsOut when f.LimitAt is { } at => $"runs out {When(at, now, tz)}",
            _ => "",
        };
    }

    /// <summary>Compact duration: "45m", "3h 47m", "2d 16h".</summary>
    public static string Duration(TimeSpan span)
    {
        var totalMinutes = (long)Math.Floor(span.TotalMinutes);
        if (totalMinutes < 1)
            return "<1m";

        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes / 60 % 24;
        var minutes = totalMinutes % 60;

        if (days > 0)
            return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        if (hours > 0)
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        return $"{minutes}m";
    }
}
