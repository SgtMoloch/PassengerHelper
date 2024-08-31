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
using Model.OpsNew;
using System.Reflection;
using Game;

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

        IEnumerable<Car> engines = car.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel);

        foreach (Car engine in engines)
        {
            AutoEngineerPersistence persistence = new(engine.KeyValueObject);

            if (!persistence.Orders.Enabled || persistence.Orders.Yard)
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

                if (!passengerLocomotive.Arrived)
                {
                    if (passengerLocomotive.Departed)
                    {
                        return;
                    }

                    logger.Information("Train {0} has not arrived at {1} yet, waiting to unload cars until it arrives", engine.DisplayName, __instance.DisplayName);
                    __result = false;
                    break;
                }
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
        if (!FindLocomotive(car, plugin, out PassengerLocomotive passengerLocomotive))
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
        PassengerMarker carMarker = (PassengerMarker)MarkerForCar.Invoke(null, new object[] { car });
        bool nullLastStop = string.IsNullOrEmpty(carMarker.LastStopIdentifier);
        if (nullLastStop || carMarker.LastStopIdentifier != __instance.identifier)
        {
            if (!nullLastStop)
            {
                FirePassengerStopEdgeMoved.Invoke(__instance, new object[] { carMarker.LastStopIdentifier });
            }

            carMarker.LastStopIdentifier = __instance.identifier;
            car.SetPassengerMarker(carMarker);
        }

        for (int i = 0; i < carMarker.Groups.Count; i++)
        {
            PassengerMarker.Group carMarkerGroup = carMarker.Groups[i];
            if (carMarkerGroup.Count <= 0)
            {
                continue;
            }

            bool groupIsForDestinationSelectedOnCar = carMarker.Destinations.Contains(carMarkerGroup.Destination);
            bool groupDestinationIsThisDestination = carMarkerGroup.Destination == __instance.identifier;

            if (!(!groupDestinationIsThisDestination && groupIsForDestinationSelectedOnCar))
            {
                carMarkerGroup.Count--;
                if (carMarkerGroup.Count > 0)
                {
                    carMarker.Groups[i] = carMarkerGroup;
                }
                else
                {
                    carMarker.Groups.RemoveAt(i);
                    i--;
                }

                car.SetPassengerMarker(carMarker);
                if (groupDestinationIsThisDestination)
                {
                    QueuePayment.Invoke(__instance, new object[] { 1, carMarkerGroup.Origin, carMarkerGroup.Destination, bonusMultiplier });
                    FirePassengerStopServed.Invoke(__instance, new object[] { 1, car.Condition });
                }
                else
                {
                    logger.Information("Group destination is not this station and destination is not marked on car. group: {0}", carMarkerGroup);
                    if (!passengerHelperPassengerStop.UnloadTransferPassengers(passengerLocomotive, car, carMarker, carMarkerGroup, ref i))
                    {
                        UnloadPassengersToWait.Invoke(__instance, new object[] { carMarkerGroup.Destination, 1 });
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
        if (!FindLocomotive(car, plugin, out PassengerLocomotive passengerLocomotive))
        {
            return true;
        }

        MethodInfo PassengerCapacity = typeof(PassengerStop).GetMethod("PassengerCapacity", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo MarkerForCar = typeof(PassengerStop).GetMethod("MarkerForCar", BindingFlags.NonPublic | BindingFlags.Static);

        PassengerMarker carMarker = (PassengerMarker)MarkerForCar.Invoke(null, new object[] { car });

        int carCapacity = (int)PassengerCapacity.Invoke(__instance, new object[] { car }) - carMarker.TotalPassengers;
        if (carCapacity <= 0)
        {
            __result = false;
            return false;
        }

        PassengerHelperPassengerStop passengerHelperPassengerStop = __instance.GetComponentInChildren<PassengerHelperPassengerStop>();

        if (passengerHelperPassengerStop.LoadTransferPassengers(passengerLocomotive, car, carMarker, carCapacity))
        {
            __result = true;
            return false;
        }

        return true;
    }

    private static bool FindLocomotive(Car car, PassengerHelperPlugin plugin, out PassengerLocomotive passengerLocomotive)
    {
        IEnumerable<Car> engines = car.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel);
        foreach (Car engine in engines)
        {

            if (plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engine, out passengerLocomotive))
            {
                return true;
            }
        }
        logger.Information("Did not find locomotive");

        passengerLocomotive = null;

        return false;
    }
}