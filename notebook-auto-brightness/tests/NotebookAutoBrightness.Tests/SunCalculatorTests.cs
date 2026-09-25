using System;
using NotebookAutoBrightness;
using Xunit;

namespace NotebookAutoBrightness.Tests;

// Reference values come from api.sunrise-sunset.org, which implements the same NOAA equations the
// local calculator does. Comparing instants and day length keeps the tests offline and independent
// of the time zone of the machine that runs them.
public sealed class SunCalculatorTests
{
    [Fact]
    public void BerlinInstantsMatchTheReferenceService()
    {
        var computed = NeedTimes(SunCalculator.Compute(52.4746, 13.4164, LocalNoon(2026, 9, 22)));

        // 2026-09-22: sunrise 04:50:44Z, sunset 17:07:22Z.
        AssertInstants(
            computed,
            new DateTimeOffset(2026, 9, 22, 4, 50, 44, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 22, 17, 7, 22, TimeSpan.Zero));
    }

    [Fact]
    public void DayLengthMatchesTheReferenceServiceAcrossTheGlobe()
    {
        var acapulco = NeedTimes(SunCalculator.Compute(16.8531, -99.8237, LocalNoon(2026, 9, 23)));
        var sydney = NeedTimes(SunCalculator.Compute(-33.8688, 151.2093, LocalNoon(2026, 9, 23)));

        AssertDayLength(acapulco, 12.141);

        // The free reference service reports the pair inside UTC day boundaries, which for a UTC+10
        // site is the next local day. Near the equinox the day length there grows by about two and a
        // half minutes per day, so this gap is the convention difference rather than the maths - and
        // it is irrelevant next to a 40 minute transition window.
        AssertDayLength(sydney, 12.179, toleranceMinutes: 6);
    }

    [Fact]
    public void PolarDayIsDetectedAboveTheArcticCircle()
    {
        var now = new DateTime(2026, 6, 21, 12, 0, 0);
        var computation = SunCalculator.Compute(78.22, 15.65, now);
        Assert.Equal(SunPolarState.AlwaysDay, computation.Polar);

        // The sentinel window has to keep the schedule on day brightness all day long.
        var times = SunCalculator.TryGetSunTimes(78.22, 15.65, now) ?? throw new InvalidOperationException();
        Assert.True(times.Sunrise <= now && now < times.Sunset);
    }

    [Fact]
    public void PolarNightIsDetectedAboveTheArcticCircle()
    {
        var now = new DateTime(2026, 12, 21, 12, 0, 0);
        var computation = SunCalculator.Compute(78.22, 15.65, now);
        Assert.Equal(SunPolarState.AlwaysNight, computation.Polar);

        var times = SunCalculator.TryGetSunTimes(78.22, 15.65, now) ?? throw new InvalidOperationException();
        Assert.True(times.Sunset <= now);
    }

    private static DateTime LocalNoon(int year, int month, int day) => new(year, month, day, 12, 0, 0);

    private static SunTimes NeedTimes(SunComputation computation) =>
        computation.Times ?? throw new InvalidOperationException("expected a normal day");

    private static void AssertInstants(SunTimes times, DateTimeOffset expectedSunriseUtc, DateTimeOffset expectedSunsetUtc)
    {
        var sunriseUtc = ToUtc(times.Sunrise);
        var sunsetUtc = ToUtc(times.Sunset);

        Assert.True(
            Math.Abs((sunriseUtc - expectedSunriseUtc).TotalSeconds) <= 150,
            $"sunrise is off by {(sunriseUtc - expectedSunriseUtc).TotalSeconds:F0}s");
        Assert.True(
            Math.Abs((sunsetUtc - expectedSunsetUtc).TotalSeconds) <= 150,
            $"sunset is off by {(sunsetUtc - expectedSunsetUtc).TotalSeconds:F0}s");
    }

    private static void AssertDayLength(SunTimes times, double expectedHours, double toleranceMinutes = 3)
    {
        var hours = (times.Sunset - times.Sunrise).TotalHours;
        Assert.InRange(hours, expectedHours - toleranceMinutes / 60.0, expectedHours + toleranceMinutes / 60.0);
    }

    private static DateTimeOffset ToUtc(DateTime localWallClock) =>
        new(localWallClock, TimeZoneInfo.Local.GetUtcOffset(localWallClock));
}
