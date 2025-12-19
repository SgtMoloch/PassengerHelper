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

//non terminus
public partial class StationManager
{
    private bool RunNonTerminusStationProcedure(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<Car> coaches, List<string> orderedStopAtStations, List<string> orderedTerminusStations)
    {
        string currentStopIdentifier = CurrentStop.identifier;
        string prevStopIdentifier = passengerLocomotive.PreviousStation != null ? passengerLocomotive.PreviousStation.identifier : "";

        settings.TrainStatus.AtTerminusStationWest = false;
        settings.TrainStatus.AtTerminusStationEast = false;

        int westTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[1]);
        int eastTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[0]);
        int currentIndex = orderedStopAtStations.IndexOf(currentStopIdentifier);

        // Terminus indices must exist
        if (westTerminusIndex < 0 || eastTerminusIndex < 0)
        {
            Loader.LogError("Terminus station not found in StopAt list; pausing to avoid invalid indexing.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Terminus ordering mismatch";
            passengerLocomotive.PostNotice("ai-stop", "Paused, terminus ordering mismatch.");
            return true;
        }

        Loader.Log($"Not at either terminus station, so there are more stops");
        Loader.Log($"Checking to see if train is at station outside of terminus bounds");

        bool notAtASelectedStation = currentIndex < 0;
        if (notAtASelectedStation)
        {
            NotAtASelectedStationProcedure(passengerLocomotive, settings, CurrentStop, orderedStopAtStations, orderedTerminusStations);
            return false;
        }

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

                if (indexPrev < 0)
                {
                    Loader.Log($"Previous stop '{passengerLocomotive.PreviousStation.identifier}' not in StopAt list; cannot infer direction.");
                    // Keep UNKNOWN and let  existing "pause for unknown" logic handle it later if needed.
                }
                else if (indexPrev < currentIndex)
                {
                    Loader.Log($"Direction of Travel: WEST");
                    settings.DirectionOfTravel = DirectionOfTravel.WEST;
                }
                else if (indexPrev > currentIndex)
                {
                    Loader.Log($"Direction of Travel: EAST");
                    settings.DirectionOfTravel = DirectionOfTravel.EAST;
                }
                else
                {
                    Loader.Log("Previous stop equals current index; cannot infer direction.");
                }
            }
        }

        bool hasNextWest = currentIndex + 1 < orderedStopAtStations.Count;
        bool hasNextEast = currentIndex - 1 >= 0;

        if (settings.DirectionOfTravel != DirectionOfTravel.UNKNOWN)
        {
            HashSet<string> expectedSelectedDestinations = new(StringComparer.Ordinal);
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST && hasNextWest && westTerminusIndex >= currentIndex)
            {
                // add one to range to include terminus station
                expectedSelectedDestinations = orderedStopAtStations.GetRange(currentIndex, westTerminusIndex - currentIndex + 1).ToHashSet(StringComparer.Ordinal);

                if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == alarkaIdentifier)
                {
                    Loader.Log($"Train is at cochran, heading west and alarka was previous station. Remove Alarka from expected stations");
                    expectedSelectedDestinations.Remove(alarkaIdentifier);
                }
            }

            if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                // add one to range to include current station
                expectedSelectedDestinations = orderedStopAtStations.GetRange(0, currentIndex + 1).ToHashSet(StringComparer.Ordinal);

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
            List<string> orderedTransferStations = settings.StationSettings.Where(s => s.Value.TransferStation && s.Value.StopAtStation).Select(station => station.Key).OrderBy(id => GetOrder(orderIndex, id)).ToList();
            int numTransferStations = orderedTransferStations.Count();
            bool transferStationSelected = numTransferStations > 0;

            if (transferStationSelected)
            {
                Loader.Log($"Transfer station selected, checking direction and modifying expected selected stations");
                List<string> pickUpPassengerStations = settings.StationSettings.Where(s => s.Value.PickupPassengersForStation).Select(s => s.Key).OrderBy(id => GetOrder(orderIndex, id)).ToList();

                Loader.Log($"The following stations are pickup stations: {pickUpPassengerStations}");

                bool useNormalLogic = true;

                if (orderedTransferStations.Contains(alarkajctIdentifier))
                {
                    Loader.Log($"Train has alarkajct as a transfer station");

                    useNormalLogic = RunAlarkaJctTransferStationProcedure(settings, currentStopIdentifier, expectedSelectedDestinations, orderedStopAtStations, orderedTerminusStations, orderedTransferStations, pickUpPassengerStations);
                }

                if (useNormalLogic)
                {
                    if (settings.DirectionOfTravel == DirectionOfTravel.WEST && hasNextWest)
                    {
                        var nextStopId = orderedStopAtStations[currentIndex + 1];
                        int nextStopAtStationPickupIndex = pickUpPassengerStations.IndexOf(nextStopId);

                        if (nextStopAtStationPickupIndex >= 0)
                        {
                            Loader.Log($"Selecting pickup stations {pickUpPassengerStations.GetRange(nextStopAtStationPickupIndex, pickUpPassengerStations.Count - nextStopAtStationPickupIndex)} that are further west of the next StopAt station: {orderedStopAtStations[currentIndex + 1]}");
                            expectedSelectedDestinations.UnionWith(
                                pickUpPassengerStations.GetRange(nextStopAtStationPickupIndex, pickUpPassengerStations.Count - nextStopAtStationPickupIndex));
                        }
                        else
                        {
                            Loader.Log($"Next StopAt '{nextStopId}' is not in pickup list; skipping pickup range selection (WEST).");
                        }
                    }

                    if (settings.DirectionOfTravel == DirectionOfTravel.EAST && hasNextEast)
                    {
                        var nextStopId = orderedStopAtStations[currentIndex - 1];
                        int nextStopAtStationPickupIndex = pickUpPassengerStations.IndexOf(nextStopId);

                        if (nextStopAtStationPickupIndex >= 0)
                        {
                            Loader.Log($"Selecting pickup stations {pickUpPassengerStations.GetRange(0, nextStopAtStationPickupIndex + 1)} that are further east of the next StopAt station: {orderedStopAtStations[currentIndex - 1]}");
                            // select all to the east of the current station
                            expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(0, nextStopAtStationPickupIndex + 1));
                        }
                        else
                        {
                            Loader.Log($"Next StopAt '{nextStopId}' is not in pickup list; skipping pickup range selection (EAST).");
                        }
                    }
                }
            }

            Loader.Log($"Checking passenger cars to make sure they have the proper selected stations");
            Loader.Log($"Setting the following stations: {expectedSelectedDestinations}");

            var expectedList = expectedSelectedDestinations.ToList();

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
                        StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedList));
                    }
                }
            }

            Loader.Log($"Checking if train is in alarka or cochran and Passenger Mode to see if we need to reverse engine direction");
            bool atAlarka = currentStopIdentifier == alarkaIdentifier && !settings.TrainStatus.AtAlarka;
            bool atCochran = currentStopIdentifier == cochranIdentifier && !settings.TrainStatus.AtCochran && !orderedStopAtStations.Contains(alarkaIdentifier);

            if (!settings.StationSettings.TryGetValue(currentStopIdentifier, out var curStationSettings))
            {
                Loader.Log($"No StationSettings entry for {currentStopIdentifier}; skipping passenger mode reverse logic.");
                return false;
            }

            if (curStationSettings.PassengerMode != PassengerMode.Loop)
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
        var orderedStations = getOrderedStations();

        int currentStationIndex = orderedStations.IndexOf(CurrentStop.identifier);
        int indexWestTerminus_All = orderedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus_All = orderedStations.IndexOf(orderedTerminusStations[0]);

        if (currentStationIndex < 0 || indexWestTerminus_All < 0 || indexEastTerminus_All < 0)
        {
            Loader.Log($"Cannot correct out-of-bounds station because ordering index missing (cur={currentStationIndex}, eastT={indexEastTerminus_All}, westT={indexWestTerminus_All}).");
            return;
        }

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
                Loader.Log($"train is already going in right direction toward east terminus station {CurrentStop.identifier} -> {orderedTerminusStations[0]}");
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                if (!settings.StationSettings.TryGetValue(CurrentStop.identifier, out var curStationSettings))
                {
                    Loader.Log($"No StationSettings entry for {CurrentStop.identifier}; cannot apply loop/point-to-point behavior here.");
                    return;
                }

                Loader.Log($"train is going wrong way from east terminus, revering direction based on loop/point to point setting");
                Loader.Log($"Checking if in loop mode");

                if (curStationSettings.PassengerMode == PassengerMode.Loop)
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
                if (!settings.StationSettings.TryGetValue(CurrentStop.identifier, out var curStationSettings))
                {
                    Loader.Log($"No StationSettings entry for {CurrentStop.identifier}; cannot apply loop/point-to-point behavior here.");
                    return;
                }

                Loader.Log($"train is going wrong way from west terminus, revering direction based on loop/point to point setting");
                Loader.Log($"Checking if in loop mode");

                if (curStationSettings.PassengerMode == PassengerMode.Loop)
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
}