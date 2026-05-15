using System.Collections.Generic;
using Game;
using HarmonyLib;
using JetBrains.Annotations;
using Model.Ops;
using PassengerHelper.Plugin;

namespace PassengerHelper.Patches;

[HarmonyPatch]
public static class PassengerMarkerPatch
{
    /* 
    this patch will unload experied passengers when at a station, paying for the distance.
     */
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerMarker), nameof(PassengerMarker.TryRemovePassenger))]
    private static bool TryRemovePassenger(
        ref PassengerMarker __instance,
        ref bool __result,
        string destination,
        ref string removedDestination,
        ref string removedOrigin,
        ref GameDateTime removedBoarded)
    {
        if (!Loader.ModEntry.Enabled)
        {
            return true;
        }

        /* existing game logic unless otherwise indicated */

        // start custom logic
        GameDateTime gameDateTime = TimeWeather.Now.AddingHours(-6.5f);
        // end custom logic

        for (int i = 0; i < __instance.Groups.Count; i++)
        {
            PassengerGroup value = __instance.Groups[i];
            if (value.Count <= 0)
            {
                continue;
            }

            bool flag = __instance.Destinations.Contains(value.Destination);

            if (!(!(value.Destination == destination) && flag))
            {

                value.Count--;
                if (value.Count > 0)
                {
                    __instance.Groups[i] = value;
                }
                else
                {
                    __instance.Groups.RemoveAt(i);
                    i--;
                }

                removedOrigin = value.Origin;
                removedBoarded = value.Boarded;
                removedDestination = value.Destination;
                __result = true;
                return false;
            }
            //start custom logic
            // If the passenger is expired, unload them here and pay to the current station.
            if (!(value.Boarded >= gameDateTime))
            {
                Loader.LogVerbose(
                    $"[PassengerMarkerPatch::TryRemovePassenger] Removing expired passenger at {destination}. " +
                    $"Original trip: {value.Origin} -> {value.Destination}. " +
                    $"Paying as: {value.Origin} -> {destination}. " +
                    $"Boarded={value.Boarded}, cutoff={gameDateTime}."
                );

                value.Count--;
                if (value.Count > 0)
                {
                    __instance.Groups[i] = value;
                }
                else
                {
                    __instance.Groups.RemoveAt(i);
                    i--;
                }

                removedOrigin = value.Origin;
                removedBoarded = value.Boarded;
                // Important:
                // Use the current station as the removed destination so payout is based
                // on distance actually traveled, not the passenger's intended destination.
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
