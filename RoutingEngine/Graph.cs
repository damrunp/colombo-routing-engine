using System.Globalization;
using System.Text;

namespace RoutingEngine;

public enum TrafficCondition
{
    OffPeak,
    RushHour
}

/// <summary>
/// The road network, stored as an ADJACENCY LIST.
///
/// _adjacency[u] is the list of edges leaving node u.
///   Space:                  O(V + E)
///   Iterate neighbours:     O(degree(u))   <- what Dijkstra/A*/BFS/DFS do constantly
///   Add a node or edge:     O(1) amortised
///   Does edge u->v exist?:  O(degree(u))   <- rare in routing, and degree is only 1-4 here
///
/// Because node ids are 0..V-1, the outer structure is a List indexed by id:
/// an O(1) lookup with no hashing needed.
/// </summary>
public class Graph
{
    private readonly List<Node> _nodes = new();
    private readonly List<List<Edge>> _adjacency = new();
    private int _edgeCount;

    /// <summary>
    /// Speed used when OSM has no speed limit for a road.
    /// Each value is the most common TAGGED speed for that road type in the
    /// Colombo 2 + 7 data itself (e.g. 125 of 133 tagged residential roads are 30).
    /// Road types with no tagged examples use the residential value.
    /// </summary>
    public static readonly Dictionary<string, double> DefaultSpeedKmh = new()
    {
        ["trunk"] = 60,
        ["primary"] = 60,
        ["primary_link"] = 60,
        ["secondary"] = 50,
        ["secondary_link"] = 50,
        ["tertiary"] = 40,
        ["tertiary_link"] = 40,
        ["residential"] = 30,
        ["living_street"] = 30,
        ["unclassified"] = 30
    };

    public const double FallbackSpeedKmh = 30;

    /// <summary>
    /// Simulated rush-hour slowdown per road type (ASSUMPTION - no live data).
    /// Main roads carry the most traffic, so they slow down the most.
    /// </summary>
    public static readonly Dictionary<string, double> RushHourFactor = new()
    {
        ["trunk"] = 2.0,
        ["primary"] = 1.8,
        ["primary_link"] = 1.8,
        ["secondary"] = 1.5,
        ["secondary_link"] = 1.5,
        ["tertiary"] = 1.3,
        ["tertiary_link"] = 1.3,
        ["residential"] = 1.1,
        ["unclassified"] = 1.1,
        ["living_street"] = 1.0
    };

    public int NodeCount => _nodes.Count;
    public int EdgeCount => _edgeCount;
    public double MaxSpeedKmh { get; private set; }   // used by the A* heuristic
    public IReadOnlyList<Node> Nodes => _nodes;

    // ---------------------------------------------------------------- building

    /// <summary>Adds a node and returns its id. O(1) amortised.</summary>
    public int AddNode(double lat, double lon)
    {
        int id = _nodes.Count;
        _nodes.Add(new Node(id, lat, lon));
        _adjacency.Add(new List<Edge>());
        return id;
    }

    /// <summary>Adds a directed edge. O(1) amortised.</summary>
    public void AddEdge(Edge edge)
    {
        if (!IsValidNode(edge.From) || !IsValidNode(edge.To))
            throw new ArgumentException(
                $"Edge {edge.From}->{edge.To} refers to a node that does not exist.");

        _adjacency[edge.From].Add(edge);
        _edgeCount++;
        MaxSpeedKmh = Math.Max(MaxSpeedKmh, edge.SpeedKmh);
    }

    // ---------------------------------------------------------------- queries

    public bool IsValidNode(int id) => id >= 0 && id < _nodes.Count;

    public Node GetNode(int id) => _nodes[id];

    /// <summary>All roads leaving a node. O(1) to get the list.</summary>
    public IReadOnlyList<Edge> GetEdgesFrom(int id) => _adjacency[id];

    /// <summary>Finds the road u->v, or null. O(degree(u)).</summary>
    public Edge? FindEdge(int from, int to)
    {
        foreach (Edge e in _adjacency[from])
            if (e.To == to)
                return e;
        return null;
    }

    /// <summary>Every edge in the network. O(V + E).</summary>
    public IEnumerable<Edge> AllEdges()
    {
        foreach (List<Edge> list in _adjacency)
            foreach (Edge e in list)
                yield return e;
    }

    // ---------------------------------------------------------------- traffic

    /// <summary>Sets every edge's congestion for the chosen condition. O(E).</summary>
    public void ApplyTraffic(TrafficCondition condition)
    {
        foreach (Edge e in AllEdges())
        {
            if (condition == TrafficCondition.RushHour &&
                RushHourFactor.TryGetValue(e.RoadType, out double factor))
                e.CongestionFactor = factor;
            else
                e.CongestionFactor = 1.0;
        }
    }

    // ---------------------------------------------------------------- speed

