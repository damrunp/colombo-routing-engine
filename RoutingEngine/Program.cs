using System.Diagnostics;

namespace RoutingEngine;

public static class Program
{
    // Demo locations picked from the Colombo 2 + 7 data.
    // NOTE: these ids only match THIS nodes.csv. Re-downloading the map renumbers them.
    private const int GangaramayaJunction = 12;    // Sri Jinarathana Rd / Sir James Peiris Mw (Colombo 2)
    private const int HortonWijeramaJunction = 11; // Horton Place / Wijerama Mw (Colombo 7)
    private const int DawsonUnionJunction = 29;    // Dawson Street / Union Place (Colombo 2)
    private const int RegentStreetJunction = 44;   // Dean's Road / Regent Street
    private const int OneWayTrapNode = 3;          // Dean's Road point with no road leading INTO it

    // The eight nodes that make up Alexandra Roundabout.
    private static readonly int[] AlexandraRoundabout = { 25, 26, 104, 105, 183, 188, 189, 190 };

    private static Graph _graph = null!;
    private static PathFinder _finder = null!;

    public static void Main()
    {
        string dataDir = FindDataFolder();
        try
        {
            _graph = Graph.LoadFromCsv(Path.Combine(dataDir, "nodes.csv"),
                                       Path.Combine(dataDir, "edges.csv"));
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or ArgumentException)
        {
            Console.WriteLine("Could not load the network: " + ex.Message);
            Console.WriteLine("Make sure nodes.csv and edges.csv are in the Data folder.");
            return;
        }

        _finder = new PathFinder(_graph);
        Console.WriteLine($"Loaded Colombo 2 + 7: {_graph.NodeCount} junctions, {_graph.EdgeCount} directed road segments.");

        while (true)
        {
            PrintMenu();
            Console.Write("Choose an option: ");
            string? input = Console.ReadLine();
            if (input == null) return;
            Console.WriteLine();

            switch (input.Trim())
            {
                case "1": ShowNetworkSummary(); break;
                case "2": ShowShortestVsFastest(); break;
                case "3": CompareAlgorithms(); break;
                case "4": ShowConstrainedRouting(); break;
                case "5": ShowRushHour(); break;
                case "6": VerifyCorrectness(); break;
                case "7": RunEdgeCases(); break;
                case "0": return;
                default: Console.WriteLine("Please type a number from the menu."); break;
            }
        }
    }

    private static void PrintMenu()
    {
        Console.WriteLine();
        Console.WriteLine("================ COLOMBO ROUTING ENGINE ================");
        Console.WriteLine(" 1. Network summary");
        Console.WriteLine(" 2. Shortest vs fastest route (+ one-way check)");
        Console.WriteLine(" 3. Compare algorithms: Dijkstra vs A* vs linear scan vs Floyd-Warshall");
        Console.WriteLine(" 4. Constrained routing (heavy vehicle, closed roundabout)");
        Console.WriteLine(" 5. Rush hour vs off-peak");
        Console.WriteLine(" 6. Correctness check (all algorithms must agree)");
        Console.WriteLine(" 7. Edge cases / worst cases");
        Console.WriteLine(" 0. Exit");
        Console.WriteLine("========================================================");
    }

    // ================================================================ 1. summary

    private static void ShowNetworkSummary()
    {
        int v = _graph.NodeCount;
        int e = _graph.EdgeCount;
        double density = (double)e / ((double)v * (v - 1));

        Console.WriteLine("NETWORK SUMMARY");
        Console.WriteLine($"  Junctions (V):             {v}");
        Console.WriteLine($"  Directed road segments (E): {e}");
        Console.WriteLine($"  Average roads leaving a junction: {(double)e / v:F2}");
        Console.WriteLine($"  Density E / (V(V-1)):      {density:P2}  -> very SPARSE");
        Console.WriteLine();
        Console.WriteLine("  Why an adjacency list, not a matrix:");
        Console.WriteLine($"    Matrix cells needed (V x V): {(long)v * v:N0}  (~{8.0 * v * v / 1024:F0} KB of doubles)");
        Console.WriteLine($"    List entries needed (V + E): {v + e:N0}");
        Console.WriteLine($"    Matrix cells that would be empty: {1 - density:P2}");
        Console.WriteLine();

        var counts = new Dictionary<string, int>();   // hash table: O(1) per update
        int tagged = 0;
        foreach (Edge edge in _graph.AllEdges())
        {
            counts[edge.RoadType] = counts.GetValueOrDefault(edge.RoadType) + 1;
            if (edge.SpeedFromOsm) tagged++;
        }

        Console.WriteLine("  Road types:");
        foreach (var pair in counts.OrderByDescending(p => p.Value))
            Console.WriteLine($"    {pair.Key,-16} {pair.Value,4}   default speed {Graph.DefaultSpeedKmh.GetValueOrDefault(pair.Key, Graph.FallbackSpeedKmh)} km/h");
        Console.WriteLine();
        Console.WriteLine($"  Speed limit from OSM: {tagged} of {e} segments; the other {e - tagged} use the defaults above.");
    }

