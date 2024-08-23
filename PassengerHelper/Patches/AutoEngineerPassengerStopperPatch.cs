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
using Network;
using System.Reflection.Emit;

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
        var _currentStop = typeof(AutoEngineerPassengerStopper).GetField("_nextStop", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as PassengerStop;

        if (_locomotive == null || _currentStop == null)
        {
            return true;
        }

        if (plugin.stationManager.HandleTrainAtStation(_locomotive, _currentStop))
        {
            __result = true;
            return false;
        }

        return true;
    }

    private static bool PauseAtCurrentStation(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.Continue)
        {
            return false;
        }

        if (settings.StopAtNextStation)
        {
            logger.Information("Pausing at next station due to setting");
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at next station";
            return true;
        }

        if (settings.Stations[_nextStop.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Pausing at {0} due to setting", _nextStop.DisplayName);
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at " + _nextStop.DisplayName;
            return true;
        }

        return false;
    }
    private static bool StayStoppedAtStation(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, PassengerHelperPlugin plugin)
    {
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);

        // get terminus stations
        List<string> terminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();
        logger.Information("terminus stations are: {0}", terminusStations);

        if (terminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations");
            // continue normal logic
            return false;
        }

        logger.Information("Current stop is: {0}", _nextStop.DisplayName);

        if (!terminusStations.Contains(_nextStop.identifier))
        {
            logger.Information("Not at a terminus station");
            StationProcedure(plugin, settings, _nextStop, _locomotive, passengerLocomotive, coaches, terminusStations);

            logger.Information("Setting Previous stop to the current stop (not at terminus)");
            passengerLocomotive.PreviousStop = _nextStop;

            // continue normal logic
            return false;
        }
        else
        {
            logger.Information("At terminus station");
            bool atTerminusStationWest = terminusStations[1] == _nextStop.identifier;
            bool atTerminusStationEast = terminusStations[0] == _nextStop.identifier;
            logger.Information("at west terminus: {0} at east terminus {1}", atTerminusStationWest, atTerminusStationEast);
            logger.Information("passenger locomotive atTerminusWest settings: {0}", passengerLocomotive.AtTerminusStationWest);
            logger.Information("passenger locomotive atTerminusEast settings: {0}", passengerLocomotive.AtTerminusStationEast);
            bool tspRetVal = true;
            if (atTerminusStationWest && !passengerLocomotive.AtTerminusStationWest)
            {
                tspRetVal = TerminusStationProcedure(plugin, settings, _nextStop, _locomotive, passengerLocomotive, coaches, DirectionOfTravel.EAST);

                logger.Information("Setting Previous stop to the current stop (west terminus)");
                passengerLocomotive.PreviousStop = _nextStop;

                passengerLocomotive.AtTerminusStationWest = true;
                passengerLocomotive.AtTerminusStationEast = false;

                settings.DoTLocked = true;
                plugin.SaveSettings();

                return tspRetVal;
            }

            if (atTerminusStationEast && !passengerLocomotive.AtTerminusStationEast)
            {
                tspRetVal = TerminusStationProcedure(plugin, settings, _nextStop, _locomotive, passengerLocomotive, coaches, DirectionOfTravel.WEST);

                logger.Information("Setting Previous stop to the current stop (east terminus)");
                passengerLocomotive.PreviousStop = _nextStop;

                passengerLocomotive.AtTerminusStationEast = true;
                passengerLocomotive.AtTerminusStationWest = false;

                settings.DoTLocked = true;
                plugin.SaveSettings();

                return tspRetVal;
            }
        }

        return false;
    }

    private static void StationProcedure(PassengerHelperPlugin plugin, PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, IEnumerable<Car> coaches, List<string> terminusStations)
    {
        passengerLocomotive.AtTerminusStationWest = false;
        passengerLocomotive.AtTerminusStationEast = false;

        List<string> orderedSelectedStations = settings.Stations.Where(kv => kv.Value.include == true).Select(kv => kv.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();

        int indexWestTerminus = orderedSelectedStations.IndexOf(terminusStations[1]);
        int indexEastTerminus = orderedSelectedStations.IndexOf(terminusStations[0]);

        logger.Information("Not at either terminus station, so there are more stops, continuing.");
        logger.Information("Checking to see if train is at station outside of terminus bounds");
        bool notAtASelectedStation = !orderedSelectedStations.Contains(_nextStop.identifier);
        bool useAllStations = false;

        if (notAtASelectedStation)
        {
            logger.Information("Train is at a station that is not selected in settings.");

            if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
            {
                logger.Information("Travel direction is unknown, so unable to correct. Continuing in the current direction and will check again at the next station");
                useAllStations = true;
            }
            else
            {
                logger.Information("Travel direction is known.");
                logger.Information("Getting direction train should continue in");

                int currentStationIndex = plugin.orderedStations.IndexOf(_nextStop.identifier);
                int indexWestTerminus_All = plugin.orderedStations.IndexOf(terminusStations[1]);
                int indexEastTerminus_All = plugin.orderedStations.IndexOf(terminusStations[0]);

                if (currentStationIndex > indexEastTerminus_All && currentStationIndex < indexWestTerminus_All)
                {
                    logger.Information("station is inbounds of the terminus stations");
                }

                if (currentStationIndex < indexEastTerminus_All)
                {

                    if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
                    {
                        logger.Information("train is already going in right direction to east terminus station {0} -> {1}", _nextStop.identifier, terminusStations[0]);
                    }
                    else if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
                    {
                        logger.Information("train is going wrong way from east teminus, revering direction based on loop/point to point setting");
                        logger.Information("Checking if in loop mode");

                        if (settings.LoopMode)
                        {
                            logger.Information("Loop Mode is set to true. Continuing in current direction.");
                            Say($"{Hyperlink.To(_locomotive)} continuing direction to loop back to east terminus");
                        }
                        else
                        {
                            logger.Information("Reversing direction");
                            Say($"{Hyperlink.To(_locomotive)} reversing direction to return to east terminus");

                            // reverse the direction of the loco
                            passengerLocomotive.ReverseLocoDirection();
                        }
                    }
                }
                else if (currentStationIndex > indexWestTerminus_All)
                {
                    if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
                    {
                        logger.Information("train is already going in right direction to west terminus station {0} -> {1}", _nextStop.identifier, terminusStations[1]);
                    }
                    else if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
                    {
                        logger.Information("train is going wrong way from west teminus, revering direction based on loop/point to point setting");
                        logger.Information("Checking if in loop mode");

                        if (settings.LoopMode)
                        {
                            logger.Information("Loop Mode is set to true. Continuing in current direction.");
                            Say($"{Hyperlink.To(_locomotive)} continuing direction to loop back to west terminus");
                        }
                        else
                        {
                            logger.Information("Reversing direction");
                            Say($"{Hyperlink.To(_locomotive)} reversing direction to return to west terminus.");

                            // reverse the direction of the loco
                            passengerLocomotive.ReverseLocoDirection();
                        }
                    }
                }
            }
        }

        foreach (Car coach in coaches)
        {
            logger.Information("Car: {0}", coach.DisplayName);
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker != null && marker.HasValue)
            {
                PassengerMarker actualMarker = marker.Value;

                logger.Information("Direction Intelligence. Selected Destinations Count = {0}, Current selected destinations: {1}, Current direction of travel: {2}, previousStop known: {3}, currentStop {4}",
                    actualMarker.Destinations.Count,
                    actualMarker.Destinations,
                    settings.DirectionOfTravel,
                    passengerLocomotive.PreviousStop != null ? passengerLocomotive.PreviousStop.DisplayName : false,
                    _nextStop.DisplayName);

                if (actualMarker.Destinations.Count == 0 && settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && (passengerLocomotive.PreviousStop == null || passengerLocomotive.PreviousStop == _nextStop))
                {
                    logger.Information("There are no stations selected. Need to determine which direction train is going");

                    int currentStationIndex;
                    string neighborA;
                    string neighborB;

                    if (!useAllStations)
                    {
                        logger.Information("Using selected ordered stations");
                        logger.Information("Getting neighboring stations based on selected stations in settings");
                        currentStationIndex = orderedSelectedStations.IndexOf(_nextStop.identifier);
                        neighborA = orderedSelectedStations[currentStationIndex - 1];
                        neighborB = orderedSelectedStations[currentStationIndex + 1];
                    }
                    else
                    {
                        logger.Information("Using all (global) ordered stations");
                        logger.Information("Getting neighboring stations based on all stations");
                        currentStationIndex = plugin.orderedStations.IndexOf(_nextStop.identifier);
                        neighborA = plugin.orderedStations[currentStationIndex - 1];
                        neighborB = plugin.orderedStations[currentStationIndex + 1];
                    }

                    logger.Information("Selecting the neighboring stops to the current stop: {0} and {1}", PassengerStop.NameForIdentifier(neighborA), PassengerStop.NameForIdentifier(neighborB));
                    StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, new List<string> { neighborA, neighborB }));

                    continue;
                }

                if (actualMarker.Destinations.Count >= 0 && settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && passengerLocomotive.PreviousStop != null && passengerLocomotive.PreviousStop != _nextStop)
                {
                    logger.Information("There are stations selected. Should now be able to determine which direction train is going");

                    int indexPrev = orderedSelectedStations.IndexOf(passengerLocomotive.PreviousStop.identifier);
                    int indexCurr = orderedSelectedStations.IndexOf(_nextStop.identifier);

                    if (indexPrev < indexCurr)
                    {
                        settings.DirectionOfTravel = DirectionOfTravel.WEST;
                    }
                    else
                    {
                        settings.DirectionOfTravel = DirectionOfTravel.EAST;
                    }
                    plugin.SaveSettings();
                }

                HashSet<string> expectedSelectedDestinations;
                if (settings.DirectionOfTravel != DirectionOfTravel.UNKNOWN)
                {
                    logger.Information("The direction of travel is known, checking to make sure stations are selected");

                    if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
                    {
                        int indexNext = orderedSelectedStations.IndexOf(_nextStop.identifier) + 1;

                        expectedSelectedDestinations = orderedSelectedStations.GetRange(indexNext, indexWestTerminus - indexNext + 1).ToHashSet();
                        if (actualMarker.Destinations.Contains(_nextStop.identifier))
                        {
                            logger.Information("Passengers for current stop have not finsihed unloading yet, keeping current stop as part of expected stations");
                            expectedSelectedDestinations.Add(_nextStop.identifier);
                        }

                        logger.Information("Expected stations: {0} actual stations: {1}", expectedSelectedDestinations, actualMarker.Destinations);

                        if (!actualMarker.Destinations.SetEquals(expectedSelectedDestinations))
                        {
                            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
                        }

                        continue;
                    }

                    if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
                    {
                        int indexNext = orderedSelectedStations.IndexOf(_nextStop.identifier) - 1;

                        expectedSelectedDestinations = orderedSelectedStations.GetRange(indexEastTerminus, indexNext - indexEastTerminus + 1).ToHashSet();
                        if (actualMarker.Destinations.Contains(_nextStop.identifier))
                        {
                            logger.Information("Passengers for current stop have not finsihed unloading yet, keeping current stop as part of expected stations");
                            expectedSelectedDestinations.Add(_nextStop.identifier);
                        }

                        logger.Information("Expected stations: {0} actual stations: {1}", expectedSelectedDestinations, actualMarker.Destinations);

                        if (!actualMarker.Destinations.SetEquals(expectedSelectedDestinations))
                        {
                            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
                        }

                        continue;
                    }
                }
            }
        }

        logger.Information("Checking if train is in alarka");
        if (_nextStop.identifier == "alarka" && !settings.LoopMode && !passengerLocomotive.AtAlarka)
        {
            passengerLocomotive.AtAlarka = true;
            logger.Information("Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
            Say($"AI Engineer {Hyperlink.To(_locomotive)}: Arrived in Alarka, reversing direction to continue.");
            passengerLocomotive.ReverseLocoDirection();

            logger.Information("Since we are in alarka, we need to recheck cochran station. Doing so now.");
            foreach (Car coach in coaches)
            {
                logger.Information("Car: {0}", coach.DisplayName);
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (marker != null && marker.HasValue)
                {
                    PassengerMarker actualMarker = marker.Value;
                    List<string> currentDestinations = actualMarker.Destinations.ToList();

                    List<string> newDestinations = new List<string>(currentDestinations);
                    newDestinations.Add("cochran");

                    logger.Information("Current stations: {0} new stations: {1}", currentDestinations, newDestinations);

                    StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, newDestinations));
                }
            }
        }
        else if (_nextStop.identifier != "alarka")
        {
            passengerLocomotive.AtAlarka = false;
        }
    }

    private static bool TerminusStationProcedure(PassengerHelperPlugin plugin, PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive, IEnumerable<Car> coaches, DirectionOfTravel directionOfTravel)
    {
        if (passengerLocomotive.ShouldStayStopped())
        {
            // stay stopped
            return true;
        }

        // we have reached the last station
        if (settings.StopAtLastStation)
        {
            logger.Information("Pausing at last station due to setting");
            _locomotive.PostNotice("ai-stop", $"Paused at last station stop {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at last station";
            return true;
        }

        logger.Information("Reselecting station stops based on settings.");
        logger.Information("{0} reached terminus station at {1}", _locomotive.DisplayName, _nextStop.DisplayName);
        Say($"AI Engineer {Hyperlink.To(_locomotive)}: Reached terminus station at {Hyperlink.To(_nextStop)}.");

        List<string> selectedOrderedStations = settings.Stations
        .Where(station => station.Value.include == true)
        .Select(station => station.Key)
        .OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();


        logger.Information("Setting the following stations: {0}", selectedOrderedStations);

        foreach (Car coach in coaches)
        {
            foreach (string identifier in selectedOrderedStations)
            {
                logger.Debug(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, selectedOrderedStations));
        }

        logger.Information("Checking to see if train is approaching terminus from outside of terminus bounds");

        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Direction is currently unknown");

            if (passengerLocomotive.PreviousStop == null)
            {
                logger.Information("train was not previously at a station.");
                logger.Information("treating this terminus station as a regular station for now, to determine direction");
                StationProcedure(plugin, settings, _nextStop, _locomotive, passengerLocomotive, coaches, settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList());
                return false;
            }
            else
            {
                logger.Information("train was  previously at a station.");
                string prevStopId = passengerLocomotive.PreviousStop.identifier;

                logger.Information("Checking if previous stop was inside teminus bounds");
                if (selectedOrderedStations.Contains(prevStopId))
                {
                    logger.Information("Previous stop was inside terminus bounds, therefore proceed with normal loop/point to point logic");
                    settings.DirectionOfTravel = directionOfTravel;

                    logger.Information("Checking if in loop mode");
                    // if we don't want to reverse, return to orignal logic
                    if (settings.LoopMode)
                    {
                        logger.Information("Loop Mode is set to true. Continuing in current direction.");
                        return false;
                    }

                    logger.Information("Reversing direction");
                    Say($"AI Engineer {Hyperlink.To(_locomotive)}: Reversing direction.");

                    // reverse the direction of the loco
                    passengerLocomotive.ReverseLocoDirection();
                }
                else
                {
                    logger.Information("We arrived at the terminus station from outside the bounds, therefore we should proceed in the current direction");
                }

                return true;
            }
        }
        else
        {
            logger.Information("Direction of travel is known, proceed normally");

            if (settings.DirectionOfTravel == directionOfTravel)
            {
                logger.Information("The new direction of travel is the same as the new direction of travel.");
            }
            else
            {
                logger.Information("The new direction of travel is opposite current direction of travel");

                settings.DirectionOfTravel = directionOfTravel;
                plugin.SaveSettings();

                logger.Information("Checking if in loop mode");
                // if we don't want to reverse, return to orignal logic
                if (settings.LoopMode)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    return false;
                }

                logger.Information("Reversing direction");
                Say($"AI Engineer {Hyperlink.To(_locomotive)}: Reversing direction.");

                // reverse the direction of the loco
                passengerLocomotive.ReverseLocoDirection();
            }
            return true;
        }
    }

    private static bool HaveLowFuel(PassengerLocomotive passengerLocomotive, BaseLocomotive _locomotive, PassengerLocomotiveSettings settings, PassengerStop _nextStop)
    {
        if (passengerLocomotive.Continue)
        {
            return false;
        }

        bool retVal = false;
        if (settings.StopForDiesel)
        {
            logger.Information("Requested stop for low diesel, checking level");
            // check diesel
            if (passengerLocomotive.CheckDieselFuelLevel(out float diesel))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(_nextStop)}.");
                retVal = true;
            }
        }

        if (settings.StopForCoal)
        {
            logger.Information("Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(out float coal))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(_nextStop)}.");
                retVal = true;
            }
        }

        if (settings.StopForWater)
        {
            logger.Information("Requested stop for low water, checking level");
            // check water
            if (passengerLocomotive.CheckWaterLevel(out float water))
            {
                _locomotive.PostNotice("ai-stop", $"Stopped, low water at {Hyperlink.To(_nextStop)}.");
                retVal = true;
            }
        }

        return retVal;
    }


    private static void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }
}
