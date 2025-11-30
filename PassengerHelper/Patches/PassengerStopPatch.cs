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
using PassengerHelper.UMM;
using PassengerHelper.Support.GameObjects;
using Serilog;

[HarmonyPatch]
public static class PassengerStopPatches
{

    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerStopPatches));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PassengerStop), "Awake")]
    private static void Awake(PassengerStop __instance)
    {
        PassengerHelper plugin = Loader.passengerHelper;
        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        PassengerHelperPassengerStop passengerHelperPassengerStop = __instance.gameObject.AddComponent<PassengerHelperPassengerStop>();
        passengerHelperPassengerStop.gameObject.SetActive(true);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PassengerStop), "LoadState")]
    private static void LoadState(PassengerStop __instance)
    {
        PassengerHelper plugin = Loader.passengerHelper;
        if (!Loader.ModEntry.Enabled)
        {
            return;
        }
        PassengerHelperPassengerStop passengerHelperPassengerStop = __instance.GetComponent<PassengerHelperPassengerStop>();

        IReadOnlyDictionary<string, Value> dictionaryValue = passengerHelperPassengerStop._keyValueObject["pass-helper-state"].DictionaryValue;
        logger.Information("loaded state is: {0}", dictionaryValue);

        if (!dictionaryValue.Any())
        {
            return;
        }
        try
        {
            List<Value> transferGroupListValue = dictionaryValue["transfer_group_list"].ArrayValue.ToList();
            List<PassengerGroup> groups = transferGroupListValue.Select(g => PassengerGroup.FromPropertyValue(g)).ToList();

            MethodInfo OffsetWaiting = typeof(PassengerStop).GetMethod("OffsetWaiting", BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (PassengerGroup g in groups)
            {
                OffsetWaiting.Invoke(__instance, new object[] { g.Destination, g.Origin, TimeWeather.Now, g.Count });
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Exception in LoadState {identifier}", __instance.identifier);
        }
    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(PassengerStop), "ShouldWorkCar")]
    private static void ShouldWorkCar(ref bool __result, Car car, PassengerStop __instance)
    {
        PassengerHelper plugin = Loader.passengerHelper;
        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        List<Car> engines = car.EnumerateCoupled().Where(car => car.IsLocomotive).ToList();

        if(engines.Count == 0)
        {
            return;
        }

        PassengerLocomotive passengerLocomotive = plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engines[0]);
        PassengerLocomotiveSettings settings = plugin.settingsManager.GetSettings(passengerLocomotive);

        AutoEngineerPersistence persistence = new(passengerLocomotive._locomotive.KeyValueObject);

        if (persistence.Orders.Mode != AutoEngineerMode.Road)
        {
            // manual mode or yard mode
            return;
        }

        if (settings.Disable)
        {
            // Passenger Helper disabled
            return;
        }

        if (settings.TrainStatus.Arrived && passengerLocomotive.CurrentStation == __instance)
        {
            return;
        }

        if (!settings.TrainStatus.Arrived && !settings.TrainStatus.ReadyToDepart && !settings.TrainStatus.Departed)
        {
            return;
        }

        logger.Information("Train {0} has not arrived at {1} yet, waiting to unload/load cars until it arrives", passengerLocomotive._locomotive.DisplayName, __instance.DisplayName);
        __result = false;

    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerStop), "LoadCar")]
    private static bool LoadCar(ref bool __result, Car car, PassengerStop __instance)
    {
        PassengerHelper plugin = Loader.passengerHelper;
        if (!Loader.ModEntry.Enabled)
        {
            return true;
        }

        logger.Debug("Patched Load method");
        PassengerLocomotive passengerLocomotive = plugin.trainManager.GetPassengerLocomotive(car);

        PassengerLocomotiveSettings settings = plugin.settingsManager.GetSettings(passengerLocomotive);

        if (settings.Disable)
        {
            return true;
        }

        bool trainIsPaused = settings.TrainStatus.CurrentlyStopped;
        bool preventPaxLoad = settings.PreventLoadWhenPausedAtStation;
        bool trainAtTerminus = settings.TrainStatus.AtTerminusStationEast || settings.TrainStatus.AtTerminusStationWest;
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