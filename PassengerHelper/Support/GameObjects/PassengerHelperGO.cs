namespace PassengerHelperPlugin.Support.GameObjects;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.AccessControl;
using Game.Messages;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model.Ops;
using RollingStock;
using Serilog;
using UnityEngine;
using Support;
using Model;
using Model.Definition;
using Model.Definition.Data;

public class PassengerHelperSettingsGO : GameBehaviour
{

    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerHelperSettingsGO));
    private KeyValueObject _keyValueObject;
    private IDisposable _observer;

    public Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings = new();

    private string KeyValueIdentifier => "Moloch.PH.Settings";

    public bool Loaded = false;


    private void Awake()
    {
        _keyValueObject = base.gameObject.AddComponent<KeyValueObject>();
        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier, _keyValueObject, AuthorizationRequirement.HostOnly);
    }

    protected override void OnEnableWithProperties()
    {
        if (StateManager.IsHost)
        {
            LoadState();
            Loaded = true;
        }
        else
        {
            _observer = _keyValueObject.Observe("state", delegate
            {
                LoadState();
            });
        }

    }

    private void LoadState()
    {
        IReadOnlyDictionary<string, Value> dictionaryValue = _keyValueObject["state"].DictionaryValue;
        if (!dictionaryValue.Any())
        {
            return;
        }

        try
        {
            Value _passengerHelperSettingsState = dictionaryValue["moloch.passengerhelper"];
            foreach (KeyValuePair<string, Value> _settingsState in _passengerHelperSettingsState.DictionaryValue)
            {
                _settingsState.Deconstruct(out var _locomotive, out var _passengerSettings);
                string key2 = _locomotive;
                Value value2 = _passengerSettings;
                passengerLocomotivesSettings[key2] = PassengerLocomotiveSettings.FromPropertyValue(value2);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Exception in PassengerHelperGO LoadState");
        }
    }

    public void SaveState()
    {
        Dictionary<string, Value> dictionary = new Dictionary<string, Value>();
        Dictionary<string, Value> dictionary2 = new Dictionary<string, Value>();
        foreach (var (_locomotive, passengerLocomotivesSetting) in passengerLocomotivesSettings)
        {
            dictionary2[_locomotive] = passengerLocomotivesSetting.PropertyValue();
        }

        dictionary["moloch.passengerhelper"] = Value.Dictionary(dictionary2);
        _keyValueObject["state"] = Value.Dictionary(dictionary);
    }
}