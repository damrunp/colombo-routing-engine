namespace RoutingEngine;

/// <summary>
/// A location in the road network: a junction, roundabout point or dead end.
/// Ids run 0, 1, 2 ... V-1 so they can be used directly as array indexes.
/// </summary>
public class Node
{
    public int Id { get; }
    public double Lat { get; }
    public double Lon { get; }

    public Node(int id, double lat, double lon)
    {
        Id = id;
        Lat = lat;
        Lon = lon;
    }

    public override string ToString() => $"Node {Id} ({Lat:F5}, {Lon:F5})";
}
