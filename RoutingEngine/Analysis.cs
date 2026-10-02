namespace RoutingEngine;

// Looks at the map as a whole: how far you can get, whether it is connected, and its weak spots.
public class Analysis
{
    public enum View { Undirected, Forward, Backward }

    private readonly Graph _g;
    private readonly List<int>[] _undirected;   // roads in both directions (ignores one-way rules)
    private readonly List<int>[] _forward;      // roads in their real direction
    private readonly List<int>[] _backward;     // roads turned around ("who can reach me?")

    // Builds three neighbour lists for every junction: both ways, forward and backward.
    public Analysis(Graph g)
    {
        _g = g;
        int n = g.NodeCount;
        _undirected = new List<int>[n];
        _forward = new List<int>[n];
        _backward = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            _undirected[i] = new List<int>();
            _forward[i] = new List<int>();
            _backward[i] = new List<int>();
        }
        foreach (Edge e in g.AllEdges())
        {
            _undirected[e.From].Add(e.To);
            _undirected[e.To].Add(e.From);
            _forward[e.From].Add(e.To);
            _backward[e.To].Add(e.From);
        }
    }

    // Picks the neighbour list for the chosen view.
    private List<int>[] Adj(View view) =>
        view == View.Forward ? _forward : view == View.Backward ? _backward : _undirected;

    // Finds every junction you can reach within the time budget, with its time and number of roads.
    // It is Dijkstra that stops as soon as the next junction is over budget.
    public Dictionary<int, (double Cost, int Roads)> Reachable(int source, double budget, Metric m)
    {
        var done = new Dictionary<int, (double Cost, int Roads)>();
        if (!_g.IsValid(source) || budget < 0) return done;

        var best = new Dictionary<int, (double Cost, int Roads)> { [source] = (0, 0) };
        var pq = new PriorityQueue<int, double>();
        pq.Enqueue(source, 0);

        while (pq.TryDequeue(out int u, out double d))
        {
            if (d > budget) break;                 // everything left is too far
            if (done.ContainsKey(u)) continue;     // already finished this junction
            done[u] = best[u];

            foreach (Edge e in _g.EdgesFrom(u))
            {
                double newCost = d + e.Weight(m);
                if (newCost <= budget && (!best.TryGetValue(e.To, out var old) || newCost < old.Cost))
                {
                    best[e.To] = (newCost, best[u].Roads + 1);
                    pq.Enqueue(e.To, newCost);
                }
            }
        }
        return done;
    }

    // BFS: uses a QUEUE to visit junctions ring by ring, nearest first. Returns which junctions were reached.
    public bool[] Bfs(int start, View view = View.Undirected)
    {
        var seen = new bool[_g.NodeCount];
        var queue = new Queue<int>();
        seen[start] = true;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            foreach (int v in Adj(view)[u])
            {
                if (seen[v]) continue;
                seen[v] = true;
                queue.Enqueue(v);
            }
        }
        return seen;
    }

    // DFS: uses a STACK to follow one road as far as it goes, then backs up. Returns which junctions were reached.
    public bool[] Dfs(int start, View view = View.Undirected)
    {
        var seen = new bool[_g.NodeCount];
        var stack = new Stack<int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            int u = stack.Pop();
            if (seen[u]) continue;
            seen[u] = true;
            foreach (int v in Adj(view)[u])
                if (!seen[v]) stack.Push(v);
        }
        return seen;
    }

    // Counts how many junctions were reached.
    public static int Count(bool[] seen) => seen.Count(x => x);

    // BRUTE FORCE: closes each junction one at a time and counts how many junctions get cut off.
    // Returns only the critical ones, worst first.
    public List<(int Node, int CutOff)> CriticalJunctions()
    {
        var results = new List<(int Node, int CutOff)>();
        if (_g.NodeCount < 3) return results;

        var before = Pieces();
        for (int x = 0; x < _g.NodeCount; x++)
        {
            if (before.Piece[x] != before.Largest) continue;
            int cutOff = before.LargestSize - 1 - Pieces(closedNode: x).LargestSize;
            if (cutOff > 0) results.Add((x, cutOff));
        }
        return Locations.MergeSort(results, (a, b) => b.CutOff.CompareTo(a.CutOff));
    }

    // BRUTE FORCE: closes each road one at a time and counts how many junctions get cut off.
    // Returns only the critical ones, worst first.
    public List<(int A, int B, int CutOff)> CriticalRoads()
    {
        var results = new List<(int A, int B, int CutOff)>();
        if (_g.NodeCount < 3) return results;

        var before = Pieces();
        foreach (var (a, b) in Roads())
        {
            if (before.Piece[a] != before.Largest) continue;
            int cutOff = before.LargestSize - Pieces(roadA: a, roadB: b).LargestSize;
            if (cutOff > 0) results.Add((a, b, cutOff));
        }
        return Locations.MergeSort(results, (x, y) => y.CutOff.CompareTo(x.CutOff));
    }

    // Lists the junctions that lose their connection to the rest of the map when junction x closes.
    public List<int> CutOffBy(int x)
    {
        var lost = new List<int>();
        if (_g.NodeCount < 3 || !_g.IsValid(x)) return lost;

        var before = Pieces();
        var after = Pieces(closedNode: x);
        for (int i = 0; i < _g.NodeCount; i++)
            if (i != x && before.Piece[i] == before.Largest && after.Piece[i] != after.Largest)
                lost.Add(i);
        return lost;
    }

    // Lists every road once, ignoring direction (a two-way road is stored as two edges).
    public List<(int A, int B)> Roads()
    {
        var seen = new HashSet<(int, int)>();
        var list = new List<(int A, int B)>();
        foreach (Edge e in _g.AllEdges())
        {
            var key = (Math.Min(e.From, e.To), Math.Max(e.From, e.To));
            if (seen.Add(key)) list.Add(key);
        }
        return list;
    }

    // Splits the map into separate pieces (using BFS), with one junction or one road closed.
    // Returns which piece each junction is in, and which piece is the biggest.
    private (int[] Piece, int Largest, int LargestSize) Pieces(int closedNode = -1, int roadA = -1, int roadB = -1)
    {
        int n = _g.NodeCount;
        var piece = new int[n];
        Array.Fill(piece, -1);
        var queue = new Queue<int>();
        int count = 0, largest = -1, largestSize = 0;

        for (int s = 0; s < n; s++)
        {
            if (s == closedNode || piece[s] != -1) continue;
            int size = 0;
            piece[s] = count;
            queue.Enqueue(s);

            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                size++;
                foreach (int v in _undirected[u])
                {
                    if (v == closedNode || piece[v] != -1) continue;
                    if ((u == roadA && v == roadB) || (u == roadB && v == roadA)) continue;   // closed road
                    piece[v] = count;
                    queue.Enqueue(v);
                }
            }

            if (size > largestSize)
            {
                largestSize = size;
                largest = count;
            }
            count++;
        }
        return (piece, largest, largestSize);
    }
}
