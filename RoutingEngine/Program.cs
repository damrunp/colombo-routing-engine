using System.Diagnostics;
using System.Globalization;

namespace RoutingEngine;

public static class Program
{
    // Junctions used in the fixed demos (these ids only match this nodes.csv).
    private const int Gangaramaya = 12;     // Sir James Peiris Mw / Sri Jinarathana Rd
    private const int HortonPlace = 11;     // Horton Place / Wijerama Mw
    private const int UnionPlace = 29;      // Dawson St / Union Place
    private const int RegentStreet = 44;    // Dean's Rd / Regent St
    private const int OneWayDeadEnd = 3;    // no road leads INTO this point
    private static readonly int[] AlexandraRoundabout = { 25, 26, 104, 105, 183, 188, 189, 190 };

    private static Graph _g = null!;
    private static PathFinder _pf = null!;
    private static Locations _loc = null!;
    private static Analysis _an = null!;

    // Loads the map, then shows the menu until the user exits.
    public static void Main()
    {
        string dir = Directory.Exists("Data") ? "Data" : Path.Combine(AppContext.BaseDirectory, "../../../Data");
        try
        {
            _g = Graph.Load(Path.Combine(dir, "nodes.csv"), Path.Combine(dir, "edges.csv"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not load the map: " + ex.Message);
            return;
        }
        _pf = new PathFinder(_g);
        _loc = new Locations(_g);
        _an = new Analysis(_g);

        while (true)
        {
            Console.WriteLine("\n======= COLOMBO ROUTING ENGINE =======");
            Console.WriteLine("1. Network summary");
            Console.WriteLine("2. Find a route (From / To)");
            Console.WriteLine("3. List locations");
            Console.WriteLine("4. Compare algorithms");
            Console.WriteLine("5. Road closure & heavy vehicle");
            Console.WriteLine("6. Rush hour vs off-peak");
            Console.WriteLine("7. Critical junctions & roads");
            Console.WriteLine("8. Edge cases");
            Console.WriteLine("9. Reachability (time budget)");
            Console.WriteLine("10. Connectivity (BFS vs DFS)");
            Console.WriteLine("11. Sparse vs dense (list vs matrix)");
            Console.WriteLine("0. Exit");
            Console.Write("Choose: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": Summary(); break;
                case "2": FindRoute(); break;
                case "3": ListLocations(); break;
                case "4": CompareAlgorithms(); break;
                case "5": Constraints(); break;
                case "6": RushHour(); break;
                case "7": CriticalPoints(); break;
                case "8": EdgeCases(); break;
                case "9": Reachability(); break;
                case "10": Connectivity(); break;
                case "11": Benchmark.SparseVsDense(); break;
                case "0":
                case null: return;
                default: Console.WriteLine("Type a number from the menu."); break;
            }
        }
    }

    // 1. Shows the size of the map: junctions, roads, one-way roads and locations.
    private static void Summary()
    {
        int oneWay = 0, twoWayEdges = 0;
        foreach (Edge e in _g.AllEdges())
            if (_g.FindEdge(e.To, e.From) == null) oneWay++; else twoWayEdges++;

        int v = _g.NodeCount, edges = _g.EdgeCount;
        Console.WriteLine($"Nodes (junctions):             {v}");
        Console.WriteLine($"Edges (directed segments):     {edges}");
        Console.WriteLine($"  one-way segments:            {oneWay}");
        Console.WriteLine($"  two-way roads:               {twoWayEdges / 2}  (stored as {twoWayEdges} edges)");
        Console.WriteLine($"Total roads (two-way once):    {oneWay + twoWayEdges / 2}");
        Console.WriteLine($"Locations (named streets):     {_loc.Names.Count}");
        Console.WriteLine($"Density E / V(V-1):            {(double)edges / ((double)v * (v - 1)):P2}  -> sparse");
        Console.WriteLine("Algorithms: 7 (Dijkstra, A*, Floyd-Warshall, BFS, DFS, brute-force critical search, merge sort)");
    }

    // 2. Asks for a start and end street, then shows the best route between them.
    private static void FindRoute()
    {
        string? from = AskLocation("From");
        if (from == null) return;
        string? to = AskLocation("To");
        if (to == null) return;

        Console.Write("1 = fastest, 2 = shortest [1]: ");
        Metric m = Console.ReadLine()?.Trim() == "2" ? Metric.Distance : Metric.Time;
        bool rush = AskYesNo("Rush hour? y/n [n]: ");

        _g.SetRushHour(rush);
        Print($"{from} -> {to}", _pf.AStar(_loc.NodeOf(from), _loc.NodeOf(to), m));
        _g.SetRushHour(false);
    }

    // 3. Prints every street name, A to Z, with its number.
    private static void ListLocations()
    {
        for (int i = 0; i < _loc.Names.Count; i++)
            Console.WriteLine($"{i + 1,3}. {_loc.Names[i]}");
    }

    // 4. Runs Dijkstra, A* and Floyd-Warshall on the same trip and compares speed and work done.
    private static void CompareAlgorithms()
    {
        int s = Gangaramaya, t = HortonPlace;
        Console.WriteLine($"Fastest route {s} -> {t} (average of 1000 runs)");

        Func<PathResult> dijkstra = () => _pf.Dijkstra(s, t, Metric.Time);
        Func<PathResult> aStar = () => _pf.AStar(s, t, Metric.Time);
        foreach (var (name, run) in new[] { ("Dijkstra", dijkstra), ("A*", aStar) })
        {
            PathResult r = run();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) run();
            Console.WriteLine($"  {name,-15} {r.Seconds,6:F1} s  {r.Explored,4} junctions explored  {sw.Elapsed.TotalMicroseconds / 1000,7:F1} us/run");
        }

        var fwTimer = Stopwatch.StartNew();
        double[,] all = _pf.FloydWarshall(Metric.Time);
        Console.WriteLine($"  {"Floyd-Warshall",-15} {all[s, t],6:F1} s  all {_g.NodeCount * _g.NodeCount:N0} pairs  {fwTimer.Elapsed.TotalMilliseconds,7:F1} ms (once)");
    }

    // 5. Shows how routes change for a heavy vehicle and when a roundabout is closed.
    private static void Constraints()
    {
        var heavy = new RouteConstraints();
        heavy.AvoidRoadTypes.UnionWith(new[] { "residential", "living_street" });
        Print("Car: Gangaramaya -> Union Place", _pf.Dijkstra(Gangaramaya, UnionPlace, Metric.Time));
        Print("Heavy vehicle (no residential roads)", _pf.Dijkstra(Gangaramaya, UnionPlace, Metric.Time, heavy));

        var closed = new RouteConstraints();
        closed.BlockedNodes.UnionWith(AlexandraRoundabout);
        Print("Normal: Gangaramaya -> Horton Place", _pf.Dijkstra(Gangaramaya, HortonPlace, Metric.Time));
        Print("Alexandra Roundabout closed", _pf.Dijkstra(Gangaramaya, HortonPlace, Metric.Time, closed));
    }

    // 6. Asks for two streets, then compares the route with no traffic and at rush hour.
    private static void RushHour()
    {
        string? from = AskLocation("From");
        if (from == null) return;
        string? to = AskLocation("To");
        if (to == null) return;
        int s = _loc.NodeOf(from), t = _loc.NodeOf(to);

        _g.SetRushHour(false);
        PathResult night = _pf.Dijkstra(s, t, Metric.Time);
        if (!night.Found)
        {
            Console.WriteLine($"No route: {night.Message}");
            return;
        }

        _g.SetRushHour(true);
        double sameRoute = PathSeconds(night.Path);       // the night route, but with rush-hour traffic
        PathResult rush = _pf.Dijkstra(s, t, Metric.Time);
        _g.SetRushHour(false);

        Console.WriteLine("\nNo traffic (night):");
        Console.WriteLine($"  {Minutes(night.Seconds)}   {Streets(night.Path)}");
        Console.WriteLine("Same route at rush hour:");
        Console.WriteLine($"  {Minutes(sameRoute)}");
        Console.WriteLine("Best route at rush hour:");
        Console.WriteLine($"  {Minutes(rush.Seconds)}   {Streets(rush.Path)}");

        if (rush.Path.SequenceEqual(night.Path))
            Console.WriteLine($"-> The same route is best either way; traffic adds {Minutes(rush.Seconds - night.Seconds)}.");
        else
            Console.WriteLine($"-> A different route is best at rush hour; it saves {Minutes(sameRoute - rush.Seconds)} compared with the usual route.");
    }

    // Adds up the travel time of a route, using the current traffic.
    private static double PathSeconds(List<int> path)
    {
        double total = 0;
        for (int i = 0; i + 1 < path.Count; i++)
            total += _g.FindEdge(path[i], path[i + 1])!.Seconds;
        return total;
    }

    // 7. Lists every junction and road that cuts places off if it closes, and explains the worst one.
    private static void CriticalPoints()
    {
        var timer = Stopwatch.StartNew();
        List<(int Node, int CutOff)> junctions = _an.CriticalJunctions();
        List<(int A, int B, int CutOff)> roads = _an.CriticalRoads();
        Console.WriteLine($"Checked all {_g.NodeCount} junctions and {_an.Roads().Count} roads in {timer.Elapsed.TotalMilliseconds:F0} ms (brute force).");

        Console.WriteLine($"\nCRITICAL JUNCTIONS: {junctions.Count}");
        for (int i = 0; i < junctions.Count; i++)
        {
            var (node, cut) = junctions[i];
            Console.WriteLine($"  {i + 1,3}. Junction {node,-5} cuts off {cut,3}   {StreetsAt(node)}");
        }

        Console.WriteLine($"\nCRITICAL ROADS: {roads.Count}");
        for (int i = 0; i < roads.Count; i++)
        {
            var (a, b, cut) = roads[i];
            string road = $"{a}-{b}";
            Console.WriteLine($"  {i + 1,3}. Road {road,-9} cuts off {cut,3}   {RoadName(a, b)}");
        }

        if (junctions.Count == 0) return;
        var (top, topCut) = junctions[0];
        List<string> lostStreets = _an.CutOffBy(top).SelectMany(n => StreetNames(n)).Distinct().ToList();
        Console.WriteLine($"\nMOST CRITICAL: junction {top} ({StreetsAt(top)})");
        Console.WriteLine($"  It is the ONLY way in to {topCut} junctions on: {string.Join(", ", lostStreets)}.");
        Console.WriteLine("  If it closes (accident, flooding, roadworks), those homes have no road access,");
        Console.WriteLine("  not even for ambulances. A second access road here would help most.");
        Console.WriteLine("\nBrute force is used on purpose: simple, and fast enough for this map.");
        Console.WriteLine("On a much bigger map, Tarjan's algorithm would do the same job in one DFS, O(V + E).");
    }

    // 8. Tries awkward inputs to show the program answers sensibly instead of crashing.
    private static void EdgeCases()
    {
        var noTrunk = new RouteConstraints();
        noTrunk.AvoidRoadTypes.Add("trunk");
        var endClosed = new RouteConstraints();
        endClosed.BlockedNodes.Add(HortonPlace);
        var trapped = new RouteConstraints();
        foreach (Edge e in _g.EdgesFrom(Gangaramaya)) trapped.BlockedNodes.Add(e.To);

        Print("1. Same start and end", _pf.Dijkstra(HortonPlace, HortonPlace, Metric.Time));
        Print("2. Start does not exist (-1)", _pf.Dijkstra(-1, HortonPlace, Metric.Time));
        Print("3. End does not exist (99999)", _pf.Dijkstra(Gangaramaya, 99999, Metric.Time));
        Print("4. No road leads into the end (one-way)", _pf.Dijkstra(RegentStreet, OneWayDeadEnd, Metric.Time));
        Print("5. Avoid trunk roads, but the start is only on trunk roads", _pf.Dijkstra(Gangaramaya, HortonPlace, Metric.Time, noTrunk));
        Print("6. End junction closed", _pf.Dijkstra(Gangaramaya, HortonPlace, Metric.Time, endClosed));
        Print("7. Every junction next to the start closed", _pf.Dijkstra(Gangaramaya, HortonPlace, Metric.Time, trapped));
        Print("8. Empty network", new PathFinder(new Graph()).Dijkstra(0, 0, Metric.Time));
    }

    // 9. Asks for a start and a time limit, then lists every location you can reach in that time.
    private static void Reachability()
    {
        string? start = AskLocation("Start");
        if (start == null) return;
        double minutes = AskNumber("Time budget in minutes [Enter = 2]: ", 2);
        bool rush = AskYesNo("Rush hour? y/n [n]: ");

        _g.SetRushHour(rush);
        Dictionary<int, (double Cost, int Roads)> reached = _an.Reachable(_loc.NodeOf(start), minutes * 60, Metric.Time);
        _g.SetRushHour(false);

        List<string> found = _loc.Names.Where(name => reached.ContainsKey(_loc.NodeOf(name))).ToList();
        found = Locations.MergeSort(found, (a, b) => reached[_loc.NodeOf(a)].Cost.CompareTo(reached[_loc.NodeOf(b)].Cost));

        Console.WriteLine($"\n{found.Count} location(s) reachable from {start} within {minutes:0.#} min {(rush ? "at rush hour" : "off-peak")}:");
        foreach (string name in found)
        {
            var (cost, roads) = reached[_loc.NodeOf(name)];
            Console.WriteLine($"  {cost / 60,5:F1} min   {name,-55} ({roads} road(s))");
        }
    }

    // 10. Checks the map is in one piece with BFS and DFS, and checks one-way access to Regent Street.
    private static void Connectivity()
    {
        int v = _g.NodeCount;
        int bfsCount = Analysis.Count(_an.Bfs(0));
        int dfsCount = Analysis.Count(_an.Dfs(0));
        double bfsUs = Benchmark.TimeMs(() => _an.Bfs(0), 1000) * 1000;
        double dfsUs = Benchmark.TimeMs(() => _an.Dfs(0), 1000) * 1000;

        Console.WriteLine("Ignoring one-way rules, is the road map in one piece?");
        Console.WriteLine($"  BFS (queue): reached {bfsCount} of {v} junctions in {bfsUs:F1} us");
        Console.WriteLine($"  DFS (stack): reached {dfsCount} of {v} junctions in {dfsUs:F1} us");
        Console.WriteLine(bfsCount == v ? "  -> Yes: one connected map." : "  -> No: the map is split.");

        bool[] reachedFrom = _an.Bfs(RegentStreet, Analysis.View.Forward);
        bool[] canReach = _an.Bfs(RegentStreet, Analysis.View.Backward);
        int both = Enumerable.Range(0, v).Count(i => reachedFrom[i] && canReach[i]);
        Console.WriteLine("\nFollowing one-way rules, to and from Regent Street:");
        Console.WriteLine($"  Reachable from it: {Analysis.Count(reachedFrom)}   Can reach it: {Analysis.Count(canReach)}   Both ways: {both}");
        Console.WriteLine("  -> The few missing junctions are on one-way streets cut off at the edge of the map.");
    }

    // Keeps asking until the user picks one street (by name or list number). Enter cancels.
    private static string? AskLocation(string label)
    {
        while (true)
        {
            Console.Write($"{label} (id or name, Enter to cancel): ");
            string input = Console.ReadLine()?.Trim() ?? "";
            if (input.Length == 0) return null;

            if (int.TryParse(input, out int n) && n >= 1 && n <= _loc.Names.Count)
                return Picked(_loc.Names[n - 1]);

            List<string> hits = _loc.Search(input);
            if (hits.Count == 1) return Picked(hits[0]);
            if (hits.Count == 0)
            {
                Console.WriteLine("  No match. Try one word, or see option 3 for the list.");
                continue;
            }

            for (int i = 0; i < hits.Count; i++) Console.WriteLine($"  {_loc.NumberOf(hits[i])}. {hits[i]}");
            Console.Write("  Type the number: ");
            if (int.TryParse(Console.ReadLine(), out int k) && hits.Contains(_loc.Names.ElementAtOrDefault(k - 1) ?? ""))
                return Picked(_loc.Names[k - 1]);
        }
    }

    // Shows the street the user picked, with its number.
    private static string Picked(string name)
    {
        Console.WriteLine($"  -> {_loc.NumberOf(name)}: {name}");
        return name;
    }

    // Asks for a number. Uses the default if the user presses Enter or types something invalid.
    private static double AskNumber(string prompt, double defaultValue)
    {
        Console.Write(prompt);
        string text = Console.ReadLine()?.Trim() ?? "";
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value
            : defaultValue;
    }

    // Asks a yes/no question. Only "y" counts as yes.
    private static bool AskYesNo(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim().ToLower() == "y";
    }

    // Gets the names of all streets that touch a junction.
    private static IEnumerable<string> StreetNames(int node) =>
        _g.AllEdges().Where(e => (e.From == node || e.To == node) && e.Name.Length > 0)
                     .Select(e => e.Name).Distinct();

    // Gets a junction's street names as one line, e.g. "Horton Place / Ward Place".
    private static string StreetsAt(int node)
    {
        List<string> names = StreetNames(node).ToList();
        return names.Count > 0 ? string.Join(" / ", names) : "(unnamed lane)";
    }

    // Gets the name of the road between two junctions.
    private static string RoadName(int a, int b)
    {
        string name = _g.FindEdge(a, b)?.Name ?? _g.FindEdge(b, a)?.Name ?? "";
        return name.Length > 0 ? name : "(unnamed lane)";
    }

    // Prints a route: distance, time, size, and the streets it uses.
    private static void Print(string title, PathResult r)
    {
        Console.WriteLine(title);
        if (!r.Found)
        {
            Console.WriteLine($"   NO ROUTE: {r.Message}");
            return;
        }
        Console.WriteLine($"   {r.Meters / 1000:F2} km, {FormatTime(r.Seconds)}, {r.Path.Count} junctions, {r.Explored} explored");
        Console.WriteLine($"   Via: {Streets(r.Path)}");
    }

    // Lists the street names along a route, without repeats.
    private static string Streets(List<int> path)
    {
        if (path.Count < 2) return "(already there)";
        var names = new List<string>();
        for (int i = 0; i + 1 < path.Count; i++)
        {
            string name = _g.FindEdge(path[i], path[i + 1])!.Name;
            if (name.Length > 0 && (names.Count == 0 || names[^1] != name)) names.Add(name);
        }
        return names.Count > 0 ? string.Join(" > ", names) : "(unnamed roads)";
    }

    // Turns seconds into minutes, e.g. "2.4 min".
    private static string Minutes(double s) => $"{s / 60:F1} min";

    // Turns seconds into "2 min 25 s".
    private static string FormatTime(double s) => $"{(int)(s / 60)} min {(int)(s % 60):D2} s";
}
