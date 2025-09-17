namespace PassengerHelperPlugin.Patches;

using HarmonyLib;
using Model;
using Model.AI;
using Serilog;
using System.Reflection;
using Game;
using RollingStock;
using Support.GameObjects;
using Model.Ops;
using global::PassengerHelperPlugin.Managers;
using global::PassengerHelperPlugin.Support;

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

        PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(_locomotive);
        StationManager stationManager = plugin.stationManager;

        if (stationManager.HandleTrainAtStation(pl, _currentStop))
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

        if (_locomotive.VelocityMphAbs >= 5f)
        {
            PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(_locomotive);
            PassengerLocomotiveSettings pls = plugin.settingsManager.GetSettings(pl);

            if (pls.TrainStatus.ReadyToDepart && pl.CurrentStation != null)
            {
                logger.Information("Train {0} has departed {1} at {2}.", pl._locomotive.DisplayName, pl.CurrentStation.DisplayName, TimeWeather.Now);
                pls.TrainStatus.Arrived = false;
                pls.TrainStatus.ReadyToDepart = false;
                pls.TrainStatus.Departed = true;
                pl.PreviousStation = pl.CurrentStation;
                pl.CurrentStation = null;

                plugin.settingsManager.SaveSettings(pl, pls);
            }
        }
    }
}
