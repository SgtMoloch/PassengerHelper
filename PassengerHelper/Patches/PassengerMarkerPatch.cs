using System.Collections.Generic;
using Game;
using HarmonyLib;
using JetBrains.Annotations;
using Model.Ops;

namespace PassengerHelper.Patches;

public static class PassengerMarkerPatch
{
    /* 
    this patch will unload experied passengers when at a station, paying for the distance.
     */
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerMarker), "TryRemovePassenger")]
    private static bool UnloadCar(ref bool __result, string destination, out string removedDestination, out string removedOrigin, out GameDateTime removedBoarded, List<PassengerGroup> ___Groups, HashSet<string> ___Destinations)
    {
        /* existing game logic unless otherwise indicated */

        // start custom logic
        GameDateTime gameDateTime = TimeWeather.Now.AddingHours(-4f);
        // end custom logic

        for (int i = 0; i < ___Groups.Count; i++)
        {
            PassengerGroup value = ___Groups[i];
            if (value.Count <= 0)
            {
                continue;
            }

            bool flag = ___Destinations.Contains(value.Destination);
            if (!(!(value.Destination == destination) && flag))
            {

                value.Count--;
                if (value.Count > 0)
                {
                    ___Groups[i] = value;
                }
                else
                {
                    ___Groups.RemoveAt(i);
                    i--;
                }

                removedOrigin = value.Origin;
                removedBoarded = value.Boarded;
                removedDestination = value.Destination;
                __result = true;
                return false;
            }
            //start custom logic
            if (value.Boarded >= gameDateTime)
            {
                // end custom logic
                value.Count--;
                if (value.Count > 0)
                {
                    ___Groups[i] = value;
                }
                else
                {
                    ___Groups.RemoveAt(i);
                    i--;
                }

                removedOrigin = value.Origin;
                removedBoarded = value.Boarded;
                removedDestination = destination;
                __result = true;
                return false;
            }
            // end custom logic
        }

        removedDestination = null;
        removedBoarded = default(GameDateTime);
        removedOrigin = null;
        __result = false;
        return false;
    }
}
