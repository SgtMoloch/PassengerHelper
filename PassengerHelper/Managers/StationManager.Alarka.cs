using System;
using System.Collections.Generic;
using PassengerHelper.Support;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;

//alarka
public partial class StationManager
{
    private bool RunAlarkaJctTransferStationProcedure(PassengerLocomotiveSettings pls, HashSet<string> expectedSelectedDestinations, StationProcedureContext ctx, DirectionOfTravel directionOfTravel)
    {
        int currentIndex_Pickup = ctx.PickupIndex[ctx.CurrentStation.identifier];
        int alarkaJctIndex_Pickup = ctx.PickupIndex[StationIds.AlarkaJct];
 
        bool hasAlarkaPickup = ctx.PickupIndex.TryGetValue(StationIds.Alarka, out _);
        bool hasCochranPickup = ctx.PickupIndex.TryGetValue(StationIds.Cochran, out _);

        bool hasAlarkaStopAt = ctx.StopAtIndex.TryGetValue(StationIds.Alarka, out _);
        bool hasCochranStopAt = ctx.StopAtIndex.TryGetValue(StationIds.Cochran, out _);

        bool jctIsWestTerminus = ctx.WestTerminusId == StationIds.AlarkaJct;
        bool jctIsEastTerminus = ctx.EastTerminusId == StationIds.AlarkaJct;

        bool alarkaIsWestTerminus = ctx.WestTerminusId == StationIds.Alarka;
        bool alarkaIsEastTerminus = ctx.EastTerminusId == StationIds.Alarka;

        if (jctIsWestTerminus)
        {
            // train is running from some east point <-> alarka jct
            Loader.Log($"Train has alarkajct as the west terminus station");
            // use normal logic
            return true;
        }

        if (jctIsEastTerminus)
        {
            Loader.Log($"Train has alarkajct as the east terminus station");
            if (alarkaIsWestTerminus)
            {
                // train is running jct <-> cochran <-> alarka; branch only
                Loader.Log($"Train has alarka as the west terminus station; Train is doing the alarka branch only");

                if (directionOfTravel == DirectionOfTravel.EAST)
                {
                    Loader.Log($"Train is heading East so selecting all pickup stations");
                    expectedSelectedDestinations.UnionWith(ctx.OrderedPickupStations);
                    expectedSelectedDestinations.Remove(StationIds.Alarka);
                }

                if (directionOfTravel == DirectionOfTravel.WEST)
                {
                    Loader.Log($"Train is heading West, so selecting only cochran and alarka as normal");
                }

                // this is a special case, so do not use normal logic
                return false;
            }

            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                // train is running jct <-> some west point
                // in this case, train is going east but has not reached jct yet, so if alarka and cochran are stations, need to select them since they appear between almond and jct in the ordered list
                Loader.Log($"Train is now heading East, so selecting alarka and cochran as pickup stations if needed");
                if (hasAlarkaPickup)
                {
                    Loader.Log($"adding alarka");
                    expectedSelectedDestinations.Add(StationIds.Alarka);
                }

                if (hasCochranPickup)
                {
                    Loader.Log($"adding cochran");
                    expectedSelectedDestinations.Add(StationIds.Cochran);
                }
            }

            // use normal logic in this case
            return true;
        }

        Loader.Log($"Train has a station other than alarkajct for the east terminus");

        if (alarkaIsWestTerminus)
        {
            // train is running from some east point <-> alarka
            Loader.Log($"Train has alarka as the west terminus station");
            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                // train is going east, check if train is at alarka or cochran. aka before jct
                if (currentIndex_Pickup > alarkaJctIndex_Pickup)
                {
                    // if before jct, then select all stations except alarka. aka normal logic - alarka
                    Loader.Log($"Train is heading East, selecting all pickup stations except alarka");
                    expectedSelectedDestinations.UnionWith(ctx.OrderedPickupStations);
                    expectedSelectedDestinations.Remove(StationIds.Alarka);

                    // special case, do not use normal logic
                    return false;
                }

                // if train is going east and has past jct, then just use normal logic
                Loader.Log($"Train is going east and is at or past alarkajct, using normal logic");
                return true;
            }

            if (directionOfTravel == DirectionOfTravel.WEST)
            {
                // train is going west, before jct
                if (currentIndex_Pickup < alarkaJctIndex_Pickup)
                {
                    // if before jct, use normal logic
                    Loader.Log($"Train is heading West, and is before jct, use normal logic");

                    return true;
                }

                // in the case the train has passed jct, we don't want to use normal logic
                // cannot have jct and alarka as transfer stations, they are mutually exclusive
                Loader.Log($"Train is going west, passed jct, do nothing");
                return false;
            }
        }

        // train is running from some east point <-> some west point and does not go to alarka
        Loader.Log($"Train is long distance train with no alarka branch. Checking if alarka and cochran are pickup stations");

        bool addAlarkaAndCochran = true;
        bool pickUpContainsAlarkaAndCochran = hasAlarkaPickup && hasCochranPickup;
        Loader.Log($"Alarka and cochran are pickup stations: {pickUpContainsAlarkaAndCochran}");

        bool dotEastAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.EAST;
        bool dotWestAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.WEST;

        if (pickUpContainsAlarkaAndCochran)
        {
            // this is sanity check, really shouldn't have jct as a transfer without having cochran and alarka as pickups
            Loader.Log($"Checking if train is before Alarka jct, based on current direction of travel");

            dotEastAndBeforeAlarkaJct &= currentIndex_Pickup > alarkaJctIndex_Pickup;
            dotWestAndBeforeAlarkaJct &= currentIndex_Pickup < alarkaJctIndex_Pickup;
            Loader.Log($"train is heading west towards AlarkaJct: {dotWestAndBeforeAlarkaJct}, train is heading east towards AlarkaJct: {dotEastAndBeforeAlarkaJct}");

            Loader.Log($"Ensuring that cochran and alarka are not stop at stations");
            bool stopDoesNotIncludeAlarkaAndCochran = !hasAlarkaStopAt && !hasCochranStopAt;
            Loader.Log($"cochran and alarka are not stop at stations: {stopDoesNotIncludeAlarkaAndCochran}");

            // if we haven't arrived at jct yet, we want to add alarka and cochran if they are pickup stations
            addAlarkaAndCochran &= stopDoesNotIncludeAlarkaAndCochran && (dotEastAndBeforeAlarkaJct || dotWestAndBeforeAlarkaJct);

            if (addAlarkaAndCochran)
            {
                Loader.Log($"adding alarka");
                expectedSelectedDestinations.Add(StationIds.Alarka);
                Loader.Log($"adding cochran");
                expectedSelectedDestinations.Add(StationIds.Cochran);
            }
        }

        // check here, because there can be at most 3 transfer stations. Each terminus plus jct.
        // prior to this method call, only jct was confirmed a transfer
        int transferStationCount = ctx.OrderedTransferStations.Count;

        if (transferStationCount == 1)
        {
            // if jct is the only transfer, don't use normal logic
            return false;
        }

        // otherwise, use normal logic (for example, the case where tring is bryson <-> almond where bryson almon and jct are transfer stations, use normal logic for byrson and almond)
        return true;
    }
}
