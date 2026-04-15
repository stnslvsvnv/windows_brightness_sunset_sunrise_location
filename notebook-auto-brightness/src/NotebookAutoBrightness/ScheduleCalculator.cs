using System;

namespace NotebookAutoBrightness;

internal enum SchedulePhase
{
    Night,
    Dawn,
    Day,
    Twilight
}

internal sealed record ScheduleEvaluation(int Brightness, SchedulePhase Phase, DateTime NextChange, bool IsDaytime);

internal static class ScheduleCalculator
{
    public static ScheduleEvaluation EvaluateBrightness(
        DateTime now,
        SunTimes? sunTimes,
        TimeSpan dayStartTime,
        TimeSpan nightStartTime,
        int dayBrightness,
        int nightBrightness,
        TimeSpan transitionDuration)
    {
        var normalizedDayBrightness = Clamp(dayBrightness);
        var normalizedNightBrightness = Clamp(nightBrightness);
        var (isDay, nextChange) = sunTimes != null
            ? GetPeriodFromSunTimes(now, sunTimes)
            : GetPeriodFromManualTimes(now, dayStartTime, nightStartTime);

        var brightness = isDay ? normalizedDayBrightness : normalizedNightBrightness;
        var phase = isDay ? SchedulePhase.Day : SchedulePhase.Night;

        if (transitionDuration > TimeSpan.Zero)
        {
            if (sunTimes != null &&
                TryGetSunTransitionBrightness(
                    now,
                    sunTimes,
                    transitionDuration,
                    normalizedDayBrightness,
                    normalizedNightBrightness,
                    out var sunTransitionBrightness,
                    out var sunTransitionPhase,
                    out var sunTransitionEnd))
            {
                return new ScheduleEvaluation(sunTransitionBrightness, sunTransitionPhase, sunTransitionEnd, isDay);
            }

            if (sunTimes == null &&
                TryGetManualTransitionBrightness(
                    now,
                    dayStartTime,
                    nightStartTime,
                    transitionDuration,
                    normalizedDayBrightness,
                    normalizedNightBrightness,
                    out var manualTransitionBrightness,
                    out var manualTransitionPhase,
                    out var manualTransitionEnd))
            {
                return new ScheduleEvaluation(manualTransitionBrightness, manualTransitionPhase, manualTransitionEnd, isDay);
            }
        }

        return new ScheduleEvaluation(brightness, phase, nextChange, isDay);
    }

    public static bool ShouldUseLightTheme(
        DateTime now,
        SunTimes? sunTimes,
        TimeSpan dayStartTime,
        TimeSpan nightStartTime,
        TimeSpan switchLeadTime)
    {
        if (sunTimes != null)
        {
            var shiftedSunTimes = switchLeadTime > TimeSpan.Zero
                ? new SunTimes(sunTimes.Sunrise - switchLeadTime, sunTimes.Sunset - switchLeadTime)
                : sunTimes;
            return GetPeriodFromSunTimes(now, shiftedSunTimes).IsDay;
        }

        var shiftedDayStart = dayStartTime - switchLeadTime;
        var shiftedNightStart = nightStartTime - switchLeadTime;
        return GetPeriodFromManualTimes(now, shiftedDayStart, shiftedNightStart).IsDay;
    }

    public static string GetPhaseLabel(SchedulePhase phase) =>
        phase switch
        {
            SchedulePhase.Dawn => "Dawn",
            SchedulePhase.Day => "Day",
            SchedulePhase.Twilight => "Twilight",
            _ => "Night"
        };

    private static (bool IsDay, DateTime NextChange) GetPeriodFromSunTimes(DateTime now, SunTimes sunTimes)
    {
        if (now >= sunTimes.Sunrise && now < sunTimes.Sunset)
        {
            return (true, sunTimes.Sunset);
        }

        var next = sunTimes.Sunrise;
        if (now >= sunTimes.Sunset)
        {
            next = sunTimes.Sunrise.AddDays(1);
        }

        return (false, next);
    }

