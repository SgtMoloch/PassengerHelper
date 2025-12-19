namespace PassengerHelper.Patches;

using System.Collections.Generic;
using System.Linq;
using Game.Progression;
using HarmonyLib;
using Model.Ops;
using PassengerHelper.Managers;
using PassengerHelper.UMM;
using RollingStock;
using Serilog;
using Support;

[HarmonyPatch]
public static class MapFeatureManagerPatches
{
    static ILogger logger = Log.ForContext(typeof(MapFeatureManagerPatches));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapFeatureManager), "HandleFeatureEnablesChanged")]
    private static void HandleFeatureEnablesChanged()
    {
        Loader.LogDebug($"Progressions Changed. Checking Stations");
        PassengerHelper shared = Loader.PassengerHelper;

        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        PassengerStopOrderManager passengerStopOrderManager = shared.passengerStopOrderManager;

        // catch if this runs before
        if (passengerStopOrderManager.OrderedAll.Count == 0)
        {
            passengerStopOrderManager.EnsureTopologyUpToDate(() =>
            {
                if (StopOrder.TryComputeOrderedStopsAnchored(out var ordered, out var warn))
                {
                    if (!string.IsNullOrEmpty(warn))
                        Loader.Log(warn);
                    return ordered;
                }

                Loader.Log("Stop ordering failed; using empty list.");
                return new List<PassengerStop>();
            });
        }

        passengerStopOrderManager.RefreshUnlocked(stop => !stop.ProgressionDisabled);

        // 🔍 TEMP sanity log — after RefreshUnlocked
        var all = passengerStopOrderManager.OrderedAll;
        var unlocked = passengerStopOrderManager.OrderedUnlocked;

        Loader.Log($"StopOrder: all={all.Count}, unlocked={unlocked.Count}");
    }
}
