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

    [Fact]
    public void ThemeLeadEndsTwilightRampAtTheSwitchMoment()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));
        var lead = TimeSpan.FromMinutes(15);
        var shifted = ScheduleCalculator.ShiftForThemeLead(sunTimes, lead);
        var beforeSwitch = new DateTime(2026, 4, 15, 19, 44, 59);
        var atSwitch = new DateTime(2026, 4, 15, 19, 45, 0);

        var twilight = ScheduleCalculator.EvaluateBrightness(
            beforeSwitch,
            shifted,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));
        var night = ScheduleCalculator.EvaluateBrightness(
            atSwitch,
            shifted,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));

        Assert.Equal(SchedulePhase.Twilight, twilight.Phase);
        Assert.Equal(30, twilight.Brightness);
        Assert.Equal(SchedulePhase.Night, night.Phase);
        Assert.Equal(30, night.Brightness);
        Assert.True(ScheduleCalculator.ShouldUseLightTheme(
            beforeSwitch, sunTimes, new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), lead));
        Assert.False(ScheduleCalculator.ShouldUseLightTheme(
            atSwitch, sunTimes, new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), lead));
    }

    [Fact]
    public void ThemeLeadEndsDawnRampAtTheSwitchMoment()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));
        var lead = TimeSpan.FromMinutes(15);
        var shifted = ScheduleCalculator.ShiftForThemeLead(sunTimes, lead);
        var beforeSwitch = new DateTime(2026, 4, 15, 5, 44, 59);
        var atSwitch = new DateTime(2026, 4, 15, 5, 45, 0);

        var dawn = ScheduleCalculator.EvaluateBrightness(
            beforeSwitch,
            shifted,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));
        var day = ScheduleCalculator.EvaluateBrightness(
            atSwitch,
            shifted,
            new TimeSpan(7, 0, 0),
            new TimeSpan(19, 0, 0),
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));

        Assert.Equal(SchedulePhase.Dawn, dawn.Phase);
        Assert.Equal(80, dawn.Brightness);
        Assert.Equal(SchedulePhase.Day, day.Phase);
        Assert.Equal(80, day.Brightness);
        Assert.False(ScheduleCalculator.ShouldUseLightTheme(
            beforeSwitch, sunTimes, new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), lead));
        Assert.True(ScheduleCalculator.ShouldUseLightTheme(
            atSwitch, sunTimes, new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), lead));
    }

    [Fact]
    public void ThemeLeadMovesManualNightTransitionAsWell()
    {
        var lead = TimeSpan.FromMinutes(15);
        var dayStart = new TimeSpan(7, 0, 0) - lead;
        var nightStart = new TimeSpan(19, 0, 0) - lead;

        var twilight = ScheduleCalculator.EvaluateBrightness(
            new DateTime(2026, 4, 15, 18, 44, 59),
            sunTimes: null,
            dayStart,
            nightStart,
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));
        var night = ScheduleCalculator.EvaluateBrightness(
            new DateTime(2026, 4, 15, 18, 45, 0),
            sunTimes: null,
            dayStart,
            nightStart,
            dayBrightness: 80,
            nightBrightness: 30,
            transitionDuration: TimeSpan.FromMinutes(10));

        Assert.Equal(SchedulePhase.Twilight, twilight.Phase);
        Assert.Equal(30, twilight.Brightness);
        Assert.Equal(SchedulePhase.Night, night.Phase);
        Assert.Equal(30, night.Brightness);
    }

    [Fact]
    public void ZeroThemeLeadKeepsScheduleUntouched()
    {
        var sunTimes = new SunTimes(
            new DateTime(2026, 4, 15, 6, 0, 0),
            new DateTime(2026, 4, 15, 20, 0, 0));

        var shifted = ScheduleCalculator.ShiftForThemeLead(sunTimes, TimeSpan.Zero);

        Assert.Same(sunTimes, shifted);
    }
}
