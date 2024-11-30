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
    [HarmonyPatch(typeof(PassengerStop), "UnloadCar")]
    private static bool UnloadCar(ref bool __result, Car car, PassengerStop __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return true;
        }
        logger.Debug("Patched unload method");
        if (!FindLocomotive(car, plugin, __instance, out PassengerLocomotive passengerLocomotive))
        {
            return true;
        }

        if (passengerLocomotive.Settings.Disable)
        {
            return true;
        }

        MethodInfo CalculateBonusMultiplier = typeof(PassengerStop).GetMethod("CalculateBonusMultiplier", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo MarkerForCar = typeof(PassengerStop).GetMethod("MarkerForCar", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo FirePassengerStopEdgeMoved = typeof(PassengerStop).GetMethod("FirePassengerStopEdgeMoved", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo QueuePayment = typeof(PassengerStop).GetMethod("QueuePayment", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo FirePassengerStopServed = typeof(PassengerStop).GetMethod("FirePassengerStopServed", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo UnloadPassengersToWait = typeof(PassengerStop).GetMethod("UnloadPassengersToWait", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo PassengerCapacity = typeof(PassengerStop).GetMethod("PassengerCapacity", BindingFlags.NonPublic | BindingFlags.Instance);

        PassengerHelperPassengerStop passengerHelperPassengerStop = __instance.GetComponentInChildren<PassengerHelperPassengerStop>();

        float bonusMultiplier = (float)CalculateBonusMultiplier.Invoke(null, new object[] { car });
        PassengerMarker value = (PassengerMarker)MarkerForCar.Invoke(null, new object[] { car });
        bool nullLastStop = string.IsNullOrEmpty(value.LastStopIdentifier);
        if (nullLastStop || value.LastStopIdentifier != __instance.identifier)
        {
            if (!nullLastStop)
            {
                FirePassengerStopEdgeMoved.Invoke(__instance, new object[] { value.LastStopIdentifier });
            }

            value.LastStopIdentifier = __instance.identifier;
            car.SetPassengerMarker(value);
        }

        for (int i = 0; i < value.Groups.Count; i++)
        {
            PassengerGroup passengerGroup = value.Groups[i];
            if (passengerGroup.Count <= 0)
            {
                continue;
            }

            bool groupIsForDestinationSelectedOnCar = value.Destinations.Contains(passengerGroup.Destination);
            bool groupDestinationIsThisDestination = passengerGroup.Destination == __instance.identifier;

            if (!(!groupDestinationIsThisDestination && groupIsForDestinationSelectedOnCar))
            {
                passengerGroup.Count--;
                if (passengerGroup.Count > 0)
                {
                    value.Groups[i] = passengerGroup;
                }
                else
                {
                    value.Groups.RemoveAt(i);
                    i--;
                }

                car.SetPassengerMarker(value);
                if (groupDestinationIsThisDestination)
                {
                    QueuePayment.Invoke(__instance, new object[] { 1, passengerGroup.Origin, passengerGroup.Destination, bonusMultiplier });
                    FirePassengerStopServed.Invoke(__instance, new object[] { 1, car.Condition });
                }
                else
                {
                    logger.Information("Group destination is not this station and destination is not marked on car. group: {0}", passengerGroup);
                    if (!passengerHelperPassengerStop.UnloadTransferPassengers(passengerLocomotive, car, value, passengerGroup, ref i))
                    {
                        UnloadPassengersToWait.Invoke(__instance, new object[] { passengerGroup, 1 });
                        FirePassengerStopServed.Invoke(__instance, new object[] { -1, car.Condition });
                    }
                }
                __result = true;
                return false;
            }
        }

        __result = false;
        return false;
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

        MethodInfo PassengerCapacity = typeof(PassengerStop).GetMethod("PassengerCapacity", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo MarkerForCar = typeof(PassengerStop).GetMethod("MarkerForCar", BindingFlags.NonPublic | BindingFlags.Static);

        PassengerMarker value = (PassengerMarker)MarkerForCar.Invoke(null, new object[] { car });

        int num = (int)PassengerCapacity.Invoke(__instance, new object[] { car }) - value.TotalPassengers;
        if (num <= 0)
        {
            __result = false;
            return false;
        }

        PassengerHelperPassengerStop passengerHelperPassengerStop = __instance.GetComponentInChildren<PassengerHelperPassengerStop>();

        if (passengerHelperPassengerStop.LoadTransferPassengers(passengerLocomotive, car, value, num))
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