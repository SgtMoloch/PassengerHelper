namespace PassengerHelper.Patches;

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Definition;
using Support;
using RollingStock;
using Model.Ops;
using System.Reflection;
using Game;
using Game.Messages;
using Game.State;
using PassengerHelper.Plugin;

[HarmonyPatch]
public static class PassengerExpirationPatches
{
    private static MethodInfo FindCars = typeof(PassengerStop).GetMethod("FindCars", BindingFlags.NonPublic | BindingFlags.Instance);
    /*
        Replaces PassengerExpiration.Tick.

        Changes passenger expiration from 4 hours to 6.5 hours for passengers
        waiting at stations.

        Waiting passengers at stations still expire normally.

        Onboard passengers are never expired by this tick.
        This prevents passengers from poofing mid-route before they can deboard
        and generate payment.

        Original game code unless otherwise stated.
    */
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerExpiration), "Tick")]
    private static bool Tick(PassengerExpiration __instance)
    {
        if (!Loader.ModEntry.Enabled)
        {
            return true;
        }

        IEnumerable<PassengerStop> enumerable = PassengerStop.FindAll();

        List<Car> list = TrainController.Shared.Cars.Where((Car car) => car.IsPassengerCar()).ToList();

        // -6.5f is custom
        GameDateTime gameDateTime = TimeWeather.Now.AddingHours(-6.5f);

        using (StateManager.TransactionScope())
        {
            int expiredPassengerCount = 0;

            HashSet<Car> carsAtStation = new HashSet<Car>();

            foreach (PassengerStop stop in enumerable)
            {
                expiredPassengerCount += stop.ExpirePassengers(gameDateTime);
            }

            // custom remove code to expire passengers in cars

            if (expiredPassengerCount > 0)
            {
                Loader.Log($"Expired {expiredPassengerCount} passengers since {gameDateTime}.");
            }
        }

        return false;
    }
}