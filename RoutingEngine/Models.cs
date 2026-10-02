namespace RoutingEngine;

// What a route should be best at: shortest distance or quickest time.
public enum Metric { Distance, Time }

// A junction: an id number and its GPS position.
public record Node(int Id, double Lat, double Lon);

// One road going in ONE direction. A two-way road is saved as two of these.
public class Edge
{
    private double _congestion = 1.0;

    public int From { get; }
    public int To { get; }
    public double LengthMeters { get; }
    public string RoadType { get; }
    public string Name { get; }
    public double SpeedKmh { get; }

    // Traffic level: 1.0 = clear road, 2.0 = twice as slow. It can never be below 1.
    public double Congestion
    {
        get => _congestion;
        set => _congestion = value >= 1 ? value : throw new ArgumentException("Congestion must be at least 1.");
    }

    // Creates a road. Rejects negative lengths and zero speeds, because the route search can't handle them.
    public Edge(int from, int to, double lengthMeters, string roadType, string name, double speedKmh)
    {
        if (lengthMeters < 0 || speedKmh <= 0)
            throw new ArgumentException($"Road {from}->{to} has an invalid length or speed.");

        From = from;
        To = to;
        LengthMeters = lengthMeters;
        RoadType = roadType;
        Name = name;
        SpeedKmh = speedKmh;
    }

    // Time to drive this road in seconds: distance ÷ speed × traffic (km/h ÷ 3.6 = metres per second).
    public double Seconds => LengthMeters / (SpeedKmh / 3.6) * Congestion;

    // The "cost" of using this road: its length or its time, depending on what the route wants.
    public double Weight(Metric m) => m == Metric.Distance ? LengthMeters : Seconds;
}

// Rules for a route: closed junctions and road types to avoid.
public class RouteConstraints
{
    public HashSet<int> BlockedNodes { get; } = new();
    public HashSet<string> AvoidRoadTypes { get; } = new();

    // True if the route is allowed to use this road.
    public bool Allows(Edge e) =>
        !BlockedNodes.Contains(e.From) && !BlockedNodes.Contains(e.To) && !AvoidRoadTypes.Contains(e.RoadType);
}

// The answer to a route search.
public class PathResult
{
    public bool Found { get; init; }
    public string Message { get; init; } = "";
    public List<int> Path { get; init; } = new();
    public double Cost { get; init; } = double.PositiveInfinity;
    public double Meters { get; init; }
    public double Seconds { get; init; }
    public int Explored { get; init; }

    // Makes a "no route found" answer with a message saying why.
    public static PathResult Fail(string message, int explored = 0) =>
        new() { Message = message, Explored = explored };
}
