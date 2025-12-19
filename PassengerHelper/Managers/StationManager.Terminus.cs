using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

//terminus
public partial class StationManager
{
    private bool RunTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<Car> coaches, List<string> orderedStopAtStations, List<string> orderedTerminusStations, DirectionOfTravel directionOfTravel)
    {
        var orderIndex = BuildOrderIndex();

        Loader.Log($"{passengerLocomotive._locomotive.DisplayName} reached terminus station at {CurrentStop.DisplayName}");
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reached terminus station at {Hyperlink.To(CurrentStop)}.\"");

        Loader.Log($"Re-selecting station stops based on settings.");

        // transfer station check
        List<string> orderedTransferStations = settings.StationSettings
            .Where(s => s.Value.TransferStation && s.Value.StopAtStation)
            .Select(station => station.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        int numTransferStations = orderedTransferStations.Count();
        bool transferStationSelected = numTransferStations > 0;

        string currentStopIdentifier = CurrentStop.identifier;

        Loader.Log($"Checking to see if train is approaching terminus from outside of terminus bounds");
        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            Loader.Log($"Direction of travel is unknown");

            if (passengerLocomotive.PreviousStation == null)
            {
                Loader.Log($"train was not previously at a station. Waiting for input from engineer about which direction to travel in");
                Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Unknown Direction. Pausing until I receive Direction of Travel via PassengerSettings.\"");
                passengerLocomotive.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(CurrentStop)}.");

                settings.TrainStatus.CurrentlyStopped = true;
                settings.TrainStatus.CurrentReasonForStop = "At Terminus Station and have an unknown direction.";
                settings.TrainStatus.StoppedUnknownDirection = true;
                return true;
            }
            else
            {
                string prevStopId = passengerLocomotive.PreviousStation.identifier;

                Loader.Log($"train was  previously at a station. Checking if previous stop {prevStopId} was inside terminus bounds");
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

        HashSet<string> expectedSelectedDestinations = new HashSet<string>(orderedStopAtStations, StringComparer.Ordinal);

        if (transferStationSelected)
        {
            Loader.Log($"Transfer station selected, checking direction and modifying expected selected stations");
            List<string> pickUpPassengerStations = settings.StationSettings.Where(s => s.Value.PickupPassengersForStation).Select(s => s.Key).OrderBy(id => GetOrder(orderIndex, id)).ToList();
            int westTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[1]);
            int eastTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[0]);

            if (westTerminusIndex_Pickup < 0 || eastTerminusIndex_Pickup < 0)
            {
                Loader.LogError("Terminus station not found in orderedPickUpStations; pausing to avoid bad selection.");
                settings.TrainStatus.CurrentlyStopped = true;
                settings.TrainStatus.CurrentReasonForStop = "Station ordering mismatch";
                passengerLocomotive.PostNotice("ai-stop", "Paused, station ordering mismatch.");
                return true;
            }

            Loader.Log($"The following stations are pickup stations: {Dump(pickUpPassengerStations)}");
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
                        Loader.Log($"Selecting pickup stations {Dump(pickUpPassengerStations.GetRange(westTerminusIndex_Pickup, pickUpPassengerStations.Count - westTerminusIndex_Pickup))} that are further west of the west terminus station: {orderedTerminusStations[1]}");
                        // select all to the west of the west terminus station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(westTerminusIndex_Pickup, pickUpPassengerStations.Count - westTerminusIndex_Pickup));
                    }
                }

                if (directionOfTravel == DirectionOfTravel.EAST)
                {
                    if (orderedTransferStations.Contains(orderedTerminusStations[0]))
                    {
                        Loader.Log($"Selecting pickup stations {Dump(pickUpPassengerStations.GetRange(0, eastTerminusIndex_Pickup + 1))} that are further east of the east terminus station: {orderedTerminusStations[0]}");
                        // select all to the east of the east terminus station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(0, eastTerminusIndex_Pickup + 1));
                    }
                }
            }
        }

        Loader.Log($"Setting the following stations: {Dump(expectedSelectedDestinations)}");
        var expectedList = expectedSelectedDestinations.ToList();
        foreach (Car coach in coaches)
        {
            foreach (string identifier in expectedSelectedDestinations)
            {
                Loader.LogVerbose(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedList));
        }

        return false;
    }

    private void TerminusStationReverseDirectionProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, string currentStopIdentifier)
    {
        Loader.Log($"Checking if in loop mode");
        if (!settings.StationSettings.TryGetValue(currentStopIdentifier, out var curStationSettings))
        {
            Loader.Log($"No StationSettings entry for {currentStopIdentifier}; skipping passenger mode reverse logic.");
            return;
        }
            
        // if we don't want to reverse, return to original logic
        if (curStationSettings.PassengerMode == PassengerMode.Loop)
        {
            Loader.Log($"Loop mode selected; not reversing.");
            return;
        }

        Loader.Log($"Reversing direction");
        Say($"AI Engineer {Hyperlink.To(passengerLocomotive._locomotive)}: \"Reversing direction.\"");

        // reverse the direction of the loco
        passengerLocomotive.ReverseLocoDirection();
    }
}