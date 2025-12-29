namespace PassengerHelper.Patches;

using System.Collections.Generic;
using System.Linq;
using Game.Progression;
using HarmonyLib;
using Model.Ops;
using PassengerHelper.Managers;
using PassengerHelper.Plugin;
using RollingStock;
using Support;

[HarmonyPatch]
public static class MapFeatureManagerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapFeatureManager), "HandleFeatureEnablesChanged")]
    private static void HandleFeatureEnablesChanged()
    {
        Loader.LogDebug($"[MapFeatureManagerPatch] Progressions Changed. Checking Stations");
        PassengerHelperPlugin shared = Loader.PassengerHelper;

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
                if (StopOrder.TryComputeOrderedStopsAnchored(out var orderedMainline, out var orderedAll, out var warn))
                {
                    if (!string.IsNullOrEmpty(warn))
                    {
                        Loader.Log(warn);
                    }

                    // TEMP DEBUG: dump ordering
                    for (int i = 0; i < orderedMainline.Count; i++)
                    {
                        Loader.Log($"[MapFeatureManagerPatch] MainlineOrder[{i}] = {orderedMainline[i].identifier}");
                    }

                    for (int i = 0; i < orderedAll.Count; i++)
                    {
                        Loader.Log($"[MapFeatureManagerPatch] AllOrder[{i}] = {orderedAll[i].identifier}");
                    }

                    return new StopOrderResult{Mainline = orderedMainline, All = orderedAll, Warning = warn};
                }

            return new StopOrderResult{Mainline = new(), All = new(), Warning = "[MapFeatureManagerPatch] Stop ordering failed; using empty lists."};
            });
        }

        passengerStopOrderManager.RefreshUnlocked(stop => !stop.ProgressionDisabled);

        // 🔍 TEMP sanity log — after RefreshUnlocked
        var all = passengerStopOrderManager.OrderedAll;
        var unlocked = passengerStopOrderManager.OrderedUnlockedAll;

        Loader.Log($"[MapFeatureManagerPatch] StopOrder: all={all.Count}, unlocked={unlocked.Count}");
    }
}
