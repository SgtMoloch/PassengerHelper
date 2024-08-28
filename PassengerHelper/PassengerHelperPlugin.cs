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

public class PassengerHelperPlugin : SingletonPluginBase<PassengerHelperPlugin>
{
    static ILogger logger = Log.ForContext(typeof(PassengerHelperPlugin));

    private readonly IModdingContext ctx;
    private readonly IModDefinition self;
    internal Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings { get; }
    internal Dictionary<BaseLocomotive, PassengerLocomotive> _locomotives = new();
    internal StationManager stationManager { get; }
    internal SettingsManager settingsManager { get; }
    internal TrainManager trainManager{ get; }

    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    internal readonly List<string> orderedStations_Full = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka", "cochran",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };
    public PassengerHelperPlugin(IModdingContext ctx, IModDefinition self, IUIHelper uiHelper)
    {
        new Harmony(self.Id).PatchAll(GetType().Assembly);
        passengerLocomotivesSettings = ctx.LoadSettingsData<Dictionary<string, PassengerLocomotiveSettings>>(self.Id) ?? new Dictionary<string, PassengerLocomotiveSettings>();
        this.self = self;
        this.ctx = ctx;

        this.stationManager = new StationManager(this);
        this.settingsManager = new SettingsManager(this, passengerLocomotivesSettings, uiHelper);
        this.trainManager = new TrainManager(this);

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    public void SaveSettings()
    {
        ctx.SaveSettingsData(self.Id, passengerLocomotivesSettings);
    }

    public void SaveSettings(Dictionary<string, PassengerLocomotiveSettings> settings)
    {
        ctx.SaveSettingsData(self.Id, settings);
    }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        passengerLocomotivesSettings.Values.ToList().ForEach(x => x.gameLoadFlag = true);

        SaveSettings();
    }

}
