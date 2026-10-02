namespace RoutingEngine;

// Turns street names into places you can search for. Each street is one location.
public class Locations
{
    private readonly Dictionary<string, int> _nodeOf = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Names { get; }   // all street names, A to Z

    // Finds every street, picks one junction to stand for it, and sorts the names.
    public Locations(Graph g)
    {
        var streetNodes = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var hasRoadIn = new bool[g.NodeCount];

        foreach (Edge e in g.AllEdges())
        {
            hasRoadIn[e.To] = true;
            if (e.Name.Length == 0) continue;
            if (!streetNodes.ContainsKey(e.Name)) streetNodes[e.Name] = new HashSet<int>();
            streetNodes[e.Name].Add(e.From);
            streetNodes[e.Name].Add(e.To);
        }

        // The street's point = the junction closest to its middle that you can drive into AND out of.
        foreach (var (name, nodes) in streetNodes)
        {
            double lat = nodes.Average(id => g.GetNode(id).Lat);
            double lon = nodes.Average(id => g.GetNode(id).Lon);
            var usable = nodes.Where(id => hasRoadIn[id] && g.EdgesFrom(id).Count > 0).ToList();
            if (usable.Count == 0) usable = nodes.ToList();
            _nodeOf[name] = usable.MinBy(id => Sq(g.GetNode(id).Lat - lat) + Sq(g.GetNode(id).Lon - lon));
        }

        Names = MergeSort(_nodeOf.Keys.ToList(), (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
    }

    // Gets the junction that stands for a street.
    public int NodeOf(string name) => _nodeOf[name];

    // Gets a street's number in the A-Z list (starting at 1).
    public int NumberOf(string name) => Names.IndexOf(name) + 1;

    // Finds streets matching what the user typed. Capital letters and punctuation don't matter.
    public List<string> Search(string query)
    {
        string q = Clean(query);
        var hits = new List<string>();
        if (q.Length == 0) return hits;

        string[] words = q.Split(' ');
        foreach (string name in Names)
        {
            string key = Clean(name);
            if (key == q) return new List<string> { name };      // exact match wins
            if (words.All(w => key.Contains(w))) hits.Add(name);
        }
        return hits;
    }

    // Makes text easy to compare: "Dean's Road" -> "deans road".
    private static string Clean(string s)
    {
        char[] chars = s.ToLowerInvariant()
                        .Where(ch => ch != '\'' && ch != '\u2019')
                        .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
                        .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // MERGE SORT: splits the list in half, sorts each half, then merges them back in order.
    public static List<T> MergeSort<T>(List<T> list, Comparison<T> compare)
    {
        if (list.Count < 2) return list;
        int mid = list.Count / 2;
        List<T> left = MergeSort(list.GetRange(0, mid), compare);
        List<T> right = MergeSort(list.GetRange(mid, list.Count - mid), compare);

        var merged = new List<T>(list.Count);
        int i = 0, j = 0;
        while (i < left.Count && j < right.Count)
            merged.Add(compare(right[j], left[i]) < 0 ? right[j++] : left[i++]);
        merged.AddRange(left.Skip(i));
        merged.AddRange(right.Skip(j));
        return merged;
    }

    // Squares a number.
    private static double Sq(double x) => x * x;
}
