namespace PassengerHelper.Managers;

using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Support;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Network;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.UMM;

public class StationManager
{
    internal readonly List<string> orderedStations;

    internal TrainManager trainManager;
    internal SettingsManager settingsManager;

    public StationManager(SettingsManager settingsManager, TrainManager trainManager, List<string> orderedStations)
    {
        this.orderedStations = orderedStations;
        this.trainManager = trainManager;
        this.settingsManager = settingsManager;
    }

    internal List<PassengerStop> GetPassengerStops()
    {
        return PassengerStop.FindAll()
        .Where(ps => !ps.ProgressionDisabled && orderedStations.Contains(ps.identifier))
        .OrderBy(d => orderedStations.IndexOf(d.identifier))
        .ToList();
    }

    public bool HandleTrainAtStation(BaseLocomotive locomotive, PassengerStop currentStop)
    {
        PassengerLocomotive passengerLocomotive = this.trainManager.GetPassengerLocomotive(locomotive);
        PassengerLocomotiveSettings settings = settingsManager.GetSettings(passengerLocomotive);

        if (settings.Disable)
        {
            Loader.Log($"Passenger Helper is currently disabled for " + locomotive.DisplayName + " due to disabled setting.");
            // return to original game logic
            return false;
        }

        if (settings.StationSettings.Select(s => s.Value.StopAtStation).Count() < 2 && settings.StationSettings.Select(s => s.Value.TerminusStation).Count() != 2)
        {
            Loader.Log($"Invalid settings detected. Alerting player and stopping.");
            passengerLocomotive.PostNotice("ai-stop", $"Paused, invalid settings.");
            Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Invalid settings. Check your passenger settings.\"");
        }

        if (currentStop != passengerLocomotive.CurrentStation)
        {
            Loader.Log($"Train " + locomotive.DisplayName + " has arrived at station " + currentStop.DisplayName);
            passengerLocomotive.CurrentStation = currentStop;
            settings.TrainStatus.Arrived = true;
            passengerLocomotive.ResetSettingsHash();
            passengerLocomotive.ResetStatusFlags();
            settings = settingsManager.GetSettings(passengerLocomotive);

        }

        if (settings.TrainStatus.Continue)
        {
            passengerLocomotive.ResetStoppedFlags();
            settings = settingsManager.GetSettings(passengerLocomotive);
        }

        Loader.Log($"locomotive cached settings hash: {passengerLocomotive.settingsHash}, actual setting hash: {settings.getSettingsHash()}");
        // if we have departed, cease all procedures unless a setting was changed after running the procedure
        if (settings.TrainStatus.ReadyToDepart && passengerLocomotive.settingsHash == settings.getSettingsHash())
        {
            return false;
        }

        // v2
        // is next station occupied?
        // is train en-route to current station?
        // how many tracks does current station have?
        // route appropriately

        // if train is currently Stopped
        if (IsStoppedAndShouldStayStopped(passengerLocomotive, settings))
        {
            return true;
        }

        if (passengerLocomotive.stationSettingsHash != settings.getStationSettingsHash())
        {
            if (!settings.TrainStatus.Continue)
            {
                Loader.Log($"Running Station Procedure");
                if (RunStationProcedure(passengerLocomotive, settings))
                {
                    settingsManager.SaveSettings(passengerLocomotive, settings);
                    return true;
                }
            }
        }
        else
        {
            Loader.Log($"Skipping Station Procedure");
        }

        if (passengerLocomotive.settingsHash != settings.getSettingsHash())
        {
            if (!settings.TrainStatus.Continue)
            {
                Loader.Log($"Settings have changed, checking for pause conditions");
                passengerLocomotive.ResetStoppedFlags();

                if (PauseAtCurrentStation(passengerLocomotive, settings))
                {
                    settingsManager.SaveSettings(passengerLocomotive, settings);
                    return true;
                }

                if (HaveLowFuel(passengerLocomotive, settings))
                {
                    settingsManager.SaveSettings(passengerLocomotive, settings);
                    return true;
                }
            }

            passengerLocomotive.settingsHash = settings.getSettingsHash();
        }
        else
        {
            Loader.Log($"Settings have not changed, skipping check for pause conditions");
        }

        if (!settings.TrainStatus.CurrentlyStopped)
        {
            Loader.Log($"Train {locomotive.DisplayName} is ready to depart station {currentStop.DisplayName}");
            settings.TrainStatus.ReadyToDepart = true;
            settingsManager.SaveSettings(passengerLocomotive, settings);
        }

