using System;
using System.Collections.Generic;
using Network;

namespace PassengerHelper.Managers;

//helper
public partial class StationManager
{
    private Dictionary<string, int> BuildOrderIndex()
    {
        var orderedStations = getOrderedStations();
        
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
}
