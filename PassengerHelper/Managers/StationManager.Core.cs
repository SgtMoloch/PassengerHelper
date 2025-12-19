using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

// core
public partial class StationManager
{
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

        int stopCount = settings.StationSettings.Values.Count(v => v.StopAtStation);
        int terminusCount = settings.StationSettings.Values.Count(v => v.TerminusStation);

        if (stopCount < 2 || terminusCount != 2)
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

    private Dictionary<string, int> orderIndex = new(StringComparer.Ordinal);

    private bool RunStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        orderIndex = BuildOrderIndex();

        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStation;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;

        List<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.IsPassengerCar()).ToList();

        List<string> orderedTerminusStations = settings.StationSettings
            .Where(kvp => kvp.Value.TerminusStation)
            .Select(kvp => kvp.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        List<string> orderedSelectedStations = settings.StationSettings
            .Where(kvp => kvp.Value.StopAtStation)
            .Select(kvp => kvp.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        Loader.Log($"Running station procedure for Train {LocomotiveName} at {CurrentStopName} with {coaches.Count()} coaches, the following selected stations: {orderedSelectedStations}, and the following terminus stations: {orderedTerminusStations}, in the following direction: {settings.DirectionOfTravel.ToString()}");

        if (orderedSelectedStations.Count < 2)
        {
            Loader.Log($"there are less than 2 stations to stop at, current selected stations: {orderedSelectedStations}");
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
}