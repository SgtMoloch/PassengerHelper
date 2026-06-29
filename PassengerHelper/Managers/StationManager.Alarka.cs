using System;
using System.Collections.Generic;
using PassengerHelper.Support;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;

//alarka
public partial class StationManager
{
    private bool RunAlarkaJctTransferStationProcedure(HashSet<string> expectedSelectedDestinations, StationProcedureContext ctx, DirectionOfTravel directionOfTravel)
    {
        int currentIndex_Pickup = ctx.PickupIndex[ctx.CurrentStation.identifier];
        int alarkaJctIndex_Pickup = ctx.PickupIndex[StationIds.AlarkaJct];

        bool currentStationIsJct = ctx.CurrentStation.identifier == StationIds.AlarkaJct;

        bool hasAlarkaPickup = ctx.PickupIndex.TryGetValue(StationIds.Alarka, out _);
        bool hasCochranPickup = ctx.PickupIndex.TryGetValue(StationIds.Cochran, out _);

        bool hasAlarkaStopAt = ctx.StopAtIndex.TryGetValue(StationIds.Alarka, out _);
        bool hasCochranStopAt = ctx.StopAtIndex.TryGetValue(StationIds.Cochran, out _);

        bool jctIsWestTerminus = ctx.WestTerminusId == StationIds.AlarkaJct;
        bool jctIsEastTerminus = ctx.EastTerminusId == StationIds.AlarkaJct;

        bool alarkaIsWestTerminus = ctx.WestTerminusId == StationIds.Alarka;

        bool alarkaBranchOnly = jctIsEastTerminus && alarkaIsWestTerminus;
        bool jctIsTerminus = jctIsWestTerminus || jctIsEastTerminus;

        bool mainlineOnlyNoBranchStop = !hasCochranStopAt && !hasAlarkaStopAt;

        if (alarkaBranchOnly)
        {
            // most common case
            Loader.LogVerbose($"Train is doing the alarka branch only");
            if (currentStationIsJct || directionOfTravel == DirectionOfTravel.WEST)
            {
                Loader.LogVerbose($"Train is at JCT or heading West on branch-only route");

                // special case, do not use normal logic
                return false;
            }

            Loader.LogVerbose($"Train is heading East to Jct so selecting all pickup stations except alarka");
            expectedSelectedDestinations.UnionWith(ctx.OrderedPickupStations);
            expectedSelectedDestinations.Remove(StationIds.Alarka);

            // special case, do not use normal logic
            return false;
        }

        if (jctIsTerminus)
        {
            // if train has arrived at jct and jct is terminus, direction is now the new direction before this gets called
            // so just always use normal logic
            Loader.LogVerbose($"Train has route with Alarka JCT as terminus");
            if (currentStationIsJct && jctIsEastTerminus)
            {
                Loader.LogVerbose("Train is at JCT and JCT is EAST terminus; selecting pickups west of JCT except Alarka/Cochran");

                if (currentIndex_Pickup + 1 < ctx.OrderedPickupStations.Count)
                {
                    expectedSelectedDestinations.UnionWith(
                        ctx.OrderedPickupStations.GetRange(
                            currentIndex_Pickup + 1,
                            ctx.OrderedPickupStations.Count - (currentIndex_Pickup + 1)));
                }

                expectedSelectedDestinations.Remove(StationIds.Alarka);
                expectedSelectedDestinations.Remove(StationIds.Cochran);

                // special case, do not use normal logic
                return false;
            }

            // this is true, because JCT is a terminus, so we want all stations selected based on the direction of travel.
            return true;
        }

        if (mainlineOnlyNoBranchStop)
        {
            // train is running from some east point <-> some west point and does not go to alarka
            Loader.LogVerbose($"Train is mainline train with no alarka branch.");

            Loader.LogVerbose($"Alarka is pickup station: {hasAlarkaPickup}");
            Loader.LogVerbose($"Cochran is pickup station: {hasCochranPickup}");

            bool beforeJct =
            (directionOfTravel == DirectionOfTravel.WEST && currentIndex_Pickup < alarkaJctIndex_Pickup) ||
            (directionOfTravel == DirectionOfTravel.EAST && currentIndex_Pickup > alarkaJctIndex_Pickup);

            if (beforeJct)
            {
                if (hasAlarkaPickup)
                {
                    expectedSelectedDestinations.Add(StationIds.Alarka);
                }

                if (hasCochranPickup)
                {
                    expectedSelectedDestinations.Add(StationIds.Cochran);
                }
            }

            // use normal logic (for example, the case where route is bryson <-> almond where bryson almond and jct are all transfer stations, use normal logic for bryson and almond)
            Loader.LogVerbose($"Mainline train, using normal transfer pass pickup logic if needed");
            return ShouldRunNormalTransferPickupLogic(ctx);
        }

        if (alarkaIsWestTerminus)
        {
            // train has Alarka as west terminus, with JCT as an intermediate transfer point
            Loader.LogVerbose($"Train has alarka as the west terminus station");
            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                // train is going east, check if train is at alarka or cochran. aka before jct
                if (currentIndex_Pickup > alarkaJctIndex_Pickup)
                {
                    // if before jct, then select all stations except alarka. aka normal logic - alarka
                    Loader.LogVerbose($"Train is heading East, selecting all pickup stations except alarka");
                    expectedSelectedDestinations.UnionWith(ctx.OrderedPickupStations);
                    expectedSelectedDestinations.Remove(StationIds.Alarka);

                    // special case, do not use normal logic
                    return false;
                }

                // if train is going east and is at or past jct, then just use normal transfer logic if needed
                Loader.LogVerbose($"Train is going east and is at or past alarkajct, using normal transfer pass pickup logic if needed");
                return ShouldRunNormalTransferPickupLogic(ctx);
            }

            if (directionOfTravel == DirectionOfTravel.WEST)
            {
                // train is going west, before jct
                if (currentIndex_Pickup < alarkaJctIndex_Pickup)
                {
                    // if before jct, use normal logic
                    Loader.LogVerbose($"Train is heading West, and is before jct, using normal transfer pass pickup logic");

                    return true;
                }

                // in the case the train has passed jct, we don't want to use normal logic
                // cannot have jct and alarka as transfer stations, they are mutually exclusive
                Loader.LogVerbose($"Train is going west, passed jct, do nothing");
                return false;
            }
        }

        // check here, because there can be at most 3 transfer stations. Each terminus plus jct.
        // prior to this method call, only jct was confirmed a transfer
        // if jct is the only transfer, don't use normal logic
        // otherwise, use normal logic (for example, the case where train is bryson <-> almond where bryson almond and jct are transfer stations, use normal logic for bryson and almond)
        Loader.LogVerbose($"Weird condition encountered. JCT is transfer station, but it is not a terminus, not part of a mainline, not part of the alarka branch and Alarka is not the west terminus. Using normal logic if needed");
        return ShouldRunNormalTransferPickupLogic(ctx);
    }

    private bool ShouldRunNormalTransferPickupLogic(StationProcedureContext ctx)
    {
        return ctx.OrderedTransferStations.Count > 1;
    }
}