    private static (bool IsDay, DateTime NextChange) GetPeriodFromManualTimes(DateTime now, TimeSpan dayStart, TimeSpan nightStart)
    {
        var lastDayStart = GetMostRecentOccurrence(now, dayStart);
        var lastNightStart = GetMostRecentOccurrence(now, nightStart);

        if (lastDayStart > lastNightStart)
        {
            return (true, lastNightStart.AddDays(lastNightStart <= now ? 1 : 0));
        }

        return (false, lastDayStart.AddDays(lastDayStart <= now ? 1 : 0));
    }

    private static bool TryGetSunTransitionBrightness(
        DateTime now,
        SunTimes sunTimes,
        TimeSpan transitionDuration,
        int dayBrightness,
        int nightBrightness,
        out int brightness,
        out SchedulePhase phase,
        out DateTime transitionEnd)
    {
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var sunrise = sunTimes.Sunrise.AddDays(dayOffset);
            if (TryGetTransitionBrightness(
                    now,
                    sunrise - transitionDuration,
                    transitionDuration,
                    nightBrightness,
                    dayBrightness,
                    out brightness))
            {
                phase = SchedulePhase.Dawn;
                transitionEnd = sunrise;
                return true;
            }

            var sunset = sunTimes.Sunset.AddDays(dayOffset);
            if (TryGetTransitionBrightness(
                    now,
                    sunset - transitionDuration,
                    transitionDuration,
                    dayBrightness,
                    nightBrightness,
                    out brightness))
            {
                phase = SchedulePhase.Twilight;
                transitionEnd = sunset;
                return true;
            }
        }

        brightness = 0;
        phase = SchedulePhase.Night;
        transitionEnd = DateTime.MinValue;
        return false;
    }

    private static bool TryGetManualTransitionBrightness(
        DateTime now,
        TimeSpan dayStartTime,
        TimeSpan nightStartTime,
        TimeSpan transitionDuration,
        int dayBrightness,
        int nightBrightness,
        out int brightness,
        out SchedulePhase phase,
        out DateTime transitionEnd)
    {
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var dayStart = now.Date.AddDays(dayOffset).Add(dayStartTime);
            if (TryGetTransitionBrightness(
                    now,
                    dayStart - transitionDuration,
                    transitionDuration,
                    nightBrightness,
                    dayBrightness,
                    out brightness))
            {
                phase = SchedulePhase.Dawn;
                transitionEnd = dayStart;
                return true;
            }

            var nightStart = now.Date.AddDays(dayOffset).Add(nightStartTime);
            if (TryGetTransitionBrightness(
                    now,
                    nightStart - transitionDuration,
                    transitionDuration,
                    dayBrightness,
                    nightBrightness,
                    out brightness))
            {
                phase = SchedulePhase.Twilight;
                transitionEnd = nightStart;
                return true;
            }
        }

        brightness = 0;
        phase = SchedulePhase.Night;
        transitionEnd = DateTime.MinValue;
        return false;
    }

    private static bool TryGetTransitionBrightness(
        DateTime now,
        DateTime transitionStart,
        TimeSpan transitionDuration,
        int fromBrightness,
        int toBrightness,
        out int brightness)
    {
        if (transitionDuration <= TimeSpan.Zero)
        {
            brightness = 0;
            return false;
        }

        var transitionEnd = transitionStart.Add(transitionDuration);
        if (now < transitionStart || now >= transitionEnd)
        {
            brightness = 0;
            return false;
        }

        var progress = (now - transitionStart).TotalMilliseconds / transitionDuration.TotalMilliseconds;
        brightness = InterpolateBrightness(fromBrightness, toBrightness, progress);
        return true;
    }

    private static int InterpolateBrightness(int fromBrightness, int toBrightness, double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0d, 1d);
        var easedProgress = (1 - Math.Cos(clampedProgress * Math.PI)) / 2;
        var value = fromBrightness + ((toBrightness - fromBrightness) * easedProgress);
        return Clamp((int)Math.Round(value));
    }

    private static DateTime GetMostRecentOccurrence(DateTime now, TimeSpan timeOfDay)
    {
        var occurrence = now.Date.Add(timeOfDay);
        if (occurrence > now)
        {
            occurrence = occurrence.AddDays(-1);
        }

        while (occurrence.AddDays(1) <= now)
        {
            occurrence = occurrence.AddDays(1);
        }

        return occurrence;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
