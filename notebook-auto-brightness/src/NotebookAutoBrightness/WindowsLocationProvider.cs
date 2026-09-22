using System;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace NotebookAutoBrightness;

// Location from the Windows location stack: Wi-Fi positioning (and GNSS when the machine has it).
// Needs "Let desktop apps access your location" to be on; any failure simply means the caller
// falls through to the next source, so this never blocks the schedule.
internal static class WindowsLocationProvider
{
    private const string SourceName = "Windows location";
    private static readonly TimeSpan FixTimeout = TimeSpan.FromSeconds(8);

    public static async Task<LocationResult?> TryGetLocationAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access == GeolocationAccessStatus.Denied)
            {
                return null;
            }

            // Unspecified is common for unpackaged desktop apps: the system switch decides anyway,
            // so a fix is attempted and any refusal is caught below.
            var geolocator = new Geolocator { DesiredAccuracyInMeters = 5000 };
            var position = await geolocator.GetGeopositionAsync(TimeSpan.FromMinutes(5), FixTimeout);
            var point = position.Coordinate.Point.Position;

            return new LocationResult(point.Latitude, point.Longitude, string.Empty, string.Empty, SourceName);
        }
        catch
        {
            // Denied, switched off, no fix within the timeout or unavailable for this process.
            return null;
        }
    }
}
