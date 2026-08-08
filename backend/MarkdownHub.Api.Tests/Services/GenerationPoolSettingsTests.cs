using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class GenerationPoolSettingsTests
{
    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 8, 7, hour, minute, 0, TimeSpan.Zero);

    private static GenerationPoolSettings Window(string? start, string? end) =>
        new(Paused: false, start, end, IntervalSeconds: 60, UsedEntryRetentionDays: 90);

    [Fact]
    public void NoWindow_AllowedAtAnyHour()
    {
        var settings = Window(null, null);

        Assert.True(settings.IsAllowedAt(At(3)));
        Assert.True(settings.IsAllowedAt(At(15)));
    }

    [Fact]
    public void Paused_NeverAllowed_EvenInsideTheWindow()
    {
        var settings = Window("01:00", "05:00") with { Paused = true };

        Assert.False(settings.IsAllowedAt(At(3)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(9, false)]
    public void SameDayWindow_IsInclusiveOfStartAndExclusiveOfEnd(int hour, bool expected)
    {
        Assert.Equal(expected, Window("01:00", "05:00").IsAllowedAt(At(hour)));
    }

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(12, false)]
    public void WindowEndingBeforeItStarts_WrapsPastMidnight(int hour, bool expected)
    {
        Assert.Equal(expected, Window("22:00", "06:00").IsAllowedAt(At(hour)));
    }

    [Fact]
    public void IdenticalStartAndEnd_TreatedAsNoWindow_RatherThanNeverAllowed()
    {
        Assert.True(Window("02:00", "02:00").IsAllowedAt(At(14)));
    }

    [Fact]
    public void HalfSetWindow_IsIgnored()
    {
        Assert.True(Window("02:00", null).IsAllowedAt(At(14)));
        Assert.True(Window(null, "02:00").IsAllowedAt(At(14)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("06:30", true)]
    [InlineData("6:30", false)]
    [InlineData("24:00", false)]
    [InlineData("later", false)]
    public void IsValidTime_AcceptsBlankAndHHmmOnly(string? value, bool expected)
    {
        Assert.Equal(expected, GenerationPoolSettings.IsValidTime(value));
    }
}
