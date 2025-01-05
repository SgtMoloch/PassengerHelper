namespace PassengerHelperPlugin.Patches;

using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using HarmonyLib;
using Model;
using Core;
using RollingStock;
using Serilog;
using GameObjects;
using Model.Ops;

[HarmonyPatch]
public class StationAgentPatch
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(StationAgentPatch));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StationAgent), "PassengerSummary")]
    private static void ShouldWorkCar(ref string __result, StationAgent __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }
        PassengerStop _currentStop = typeof(StationAgent).GetField("passengerStop", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as PassengerStop;

        PassengerHelperPassengerStop passengerHelperPassengerStop = _currentStop.GetComponentInChildren<PassengerHelperPassengerStop>();

        __result += "\n" + passengerHelperPassengerStop._stationTransfersWaiting.Sum(g => g.Value).Pluralize("transfer passenger") + " waiting";
    }
}