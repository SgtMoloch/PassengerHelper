namespace PassengerHelper.Support;

using System.Collections.Generic;
using Model.Ops;

public sealed class StopOrderCache
{
    public IReadOnlyList<PassengerStop> OrderedAll { get; private set; } = new List<PassengerStop>();
    public IReadOnlyDictionary<PassengerStop, int> IndexAll { get; private set; }
        = new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);

    // Optional: indices only for mainline (spine). If a stop isn't on spine, it's not present.
    public IReadOnlyDictionary<PassengerStop, int> SpineIndex { get; private set; }
        = new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);

    // Store warning from ordering (for UI/logging)
    public string Warning { get; private set; } = "";

    /// <summary>
    /// Rebuild from StopOrder.TryComputeOrderedStopsAnchored(). Call when topology changes.
    /// </summary>
    public bool Rebuild()
    {
        if (!StopOrder.TryComputeOrderedStopsAnchored(out var ordered, out var warn))
        {
            OrderedAll = new List<PassengerStop>();
            IndexAll = new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);
            SpineIndex = new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);
            Warning = string.IsNullOrEmpty(warn) ? "Stop ordering failed." : warn;
            return false;
        }

        OrderedAll = ordered;
        IndexAll = BuildIndexMap(ordered);
        SpineIndex = BuildSpineIndexMap(); // uses anchors; safe even if it ends up empty
        Warning = warn ?? "";
        return true;
    }

    public bool TryGetIndex(PassengerStop stop, out int index)
        => IndexAll.TryGetValue(stop, out index);

    public bool TryGetSpineIndex(PassengerStop stop, out int spineIndex)
        => SpineIndex.TryGetValue(stop, out spineIndex);

    // ------------------------------------------------------------

    private static Dictionary<PassengerStop, int> BuildIndexMap(List<PassengerStop> ordered)
    {
        var dict = new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);
        for (int i = 0; i < ordered.Count; i++)
            dict[ordered[i]] = i;
        return dict;
    }

    /// <summary>
    /// Builds a spine index using the same anchored path concept (sylva->andrews).
    /// If anchors/path can’t be found, returns empty map (direction inference can fall back to manual).
    /// </summary>
    private static Dictionary<PassengerStop, int> BuildSpineIndexMap()
    {
        // We reuse StopOrder’s anchored path logic indirectly:
        // simplest is: recompute the spine path here with a tiny BFS.
        // (This is cheap compared to full ordering, and only on rebuild.)

        const string East = "sylva";
        const string West = "andrews";

        var allEnum = PassengerStop.FindAll();
        if (allEnum == null) return new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);

        var byId = new Dictionary<string, PassengerStop>(System.StringComparer.Ordinal);
        foreach (var s in allEnum)
        {
            if (s == null) continue;
            if (string.IsNullOrEmpty(s.identifier)) continue;
            byId[s.identifier] = s;
        }

        if (!byId.TryGetValue(East, out var east) || !byId.TryGetValue(West, out var west))
            return new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);

        if (!TryShortestPath(east, west, out var spine))
            return new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);

        var dict = new Dictionary<PassengerStop, int>(RefEq<PassengerStop>.Instance);
        for (int i = 0; i < spine.Count; i++)
            dict[spine[i]] = i;

        return dict;
    }

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

    private sealed class RefEq<T> : IEqualityComparer<T> where T : class
    {
        public static readonly RefEq<T> Instance = new();
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