        return settings.TrainStatus.CurrentlyStopped;
    }

    private bool IsStoppedAndShouldStayStopped(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (settings.TrainStatus.CurrentlyStopped)
        {
            Loader.Log($"Train is currently Stopped due to: {settings.TrainStatus.CurrentReasonForStop}");
            if (passengerLocomotive.ShouldStayStopped())
            {
                return true;
            }
        }

        settings.TrainStatus.CurrentlyStopped = false;
        settings.TrainStatus.CurrentReasonForStop = "";
        settingsManager.SaveSettings(passengerLocomotive, settings);
        return false;
    }

    private bool PauseAtCurrentStation(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (settings.PauseAtNextStation)
        {
            Loader.Log($"Pausing at station due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Requested pause at next station";
            settings.TrainStatus.StoppedNextStation = true;
            return true;
        }

        if (settings.StationSettings[passengerLocomotive.CurrentStation.identifier].PauseAtStation)
        {
            Loader.Log($"Pausing at {passengerLocomotive.CurrentStation.DisplayName} due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Requested pause at " + passengerLocomotive.CurrentStation.DisplayName;
            settings.TrainStatus.StoppedStationPause = true;
            return true;
        }

        if (settings.PauseAtTerminusStation && settings.StationSettings[passengerLocomotive.CurrentStation.identifier].TerminusStation == true)
        {
            Loader.Log($"Pausing at {passengerLocomotive.CurrentStation.DisplayName} due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at terminus station {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Requested pause at terminus station " + passengerLocomotive.CurrentStation.DisplayName;
            settings.TrainStatus.StoppedTerminusStation = true;
            return true;
        }

        if (settings.WaitForFullPassengersTerminusStation && settings.StationSettings[passengerLocomotive.CurrentStation.identifier].TerminusStation == true)
        {
            Loader.Log($"Waiting For full Passengers at terminus.");

            List<Car> coaches = passengerLocomotive._locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();
            foreach (Car coach in coaches)
            {
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (marker == null)
                {
                    Loader.Log($"Passenger car not full, remaining stopped");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    settings.TrainStatus.StoppedWaitForFullLoad = true;
                    return true;
                }

                LoadSlot loadSlot = coach.Definition.LoadSlots.FirstOrDefault((LoadSlot slot) => slot.RequiredLoadIdentifier == "passengers");
                int maxCapacity = (int)loadSlot.MaximumCapacity;
                PassengerMarker actualMarker = marker.Value;
                bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(passengerLocomotive.CurrentStation.identifier);
                bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
                if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                {
                    Loader.Log($"Passenger car not full, remaining stopped");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    settings.TrainStatus.StoppedWaitForFullLoad = true;
                    return true;
                }
            }

            Loader.Log($"Passengers are full, continuing.");
        }

        return false;
    }

    private bool HaveLowFuel(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        bool retVal = false;
        if (settings.PauseForDiesel)
        {
            Loader.Log($"Requested stop for low diesel, checking level");
            // check diesel
            if (passengerLocomotive.CheckDieselFuelLevel(out float diesel))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        if (settings.PauseForCoal)
        {
            Loader.Log($"Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(out float coal))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        if (settings.PauseForWater)
        {
            Loader.Log($"Requested stop for low water, checking level");
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

        List<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.IsPassengerCar()).ToList();

        List<string> orderedTerminusStations = settings.StationSettings.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.StationSettings.Where(station => station.Value.StopAtStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        Loader.Log($"Running station procedure for Train {LocomotiveName} at {CurrentStopName} with {coaches.Count()} coaches, the following selected stations: {orderedSelectedStations}, and the following terminus stations: {orderedTerminusStations}, in the following direction: {settings.DirectionOfTravel.ToString()}");

        if (orderedSelectedStations.Count < 2)
        {
            Loader.Log($"there are at least 2 stations to stop at, current selected stations: {orderedSelectedStations}");
            Say($"AI Engineer {Hyperlink.To(_locomotive)}: \"At least 2 stations must be selected. Check your passenger settings.\"");

            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Stations not selected";
            settings.TrainStatus.StoppedInsufficientStopAtStations = true;

            return true;
        }

        if (orderedTerminusStations.Count != 2)
        {
            Loader.Log($"there are not exactly 2 terminus stations, current selected terminus stations: {orderedTerminusStations}");
            Say($"AI Engineer {Hyperlink.To(_locomotive)}: \"2 Terminus stations must be selected. Check your passenger settings.\"");

            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Terminus stations not selected";
            settings.TrainStatus.StoppedInsufficientTerminusStations = true;
            return true;
        }

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        if (!orderedTerminusStations.Contains(CurrentStopIdentifier))
        {
            Loader.Log($"Not at a terminus station");

            if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && (passengerLocomotive.PreviousStation == null || passengerLocomotive.PreviousStation == CurrentStop))
            {
                string reason = "Unknown Direction of Travel";
                if (settings.TrainStatus.CurrentReasonForStop != reason)
                {
                    Loader.Log($"Train is in {CurrentStop.DisplayName}, previous stop is not known, direction of travel is unknown, so cannot accurately determine direction, pausing and waiting for manual intervention");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(CurrentStop)} until I receive Direction of Travel via PassengerSettings.\"");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                    passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = reason;

                    if (CurrentStop.identifier == "alarka")
                    {
                        settings.TrainStatus.AtAlarka = true;
                    }

                    if (CurrentStop.identifier == "cochran")
                    {
                        settings.TrainStatus.AtCochran = true;
                    }
                }

                settings.TrainStatus.StoppedUnknownDirection = true;

                return true;
            }

            bool nonTerminusStationProcedureRetVal = RunNonTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations);

            if (!nonTerminusStationProcedureRetVal)
            {
                Loader.Log($"Setting Previous stop to the current stop (not at terminus)");
                passengerLocomotive.PreviousStation = CurrentStop;

                settings.DoTLocked = true;
                settings.TrainStatus.NonTerminusStationProcedureComplete = true;
                settingsManager.SaveSettings(passengerLocomotive, settings);
            }

            // setting the previous stop on the settings changes the hash, so re-cache the settings
            passengerLocomotive.stationSettingsHash = settings.getStationSettingsHash();

            return nonTerminusStationProcedureRetVal;
        }
        else
        {
            Loader.Log($"At terminus station");

            bool atTerminusStationWest = orderedTerminusStations[1] == CurrentStopIdentifier;
            bool atTerminusStationEast = orderedTerminusStations[0] == CurrentStopIdentifier;

            Loader.Log($"at west terminus: {atTerminusStationWest} at east terminus {atTerminusStationEast}");
            Loader.Log($"passenger locomotive atTerminusWest settings: {settings.TrainStatus.AtTerminusStationWest}");
            Loader.Log($"passenger locomotive atTerminusEast settings: {settings.TrainStatus.AtTerminusStationEast}");

            // true means stay stopped, false means continue
            bool terminusStationProcedureRetVal = false;

            if (atTerminusStationWest && !settings.TrainStatus.AtTerminusStationWest)
            {
                terminusStationProcedureRetVal = RunTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations, DirectionOfTravel.EAST);

                if (!terminusStationProcedureRetVal)
                {
                    Loader.Log($"Setting Previous stop to the current stop (west terminus)");
                }
            }

            if (atTerminusStationEast && !settings.TrainStatus.AtTerminusStationEast)
            {
                terminusStationProcedureRetVal = RunTerminusStationProcedure(passengerLocomotive, settings, CurrentStop, coaches, orderedSelectedStations, orderedTerminusStations, DirectionOfTravel.WEST);

                if (!terminusStationProcedureRetVal)
                {
                    Loader.Log($"Setting Previous stop to the current stop (east terminus)");
                }
            }

            if (!terminusStationProcedureRetVal && !settings.TrainStatus.TerminusStationProcedureComplete)
            {
                passengerLocomotive.PreviousStation = CurrentStop;
                settings.TrainStatus.AtTerminusStationWest = atTerminusStationWest;
                settings.TrainStatus.AtTerminusStationEast = atTerminusStationEast;
                settings.TrainStatus.TerminusStationProcedureComplete = true;

                settings.DoTLocked = true;

                settingsManager.SaveSettings(passengerLocomotive, settings);
            }

            // setting the previous stop on the settings changes the hash, so re-cache the settings
            passengerLocomotive.stationSettingsHash = settings.getStationSettingsHash();

            return terminusStationProcedureRetVal;
        }

    }

    private bool RunNonTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<Car> coaches, List<string> orderedStopAtStations, List<string> orderedTerminusStations)
    {
        string currentStopIdentifier = CurrentStop.identifier;
        string prevStopIdentifier = passengerLocomotive.PreviousStation != null ? passengerLocomotive.PreviousStation.identifier : "";

        settings.TrainStatus.AtTerminusStationWest = false;
        settings.TrainStatus.AtTerminusStationEast = false;

        int westTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[1]);
        int eastTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[0]);
        int currentIndex = orderedStopAtStations.IndexOf(currentStopIdentifier);

        Loader.Log($"Not at either terminus station, so there are more stops");
        Loader.Log($"Checking to see if train is at station outside of terminus bounds");
        bool notAtASelectedStation = !orderedStopAtStations.Contains(currentStopIdentifier);

        if (notAtASelectedStation)
        {
            NotAtASelectedStationProcedure(passengerLocomotive, settings, CurrentStop, orderedStopAtStations, orderedTerminusStations);
        }

        string cochranIdentifier = "cochran";
        string alarkaIdentifier = "alarka";
        string almondIdentifier = "almond";
        string alarkajctIdentifier = "alarkajct";

        Loader.Log($"Direction Intelligence: Current direction of travel: {settings.DirectionOfTravel}, previousStop known: {passengerLocomotive.PreviousStation != null} previousStop: {passengerLocomotive.PreviousStation?.DisplayName}, currentStop {CurrentStop.DisplayName}");


        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN && passengerLocomotive.PreviousStation != null && passengerLocomotive.PreviousStation != CurrentStop)
        {
            Loader.Log($"Should now be able to determine which direction train is going");

            string reason = "Unknown Direction of Travel";
            if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == alarkaIdentifier && !orderedTerminusStations.Contains(alarkaIdentifier))
            {
                if (settings.TrainStatus.CurrentReasonForStop != reason)
                {
                    Loader.Log($"Train is in Cochran, previous stop was alarka, direction of travel is unknown, and alarka was not a terminus station, so cannot accurately determine direction, pausing and waiting for manual intervention");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(CurrentStop)} until I receive Direction of Travel via PassengerSettings.\"");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                    passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = reason;
                    settings.TrainStatus.AtCochran = true;
                    settings.TrainStatus.StoppedUnknownDirection = true;
                }

                return true;
            }
            else if (currentStopIdentifier == alarkaIdentifier && prevStopIdentifier == cochranIdentifier)
            {
                if (settings.TrainStatus.CurrentReasonForStop != reason)
                {
                    Loader.Log($"Train is in Alarka, previous stop was cochran, direction of travel is unknown, so cannot accurately determine direction, pausing and waiting for manual intervention");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing until at {Hyperlink.To(CurrentStop)} until I receive Direction of Travel via PassengerSettings.\"");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                    passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = reason;
                    settings.TrainStatus.AtAlarka = true;
                    settings.TrainStatus.StoppedUnknownDirection = true;
                }

                return true;
            }
            else
            {
                Loader.Log($"Determining direction of travel");
                int indexPrev = orderedStopAtStations.IndexOf(passengerLocomotive.PreviousStation.identifier);

                if (indexPrev < currentIndex)
                {
                    Loader.Log($"Direction of Travel: WEST");
                    settings.DirectionOfTravel = DirectionOfTravel.WEST;
                }
                else
                {
                    Loader.Log($"Direction of Travel: EAST");
                    settings.DirectionOfTravel = DirectionOfTravel.EAST;
                }
            }
        }

        if (settings.DirectionOfTravel != DirectionOfTravel.UNKNOWN)
        {
            HashSet<string> expectedSelectedDestinations = new();
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                // add one to range to include terminus station
                expectedSelectedDestinations = orderedStopAtStations.GetRange(currentIndex, westTerminusIndex - currentIndex + 1).ToHashSet();

                if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == alarkaIdentifier)
                {
                    Loader.Log($"Train is at cochran, heading west and alarka was previous station. Remove Alarka from expected stations");
                    expectedSelectedDestinations.Remove(alarkaIdentifier);
                }
            }

            if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                // add one to range to include current station
                expectedSelectedDestinations = orderedStopAtStations.GetRange(0, currentIndex + 1).ToHashSet();

                if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == almondIdentifier)
                {
                    Loader.Log($"Train is at cochran, heading east and almond was previous station. Add Alarka to expected stations");
                    expectedSelectedDestinations.Add(alarkaIdentifier);
                }
            }

            if (currentStopIdentifier == alarkaIdentifier && orderedStopAtStations.Contains(cochranIdentifier))
            {
                Loader.Log($"Train is at alarka, heading {settings.DirectionOfTravel.ToString()} and alarka is not a terminus. Add cochran to expected stations");
                expectedSelectedDestinations.Add(cochranIdentifier);
            }

            Loader.Log($"Expected selected stations are: {expectedSelectedDestinations}");

            // transfer station check
            List<string> orderedTransferStations = settings.StationSettings.Where(s => s.Value.TransferStation && s.Value.StopAtStation).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
            int numTransferStations = orderedTransferStations.Count();
            bool transferStationSelected = numTransferStations > 0;

            if (transferStationSelected)
            {
                Loader.Log($"Transfer station selected, checking direction and modifying expected selected stations");
                List<string> pickUpPassengerStations = settings.StationSettings.Where(s => s.Value.PickupPassengersForStation).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
                int westTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[1]);
                int eastTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[0]);

                Loader.Log($"The following stations are pickup stations: {pickUpPassengerStations}");

                bool useNormalLogic = true;

                if (orderedTransferStations.Contains(alarkajctIdentifier))
                {
                    Loader.Log($"Train has alarkajct as a transfer station");

                    useNormalLogic = RunAlarkaJctTransferStationProcedure(settings, currentStopIdentifier, expectedSelectedDestinations, orderedStopAtStations, orderedTerminusStations, orderedTransferStations, pickUpPassengerStations);
                }

                if (useNormalLogic)
                {
                    if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
                    {
                        int nextStopAtStationPickupIndex = pickUpPassengerStations.IndexOf(orderedStopAtStations[currentIndex + 1]);
                        Loader.Log($"Selecting pickup stations {pickUpPassengerStations.GetRange(nextStopAtStationPickupIndex, pickUpPassengerStations.Count - nextStopAtStationPickupIndex)} that are further west of the next StopAt station: {orderedStopAtStations[currentIndex + 1]}");
                        // select all to the west of the current station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(nextStopAtStationPickupIndex, pickUpPassengerStations.Count - nextStopAtStationPickupIndex));
                    }

                    if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
                    {
                        int nextStopAtStationPickupIndex = pickUpPassengerStations.IndexOf(orderedStopAtStations[currentIndex - 1]);
                        Loader.Log($"Selecting pickup stations {pickUpPassengerStations.GetRange(0, nextStopAtStationPickupIndex + 1)} that are further east of the next StopAt station: {orderedStopAtStations[currentIndex - 1]}");
                        // select all to the east of the current station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(0, nextStopAtStationPickupIndex + 1));
                    }
                }
            }

            Loader.Log($"Checking passenger cars to make sure they have the proper selected stations");
            Loader.Log($"Setting the following stations: {expectedSelectedDestinations}");

            foreach (Car coach in coaches)
            {
                Loader.Log($"Checking Car {coach.DisplayName}");
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (marker != null && marker.HasValue)
                {
                    PassengerMarker actualMarker = marker.Value;

                    Loader.Log($"Current selected stations are: {actualMarker.Destinations}");
                    if (!actualMarker.Destinations.SetEquals(expectedSelectedDestinations))
                    {
                        StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
                    }
                }
            }

            Loader.Log($"Checking if train is in alarka or cochran and Passenger Mode to see if we need to reverse engine direction");
            bool atAlarka = currentStopIdentifier == alarkaIdentifier && !settings.TrainStatus.AtAlarka;
            bool atCochran = currentStopIdentifier == cochranIdentifier && !settings.TrainStatus.AtCochran && !orderedStopAtStations.Contains(alarkaIdentifier);

            if (settings.StationSettings[currentStopIdentifier].PassengerMode != PassengerMode.Loop)
            {
                if (atAlarka)
                {
                    settings.TrainStatus.AtAlarka = true;
                    Loader.Log($"Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Arrived in Alarka, reversing direction to continue.\"");
                    passengerLocomotive.ReverseLocoDirection();
                }
                else
                {
                    settings.TrainStatus.AtAlarka = false;
                }

                if (atCochran)
                {
                    settings.TrainStatus.AtCochran = true;
                    Loader.Log($"Train is in Cochran, there are more stops, loop mode is not activated and alarka is not a selected station. Reversing train.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Arrived in Cochran, reversing direction to continue.\"");
                    passengerLocomotive.ReverseLocoDirection();
                }
                else
                {
                    settings.TrainStatus.AtCochran = false;
                }
            }
        }

        return false;
    }

    private bool RunTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<Car> coaches, List<string> orderedStopAtStations, List<string> orderedTerminusStations, DirectionOfTravel directionOfTravel)
    {
        Loader.Log($"{passengerLocomotive._locomotive.DisplayName} reached terminus station at {CurrentStop.DisplayName}");
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reached terminus station at {Hyperlink.To(CurrentStop)}.\"");

        Loader.Log($"Re-selecting station stops based on settings.");

        // transfer station check
        List<string> orderedTransferStations = settings.StationSettings.Where(s => s.Value.TransferStation && s.Value.StopAtStation).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        int numTransferStations = orderedTransferStations.Count();
        bool transferStationSelected = numTransferStations > 0;

        string currentStopIdentifier = CurrentStop.identifier;
        int westTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[1]);
        int eastTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[0]);
        int currentIndex = orderedStopAtStations.IndexOf(currentStopIdentifier);

        Loader.Log($"Checking to see if train is approaching terminus from outside of terminus bounds");
        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            Loader.Log($"Direction of travel is unknown");

            if (passengerLocomotive.PreviousStation == null)
            {
                Loader.Log($"train was not previously at a station.");
                Loader.Log($"Waiting for input from engineer about which direction to travel in");
                Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing until I receive Direction of Travel via PassengerSettings.\"");
                passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");

                settings.TrainStatus.CurrentlyStopped = true;
                settings.TrainStatus.CurrentReasonForStop = "At Terminus Station and have an unknown direction.";
                settings.TrainStatus.StoppedUnknownDirection = true;
                return true;
            }
            else
            {
                Loader.Log($"train was  previously at a station.");
                string prevStopId = passengerLocomotive.PreviousStation.identifier;

                Loader.Log($"Checking if previous stop {prevStopId} was inside terminus bounds");
                if (orderedStopAtStations.Contains(prevStopId))
                {
                    Loader.Log($"Previous stop was inside terminus bounds, therefore proceed with normal loop/point to point logic");
                    settings.DirectionOfTravel = directionOfTravel;
                    TerminusStationReverseDirectionProcedure(passengerLocomotive, settings, currentStopIdentifier);
                }
                else
                {
                    Loader.Log($"We arrived at the terminus station from outside the bounds, therefore we should proceed in the current direction");
                }
            }
        }
        else
        {
            Loader.Log($"Direction of travel is known");

            if (settings.DirectionOfTravel == directionOfTravel)
            {
                Loader.Log($"The current direction of travel is the same as the new direction of travel.");
            }
            else
            {
                Loader.Log($"The new direction of travel is opposite current direction of travel");

                settings.DirectionOfTravel = directionOfTravel;
                settingsManager.SaveSettings(passengerLocomotive, settings);

                TerminusStationReverseDirectionProcedure(passengerLocomotive, settings, currentStopIdentifier);
            }
        }

        string alarkajctIdentifier = "alarkajct";

        HashSet<string> expectedSelectedDestinations = orderedStopAtStations.ToHashSet();

        if (transferStationSelected)
        {
            Loader.Log($"Transfer station selected, checking direction and modifying expected selected stations");
            List<string> pickUpPassengerStations = settings.StationSettings.Where(s => s.Value.PickupPassengersForStation).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
            int westTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[1]);
            int eastTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[0]);

            Loader.Log($"The following stations are pickup stations: {pickUpPassengerStations}");
            bool useNormalLogic = true;

            if (orderedTransferStations.Contains(alarkajctIdentifier))
            {
                Loader.Log($"Train has alarkajct as a transfer station");
                useNormalLogic = RunAlarkaJctTransferStationProcedure(settings, currentStopIdentifier, expectedSelectedDestinations, orderedStopAtStations, orderedTerminusStations, orderedTransferStations, pickUpPassengerStations, directionOfTravel);
            }

            if (useNormalLogic)
            {
                if (directionOfTravel == DirectionOfTravel.WEST)
                {
                    if (orderedTransferStations.Contains(orderedTerminusStations[1]))
                    {
                        Loader.Log($"Selecting pickup stations {pickUpPassengerStations.GetRange(westTerminusIndex_Pickup, pickUpPassengerStations.Count - westTerminusIndex_Pickup)} that are further west of the west terminus station: {orderedTerminusStations[1]}");
                        // select all to the west of the west terminus station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(westTerminusIndex_Pickup, pickUpPassengerStations.Count - westTerminusIndex_Pickup));
                    }
                }

                if (directionOfTravel == DirectionOfTravel.EAST)
                {
                    if (orderedTransferStations.Contains(orderedTerminusStations[0]))
                    {
                        Loader.Log($"Selecting pickup stations {pickUpPassengerStations.GetRange(0, eastTerminusIndex_Pickup + 1)} that are further east of the east terminus station: {orderedTerminusStations[0]}");
                        // select all to the east of the east terminus station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(0, eastTerminusIndex_Pickup + 1));
                    }
                }
            }
        }

        Loader.Log($"Setting the following stations: {expectedSelectedDestinations}");

        foreach (Car coach in coaches)
        {
            foreach (string identifier in expectedSelectedDestinations)
            {
                Loader.Log(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
        }

        return false;
    }

    private void TerminusStationReverseDirectionProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, string currentStopIdentifier)
    {
        Loader.Log($"Checking if in loop mode");
        // if we don't want to reverse, return to original logic
        if (settings.StationSettings[currentStopIdentifier].PassengerMode == PassengerMode.Loop)
        {
            Loader.Log($"Loop Mode is set to true. Continuing in current direction.");
            return;
        }

        Loader.Log($"Reversing direction");
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reversing direction.\"");

        // reverse the direction of the loco
        passengerLocomotive.ReverseLocoDirection();
    }

    private void NotAtASelectedStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<string> orderedSelectedStations, List<string> orderedTerminusStations)
    {
        Loader.Log($"Train is at a station: {CurrentStop.identifier} that is not within the terminus bounds: {orderedTerminusStations}");

        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            Loader.Log($"Travel direction is unknown, so unable to correct. Continuing in the current direction and will check again at the next station");
            return;
        }

        Loader.Log($"Travel direction is known.");
        Loader.Log($"Getting direction train should continue in");

        int currentStationIndex = orderedStations.IndexOf(CurrentStop.identifier);
        int indexWestTerminus_All = orderedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus_All = orderedStations.IndexOf(orderedTerminusStations[0]);

        if (currentStationIndex > indexEastTerminus_All && currentStationIndex < indexWestTerminus_All)
        {
            Loader.Log($"station is inbounds of the terminus stations");
            return;
        }

        // if not in bounds of terminus
        if (currentStationIndex < indexEastTerminus_All)
        {
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                Loader.Log($"train is already going in right direction to east terminus station {CurrentStop.identifier} -> {orderedTerminusStations[0]}");
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                Loader.Log($"train is going wrong way from east terminus, revering direction based on loop/point to point setting");
                Loader.Log($"Checking if in loop mode");

                if (settings.StationSettings[CurrentStop.identifier].PassengerMode == PassengerMode.Loop)
                {
                    Loader.Log($"Loop Mode is set to true. Continuing in current direction.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Continuing direction to loop back to East Terminus.\"");
                }
                else
                {
                    Loader.Log($"Reversing direction");
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
                Loader.Log($"train is already going in right direction to west terminus station {CurrentStop.identifier} -> {orderedTerminusStations[1]}");
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                Loader.Log($"train is going wrong way from west terminus, revering direction based on loop/point to point setting");
                Loader.Log($"Checking if in loop mode");

                if (settings.StationSettings[CurrentStop.identifier].PassengerMode == PassengerMode.Loop)
                {
                    Loader.Log($"Loop Mode is set to true. Continuing in current direction.");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Continuing direction to loop back to West Terminus.\"");
                }
                else
                {
                    Loader.Log($"Reversing direction");
                    Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reversing direction to return back to West Terminus.\"");

                    // reverse the direction of the loco
                    passengerLocomotive.ReverseLocoDirection();
                }
            }
        }

    }

    private bool RunAlarkaJctTransferStationProcedure(PassengerLocomotiveSettings settings, string currentStopIdentifier, HashSet<string> expectedSelectedDestinations, List<string> orderedStopAtStations, List<string> orderedTerminusStations, List<string> orderedTransferStations, List<string> pickUpPassengerStations, DirectionOfTravel? directionOfTravel = null)
    {
        string cochranIdentifier = "cochran";
        string alarkaIdentifier = "alarka";
        string alarkajctIdentifier = "alarkajct";
        int currentIndex_Pickup = pickUpPassengerStations.IndexOf(currentStopIdentifier);

        bool jctIsWestTerminus = orderedTerminusStations[1] == alarkajctIdentifier;
        bool jctIsEastTerminus = orderedTerminusStations[0] == alarkajctIdentifier;

        bool alarkaIsWestTerminus = orderedTerminusStations[1] == alarkaIdentifier;
        bool alarkaIsEastTerminus = orderedTerminusStations[0] == alarkaIdentifier;

        if (directionOfTravel == null)
        {
            directionOfTravel = settings.DirectionOfTravel;
        }

        if (jctIsWestTerminus)
        {
            Loader.Log($"Train has alarkajct as the west terminus station");
            return true;
        }

        if (jctIsEastTerminus)
        {
            Loader.Log($"Train has alarkajct as the east terminus station");
            if (alarkaIsWestTerminus)
            {
                Loader.Log($"Train has alarka as the west terminus station");
                Loader.Log($"Train is doing the alarka branch only");

                if (directionOfTravel == DirectionOfTravel.EAST)
                {
                    Loader.Log($"Train is heading East so selecting all pickup stations");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);
                    expectedSelectedDestinations.Remove(alarkaIdentifier);
                }

                if (directionOfTravel == DirectionOfTravel.WEST)
                {
                    Loader.Log($"Train is heading West, so selecting only cochran and alarka as normal");
                }

                return false;
            }

            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                Loader.Log($"Train is now heading East, so selecting alarka and cochran as pickup stations if needed");
                if (pickUpPassengerStations.Contains(alarkaIdentifier))
                {
                    Loader.Log($"adding alarka");
                    expectedSelectedDestinations.Add(alarkaIdentifier);
                }

                if (pickUpPassengerStations.Contains(cochranIdentifier))
                {
                    Loader.Log($"adding cochran");
                    expectedSelectedDestinations.Add(cochranIdentifier);
                }
            }

            return true;
        }

        Loader.Log($"Train has a station other than alarkajct for the east terminus");

        if (alarkaIsWestTerminus)
        {
            Loader.Log($"Train has alarka as the west terminus station");

            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                if (currentIndex_Pickup > pickUpPassengerStations.IndexOf(alarkajctIdentifier))
                {
                    Loader.Log($"Train is heading East, selecting all pickup stations except alarka");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);
                    expectedSelectedDestinations.Remove(alarkaIdentifier);

                    return false;
                }

                Loader.Log($"Train is going east and is at or past alarkajct, using normal logic");
                return true;
            }

            if (directionOfTravel == DirectionOfTravel.WEST)
            {
                if (currentIndex_Pickup < pickUpPassengerStations.IndexOf(alarkajctIdentifier))
                {
                    Loader.Log($"Train is heading West, selecting all pickup stations");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);

                    return false;
                }

                Loader.Log($"Train is going west, using normal logic if at or after alarka jct");
                return true;
            }
        }

        Loader.Log($"Train is long distance train with no alarka branch.");

        bool addAlarkaAndCochran = true;

        Loader.Log($"Checking if alarka and cochran are pickup stations");
        bool pickUpContainsAlarkaAndCochran = pickUpPassengerStations.Contains(alarkaIdentifier) && pickUpPassengerStations.Contains(cochranIdentifier);
        Loader.Log($"Alarka and cochran are pickup stations: {pickUpContainsAlarkaAndCochran}");

        bool dotEastAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.EAST;
        bool dotWestAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.WEST;

        if (pickUpContainsAlarkaAndCochran)
        {
            Loader.Log($"Checking if train is before Alarka jct, based on current direction of travel");
            int alarkaJctIndex_Pickup = pickUpPassengerStations.IndexOf(alarkajctIdentifier);
            dotEastAndBeforeAlarkaJct &= currentIndex_Pickup > alarkaJctIndex_Pickup;
            dotWestAndBeforeAlarkaJct &= currentIndex_Pickup < alarkaJctIndex_Pickup;
            Loader.Log($"train is before Alarka jct going west: {dotWestAndBeforeAlarkaJct}, train is before Alarka jct going east: {dotEastAndBeforeAlarkaJct}");

            Loader.Log($"Ensuring that cochran and alarka are not stop at stations");
            bool stopDoesNotIncludeAlarkaAndCochran = !orderedStopAtStations.Contains(alarkaIdentifier) && !orderedStopAtStations.Contains(cochranIdentifier);
            Loader.Log($"cochran and alarka are not stop at stations: {stopDoesNotIncludeAlarkaAndCochran}");

            addAlarkaAndCochran &= stopDoesNotIncludeAlarkaAndCochran && (dotEastAndBeforeAlarkaJct || dotWestAndBeforeAlarkaJct);

            if (addAlarkaAndCochran)
            {
                Loader.Log($"adding alarka");
                expectedSelectedDestinations.Add(alarkaIdentifier);
                Loader.Log($"adding cochran");
                expectedSelectedDestinations.Add(cochranIdentifier);
            }
        }

        int transferStationCount = orderedTransferStations.Count;

        if (transferStationCount == 1)
        {
            return false;
        }

        return true;
    }

    private void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }
}