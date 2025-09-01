namespace PassengerHelperPlugin.Patches;

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Definition;
using Support;
using RollingStock;
using Serilog;
using GameObjects;
using Model.Ops;
using System.Reflection;
using Game;
using Game.Messages;
using Game.State;

[HarmonyPatch]
public static class PassengerExpirationPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerExpirationPatches));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PassengerExpiration), "Tick")]
    private static void Tick(PassengerExpiration __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }

        /*
           Original Game code unless otherwise stated
        */

        IEnumerable<PassengerStop> enumerable = PassengerStop.FindAll();
        List<Car> list = TrainController.Shared.Cars.Where((Car car) => car.IsPassengerCar()).ToList();
        GameDateTime gameDateTime = TimeWeather.Now.AddingHours(-4f);
        using (StateManager.TransactionScope())
        {
            int num = 0;
            HashSet<Car> carsAtStation = new HashSet<Car>();
            foreach (PassengerStop item in enumerable)
            {
                num += item.ExpirePassengers(gameDateTime);

                // start custom logic
                MethodInfo FindCars = typeof(PassengerStop).GetMethod("FindCars", BindingFlags.NonPublic | BindingFlags.Instance);
                HashSet<Car> cars = (HashSet<Car>)FindCars.Invoke(item, new object[] { TrainController.Shared });
                carsAtStation.UnionWith(cars);
                //end custom logic
            }

            // start custom logic
            list.RemoveAll(car => carsAtStation.Contains(car));
            //end custom logic

            foreach (Car car in list)
            {
                PassengerMarker? passengerMarker = car.GetPassengerMarker();
                if (!passengerMarker.HasValue)
                {
                    continue;
                }
                PassengerMarker valueOrDefault = passengerMarker.GetValueOrDefault();
                bool flag = false;
                for (int num2 = valueOrDefault.Groups.Count - 1; num2 >= 0; num2--)
                {
                    PassengerGroup passengerGroup = valueOrDefault.Groups[num2];
                    // start custom logic
                    if (!(passengerGroup.Boarded >= gameDateTime) && car.IsAtRest)
                    {
                         //end custom logic
                        num += passengerGroup.Count;
                        valueOrDefault.Groups.RemoveAt(num2);
                        flag = true;
                    }
                }
                if (flag)
                {
                    car.SetPassengerMarker(valueOrDefault);
                }
            }

            if (num > 0)
            {
                Log.Information("Expired {count} passengers since {exp}.", num, gameDateTime);
            }
        }
    }
}