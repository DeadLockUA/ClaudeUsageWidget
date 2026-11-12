using GitHubUsageWidget.Core;

namespace GitHubUsageWidget.Core.Tests;

public class DisplayFormatterTests
{
    // Thursday 2026-11-12 15:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");

    private static Forecast F(WindowKind kind, double remaining, Verdict verdict, DateTimeOffset? limitAt = null, DateTimeOffset? resetsAt = null) =>
        new(kind, remaining, resetsAt ?? Now.AddHours(3), TimeSpan.FromHours(3), 10, RateSource.Recent,
            limitAt is null ? null : limitAt - Now, limitAt, verdict);

    // Strip: no status words; the verdict is carried by the tail color.
    // 5h tail = predicted limit time relative to reset; Wk tail = predicted limit clock time.

    [Fact]
    public void Five_hour_line_without_burn_says_ok()
    {
        Assert.Equal("5h 89% OK", DisplayFormatter.StripLine(F(WindowKind.FiveHour, 89, Verdict.Ok), Now, Kyiv));
    }

    [Fact]
    public void Five_hour_line_shows_surplus_after_reset()
    {
        var limitAt = Now.AddHours(3) + TimeSpan.FromMinutes(78);

        Assert.Equal("5h 71% +1h18m",
            DisplayFormatter.StripLine(F(WindowKind.FiveHour, 71, Verdict.Ok, limitAt), Now, Kyiv));
    }

    [Fact]
    public void Five_hour_line_shows_shortfall_before_reset()
    {
        Assert.Equal("5h 12% -1h",
            DisplayFormatter.StripLine(F(WindowKind.FiveHour, 12, Verdict.RunsOut, Now.AddHours(2)), Now, Kyiv));
    }

    [Fact]
    public void Five_hour_exhausted_shows_remaining_blocked_time()
    {
        Assert.Equal("5h 0% -1h",
            DisplayFormatter.StripLine(F(WindowKind.FiveHour, 0, Verdict.Exhausted, resetsAt: Now.AddHours(1)), Now, Kyiv));
    }

    [Fact]
    public void Weekly_runs_out_shows_only_weekday_and_local_time()
    {
        var limitAt = new DateTimeOffset(2026, 11, 13, 19, 15, 0, TimeSpan.Zero); // Fri 21:15 at UTC+2

        Assert.Equal("Wk 21% Fri 21:15",
            DisplayFormatter.StripLine(F(WindowKind.Weekly, 21, Verdict.RunsOut, limitAt), Now, Kyiv));
    }

    [Fact]
    public void Weekly_ok_still_shows_limit_time()
    {
        var limitAt = new DateTimeOffset(2026, 11, 16, 1, 0, 0, TimeSpan.Zero); // Mon 03:00 at UTC+2

        Assert.Equal("Wk 80% Mon 03:00",
            DisplayFormatter.StripLine(F(WindowKind.Weekly, 80, Verdict.Ok, limitAt), Now, Kyiv));
    }

    [Fact]
    public void Weekly_tight_shows_limit_time()
    {
        var limitAt = Now.AddHours(4); // 21:00 today at UTC+2

        Assert.Equal("Wk 30% 21:00",
            DisplayFormatter.StripLine(F(WindowKind.Weekly, 30, Verdict.Tight, limitAt), Now, Kyiv));
    }

    [Fact]
    public void Weekly_without_burn_says_ok()
    {
        Assert.Equal("Wk 80% OK", DisplayFormatter.StripLine(F(WindowKind.Weekly, 80, Verdict.Ok), Now, Kyiv));
    }

    [Fact]
    public void Weekly_exhausted_shows_reset_time()
    {
        var reset = new DateTimeOffset(2026, 11, 15, 7, 0, 0, TimeSpan.Zero); // Sun 09:00 at UTC+2

        Assert.Equal("Wk 0% until Sun 09:00",
            DisplayFormatter.StripLine(F(WindowKind.Weekly, 0, Verdict.Exhausted, resetsAt: reset), Now, Kyiv));
    }

