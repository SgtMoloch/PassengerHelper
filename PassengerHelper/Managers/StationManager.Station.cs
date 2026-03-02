using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;

//non terminus
public partial class StationManager
{
    private void RunStationProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        state.AtTerminusStationWest = false;
        state.AtTerminusStationEast = false;

        int currentIndex = ctx.StopAtIndex[ctx.CurrentStation.identifier];

        HashSet<string> expectedSelectedDestinations = ComputeExpectedDestinations(pl, pls, state, ctx);

        Loader.Log($"Expected selected stations are: {Dump(expectedSelectedDestinations)}");

        // transfer station check
        int numTransferStations = ctx.OrderedTransferStations.Count();
        bool transferStationSelected = numTransferStations > 0;

        if (transferStationSelected)
        {
            Loader.Log($"Transfer station selected, checking direction and modifying expected selected stations. The following stations are pickup stations: {Dump(ctx.OrderedPickupStations)}");

            bool useNormalLogic = true;
            bool hasAlarkaJctTransfer = ctx.TransferIndex.TryGetValue(StationIds.AlarkaJct, out _);

            if (hasAlarkaJctTransfer)
            {
                Loader.Log($"Train has alarkajct as a transfer station");
                useNormalLogic = RunAlarkaJctTransferStationProcedure(pls, expectedSelectedDestinations, ctx, DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel).Value);
            }

            if (useNormalLogic)
            {
                expectedSelectedDestinations.UnionWith(ComputeExpectedPickups(pl, pls, state, ctx));
            }
        }

        Loader.Log($"Checking passenger cars to make sure they have the proper selected stations");
        Loader.Log($"Setting the following stations: {expectedSelectedDestinations}");

        var expectedList = expectedSelectedDestinations.ToList();

        foreach (Car coach in ctx.Coaches)
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
        if (pls.StationSettings[ctx.CurrentStation.identifier].PassengerMode != PassengerMode.Loop)
        {
            // we are in point to point mode at the current station
            if (state.AtAlarka)
            {
                // train is at alarka, and this is not a terminus, so we need to reverse direction but NOT change cardinal direction
                Loader.Log($"Train is in Alarka, there are more stops, and loop mode is not activated. Reversing train.");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: Arrived in Alarka, reversing direction to continue.\"");
                pl.ReverseLocoDirection();
            }

            if (state.AtCochran && !ctx.OrderedStopAtStations.Contains(StationIds.Alarka))
            {
                // train is at cochran, train does not go to alarka, there are more stops, we need to reverse direction but NOT change cardinal direction
                Loader.Log($"Train is in Cochran, there are more stops, loop mode is not activated and alarka is not a selected station. Reversing train.");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: Arrived in Cochran, reversing direction to continue.\"");
                pl.ReverseLocoDirection();
            }
        }

        state.NonTerminusStationProcedureComplete = true;

        return;
    }

    private void NotAtASelectedStationProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, PassengerStop currentStation, Dictionary<string, int> orderIndex, List<string> orderedTerminusStations)
    {
        Loader.Log($"[StationManager::NotAtASelectedStationProcedure] Train is at a station: {currentStation.identifier} that is not within the terminus bounds: {Dump(orderedTerminusStations)}");

        EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
        if (effectiveDOT.Value == DirectionOfTravel.UNKNOWN)
        {
            Loader.Log($"[StationManager::NotAtASelectedStationProcedure] Travel direction is unknown, so unable to correct. Continuing in the current direction and will check again at the next station");
            return;
        }

        Loader.Log($"Travel direction is known.");
        Loader.Log($"Getting direction train should continue in");

        bool goodCurr = orderIndex.TryGetValue(currentStation.identifier, out int currentStationIndex_All);
        bool goodWest = orderIndex.TryGetValue(orderedTerminusStations[1], out int indexWestTerminus_All);
        bool goodEast = orderIndex.TryGetValue(orderedTerminusStations[0], out int indexEastTerminus_All);

        if (!goodCurr || !goodWest || !goodEast)
        {
            Loader.Log($"[StationManager::NotAtASelectedStationProcedure] Cannot correct out-of-bounds station because ordering index missing (cur={currentStationIndex_All}, eastTerminus={indexEastTerminus_All}, westTerminus={indexWestTerminus_All}).");
            return;
        }

        // if to the east of the east terminus
        if (currentStationIndex_All < indexEastTerminus_All)
        {
            // going west, towards east terminus, do nothing
            if (effectiveDOT.Value == DirectionOfTravel.WEST)
            {
                Loader.Log($"[StationManager::NotAtASelectedStationProcedure] train is already going in right direction toward east terminus station {currentStation.identifier} -> {orderedTerminusStations[0]}");
            }
            // if going east, need to switch direction of travel to go west.
            else if (effectiveDOT.Value == DirectionOfTravel.EAST)
            {
                Loader.Log($"[StationManager::NotAtASelectedStationProcedure] train is going wrong way from east terminus, revering direction");
                state.InferredDirectionOfTravel = DirectionOfTravel.WEST;
                pl.ReverseLocoDirection();
            }

            return;
        }

        // if further west of the west terminus
        if (currentStationIndex_All > indexWestTerminus_All)
        {
            // if going east, direction is correct, do nothing
            if (effectiveDOT.Value == DirectionOfTravel.EAST)
            {
                Loader.Log($"[StationManager::NotAtASelectedStationProcedure] train is already going in right direction to west terminus station {currentStation.identifier} -> {orderedTerminusStations[1]}");
            }
            // if going west, need to swicth direction of travel to go east
            else if (effectiveDOT.Value == DirectionOfTravel.WEST)
            {
                Loader.Log($"[StationManager::NotAtASelectedStationProcedure] train is going wrong way from west terminus, revering direction");
                state.InferredDirectionOfTravel = DirectionOfTravel.EAST;
                pl.ReverseLocoDirection();
            }

            return;
        }

        state.NonTerminusStationProcedureComplete = true;

        return;
    }
}