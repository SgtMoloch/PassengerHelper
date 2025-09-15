namespace PassengerHelperPlugin.Managers;

using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.Messages;
using Game.State;
using Support.GameObjects;
using Model;
using Model.Definition;
using Model.Ops;
using Railloader;
using RollingStock;
using Serilog;
using Support;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.UI;
using KeyValue.Runtime;

public class SettingsManager
{
    internal static class SettingKey
    {
        internal static string PauseForDiesel = "pause_for_diesel";
        internal static string DieselLevel = "diesel_level";
        internal static string PauseForCoal = "pause_for_coal";
        internal static string CoalLevel = "coal_level";
        internal static string PauseForWater = "pause_for_water";
        internal static string WaterLevel = "water_level";
        internal static string PauseAtNextStation = "pause_next_station";
        internal static string PauseAtTerminusStation = "pause_terminus_station";
        internal static string PreventLoadWhenPausedAtStation = "prevent_load_when_paused";
        internal static string WaitForFullPassengersTerminusStation = "wait_for_full_load_at_terminus_station";
        internal static string Disable = "disable";
        internal static string DirectionOfTravel = "direction_of_travel";
        internal static string DoTLocked = "dot_locked";
        internal static string TrainStatus = "train_status";
    }

    internal static class StationSettingKey
    {
        internal static string StopAtStation = "stop_at_station";
        internal static string TerminusStation = "terminus_station";
        internal static string PickupPassengersForStation = "pick_up_station";
        internal static string PauseAtStation = "pause_at_station";
        internal static string TransferStation = "transfer_station";
        internal static string PassengerMode = "passenger_mode";
    }

    internal static class TrainStatusKey
    {
        internal static string PreviousStation = "previous_station";
        internal static string CurrentStation = "current_station";
        internal static string ArrivedAtStation = "arrived_at_station";
        internal static string AtTerminusStationEast = "at_terminus_station_east";
        internal static string AtTerminusStationWest = "at_terminus_station_west";
        internal static string AtAlarkaStation = "at_alarka_station";
        internal static string AtCochranStation = "at_cochran_station";
        internal static string TerminusStationProcedureComplete = "terminus_station_procedure_complete";
        internal static string StationProcedureComplete = "station_procedure_complete";
        internal static string CurrentlyStopped = "currently_stopped";
        internal static string CurrentStopReason = "current_stop_reason";
        internal static string StoppedUnknownDirection = "stopped_unknown_direction";
        internal static string StoppedInvalidTerminusStations = "stopped_invalid_terminus_stations";
        internal static string StoppedInvalidStations = "stopped_invalid_stations";
        internal static string StoppedDiesel = "stopped_diesel";
        internal static string StoppedCoal = "stopped_coal";
        internal static string StoppedWater = "stopped_water";
        internal static string StoppedNextStation = "stopped_next_station";
        internal static string StoppedTerminusStation = "stopped_terminus_station";
        internal static string StoppedPause = "stopped_pause";
        internal static string StoppedFullLoad = "stopped_full_load";
        internal static string ReadyToDepart = "ready_to_depart";
        internal static string Departed = "departed";
        internal static string Continue = "continue";
    }

    static readonly Serilog.ILogger logger = Log.ForContext(typeof(SettingsManager));
    internal IUIHelper uIHelper { get; }
    private PassengerHelperPlugin plugin;
    private UtilManager utilManager;
    private Dictionary<BaseLocomotive, bool> settingsWindowShowing = new();

    private Dictionary<PassengerLocomotive, PassengerLocomotiveSettings> plsMap = new();
    private Dictionary<PassengerLocomotive, IDisposable> plKeyObvDisposeMap = new();