    // ================================================================ 2. shortest vs fastest

    private static void ShowShortestVsFastest()
    {
        Console.WriteLine($"FROM Gangaramaya area (node {GangaramayaJunction}) TO Horton Place (node {HortonWijeramaJunction})");
        PrintRoute("Shortest DISTANCE", _finder.Dijkstra(GangaramayaJunction, HortonWijeramaJunction, Metric.Distance));
        PrintRoute("Shortest TIME", _finder.Dijkstra(GangaramayaJunction, HortonWijeramaJunction, Metric.Time));
        Console.WriteLine("  -> The shortest road is not the quickest: main roads are faster.");
        Console.WriteLine();

        Console.WriteLine("ONE-WAY CHECK: the same two places in opposite directions");
        PrintRoute($"Regent Street ({RegentStreetJunction}) -> Horton Place ({HortonWijeramaJunction})",
                   _finder.Dijkstra(RegentStreetJunction, HortonWijeramaJunction, Metric.Time));
        PrintRoute($"Horton Place ({HortonWijeramaJunction}) -> Regent Street ({RegentStreetJunction})",
                   _finder.Dijkstra(HortonWijeramaJunction, RegentStreetJunction, Metric.Time));
        Console.WriteLine("  -> Different routes and times: the network must be DIRECTED.");
    }

    // ================================================================ 3. algorithm comparison

    private static void CompareAlgorithms()
    {
        int s = GangaramayaJunction, t = HortonWijeramaJunction;
        const int repeats = 1000;
        Console.WriteLine($"FASTEST ROUTE {s} -> {t}, each algorithm run {repeats} times (average shown)");
        Console.WriteLine();
        Console.WriteLine($"  {"Algorithm",-26}{"Time (s)",10}{"Explored",10}{"Avg run",14}");

        var runs = new (string Label, Func<PathResult> Run)[]
        {
            ("Dijkstra (binary heap)", () => _finder.Dijkstra(s, t, Metric.Time)),
            ("A*", () => _finder.AStar(s, t, Metric.Time)),
            ("Dijkstra (linear scan)", () => _finder.DijkstraLinearScan(s, t, Metric.Time))
        };

        foreach (var (label, run) in runs)
        {
            PathResult r = run();
            double micros = AverageMicroseconds(run, repeats);
            Console.WriteLine($"  {label,-26}{r.Cost,10:F1}{r.NodesExplored,10}{micros,11:F1} us");
        }

        var sw = Stopwatch.StartNew();
        AllPairsResult all = _finder.FloydWarshall(Metric.Time);
        sw.Stop();
        Console.WriteLine($"  {"Floyd-Warshall (all pairs)",-26}{all.Cost(s, t),10:F1}{"all",10}{sw.Elapsed.TotalMilliseconds,11:F1} ms (once)");
        Console.WriteLine();
        Console.WriteLine("  Same answer from every algorithm - they differ only in how much work they do.");
        Console.WriteLine($"  Floyd-Warshall answers ALL {(long)_graph.NodeCount * _graph.NodeCount:N0} pairs in that one run;");
        Console.WriteLine("  worth it only if the network is small and many queries are needed.");
    }

