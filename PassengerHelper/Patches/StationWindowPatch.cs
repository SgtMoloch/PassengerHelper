namespace PassengerHelperPlugin.Patches;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core;
using HarmonyLib;
using Model.Ops;
using GameObjects;
using RollingStock;
using Serilog;
using UI.Builder;
using UI.StationWindow;
using Game;

[HarmonyPatch]
public class StationWindowPatch
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(StationWindowPatch));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(StationWindow), "BuildPassengerTab")]
    private static bool BuildPassengerTab(UIPanelBuilder builder, PassengerStop passengerStop)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return true;
        }

        // begin existing code
        builder.VScrollView(delegate (UIPanelBuilder builder)
        {
            builder.RebuildOnInterval(1f);

            PassengerHelperPassengerStop passengerHelperPassengerStop = passengerStop.GetComponentInChildren<PassengerHelperPassengerStop>();

            IReadOnlyDictionary<string, int> _transfersWaiting = passengerHelperPassengerStop._stationTransfersWaiting;

            IReadOnlyDictionary<string, PassengerStop.WaitingInfo> waiting = passengerStop.Waiting;
            IEnumerable<KeyValuePair<string, PassengerStop.WaitingInfo>> enumerable = waiting.Where((KeyValuePair<string, PassengerStop.WaitingInfo> pair) => pair.Value.Total > 0);
            bool flag = waiting.Count == 0;
            int number = waiting.Sum((KeyValuePair<string, PassengerStop.WaitingInfo> kv) => kv.Value.Total);
            string text = (flag ? "no passengers" : number.Pluralize("passenger"));

            // transfer passengers
            IReadOnlyDictionary<string, int> transfersWaiting = _transfersWaiting;
            logger.Information("transfers waiting: {0}", transfersWaiting);
            IEnumerable<KeyValuePair<string, int>> transfersEnumerable = transfersWaiting.Where((KeyValuePair<string, int> pair) => pair.Value > 0);
            bool transfersZero = transfersWaiting.Count == 0;
            int transferNumber = transfersWaiting.Sum((KeyValuePair<string, int> kv) => kv.Value);
            string transferText = (transfersZero ? "no transfer passengers" : transferNumber.Pluralize("transfer passenger"));

            builder.AddLabel(passengerStop.DisplayName + " has " + text + " waiting.");

            if (!flag)
            {
                builder.Spacer(8f);
                builder.HStack(delegate (UIPanelBuilder builder)
                            {
                                builder.AddLabel("<b>Count</b>").Width(100f);
                                builder.AddLabel("<b>Destination</b>");
                            });
                foreach (KeyValuePair<string, PassengerStop.WaitingInfo> item in enumerable)
                {
                    item.Deconstruct(out var key, out var value);
                    string identifier = key;
                    int numWaiting = value.Total;
                    string destName = PassengerStop.NameForIdentifier(identifier);
                    builder.HStack(delegate (UIPanelBuilder builder)
                    {
                        builder.AddLabel($"{numWaiting}").Width(100f);
                        builder.AddLabel(destName);
                    });
                }
            }

            builder.AddLabel(passengerStop.DisplayName + " has " + transferText + " waiting.");

            if (!transfersZero)
            {

                builder.Spacer(8f);
                builder.HStack(delegate (UIPanelBuilder builder)
                {
                    builder.AddLabel("<b>Count</b>").Width(100f);
                    builder.AddLabel("<b>Destination</b>");
                });
                foreach (KeyValuePair<string, int> item in transfersEnumerable)
                {
                    item.Deconstruct(out var key, out var value);
                    string identifier = key;
                    int numWaiting = value;
                    string destName = PassengerStop.NameForIdentifier(identifier);
                    builder.HStack(delegate (UIPanelBuilder builder)
                    {
                        builder.AddLabel($"{numWaiting}").Width(100f);
                        builder.AddLabel(destName);
                    });
                }
            }
        });

        if (plugin.TestMode)
        {
            builder.AddButton("Spawn Passengers", () =>
            {
                Dictionary<string, int> _waiting = typeof(PassengerStop).GetField("_waiting", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(passengerStop) as Dictionary<string, int>;

                foreach (string stationId in plugin.orderedStations)
                {
                    if (stationId == passengerStop.identifier)
                    {
                        continue;
                    }
                    if (_waiting.TryGetValue(stationId, out var stat))
                    {
                        _waiting[stationId] = UnityEngine.Random.Range(1, 100);
                    }
                    else
                    {
                        _waiting.Add(stationId, UnityEngine.Random.Range(1, 100));
                    }
                }
            });
            builder.AddButton("Spawn Transfer Passengers", () =>
            {
                PassengerHelperPassengerStop passengerHelperPassengerStop = passengerStop.GetComponentInChildren<PassengerHelperPassengerStop>();

                List<PassengerGroup> _transferWaiting = passengerHelperPassengerStop._stationTransferGroups;

                foreach (string stationId in plugin.orderedStations)
                {
                    if (stationId == passengerStop.identifier)
                    {
                        continue;
                    }

                    passengerHelperPassengerStop._stationTransferGroups.Add(new PassengerGroup(passengerStop.identifier, stationId, UnityEngine.Random.Range(10, 100), TimeWeather.Now));
                }
            });
        }

        return false;
    }
}