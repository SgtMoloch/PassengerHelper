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
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PassengerStop), "ShouldWorkCar")]
    private static void ShouldWorkCar(ref bool __result, Car car, PassengerStop __instance)
    {
        PassengerHelperPlugin plugin = Loader.PassengerHelper;
        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        List<Car> engines = car.EnumerateCoupled().Where(car => car.IsLocomotive).ToList();

        if (engines.Count == 0)
        {
            return;
        }

        PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engines[0]);
        PassengerLocomotiveSettings settings = plugin.settingsManager.GetSettings(pl);

        if (settings.Disable)
        {
            // Passenger Helper disabled
            return;
        }

        AutoEngineerPersistence persistence = new(pl._locomotive.KeyValueObject);

        if (persistence.Orders.Mode == AutoEngineerMode.Off)
        {
            // manual mode
            return;
        }

        if (persistence.Orders.Mode == AutoEngineerMode.Yard)
        {
            // yard mode
            return;
        }

        TrainState state = plugin.trainStateManager.GetState(pl);

        // train has arrived and ran station procedure, so work car
        if (state.Arrived && state.CurrentStationId == __instance.identifier)
        {
            return;
        }

        Loader.LogVerbose($"Train {pl._locomotive.DisplayName} has not arrived at {__instance.DisplayName} yet, waiting to unload/load cars until it arrives");
        __result = false;

    }

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

        bool trainIsPaused = state.CurrentlyStopped;
        bool preventPaxLoad = settings.PreventLoadWhenPausedAtStation;
        bool trainAtTerminus = state.AtTerminusStationEast || state.AtTerminusStationWest;
        bool waitForFullLoadAtTerminus = settings.WaitForFullPassengersTerminusStation;

        bool shouldNotLoad = trainIsPaused && preventPaxLoad && !(trainAtTerminus && waitForFullLoadAtTerminus);

        if (shouldNotLoad)
        {
            __result = true;
            return false;
        }

        return true;
    }
}