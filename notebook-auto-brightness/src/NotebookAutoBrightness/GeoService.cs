using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NotebookAutoBrightness;

public static class GeoService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    static GeoService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("NotebookSunriseSunsetAutoBrightness/1.0");
    }

    public static async Task<LocationResult?> TryGetIpLocationAsync()
    {
        try
        {
            var location = await TryGetIpWhoIsLocationAsync();
            if (location != null)
            {
                return location;
            }

            location = await TryGetIpApiCoLocationAsync();
            if (location != null)
            {
                return location;
            }

            return await TryGetIpApiLocationAsync();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LocationResult?> TryGetIpWhoIsLocationAsync()
    {
        try
        {
            using var response = await Http.GetAsync("https://ipwho.is/");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<IpWhoIsResponse>(payload);
            if (data == null || !data.Success)
            {
                return null;
            }

            return new LocationResult(
                data.Latitude,
                data.Longitude,
                data.City ?? string.Empty,
                data.Country ?? string.Empty,
                "IP Geolocation");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LocationResult?> TryGetIpApiCoLocationAsync()
    {
        try
        {
            using var response = await Http.GetAsync("https://ipapi.co/json/");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<IpApiCoResponse>(payload);
            if (data == null || data.Error || data.Latitude == null || data.Longitude == null)
            {
                return null;
            }

            return new LocationResult(
                data.Latitude.Value,
                data.Longitude.Value,
                data.City ?? string.Empty,
                data.CountryName ?? data.Country ?? string.Empty,
                "IP Geolocation");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LocationResult?> TryGetIpApiLocationAsync()
    {
        try
        {
            using var response = await Http.GetAsync("http://ip-api.com/json/");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<IpApiResponse>(payload);
            if (data == null || !string.Equals(data.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new LocationResult(
                data.Lat,
                data.Lon,
                data.City ?? string.Empty,
                data.Country ?? string.Empty,
                "IP Geolocation");
        }
        catch
        {
            return null;
        }
    }

    public static async Task<LocationResult?> TryGeocodeCityAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        try
        {
            var url = "https://nominatim.openstreetmap.org/search?format=json&limit=1&q=" +
                      Uri.EscapeDataString(city);
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<NominatimItem[]>(payload);
            if (items == null || items.Length == 0)
            {
                return null;
            }

            if (!double.TryParse(items[0].Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(items[0].Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                return null;
            }

            return new LocationResult(lat, lon, city, string.Empty, "City Geocoding");
        }
        catch
        {
            return null;
        }
    }

    private sealed class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    private sealed class IpWhoIsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    private sealed class IpApiCoResponse
    {
        [JsonPropertyName("error")]
        public bool Error { get; set; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("country_name")]
        public string? CountryName { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    private sealed class NominatimItem
    {
        [JsonPropertyName("lat")]
        public string Lat { get; set; } = "0";

        [JsonPropertyName("lon")]
        public string Lon { get; set; } = "0";
    }
}

public sealed record LocationResult(double Latitude, double Longitude, string City, string Country, string Source);
