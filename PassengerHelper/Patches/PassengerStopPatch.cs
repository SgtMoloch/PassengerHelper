using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using Model.Definition;
using PassengerHelperPlugin.Support;
using RollingStock;
using Serilog;

namespace PassengerHelperPlugin.Patches;

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
            if (plugin._locomotives.TryGetValue((BaseLocomotive)engine, out PassengerLocomotive passengerLocomotive))
            {
                if (passengerLocomotive.CurrentStop != __instance)
                {
                    logger.Information("Train {0} has not stopped at {1} yet, waiting to unload cars until it stops", engine.DisplayName, __instance.DisplayName);
                    __result = false;
                }
                break;
            }
        }

    }
}