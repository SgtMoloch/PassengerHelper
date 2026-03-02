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

//terminus
public partial class StationManager
{
    private void RunTerminusStationProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        Loader.Log($"{pl._locomotive.DisplayName} reached terminus station at {ctx.CurrentStation.DisplayName}");

        bool atTerminusStationWest = ctx.WestTerminusId == state.CurrentStationId;
        bool atTerminusStationEast = ctx.EastTerminusId == state.CurrentStationId;

        Loader.LogVerbose($"at west terminus: {atTerminusStationWest} at east terminus {atTerminusStationEast}");
        Loader.LogVerbose($"passenger locomotive atTerminusWest settings: {state.AtTerminusStationWest}");
        Loader.LogVerbose($"passenger locomotive atTerminusEast settings: {state.AtTerminusStationEast}");

        EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
        DirectionOfTravel currentDOT = effectiveDOT.Value;

        Loader.LogVerbose($"Checking to see if train is approaching terminus from outside of terminus bounds");
        if (atTerminusStationEast && currentDOT == DirectionOfTravel.EAST)
        {
            // arrived at east terminus, need to flip direction
            Loader.LogVerbose($"The new direction of travel is opposite current direction of travel");
            TerminusStationReverseDirectionProcedure(pl, pls, ctx.CurrentStation.identifier);
            state.InferredDirectionOfTravel = DirectionOfTravel.WEST;
            pl.ResetDOTHandoff();
            //update local variable for calculation cost savings
            currentDOT = DirectionOfTravel.WEST;
        }
        else if (atTerminusStationWest && currentDOT == DirectionOfTravel.WEST)
        {
            // arrived at west terminus, need to flip direction
            Loader.LogVerbose($"The new direction of travel is opposite current direction of travel");
            TerminusStationReverseDirectionProcedure(pl, pls, ctx.CurrentStation.identifier);
            state.InferredDirectionOfTravel = DirectionOfTravel.EAST;
            pl.ResetDOTHandoff();
            //update local variable for calculation cost savings
            currentDOT = DirectionOfTravel.EAST;
        }
        else
        {
            Loader.LogVerbose($"The current direction of travel is the same as the new direction of travel.");
        }

        HashSet<string> expectedSelectedDestinations = new HashSet<string>(ctx.OrderedStopAtStations, StringComparer.Ordinal);

        int numTransferStations = ctx.OrderedTransferStations.Count();
        bool transferStationSelected = numTransferStations > 0;

        if (transferStationSelected)
        {
            Loader.LogVerbose($"Transfer station selected, checking direction and modifying expected selected stations");
            Loader.LogVerbose($"The following stations are pickup stations: {Dump(ctx.OrderedPickupStations)}");
            bool useNormalLogic = true;
            bool hasAlarkaJctTransfer = ctx.TransferIndex.TryGetValue(StationIds.AlarkaJct, out _);

            if (hasAlarkaJctTransfer)
            {
                Loader.LogVerbose($"Train has alarkajct as a transfer station");
                useNormalLogic = RunAlarkaJctTransferStationProcedure(pls, expectedSelectedDestinations, ctx, currentDOT);
            }

            if (useNormalLogic)
            {
                expectedSelectedDestinations.UnionWith(ComputeExpectedPickups(pl, pls, state, ctx));
            }
        }

        Loader.LogVerbose($"Setting the following stations: {Dump(expectedSelectedDestinations)}");
        var expectedList = expectedSelectedDestinations.ToList();
        foreach (Car coach in ctx.Coaches)
        {
            foreach (string identifier in expectedSelectedDestinations)
            {
                Loader.LogVerbose(string.Format("Applying {0} to car {1}", identifier, coach.DisplayName));
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedList));
        }

        state.AtTerminusStationWest = atTerminusStationWest;
        state.AtTerminusStationEast = atTerminusStationEast;
        state.TerminusStationProcedureComplete = true;

        return;
    }

    private void TerminusStationReverseDirectionProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings pls, string currentStopIdentifier)
    {
        Loader.LogVerbose($"Checking if in loop mode");

        StationSetting curStationSettings = pls.StationSettings[currentStopIdentifier];


        // if we don't want to reverse, return to original logic
        if (curStationSettings.PassengerMode == PassengerMode.Loop)
        {
            Loader.Log($"Loop mode selected; not reversing.");
            return;
        }

        Loader.Log($"Reversing direction");

        // reverse the direction of the loco
        bool reversed = pl.ReverseLocoDirection();

        if (reversed)
        {
            Say($"PH \"{Hyperlink.To(pl._locomotive)}: Reversed direction.\"");
        }
    }
}