using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
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
    private const float _intervalSeconds = 5.0f;
    public float IntervalSeconds => _intervalSeconds;

    private StationManager _stationManager;
    private TrainManager _trainManager;
    private SettingsManager _settingsManager;
    private TrainStateManager _trainStateManager;
    private PassengerStopOrderManager _passengerStopOrderManager;

    private Coroutine _loop;

    public bool IsRunning => _loop != null;


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

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        if (_loop != null)
        {
            Loader.Log($"[PassengerHelperTicker] stopping loop");
            StopCoroutine(_loop);
            _loop = null;
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
                Tick();
            }
            catch (System.Exception ex)
            {
                Loader.LogError($"PassengerHelperTicker exception: {ex}");
            }

            yield return new WaitForSeconds(_intervalSeconds);
        }
    }

    private void Tick()
    {

        _stationManager.TickDeparture();
        _stationManager.TickStations();
    }

}