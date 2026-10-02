using System.Diagnostics;

namespace RoutingEngine;

// Makes fake test maps and compares an adjacency list with an adjacency matrix.
public static class Benchmark
{
    private static readonly double[] Speeds = { 30, 40, 50, 60 };
    private static double _sink;                 // stops .NET from skipping "unused" test loops

    // Makes a grid-shaped map like city blocks: side x side junctions about 100 m apart.
    public static Graph Grid(int side, int seed = 1)
    {
        var rng = new Random(seed);
        var g = new Graph();
        const double step = 0.0009;              // about 100 m

        for (int r = 0; r < side; r++)
            for (int c = 0; c < side; c++)
                g.AddNode(6.9 + r * step, 79.85 + c * step);

        for (int r = 0; r < side; r++)
            for (int c = 0; c < side; c++)
            {
                int id = r * side + c;
                if (c + 1 < side) AddRoad(g, rng, id, id + 1);
                if (r + 1 < side) AddRoad(g, rng, id, id + side);
            }
        return g;
    }

    // Makes a crowded map where each pair of junctions has a road with chance p.
    public static Graph Dense(int n, double p, int seed = 1)
    {
        var rng = new Random(seed);
        var g = new Graph();
        for (int i = 0; i < n; i++)
            g.AddNode(6.9 + rng.NextDouble() * 0.03, 79.85 + rng.NextDouble() * 0.03);

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (rng.NextDouble() < p) AddRoad(g, rng, i, j);
        return g;
    }

    // Adds a two-way road a bit longer than the straight line (real roads bend) with a random speed limit.
    private static void AddRoad(Graph g, Random rng, int a, int b)
    {
        double length = PathFinder.Haversine(g.GetNode(a), g.GetNode(b)) * (1.0 + rng.NextDouble() * 0.3);
        double speed = Speeds[rng.Next(Speeds.Length)];
        g.AddEdge(new Edge(a, b, length, "synthetic", "", speed));
        g.AddEdge(new Edge(b, a, length, "synthetic", "", speed));
    }

    // Compares an adjacency list and an adjacency matrix on a sparse map and a crowded map.
    public static void SparseVsDense()
    {
        Console.WriteLine("484 junctions each. Memory is measured; times are the average of 20 runs.");
        Console.WriteLine($"{"Network",-26}{"Edges",9}{"List MB",10}{"Matrix MB",11}{"List scan ms",14}{"Matrix scan ms",16}");
        Compare("Sparse (road-like grid)", () => Grid(22));
        Compare("Dense (50% of all pairs)", () => Dense(484, 0.5));
        Console.WriteLine();
        Console.WriteLine("Sparse: the list is much smaller and faster, because it only stores roads that exist.");
        Console.WriteLine("Dense:  the matrix uses less memory, because each list entry is a whole road object.");
        Console.WriteLine("Real road maps are sparse, so this project uses a list.");
    }

    // Builds one map, then measures memory and "visit every road" time for the list and the matrix.
    private static void Compare(string label, Func<Graph> build)
    {
        long before = GC.GetTotalMemory(true);
        Graph g = build();
        double listMb = (GC.GetTotalMemory(true) - before) / 1_048_576.0;

        before = GC.GetTotalMemory(true);
        double[,] matrix = ToMatrix(g);
        double matrixMb = (GC.GetTotalMemory(true) - before) / 1_048_576.0;

        int n = g.NodeCount;
        double listScan = TimeMs(() =>
        {
            double sum = 0;
            for (int u = 0; u < n; u++)
                foreach (Edge e in g.EdgesFrom(u)) sum += e.LengthMeters;   // only real roads
            _sink = sum;
        }, 20);
        double matrixScan = TimeMs(() =>
        {
            double sum = 0;
            for (int u = 0; u < n; u++)
                for (int v = 0; v < n; v++)                                // every cell, road or not
                    if (!double.IsPositiveInfinity(matrix[u, v])) sum += matrix[u, v];
            _sink = sum;
        }, 20);

        Console.WriteLine($"{label,-26}{g.EdgeCount,9}{listMb,10:F2}{matrixMb,11:F2}{listScan,14:F3}{matrixScan,16:F3}");
    }

    // Turns the map into a table: cell [from, to] = road length, or infinity if there is no road.
    private static double[,] ToMatrix(Graph g)
    {
        int n = g.NodeCount;
        var m = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                m[i, j] = double.PositiveInfinity;
        foreach (Edge e in g.AllEdges())
            m[e.From, e.To] = Math.Min(m[e.From, e.To], e.LengthMeters);
        return m;
    }

    // Runs a test several times and returns the average time in ms (after one warm-up run).
    public static double TimeMs(Action run, int repeats)
    {
        if (repeats > 1) run();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < repeats; i++) run();
        return sw.Elapsed.TotalMilliseconds / repeats;
    }
}
