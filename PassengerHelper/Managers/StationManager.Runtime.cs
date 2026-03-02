using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game;
using Game.Messages;
using Model;
using Model.Ops;
using PassengerHelper.Plugin;
using PassengerHelper.Support;

namespace PassengerHelper.Managers;

public partial class StationManager
{
    private MethodInfo FindCars = typeof(PassengerStop).GetMethod("FindCars", BindingFlags.NonPublic | BindingFlags.Instance);

    public void TickDeparture()
    {
        foreach (string locoId in _armedDepartures.ToArray())
        {
            PassengerLocomotive pl = trainManager.GetPassengerLocomotive(locoId);
            if (pl == null)
            {
                _armedDepartures.Remove(locoId);
                continue;
            }

            TrainState state = trainStateManager.GetState(pl);

            if (!state.ReadyToDepart || state.Departed || state.CurrentStation == null)
            {
                _armedDepartures.Remove(locoId);
                continue;
            }

            float speed = Math.Abs(pl._locomotive.velocity);

            if (!pl._locomotive.IsStopped(10f) && speed > 0.05f)
            {
                Loader.Log($"Train {pl._locomotive.DisplayName} has departed {state.CurrentStation.DisplayName} at {TimeWeather.Now}.");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: has departed {state.CurrentStation.DisplayName}\"");

                state.OnDepartReset();
                trainStateManager.SaveState(pl, state);

                _armedDepartures.Remove(locoId);
            }
        }
    }

    public void TickStations()
    {
        List<PassengerStop> passStops = passengerStopOrderManager.OrderedUnlockedAll.ToList();

        foreach (PassengerStop ps in passStops)
        {
            if (ps != null)
            {
                HashSet<string> visitedCarIds = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> processedLocoIds = new HashSet<string>(StringComparer.Ordinal);
                HashSet<Car> carsAtStation = (HashSet<Car>)FindCars.Invoke(ps, new object[] { TrainController.Shared });

                foreach (Car car in carsAtStation)
                {
                    if (car == null) continue;

                    if (!visitedCarIds.Add(car.id)) continue;

                    if (!trainManager.TryGetPassengerLocomotive(car, visitedCarIds, out BaseLocomotive lm, out string failReason))
                    {
                        Loader.LogError($"PassenegerHelperTick: skipping {car.DisplayName} because: {failReason}");
                        continue;
                    }

                    if (!processedLocoIds.Add(lm.id)) continue;

                    HandleTrainAtStation(lm, ps);
                }
            }
        }
        // 1) Use your station-span scan here to find passenger cars “being worked”
        // 2) For each passenger car: resolve pl = _trainManager.GetPassengerLocomotive(car)
        // 3) Decide if it’s “at station” (or “departed”) and call station manager logic
    }
}