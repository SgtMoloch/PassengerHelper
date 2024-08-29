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
    internal bool TestMode { get; } = true;

    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    private readonly string serializedGroupDictionary = ".TransferPassengerGroups";
    public PassengerHelperPlugin(IModdingContext ctx, IModDefinition self, IUIHelper uiHelper)
    {
        new Harmony(self.Id).PatchAll(GetType().Assembly);
        Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings = ctx.LoadSettingsData<Dictionary<string, PassengerLocomotiveSettings>>(self.Id) ?? new Dictionary<string, PassengerLocomotiveSettings>();
        Dictionary<string, List<NewGroup>> serializedGroupsDictionary = ctx.LoadSettingsData<Dictionary<string, List<NewGroup>>>(self.Id + serializedGroupDictionary) ?? new Dictionary<string, List<NewGroup>>();

        this.self = self;
        this.ctx = ctx;

        SettingsManager settingsManager = new SettingsManager(this, passengerLocomotivesSettings, uiHelper);
        TrainManager trainManager = new TrainManager(settingsManager);
        StationManager stationManager = new StationManager(settingsManager, trainManager, orderedStations, GetActualGroupDictionaryFromSerialized(serializedGroupsDictionary));

        this.settingsManager = settingsManager;
        this.trainManager = trainManager;
        this.stationManager = stationManager;

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    public void SaveSettings(Dictionary<string, PassengerLocomotiveSettings> settings)
    {
        ctx.SaveSettingsData(self.Id, settings);
    }

    private Dictionary<string, List<PassengerMarker.Group>> GetActualGroupDictionaryFromSerialized(Dictionary<string, List<NewGroup>> serializedGroups)
    {
        Dictionary<string, List<PassengerMarker.Group>> actualGroups = new();

        foreach (string stationId in serializedGroups.Keys)
        {
            List<NewGroup> groupList = serializedGroups[stationId];
            List<PassengerMarker.Group> _newGroups = new();

            foreach (var group in groupList)
            {
                _newGroups.Add(new PassengerMarker.Group(group.Origin, group.Destination, group.Count, new GameDateTime(group.Boarded)));
            }
            actualGroups.Add(stationId, _newGroups);
        }

        return actualGroups;
    }

    private Dictionary<string, List<NewGroup>> GetSerializableGroupDictionaryFromActual(Dictionary<string, List<PassengerMarker.Group>> actualGroups)
    {
        Dictionary<string, List<NewGroup>> serializableGroupDictionary = new();

        foreach (string stationId in actualGroups.Keys)
        {
            List<PassengerMarker.Group> groupList = actualGroups[stationId];
            List<NewGroup> _newGroups = new();

            foreach (var group in groupList)
            {
                _newGroups.Add(new NewGroup(group.Origin, group.Destination, group.Count, group.Boarded.TotalSeconds));
            }
            serializableGroupDictionary.Add(stationId, _newGroups);
        }

        return serializableGroupDictionary;
    }
    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        settingsManager.GetAllSettings().Values.ToList().ForEach(x => x.gameLoadFlag = true);

        settingsManager.SaveSettings();

        ctx.SaveSettingsData(self.Id + serializedGroupDictionary, GetSerializableGroupDictionaryFromActual(stationManager.groupDictionary));
    }

    struct NewGroup
    {
        public string Origin;

        public string Destination;

        public int Count;

        public double Boarded;

        public NewGroup(string origin, string destination, int count, double boarded)
        {
            Origin = origin;
            Destination = destination;
            Count = count;
            Boarded = boarded;
        }
    }

}
