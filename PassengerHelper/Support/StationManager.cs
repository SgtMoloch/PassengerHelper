using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.Notices;
using Game.State;
using Model;
using Model.AI;
using Model.Definition;
using Model.Definition.Data;
using Model.OpsNew;
using Network;
using RollingStock;
using Serilog;

namespace PassengerHelperPlugin.Support;


public class StationManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(StationManager));

    private PassengerHelperPlugin plugin;

    public StationManager(PassengerHelperPlugin plugin)
    {
        this.plugin = plugin;
    }

    public bool HandleTrainAtStation(BaseLocomotive _locomotive, PassengerStop _currentStop)
    {
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

        if (_currentStop != passengerLocomotive.CurrentStop)
        {
            passengerLocomotive.CurrentStop = _currentStop;
            // can set the continue flag back to false, as we have reached the next station
            passengerLocomotive.Continue = false;
            passengerLocomotive.NonTerminusStationProcedureComplete = false;
        }

        // if train is currently Stopped
        if (IsStoppedAndShouldStayStopped(passengerLocomotive))
        {
            return true;
        }

        passengerLocomotive.ResetStoppedFlags();

        if (PauseAtCurrentStation(passengerLocomotive, settings))
        {
            return true;
        }

        if (HaveLowFuel(passengerLocomotive, settings))
        {
            return true;
        }



        if (RunStationProcedure(passengerLocomotive, settings))
        {

            return true;
        }

        return false;
    }

    private bool IsStoppedAndShouldStayStopped(PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.CurrentlyStopped && !passengerLocomotive.Continue)
        {
            logger.Information("Train is currently Stopped due to: {0}", passengerLocomotive.CurrentReasonForStop);
            bool stayStopped = passengerLocomotive.ShouldStayStopped();
            if (stayStopped)
            {
                AutoEngineerPersistence persistence = new(passengerLocomotive.KeyValueObject);
                persistence.PassengerModeStatus = "Paused";
                return true;
            }
        }

        return false;
    }

    private bool PauseAtCurrentStation(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (passengerLocomotive.Continue)
        {
            return false;
        }

        if (settings.StopAtNextStation)
        {
            logger.Information("Pausing at next station due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at next station";
            return true;
        }

        if (settings.Stations[passengerLocomotive.CurrentStop.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Pausing at {0} due to setting", passengerLocomotive.CurrentStop.DisplayName);
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at " + passengerLocomotive.CurrentStop.DisplayName;
            return true;
        }

        return false;
    }

    private bool HaveLowFuel(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
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
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(passengerLocomotive.CurrentStop)}.");
                retVal = true;
            }
        }

        if (settings.StopForCoal)
        {
            logger.Information("Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(out float coal))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(passengerLocomotive.CurrentStop)}.");
                retVal = true;
            }
        }

        if (settings.StopForWater)
        {
            logger.Information("Requested stop for low water, checking level");
            // check water
            if (passengerLocomotive.CheckWaterLevel(out float water))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low water at {Hyperlink.To(passengerLocomotive.CurrentStop)}.");
                retVal = true;
            }
        }

        return retVal;
    }

    private bool RunStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStop;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;
        string LocomotiveName = _locomotive.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.include == true).Select(station => station.Key).OrderBy(d => plugin.orderedStations.IndexOf(d)).ToList();

        logger.Information("Train {0} has arrived at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations
        );

        if (orderedTerminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations");
            // continue normal logic
            return false;
        }

        if (!orderedTerminusStations.Contains(CurrentStopIdentifier))
        {
            logger.Information("Not at a terminus station");
            NonTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations);

            logger.Information("Setting Previous stop to the current stop (not at terminus)");
            passengerLocomotive.PreviousStop = CurrentStop;

            // continue normal logic
            return false;
        }
        else
        {
            logger.Information("At terminus station");

            bool atTerminusStationWest = orderedTerminusStations[1] == CurrentStopIdentifier;
            bool atTerminusStationEast = orderedTerminusStations[0] == CurrentStopIdentifier;

            logger.Information("at west terminus: {0} at east terminus {1}", atTerminusStationWest, atTerminusStationEast);
            logger.Information("passenger locomotive atTerminusWest settings: {0}", passengerLocomotive.AtTerminusStationWest);
            logger.Information("passenger locomotive atTerminusEast settings: {0}", passengerLocomotive.AtTerminusStationEast);

            // true means stay stopped, false means continue
            bool terminusStationProceduteRetVal = true;

            if (atTerminusStationWest && !passengerLocomotive.AtTerminusStationWest)
            {
                terminusStationProceduteRetVal = TerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, DirectionOfTravel.EAST);

                logger.Information("Setting Previous stop to the current stop (west terminus)");

            }

            if (atTerminusStationEast && !passengerLocomotive.AtTerminusStationEast)
            {
                terminusStationProceduteRetVal = TerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, DirectionOfTravel.WEST);

                logger.Information("Setting Previous stop to the current stop (east terminus)");
            }

            passengerLocomotive.PreviousStop = CurrentStop;

            passengerLocomotive.AtTerminusStationWest = atTerminusStationWest;
            passengerLocomotive.AtTerminusStationEast = atTerminusStationEast;

            if (!terminusStationProceduteRetVal)
            {
                settings.DoTLocked = true;
                plugin.SaveSettings();
            }

            if ((passengerLocomotive.AtTerminusStationWest || passengerLocomotive.AtTerminusStationEast) && settings.WaitForFullPassengersLastStation)
            {
                logger.Information("Waiting For full Passengers at terminus.");

                foreach (Car coach in coaches)
                {
                    PassengerMarker? marker = coach.GetPassengerMarker();


                    if (marker != null && marker.HasValue)
                    {
                        LoadSlot loadSlot = coach.Definition.LoadSlots.FirstOrDefault((LoadSlot slot) => slot.RequiredLoadIdentifier == "passengers");
                        int maxCapacity = (int)loadSlot.MaximumCapacity;
                        PassengerMarker actualMarker = marker.Value;
                        if (actualMarker.TotalPassengers < maxCapacity)
                        {
                            logger.Information("Passenger car not full, remaining stopped");
                            passengerLocomotive.CurrentlyStopped = true;
                            passengerLocomotive.CurrentReasonForStop = "Waiting for full passengers at terminus station";

                            return true;
                        }
                    }
                }

                logger.Information("Passengers are full, continuing.");

                return terminusStationProceduteRetVal;
            }
        }

        return false;
    }

    private void NonTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, IEnumerable<Car> coaches, List<string> orderedSelectedStations, List<string> orderedTerminusStations)
    {
        if (passengerLocomotive.NonTerminusStationProcedureComplete)
        {
            logger.Information("Non Terminus Station Procedure Complete");
            return;
        }

        passengerLocomotive.AtTerminusStationWest = false;
        passengerLocomotive.AtTerminusStationEast = false;

        int indexWestTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);

        logger.Information("Not at either terminus station, so there are more stops, continuing.");
        logger.Information("Checking to see if train is at station outside of terminus bounds");
        bool notAtASelectedStation = !orderedSelectedStations.Contains(CurrentStop.identifier);

        if (notAtASelectedStation)
        {
            NotAtASelectedStationProcedure(passengerLocomotive, settings, CurrentStop, orderedSelectedStations, orderedTerminusStations);
        }

        foreach (Car coach in coaches)
        {
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker != null && marker.HasValue)
            {
                PassengerMarker actualMarker = marker.Value;

                logger.Information("Direction Intelligence. Selected Destinations Count = {0}, Current selected destinations: {1}, Current direction of travel: {2}, previousStop known: {3}, currentStop {4}",
                    actualMarker.Destinations.Count,
                    actualMarker.Destinations,
                    settings.DirectionOfTravel,
                    passengerLocomotive.PreviousStop != null ? passengerLocomotive.PreviousStop.DisplayName : false,
                    CurrentStop.DisplayName);

                if (actualMarker.Destinations.Count >= 0 && settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && passengerLocomotive.PreviousStop != null && passengerLocomotive.PreviousStop != CurrentStop)
                {
                    logger.Information("There are stations selected. Should now be able to determine which direction train is going");

                    int indexPrev = orderedSelectedStations.IndexOf(passengerLocomotive.PreviousStop.identifier);
                    int indexCurr = orderedSelectedStations.IndexOf(CurrentStop.identifier);

                    if (indexPrev < indexCurr)
                    {
                        settings.DirectionOfTravel = DirectionOfTravel.WEST;
                    }
                    else
                    {
                        settings.DirectionOfTravel = DirectionOfTravel.EAST;
                    }
                }

                if (settings.DirectionOfTravel != DirectionOfTravel.UNKNOWN)
                {
                    settings.DoTLocked = true;
                    plugin.SaveSettings();
                    break;
                }
            }
        }

        logger.Information("Checking if train is in alarka");
        if (CurrentStop.identifier == "alarka" && !settings.LoopMode && !passengerLocomotive.AtAlarka)
        {
            passengerLocomotive.AtAlarka = true;
            logger.Information("Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
            Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: Arrived in Alarka, reversing direction to continue.");
            passengerLocomotive.ReverseLocoDirection();

            string cochranIdentifier = "cochran";

            logger.Information("Since train is in alarka, and the previous stop was cochran, and alarka is not a terminus, need to recheck cochran station. Doing so now.");
            foreach (Car coach in coaches)
            {
                logger.Information("Car: {0}", coach.DisplayName);
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (marker != null && marker.HasValue)
                {
                    PassengerMarker actualMarker = marker.Value;
                    List<string> currentDestinations = actualMarker.Destinations.ToList();

                    List<string> newDestinations = new List<string>(currentDestinations);
                    newDestinations.Add(cochranIdentifier);

                    logger.Information("Current stations: {0} new stations: {1}", currentDestinations, newDestinations);

                    StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, newDestinations));
                }
            }

        }
        else if (CurrentStop.identifier != "alarka")
        {
            passengerLocomotive.AtAlarka = false;
        }

        passengerLocomotive.NonTerminusStationProcedureComplete = true;
    }

    private bool TerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, IEnumerable<Car> coaches, List<string> orderedSelectedStations, DirectionOfTravel directionOfTravel)
    {
        // we have reached the last station
        if (settings.StopAtLastStation)
        {
            logger.Information("Pausing at last station due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at last station stop {Hyperlink.To(CurrentStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at last station";
            return true;
        }

        logger.Information("{0} reached terminus station at {1}", passengerLocomotive._locomotive.DisplayName, CurrentStop.DisplayName);
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: Reached terminus station at {Hyperlink.To(CurrentStop)}.");

        logger.Information("Reselecting station stops based on settings.");
        logger.Information("Setting the following stations: {0}", orderedSelectedStations);

        foreach (Car coach in coaches)
        {
            foreach (string identifier in orderedSelectedStations)
            {
                logger.Debug(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, orderedSelectedStations));
        }

        logger.Information("Checking to see if train is approaching terminus from outside of terminus bounds");
        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Direction of travel is unknown");

            if (passengerLocomotive.PreviousStop == null)
            {
                logger.Information("train was not previously at a station.");
                logger.Information("Waiting for input from engineer about which direction to travel in");
                Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: Unknown Direction. Pausing until I receive Direction of Travel via PassengerSettings.");
                passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                return true;
            }
            else
            {
                logger.Information("train was  previously at a station.");
                string prevStopId = passengerLocomotive.PreviousStop.identifier;

                logger.Information("Checking if previous stop {0} was inside teminus bounds", prevStopId);
                if (orderedSelectedStations.Contains(prevStopId))
                {
                    logger.Information("Previous stop was inside terminus bounds, therefore proceed with normal loop/point to point logic");
                    settings.DirectionOfTravel = directionOfTravel;
                    TerminusStationReverseDirectionProcedure(passengerLocomotive, settings);
                }
                else
                {
                    logger.Information("We arrived at the terminus station from outside the bounds, therefore we should proceed in the current direction");
                }
            }
        }
        else
        {
            logger.Information("Direction of travel is known");

            if (settings.DirectionOfTravel == directionOfTravel)
            {
                logger.Information("The current direction of travel is the same as the new direction of travel.");
            }
            else
            {
                logger.Information("The new direction of travel is opposite current direction of travel");

                settings.DirectionOfTravel = directionOfTravel;
                plugin.SaveSettings();

                TerminusStationReverseDirectionProcedure(passengerLocomotive, settings);
            }
        }

        return false;
    }

    private void TerminusStationReverseDirectionProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        logger.Information("Checking if in loop mode");
        // if we don't want to reverse, return to orignal logic
        if (settings.LoopMode)
        {
            logger.Information("Loop Mode is set to true. Continuing in current direction.");
            return;
        }

        logger.Information("Reversing direction");
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: Reversing direction.");

        // reverse the direction of the loco
        passengerLocomotive.ReverseLocoDirection();
    }

    private void NotAtASelectedStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<string> orderedSelectedStations, List<string> orderedTerminusStations)
    {
        logger.Information("Train is at a station that is not selected in settings.");

        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Travel direction is unknown, so unable to correct. Continuing in the current direction and will check again at the next station");
            return;
        }

        logger.Information("Travel direction is known.");
        logger.Information("Getting direction train should continue in");

        int currentStationIndex = plugin.orderedStations.IndexOf(CurrentStop.identifier);
        int indexWestTerminus_All = plugin.orderedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus_All = plugin.orderedStations.IndexOf(orderedTerminusStations[0]);

        if (currentStationIndex > indexEastTerminus_All && currentStationIndex < indexWestTerminus_All)
        {
            logger.Information("station is inbounds of the terminus stations");
        }

        if (currentStationIndex < indexEastTerminus_All)
        {
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                logger.Information("train is already going in right direction to east terminus station {0} -> {1}", CurrentStop.identifier, orderedTerminusStations[0]);
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("train is going wrong way from east teminus, revering direction based on loop/point to point setting");
                logger.Information("Checking if in loop mode");

                if (settings.LoopMode)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    Say($"{Hyperlink.To(passengerLocomotive._locomotive)} continuing direction to loop back to east terminus");
                }
                else
                {
                    logger.Information("Reversing direction");
                    Say($"{Hyperlink.To(passengerLocomotive._locomotive)} reversing direction to return to east terminus");

                    // reverse the direction of the loco
                    passengerLocomotive.ReverseLocoDirection();
                }
            }
        }
        else if (currentStationIndex > indexWestTerminus_All)
        {
            if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("train is already going in right direction to west terminus station {0} -> {1}", CurrentStop.identifier, orderedTerminusStations[1]);
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                logger.Information("train is going wrong way from west teminus, revering direction based on loop/point to point setting");
                logger.Information("Checking if in loop mode");

                if (settings.LoopMode)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    Say($"{Hyperlink.To(passengerLocomotive._locomotive)} continuing direction to loop back to west terminus");
                }
                else
                {
                    logger.Information("Reversing direction");
                    Say($"{Hyperlink.To(passengerLocomotive._locomotive)} reversing direction to return to west terminus.");

                    // reverse the direction of the loco
                    passengerLocomotive.ReverseLocoDirection();
                }
            }
        }

    }

    private void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }
}