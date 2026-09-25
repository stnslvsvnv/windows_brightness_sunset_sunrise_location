using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotebookAutoBrightness;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool UseGeolocation { get; set; } = true;
    public bool UseSunSchedule { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool AutoThemeSwitching { get; set; } = false;

    public int DayBrightness { get; set; } = 80;
    public int NightBrightness { get; set; } = 33;
    public int TransitionMinutes { get; set; } = 5;
    public int ThemeSwitchLeadMinutes { get; set; } = 0;
    public int CheckIntervalSeconds { get; set; } = 15;

    public string City { get; set; } = string.Empty;

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan DayStartTime { get; set; } = new(7, 0, 0);

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan NightStartTime { get; set; } = new(19, 0, 0);

    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public string? LastCity { get; set; }
    public string? LastCountry { get; set; }

    /// <summary>Time zone the last known location was resolved in; a change means the machine moved.</summary>
    public string? LastTimeZoneId { get; set; }

    /// <summary>When the last known location was resolved, so it can be aged in the status label.</summary>
    public DateTime? LastLocationResolvedAtUtc { get; set; }
}

public sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    private const string Format = @"hh\:mm";

    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return TimeSpan.TryParseExact(text, Format, null, out var value) ? value : TimeSpan.Zero;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}
