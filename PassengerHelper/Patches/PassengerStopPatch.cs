namespace PassengerHelperPlugin.Patches;

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Definition;
using Support;
using RollingStock;
using Serilog;
using GameObjects;
using Model.Ops;
using System.Reflection;
using Game;
using Game.Messages;
using static Model.Ops.PassengerStop;
using KeyValue.Runtime;
using System;

[HarmonyPatch]
public static class PassengerStopPatches
{

    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerStopPatches));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PassengerStop), "Awake")]
    private static void Awake(PassengerStop __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
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
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
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
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }

        List<Car> engines = car.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel).ToList();

        foreach (Car engine in engines)
        {
            AutoEngineerPersistence persistence = new(engine.KeyValueObject);

            if (persistence.Orders.Mode != AutoEngineerMode.Road)
            {
                // manual mode or yard mode
                return;
            }

            if (plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engine, out PassengerLocomotive passengerLocomotive))
            {
                if (passengerLocomotive.Settings.Disable)
                {
                    // Passenger Helper disabled
                    return;
                }

                if (passengerLocomotive.TrainStatus.Arrived && passengerLocomotive.CurrentStation == __instance)
                {
                    return;
                }

                if (!passengerLocomotive.TrainStatus.Arrived && !passengerLocomotive.TrainStatus.ReadyToDepart && !passengerLocomotive.TrainStatus.Departed)
                {
                    return;
                }

                logger.Information("Train {0} has not arrived at {1} yet, waiting to unload/load cars until it arrives", engine.DisplayName, __instance.DisplayName);
                __result = false;
                break;
            }
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerStop), "LoadCar")]
    private static bool LoadCar(ref bool __result, Car car, PassengerStop __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return true;
        }

        logger.Debug("Patched Load method");
        if (!FindLocomotive(car, plugin, __instance, out PassengerLocomotive passengerLocomotive))
        {
            return true;
        }

        if (passengerLocomotive.Settings.Disable)
        {
            return true;
        }

        bool trainIsPaused = passengerLocomotive.Settings.TrainStatus.CurrentlyStopped;
        bool preventPaxLoad = passengerLocomotive.Settings.PreventLoadWhenPausedAtStation;
        bool trainAtTerminus = passengerLocomotive.Settings.TrainStatus.AtTerminusStationEast || passengerLocomotive.Settings.TrainStatus.AtTerminusStationWest;
        bool waitForFullLoadAtTerminus = passengerLocomotive.Settings.WaitForFullPassengersTerminusStation;

        bool shouldNotLoad = trainIsPaused && preventPaxLoad && !(trainAtTerminus && waitForFullLoadAtTerminus);

        if (shouldNotLoad)
        {
            __result = true;
            return false;
        }

        return true;
    }

    private static bool FindLocomotive(Car car, PassengerHelperPlugin plugin, PassengerStop CurrentStop, out PassengerLocomotive passengerLocomotive)
    {
        logger.Debug("Attempting to find locomotive for car {0}", car.DisplayName);
        List<Car> engines = car.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel).ToList();
        foreach (Car engine in engines)
        {
            logger.Debug("Checking {0}", engine.DisplayName);
            if (plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engine, out passengerLocomotive))
            {
                return true;
            }
        }
        logger.Error("Did not find locomotive for car {0}", car.DisplayName);

        passengerLocomotive = null;

        return false;
    }
}