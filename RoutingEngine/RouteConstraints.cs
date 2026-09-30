namespace RoutingEngine;

/// <summary>
/// Rules a route must obey: closed junctions, closed roads, forbidden road types.
///
/// Stored in HASH SETS, so each check is O(1) on average. The routing
/// algorithms call IsEdgeAllowed once per edge they look at, so the
/// constraints add only O(1) per edge and do not change their Big-O.
///
/// Alternative considered: deleting blocked edges from the graph before each
/// query. Rejected: it costs O(E) per query, and the graph must then be
/// rebuilt afterwards, so concurrent queries with different rules would clash.
/// </summary>
public class RouteConstraints
{
    public HashSet<int> BlockedNodes { get; } = new();
    public HashSet<(int From, int To)> BlockedEdges { get; } = new();
    public HashSet<string> AvoidRoadTypes { get; } = new();

    /// <summary>Closes a road in BOTH directions.</summary>
    public void BlockRoad(int a, int b)
    {
        BlockedEdges.Add((a, b));
        BlockedEdges.Add((b, a));
    }

    /// <summary>O(1) average: four hash-set lookups.</summary>
    public bool IsEdgeAllowed(Edge e) =>
        !BlockedNodes.Contains(e.From) &&
        !BlockedNodes.Contains(e.To) &&
        !BlockedEdges.Contains((e.From, e.To)) &&
        !AvoidRoadTypes.Contains(e.RoadType);

    public string Describe()
    {
        var parts = new List<string>();
        if (BlockedNodes.Count > 0)
            parts.Add($"{BlockedNodes.Count} junction(s) closed");
        if (BlockedEdges.Count > 0)
            parts.Add($"{BlockedEdges.Count} road direction(s) closed");
        if (AvoidRoadTypes.Count > 0)
            parts.Add("avoiding " + string.Join(", ", AvoidRoadTypes));
        return parts.Count == 0 ? "no constraints" : string.Join("; ", parts);
    }
}
