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

    public int DayBrightness { get; set; } = 80;
    public int NightBrightness { get; set; } = 33;

    public string City { get; set; } = string.Empty;

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan DayStartTime { get; set; } = new(7, 0, 0);

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan NightStartTime { get; set; } = new(19, 0, 0);

    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public string? LastCity { get; set; }
    public string? LastCountry { get; set; }
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
