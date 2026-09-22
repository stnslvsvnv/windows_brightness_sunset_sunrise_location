using System;
using NotebookAutoBrightness;
using Xunit;

namespace NotebookAutoBrightness.Tests;

public sealed class SunServiceTests
{
    [Fact]
    public void InvalidPayloadIsRejected()
    {
        Assert.Null(SunService.TryParseTimes(null, null));
        Assert.Null(SunService.TryParseTimes("Invalid Date", "Invalid Date"));
        Assert.Null(SunService.TryParseTimes("2026-09-22T16:19:13+00:00", "Invalid Date"));
    }

    [Fact]
    public void UtcPairSpanningTwoDatesKeepsDayLength()
    {
        // Sunset falls on the next UTC date for locations west of UTC (Honolulu, 2026-09-22).
        var times = SunService.TryParseTimes("2026-09-22T16:19:13+00:00", "2026-09-23T04:28:44+00:00");

        Assert.NotNull(times);
        var parsed = times!;
        Assert.True(parsed.Sunset > parsed.Sunrise);
        Assert.InRange((parsed.Sunset - parsed.Sunrise).TotalHours, 12.1, 12.2);
    }
}
