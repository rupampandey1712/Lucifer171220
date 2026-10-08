using StaySphere.Domain.Common;

namespace StaySphere.Domain.ValueObjects;

public readonly record struct Coordinates
{
    private const double EarthRadiusKm = 6371.0;

    private Coordinates(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }

    public static Result<Coordinates> Create(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || double.IsNaN(latitude))
            return Error.Validation("coordinates.latitude", "Latitude must be between -90 and 90.");
        if (longitude is < -180 or > 180 || double.IsNaN(longitude))
            return Error.Validation("coordinates.longitude", "Longitude must be between -180 and 180.");
        return new Coordinates(latitude, longitude);
    }

    /// <summary>Great-circle distance using the haversine formula.</summary>
    public double DistanceKmTo(Coordinates other)
    {
        var dLat = ToRad(other.Latitude - Latitude);
        var dLon = ToRad(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(ToRad(Latitude)) * Math.Cos(ToRad(other.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
