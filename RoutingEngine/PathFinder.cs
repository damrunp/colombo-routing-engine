namespace RoutingEngine;

/// <summary>
/// Shortest-path algorithms.
///
///   Dijkstra (binary heap)   O((V + E) log V) time, O(V) extra space   - one source, one target
///   A*                       same worst case, usually explores far fewer nodes
///   Dijkstra (linear scan)   O(V^2) time - same algorithm, list instead of heap
///   Floyd-Warshall (DP)      O(V^3) time, O(V^2) space                 - every pair at once
///
/// All of them assume non-negative weights (guaranteed by Edge).
/// </summary>
public class PathFinder
{
    /// <summary>Above this, Floyd-Warshall's two V x V tables become too large.</summary>
    public const int FloydWarshallMaxNodes = 2500;

    /// <summary>
    /// OSMnx rounds road lengths to 0.1 m, so a road can appear a few cm shorter
    /// than the straight line between its ends. Scaling the A* estimate down by 1%
    /// guarantees it never overestimates, which is what keeps A* correct.
    /// </summary>
    private const double HeuristicSafetyFactor = 0.99;

    private readonly Graph _graph;

    public PathFinder(Graph graph)
    {
        _graph = graph;
    }

    // ================================================================ Dijkstra and A*

    /// <summary>Best-first search ordered by distance-so-far g(n).</summary>
    public PathResult Dijkstra(int source, int target, Metric metric,
                               RouteConstraints? constraints = null) =>
        BestFirstSearch(source, target, metric, constraints ?? new RouteConstraints(),
                        useHeuristic: false);

    /// <summary>Best-first search ordered by g(n) + h(n), h = straight-line estimate to target.</summary>
    public PathResult AStar(int source, int target, Metric metric,
                            RouteConstraints? constraints = null) =>
        BestFirstSearch(source, target, metric, constraints ?? new RouteConstraints(),
                        useHeuristic: true);

    /// <summary>
    /// Dijkstra and A* are the SAME algorithm; A* just adds an estimate h(n)
    /// to the priority. With h(n) = 0 it IS Dijkstra.
    ///
    /// Priority queue: .NET PriorityQueue (a 4-ary min-heap, a tree stored in an array).
    /// It has no "decrease priority" operation, so when we find a shorter way to a node
    /// we simply add it again ("lazy deletion") and skip the old, stale copy when it
    /// comes out. The queue can then hold up to E entries, giving O(E log E) =
    /// O(E log V) time, since E is at most V^2 and log V^2 = 2 log V.
    /// </summary>
    private PathResult BestFirstSearch(int source, int target, Metric metric,
                                       RouteConstraints c, bool useHeuristic)
    {
        string name = useHeuristic ? "A*" : "Dijkstra";
        string? error = Validate(source, target, c);
        if (error != null) return PathResult.NotFound(name, error);

        int n = _graph.NodeCount;
        var dist = new double[n];          // best known cost from source      O(V)
        var parentEdge = new Edge?[n];     // edge used to reach each node     O(V)
        Array.Fill(dist, double.PositiveInfinity);

        Node goal = _graph.GetNode(target);
        double maxSpeedMs = _graph.MaxSpeedKmh / 3.6;

        // h(n): an estimate that must NEVER overestimate the true remaining cost.
        //  Distance: no road can be shorter than the straight line.
        //  Time:     no trip can beat the straight line driven at the network's top speed
        //            (congestion is always >= 1, so it can only make trips slower).
        double Heuristic(int v)
        {
            if (!useHeuristic) return 0;
            double straight = GeoUtils.HaversineMeters(_graph.GetNode(v), goal)
                              * HeuristicSafetyFactor;
            if (metric == Metric.Distance) return straight;
            return maxSpeedMs > 0 ? straight / maxSpeedMs : 0;   // 0 = no edges yet
        }

        var queue = new PriorityQueue<int, double>();
        dist[source] = 0;
        queue.Enqueue(source, Heuristic(source));
        int explored = 0;

        while (queue.TryDequeue(out int u, out double priority))
        {
            // Stale entry: a better route to u was found after this copy was queued.
            if (priority > dist[u] + Heuristic(u) + 1e-9) continue;

            explored++;
            if (u == target) break;    // target settled: its cost can no longer improve

            foreach (Edge e in _graph.GetEdgesFrom(u))
            {
                if (!c.IsEdgeAllowed(e)) continue;     // constrained routing: O(1)

                double newDist = dist[u] + e.Weight(metric);
                if (newDist < dist[e.To])              // "relaxation"
                {
                    dist[e.To] = newDist;
                    parentEdge[e.To] = e;
                    queue.Enqueue(e.To, newDist + Heuristic(e.To));
                }
            }
        }

        if (double.IsPositiveInfinity(dist[target]))
            return PathResult.NotFound(name,
                $"No route from {source} to {target}: one-way streets or the current " +
                $"constraints ({c.Describe()}) cut it off.", explored);

        return BuildResult(name, source, target, parentEdge, dist[target], explored);
    }

    // ================================================================ Dijkstra, linear scan

