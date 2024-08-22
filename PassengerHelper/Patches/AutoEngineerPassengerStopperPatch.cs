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
        logger.Information("terminus stations are: {0}", terminusStations);

        if (terminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations");
            // continue normal logic
            return true;
        }

        logger.Information("Current stop is: {0}", _nextStop.DisplayName);

        if (!terminusStations.Contains(_nextStop.identifier))
        {
            logger.Information("Not at a terminus station");
            StationProcedure(plugin, settings, _nextStop, _locomotive, passengerLocomotive, coaches, terminusStations);
            // continue normal logic
            return true;
        }
        else
        {
            logger.Information("At terminus station");
            bool atTerminusStationWest = terminusStations[1] == _nextStop.identifier;
            bool atTerminusStationEast = terminusStations[0] == _nextStop.identifier;
            logger.Information("at west terminus: {0} at east terminus {1}", atTerminusStationWest, atTerminusStationEast);
            logger.Information("passenger locomotive atTerminusWest settings: (0)", passengerLocomotive.AtTerminusStationWest);
            logger.Information("passenger locomotive atTerminusEast settings: (0)", passengerLocomotive.AtTerminusStationEast);

            if (atTerminusStationWest && !passengerLocomotive.AtTerminusStationWest)
            {
                settings.directionOfTravel = DirectionOfTravel.EAST;
                passengerLocomotive.AtTerminusStationWest = true;
                passengerLocomotive.AtTerminusStationEast = false;

                return TerminusStationProcedure(settings, _nextStop, _locomotive, passengerLocomotive, coaches);
            }

            if (atTerminusStationEast && !passengerLocomotive.AtTerminusStationEast)
            {
                settings.directionOfTravel = DirectionOfTravel.WEST;
                passengerLocomotive.AtTerminusStationEast = true;
                passengerLocomotive.AtTerminusStationWest = false;

                return TerminusStationProcedure(settings, _nextStop, _locomotive, passengerLocomotive, coaches);
            }
        }

        return true;
    }

    private static void StationProcedure(PassengerHelperPlugin plugin, PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, IEnumerable<Car> coaches, List<string> terminusStations)
    {
        passengerLocomotive.AtTerminusStationWest = false;
        passengerLocomotive.AtTerminusStationEast = false;

        int indexWestTerminus = plugin.orderedStations.IndexOf(terminusStations[1]);
        int indexEastTerminus = plugin.orderedStations.IndexOf(terminusStations[0]);

        List<string> orderedSelectedStations = settings.Stations.Where(kv => kv.Value.include == true).Select(kv => kv.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();

        logger.Information("Not at either terminus station, so there are more stops, continuing.");
        logger.Information("Ensuring at least 1 passenger car has a terminus station selected");

        foreach (Car coach in coaches)
        {
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker != null && marker.HasValue && !passengerLocomotive.HasMoreStops)
            {
                PassengerMarker actualMarker = marker.Value;
                logger.Information("Direction Intelligence. Selected Destinations Count = {0}, Current direction of travel: {1}, previousStop known: {2}, currentStop {3}",
                    actualMarker.Destinations.Count,
                    settings.directionOfTravel,
                    passengerLocomotive.PreviousStop != null ? passengerLocomotive.PreviousStop.DisplayName : false,
                    _nextStop.DisplayName);

                if (actualMarker.Destinations.Count == 0 && settings.directionOfTravel == DirectionOfTravel.UNKNOWN)
                {
                    logger.Information("There are no stations selected. Need to determine which direction train is going");

                    logger.Information("Setting Previous stop to the current stop");
                    passengerLocomotive.PreviousStop = _nextStop;

                    logger.Information("Getting neighboring stations based on selected stations in settings");
                    int currentStationIndex = orderedSelectedStations.IndexOf(_nextStop.identifier);
                    string neighborA = orderedSelectedStations[currentStationIndex - 1];
                    string neighborB = orderedSelectedStations[currentStationIndex + 1];

                    logger.Information("Selecting the neighboring stops to the current stop: {0} and {1}", PassengerStop.NameForIdentifier(neighborA), PassengerStop.NameForIdentifier(neighborB));
                    StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, new List<string> { neighborA, neighborB }));

                    continue;
                }

                if (actualMarker.Destinations.Count == 1 && settings.directionOfTravel == DirectionOfTravel.UNKNOWN && passengerLocomotive.PreviousStop != null && passengerLocomotive.PreviousStop != _nextStop)
                {
                    logger.Information("There is only 1 station selected. Should now be able to determine which direction train is going");

                    int indexPrev = orderedSelectedStations.IndexOf(passengerLocomotive.PreviousStop.identifier);
                    int indexCurr = orderedSelectedStations.IndexOf(_nextStop.identifier);

                    if (indexPrev < indexCurr)
                    {
                        settings.directionOfTravel = DirectionOfTravel.WEST;
                    }
                    else
                    {
                        settings.directionOfTravel = DirectionOfTravel.EAST;
                    }
                }

                if (actualMarker.Destinations.Count == 1 && settings.directionOfTravel != DirectionOfTravel.UNKNOWN)
                {
                    logger.Information("There is only 1 station selected and the direction of travel is known");

                    if (settings.directionOfTravel == DirectionOfTravel.WEST)
                    {
                        int indexNext = orderedSelectedStations.IndexOf(_nextStop.identifier) + 1;
                        selectStationsBasedOnIndex(indexNext, indexWestTerminus, orderedSelectedStations, coach);

                        continue;
                    }

                    if (settings.directionOfTravel == DirectionOfTravel.EAST)
                    {
                        int indexNext = orderedSelectedStations.IndexOf(_nextStop.identifier) - 1;
                        selectStationsBasedOnIndex(indexEastTerminus, indexNext, orderedSelectedStations, coach);

                        continue;
                    }
                }

                if (actualMarker.Destinations.Count == 0 && settings.directionOfTravel != DirectionOfTravel.UNKNOWN)
                {
                    logger.Information("There are no stations selected and the direction of travel is known");

                    if (settings.directionOfTravel == DirectionOfTravel.WEST)
                    {
                        int indexNext = orderedSelectedStations.IndexOf(_nextStop.identifier) + 1;
                        selectStationsBasedOnIndex(indexNext, indexWestTerminus, orderedSelectedStations, coach);

                        continue;
                    }

                    if (settings.directionOfTravel == DirectionOfTravel.EAST)
                    {
                        int indexNext = orderedSelectedStations.IndexOf(_nextStop.identifier) - 1;

                        selectStationsBasedOnIndex(indexEastTerminus, indexNext, orderedSelectedStations, coach);

                        continue;
                    }
                }
            }
        }

        logger.Information("Checking if train is in alarka");
        if (_nextStop?.identifier == "alarka" && !settings.LoopMode && !passengerLocomotive.AtLarka)
        {
            passengerLocomotive.AtLarka = true;
            logger.Information("Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
            passengerLocomotive.ReverseLocoDirection();
        }
        else if (_nextStop?.identifier != "alarka")
        {
            passengerLocomotive.AtLarka = false;
        }
    }

    private static void selectStationsBasedOnIndex(int startIndex, int endIndex, List<string> orderedSelectedStations, Car coach)
    {
        List<string> stationsToSelect = new();

        for (int i = startIndex; i <= endIndex; i++)
        {
            stationsToSelect.Add(orderedSelectedStations[i]);
        }

        StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, stationsToSelect));
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
        logger.Information("{0} reached terminus station at {1}", _locomotive.DisplayName, _nextStop.DisplayName);
        Say($"{Hyperlink.To(_locomotive)} reached terminus station at {Hyperlink.To(_nextStop)}.");

        HashSet<string> filteredStations = settings.Stations
        .Where(station => station.Value.include == true)
        .Select(station => station.Key)
        .ToHashSet();

        logger.Information("Setting the following stations: {0}", filteredStations);

        foreach (Car coach in coaches)
        {
            foreach (string identifier in filteredStations)
            {
                logger.Debug(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, filteredStations.ToList()));
        }

        logger.Information("Checking if in loop mode");
        // if we don't want to reverse, return to orignal logic
        if (settings.LoopMode)
        {
            logger.Information("Loop Mode is set to true. Continuing in current direction.");
            return true;
        }

        logger.Information("Reversing direction");
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