    /// <summary>
    /// Uses the OSM speed limit when there is one; otherwise the default
    /// for the road type; otherwise the fallback.
    /// </summary>
    public static double ResolveSpeed(string roadType, string maxspeedText, out bool fromOsm)
    {
        // OSM sometimes stores things like "50 mph" or "40;50": take the first number.
        string firstToken = maxspeedText.Trim().Split(' ', ';')[0];
        if (double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double tagged) && tagged > 0)
        {
            fromOsm = true;
            return tagged;
        }

        fromOsm = false;
        return DefaultSpeedKmh.TryGetValue(roadType, out double def) ? def : FallbackSpeedKmh;
    }

    // ---------------------------------------------------------------- loading

    /// <summary>
    /// Builds the graph from nodes.csv and edges.csv. O(V + E).
    /// Throws a clear error if a file is missing or malformed.
    /// </summary>
    public static Graph LoadFromCsv(string nodesPath, string edgesPath)
    {
        if (!File.Exists(nodesPath))
            throw new FileNotFoundException($"Could not find {nodesPath}");
        if (!File.Exists(edgesPath))
            throw new FileNotFoundException($"Could not find {edgesPath}");

        var graph = new Graph();

        // ---- nodes
        string[] nodeLines = File.ReadAllLines(nodesPath);
        if (nodeLines.Length == 0)
            throw new InvalidDataException("nodes.csv is empty.");

        Dictionary<string, int> nc = ColumnIndex(nodeLines[0]);
        int idCol = Column(nc, "id", "nodes.csv");
        int latCol = Column(nc, "lat", "nodes.csv");
        int lonCol = Column(nc, "lon", "nodes.csv");

        for (int i = 1; i < nodeLines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(nodeLines[i])) continue;
            string[] f = SplitCsvLine(nodeLines[i]);

            int id = ParseInt(Field(f, idCol), "nodes.csv", i);
            double lat = ParseDouble(Field(f, latCol), "nodes.csv", i);
            double lon = ParseDouble(Field(f, lonCol), "nodes.csv", i);

            int assigned = graph.AddNode(lat, lon);
            if (assigned != id)
                throw new InvalidDataException(
                    $"nodes.csv line {i + 1}: expected id {assigned} but found {id}. " +
                    "Node ids must be 0, 1, 2 ... in order.");
        }

        // ---- edges
        string[] edgeLines = File.ReadAllLines(edgesPath);
        if (edgeLines.Length == 0)
            throw new InvalidDataException("edges.csv is empty.");

        Dictionary<string, int> ec = ColumnIndex(edgeLines[0]);
        int fromCol = Column(ec, "from", "edges.csv");
        int toCol = Column(ec, "to", "edges.csv");
        int lenCol = Column(ec, "length_m", "edges.csv");
        int typeCol = Column(ec, "road_type", "edges.csv");
        int speedCol = Column(ec, "maxspeed", "edges.csv");
        int nameCol = Column(ec, "name", "edges.csv");

        for (int i = 1; i < edgeLines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(edgeLines[i])) continue;
            string[] f = SplitCsvLine(edgeLines[i]);

            int from = ParseInt(Field(f, fromCol), "edges.csv", i);
            int to = ParseInt(Field(f, toCol), "edges.csv", i);
            double length = ParseDouble(Field(f, lenCol), "edges.csv", i);
            string roadType = Field(f, typeCol).Trim();
            string name = Field(f, nameCol).Trim();
            double speed = ResolveSpeed(roadType, Field(f, speedCol), out bool fromOsm);

            graph.AddEdge(new Edge(from, to, length, roadType, name, speed, fromOsm));
        }

        return graph;
    }

    // ---------------------------------------------------------------- CSV helpers

    /// <summary>Maps each header name to its column number (a hash table lookup later).</summary>
    private static Dictionary<string, int> ColumnIndex(string headerLine)
    {
        var map = new Dictionary<string, int>();
        string[] headers = SplitCsvLine(headerLine);
        for (int i = 0; i < headers.Length; i++)
            map[headers[i].Trim()] = i;
        return map;
    }

    private static int Column(Dictionary<string, int> map, string name, string file)
    {
        if (!map.TryGetValue(name, out int index))
            throw new InvalidDataException($"{file} has no '{name}' column.");
        return index;
    }

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index] : "";

    private static int ParseInt(string text, string file, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new InvalidDataException($"{file} line {line + 1}: '{text}' is not a whole number.");
        return value;
    }

    // InvariantCulture: always read "6.9095" with a dot, whatever the computer's language settings.
    private static double ParseDouble(string text, string file, int line)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new InvalidDataException($"{file} line {line + 1}: '{text}' is not a number.");
        return value;
    }

    /// <summary>Splits one CSV line, respecting "quoted, fields". O(line length).</summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');   // "" inside quotes = a literal quote
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