    public SettingsManager(PassengerHelperPlugin plugin, IUIHelper uIHelper, UtilManager utilManager)
    {
        this.plugin = plugin;
        this.uIHelper = uIHelper;
        this.utilManager = utilManager;

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    public void SaveSettings(PassengerLocomotive pl, PassengerLocomotiveSettings pls)
    {
        StateManager.ApplyLocal(new PropertyChange(pl._locomotive.id, pl.KeyValueIdentifier, PropertyValueConverter.RuntimeToSnapshot(pls.PropertyValue())));
    }

    public void SaveSettings(PassengerLocomotive pl)
    {
        SaveSettings(pl, plsMap[pl]);
    }

    public PassengerLocomotiveSettings CreateNewSettings(PassengerLocomotive pl)
    {
        PassengerLocomotiveSettings pls = new(this.utilManager.GetPassengerStops().Select(ps => ps.identifier).ToList());
        plsMap.Add(pl, pls);

        SaveSettings(pl, pls);

       IDisposable plObv = pl._keyValueObject.Observe(pl.KeyValueIdentifier, delegate (Value val)
       {
           logger.Information("updating settings map existing loco, new values: {0}", val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString()));
           PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(val, this.utilManager.GetPassengerStops().Select(ps => ps.identifier).ToList());
           plsMap[pl] = pls;
       }, callInitial: false);

        plKeyObvDisposeMap[pl] = plObv;

        return pls;
    }

    public PassengerLocomotiveSettings LoadSettings(PassengerLocomotive pl)
    {
        PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(pl._keyValueObject[pl.KeyValueIdentifier], this.utilManager.GetPassengerStops().Select(ps => ps.identifier).ToList());

        logger.Information("loaded settings for {0}", pl._locomotive.DisplayName);
        if (!plsMap.ContainsKey(pl))
        {
            logger.Information("pass loco not in settings map, adding");
            plsMap.Add(pl, pls);
            logger.Information("adding observer");
            IDisposable plObv = pl._keyValueObject.Observe(pl.KeyValueIdentifier, delegate (Value val)
            {
                logger.Information("updating settings map existing loco, new values: {0}", val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString()));
                PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(val, utilManager.orderedStations);
                plsMap[pl] = pls;
            }, callInitial: false);

            plKeyObvDisposeMap[pl] = plObv;
        }

        return pls;
    }

    public void UpdateSettings(PassengerLocomotive pl)
    {
        PassengerLocomotiveSettings pls = LoadSettings(pl);

        plsMap[pl] = pls;
    }

    public PassengerLocomotiveSettings GetSettings(PassengerLocomotive pl)
    {
        if (!plsMap.ContainsKey(pl))
        {
            throw new Exception("passneger locomotive has not been added to internal settings map");
        }

        return plsMap[pl];
    }

    public StationSetting GetStationSetting(PassengerLocomotive pl, string stationId)
    {
        if (!plsMap.ContainsKey(pl))
        {
            throw new Exception("passneger locomotive has not been added to internal settings map");
        }

        return plsMap[pl].StationSettings[stationId];
    }

    public TrainStatus GetTrainStatus(PassengerLocomotive pl)
    {
        if (!plsMap.ContainsKey(pl))
        {
            throw new Exception("passneger locomotive has not been added to internal settings map");
        }

        return plsMap[pl].TrainStatus;
    }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        plsMap.Clear();

        foreach (PassengerLocomotive pl in plKeyObvDisposeMap.Keys)
        {
            plKeyObvDisposeMap[pl].Dispose();
        }

        plKeyObvDisposeMap.Clear();
    }

    private Window CreateSettingsWindow(string locomotiveDisplayName)
    {
        Window passengerSettingsWindow = uIHelper.CreateWindow("Moloch.PH.settings." + locomotiveDisplayName, 700, 250, Window.Position.Center);

        passengerSettingsWindow.Title = "Passenger Helper Settings for " + locomotiveDisplayName;

        return passengerSettingsWindow;
    }

    internal void ShowSettingsWindow(PassengerLocomotive passengerLocomotive)
    {
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;

        string locomotiveDisplayName = _locomotive.DisplayName;

        if (!this.settingsWindowShowing.TryGetValue(_locomotive, out var showing))
        {
            this.settingsWindowShowing.Add(_locomotive, false);
        }

        if (this.settingsWindowShowing[_locomotive])
        {
            return;
        }

        this.settingsWindowShowing[_locomotive] = true;

        Window passengerSettingsWindow = CreateSettingsWindow(locomotiveDisplayName);

        PassengerSettingsWindow settingsWindow = new PassengerSettingsWindow(this.uIHelper, this.utilManager.GetPassengerStops(), this);

        settingsWindow.PopulateAndShowSettingsWindow(passengerSettingsWindow, passengerLocomotive);

        passengerSettingsWindow.OnShownDidChange += (showing) =>
        {
            if (!showing)
            {
                this.settingsWindowShowing[_locomotive] = false;
                // SaveSettings();
            }
        };

        return;
    }
}
