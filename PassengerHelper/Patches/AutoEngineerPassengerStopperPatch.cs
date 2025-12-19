namespace PassengerHelper.Patches;

using HarmonyLib;
using Model;
using Model.AI;
using Serilog;
using System.Reflection;
using Game;
using RollingStock;
using Support;
using Model.Ops;
using Support.GameObjects;
using PassengerHelper.UMM;
using global::PassengerHelper.Managers;

[HarmonyPatch]
public static class AutoEngineerPassengerStopperPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(AutoEngineerPassengerStopperPatches));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "ShouldStayStopped")]
    private static bool ShouldStayStopped(ref bool __result, AutoEngineerPassengerStopper __instance)
    {
        PassengerHelper plugin = Loader.PassengerHelper;
        if (!Loader.ModEntry.Enabled)
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
        PassengerHelper plugin = Loader.PassengerHelper;
        if (!Loader.ModEntry.Enabled)
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
                Loader.Log($"Train {pl._locomotive.DisplayName} has departed {pl.CurrentStation.DisplayName} at {TimeWeather.Now}.");
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
