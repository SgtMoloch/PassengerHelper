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
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;

        if (settings.Disable)
        {
            return true;
        }

        if (_nextStop != passengerLocomotive.CurrentStop)
        {
            passengerLocomotive.CurrentStop = _nextStop;
            // can set the continue flag back to false, as we have reached the next station
            passengerLocomotive.Continue = false;
        }

        // if train is currently Stopped
        if (passengerLocomotive.CurrentlyStopped && !passengerLocomotive.Continue)
        {
            logger.Information("Train is currently Stopped due to: {0}", passengerLocomotive.CurrentReasonForStop);
            bool stayStopped = passengerLocomotive.ShouldStayStopped();
            if (stayStopped)
            {
                AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
                persistence.PassengerModeStatus = "Paused";
                __result = true;
                return false;
            }
        }

        passengerLocomotive.ResetStoppedFlags();

        if (!CheckFuelLevels(passengerLocomotive, _locomotive, settings, _nextStop))
        {
            __result = true;
            return false;
        }

        if (!CheckPauseAtCurrentStation(settings, _nextStop, _locomotive, passengerLocomotive))
        {
            __result = true;
            return false;
        }

        if (!CheckTerminusStation(settings, _nextStop, _locomotive, passengerLocomotive, plugin))
        {
            __result = true;
            return false;
        }

        return true;
    }

    private static bool CheckPauseAtCurrentStation(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.Continue)
        {
            return true;
        }

        if (settings.StopAtNextStation)
        {
            logger.Information("Pausing at next station due to setting");
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at next station";
            return false;
        }

        if (settings.Stations[_nextStop.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Pausing at {0} due to setting", _nextStop.DisplayName);
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at " + _nextStop.DisplayName;
            return false;
        }

        return true;
    }
    private static bool CheckTerminusStation(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, PassengerHelperPlugin plugin)
    {
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);

        // get terminus stations
        List<string> terminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();

        if (terminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations");
            // continue normal logic
            return true;
        }

        if (!terminusStations.Contains(_nextStop.identifier))
        {

            StationProcedure(settings, _nextStop, _locomotive, passengerLocomotive, coaches, terminusStations);
            // continue normal logic
            return true;
        }
        else
        {
            bool atTerminusStationWest = terminusStations[1] == _nextStop.identifier;
            bool atTerminusStationEat = terminusStations[0] == _nextStop.identifier;

            if (atTerminusStationWest && !passengerLocomotive.AtTerminusStationWest)
            {
                passengerLocomotive.AtTerminusStationWest = true;

                return TerminusStationProcedure(settings, _nextStop, _locomotive, passengerLocomotive, coaches);
            }

            if (atTerminusStationEat && !passengerLocomotive.AtTerminusStationEast)
            {
                passengerLocomotive.AtTerminusStationEast = true;

                return TerminusStationProcedure(settings, _nextStop, _locomotive, passengerLocomotive, coaches);
            }
        }

        return true;
    }

    private static void StationProcedure(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, IEnumerable<Car> coaches, List<string> terminusStations)
    {
        passengerLocomotive.AtTerminusStationWest = false;
        passengerLocomotive.AtTerminusStationEast = false;

        logger.Information("Not at either terminus station, so there are more stops, continuing.");
        logger.Information("Ensuring at least 1 passenger car has a terminus station selected");
        foreach (Car coach in coaches)
        {
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker != null && marker.HasValue && !passengerLocomotive.HasMoreStops)
            {
                PassengerMarker actualMarker = marker.Value;

                if (!actualMarker.Destinations.Contains(terminusStations[0]) || !actualMarker.Destinations.Contains(terminusStations[1]))
                {
                    logger.Information("Selecting both terminus stations on {0}", coach.DisplayName);
                    StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, terminusStations.ToList()));
                }
            }
        }

        logger.Information("Checking if train is in alarka");
        if (_nextStop?.identifier == "alarka" && !settings.LoopMode)
        {
            logger.Information("Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
            passengerLocomotive.ReverseLocoDirection();
        }
    }

    private static bool TerminusStationProcedure(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, IEnumerable<Car> coaches)
    {
        if (passengerLocomotive.ShouldStayStopped())
        {
            // stay stopped
            return false;
        }

        // we have reached the last station
        if (settings.StopAtLastStation)
        {
            logger.Information("Pausing at last station due to setting");
            _locomotive.PostNotice("ai-stop", $"Paused at last station stop {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at last station";
            return false;
        }

        logger.Information("Reselecting station stops based on settings.");
        Say($"{Hyperlink.To(_locomotive)} reached terminus station at {Hyperlink.To(_nextStop)}.");

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
            logger.Information("Loop Mode is set to true. Continuing in current direction.");
            return true;
        }

        Say($"{Hyperlink.To(_locomotive)} reversing direction.");
        // reverse the direction of the loco
        passengerLocomotive.ReverseLocoDirection();

        return false;
    }
    private static bool CheckFuelLevels(PassengerLocomotive passengerLocomotive, BaseLocomotive _locomotive, PassengerLocomotiveSettings settings, PassengerStop _nextStop)
    {
        if (passengerLocomotive.Continue)
        {
            return true;
        }

        bool retVal = true;
        if (settings.StopForDiesel)
        {
            logger.Information("Requested stop for low diesel, checking level");
            // check diesel
            if (passengerLocomotive.CheckDieselFuelLevel(out float diesel))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(_nextStop)}.");
                retVal = false;
            }
        }

        if (settings.StopForCoal)
        {
            logger.Information("Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(out float coal))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(_nextStop)}.");
                retVal = false;
            }
        }

        if (settings.StopForWater)
        {
            logger.Information("Requested stop for low water, checking level");
            // check water
            if (passengerLocomotive.CheckWaterLevel(out float water))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped, low water at {Hyperlink.To(_nextStop)}.");
                retVal = false;
            }
        }

        return retVal;
    }

    private static void Say(string message)
    {
        Alert alert = new Alert(AlertStyle.Console, message, TimeWeather.Now.TotalSeconds);
        WindowManager.Shared.Present(alert);
    }
}
