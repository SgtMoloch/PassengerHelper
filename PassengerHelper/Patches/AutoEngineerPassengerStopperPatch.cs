namespace PassengerHelperPlugin.Patches;

using HarmonyLib;
using Model;
using Model.AI;
using Serilog;
using System.Reflection;
using Game;
using RollingStock;
using Support;

[HarmonyPatch]
public static class AutoEngineerPassengerStopperPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(AutoEngineerPassengerStopperPatches));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "ShouldStayStopped")]
    private static bool ShouldStayStopped(ref bool __result, AutoEngineerPassengerStopper __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return true;
        }

        var _locomotive = typeof(AutoEngineerPassengerStopper).GetField("_locomotive", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as BaseLocomotive;
        var _currentStop = typeof(AutoEngineerPassengerStopper).GetField("_nextStop", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as PassengerStop;

        if (_locomotive == null || _currentStop == null)
        {
            return true;
        }

        if (plugin.stationManager.HandleTrainAtStation(_locomotive, _currentStop))
        {
            __result = true;
            return false;
        }

        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "_UpdateFor")]
    private static void _UpdateFor(AutoEngineerPassengerStopper __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }

        BaseLocomotive _locomotive = typeof(AutoEngineerPassengerStopper).GetField("_locomotive", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as BaseLocomotive;

        if (_locomotive == null)
        {
            return;
        }

        PassengerLocomotive passengerLocomotive = plugin.trainManager.GetPassengerLocomotive(_locomotive);

        if (passengerLocomotive.ReadyToDepart && _locomotive.VelocityMphAbs > 1f)
        {
            logger.Information("Train {0} has departed {1} at {2}.", passengerLocomotive._locomotive.DisplayName, passengerLocomotive.CurrentStation.DisplayName, TimeWeather.Now);
            passengerLocomotive.Arrived = false;
            passengerLocomotive.ReadyToDepart = false;
            passengerLocomotive.Departed = true;
            passengerLocomotive.CurrentStation = null;
            passengerLocomotive.departureTime = TimeWeather.Now;
        }

    }
}
