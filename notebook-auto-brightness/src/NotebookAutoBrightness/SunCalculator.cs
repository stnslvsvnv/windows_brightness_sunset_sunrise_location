using System;

namespace NotebookAutoBrightness;

public sealed record SunTimes(DateTime Sunrise, DateTime Sunset);

internal enum SunPolarState
{
    Normal,
    AlwaysDay,
    AlwaysNight
}

internal readonly record struct SunComputation(SunTimes? Times, SunPolarState Polar);

// Sunrise and sunset computed from coordinates instead of a web request: the same NOAA closed-form
// equations the public services use. Within a couple of minutes of truth, which is far below the
// shortest transition window, and it makes the schedule independent of a request that can be slow,
// rate limited or simply absent right after a wake-up.
internal static class SunCalculator
{
    // 90 degrees plus atmospheric refraction and the solar radius.
    private const double ZenithDegrees = 90.833;

    private const double DegreesToRadiansFactor = Math.PI / 180.0;

    public static SunComputation Compute(double latitude, double longitude, DateTime localDate)
    {
        // Everything is computed as absolute instants: the UTC clock time of solar noon is
        // 720 - 4*longitude - equationOfTime minutes after the UTC midnight of the day that
        // contains local noon, and sunrise/sunset are that instant plus or minus half the day.
        var localMidnightUtc = new DateTimeOffset(
            localDate.Date,
            TimeZoneInfo.Local.GetUtcOffset(localDate.Date)).ToUniversalTime();
        var localNoonUtc = localMidnightUtc.AddHours(12);
        var latitudeRadians = latitude * DegreesToRadiansFactor;

        var noonTerms = SolarTerms(localNoonUtc);
        var noonHourAngleCosine = HourAngleCosine(latitudeRadians, noonTerms.Declination);
        if (noonHourAngleCosine >= 1)
        {
            return new SunComputation(null, SunPolarState.AlwaysNight);
        }

        if (noonHourAngleCosine <= -1)
        {
            return new SunComputation(null, SunPolarState.AlwaysDay);
        }

        var utcDayStart = new DateTimeOffset(localNoonUtc.UtcDateTime.Date, TimeSpan.Zero);
        var solarNoon = utcDayStart.AddMinutes(720 - 4 * longitude - noonTerms.EquationOfTime);
        var noonHalfDayMinutes = 4 * Math.Acos(noonHourAngleCosine) / DegreesToRadiansFactor;

        return new SunComputation(
            new SunTimes(
                ResolveEvent(solarNoon, latitudeRadians, noonHalfDayMinutes, -1).ToLocalTime().DateTime,
                ResolveEvent(solarNoon, latitudeRadians, noonHalfDayMinutes, 1).ToLocalTime().DateTime),
            SunPolarState.Normal);
    }

    // Near the equinoxes and the solstices the declination moves by up to half a degree a day, so the
    // half day length is evaluated once more with the declination of the event itself instead of the
    // declination at noon. Without that the error reaches a few minutes in the southern hemisphere.
    private static DateTimeOffset ResolveEvent(
        DateTimeOffset solarNoon,
        double latitudeRadians,
        double estimateHalfDayMinutes,
        int side)
    {
        var estimate = solarNoon.AddMinutes(side * estimateHalfDayMinutes);
        var terms = SolarTerms(estimate);
        var hourAngleCosine = Math.Clamp(HourAngleCosine(latitudeRadians, terms.Declination), -1.0, 1.0);
        var halfDayMinutes = 4 * Math.Acos(hourAngleCosine) / DegreesToRadiansFactor;

        return solarNoon.AddMinutes(side * halfDayMinutes);
    }

    private static double HourAngleCosine(double latitudeRadians, double declination) =>
        Math.Cos(ZenithDegrees * DegreesToRadiansFactor) / (Math.Cos(latitudeRadians) * Math.Cos(declination))
        - Math.Tan(latitudeRadians) * Math.Tan(declination);

    private static (double EquationOfTime, double Declination) SolarTerms(DateTimeOffset instantUtc)
    {
        var dayFraction = (instantUtc.Hour + instantUtc.Minute / 60.0) / 24.0;
        var fractionalYear = 2 * Math.PI / 365.25 * (instantUtc.DayOfYear - 1 + dayFraction);

        var equationOfTime = 229.18 * (
            0.000075
            + 0.001868 * Math.Cos(fractionalYear)
            - 0.032077 * Math.Sin(fractionalYear)
            - 0.014615 * Math.Cos(2 * fractionalYear)
            - 0.040849 * Math.Sin(2 * fractionalYear));

        var declination =
            0.006918
            - 0.399912 * Math.Cos(fractionalYear)
            + 0.070257 * Math.Sin(fractionalYear)
            - 0.006758 * Math.Cos(2 * fractionalYear)
            + 0.000907 * Math.Sin(2 * fractionalYear)
            - 0.002697 * Math.Cos(3 * fractionalYear)
            + 0.00148 * Math.Sin(3 * fractionalYear);

        return (equationOfTime, declination);
    }

    // Sun times usable by ScheduleCalculator. Polar day and night have no sunrise or sunset, so they
    // are expressed as a window that always contains (or never contains) the moment of evaluation:
    // without that, machines above the Arctic Circle would jump to the manual schedule twice a year.
    public static SunTimes? TryGetSunTimes(double latitude, double longitude, DateTime now)
    {
        var computation = Compute(latitude, longitude, now.Date);
        return computation.Polar switch
        {
            SunPolarState.AlwaysDay => new SunTimes(now.AddDays(-1), now.AddDays(1)),
            SunPolarState.AlwaysNight => new SunTimes(now.Date, now.Date),
            _ => computation.Times
        };
    }
}
