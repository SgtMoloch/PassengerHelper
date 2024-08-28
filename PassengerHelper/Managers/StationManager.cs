namespace PassengerHelperPlugin.Managers;

using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Messages;
using Game.State;
using Support;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.OpsNew;
using Network;
using RollingStock;
using Serilog;

public class StationManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(StationManager));

    internal readonly List<string> orderedStations;

    internal TrainManager trainManager;
    internal SettingsManager settingsManager;

    public readonly Dictionary<string, List<PassengerMarker.Group>> groupDictionary = new();

    public StationManager(SettingsManager settingsManager, TrainManager trainManager, List<string> orderedStations)
    {
        this.orderedStations = orderedStations;
        this.trainManager = trainManager;
        this.settingsManager = settingsManager;
    }

    internal List<PassengerStop> GetPassengerStops()
    {
        return PassengerStop.FindAll()
        .Where(ps => !ps.ProgressionDisabled)
        .OrderBy(d => orderedStations.IndexOf(d.identifier))
        .ToList();
    }

    public bool HandleTrainAtStation(BaseLocomotive locomotive, PassengerStop currentStop)
    {
        PassengerLocomotive passengerLocomotive = this.trainManager.GetPassengerLocomotive(locomotive);
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;

        if (settings.Disable)
        {
            logger.Information("Passenger Helper is currently disabled for {0} due to disabled setting.", locomotive.DisplayName);
            // return to original game logic
            return false;
        }

        if (currentStop != passengerLocomotive.CurrentStation)
        {
            logger.Information("Train {0} has arrived at station {1}", locomotive.DisplayName, currentStop.DisplayName);
            passengerLocomotive.CurrentStation = currentStop;
            // can set the continue flag back to false, as we have reached the next station
            passengerLocomotive.Continue = false;
            passengerLocomotive.Arrived = true;
            passengerLocomotive.ReadyToDepart = false;
            passengerLocomotive.Departed = false;
            passengerLocomotive.settingsHash = 0;
            passengerLocomotive.stationSettingsHash = 0;
            passengerLocomotive.AtTerminusStationWest = false;
            passengerLocomotive.AtTerminusStationEast = false;
            passengerLocomotive.CurrentlyStopped = false;
            passengerLocomotive.NonTerminusStationProcedureComplete = false;
            passengerLocomotive.TerminusStationProcedureComplete = false;
            passengerLocomotive.UnloadTransferComplete = false;
            passengerLocomotive.LoadTransferComplete = false;
        }

        // if we have departed, cease all procedures unless a setting was changed after running the procedure
        if (passengerLocomotive.ReadyToDepart && passengerLocomotive.settingsHash == settings.getSettingsHash() + passengerLocomotive.CurrentStation.identifier.GetHashCode())
        {
            return false;
        }

        // v2
        // is next station occupied?
        // is train en-route to current station?
        // how many tracks does current station have?
        // route appropriately

        // if train is currently Stopped
        if (IsStoppedAndShouldStayStopped(passengerLocomotive))
        {
            return true;
        }

        if (passengerLocomotive.stationSettingsHash != settings.Stations.GetHashCode() + passengerLocomotive.CurrentStation.identifier.GetHashCode())
        {
            if (!passengerLocomotive.Continue)
            {
                logger.Information("Station settings have changed, running Station Procedure");
                if (RunStationProcedure(passengerLocomotive, settings))
                {
                    return true;
                }
            }
        }
        else
        {
            logger.Information("Station Settings have not changed. Skipping Station Procedure");
        }

        if (passengerLocomotive.settingsHash != settings.getSettingsHash() + passengerLocomotive.CurrentStation.identifier.GetHashCode())
        {
            if (!passengerLocomotive.Continue)
            {
                logger.Information("Passenger settings have changed, checking for pause conditions");
                passengerLocomotive.ResetStoppedFlags();

                if (PauseAtCurrentStation(passengerLocomotive, settings))
                {
                    return true;
                }

                if (HaveLowFuel(passengerLocomotive, settings))
                {
                    return true;
                }
            }

            passengerLocomotive.settingsHash = settings.getSettingsHash() + passengerLocomotive.CurrentStation.identifier.GetHashCode();
        }
        else
        {
            logger.Information("Passenger settings have not changed, skipping check for pause conditions");
        }

        if (!passengerLocomotive.CurrentlyStopped)
        {
            logger.Information("Train {0} is ready to depart station {1}", locomotive.DisplayName, currentStop.DisplayName);
            passengerLocomotive.ReadyToDepart = true;
        }

        return passengerLocomotive.CurrentlyStopped;
    }

    private bool IsStoppedAndShouldStayStopped(PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.CurrentlyStopped)
        {
            logger.Information("Train is currently Stopped due to: {0}", passengerLocomotive.CurrentReasonForStop);
            if (passengerLocomotive.ShouldStayStopped())
            {
                return true;
            }
        }

        return false;
    }

    private bool PauseAtCurrentStation(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (settings.StopAtNextStation)
        {
            logger.Information("Pausing at station due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at next station";
            return true;
        }

        if (settings.Stations[passengerLocomotive.CurrentStation.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Pausing at {0} due to setting", passengerLocomotive.CurrentStation.DisplayName);
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at " + passengerLocomotive.CurrentStation.DisplayName;
            return true;
        }

        if (settings.StopAtLastStation && settings.Stations[passengerLocomotive.CurrentStation.identifier].TerminusStation == true)
        {
            logger.Information("Pausing at {0} due to setting", passengerLocomotive.CurrentStation.DisplayName);
            passengerLocomotive.PostNotice("ai-stop", $"Paused at terminus station {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at terminus station " + passengerLocomotive.CurrentStation.DisplayName;
            return true;
        }

        return false;
    }

    private bool HaveLowFuel(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        bool retVal = false;
        if (settings.StopForDiesel)
        {
            logger.Information("Requested stop for low diesel, checking level");
            // check diesel
            if (passengerLocomotive.CheckDieselFuelLevel(out float diesel))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        if (settings.StopForCoal)
        {
            logger.Information("Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(out float coal))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        if (settings.StopForWater)
        {
            logger.Information("Requested stop for low water, checking level");
            // check water
            if (passengerLocomotive.CheckWaterLevel(out float water))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low water at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        return retVal;
    }

    private bool RunStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStation;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.include == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        List<string> boundedOrderedStations = orderedSelectedStations.GetRange(indexEastTerminus, indexWestTerminus + 1);
        logger.Information("Running station procedure for Train {0} at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}, in the following direction: {5}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString()
        );

        if (orderedTerminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations, current selected terminus stations: {0}", orderedTerminusStations);
            Say($"AI Engineer {Hyperlink.To(_locomotive)}: \"2 Terminus stations must be selected. Check your passenger settings.\"");

            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Terminus stations not selected";
            return true;
        }

        if (!orderedTerminusStations.Contains(CurrentStopIdentifier))
        {
            logger.Information("Not at a terminus station");

            if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && (passengerLocomotive.PreviousStation == null || passengerLocomotive.PreviousStation == CurrentStop))
            {
                string reason = "Unknown Direction of Travel";
                if (passengerLocomotive.CurrentReasonForStop != reason)
                {
                    logger.Information("Train is in {0}, previous stop is not known, direction of travel is unknown, so cannot accurately determine direction, pausing and waiting for manual intervention", CurrentStop.DisplayName);
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(CurrentStop)} until I receive Direction of Travel via PassengerSettings.\"");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                    passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                    passengerLocomotive.CurrentlyStopped = true;
                    passengerLocomotive.CurrentReasonForStop = reason;

                    if (CurrentStop.identifier == "alarka")
                    {
                        passengerLocomotive.AtAlarka = true;
                    }

                    if (CurrentStop.identifier == "cochran")
                    {
                        passengerLocomotive.AtCochran = true;
                    }
                }

                return true;
            }

            bool nonTerminusStationProcedureRetVal = RunNonTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations);

            if (!nonTerminusStationProcedureRetVal)
            {
                logger.Information("Setting Previous stop to the current stop (not at terminus)");
                passengerLocomotive.SetPreviousStop(CurrentStop);

                settings.DoTLocked = true;
                settingsManager.SaveSettings();
                passengerLocomotive.NonTerminusStationProcedureComplete = true;
            }

            // setting the previous stop on the settings changes the hash, so re-cache the settings
            passengerLocomotive.stationSettingsHash = settings.Stations.GetHashCode() + passengerLocomotive.CurrentStation.identifier.GetHashCode();
            passengerLocomotive.settingsHash = settings.getSettingsHash();

            return nonTerminusStationProcedureRetVal;
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
            bool terminusStationProcedureRetVal = true;

            if (atTerminusStationWest && !passengerLocomotive.AtTerminusStationWest)
            {
                terminusStationProcedureRetVal = RunTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations, DirectionOfTravel.EAST);

                logger.Information("Setting Previous stop to the current stop (west terminus)");
            }

            if (atTerminusStationEast && !passengerLocomotive.AtTerminusStationEast)
            {
                terminusStationProcedureRetVal = RunTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations, DirectionOfTravel.WEST);

                logger.Information("Setting Previous stop to the current stop (east terminus)");
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
            }

            if (!terminusStationProcedureRetVal)
            {
                passengerLocomotive.SetPreviousStop(CurrentStop);
                passengerLocomotive.AtTerminusStationWest = atTerminusStationWest;
                passengerLocomotive.AtTerminusStationEast = atTerminusStationEast;
                passengerLocomotive.TerminusStationProcedureComplete = true;

                settings.DoTLocked = true;

                settingsManager.SaveSettings();
            }


            // setting the previous stop on the settings changes the hash, so re-cache the settings
            passengerLocomotive.stationSettingsHash = settings.Stations.GetHashCode() + passengerLocomotive.CurrentStation.identifier.GetHashCode();
            passengerLocomotive.settingsHash = settings.getSettingsHash();

            return terminusStationProcedureRetVal;
        }

    }

    private bool RunNonTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, IEnumerable<Car> coaches, List<string> orderedSelectedStations, List<string> orderedTerminusStations)
    {
        string currentStopIdentifier = CurrentStop.identifier;
        string prevStopIdentifier = passengerLocomotive.PreviousStation != null ? passengerLocomotive.PreviousStation.identifier : "";

        passengerLocomotive.AtTerminusStationWest = false;
        passengerLocomotive.AtTerminusStationEast = false;

        int indexWestTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexCurr = orderedSelectedStations.IndexOf(currentStopIdentifier);

        logger.Information("Not at either terminus station, so there are more stops");
        logger.Information("Checking to see if train is at station outside of terminus bounds");
        bool notAtASelectedStation = !orderedSelectedStations.Contains(currentStopIdentifier);

        if (notAtASelectedStation)
        {
            NotAtASelectedStationProcedure(passengerLocomotive, settings, CurrentStop, orderedSelectedStations, orderedTerminusStations);
        }

        string cochranIdentifier = "cochran";
        string alarkaIdentifier = "alarka";
        string almondIdentifier = "almond";

        logger.Information("Direction Intelligence: Current direction of travel: {0}, previousStop known: {1}, currentStop {2}",
                    settings.DirectionOfTravel,
                    passengerLocomotive.PreviousStation != null ? passengerLocomotive.PreviousStation.DisplayName : false,
                    CurrentStop.DisplayName);


        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && passengerLocomotive.PreviousStation != null && passengerLocomotive.PreviousStation != CurrentStop)
        {
            logger.Information("Should now be able to determine which direction train is going");

            string reason = "Unknown Direction of Travel";
            if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == alarkaIdentifier && !orderedTerminusStations.Contains(alarkaIdentifier))
            {
                if (passengerLocomotive.CurrentReasonForStop != reason)
                {
                    logger.Information("Train is in Cochran, previous stop was alarka, direction of travel is unknown, alarka was not a terminus station, so cannot accurately determine direction, pausing and waiting for manual intervention");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(CurrentStop)} until I receive Direction of Travel via PassengerSettings.\"");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                    passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                    passengerLocomotive.CurrentlyStopped = true;
                    passengerLocomotive.CurrentReasonForStop = reason;
                    passengerLocomotive.AtCochran = true;
                }

                return true;
            }
            else if (currentStopIdentifier == alarkaIdentifier && prevStopIdentifier == cochranIdentifier)
            {
                if (passengerLocomotive.CurrentReasonForStop != reason)
                {
                    logger.Information("Train is in Alarka, previous stop was cochran, direction of travel is unknown, so cannot accurately determine direction, pausing and waiting for manual intervention");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing until at {Hyperlink.To(CurrentStop)} until I receive Direction of Travel via PassengerSettings.\"");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                    passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                    passengerLocomotive.CurrentlyStopped = true;
                    passengerLocomotive.CurrentReasonForStop = reason;
                    passengerLocomotive.AtAlarka = true;
                }

                return true;
            }
            else
            {
                logger.Information("Determining direction of travel");
                int indexPrev = orderedSelectedStations.IndexOf(passengerLocomotive.PreviousStation.identifier);

                if (indexPrev < indexCurr)
                {
                    logger.Information("Direction of Travel: WEST");
                    settings.DirectionOfTravel = DirectionOfTravel.WEST;
                }
                else
                {
                    logger.Information("Direction of Travel: EAST");
                    settings.DirectionOfTravel = DirectionOfTravel.EAST;
                }
            }
        }

        if (settings.DirectionOfTravel != DirectionOfTravel.UNKNOWN)
        {
            List<string> boundedOrderedStations = orderedSelectedStations.GetRange(indexEastTerminus, indexWestTerminus + 1);
            int indexCurrBounded = boundedOrderedStations.IndexOf(currentStopIdentifier);
            int boundedOrderedStationsCount = boundedOrderedStations.Count;
            HashSet<string> expectedSelectedDestinations = new();
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                // add one to range to include terminus station
                expectedSelectedDestinations = boundedOrderedStations.GetRange(indexCurrBounded, boundedOrderedStationsCount - indexCurrBounded + 1).ToHashSet();

                if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == alarkaIdentifier)
                {
                    logger.Information("Train is at cochran, heading west and alarka was previous station. Remove Alarka from expected stations");
                    expectedSelectedDestinations.Remove(alarkaIdentifier);
                }
            }

            if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                // add one to range to include current station
                expectedSelectedDestinations = orderedSelectedStations.GetRange(0, indexCurrBounded + 1).ToHashSet();

                if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == almondIdentifier)
                {
                    logger.Information("Train is at cochran, heading east and almond was previous station. Add Alarka from expected stations");
                    expectedSelectedDestinations.Add(alarkaIdentifier);
                }
            }

            if (currentStopIdentifier == alarkaIdentifier && orderedSelectedStations.Contains(cochranIdentifier))
            {
                logger.Information("Train is at alarka, heading {0} and alarka is not a terminus. Add cochran to expected stations", settings.DirectionOfTravel.ToString());
                expectedSelectedDestinations.Add(cochranIdentifier);
            }

            logger.Information("Expected selected stations are: {0}", expectedSelectedDestinations);

            logger.Information("Checking passenger cars to make sure they have the proper selected stations");

            // transfer station check
            int numTransferStations = settings.Stations.Where(s => s.Value.stationAction == StationAction.Transfer).Count();
            bool transferStationSelected = numTransferStations > 0;

            if (transferStationSelected)
            {
                logger.Information("Transfer station selected, checking direction and modifying expected selected stations");

                // condition 1, there is 1 transfer station, so we want to select all stops that are selected in settings on the way there, but not the way back
                if (numTransferStations == 1)
                {
                    string transferStationIdentifier = settings.Stations.Where(s => s.Value.stationAction == StationAction.Transfer).Select(s => s.Key).First();
                    int transferStationIndex = orderedSelectedStations.IndexOf(transferStationIdentifier);
                    if (transferStationIndex > indexCurr && settings.DirectionOfTravel == DirectionOfTravel.WEST)
                    {
                        // select all to the west of the west terminus station
                        expectedSelectedDestinations.Union(orderedSelectedStations.GetRange(indexWestTerminus, orderedSelectedStations.Count - indexWestTerminus + 1));
                    }

                    if (transferStationIndex < indexCurr && settings.DirectionOfTravel == DirectionOfTravel.EAST)
                    {
                        // select all to the east of the east terminus station
                        expectedSelectedDestinations.Union(orderedSelectedStations.GetRange(0, indexEastTerminus + 1));
                    }
                }
            }
            foreach (Car coach in coaches)
            {
                logger.Information("Checking Car {0}", coach.DisplayName);
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (marker != null && marker.HasValue)
                {
                    PassengerMarker actualMarker = marker.Value;

                    logger.Information("Current selected stations are: {0}", actualMarker.Destinations);
                    if (!actualMarker.Destinations.SetEquals(expectedSelectedDestinations))
                    {
                        StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
                    }
                }
            }

            logger.Information("Checking if train is in alarka or cochran and Passenger Mode to see if we need to reverse engine direction");
            bool atAlarka = currentStopIdentifier == alarkaIdentifier && !passengerLocomotive.AtAlarka;
            bool atCochran = currentStopIdentifier == cochranIdentifier && !passengerLocomotive.AtCochran && !orderedSelectedStations.Contains(alarkaIdentifier);

            if (!settings.LoopMode)
            {
                if (atAlarka)
                {
                    passengerLocomotive.AtAlarka = true;
                    logger.Information("Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Arrived in Alarka, reversing direction to continue.\"");
                    passengerLocomotive.ReverseLocoDirection();
                }
                else
                {
                    passengerLocomotive.AtAlarka = false;
                }

                if (atCochran)
                {
                    passengerLocomotive.AtCochran = true;
                    logger.Information("Train is in Cochran, there are more stops, loop mode is not activated and alarka is not a selected station. Reversing train.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Arrived in Cochran, reversing direction to continue.\"");
                    passengerLocomotive.ReverseLocoDirection();
                }
                else
                {
                    passengerLocomotive.AtCochran = false;
                }
            }
        }

        return false;
    }

    private bool RunTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, IEnumerable<Car> coaches, List<string> orderedSelectedStations, List<string> orderedTerminusStations, DirectionOfTravel directionOfTravel)
    {
        // we have reached the last station
        if (settings.StopAtLastStation)
        {
            logger.Information("Pausing at last station due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at last station stop {Hyperlink.To(CurrentStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at last station";
        }

        logger.Information("{0} reached terminus station at {1}", passengerLocomotive._locomotive.DisplayName, CurrentStop.DisplayName);
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reached terminus station at {Hyperlink.To(CurrentStop)}.\"");

        logger.Information("Re-selecting station stops based on settings.");

        // transfer station check
        int numTransferStations = settings.Stations.Where(s => s.Value.stationAction == StationAction.Transfer).Count();
        bool transferStationSelected = numTransferStations > 0;

        string currentStopIdentifier = CurrentStop.identifier;
        int indexWestTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexCurr = orderedSelectedStations.IndexOf(currentStopIdentifier);

        List<string> boundedOrderedStations = orderedSelectedStations.GetRange(indexEastTerminus, indexWestTerminus + 1);

        HashSet<string> expectedSelectedDestinations = boundedOrderedStations.ToHashSet();
        if (transferStationSelected)
        {
            logger.Information("Transfer station selected, checking direction and modifying expected selected stations");

            // condition 1, there is 1 transfer station, so we want to select all stops that are selected in settings on the way there, but not the way back
            if (numTransferStations == 1)
            {
                string transferStationIdentifier = settings.Stations.Where(s => s.Value.stationAction == StationAction.Transfer).Select(s => s.Key).First();
                int transferStationIndex = orderedSelectedStations.IndexOf(transferStationIdentifier);
                if (transferStationIndex > indexCurr && directionOfTravel == DirectionOfTravel.WEST)
                {
                    // select all to the west of the west terminus station
                    expectedSelectedDestinations.Union(orderedSelectedStations.GetRange(indexWestTerminus, orderedSelectedStations.Count - indexWestTerminus + 1));
                }

                if (transferStationIndex < indexCurr && directionOfTravel == DirectionOfTravel.EAST)
                {
                    // select all to the east of the east terminus station
                    expectedSelectedDestinations.Union(orderedSelectedStations.GetRange(0, indexEastTerminus + 1));
                }
            }
        }

        logger.Information("Setting the following stations: {0}", expectedSelectedDestinations);

        foreach (Car coach in coaches)
        {
            foreach (string identifier in expectedSelectedDestinations)
            {
                logger.Information(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
        }

        logger.Information("Checking to see if train is approaching terminus from outside of terminus bounds");
        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Direction of travel is unknown");

            if (passengerLocomotive.PreviousStation == null)
            {
                logger.Information("train was not previously at a station.");
                logger.Information("Waiting for input from engineer about which direction to travel in");
                Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing until I receive Direction of Travel via PassengerSettings.\"");
                passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");

                passengerLocomotive.CurrentlyStopped = true;
                passengerLocomotive.CurrentReasonForStop = "At Terminus Station and have an unknown direction.";
                return true;
            }
            else
            {
                logger.Information("train was  previously at a station.");
                string prevStopId = passengerLocomotive.PreviousStation.identifier;

                logger.Information("Checking if previous stop {0} was inside terminus bounds", prevStopId);
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
                settingsManager.SaveSettings();

                TerminusStationReverseDirectionProcedure(passengerLocomotive, settings);
            }
        }

        return false;
    }

    private void TerminusStationReverseDirectionProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        logger.Information("Checking if in loop mode");
        // if we don't want to reverse, return to original logic
        if (settings.LoopMode)
        {
            logger.Information("Loop Mode is set to true. Continuing in current direction.");
            return;
        }

        logger.Information("Reversing direction");
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reversing direction.\"");

        // reverse the direction of the loco
        passengerLocomotive.ReverseLocoDirection();
    }

    private void NotAtASelectedStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<string> orderedSelectedStations, List<string> orderedTerminusStations)
    {
        logger.Information("Train is at a station: {0} that is not within the terminus bounds: {1}", CurrentStop.identifier, orderedTerminusStations);

        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Travel direction is unknown, so unable to correct. Continuing in the current direction and will check again at the next station");
            return;
        }

        logger.Information("Travel direction is known.");
        logger.Information("Getting direction train should continue in");

        int currentStationIndex = orderedStations.IndexOf(CurrentStop.identifier);
        int indexWestTerminus_All = orderedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus_All = orderedStations.IndexOf(orderedTerminusStations[0]);

        if (currentStationIndex > indexEastTerminus_All && currentStationIndex < indexWestTerminus_All)
        {
            logger.Information("station is inbounds of the terminus stations");
            return;
        }

        // if not in bounds of terminus
        if (currentStationIndex < indexEastTerminus_All)
        {
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                logger.Information("train is already going in right direction to east terminus station {0} -> {1}", CurrentStop.identifier, orderedTerminusStations[0]);
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("train is going wrong way from east terminus, revering direction based on loop/point to point setting");
                logger.Information("Checking if in loop mode");

                if (settings.LoopMode)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Continuing direction to loop back to East Terminus.\"");
                }
                else
                {
                    logger.Information("Reversing direction");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reversing direction to return back to East Terminus.\"");

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
                logger.Information("train is going wrong way from west terminus, revering direction based on loop/point to point setting");
                logger.Information("Checking if in loop mode");

                if (settings.LoopMode)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Continuing direction to loop back to West Terminus.\"");
                }
                else
                {
                    logger.Information("Reversing direction");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reversing direction to return back to West Terminus.\"");

                    // reverse the direction of the loco
                    passengerLocomotive.ReverseLocoDirection();
                }
            }
        }

    }

    public void UnloadTransferPassengers(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (passengerLocomotive.UnloadTransferComplete)
        {
            return;
        }
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStation;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.include == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        List<string> boundedOrderedStations = orderedSelectedStations.GetRange(indexEastTerminus, indexWestTerminus + 1);

        logger.Information("Running UnloadTransferPassengers procedure for Train {0} at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}, in the following direction: {5}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString());

        if (orderedTerminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations, current selected terminus stations: {0}. Continuing normally", orderedTerminusStations);
            return;
        }

        // v1
        // does train consider this station a transfer station?
        if (settings.Stations[CurrentStopIdentifier].stationAction == StationAction.Transfer)
        {
            logger.Information("Train has this station as a transfer station");
            // does train have transfer passengers?

            bool maybeHasTransferPassengers = indexEastTerminus > 0 || indexWestTerminus < orderedSelectedStations.Count;

            List<string> stationsToTransferTo = orderedSelectedStations.Except(boundedOrderedStations).ToList();
            logger.Information("Train has the following terminus stations: {0}, the following selected stations: {1}, with the following transfer stations: {2}, and the train maybe has transfer passengers: {3}",
            orderedTerminusStations, orderedSelectedStations, stationsToTransferTo, maybeHasTransferPassengers);

            // if yes, unload them into the manager
            if (maybeHasTransferPassengers)
            {
                logger.Information("Train maybe has transfer passengers");
                foreach (Car coach in coaches)
                {
                    PassengerMarker? maybeMarker = coach.GetPassengerMarker();

                    if (maybeMarker != null && maybeMarker.HasValue)
                    {
                        PassengerMarker marker = maybeMarker.Value;
                        logger.Information("Coach has the following passenger marker: {0}", marker);

                        for (int i = 0; i < marker.Groups.Count; i++)
                        {
                            PassengerMarker.Group group = marker.Groups[i];
                            logger.Information("Checking group: {0}", group);
                            if (group.Count <= 0)
                            {
                                continue;
                            }
                            if (stationsToTransferTo.Contains(group.Destination))
                            {
                                logger.Information("Group contains {0} passenger(s) for a transfer destination, {1}", group.Count, group.Destination);
                                if (!groupDictionary.TryGetValue(CurrentStopIdentifier, out List<PassengerMarker.Group> groups))
                                {
                                    groups = new();
                                    groupDictionary.Add(CurrentStopIdentifier, groups);
                                }
                                groups.Add(group);
                                marker.Groups.RemoveAt(i);
                                i--;
                                marker.Destinations.Remove(group.Destination);
                                coach.SetPassengerMarker(marker);
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                }
            }
            passengerLocomotive.UnloadTransferComplete = true;
        }
    }

    public void LoadTransferPassengers(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (passengerLocomotive.LoadTransferComplete)
        {
            return;
        }
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStation;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.include == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        List<string> boundedOrderedStations = orderedSelectedStations.GetRange(indexEastTerminus, indexWestTerminus + 1);

        logger.Information("Running LoadTransferPassengers procedure for Train {0} at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}, in the following direction: {5}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString());


        logger.Information("Station Manager has the following groups: {0}", groupDictionary);
        logger.Information("Getting groups for bounded destinations");
        List<PassengerMarker.Group> groupsWithinBoundedStations = new();
        if (groupDictionary.Keys.Contains(CurrentStopIdentifier) && groupDictionary[CurrentStopIdentifier].Count > 0)
        {
            logger.Information("The current station {0} contains {1} groups, checking to see if any of them can be loaded onto the current train", CurrentStopName, groupDictionary[CurrentStopIdentifier].Count);
            for (int i = 0; i < groupDictionary[CurrentStopIdentifier].Count; i++)
            {
                PassengerMarker.Group group = groupDictionary[CurrentStopIdentifier][i];

                if (boundedOrderedStations.Contains(group.Destination))
                {
                    logger.Information("Found group {0} that can be loaded onto the current train", group);
                    groupsWithinBoundedStations.Add(group);
                    groupDictionary[CurrentStopIdentifier].RemoveAt(i);
                    i--;
                }
            }
        }

        if (groupsWithinBoundedStations.Count > 0)
        {
            logger.Information("There are {0} groups totalling {1} passengers that can be loaded onto the current train", groupsWithinBoundedStations.Count, groupsWithinBoundedStations.Sum(g => g.Count));
            foreach (Car coach in coaches)
            {
                PassengerMarker? maybeMarker = coach.GetPassengerMarker();

                if (maybeMarker != null && maybeMarker.HasValue)
                {
                    PassengerMarker marker = maybeMarker.Value;
                    logger.Information("Coach has the following passenger marker: {0}", marker);

                    int capacity = PassengerCapacity(coach, CurrentStop) - marker.TotalPassengers;
                    logger.Information("Coach has the following capacity: {0}", capacity);
                    for (int i = 0; i < groupsWithinBoundedStations.Count; i++)
                    {
                        PassengerMarker.Group group = groupsWithinBoundedStations[i];
                        logger.Information("Checking group: {0}", group);
                        if (group.Count <= 0)
                        {
                            continue;
                        }

                        if (group.Count <= capacity)
                        {
                            logger.Information("group has equal to or less than car capacity");
                            marker.Groups.Add(group);
                            groupsWithinBoundedStations.RemoveAt(i);
                        }
                        else
                        {
                            logger.Information("group has greater than car capacity");
                            marker.Groups.Add(new PassengerMarker.Group(group.Origin, group.Destination, capacity, group.Boarded));
                            group.Count -= capacity;
                        }
                        i--;
                    }
                }
            }
        }

        passengerLocomotive.LoadTransferComplete = true;

        // to what stations are you going? (terminus to terminus)
        // if selected stations inbounds match any transfer passengers I have, load them until cars at capacity or no more transfer passengers

        // do you have another transfer station?
        // if so, load all passengers for which you have selected stations
    }

    private int PassengerCapacity(Car car, PassengerStop CurrentStop)
    {
        return (int)car.Definition.LoadSlots.First((LoadSlot slot) => slot.LoadRequirementsMatch(CurrentStop.passengerLoad)).MaximumCapacity;
    }
    private void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }
}