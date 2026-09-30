namespace RoutingEngine;

/// <summary>The answer to one routing query.</summary>
public class PathResult
{
    public bool Found { get; init; }
    public string AlgorithmName { get; init; } = "";
    public string Message { get; init; } = "";
    public List<int> Path { get; init; } = new();   // node ids, source first
    public double Cost { get; init; }               // in the metric that was optimised
    public double TotalDistanceMeters { get; init; }
    public double TotalTimeSeconds { get; init; }
    public int NodesExplored { get; init; }          // how much work the search did

    public static PathResult NotFound(string algorithm, string message, int explored = 0) =>
        new()
        {
            Found = false,
            AlgorithmName = algorithm,
            Message = message,
            Cost = double.PositiveInfinity,
            NodesExplored = explored
        };
}

/// <summary>
/// Output of Floyd-Warshall: the cost between EVERY pair of nodes.
/// Two V x V tables, so O(V^2) memory.
/// </summary>
public class AllPairsResult
{
    private readonly double[,] _dist;
    private readonly int[,] _next;   // _next[i, j] = the node after i on the best path to j

    public AllPairsResult(double[,] dist, int[,] next)
    {
        _dist = dist;
        _next = next;
    }

    /// <summary>O(1) lookup - this is what the V^3 precomputation buys.</summary>
    public double Cost(int from, int to) => _dist[from, to];

    /// <summary>Rebuilds the path by following _next. O(path length).</summary>
    public List<int> GetPath(int from, int to)
    {
        var path = new List<int>();
        if (_next[from, to] == -1) return path;   // unreachable

        int current = from;
        path.Add(current);
        while (current != to)
        {
            current = _next[current, to];
            path.Add(current);
        }
        return path;
    }
}
