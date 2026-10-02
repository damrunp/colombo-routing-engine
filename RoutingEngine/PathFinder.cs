namespace RoutingEngine;

// Finds routes. Dijkstra and A* find one route; Floyd-Warshall finds every route at once.
public class PathFinder
{
    public const int FloydWarshallMaxNodes = 2500;
    private readonly Graph _g;

    // Creates a route finder for a map.
    public PathFinder(Graph g) => _g = g;

    // Finds the best route with Dijkstra: always looks at the closest unvisited junction next.
    public PathResult Dijkstra(int s, int t, Metric m, RouteConstraints? c = null) =>
        Search(s, t, m, c ?? new RouteConstraints(), useAStar: false);

    // Finds the best route with A*: like Dijkstra, but also aims towards the destination.
    public PathResult AStar(int s, int t, Metric m, RouteConstraints? c = null) =>
        Search(s, t, m, c ?? new RouteConstraints(), useAStar: true);

    // The search used by both Dijkstra and A*. A* just adds a guess of the distance left.
    private PathResult Search(int s, int t, Metric m, RouteConstraints c, bool useAStar)
    {
        string? error = Validate(s, t, c);
        if (error != null) return PathResult.Fail(error);

        int n = _g.NodeCount;
        var dist = new double[n];              // best cost found so far to each junction
        var parent = new Edge?[n];             // the road used to arrive at each junction
        Array.Fill(dist, double.PositiveInfinity);

        Node goal = _g.GetNode(t);
        double maxSpeedMs = _g.MaxSpeedKmh / 3.6;

        // A*'s guess of the cost left: the straight line to the goal. It must never guess too high.
        double Guess(int v)
        {
            if (!useAStar) return 0;
            double straight = 0.99 * Haversine(_g.GetNode(v), goal);
            if (m == Metric.Distance) return straight;
            return maxSpeedMs > 0 ? straight / maxSpeedMs : 0;
        }

        var pq = new PriorityQueue<int, double>();   // always gives back the lowest cost first
        dist[s] = 0;
        pq.Enqueue(s, Guess(s));
        int explored = 0;

        while (pq.TryDequeue(out int u, out double priority))
        {
            if (priority > dist[u] + Guess(u) + 1e-9) continue;   // old copy: a better way was found later
            explored++;
            if (u == t) break;

            foreach (Edge e in _g.EdgesFrom(u))
            {
                if (!c.Allows(e)) continue;
                double newDist = dist[u] + e.Weight(m);
                if (newDist < dist[e.To])
                {
                    dist[e.To] = newDist;
                    parent[e.To] = e;
                    pq.Enqueue(e.To, newDist + Guess(e.To));
                }
            }
        }

        if (double.IsPositiveInfinity(dist[t]))
            return PathResult.Fail("No route: one-way streets or closures cut it off.", explored);

        // Walk back from the end to the start. A stack flips the order so the route reads start-first.
        var stack = new Stack<int>();
        double meters = 0, seconds = 0;
        for (int v = t; ; v = parent[v]!.From)
        {
            stack.Push(v);
            if (v == s) break;
            meters += parent[v]!.LengthMeters;
            seconds += parent[v]!.Seconds;
        }

        return new PathResult
        {
            Found = true, Path = new List<int>(stack), Cost = dist[t],
            Meters = meters, Seconds = seconds, Explored = explored
        };
    }

    // Finds the best cost between EVERY pair of junctions (dynamic programming).
    // Step k asks: "is it cheaper to go from i to j through junction k?"
    public double[,] FloydWarshall(Metric m)
    {
        int n = _g.NodeCount;
        if (n > FloydWarshallMaxNodes)
            throw new InvalidOperationException($"Floyd-Warshall is limited to {FloydWarshallMaxNodes} junctions.");

        var d = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                d[i, j] = i == j ? 0 : double.PositiveInfinity;

        foreach (Edge e in _g.AllEdges())
            d[e.From, e.To] = Math.Min(d[e.From, e.To], e.Weight(m));

        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
            {
                if (double.IsPositiveInfinity(d[i, k])) continue;
                for (int j = 0; j < n; j++)
                    if (d[i, k] + d[k, j] < d[i, j])
                        d[i, j] = d[i, k] + d[k, j];
            }

        return d;
    }

    // Checks the start and end are usable before searching. Returns an error message, or null if fine.
    private string? Validate(int s, int t, RouteConstraints c)
    {
        if (_g.NodeCount == 0) return "The network is empty.";
        if (!_g.IsValid(s)) return $"Start junction {s} does not exist.";
        if (!_g.IsValid(t)) return $"End junction {t} does not exist.";
        if (c.BlockedNodes.Contains(s) || c.BlockedNodes.Contains(t)) return "The start or end junction is closed.";
        return null;
    }

    // Straight-line distance in metres between two GPS points (the haversine formula).
    public static double Haversine(Node a, Node b)
    {
        const double R = 6371009;   // Earth's radius in metres
        double rad = Math.PI / 180;
        double dLat = (b.Lat - a.Lat) * rad, dLon = (b.Lon - a.Lon) * rad;
        double h = Math.Pow(Math.Sin(dLat / 2), 2) +
                   Math.Cos(a.Lat * rad) * Math.Cos(b.Lat * rad) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(Math.Min(1, h)));
    }
}
