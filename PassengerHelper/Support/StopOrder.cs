namespace PassengerHelper.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using PassengerHelper.Plugin;

public sealed class StopOrderResult
{
    public List<PassengerStop> Mainline { get; set; } = new();
    public List<PassengerStop> All { get; set; } = new();
    public string Warning { get; set; } = "";
}
public static class StopOrder
{
    // Anchors that DEFINE the mainline
    private const string SylvaId = StationIds.Sylva;
    private const string AndrewsId = StationIds.Andrews;

    // Known base-game branch
    private const string AlarkaJunctionId = StationIds.AlarkaJct;

    private static readonly HashSet<string> AlarkaBranchIds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            StationIds.Cochran,
            StationIds.Alarka
        };

    private static readonly string[] CanonicalBaseOrder = new string[]
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };
    private static readonly Dictionary<string, int> CanonicalIndex = BuildCanonicalIndex();

    private static Dictionary<string, int> BuildCanonicalIndex()
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < CanonicalBaseOrder.Length; i++)
            dict[CanonicalBaseOrder[i]] = i;
        return dict;
    }

    /// <summary>
    /// - Base mainline is the path sylva -> andrews
    /// - Modded stations may extend the mainline beyond either endpoint
    /// - At alarkajct, the Alarka branch is traversed first
    /// </summary>
    public static bool TryComputeOrderedStopsAnchored(out List<PassengerStop> orderedMainline, out List<PassengerStop> orderedAll, out string warning)
    {
        orderedMainline = new List<PassengerStop>();
        orderedAll = new List<PassengerStop>();
        warning = "";

        var allEnumerablePS = PassengerStop.FindAll();
        if (allEnumerablePS == null)
        {
            warning = "No passenger stops found.";
            return false;
        }

        var allPS = new List<PassengerStop>();
        foreach (var s in allEnumerablePS)
        {
            if (s != null)
            {
                allPS.Add(s);
            }
        }

        if (allPS.Count == 0)
        {
            warning = "No passenger stops found.";
            return false;
        }

        var byId = new Dictionary<string, PassengerStop>(StringComparer.Ordinal);
        foreach (var ps in allPS)
        {
            if (string.IsNullOrEmpty(ps.identifier)) continue;
            byId[ps.identifier] = ps;
        }

        if (!byId.TryGetValue(SylvaId, out var sylva) ||
            !byId.TryGetValue(AndrewsId, out var andrews))
        {
            Loader.Log("[StopOrder::TryComputeOrderedStopsAnchored]Could not find sylva/andrews anchors. Falling back to canonical base-game ordering.");
            // Fallback: just return all stops in a stable-ish order (by canonical base-game ordering)
            warning = "Could not find sylva/andrews anchors. Falling back to canonical base-game ordering.";
            orderedMainline = SortSupportedCanonicalOrder(byId);
            orderedAll = SortByCanonicalOrderFirst(byId);
            return true;
        }

        // If junction missing, just skip the special detour; still compute spine
        byId.TryGetValue(AlarkaJunctionId, out var alarkaJct);
        Dictionary<string, HashSet<string>> adj = BuildUndirectedAdjacency(byId);

        List<PassengerStop> spine;
        if (!TryShortestPath(sylva, andrews, byId, adj, out spine))
        {
            Loader.Log("[StopOrder::TryComputeOrderedStopsAnchored]Could not find a path from sylva to andrews. Falling back to canonical base-game ordering.");
            warning = "Could not find a path from sylva to andrews. Falling back to canonical base-game ordering.";
            orderedMainline = SortSupportedCanonicalOrder(byId);
            orderedAll = SortByCanonicalOrderFirst(byId);
            return true;
        }

        // Custom mainline extension support.
        // Keep the canonical Sylva -> Andrews spine, but allow modded stations
        // to extend beyond either endpoint.
        spine = ExtendSpineFromEndpoints(spine, byId, adj);

        // Normal anchored build (no throws)
        orderedMainline = BuildOrderedFromSpine(spine, alarkaJct, byId, adj);
        orderedAll = BuildOrderedFromSpine(spine, alarkaJct, byId, adj, true);
        return true;
    }


    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    /// <summary>
    /// Extends the canonical Sylva -> Andrews spine outward from either endpoint.
    ///
    /// This supports modded passenger stations that extend the mainline beyond
    /// Sylva or Andrews without treating arbitrary mid-route branches as mainline.
    /// </summary>
    private static List<PassengerStop> ExtendSpineFromEndpoints(
        List<PassengerStop> spine,
        Dictionary<string, PassengerStop> byId,
        Dictionary<string, HashSet<string>> adj)
    {
        if (spine == null || spine.Count == 0)
        {
            return spine ?? new List<PassengerStop>();
        }

        var spineIds = new HashSet<string>(
            spine
                .Where(s => s != null && !string.IsNullOrEmpty(s.identifier))
                .Select(s => s.identifier),
            StringComparer.Ordinal);

        PassengerStop eastEnd = spine[0];
        PassengerStop westEnd = spine[spine.Count - 1];

        List<PassengerStop> eastExtension = BuildEndpointExtension(eastEnd, spineIds, byId, adj);
        List<PassengerStop> westExtension = BuildEndpointExtension(westEnd, spineIds, byId, adj);

        var result = new List<PassengerStop>();

        // eastExtension is returned endpoint-outward:
        // Sylva, Beta, Addie, ...
        // For final order, we want:
        // ..., Addie, Beta, Sylva
        eastExtension.Reverse();

        foreach (PassengerStop stop in eastExtension)
        {
            AddIfNotAlreadyPresent(result, stop);
        }

        foreach (PassengerStop stop in spine)
        {
            AddIfNotAlreadyPresent(result, stop);
        }

        foreach (PassengerStop stop in westExtension)
        {
            AddIfNotAlreadyPresent(result, stop);
        }

        Loader.LogVerbose(
            $"[StopOrder] Extended mainline spine: {string.Join(", ", result.Select(s => s.identifier))}"
        );

        return result;
    }

    /// <summary>
    /// Builds a linear extension starting at one endpoint of the existing spine.
    ///
    /// The returned list excludes the endpoint itself. For example, if called at Sylva
    /// and the graph continues Sylva -> Beta -> Addie, this returns:
    /// Beta, Addie, ...
    /// </summary>
    private static List<PassengerStop> BuildEndpointExtension(
        PassengerStop endpoint,
        HashSet<string> existingSpineIds,
        Dictionary<string, PassengerStop> byId,
        Dictionary<string, HashSet<string>> adj)
    {
        var extension = new List<PassengerStop>();

        if (endpoint == null || string.IsNullOrEmpty(endpoint.identifier))
        {
            return extension;
        }

        string endpointId = endpoint.identifier;

        if (!adj.TryGetValue(endpointId, out var endpointNeighbors))
        {
            return extension;
        }

        // Only consider neighbors outside the existing Sylva -> Andrews spine.
        var candidates = endpointNeighbors
            .Where(id => !existingSpineIds.Contains(id))
            .Where(id => byId.ContainsKey(id))
            .ToList();

        if (candidates.Count == 0)
        {
            return extension;
        }

        if (candidates.Count > 1)
        {
            Loader.Log(
                $"[StopOrder] Endpoint {endpointId} has multiple possible mainline extensions: {string.Join(", ", candidates)}. " +
                "Using the longest deterministic extension."
            );
        }

        List<string> bestPath = null;

        foreach (string candidate in candidates)
        {
            List<string> path = FindLongestEndpointExtensionPath(endpointId, candidate, existingSpineIds, byId, adj);

            if (bestPath == null ||
                path.Count > bestPath.Count ||
                (path.Count == bestPath.Count && IsLexicographicallyEarlier(path, bestPath)))
            {
                bestPath = path;
            }
        }

        if (bestPath == null || bestPath.Count == 0)
        {
            return extension;
        }

        foreach (string id in bestPath)
        {
            if (byId.TryGetValue(id, out var stop) && stop != null)
            {
                extension.Add(stop);
            }
        }

        Loader.LogVerbose($"[StopOrder] Endpoint extension from {endpointId}: {string.Join(", ", bestPath ?? new List<string>())}");

        return extension;
    }

    /// <summary>
    /// Walks outward from a spine endpoint through a mostly-linear custom extension.
    ///
    /// This intentionally stops when the extension branches, loops back into the
    /// existing spine, or hits a dead end.
    /// </summary>

    private static List<string> FindLongestEndpointExtensionPath(
    string spineEndpointId,
    string firstExtensionId,
    HashSet<string> existingSpineIds,
    Dictionary<string, PassengerStop> byId,
    Dictionary<string, HashSet<string>> adj)
    {
        var bestPath = new List<string>();

        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            spineEndpointId
        };

        void Dfs(string currentId, List<string> path)
        {
            if (string.IsNullOrEmpty(currentId))
            {
                return;
            }

            if (existingSpineIds.Contains(currentId))
            {
                return;
            }

            if (!byId.ContainsKey(currentId))
            {
                return;
            }

            if (!visited.Add(currentId))
            {
                return;
            }

            path.Add(currentId);

            if (IsBetterExtensionPath(path, bestPath))
            {
                bestPath = new List<string>(path);
            }

            if (adj.TryGetValue(currentId, out var neighbors))
            {
                foreach (string nextId in neighbors.OrderBy(id => id, StringComparer.Ordinal))
                {
                    if (visited.Contains(nextId))
                    {
                        continue;
                    }

                    if (existingSpineIds.Contains(nextId))
                    {
                        continue;
                    }

                    if (!byId.ContainsKey(nextId))
                    {
                        continue;
                    }

                    Dfs(nextId, path);
                }
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(currentId);
        }

        Dfs(firstExtensionId, new List<string>());

        return bestPath;
    }

    private static bool IsBetterExtensionPath(List<string> candidate, List<string> currentBest)
    {
        if (candidate.Count > currentBest.Count)
        {
            return true;
        }

        if (candidate.Count < currentBest.Count)
        {
            return false;
        }

        return IsLexicographicallyEarlier(candidate, currentBest);
    }

    private static void AddIfNotAlreadyPresent(List<PassengerStop> list, PassengerStop stop)
    {
        if (stop == null || string.IsNullOrEmpty(stop.identifier))
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            PassengerStop existing = list[i];

            if (existing != null &&
                StringComparer.Ordinal.Equals(existing.identifier, stop.identifier))
            {
                return;
            }
        }

        list.Add(stop);
    }

    private static bool IsLexicographicallyEarlier(List<string> left, List<string> right)
    {
        int count = Math.Min(left.Count, right.Count);

        for (int i = 0; i < count; i++)
        {
            int compare = string.CompareOrdinal(left[i], right[i]);

            if (compare < 0)
            {
                return true;
            }

            if (compare > 0)
            {
                return false;
            }
        }

        return left.Count < right.Count;
    }

    private static List<PassengerStop> BuildOrderedFromSpine(
        List<PassengerStop> spine,
        PassengerStop? alarkaJct,
        Dictionary<string, PassengerStop> byId,
        Dictionary<string, HashSet<string>> adj,
        bool includeOtherBranches = false)
    {
        var spineSet = new HashSet<PassengerStop>(RefEq<PassengerStop>.Instance);
        for (int i = 0; i < spine.Count; i++)
            spineSet.Add(spine[i]);

        var visited = new HashSet<PassengerStop>(RefEq<PassengerStop>.Instance);
        var ordered = new List<PassengerStop>();

        for (int i = 0; i < spine.Count; i++)
        {
            var cur = spine[i];

            if (visited.Add(cur))
                ordered.Add(cur);

            // Special-case: Alarka branch detour
            if (alarkaJct != null && ReferenceEquals(cur, alarkaJct))
                TraverseNamedBranch(cur, byId, adj, spineSet, visited, ordered, AlarkaBranchIds);

            // Generic: any other non-spine branch components
            if (includeOtherBranches)
                TraverseOtherBranches(cur, byId, adj, spineSet, visited, ordered);
        }

        return ordered;
    }

    private static void TraverseNamedBranch(
        PassengerStop junction,
        Dictionary<string, PassengerStop> byId,
        Dictionary<string, HashSet<string>> adj,
        HashSet<PassengerStop> spineSet,
        HashSet<PassengerStop> visited,
        List<PassengerStop> ordered,
        HashSet<string> branchIds)
    {
        var jId = junction.identifier;
        if (string.IsNullOrEmpty(jId)) return;

        if (!adj.TryGetValue(jId, out var nbrIds)) return;

        foreach (var nbId in nbrIds)
        {
            if (!byId.TryGetValue(nbId, out var nb) || nb == null) continue;
            if (spineSet.Contains(nb)) continue;

            if (branchIds.Contains(nbId))
                TraverseBranchComponent(nb, junction, byId, adj, spineSet, visited, ordered);
        }
    }

    private static void TraverseOtherBranches(
    PassengerStop spineNode,
    Dictionary<string, PassengerStop> byId,
    Dictionary<string, HashSet<string>> adj,
    HashSet<PassengerStop> spineSet,
    HashSet<PassengerStop> visited,
    List<PassengerStop> ordered)
    {
        var spineId = spineNode.identifier;
        if (string.IsNullOrEmpty(spineId)) return;

        if (!adj.TryGetValue(spineId, out var nbrIds)) return;

        foreach (var nbId in nbrIds)
        {
            if (!byId.TryGetValue(nbId, out var nb) || nb == null) continue;
            if (spineSet.Contains(nb)) continue;

            TraverseBranchComponent(nb, spineNode, byId, adj, spineSet, visited, ordered);
        }
    }

    /// <summary>
    /// DFS a branch component without spilling back onto the spine.
    /// </summary>
    private static void TraverseBranchComponent(
    PassengerStop start,
    PassengerStop? parent,
    Dictionary<string, PassengerStop> byId,
    Dictionary<string, HashSet<string>> adj,
    HashSet<PassengerStop> spineSet,
    HashSet<PassengerStop> visited,
    List<PassengerStop> ordered)
    {
        var stack = new Stack<(PassengerStop cur, PassengerStop? parent)>();
        stack.Push((start, parent));

        while (stack.Count > 0)
        {
            var (cur, par) = stack.Pop();
            if (!visited.Add(cur)) continue;

            ordered.Add(cur);

            var curId = cur.identifier;
            if (string.IsNullOrEmpty(curId)) continue;

            if (!adj.TryGetValue(curId, out var nbrIds)) continue;

            foreach (var nbId in nbrIds)
            {
                if (!byId.TryGetValue(nbId, out var nb) || nb == null) continue;
                if (ReferenceEquals(nb, par)) continue;
                if (spineSet.Contains(nb)) continue;

                stack.Push((nb, cur));
            }
        }
    }

    private static List<PassengerStop> SortByCanonicalOrderFirst(
    Dictionary<string, PassengerStop> byId)
    {
        var list = SortSupportedCanonicalOrder(byId);

        List<PassengerStop> extras = new List<PassengerStop>();

        foreach (KeyValuePair<string, PassengerStop> kvp in byId)
        {
            if (!CanonicalIndex.ContainsKey(kvp.Key) && kvp.Value != null)
            {
                extras.Add(kvp.Value);
            }
        }

        extras.Sort((a, b) => string.CompareOrdinal(a.identifier, b.identifier));
        list.AddRange(extras);

        return list;
    }

    private static List<PassengerStop> SortSupportedCanonicalOrder(
    Dictionary<string, PassengerStop> byId)
    {
        List<PassengerStop> list = new List<PassengerStop>(CanonicalBaseOrder.Length);

        foreach (string id in CanonicalIndex.Keys)
        {
            if (byId.TryGetValue(id, out var ps) && ps != null)
            {
                list.Add(ps);
            }
        }

        return list;
    }

    /// <summary>
    /// Unweighted shortest path (BFS).
    /// </summary>
    private static bool TryShortestPath(PassengerStop start, PassengerStop goal, Dictionary<string, PassengerStop> byId, Dictionary<string, HashSet<string>> adj, out List<PassengerStop> path)
    {
        path = new List<PassengerStop>();

        string startId = start.identifier;
        string goalId = goal.identifier;

        if (string.IsNullOrEmpty(startId) || string.IsNullOrEmpty(goalId))
            return false;

        var q = new Queue<string>();
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal);

        q.Enqueue(startId);
        parent[startId] = null;

        while (q.Count > 0)
        {
            var curId = q.Dequeue();
            if (StringComparer.Ordinal.Equals(curId, goalId))
                break;

            if (!adj.TryGetValue(curId, out var nbrIds)) continue;

            foreach (var nbId in nbrIds)
            {
                if (parent.ContainsKey(nbId)) continue;
                parent[nbId] = curId;
                q.Enqueue(nbId);
            }
        }

        if (!parent.ContainsKey(goalId))
            return false;

        var ids = new List<string>();

        for (string? cur = goalId; cur != null; cur = parent[cur])
            ids.Add(cur);

        ids.Reverse();

        // map ids -> PassengerStop
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var ps))
                path.Add(ps);
            else
                return false; // should not happen if adj/byId are consistent
        }

        return true;
    }

    private static Dictionary<string, HashSet<string>> BuildUndirectedAdjacency(Dictionary<string, PassengerStop> byId)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // init
        foreach (var id in byId.Keys)
            adj[id] = new HashSet<string>(StringComparer.Ordinal);

        // add edges as undirected
        foreach (var kvp in byId)
        {
            var aId = kvp.Key;
            var a = kvp.Value;
            var nbrs = a?.neighbors;
            if (nbrs == null) continue;

            for (int i = 0; i < nbrs.Length; i++)
            {
                var b = nbrs[i];
                if (b == null) continue;
                var bId = b.identifier;
                if (string.IsNullOrEmpty(bId)) continue;

                // only connect to stops we actually know about
                if (!byId.ContainsKey(bId)) continue;

                adj[aId].Add(bId);
                adj[bId].Add(aId); // <- the critical “make it symmetric”
            }
        }

        // Iterate over a snapshot because we'll mutate adj.
        foreach (var kvp in byId.ToList())
        {
            var xId = kvp.Key;

            if (!adj.TryGetValue(xId, out var xNbrs))
                continue;

            // Inline candidate: exactly two neighbors.
            if (xNbrs.Count != 2)
                continue;

            var arr = xNbrs.ToArray();
            var aId = arr[0];
            var bId = arr[1];

            // If A and B aren't connected, nothing to split.
            if (!adj.TryGetValue(aId, out var aNbrs) || !aNbrs.Contains(bId))
                continue;
            if (!adj.TryGetValue(bId, out var bNbrs) || !bNbrs.Contains(aId))
                continue;

            // Remove the shortcut edge A<->B so paths must go A->X->B
            aNbrs.Remove(bId);
            bNbrs.Remove(aId);

            // (Optional) log for debug
            Loader.Log($"[StopOrder] Split shortcut edge {aId}<->{bId} via inline stop {xId}");
        }

        return adj;
    }

    /// <summary>
    /// Reference-equality comparer (PassengerStop instances are identity-based).
    /// </summary>
    private sealed class RefEq<T> : IEqualityComparer<T> where T : class
    {
        public static readonly RefEq<T> Instance = new();
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
