using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.Core.Tests;

public class UsageParserTests
{
    private const string SampleJson = """
        {"five_hour":{"utilization":11.0,"resets_at":"2026-11-12T19:29:59.872144+00:00","limit_dollars":null},
         "seven_day":{"utilization":79.0,"resets_at":"2026-11-15T07:59:59.872168+00:00"},
         "seven_day_opus":null,
         "limits":[{"kind":"session","percent":11}]}
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 42, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_five_hour_window()
    {
        var snapshot = UsageParser.Parse(SampleJson, Now);

        Assert.NotNull(snapshot.FiveHour);
        Assert.Equal(11.0, snapshot.FiveHour!.Utilization);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 19, 29, 59, 872, TimeSpan.Zero).AddTicks(1440),
            snapshot.FiveHour.ResetsAt);
    }

    [Fact]
    public void Parses_weekly_window()
    {
        var snapshot = UsageParser.Parse(SampleJson, Now);

        Assert.NotNull(snapshot.Weekly);
        Assert.Equal(79.0, snapshot.Weekly!.Utilization);
        Assert.Equal(2026, snapshot.Weekly.ResetsAt!.Value.Year);
    }

    [Fact]
    public void Keeps_fetch_time()
    {
        Assert.Equal(Now, UsageParser.Parse(SampleJson, Now).FetchedAt);
    }

    [Fact]
    public void Null_window_becomes_null()
    {
        var snapshot = UsageParser.Parse("""{"five_hour":null,"seven_day":null}""", Now);

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.Weekly);
    }

    [Fact]
    public void Window_without_reset_time_has_null_reset()
    {
        var snapshot = UsageParser.Parse("""{"five_hour":{"utilization":0,"resets_at":null}}""", Now);

        Assert.NotNull(snapshot.FiveHour);
        Assert.Null(snapshot.FiveHour!.ResetsAt);
    }

    [Fact]
    public void Garbage_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => UsageParser.Parse("not json", Now));
    }
}
