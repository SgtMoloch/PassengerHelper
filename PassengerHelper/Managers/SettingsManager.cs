namespace PassengerHelper.Managers;

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
using PassengerHelper.Support.UIHelp;

public class SettingsManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(SettingsManager));
    internal UIHelper uIHelper { get; }

    private UtilManager utilManager;
    private Dictionary<BaseLocomotive, bool> settingsWindowShowing = new();

    private Dictionary<PassengerLocomotive, PassengerLocomotiveSettings> plsMap = new();
    private Dictionary<PassengerLocomotive, IDisposable> plKeyObvDisposeMap = new();

    public SettingsManager(UIHelper uIHelper, UtilManager utilManager)
    {
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
            logger.Information("updating settings map existing loco {0}, new values: {1}", pl._locomotive.DisplayName, val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString()));
            PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(val, this.utilManager.GetPassengerStops().Select(ps => ps.identifier).ToList());
            logger.Information("new settings for {0}: " + pls.ToString(), pl._locomotive.DisplayName);
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
                logger.Information("updating settings map existing loco {0}, new values: {1}", pl._locomotive.DisplayName, val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString()));
                PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(val, utilManager.orderedStations);
                logger.Information("new settings for {0}: " + pls.ToString(), pl._locomotive.DisplayName);
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
        foreach (KeyValuePair<PassengerLocomotive, PassengerLocomotiveSettings> kvp in plsMap)
        {
            PassengerLocomotiveSettings pls = kvp.Value;
            pls.gameLoadFlag = true;

            SaveSettings(kvp.Key, pls);
        }
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
                StateManager.DebugAssertIsHost();
                SaveManager.Shared.Save(null);
            }
        };

        return;
    }
}
