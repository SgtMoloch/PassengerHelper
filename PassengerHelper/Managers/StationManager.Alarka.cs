using System;
using System.Collections.Generic;
using PassengerHelper.Support;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

//alarka
public partial class StationManager
{
    private bool RunAlarkaJctTransferStationProcedure(PassengerLocomotiveSettings settings, string currentStopIdentifier, HashSet<string> expectedSelectedDestinations, List<string> orderedStopAtStations, List<string> orderedTerminusStations, List<string> orderedTransferStations, List<string> pickUpPassengerStations, DirectionOfTravel? directionOfTravel = null)
    {
        var pickupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pickUpPassengerStations.Count; i++)
            pickupIndex[pickUpPassengerStations[i]] = i;

        bool TryPickupIndex(string id, out int idx) => pickupIndex.TryGetValue(id, out idx);

        if (!TryPickupIndex(currentStopIdentifier, out int currentIndex_Pickup))
        {
            Loader.Log($"Current stop '{currentStopIdentifier}' is not in pickup station list; using normal logic.");
            return true; // fall back to normal logic
        }

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
                Loader.Log($"Train has alarka as the west terminus station; Train is doing the alarka branch only");

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
            if (!TryPickupIndex(alarkajctIdentifier, out int alarkaJctIndex_Pickup))
            {
                Loader.Log("alarkajct is not in pickup station list; using normal logic.");
                return true;
            }

            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                if (currentIndex_Pickup > alarkaJctIndex_Pickup)
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
                if (currentIndex_Pickup < alarkaJctIndex_Pickup)
                {
                    Loader.Log($"Train is heading West, selecting all pickup stations");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);

                    return false;
                }

                Loader.Log($"Train is going west, using normal logic if at or after alarka jct");
                return true;
            }
        }

        Loader.Log($"Train is long distance train with no alarka branch. Checking if alarka and cochran are pickup stations");

        bool addAlarkaAndCochran = true;
        bool pickUpContainsAlarkaAndCochran = pickUpPassengerStations.Contains(alarkaIdentifier) && pickUpPassengerStations.Contains(cochranIdentifier);
        Loader.Log($"Alarka and cochran are pickup stations: {pickUpContainsAlarkaAndCochran}");

        bool dotEastAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.EAST;
        bool dotWestAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.WEST;

        if (pickUpContainsAlarkaAndCochran)
        {
            Loader.Log($"Checking if train is before Alarka jct, based on current direction of travel");
            if (!TryPickupIndex(alarkajctIdentifier, out int alarkaJctIndex_Pickup))
            {
                Loader.Log("alarkajct is not in pickup station list; using normal logic.");
                return true;
            }
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
}
