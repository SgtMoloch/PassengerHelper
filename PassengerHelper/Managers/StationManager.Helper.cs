using System;
using System.Collections.Generic;
using System.Reflection;
using Model;
using Model.Ops;
using Network;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;

//helper
public partial class StationManager
{
    public void ArmDepartureCheck(PassengerLocomotive pl)
    {
        _armedDepartures.Add(pl._locomotive.id);
        pl.StartAE();
    }

    public void DisarmDepartureCheck(PassengerLocomotive pl)
    {
        _armedDepartures.Remove(pl._locomotive.id);
        pl.StopAE();
    }

    private MethodInfo carCapacity = typeof(PassengerStop).GetMethod("PassengerCapacity", BindingFlags.NonPublic | BindingFlags.Instance);

    public int PassengerCapacity(Car car, PassengerStop ps)
    {
        return (int)carCapacity.Invoke(ps, new object[] { car });
    }

    private Dictionary<string, int> BuildOrderIndex(List<string> orderedStations)
    {
        Loader.Log($"StationManager: BuildOrderIndex for {Dump(orderedStations)}");

        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < orderedStations.Count; i++)
            dict[orderedStations[i]] = i;
        return dict;
    }

    private int GetOrder(Dictionary<string, int> idx, string id) => idx.TryGetValue(id, out var v) ? v : int.MaxValue;

    private void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }

    private static string Dump(IEnumerable<string> ids) => ids == null ? "<null>" : string.Join(", ", ids);

    private HashSet<string> ComputeExpectedDestinations(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);

        int currentIndex = ctx.StopAtIndex[ctx.CurrentStation.identifier];
        int westTerminusIndex = ctx.StopAtIndex[ctx.WestTerminusId];
        int eastTerminusIndex = ctx.StopAtIndex[ctx.EastTerminusId];

        EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
        //effectiveDOT should never be unknown here
        if (effectiveDOT.Value == DirectionOfTravel.UNKNOWN)
        {
            Loader.LogError("ComputeExpectedDestinations called with UNKNOWN DOT (should be impossible here).");
            return expected;
        }

        // currentIndex should never be the terminus index because prior to this being called, the handle train at station would have determined that the train is at a terminus.
        // so can assume safely that this is only ever used for Stations and not terminus stations
        if (effectiveDOT.Value == DirectionOfTravel.WEST)
        {
            if (westTerminusIndex > currentIndex)
            {
                expected.UnionWith(ctx.OrderedStopAtStations.GetRange(
                    currentIndex, westTerminusIndex - currentIndex + 1));
            }

            if (state.CurrentStationId == cochranIdentifier && state.PreviousStationId == alarkaIdentifier)
            {
                expected.Remove(alarkaIdentifier);
            }
        }
        else if (effectiveDOT.Value == DirectionOfTravel.EAST)
        {
            if (eastTerminusIndex < currentIndex)
            {
                expected.UnionWith(ctx.OrderedStopAtStations.GetRange(0, currentIndex + 1));
            }

            if (state.CurrentStationId == cochranIdentifier && state.PreviousStationId == almondIdentifier)
            {
                expected.Add(alarkaIdentifier);
            }
        }

        if (state.CurrentStationId == alarkaIdentifier && ctx.OrderedStopAtStations.Contains(cochranIdentifier))
        {
            expected.Add(cochranIdentifier);
        }

        return expected;
    }

    private HashSet<string> ComputeExpectedPickups(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);

        int currentIndex_Pickup = ctx.PickupIndex[ctx.CurrentStation.identifier];

        int westTerminusIndex_Pickup = ctx.PickupIndex[ctx.WestTerminusId];
        int eastTerminusIndex_Pickup = ctx.PickupIndex[ctx.EastTerminusId];

        bool atWestTerminus = currentIndex_Pickup == westTerminusIndex_Pickup;
        bool atEastTerminus = currentIndex_Pickup == eastTerminusIndex_Pickup;

        EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
        //effectiveDOT should never be unknown here
        if (effectiveDOT.Value == DirectionOfTravel.UNKNOWN)
        {
            Loader.LogError("ComputeExpectedPickups called with UNKNOWN DOT (should be impossible here).");
            return expected;
        }

        if (effectiveDOT.Value == DirectionOfTravel.WEST)
        {
            if (atEastTerminus && ctx.OrderedTransferStations.Contains(ctx.WestTerminusId))
            {
                Loader.Log($"Selecting pickup stations {Dump(ctx.OrderedPickupStations.GetRange(westTerminusIndex_Pickup, ctx.OrderedPickupStations.Count - westTerminusIndex_Pickup))} that are further west of the west terminus station: {ctx.WestTerminusId}");
                // select all to the west of the west terminus station
                expected.UnionWith(ctx.OrderedPickupStations.GetRange(westTerminusIndex_Pickup, ctx.OrderedPickupStations.Count - westTerminusIndex_Pickup));

            }
            else if (currentIndex_Pickup + 1 < ctx.OrderedPickupStations.Count)
            {
                Loader.Log($"Selecting pickup stations {Dump(ctx.OrderedPickupStations.GetRange(currentIndex_Pickup + 1, ctx.OrderedPickupStations.Count - (currentIndex_Pickup + 1)))} that are further west of the current station: {ctx.CurrentStation.DisplayName}");
                expected.UnionWith(
                    ctx.OrderedPickupStations.GetRange(currentIndex_Pickup + 1, ctx.OrderedPickupStations.Count - (currentIndex_Pickup + 1)));
            }
            else
            {
                Loader.Log($"skipping pickup range selection (WEST) due to next stop being past the end of the pickup list (currIndex + 1 > PickupStationCount)");
            }
        }
        else if (effectiveDOT.Value == DirectionOfTravel.EAST)
        {
            if (atWestTerminus && ctx.OrderedTransferStations.Contains(ctx.EastTerminusId))
            {
                Loader.Log($"Selecting pickup stations {Dump(ctx.OrderedPickupStations.GetRange(0, eastTerminusIndex_Pickup + 1))} that are further east of the east terminus station: {ctx.EastTerminusId}");
                // select all to the east of the east terminus station
                expected.UnionWith(ctx.OrderedPickupStations.GetRange(0, eastTerminusIndex_Pickup + 1));
            }
            else if (currentIndex_Pickup - 1 >= 0)
            {
                Loader.Log($"Selecting pickup stations {Dump(ctx.OrderedPickupStations.GetRange(0, currentIndex_Pickup))} that are further east of the current station: {ctx.CurrentStation.DisplayName}");
                expected.UnionWith(ctx.OrderedPickupStations.GetRange(0, currentIndex_Pickup));
            }
            else
            {
                Loader.Log($"skipping pickup range selection (EAST) due to next stop being past the end of the pickup list (currIndex - 1 < 0)");
            }
        }

        return expected;
    }

    private bool TryInferDirection(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);

        if (effectiveDOT.Value != DirectionOfTravel.UNKNOWN)
        {
            return true;
        }

        string reason = "Unknown Direction of Travel";

        if (!ctx.OrderIndex.TryGetValue(state.PreviousStationId, out var prev))
        {
            PauseUnknownStation(pl, pls, state, reason, ctx);

            return false;
        }

        if (!ctx.OrderIndex.TryGetValue(state.CurrentStationId, out var cur))
        {
            PauseUnknownStation(pl, pls, state, reason, ctx);

            return false;
        }

        if (prev == cur)
        {
            PauseSameStation(pl, pls, state, reason, ctx);

            return false;
        }

        if (PauseAlakraStation(pl, pls, state, reason, ctx))
        {
            return false;
        }

        state.InferredDirectionOfTravel = (cur > prev) ? DirectionOfTravel.WEST : DirectionOfTravel.EAST;
        return true;
    }

    private void PauseUnknownStation(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, string reason, StationProcedureContext ctx)
    {
        if (state.CurrentReasonForStop != reason)
        {
            Loader.Log($"Train is at an unknown station, so cannot accurately determine direction, pausing and waiting for manual intervention");
            Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(ctx.CurrentStation)} until I receive Direction of Travel via PassengerSettings.\"");
            Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
            pl.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(ctx.CurrentStation)}.");
            state.CurrentlyStopped = true;
            state.CurrentReasonForStop = reason;
            state.StoppedUnknownDirection = true;
            state.InferredDirectionOfTravel = DirectionOfTravel.UNKNOWN;
        }
    }

    private void PauseSameStation(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, string reason, StationProcedureContext ctx)
    {
        if (state.CurrentReasonForStop != reason)
        {
            Loader.Log($"Current and Previous stations are the same, direction of travel is unknown, so cannot accurately determine direction, pausing and waiting for manual intervention");
            Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Pausing until at {Hyperlink.To(ctx.CurrentStation)} until I receive Direction of Travel via PassengerSettings.\"");
            Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
            pl.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(ctx.CurrentStation)}.");
            state.CurrentlyStopped = true;
            state.CurrentReasonForStop = reason;
            state.StoppedUnknownDirection = true;
            state.InferredDirectionOfTravel = DirectionOfTravel.UNKNOWN;
        }
    }

    private bool PauseAlakraStation(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, string reason, StationProcedureContext ctx)
    {
        if (ctx.CurrentStation.identifier == cochranIdentifier && state.PreviousStationId == alarkaIdentifier && !ctx.OrderedTerminusStations.Contains(alarkaIdentifier))
        {
            if (state.CurrentReasonForStop != reason)
            {
                Loader.Log($"Train is in Cochran, previous stop was alarka, direction of travel is unknown, and alarka was not a terminus station, so cannot accurately determine direction, pausing and waiting for manual intervention");
                Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(ctx.CurrentStation)} until I receive Direction of Travel via PassengerSettings.\"");
                Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                pl.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(ctx.CurrentStation)}.");
                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
                state.StoppedUnknownDirection = true;
                state.InferredDirectionOfTravel = DirectionOfTravel.UNKNOWN;
            }

            return true;
        }
        else if (ctx.CurrentStation.identifier == alarkaIdentifier && state.PreviousStationId == cochranIdentifier)
        {
            if (state.CurrentReasonForStop != reason)
            {
                Loader.Log($"Train is in Alarka, previous stop was cochran, direction of travel is unknown, so cannot accurately determine direction, pausing and waiting for manual intervention");
                Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Pausing at {Hyperlink.To(ctx.CurrentStation)} until I receive Direction of Travel via PassengerSettings.\"");
                Say($"AI Engineer {Hyperlink.To(pl._locomotive)}: \"Be sure to put the reverser in the correct direction too. Else I might go in the wrong direction.\"");
                pl.PostNotice("ai-stop", $"Paused, Unknown Direction at {Hyperlink.To(ctx.CurrentStation)}.");
                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
                state.StoppedUnknownDirection = true;
                state.InferredDirectionOfTravel = DirectionOfTravel.UNKNOWN;
            }

            return true;
        }

        return false;
    }
}
