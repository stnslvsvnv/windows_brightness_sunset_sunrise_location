using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NotebookAutoBrightness;

public static class SunService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    public static async Task<SunTimes?> TryGetSunTimesAsync(double latitude, double longitude, DateTime date)
    {
        try
        {
            var dateString = date.ToString("yyyy-MM-dd");
            var url = $"https://api.sunrise-sunset.org/json?lat={latitude}&lng={longitude}&date={dateString}&formatted=0";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<SunApiResponse>(payload);
            if (data?.Status != "OK")
            {
                return null;
            }

            if (!DateTime.TryParse(data.Results?.Sunrise, out var sunriseUtc) ||
                !DateTime.TryParse(data.Results?.Sunset, out var sunsetUtc))
            {
                return null;
            }

            return new SunTimes(sunriseUtc.ToLocalTime(), sunsetUtc.ToLocalTime());
        }
        catch
        {
            return null;
        }
    }

    private sealed class SunApiResponse
    {
        [JsonPropertyName("results")]
        public SunResults? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class SunResults
    {
        [JsonPropertyName("sunrise")]
        public string? Sunrise { get; set; }

        [JsonPropertyName("sunset")]
        public string? Sunset { get; set; }
    }
}

public sealed record SunTimes(DateTime Sunrise, DateTime Sunset);
