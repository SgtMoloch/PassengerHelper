using System.Reflection;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Ops;
using PassengerHelper.Plugin;
using PassengerHelper.Support;

namespace PassengerHelper.Patches;

[HarmonyPatch]
public static class AutoEngineerPassengerStopperPatch
{
    private static readonly FieldInfo FI_locomotive =
        AccessTools.Field(typeof(AutoEngineerPassengerStopper), "_locomotive");

    private static readonly FieldInfo FI_currentstop =
    AccessTools.Field(typeof(AutoEngineerPassengerStopper), "_nextStop");

    private static readonly MethodInfo MI_PassengerCapacity = AccessTools.Method(typeof(PassengerStop), "PassengerCapacity", new[] { typeof(Car) });



    [HarmonyPrefix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "ShouldStayStopped")]
    private static bool ShouldStayStopped(ref bool __result, string bypassStationCode, AutoEngineerPassengerStopper __instance)
    {

        PassengerHelperPlugin shared = Loader.PassengerHelper;

        if (!Loader.ModEntry.Enabled)
        {
            return true;
        }

        var _locomotive = FI_locomotive.GetValue(__instance) as BaseLocomotive;
        var _currentStop = FI_currentstop.GetValue(__instance) as PassengerStop;

        if (_locomotive == null || _currentStop == null)
        {
            return true;
        }

        PassengerLocomotive pl = shared.trainManager.GetPassengerLocomotive(_locomotive.id);

        if (pl == null)
        {
            return true;
        }

        PassengerLocomotiveSettings pls = shared.settingsManager.GetSettings(pl);

        if (!pls.DepartStationsWhenFull)
        {
            return true;
        }

        foreach (Car coach in pl.GetCoaches())
        {
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker == null)
            {
                Loader.LogVerbose($"Passenger car not full, remaining stopped");
                __result = true;
                return false;
            }

            int maxCapacity = (int)MI_PassengerCapacity.Invoke(_currentStop, new[] { coach });
            PassengerMarker actualMarker = marker.Value;
            bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(_currentStop.identifier);
            bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
            if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
            {
                Loader.LogVerbose($"Passenger car not full, remaining stopped");
                __result = true;
                return false;
            }
        }

        return true;
    }
}