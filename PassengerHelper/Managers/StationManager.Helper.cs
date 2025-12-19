using System;
using System.Collections.Generic;
using Network;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

//helper
public partial class StationManager
{
    private Dictionary<string, int> BuildOrderIndex()
    {
        var orderedStations = getOrderedStations();
        Loader.Log($"StationManager: BuildOrderIndex saw {orderedStations.Count} stations");

        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < orderedStations.Count; i++)
            dict[orderedStations[i]] = i;
        return dict;
    }

    private int GetOrder(Dictionary<string, int> idx, string id) => idx.TryGetValue(id, out var v) ? v : int.MaxValue;

    private void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }

    private static string Dump(IEnumerable<string> ids) => ids == null ? "<null>" : string.Join(", ", ids);
}
