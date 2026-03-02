namespace PassengerHelper.Plugin;

using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using Model;
using Support;
using System.Collections.Generic;
using System.Linq;
using Managers;
using Model.Ops;
using Game;
using global::PassengerHelper.Support.UIHelp;
using UnityModManagerNet;
using System.Reflection;
using PassengerHelper.Support.GameObjects;

public class PassengerHelperPlugin
{
    internal TrainStateManager trainStateManager { get; }
    internal SettingsManager settingsManager { get; }
    internal TrainManager trainManager { get; }
    internal StationManager stationManager { get; }
    internal PassengerStopOrderManager passengerStopOrderManager { get; }
    internal UIHelper UIHelper { get; }
    internal Harmony harmony { get; }
    internal PassengerHelperRuntime runtime { get; set; }

    public PassengerHelperPlugin(string modId)
    {
        this.harmony = new Harmony(modId);
        this.harmony.PatchAll(GetType().Assembly);
        Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings = new Dictionary<string, PassengerLocomotiveSettings>();

        UIHelper uIHelper = new UIHelper();
        PassengerStopOrderManager passengerStopOrderManager = new PassengerStopOrderManager();
        TrainStateManager trainStateManager = new TrainStateManager();
        SettingsManager settingsManager = new SettingsManager(uIHelper, () => (List<PassengerStop>)passengerStopOrderManager.OrderedMainline);
        TrainManager trainManager = new TrainManager(settingsManager, trainStateManager);
        StationManager stationManager = new StationManager(settingsManager, trainManager, trainStateManager, passengerStopOrderManager);

        this.UIHelper = uIHelper;
        this.passengerStopOrderManager = passengerStopOrderManager;
        this.settingsManager = settingsManager;
        this.trainManager = trainManager;
        this.stationManager = stationManager;
        this.trainStateManager = trainStateManager;
    }

}
