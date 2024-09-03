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
using global::PassengerHelperPlugin.Managers;
using Model.OpsNew;
using Game;

public class PassengerHelperPlugin : SingletonPluginBase<PassengerHelperPlugin>
{
    static ILogger logger = Log.ForContext(typeof(PassengerHelperPlugin));

    private readonly IModdingContext ctx;
    private readonly IModDefinition self;

    internal SettingsManager settingsManager { get; }
    internal TrainManager trainManager { get; }
    internal StationManager stationManager { get; }
    internal bool TestMode { get; } = false;

    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    public PassengerHelperPlugin(IModdingContext ctx, IModDefinition self, IUIHelper uiHelper)
    {
        new Harmony(self.Id).PatchAll(GetType().Assembly);
        Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings = ctx.LoadSettingsData<Dictionary<string, PassengerLocomotiveSettings>>(self.Id) ?? new Dictionary<string, PassengerLocomotiveSettings>();

        this.self = self;
        this.ctx = ctx;

        SettingsManager settingsManager = new SettingsManager(this, passengerLocomotivesSettings, uiHelper);
        TrainManager trainManager = new TrainManager(settingsManager);
        StationManager stationManager = new StationManager(settingsManager, trainManager, orderedStations);

        this.settingsManager = settingsManager;
        this.trainManager = trainManager;
        this.stationManager = stationManager;
    }

    public void SaveSettings(Dictionary<string, PassengerLocomotiveSettings> settings)
    {
        ctx.SaveSettingsData(self.Id, settings);
    }
}
