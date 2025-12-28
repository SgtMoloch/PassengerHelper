using System;
using System.Collections.Generic;

namespace PassengerHelper.Managers;

// Constructor
public partial class StationManager
{
    internal readonly Func<List<string>> getOrderedStations;

    internal TrainManager trainManager;
    internal SettingsManager settingsManager;
    internal TrainStateManager trainStateManager;

    private readonly HashSet<string> _armedDepartures = new();

    private const string cochranIdentifier = "cochran";
    private const string alarkaIdentifier = "alarka";
    private const string almondIdentifier = "almond";
    private const string alarkajctIdentifier = "alarkajct";

    public StationManager(SettingsManager settingsManager, TrainManager trainManager, TrainStateManager trainStateManager, Func<List<string>> getOrderedStations)
    {
        this.getOrderedStations = getOrderedStations;
        this.trainManager = trainManager;
        this.settingsManager = settingsManager;
        this.trainStateManager = trainStateManager;
    }

}