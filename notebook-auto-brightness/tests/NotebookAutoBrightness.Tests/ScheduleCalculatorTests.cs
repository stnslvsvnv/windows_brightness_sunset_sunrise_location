using System;
using NotebookAutoBrightness;
using Xunit;

namespace NotebookAutoBrightness.Tests;

public sealed class ScheduleCalculatorTests
{
    [Fact]
    public void SunsetTransitionStartsBeforeSunset()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));
        var now = new DateTime(2026, 4, 15, 19, 55, 0);

        var evaluation = ScheduleCalculator.EvaluateBrightness(
            now,
            sunTimes,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));

        Assert.Equal(SchedulePhase.Twilight, evaluation.Phase);
        Assert.Equal(new DateTime(2026, 4, 15, 20, 0, 0), evaluation.NextChange);
        Assert.InRange(evaluation.Brightness, 31, 79);
    }

    [Fact]
    public void SunsetBrightnessIsNightLevelAtSunset()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));
        var now = new DateTime(2026, 4, 15, 20, 0, 0);

        var evaluation = ScheduleCalculator.EvaluateBrightness(
            now,
            sunTimes,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));

        Assert.Equal(SchedulePhase.Night, evaluation.Phase);
        Assert.Equal(30, evaluation.Brightness);
        Assert.Equal(new DateTime(2026, 4, 16, 6, 0, 0), evaluation.NextChange);
    }

    [Fact]
    public void ManualNightTransitionAlsoStartsBeforeNightTime()
    {
        var now = new DateTime(2026, 4, 15, 18, 55, 0);

        var evaluation = ScheduleCalculator.EvaluateBrightness(
            now,
            sunTimes: null,
            dayStartTime: new TimeSpan(7, 0, 0),
            nightStartTime: new TimeSpan(19, 0, 0),
            dayBrightness: 75,
            nightBrightness: 25,
            transitionDuration: TimeSpan.FromMinutes(10));

        Assert.Equal(SchedulePhase.Twilight, evaluation.Phase);
        Assert.Equal(new DateTime(2026, 4, 15, 19, 0, 0), evaluation.NextChange);
        Assert.InRange(evaluation.Brightness, 26, 74);
    }

    [Fact]
    public void ThemeCanSwitchToDarkBeforeSunset()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));
        var now = new DateTime(2026, 4, 15, 19, 46, 0);

        var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
            now,
            sunTimes,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            TimeSpan.FromMinutes(15));

        Assert.False(useLightTheme);
    }

    [Fact]
    public void ThemeCanSwitchToLightBeforeSunrise()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));
        var now = new DateTime(2026, 4, 15, 5, 46, 0);

        var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
            now,
            sunTimes,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            TimeSpan.FromMinutes(15));

        Assert.True(useLightTheme);
    }

    [Fact]
    public void ManualThemeLeadTimeHandlesMidnightWrap()
    {
        var now = new DateTime(2026, 4, 15, 23, 30, 0);

        var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
            now,
            sunTimes: null,
            dayStartTime: new TimeSpan(0, 15, 0),
            nightStartTime: new TimeSpan(18, 0, 0),
            switchLeadTime: TimeSpan.FromMinutes(60));

        Assert.True(useLightTheme);
    }
}
