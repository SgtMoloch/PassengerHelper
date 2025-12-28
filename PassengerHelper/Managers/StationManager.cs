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
    internal PassengerStopOrderManager passengerStopOrderManager;

    private readonly HashSet<string> _armedDepartures = new();

    private const string cochranIdentifier = "cochran";
    private const string alarkaIdentifier = "alarka";
    private const string almondIdentifier = "almond";
    private const string alarkajctIdentifier = "alarkajct";

    public StationManager(SettingsManager settingsManager, TrainManager trainManager, TrainStateManager trainStateManager, PassengerStopOrderManager passengerStopOrderManager)
    {
        this.trainManager = trainManager;
        this.settingsManager = settingsManager;
        this.trainStateManager = trainStateManager;
        this.passengerStopOrderManager = passengerStopOrderManager;
        this.getOrderedStations = () => passengerStopOrderManager.OrderedMainlineStopIds;
    }

}