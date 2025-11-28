namespace PassengerHelper.Support.GameObjects;

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

public class PassengerHelperPassengerStop : GameBehaviour
{
    public string identifier;
    private PassengerStop passengerStop;
    internal KeyValueObject _keyValueObject;
    private string KeyValueIdentifier => "pass.passhelper." + identifier;

    private void Awake()
    {
        this.passengerStop = base.gameObject.GetComponentInParent<PassengerStop>();
        this.identifier = this.passengerStop.identifier;
        _keyValueObject = gameObject.GetComponent<KeyValueObject>();

        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier, _keyValueObject, AuthorizationRequirement.HostOnly);
    }
}