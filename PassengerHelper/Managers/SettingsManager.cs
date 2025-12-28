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
using Support;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.UI;
using KeyValue.Runtime;
using Support.UIHelp;
using PassengerHelper.Plugin;

public class SettingsManager
{
    internal UIHelper uIHelper { get; }
    private Func<List<PassengerStop>> getMainlineStations;

    private Dictionary<BaseLocomotive, bool> settingsWindowShowing = new();

    private Dictionary<PassengerLocomotive, PassengerLocomotiveSettings> plsMap = new();
    private Dictionary<PassengerLocomotive, IDisposable> plKeyObvDisposeMap = new();

    public SettingsManager(UIHelper uIHelper, Func<List<PassengerStop>> getMainlineStations)
    {
        this.uIHelper = uIHelper;
        this.getMainlineStations = getMainlineStations;

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    public void SaveSettings(PassengerLocomotive pl, PassengerLocomotiveSettings pls)
    {
        StateManager.ApplyLocal(new PropertyChange(pl._locomotive.id, pl.KeyValueIdentifier_Settings, PropertyValueConverter.RuntimeToSnapshot(pls.PropertyValue())));

        pl.settingsHash = pls.getSettingsHash();
        pl.stationSettingsHash = pls.getStationSettingsHash();

        plsMap[pl] = pls;
    }

    public PassengerLocomotiveSettings CreateNewSettings(PassengerLocomotive pl)
    {
        PassengerLocomotiveSettings pls = new(this.getMainlineStations().Select(ps => ps.identifier).ToList());
        plsMap.Add(pl, pls);

        SaveSettings(pl, pls);

        IDisposable plObv = pl._keyValueObject.Observe(pl.KeyValueIdentifier_Settings, delegate (Value val)
        {
            Loader.LogVerbose($"updating settings map existing loco {pl._locomotive.DisplayName}, new values: {val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString())}");
            PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(val);
            Loader.LogVerbose($"new settings for {pl._locomotive.DisplayName}: {pls.ToString()}");
            plsMap[pl] = pls;
        }, callInitial: false);

        plKeyObvDisposeMap[pl] = plObv;

        return pls;
    }

    public PassengerLocomotiveSettings LoadSettings(PassengerLocomotive pl)
    {
        PassengerLocomotiveSettings pls;

        try
        {
            pls = PassengerLocomotiveSettings.FromPropertyValue(pl._keyValueObject[pl.KeyValueIdentifier_Settings]);
        }
        catch
        {
            Loader.LogError("Error when loading passenger settings. setting current settings to null and then creating new settings. Previous settings will be lost unfortuneately. Sorry for the inconvience.");
            StateManager.ApplyLocal(new PropertyChange(pl._locomotive.id, pl.KeyValueIdentifier_Settings, PropertyValueConverter.RuntimeToSnapshot(Value.Null())));
            return CreateNewSettings(pl);
        }


        Loader.Log($"loaded settings for {pl._locomotive.DisplayName}");
        if (!plsMap.ContainsKey(pl))
        {
            Loader.Log($"pass loco not in settings map, adding observer");
            plsMap.Add(pl, pls);
            IDisposable plObv = pl._keyValueObject.Observe(pl.KeyValueIdentifier_Settings, delegate (Value val)
            {
                Loader.LogVerbose($"updating settings map existing loco {pl._locomotive.DisplayName}, new values: {val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString())}");
                PassengerLocomotiveSettings pls = PassengerLocomotiveSettings.FromPropertyValue(val);
                Loader.LogVerbose($"new settings for {pl._locomotive.DisplayName}: {pls.ToString()}");
                plsMap[pl] = pls;
            }, callInitial: false);

            plKeyObvDisposeMap[pl] = plObv;
        }

        return pls;
    }

    public PassengerLocomotiveSettings GetSettings(PassengerLocomotive pl)
    {
        if (!plsMap.ContainsKey(pl))
        {
            return CreateNewSettings(pl);
        }

        return plsMap[pl];
    }

    public StationSetting GetStationSetting(PassengerLocomotive pl, string stationId)
    {
        if (!plsMap.ContainsKey(pl))
        {
            return CreateNewSettings(pl).StationSettings[stationId];
        }

        return plsMap[pl].StationSettings[stationId];
    }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        foreach (KeyValuePair<PassengerLocomotive, PassengerLocomotiveSettings> kvp in plsMap)
        {
            PassengerLocomotiveSettings pls = kvp.Value;

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

        PassengerHelperPlugin plugin = Loader.PassengerHelper;

        PassengerSettingsWindow settingsWindow = new PassengerSettingsWindow(this.uIHelper, plugin);

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
