using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Plugin;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;

namespace PassengerHelper.Managers;

public sealed class PassengerStopOrderManager
{
    private int _topologyFingerprint;
    private List<PassengerStop> _orderedAll = new();
    private List<PassengerStop> _orderedMainline = new();
    private List<PassengerStop> _orderedUnlockedAll = new();
    private List<PassengerStop> _orderedUnlockedMainline = new();

    

    public IReadOnlyList<PassengerStop> OrderedAll => _orderedAll;
    public IReadOnlyList<PassengerStop> OrderedMainline => _orderedMainline;
    public IReadOnlyList<PassengerStop> OrderedUnlockedAll => _orderedUnlockedAll;
    public IReadOnlyList<PassengerStop> OrderedUnlockedMainline => _orderedUnlockedMainline;

    public List<string> OrderedAllStopIds { get; private set; } = new();
    public List<string> OrderedMainlineStopIds { get; private set; } = new();


    public PassengerStopOrderManager()
    {
        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }
    
    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        _orderedAll.Clear();
        _orderedMainline.Clear();
        _orderedUnlockedAll.Clear();
        _orderedUnlockedMainline.Clear();
    }

    /// <summary>
    /// Call on game load, and occasionally later. Rebuilds the expensive ordering only
    /// if the station graph changed (mods, scenario reload, etc).
    /// </summary>
    public void EnsureTopologyUpToDate(Func<StopOrderResult> rebuildOrdering)
    {
        var all = PassengerStop.FindAll();
        int fp = ComputeTopologyFingerprint(all.ToArray());

        if (fp == _topologyFingerprint && _orderedAll.Count > 0)
            return;

        _topologyFingerprint = fp;

        // Expensive rebuild (provided by you in Piece 2)
        StopOrderResult result = rebuildOrdering();
        _orderedAll = result.All;
        _orderedMainline = result.Mainline;

        if (!string.IsNullOrEmpty(result.Warning))
            Loader.Log(result.Warning);

        OrderedAllStopIds = _orderedAll
            .Where(s => s != null && !string.IsNullOrEmpty(s.identifier))
            .Select(s => s.identifier)
            .ToList();

        OrderedMainlineStopIds = _orderedMainline
        .Where(s => s != null && !string.IsNullOrEmpty(s.identifier))
        .Select(s => s.identifier)
        .ToList();
    }

    /// <summary>
    /// Call whenever progression/availability changes. Cheap: just filters OrderedAll.
    /// </summary>
    public void RefreshUnlocked(Func<PassengerStop, bool> isUnlocked)
    {
        _orderedUnlockedAll = FilterUnlocked(_orderedAll, isUnlocked);
        _orderedUnlockedMainline = FilterUnlocked(_orderedMainline, isUnlocked);
    }

    private static List<PassengerStop> FilterUnlocked(
        List<PassengerStop> orderedAll,
        Func<PassengerStop, bool> isUnlocked)
    {
        var result = new List<PassengerStop>();
        for (int i = 0; i < orderedAll.Count; i++)
        {
            var s = orderedAll[i];
            if (s != null)
            {
                Loader.Log($"[PassengerStopOrderManager::FilterUnlocked] station: {s.DisplayName} isUnlocked: {!s.ProgressionDisabled} isUnlockedCB: {isUnlocked(s)}");
                if (isUnlocked(s))
                    result.Add(s);
            }

        }
        return result;
    }

    /// <summary>
    /// Cheap-ish "did the graph change?" fingerprint.
    /// If mods add/remove stations or rewire neighbors, this should usually change.
    /// </summary>
    private static int ComputeTopologyFingerprint(PassengerStop[] stops)
    {

        int hash = 17;
        hash = hash * 31 + stops.Length;

        for (int i = 0; i < stops.Length; i++)
        {
            var s = stops[i];
            if (s == null) continue;

            Loader.Log($"[PassengerStopOrderManager::ComputeTopologyFingerprint] station: {s.DisplayName} isUnlocked: {!s.ProgressionDisabled}");
            Loader.Log($"[PassengerStopOrderManager::ComputeTopologyFingerprint] stop neighbors: {string.Join(",", s.neighbors?.Select(n=>n?.identifier) ?? Enumerable.Empty<string>())}");
            hash = hash * 31 + (s.identifier?.GetHashCode() ?? 0);
            hash = hash * 31 + (s.ProgressionDisabled.GetHashCode());

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