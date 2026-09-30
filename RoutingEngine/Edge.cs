namespace RoutingEngine;

/// <summary>What a route is optimised for.</summary>
public enum Metric
{
    Distance,   // metres
    Time        // seconds
}

/// <summary>
/// One DIRECTED road segment between two junctions.
/// A two-way road is stored as two edges (A->B and B->A);
/// a one-way road is stored as a single edge.
/// </summary>
public class Edge
{
    private double _congestionFactor = 1.0;

    public int From { get; }
    public int To { get; }
    public double LengthMeters { get; }
    public string RoadType { get; }      // trunk, primary, residential ...
    public string Name { get; }          // street name (may be empty)
    public double SpeedKmh { get; }      // speed limit used for travel time
    public bool SpeedFromOsm { get; }    // true = tagged in OSM, false = our default

    /// <summary>
    /// 1.0 = free-flowing traffic, 2.0 = twice as slow.
    /// Must be at least 1.0: congestion can only slow traffic down.
    /// (This also keeps the A* heuristic valid - see PathFinder.)
    /// </summary>
    public double CongestionFactor
    {
        get => _congestionFactor;
        set
        {
            if (value < 1.0)
                throw new ArgumentException("Congestion factor must be at least 1.0.");
            _congestionFactor = value;
        }
    }

    public Edge(int from, int to, double lengthMeters, string roadType,
                string name, double speedKmh, bool speedFromOsm)
    {
        // Dijkstra and A* are only correct with non-negative weights,
        // so bad data is rejected here, at the point it enters the system.
        if (lengthMeters < 0)
            throw new ArgumentException($"Edge {from}->{to} has a negative length.");
        if (speedKmh <= 0)
            throw new ArgumentException($"Edge {from}->{to} has a speed of zero or less.");

        From = from;
        To = to;
        LengthMeters = lengthMeters;
        RoadType = roadType;
        Name = name;
        SpeedKmh = speedKmh;
        SpeedFromOsm = speedFromOsm;
    }

    /// <summary>Time with no traffic: distance / speed (km/h -> m/s is / 3.6).</summary>
    public double FreeFlowSeconds => LengthMeters / (SpeedKmh / 3.6);

    /// <summary>Time with current traffic.</summary>
    public double TravelTimeSeconds => FreeFlowSeconds * CongestionFactor;

    /// <summary>The weight the routing algorithms use for this edge.</summary>
    public double Weight(Metric metric) =>
        metric == Metric.Distance ? LengthMeters : TravelTimeSeconds;
}
