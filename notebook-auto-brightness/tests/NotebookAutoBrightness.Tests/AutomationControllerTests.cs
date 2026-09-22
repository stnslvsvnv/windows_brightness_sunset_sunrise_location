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

    [Fact]
    public void FreshAutomaticLocationIsReusedWithinRefreshInterval()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var location = new LocationResult(52.4746, 13.4164, "Berlin", "Germany", "IP Geolocation");

        Assert.True(AutomationController.IsLocationReusable(
            location, now.AddMinutes(-5), now, "Europe/Berlin", "Europe/Berlin"));
        Assert.False(AutomationController.IsLocationReusable(
            location, now.AddMinutes(-45), now, "Europe/Berlin", "Europe/Berlin"));
    }

    [Fact]
    public void LocationWithoutStoredTimeZoneIsStillReusable()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var location = new LocationResult(52.4746, 13.4164, "Berlin", "Germany", "Windows location");

        Assert.True(AutomationController.IsLocationReusable(location, now.AddMinutes(-5), now, null, "Europe/Berlin"));
    }

    [Fact]
    public void LastKnownLocationSurvivesMuchLongerThanAnIpLookup()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var location = new LocationResult(52.4746, 13.4164, "Berlin", "Germany", "Last known");

        Assert.True(AutomationController.IsLocationReusable(
            location, now.AddHours(-3), now, "Europe/Berlin", "Europe/Berlin"));
        Assert.False(AutomationController.IsLocationReusable(
            location, now.AddHours(-9), now, "Europe/Berlin", "Europe/Berlin"));
    }

    [Fact]
    public void LocationFromAnotherTimeZoneIsNotReused()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var location = new LocationResult(16.8531, -99.8237, "Acapulco", "Mexico", "IP Geolocation");

        Assert.False(AutomationController.IsLocationReusable(
            location, now.AddMinutes(-1), now, "America/Mexico_City", "Europe/Berlin"));
    }

    [Fact]
    public void MissingLocationIsNeverReused()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(AutomationController.IsLocationReusable(null, now, now, null, "Europe/Berlin"));
    }
}
