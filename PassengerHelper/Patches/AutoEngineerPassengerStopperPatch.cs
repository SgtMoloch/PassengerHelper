namespace PassengerHelperPlugin.Patches;

using Game.State;
using HarmonyLib;
using Model;
using Model.AI;
using Model.OpsNew;
using Railloader;
using Serilog;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Model.Definition.Data;
using Model.Definition;
using System.Linq;
using Game.Messages;
using UI.EngineControls;
using UnityEngine;
using Network.Messages;
using Game;
using UI.Common;
using RollingStock;
using Game.Notices;
using Support;


[HarmonyPatch]
public static class AutoEngineerPassengerStopperPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(AutoEngineerPassengerStopperPatches));


    [HarmonyPrefix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "ShouldStayStopped")]
    private static bool ShouldStayStopped(ref bool __result, AutoEngineerPassengerStopper __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return true;
        }

        var _locomotive = typeof(AutoEngineerPassengerStopper).GetField("_locomotive", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as BaseLocomotive;
        var _nextStop = typeof(AutoEngineerPassengerStopper).GetField("_nextStop", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as PassengerStop;

        if (_locomotive == null || _nextStop == null)
        {
            return true;
        }

        if (!plugin._locomotives.TryGetValue(_locomotive, out PassengerLocomotive passengerLocomotive))
        {
            if (!plugin.passengerLocomotivesSettings.TryGetValue(_locomotive.DisplayName, out PassengerLocomotiveSettings _settings))
            {
                _settings = new PassengerLocomotiveSettings();
            }
            passengerLocomotive = new PassengerLocomotive(_locomotive, _settings);
            plugin._locomotives.Add(_locomotive, passengerLocomotive);
        }


        if (_nextStop != passengerLocomotive.CurrentStop)
        {
            passengerLocomotive.CurrentStop = _nextStop;
            passengerLocomotive.HasMoreStops = false;
        }

        // if train is currently Stopped
        if (passengerLocomotive.CurrentlyStopped)
        {
            logger.Information("Train is currently Stopped due to: {0}", passengerLocomotive.CurrentReasonForStop);
            bool stayStopped = passengerLocomotive.ShouldStayStopped();
            if (stayStopped)
            {
                __result = true;
                return false;
            }
        }

        passengerLocomotive.ResetStoppedFlags();
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;

        if (settings.StopForCoal)
        {
            logger.Information("Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(0.5f, out float coal))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped at {Hyperlink.To(_nextStop)}, low coal.");
                __result = true;
                return false;
            }
        }

        if (settings.StopForDiesel)
        {
            logger.Information("Requested stop for low diesel, checking level");
            // check diesel
            if (passengerLocomotive.CheckDieselFuelLevel(100, out float diesel))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped at {Hyperlink.To(_nextStop)}, low diesel.");
                __result = true;
                return false;
            }
        }

        if (settings.StopForWater)
        {
            logger.Information("Requested stop for low water, checking level");
            // check water
            if (passengerLocomotive.CheckWaterLevel(500, out float water))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped at {Hyperlink.To(_nextStop)}, low water.");
                __result = true;
                return false;
            }
        }

        if (settings.StopAtNextStation)
        {
            logger.Information("Pausing at next station due to setting");
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            __result = true;
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at next station";
            return false;
        }

        if (settings.Stations[_nextStop.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Pausing at {0} due to setting", _nextStop.DisplayName);
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            __result = true;
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at " + _nextStop.DisplayName;
            return false;
        }

        if (settings.WaitForConnectingTrain.Wait && passengerLocomotive.AtLastStop)
        {
            logger.Information("Checking location of connecting train based on setting");
            // get connecting train current stop
            int _baseLocoIndex = plugin._locomotives.Keys.ToList().FindIndex(x => x.id == settings.WaitForConnectingTrain.trainId);

            if (_baseLocoIndex == -1)
            {
                logger.Error("Settings said wait for connecting train, but the connecting train was not found in the mods dictionary of known trains.");
                return true;
            }
            PassengerLocomotive connectingLocomotive = plugin._locomotives.Values.ToList()[_baseLocoIndex];

            if (connectingLocomotive.CurrentStop != null)
            {
                logger.Information("Connecting train is currently at {0}.", connectingLocomotive.CurrentStop.DisplayName);
            }

            if (connectingLocomotive.CurrentStop != _nextStop || connectingLocomotive.CurrentStop == null)
            {
                logger.Information("Connecting train is not yet at the current stop, waiting for it.");
                __result = true;
                return false;
            }
        }

        if (passengerLocomotive.HasMoreStops)
        {
            return true;
        }

        logger.Information("Checking if there are more stops to do.");
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);

        foreach (Car coach in coaches)
        {
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker != null && marker.HasValue && !passengerLocomotive.HasMoreStops)
            {
                PassengerMarker actualMarker = marker.Value;

                if (actualMarker.Destinations.Where(m => m != _nextStop.identifier).Count() > 0)
                {
                    passengerLocomotive.HasMoreStops = true;
                    break;
                }
            }
        }

        if (passengerLocomotive.HasMoreStops)
        {
            logger.Information("There are more stops.");
            if (_nextStop?.identifier == "alarka" && !settings.LoopMode)
            {
                logger.Information("Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
                ReverseLocoDirection(_locomotive);
            }
            return true; // go back to default logic
        }

        logger.Information("There are no more stops.");
        passengerLocomotive.AtLastStop = true;

        if (passengerLocomotive.ShouldStayStopped())
        {
            __result = true;
            return false;
        }

        // we have reached the last station
        if (settings.StopAtLastStation)
        {
            logger.Information("Pausing at last station due to setting");
            _locomotive.PostNotice("ai-stop", $"Paused at last station stop {Hyperlink.To(_nextStop)}.");
            __result = true;
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at last station";
            return false;
        }

        logger.Information("Reselecting station stops based on settings.");
        // if we get here, then all passenger cars no longer have destinations.
        Say($"{Hyperlink.To(_locomotive)} reached last station stop at {Hyperlink.To(_nextStop)}. {TimeWeather.Now.TimeString()}.");

        HashSet<string> filteredStations = settings.Stations
        .Where(station => station.Value.include == true)
        .Select(station => station.Key)
        .ToHashSet();

        foreach (Car coach in coaches)
        {
            foreach (string identifier in filteredStations)
            {
                logger.Debug(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, filteredStations.ToList()));
        }

        // if we don't want to reverse, return to orignal logic
        if (settings.LoopMode)
        {
            logger.Information("Setting to disable reversing at last station is set to true. Continuing in current direction.");
            return true;
        }
        Say($"{Hyperlink.To(_locomotive)} reversing direction.");
        // reverse the direction of the loco
        ReverseLocoDirection(_locomotive);

        __result = true;
        return false;
    }

    private static void ReverseLocoDirection(BaseLocomotive _locomotive)
    {
        logger.Information("Reversing direction of _locomotive");
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        helper.SetOrdersValue(null, !persistence.Orders.Forward);
    }

    private static void Say(string message)
    {
        Alert alert = new Alert(AlertStyle.Console, message, TimeWeather.Now.TotalSeconds);
        WindowManager.Shared.Present(alert);
    }
}
