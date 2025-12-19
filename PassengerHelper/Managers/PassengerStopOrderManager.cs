using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;

namespace PassengerHelper.Managers;

public sealed class PassengerStopOrderManager
{
    private int _topologyFingerprint;
    private List<PassengerStop> _orderedAll = new();
    private List<PassengerStop> _orderedUnlocked = new();

    public IReadOnlyList<PassengerStop> OrderedAll => _orderedAll;
    public IReadOnlyList<PassengerStop> OrderedUnlocked => _orderedUnlocked;

    public List<string> OrderedAllStopIds { get; private set; } = new();

    /// <summary>
    /// Call on game load, and occasionally later. Rebuilds the expensive ordering only
    /// if the station graph changed (mods, scenario reload, etc).
    /// </summary>
    public void EnsureTopologyUpToDate(Func<List<PassengerStop>> rebuildOrdering)
    {
        var all = PassengerStop.FindAll();
        int fp = ComputeTopologyFingerprint(all.ToArray());

        if (fp == _topologyFingerprint && _orderedAll.Count > 0)
            return;

        _topologyFingerprint = fp;

        // Expensive rebuild (provided by you in Piece 2)
        _orderedAll = rebuildOrdering();

        OrderedAllStopIds = _orderedAll
            .Where(s => s != null && !string.IsNullOrEmpty(s.identifier))
            .Select(s => s.identifier)
            .ToList();
    }

    /// <summary>
    /// Call whenever progression/availability changes. Cheap: just filters OrderedAll.
    /// </summary>
    public void RefreshUnlocked(Func<PassengerStop, bool> isUnlocked)
    {
        _orderedUnlocked = FilterUnlocked(_orderedAll, isUnlocked);
    }

    private static List<PassengerStop> FilterUnlocked(
        List<PassengerStop> orderedAll,
        Func<PassengerStop, bool> isUnlocked)
    {
        var result = new List<PassengerStop>(orderedAll.Count);
        for (int i = 0; i < orderedAll.Count; i++)
        {
            var s = orderedAll[i];
            if (s != null && isUnlocked(s))
                result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// Cheap-ish "did the graph change?" fingerprint.
    /// If mods add/remove stations or rewire neighbors, this should usually change.
    /// </summary>
    private static int ComputeTopologyFingerprint(PassengerStop[] stops)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + stops.Length;

            for (int i = 0; i < stops.Length; i++)
            {
                var s = stops[i];
                if (s == null) continue;

                hash = hash * 31 + (s.identifier?.GetHashCode() ?? 0);

                var nbrs = s.neighbors;
                if (nbrs == null) continue;

                // Order-independent neighbor hashing: sum of neighbor id hashes
                int nh = 0;
                for (int n = 0; n < nbrs.Length; n++)
                    nh += (nbrs[n]?.identifier?.GetHashCode() ?? 0);

                hash = hash * 31 + nh;
            }

            return hash;
        }
    }
}