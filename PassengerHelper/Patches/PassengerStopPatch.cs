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

[HarmonyPatch]
public static class PassengerStopPatches
{

    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerStopPatches));

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

                // if (!passengerLocomotive.NonTerminusStationProcedureComplete && !passengerLocomotive.TerminusStationProcedureComplete)
                // {
                //     logger.Information("Train {0} has arrived at {1}, but the station procedure has not completed yet.", engine.DisplayName, __instance.DisplayName);
                //     __result = false;
                //     break;
                // }
            }
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerStop), "UnloadCar")]
    private static void UnloadCar(Car car, PassengerStop __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }

        IEnumerable<Car> engines = car.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel);
        PassengerLocomotive? passengerLocomotive = null;
        foreach (Car engine in engines)
        {

            if (plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engine, out passengerLocomotive))
            {
                break;
            }
        }

        if (passengerLocomotive == null)
        {
            logger.Information("Did not find locomotive");
            return;
        }

        if (passengerLocomotive.Settings.Disable)
        {
            logger.Information("Passenger Helper Disabled, continuing normally");

            return;
        }

        plugin.stationManager.UnloadTransferPassengers(passengerLocomotive, passengerLocomotive.Settings);
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

        logger.Information("Patched Load method");
        IEnumerable<Car> engines = car.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel);
        PassengerLocomotive? passengerLocomotive = null;
        foreach (Car engine in engines)
        {

            if (plugin.trainManager.GetPassengerLocomotive((BaseLocomotive)engine, out passengerLocomotive))
            {
                break;
            }
        }

        if (passengerLocomotive == null)
        {
            logger.Information("Did not find locomotive");
            return true;
        }

        if (passengerLocomotive.Settings.Disable)
        {
            logger.Information("Passenger Helper Disabled, continuing normally");

            return true;
        }

        plugin.stationManager.LoadTransferPassengers(passengerLocomotive, passengerLocomotive.Settings);

        return true;
    }
}