using System.Globalization;

namespace RoutingEngine;

// The road map, saved as an ADJACENCY LIST: for each junction, a list of the roads leaving it.
public class Graph
{
    private readonly List<Node> _nodes = new();
    private readonly List<List<Edge>> _adj = new();

    // Speed for roads with no speed limit in the data (the most common limit for that road type). Others: 30.
    private static readonly Dictionary<string, double> DefaultSpeed = new()
    {
        ["trunk"] = 60, ["primary"] = 60, ["primary_link"] = 60,
        ["secondary"] = 50, ["secondary_link"] = 50,
        ["tertiary"] = 40, ["tertiary_link"] = 40
    };

    // How much slower each road type gets at rush hour (our own assumption). Others: 1.0.
    private static readonly Dictionary<string, double> RushFactor = new()
    {
        ["trunk"] = 2.0, ["primary"] = 1.8, ["primary_link"] = 1.8,
        ["secondary"] = 1.5, ["secondary_link"] = 1.5,
        ["tertiary"] = 1.3, ["tertiary_link"] = 1.3,
        ["residential"] = 1.1, ["unclassified"] = 1.1
    };

    public int NodeCount => _nodes.Count;
    public int EdgeCount { get; private set; }
    public double MaxSpeedKmh { get; private set; }

    // Adds a junction and returns its id.
    public int AddNode(double lat, double lon)
    {
        _nodes.Add(new Node(_nodes.Count, lat, lon));
        _adj.Add(new List<Edge>());
        return _nodes.Count - 1;
    }

    // Adds a one-way road to the list of the junction it starts from.
    public void AddEdge(Edge e)
    {
        if (!IsValid(e.From) || !IsValid(e.To))
            throw new ArgumentException($"Road {e.From}->{e.To} uses a junction that does not exist.");
        _adj[e.From].Add(e);
        EdgeCount++;
        MaxSpeedKmh = Math.Max(MaxSpeedKmh, e.SpeedKmh);
    }

    // True if this junction id exists.
    public bool IsValid(int id) => id >= 0 && id < _nodes.Count;

    // Gets a junction by its id.
    public Node GetNode(int id) => _nodes[id];

    // Gets all roads leaving a junction.
    public List<Edge> EdgesFrom(int id) => _adj[id];

    // Finds the road from one junction to another, or null if there isn't one.
    public Edge? FindEdge(int from, int to) => _adj[from].Find(e => e.To == to);

    // Goes through every road in the map.
    public IEnumerable<Edge> AllEdges() => _adj.SelectMany(list => list);

    // Switches rush-hour traffic on or off for every road.
    public void SetRushHour(bool rush)
    {
        foreach (Edge e in AllEdges())
            e.Congestion = rush ? RushFactor.GetValueOrDefault(e.RoadType, 1.0) : 1.0;
    }

    // Reads nodes.csv and edges.csv and builds the map.
    public static Graph Load(string nodesPath, string edgesPath)
    {
        var g = new Graph();

        foreach (string line in File.ReadLines(nodesPath).Skip(1))      // id,osm_id,lat,lon
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] f = line.Split(',');
            int id = g.AddNode(Num(f[2]), Num(f[3]));
            if (id != int.Parse(f[0]))
                throw new InvalidDataException($"nodes.csv: ids must be 0, 1, 2 ... in order (expected {id}).");
        }

        foreach (string line in File.ReadLines(edgesPath).Skip(1))      // from,to,length_m,road_type,maxspeed,oneway,bridge,name
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] f = line.Split(',', 8);                           // a street name may contain commas
            string type = f[3];
            bool hasLimit = double.TryParse(f[4].Split(';', ' ')[0], NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out double speed) && speed > 0;
            if (!hasLimit) speed = DefaultSpeed.GetValueOrDefault(type, 30);
            g.AddEdge(new Edge(int.Parse(f[0]), int.Parse(f[1]), Num(f[2]), type, f[7].Trim('"'), speed));
        }

        return g;
    }

    // Turns text like "6.9095" into a number (always using a dot for decimals).
    private static double Num(string text) => double.Parse(text, CultureInfo.InvariantCulture);
}
