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

    public StationManager(SettingsManager settingsManager, TrainManager trainManager, TrainStateManager trainStateManager, PassengerStopOrderManager passengerStopOrderManager)
    {
        this.trainManager = trainManager;
        this.settingsManager = settingsManager;
        this.trainStateManager = trainStateManager;
        this.passengerStopOrderManager = passengerStopOrderManager;
        this.getOrderedStations = () => passengerStopOrderManager.OrderedMainlineStopIds;
    }

}