using System;
using NotebookAutoBrightness;
using Xunit;

namespace NotebookAutoBrightness.Tests;

public sealed class AutomationControllerTests
{
    [Fact]
    public void SettingsSavedWithoutLocationKeepLastKnownLocation()
    {
        var existing = new AppSettings
        {
            LastLatitude = 52.4746,
            LastLongitude = 13.4164,
            LastCity = "Berlin",
            LastCountry = "Germany"
        };
        var incoming = new AppSettings { DayBrightness = 90 };

        var merged = AutomationController.PreserveLastKnownLocation(incoming, existing);

        Assert.Equal(52.4746, merged.LastLatitude);
        Assert.Equal(13.4164, merged.LastLongitude);
        Assert.Equal("Berlin", merged.LastCity);
        Assert.Equal("Germany", merged.LastCountry);
        Assert.Equal(90, merged.DayBrightness);
    }

    [Fact]
    public void ExplicitLocationFromCallerWins()
    {
        var existing = new AppSettings
        {
            LastLatitude = 52.4746,
            LastLongitude = 13.4164,
            LastCity = "Berlin",
            LastCountry = "Germany"
        };
        var incoming = new AppSettings
        {
            LastLatitude = 52.39,
            LastLongitude = 13.06,
            LastCity = "Potsdam"
        };

        var merged = AutomationController.PreserveLastKnownLocation(incoming, existing);

        Assert.Equal(52.39, merged.LastLatitude);
        Assert.Equal(13.06, merged.LastLongitude);
        Assert.Equal("Potsdam", merged.LastCity);
        Assert.Equal("Germany", merged.LastCountry);
    }
}