    [Fact]
    public void Idle_line()
    {
        Assert.Equal("5h 100% idle", DisplayFormatter.StripLine(F(WindowKind.FiveHour, 100, Verdict.Idle), Now, Kyiv));
    }

    [Fact]
    public void Percent_is_rounded()
    {
        Assert.Equal("5h 90% OK", DisplayFormatter.StripLine(F(WindowKind.FiveHour, 89.6, Verdict.Ok), Now, Kyiv));
    }

    [Theory]
    [InlineData(135, "+2h15m")]
    [InlineData(-30, "-30m")]
    [InlineData(120, "+2h")]
    [InlineData(0, "+0m")]
    [InlineData(-0.4, "+0m")]
    [InlineData(3000, "+2d2h")]
    public void Gap_is_signed_and_compact(double minutes, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.Gap(TimeSpan.FromMinutes(minutes)));
    }

    // Details verdict: 5-hour window shows limit time relative to reset (+ surplus / - shortfall).
    [Fact]
    public void Five_hour_enough_shows_surplus_after_reset()
    {
        var f = F(WindowKind.FiveHour, 74, Verdict.Ok, limitAt: Now.AddHours(3) + TimeSpan.FromMinutes(135));

        Assert.Equal("enough +2h15m", DisplayFormatter.DetailsVerdict(f, Now, Kyiv));
    }

    [Fact]
    public void Five_hour_runs_out_shows_shortfall_before_reset()
    {
        var f = F(WindowKind.FiveHour, 20, Verdict.RunsOut, limitAt: Now.AddHours(2.5));

        Assert.Equal("runs out -30m", DisplayFormatter.DetailsVerdict(f, Now, Kyiv));
    }

    [Fact]
    public void Five_hour_tight_shows_surplus()
    {
        var f = F(WindowKind.FiveHour, 40, Verdict.Tight, limitAt: Now.AddHours(3).AddMinutes(20));

        Assert.Equal("tight +20m", DisplayFormatter.DetailsVerdict(f, Now, Kyiv));
    }

    [Fact]
    public void Five_hour_without_burn_is_just_enough()
    {
        Assert.Equal("enough", DisplayFormatter.DetailsVerdict(F(WindowKind.FiveHour, 74, Verdict.Ok), Now, Kyiv));
    }

    [Fact]
    public void Weekly_details_verdicts_are_unchanged()
    {
        var limitAt = new DateTimeOffset(2026, 11, 13, 15, 33, 0, TimeSpan.Zero); // Fri 17:33 at UTC+2

        Assert.Equal("enough until reset", DisplayFormatter.DetailsVerdict(F(WindowKind.Weekly, 80, Verdict.Ok), Now, Kyiv));
        Assert.Equal("runs out Fri 17:33",
            DisplayFormatter.DetailsVerdict(F(WindowKind.Weekly, 19, Verdict.RunsOut, limitAt), Now, Kyiv));
        Assert.Equal("tight: limit ~Fri 17:33",
            DisplayFormatter.DetailsVerdict(F(WindowKind.Weekly, 30, Verdict.Tight, limitAt), Now, Kyiv));
    }

    [Theory]
    [InlineData(WindowKind.FiveHour)]
    [InlineData(WindowKind.Weekly)]
    public void Exhausted_and_idle_details(WindowKind kind)
    {
        Assert.Equal("limit reached", DisplayFormatter.DetailsVerdict(F(kind, 0, Verdict.Exhausted), Now, Kyiv));
        Assert.Equal("not started", DisplayFormatter.DetailsVerdict(F(kind, 100, Verdict.Idle), Now, Kyiv));
    }

    [Theory]
    [InlineData(45, "45m")]
    [InlineData(227, "3h 47m")]
    [InlineData(180, "3h")]
    [InlineData(3840, "2d 16h")]
    [InlineData(2880, "2d")]
    [InlineData(0.5, "<1m")]
    public void Duration_formatting(double minutes, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.Duration(TimeSpan.FromMinutes(minutes)));
    }
}
