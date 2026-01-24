namespace PassengerHelper.Patches;

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Definition;
using Support;
using Model.Ops;
using System.Reflection;
using Game;
using Game.Messages;
using KeyValue.Runtime;
using System;
using PassengerHelper.Plugin;
using PassengerHelper.Support.GameObjects;

[HarmonyPatch]
public static class PassengerStopPatches
{
    /* 
    prevents loading of car based on settings. because wait for full load at terminus is considered paused, has specific check for this case.
     */
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerStop), "LoadCar")]
    private static bool LoadCar(ref bool __result, Car car, PassengerStop __instance)
    {
        PassengerHelperPlugin plugin = Loader.PassengerHelper;
        if (!Loader.ModEntry.Enabled)
        {
            return true;
        }

        PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(car);

        PassengerLocomotiveSettings settings = plugin.settingsManager.GetSettings(pl);
        TrainState state = plugin.trainStateManager.GetState(pl);

        if (settings.Disable)
        {
            return true;
        }

        bool station_procedure_ran = state.NonTerminusStationProcedureComplete || state.TerminusStationProcedureComplete;

        if (!station_procedure_ran)
        {
            Loader.Log($"station procedure for {pl._locomotive.DisplayName} has not ran yet, not loading");
            __result = true;
            return false;
        }

        bool trainPausedFulload = state.StoppedWaitForFullLoad;
        bool trainAtTerminus = state.AtTerminusStationEast || state.AtTerminusStationWest;

        if (trainPausedFulload && trainAtTerminus)
        {
            return true;
        }

        bool trainIsPaused = state.CurrentlyStopped;
        bool preventPaxLoad = settings.PreventLoadWhenPausedAtStation;

        bool shouldNotLoad = trainIsPaused && preventPaxLoad;

        if (shouldNotLoad)
        {
            __result = true;
            return false;
        }

        return true;
    }
}