    /// <summary>
    /// Dijkstra WITHOUT a heap: each step scans all V nodes for the smallest distance.
    /// V steps x O(V) scan = O(V^2). Kept only to compare against the heap version.
    /// On a sparse road network (E is about 2V) the heap version is much faster;
    /// on a very dense graph (E close to V^2) this version can actually win.
    /// </summary>
    public PathResult DijkstraLinearScan(int source, int target, Metric metric,
                                         RouteConstraints? constraints = null)
    {
        const string name = "Dijkstra (linear scan)";
        RouteConstraints c = constraints ?? new RouteConstraints();
        string? error = Validate(source, target, c);
        if (error != null) return PathResult.NotFound(name, error);

        int n = _graph.NodeCount;
        var dist = new double[n];
        var parentEdge = new Edge?[n];
        var settled = new bool[n];
        Array.Fill(dist, double.PositiveInfinity);
        dist[source] = 0;
        int explored = 0;

        while (true)
        {
            // The O(V) scan that the heap replaces.
            int u = -1;
            double best = double.PositiveInfinity;
            for (int v = 0; v < n; v++)
            {
                if (!settled[v] && dist[v] < best)
                {
                    best = dist[v];
                    u = v;
                }
            }

            if (u == -1) break;        // everything reachable is settled
            settled[u] = true;
            explored++;
            if (u == target) break;

            foreach (Edge e in _graph.GetEdgesFrom(u))
            {
                if (!c.IsEdgeAllowed(e) || settled[e.To]) continue;
                double newDist = dist[u] + e.Weight(metric);
                if (newDist < dist[e.To])
                {
                    dist[e.To] = newDist;
                    parentEdge[e.To] = e;
                }
            }
        }

        if (double.IsPositiveInfinity(dist[target]))
            return PathResult.NotFound(name,
                $"No route from {source} to {target} ({c.Describe()}).", explored);

        return BuildResult(name, source, target, parentEdge, dist[target], explored);
    }

    // ================================================================ Floyd-Warshall

    /// <summary>
    /// DYNAMIC PROGRAMMING. dist_k[i, j] = best cost from i to j using only
    /// nodes 0..k as stopping points:
    ///     dist_k[i, j] = min( dist_(k-1)[i, j],  dist_(k-1)[i, k] + dist_(k-1)[k, j] )
    /// Each answer is built from answers to smaller sub-problems, stored in a table.
    /// Three nested loops over V: O(V^3) time. Two V x V tables: O(V^2) space.
    /// </summary>
    public AllPairsResult FloydWarshall(Metric metric, RouteConstraints? constraints = null)
    {
        int n = _graph.NodeCount;
        if (n > FloydWarshallMaxNodes)
            throw new InvalidOperationException(
                $"Floyd-Warshall on {n} nodes would need two {n} x {n} tables " +
                $"(about {12.0 * n * n / 1_000_000:F0} MB). Limit is {FloydWarshallMaxNodes} nodes.");

        RouteConstraints c = constraints ?? new RouteConstraints();
        var dist = new double[n, n];
        var next = new int[n, n];

        // Base case (k = none): only direct roads are known.
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                dist[i, j] = i == j ? 0 : double.PositiveInfinity;
                next[i, j] = i == j ? i : -1;
            }
        }

        foreach (Edge e in _graph.AllEdges())
        {
            if (!c.IsEdgeAllowed(e)) continue;
            double w = e.Weight(metric);
            if (w < dist[e.From, e.To])
            {
                dist[e.From, e.To] = w;
                next[e.From, e.To] = e.To;
            }
        }

        // Fill the table: allow node k as a stopping point, for k = 0, 1, ... V-1.
        for (int k = 0; k < n; k++)
        {
            for (int i = 0; i < n; i++)
            {
                double ik = dist[i, k];
                if (double.IsPositiveInfinity(ik)) continue;   // i cannot reach k: skip row

                for (int j = 0; j < n; j++)
                {
                    double viaK = ik + dist[k, j];
                    if (viaK < dist[i, j])
                    {
                        dist[i, j] = viaK;
                        next[i, j] = next[i, k];
                    }
                }
            }
        }

        return new AllPairsResult(dist, next);
    }

    // ================================================================ helpers

    /// <summary>Catches bad input before any search starts. O(1).</summary>
    private string? Validate(int source, int target, RouteConstraints c)
    {
        if (_graph.NodeCount == 0)
            return "The network is empty.";
        if (!_graph.IsValidNode(source))
            return $"Source node {source} does not exist (valid ids: 0 to {_graph.NodeCount - 1}).";
        if (!_graph.IsValidNode(target))
            return $"Destination node {target} does not exist (valid ids: 0 to {_graph.NodeCount - 1}).";
        if (c.BlockedNodes.Contains(source))
            return $"Source node {source} is itself closed.";
        if (c.BlockedNodes.Contains(target))
            return $"Destination node {target} is closed.";
        return null;
    }

    /// <summary>
    /// Walks the parent edges backwards from target to source. That produces the
    /// route in reverse, so each node is pushed onto a STACK; popping (enumerating)
    /// the stack gives it source-first. O(path length).
    /// </summary>
    private static PathResult BuildResult(string name, int source, int target,
                                          Edge?[] parentEdge, double cost, int explored)
    {
        var stack = new Stack<int>();
        double distance = 0, time = 0;
        int current = target;
        stack.Push(current);

        while (current != source)
        {
            Edge e = parentEdge[current]!;
            distance += e.LengthMeters;
            time += e.TravelTimeSeconds;
            current = e.From;
            stack.Push(current);
        }

        return new PathResult
        {
            Found = true,
            AlgorithmName = name,
            Path = new List<int>(stack),      // enumerates top-first = source-first
            Cost = cost,
            TotalDistanceMeters = distance,
            TotalTimeSeconds = time,
            NodesExplored = explored,
            Message = "OK"
        };
    }
}
