namespace PassengerHelper.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;

public sealed class StopOrderResult
{
    public List<PassengerStop> Mainline { get; set; } = new();
    public List<PassengerStop> All { get; set; } = new();
    public string Warning { get; set; } = "";
}
public static class StopOrder
{
    // Anchors that DEFINE the mainline
    private const string EastAnchorId = StationIds.Sylva;
    private const string WestAnchorId = StationIds.Andrews;

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
    /// Computes a deterministic station ordering.
    /// - Mainline is ALWAYS the path sylva -> andrews
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

        if (!byId.TryGetValue(EastAnchorId, out var east) ||
            !byId.TryGetValue(WestAnchorId, out var west))
        {
            // Fallback: just return all stops in a stable-ish order (by canonical base-game ordering)
            warning = "Could not find sylva/andrews anchors. Falling back to canonical base-game ordering.";
            orderedMainline = SortSupportedCanonicalOrder(byId);
            orderedAll = SortByCanonicalOrderFirst(byId);
            return true;
        }

        // If junction missing, just skip the special detour; still compute spine
        byId.TryGetValue(AlarkaJunctionId, out var alarkaJct);

        List<PassengerStop> spine;
        if (!TryShortestPath(east, west, out spine))
        {
            warning = "Could not find a path from sylva to andrews. Falling back to canonical base-game ordering.";
            orderedMainline = SortSupportedCanonicalOrder(byId);
            orderedAll = SortByCanonicalOrderFirst(byId);
            return true;
        }

        // Normal anchored build (no throws)
        orderedMainline = BuildOrderedFromSpine(spine, alarkaJct);
        orderedAll = BuildOrderedFromSpine(spine, alarkaJct, true);
        return true;
    }


    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static List<PassengerStop> BuildOrderedFromSpine(
        List<PassengerStop> spine,
        PassengerStop? alarkaJct, bool includeOtherBranches = false)
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
                TraverseNamedBranch(cur, spineSet, visited, ordered, AlarkaBranchIds);

            // Generic: any other non-spine branch components
            if (includeOtherBranches)
                TraverseOtherBranches(cur, spineSet, visited, ordered);
        }

        return ordered;
    }

    private static void TraverseNamedBranch(
        PassengerStop junction,
        HashSet<PassengerStop> spineSet,
        HashSet<PassengerStop> visited,
        List<PassengerStop> ordered,
        HashSet<string> branchIds)
    {
        var nbrs = junction.neighbors;
        if (nbrs == null) return;

        for (int i = 0; i < nbrs.Length; i++)
        {
            var nb = nbrs[i];
            if (nb == null) continue;
            if (spineSet.Contains(nb)) continue;

            if (branchIds.Contains(nb.identifier))
                TraverseBranchComponent(nb, junction, spineSet, visited, ordered);
        }
    }

    private static void TraverseOtherBranches(
        PassengerStop spineNode,
        HashSet<PassengerStop> spineSet,
        HashSet<PassengerStop> visited,
        List<PassengerStop> ordered)
    {
        var nbrs = spineNode.neighbors;
        if (nbrs == null) return;

        for (int i = 0; i < nbrs.Length; i++)
        {
            var nb = nbrs[i];
            if (nb == null) continue;
            if (spineSet.Contains(nb)) continue;

            TraverseBranchComponent(nb, spineNode, spineSet, visited, ordered);
        }
    }

    /// <summary>
    /// DFS a branch component without spilling back onto the spine.
    /// </summary>
    private static void TraverseBranchComponent(
        PassengerStop start,
        PassengerStop? parent,
        HashSet<PassengerStop> spineSet,
        HashSet<PassengerStop> visited,
        List<PassengerStop> ordered)
    {
        var stack = new Stack<(PassengerStop cur, PassengerStop? parent)>();
        stack.Push((start, parent));

        while (stack.Count > 0)
        {
            var (cur, par) = stack.Pop();
            if (!visited.Add(cur))
                continue;

            ordered.Add(cur);

            var nbrs = cur.neighbors;
            if (nbrs == null) continue;

            for (int i = nbrs.Length - 1; i >= 0; i--)
            {
                var nb = nbrs[i];
                if (nb == null) continue;
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
    private static bool TryShortestPath(PassengerStop start, PassengerStop goal, out List<PassengerStop> path)
    {
        path = new List<PassengerStop>();

        var q = new Queue<PassengerStop>();
        var parent = new Dictionary<PassengerStop, PassengerStop?>(RefEq<PassengerStop>.Instance);

        q.Enqueue(start);
        parent[start] = null;

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (ReferenceEquals(cur, goal))
                break;

            var nbrs = cur.neighbors;
            if (nbrs == null) continue;

            for (int i = 0; i < nbrs.Length; i++)
            {
                var nb = nbrs[i];
                if (nb == null) continue;
                if (parent.ContainsKey(nb)) continue;

                parent[nb] = cur;
                q.Enqueue(nb);
            }
        }

        if (!parent.ContainsKey(goal))
            return false;

        for (PassengerStop? cur = goal; cur != null; cur = parent[cur])
            path.Add(cur);

        path.Reverse();
        return true;
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
