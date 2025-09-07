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

public class SettingsManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(SettingsManager));
    internal IUIHelper uIHelper { get; }
    private PassengerHelperPlugin plugin;
    private Dictionary<BaseLocomotive, bool> settingsWindowShowing = new();

    public SettingsManager(PassengerHelperPlugin plugin, IUIHelper uIHelper)
    {
        this.plugin = plugin;
        this.uIHelper = uIHelper;

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    // public void LoadSettings()
    // {
    
    // }

    // public void SaveSettings()
    // {
    //     logger.Debug("Saving settings");
    //     plugin.SaveSettings(this._settings);
    // }

    // public void SaveSettings(string locomotiveName, TrainStatus trainStatus)
    // {
    //     _settings[locomotiveName].TrainStatus = trainStatus;
    //     SaveSettings();
    // }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        // foreach (PassengerLocomotiveSettings settings in _settings.Values)
        // {
        //     settings.gameLoadFlag = true;
        // }
        // SaveSettings();
    }

    // public PassengerLocomotiveSettings GetSettings(string locomotiveDisplayName)
    // {
    //     logger.Debug("Getting Passenger Settings for {0}", locomotiveDisplayName);
    //     if (!_settings.TryGetValue(locomotiveDisplayName, out PassengerLocomotiveSettings settings))
    //     {
    //         logger.Information("Did not Find settings for {0}, creating new settings", locomotiveDisplayName);
    //         settings = new PassengerLocomotiveSettings();

    //         logger.Debug("Adding new settings to internal Dictionary");
    //         this._settings.Add(locomotiveDisplayName, settings);

    //         SaveSettings();
    //     }

    //     return settings;
    // }

    // public Dictionary<string, PassengerLocomotiveSettings> GetAllSettings()
    // {
    //     return this._settings;
    // }

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

        PassengerSettingsWindow settingsWindow = new PassengerSettingsWindow(this.uIHelper, this.plugin.stationManager);

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
