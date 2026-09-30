namespace RoutingEngine;

public static class GeoUtils
{
    // Same Earth radius OSMnx used to measure the road lengths,
    // so straight-line and road distances are directly comparable.
    private const double EarthRadiusMeters = 6371009;

    /// <summary>
    /// Straight-line ("as the crow flies") distance between two nodes, in metres,
    /// using the haversine formula. O(1).
    /// </summary>
    public static double HaversineMeters(Node a, Node b)
    {
        double lat1 = ToRadians(a.Lat);
        double lat2 = ToRadians(b.Lat);
        double dLat = lat2 - lat1;
        double dLon = ToRadians(b.Lon - a.Lon);

        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1) * Math.Cos(lat2) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Min(1.0, h)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
