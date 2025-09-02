namespace PassengerHelperPlugin;

using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using Model;
using Support;
using Railloader;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using Managers;
using Model.Ops;
using Game;
using UnityEngine;
using Support.GameObjects;
using System;

public class PassengerHelperPlugin : SingletonPluginBase<PassengerHelperPlugin>
{
    static Serilog.ILogger logger = Log.ForContext(typeof(PassengerHelperPlugin));

    internal PassengerHelperSettingsGO passengerHelperSettingsGO;

    private readonly IModdingContext ctx;
    private readonly IModDefinition self;

    internal SettingsManager settingsManager { get; }
    internal TrainManager trainManager { get; }
    internal StationManager stationManager { get; }
    internal bool DebugLogging { get; } = true;

    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    public PassengerHelperPlugin(IModdingContext ctx, IModDefinition self, IUIHelper uiHelper)
    {
        new Harmony(self.Id).PatchAll(GetType().Assembly);
        
        this.self = self;
        this.ctx = ctx;

        SettingsManager settingsManager = new SettingsManager(this, uiHelper);
        TrainManager trainManager = new TrainManager(settingsManager);
        StationManager stationManager = new StationManager(settingsManager, trainManager, orderedStations);

        this.settingsManager = settingsManager;
        this.trainManager = trainManager;
        this.stationManager = stationManager;

        Messenger.Default.Register<MapDidLoadEvent>((object)this, (Action<MapDidLoadEvent>)OnMapDidLoad);
    }

    public void SaveSettings(Dictionary<string, PassengerLocomotiveSettings> settings)
    {
        passengerHelperSettingsGO.SaveState();
    }

    private void OnMapDidLoad(MapDidLoadEvent @event)
    {
        GameObject val = new GameObject("[Moloch PH Settings]");
        UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)val);
        passengerHelperSettingsGO = val.AddComponent<PassengerHelperSettingsGO>();
        passengerHelperSettingsGO.enabled = true;

        settingsManager.LoadSettings();
    }
}
