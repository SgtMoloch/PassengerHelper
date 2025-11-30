namespace PassengerHelper.UMM;

using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using Model;
using Support;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using Managers;
using Model.Ops;
using Game;
using global::PassengerHelper.Support.UIHelp;
using UnityModManagerNet;
using System.Reflection;

public class PassengerHelper
{
    static ILogger logger = Log.ForContext(typeof(PassengerHelper));


    internal SettingsManager settingsManager { get; }
    internal TrainManager trainManager { get; }
    internal StationManager stationManager { get; }
    internal UtilManager utilManager { get; }
    internal UIHelper UIHelper { get; }
    internal bool TestMode { get; } = true;
    internal Harmony harmony { get; }

    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    public PassengerHelper(string modId)
    {
        this.harmony = new Harmony(modId);
        this.harmony.PatchAll(GetType().Assembly);
        Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings = new Dictionary<string, PassengerLocomotiveSettings>();

        UIHelper uIHelper = new UIHelper();
        UtilManager utilManager = new UtilManager();
        SettingsManager settingsManager = new SettingsManager(uIHelper, utilManager);
        TrainManager trainManager = new TrainManager(settingsManager);
        StationManager stationManager = new StationManager(settingsManager, trainManager, orderedStations);

        this.UIHelper = uIHelper;
        this.utilManager = utilManager;
        this.settingsManager = settingsManager;
        this.trainManager = trainManager;
        this.stationManager = stationManager;
    }

}
