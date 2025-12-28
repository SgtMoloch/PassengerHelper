using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Messages;
using Game.State;
using Model;
using Model.Ops;
using PassengerHelper.Managers;
using PassengerHelper.Plugin;
using Track;
using UnityEngine;

namespace PassengerHelper.Support.GameObjects;

public sealed class PassengerHelperRuntime : MonoBehaviour
{
    private const float _intervalSeconds = 3.0f;
    public float IntervalSeconds => _intervalSeconds;

    private StationManager _stationManager;
    private TrainManager _trainManager;
    private SettingsManager _settingsManager;
    private TrainStateManager _trainStateManager;
    private PassengerStopOrderManager _passengerStopOrderManager;

    private Coroutine _loop;

    public bool IsRunning => _loop != null;

    private MethodInfo FindCars = typeof(PassengerStop).GetMethod("FindCars", BindingFlags.NonPublic | BindingFlags.Instance);

    public void Init(
        StationManager stationManager,
        TrainManager trainManager,
        SettingsManager settingsManager,
        TrainStateManager trainStateManager,
        PassengerStopOrderManager passengerStopOrderManager)
    {
        _stationManager = stationManager;
        _trainManager = trainManager;
        _settingsManager = settingsManager;
        _trainStateManager = trainStateManager;
        _passengerStopOrderManager = passengerStopOrderManager;

        if (StateManager.IsHost && _loop == null)
        {
            Loader.Log("[PassengerHelperTicker] starting loop (Init)");
            _loop = StartCoroutine(Loop());
        }
    }

    private void OnDisable()
    {
        if (_loop != null)
        {
            Loader.Log($"[PassengerHelperTicker] stopping loop");
            StopCoroutine(_loop);
            _loop = null;
        }
    }

    private void Update()
    {
        if (_loop == null && StateManager.IsHost)
        {
            Loader.Log("[PassengerHelperTicker] starting loop (host became active)");
            _loop = StartCoroutine(Loop());
        }
    }

    private IEnumerator Loop()
    {
        while (true)
        {
            if (!Loader.ModEntry.Enabled)
            {
                yield return new WaitForSeconds(60f);
            }

            try
            {
                TickOnce();
                _stationManager.TickDepartureChecks();
            }
            catch (System.Exception ex)
            {
                // log once or throttle logs
                Loader.LogError($"PassengerHelperTicker exception: {ex}");
            }

            yield return new WaitForSeconds(_intervalSeconds);
        }
    }

    private void TickOnce()
    {
        List<PassengerStop> passStops = _passengerStopOrderManager.OrderedUnlockedAll.ToList();

        foreach (PassengerStop ps in passStops)
        {
            HashSet<string> visitedCarIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> processedLocoIds = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<Car> carsAtStation = (IEnumerable<Car>)FindCars.Invoke(ps, new object[] { TrainController.Shared });

            foreach (Car car in carsAtStation)
            {
                if (car == null) continue;

                if (!visitedCarIds.Add(car.id)) continue;

                if (!TryGetPassengerLocomotive(car, visitedCarIds, out BaseLocomotive lm, out string failReason))
                {
                    Loader.LogError($"PassenegerHelperTick: skipping {car.DisplayName} because: {failReason}");
                    continue;
                }

                if (!processedLocoIds.Add(lm.id)) continue;

                _stationManager.HandleTrainAtStation(lm, ps);
            }
        }
        // 1) Use your station-span scan here to find passenger cars “being worked”
        // 2) For each passenger car: resolve pl = _trainManager.GetPassengerLocomotive(car)
        // 3) Decide if it’s “at station” (or “departed”) and call station manager logic
    }

    private bool TryGetPassengerLocomotive(Car car, HashSet<string> visitedCarIds, out BaseLocomotive lm, out string failReason)
    {
        lm = null;
        failReason = "";

        string localFail = null;

        BaseLocomotive found = null;

        foreach (Car c in car.EnumerateCoupled())
        {
            if (localFail != null) break;
            Check(c);
        }
        foreach (Car c in car.EnumerateCoupled(Car.LogicalEnd.B))
        {
            if (localFail != null) break;
            Check(c);
        }

        void Check(Car c)
        {
            if (c == null) return;

            if (string.IsNullOrEmpty(c.id)) return;

            if (!visitedCarIds.Add(c.id)) return;

            if (c is not BaseLocomotive loco) return;

            bool isMu = c.ControlProperties[PropertyChange.Control.Mu];

            if (!isMu)
            {
                if (found == null)
                {
                    found = loco;
                }
                else if (found.id != loco.id)
                {
                    localFail = $"More than 1 engine is coupled to car: {car.DisplayName} and those additional engines are NOT mu, therefore unable to determine which engine is the actual passenger locomotive";
                    return;
                }
            }
        }

        if (localFail != null)
        {
            failReason = localFail;
            return false;
        }

        if (found == null)
        {
            failReason = $"No non-MU locomotive coupled to {car.DisplayName}";
            return false;
        }

        lm = found;
        return true;
    }
}