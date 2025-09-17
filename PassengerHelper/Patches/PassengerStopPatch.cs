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
using Support.GameObjects;
using Model.Ops;
using System.Reflection;
using Game;
using Game.Messages;
using static Model.Ops.PassengerStop;
using KeyValue.Runtime;
using System;
using Managers;

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

        if (!dictionaryValue.Any())
        {
            return;
        }
        try
        {
            logger.Information("Converting old PH transfer passengers to base game passengers. loaded state is: {0}", dictionaryValue);

            List<Value> transferGroupListValue = dictionaryValue["transfer_group_list"].ArrayValue.ToList();
            List<PassengerGroup> groups = transferGroupListValue.Select(g => PassengerGroup.FromPropertyValue(g)).ToList();

            MethodInfo OffsetWaiting = typeof(PassengerStop).GetMethod("OffsetWaiting", BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (PassengerGroup g in groups)
            {
                OffsetWaiting.Invoke(__instance, new object[] { g.Destination, g.Origin, TimeWeather.Now, g.Count });
            }

            Dictionary<string, Value> newDict = new();
            newDict["transfer_group_list"] = Value.Dictionary(new Dictionary<string, Value>());
            passengerHelperPassengerStop._keyValueObject["pass-helper-state"] = Value.Dictionary(newDict);
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

        //inject station logic here
        //need quick return value since this method gets called repeatedly

        // only reach out to station manager if we should be working the car
        if (__result)
        {
            PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(car);
            StationManager stationManager = plugin.stationManager;

            // check if we should run the procedure
            bool runProcedure = stationManager.ShouldRunStationProcedure(pl, __instance);

            // if yes, run the procedure. only run it once
            if (runProcedure)
            {
                logger.Information("PassengerStopPatch::ShouldWorkCar Running station procedure");
                stationManager.RunStationProcedure(pl, __instance);
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
        PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(car);
        PassengerLocomotiveSettings pls = plugin.settingsManager.GetSettings(pl);

        if (pls.Disable)
        {
            return true;
        }

        bool trainIsPaused = pls.TrainStatus.CurrentlyStopped;
        bool preventPaxLoad = pls.PreventLoadWhenPausedAtStation;
        bool trainAtTerminus = pls.TrainStatus.AtTerminusStationEast || pls.TrainStatus.AtTerminusStationWest;
        bool waitForFullLoadAtTerminus = pls.WaitForFullPassengersTerminusStation;

        bool shouldNotLoad = trainIsPaused && preventPaxLoad && !(trainAtTerminus && waitForFullLoadAtTerminus);

        if (shouldNotLoad)
        {
            __result = true;
            return false;
        }

        return true;
    }
}