    /// <summary>
    /// Runs once first so .NET compiles the code (JIT) before timing starts;
    /// otherwise the first run would look unfairly slow.
    /// </summary>
    private static double AverageMicroseconds(Func<PathResult> run, int repeats)
    {
        run();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < repeats; i++) run();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds * 1000.0 / repeats;
    }

    // ================================================================ 4. constraints

    private static void ShowConstrainedRouting()
    {
        Console.WriteLine($"HEAVY VEHICLE: Gangaramaya ({GangaramayaJunction}) -> Dawson St / Union Place ({DawsonUnionJunction})");
        PrintRoute("Normal car", _finder.Dijkstra(GangaramayaJunction, DawsonUnionJunction, Metric.Time));

        var heavy = new RouteConstraints();
        heavy.AvoidRoadTypes.Add("residential");
        heavy.AvoidRoadTypes.Add("living_street");
        PrintRoute($"Heavy vehicle ({heavy.Describe()})",
                   _finder.Dijkstra(GangaramayaJunction, DawsonUnionJunction, Metric.Time, heavy));
        Console.WriteLine("  -> Longer, but keeps the lorry off narrow residential lanes.");
        Console.WriteLine();

        Console.WriteLine($"ROAD CLOSURE: Gangaramaya ({GangaramayaJunction}) -> Horton Place ({HortonWijeramaJunction})");
        PrintRoute("Normal", _finder.Dijkstra(GangaramayaJunction, HortonWijeramaJunction, Metric.Time));

        var closed = new RouteConstraints();
        foreach (int node in AlexandraRoundabout) closed.BlockedNodes.Add(node);
        PrintRoute("Alexandra Roundabout closed",
                   _finder.Dijkstra(GangaramayaJunction, HortonWijeramaJunction, Metric.Time, closed));
        Console.WriteLine("  -> The engine detours around the closure automatically.");
    }

    // ================================================================ 5. rush hour

    private static void ShowRushHour()
    {
        int s = GangaramayaJunction, t = HortonWijeramaJunction;
        Console.WriteLine($"FASTEST ROUTE {s} -> {t}");

        _graph.ApplyTraffic(TrafficCondition.OffPeak);
        PathResult offPeak = _finder.Dijkstra(s, t, Metric.Time);
        PrintRoute("Off-peak", offPeak);

        _graph.ApplyTraffic(TrafficCondition.RushHour);
        PathResult rush = _finder.Dijkstra(s, t, Metric.Time);
        PrintRoute("Rush hour (congestion-aware)", rush);

        double sameRouteAtRush = PathTime(offPeak.Path);
        Console.WriteLine($"  Taking the usual off-peak route at rush hour: {FormatTime(sameRouteAtRush)}");
        Console.WriteLine($"  Congestion-aware route saves:                 {FormatTime(sameRouteAtRush - rush.TotalTimeSeconds)}");

        _graph.ApplyTraffic(TrafficCondition.OffPeak);   // always restore normal traffic
    }

    /// <summary>Travel time of a given path under the CURRENT traffic. O(path length x degree).</summary>
    private static double PathTime(List<int> path)
    {
        double total = 0;
        for (int i = 0; i + 1 < path.Count; i++)
            total += _graph.FindEdge(path[i], path[i + 1])!.TravelTimeSeconds;
        return total;
    }

    // ================================================================ 6. correctness

    private static void VerifyCorrectness()
    {
        const int pairs = 500;
        Console.WriteLine($"Checking {pairs} random pairs: Dijkstra, A*, linear scan and Floyd-Warshall must agree...");

        AllPairsResult fw = _finder.FloydWarshall(Metric.Time);
        var rng = new Random(42);   // fixed seed = same pairs every run (repeatable)
        int agreed = 0, bothUnreachable = 0, mismatches = 0;

        for (int i = 0; i < pairs; i++)
        {
            int s = rng.Next(_graph.NodeCount);
            int t = rng.Next(_graph.NodeCount);

            PathResult d = _finder.Dijkstra(s, t, Metric.Time);
            PathResult a = _finder.AStar(s, t, Metric.Time);
            PathResult l = _finder.DijkstraLinearScan(s, t, Metric.Time);
            double f = fw.Cost(s, t);

            if (!d.Found && !a.Found && !l.Found && double.IsPositiveInfinity(f))
                bothUnreachable++;
            else if (d.Found && a.Found && l.Found &&
                     Close(d.Cost, a.Cost) && Close(d.Cost, l.Cost) && Close(d.Cost, f))
                agreed++;
            else
            {
                mismatches++;
                Console.WriteLine($"  MISMATCH {s}->{t}: Dijkstra {d.Cost:F3}, A* {a.Cost:F3}, linear {l.Cost:F3}, FW {f:F3}");
            }
        }

        Console.WriteLine($"  Same cost from all four:            {agreed}");
        Console.WriteLine($"  All four agree there is no route:   {bothUnreachable}   (one-way streets at the map edge)");
        Console.WriteLine($"  Mismatches:                         {mismatches}");
        Console.WriteLine(mismatches == 0 ? "  PASS" : "  FAIL - see mismatches above");
    }

    private static bool Close(double x, double y) =>
        Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Abs(x));

    // ================================================================ 7. edge cases

    private static void RunEdgeCases()
    {
        int s = GangaramayaJunction, t = HortonWijeramaJunction;
        Console.WriteLine("EDGE CASES - the engine must answer sensibly, never crash");
        Console.WriteLine();

        PrintRoute("1. Source = destination", _finder.Dijkstra(t, t, Metric.Time));
        PrintRoute("2. Source id does not exist (-1)", _finder.Dijkstra(-1, t, Metric.Time));
        PrintRoute("3. Destination id does not exist (99999)", _finder.Dijkstra(s, 99999, Metric.Time));
        PrintRoute($"4. Destination with no road leading into it ({RegentStreetJunction} -> {OneWayTrapNode})",
                   _finder.Dijkstra(RegentStreetJunction, OneWayTrapNode, Metric.Time));

        var noTrunk = new RouteConstraints();
        noTrunk.AvoidRoadTypes.Add("trunk");
        PrintRoute("5. Constraint makes it impossible (avoid trunk; both roads out of the source are trunk)",
                   _finder.Dijkstra(s, t, Metric.Time, noTrunk));

        var destClosed = new RouteConstraints();
        destClosed.BlockedNodes.Add(t);
        PrintRoute("6. Destination junction closed", _finder.Dijkstra(s, t, Metric.Time, destClosed));

        var trapped = new RouteConstraints();
        foreach (Edge e in _graph.GetEdgesFrom(s)) trapped.BlockRoad(e.From, e.To);
        PrintRoute("7. Every road out of the source closed", _finder.Dijkstra(s, t, Metric.Time, trapped));

        var emptyFinder = new PathFinder(new Graph());
        PrintRoute("8. Empty network", emptyFinder.Dijkstra(0, 0, Metric.Time));
    }

    // ================================================================ output helpers

    private static void PrintRoute(string title, PathResult r)
    {
        if (!r.Found)
        {
            Console.WriteLine($"  {title}");
            Console.WriteLine($"    NO ROUTE: {r.Message}");
            return;
        }

        Console.WriteLine($"  {title}");
        Console.WriteLine($"    {r.TotalDistanceMeters / 1000:F2} km, {FormatTime(r.TotalTimeSeconds)}, " +
                          $"{r.Path.Count} junctions, {r.NodesExplored} nodes explored");
        Console.WriteLine($"    Via: {DescribeStreets(r.Path)}");
    }

    /// <summary>Street names along the route, without repeats or unnamed segments.</summary>
    private static string DescribeStreets(List<int> path)
    {
        if (path.Count <= 1) return "(already at the destination)";

        var names = new List<string>();
        for (int i = 0; i + 1 < path.Count; i++)
        {
            string name = _graph.FindEdge(path[i], path[i + 1])?.Name ?? "";
            if (name.Length > 0 && (names.Count == 0 || names[^1] != name))
                names.Add(name);
        }
        return names.Count == 0 ? "(unnamed roads)" : string.Join(" -> ", names);
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes} min {t.Seconds:D2} s";
    }

    private static string FindDataFolder()
    {
        string[] candidates =
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Data"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data"))
        };
        foreach (string dir in candidates)
            if (File.Exists(Path.Combine(dir, "nodes.csv")))
                return dir;
        return candidates[0];
    }
}
