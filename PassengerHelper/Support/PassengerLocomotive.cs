namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Notices;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Definition;
using Model.Definition.Data;
using Model.OpsNew;
using RollingStock;
using Serilog;
using UI.EngineControls;
using static Model.Car;


public class PassengerLocomotive
{
    readonly ILogger logger = Log.ForContext(typeof(PassengerLocomotive));

    internal readonly BaseLocomotive _locomotive;

    public PassengerLocomotiveSettings Settings;

    // duplicates from settings
    public string CurrentStationId = "";
    public string PreviousStationId = "";
    public bool Arrived = false;
    public bool AtTerminusStationEast = false;
    public bool AtTerminusStationWest = false;
    public bool AtAlarka = false;
    public bool AtCochran = false;
    public bool TerminusStationProcedureComplete = false;
    public bool NonTerminusStationProcedureComplete = false;
    public bool CurrentlyStopped = false;
    public string CurrentReasonForStop = "";
    public bool StoppedForDiesel = false;
    public bool StoppedForCoal = false;
    public bool StoppedForWater = false;
    public bool StoppedNextStation = false;
    public bool StoppedTerminusStation = false;
    public bool StoppedStationPause = false;
    public bool StoppedWaitForFullLoad = false;
    public bool ReadyToDepart = false;
    public bool Departed = false;
    public bool Continue = false;

    public PassengerStop? CurrentStation;
    public PassengerStop? PreviousStation;


    public bool UnloadTransferComplete = false;
    public bool LoadTransferComplete = false;
    private readonly bool hasTender = false;
    private Car FuelCar;
    private int _dieselFuelSlotIndex;
    private float _dieselSlotMax;
    private int _coalSlotIndex;
    private float _coalSlotMax;
    private int _waterSlotIndex;
    private float _waterSlotMax;

    private Orders? cachedOrders = null;

    internal int settingsHash = 0;
    internal int stationSettingsHash = 0;

    private bool _selfSentOrders = false;

    public PassengerLocomotive(BaseLocomotive _locomotive, PassengerLocomotiveSettings Settings)
    {
        this._locomotive = _locomotive;
        if (_locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            hasTender = true;
        }

        this.Settings = Settings;

        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);

        persistence.ObserveOrders(delegate (Orders orders)
        {
            logger.Information("Orders changed. Orders are now: {0} and selfSentOrders is: {1}", orders, _selfSentOrders);
            if (!_selfSentOrders)
            {
                // if it is the start up of the game, the game sends an updated order to get the train moving again, so ignore it
                if (Settings.gameLoadFlag)
                {
                    Settings.gameLoadFlag = false;
                    return;
                }

                // if we aren't locked, we shouldn't change to unknown
                if (!Settings.DoTLocked)
                {
                    return;
                }

                Settings.DirectionOfTravel = DirectionOfTravel.UNKNOWN;
                Settings.DoTLocked = false;
            }
            _selfSentOrders = false;
        });

        LoadSettings(Settings);
        this.FuelCar = GetFuelCar();
    }

    private void LoadSettings(PassengerLocomotiveSettings Settings)
    {
        IEnumerable<PassengerStop> stations = PassengerStop.FindAll();

        if (Settings.CurrentStation.Length > 0)
        {
            this.CurrentStation = stations.FirstOrDefault((PassengerStop stop) => stop.identifier == Settings.PreviousStation);
            this.CurrentStationId = Settings.CurrentStation;
        }

        if (Settings.PreviousStation.Length > 0)
        {
            this.PreviousStation = stations.FirstOrDefault((PassengerStop stop) => stop.identifier == Settings.PreviousStation);
            this.PreviousStationId = Settings.PreviousStation;
        }

        this.Arrived = Settings.Arrived;
        this.AtTerminusStationWest = Settings.AtTerminusStationWest;
        this.AtTerminusStationEast = Settings.AtTerminusStationEast;
        this.AtAlarka = Settings.AtAlarka;
        this.AtCochran = Settings.AtCochran;
        this.TerminusStationProcedureComplete = Settings.TerminusStationProcedureComplete;
        this.NonTerminusStationProcedureComplete = Settings.NonTerminusStationProcedureComplete;
        this.CurrentlyStopped = Settings.CurrentlyStopped;
        this.CurrentReasonForStop = Settings.CurrentReasonForStop;
        this.StoppedForDiesel = Settings.StoppedForDiesel;
        this.StoppedForCoal = Settings.StoppedForCoal;
        this.StoppedForWater = Settings.StoppedForWater;
        this.StoppedNextStation = Settings.StoppedNextStation;
        this.StoppedTerminusStation = Settings.StoppedTerminusStation;
        this.StoppedStationPause = Settings.StoppedStationPause;
        this.StoppedWaitForFullLoad = Settings.StoppedWaitForFullLoad;
        this.ReadyToDepart = Settings.ReadyToDepart;
        this.Departed = Settings.Departed;
        this.Continue = Settings.Continue;

        if (Settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            Settings.DoTLocked = false;
        }

    }
    
    private float GetDieselLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar.GetLoadInfo(_dieselFuelSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveDiesel)
        {
            logger.Information("{0} has {1}gal of diesel fuel", _locomotive.DisplayName, loadInfo.Value.Quantity);
            level = loadInfo.Value.Quantity;
        }

        return level;
    }

    public bool CheckDieselFuelLevel(out float level)
    {
        level = GetDieselLevelForLoco();
        float minLevel = Settings.DieselLevel;
        float actualLevel = level / _dieselSlotMax;
        logger.Information("diesel: min level is: {0}, actual level is: {1}, max quantity is: {2}", minLevel, actualLevel, _dieselSlotMax);

        StoppedForDiesel = _locomotive.Archetype == CarArchetype.LocomotiveDiesel && actualLevel < minLevel;

        if (StoppedForDiesel)
        {
            logger.Information("{0} is low on diesel", _locomotive.DisplayName);
            CurrentReasonForStop = "stopped for low diesel";
            CurrentlyStopped = true;
        }
        return StoppedForDiesel;
    }

    private float GetCoalLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar.GetLoadInfo(_coalSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            logger.Information("{0} has {1}T of coal", _locomotive.DisplayName, loadInfo.Value.Quantity / 2000);
            level = loadInfo.Value.Quantity;
        }

        return level;
    }

    public bool CheckCoalLevel(out float level)
    {
        level = GetCoalLevelForLoco();
        float minLevel = Settings.CoalLevel;
        float actualLevel = level / _coalSlotMax;
        logger.Information("coal: min level is: {0}, actual level is: {1}, max quantity is: {2}", minLevel, actualLevel, _coalSlotMax);

        StoppedForCoal = hasTender && actualLevel < minLevel;

        if (StoppedForCoal)
        {
            logger.Information("{0} is low on coal", _locomotive.DisplayName);
            CurrentReasonForStop = "stopped for low coal";
            CurrentlyStopped = true;
        }
        return StoppedForCoal;
    }

    private float GetWaterLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar.GetLoadInfo(_waterSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            logger.Information("{0} has {1}gal of water", _locomotive.DisplayName, loadInfo.Value.Quantity);
            level = loadInfo.Value.Quantity;
        }

        return level;
    }

    public bool CheckWaterLevel(out float level)
    {
        level = GetWaterLevelForLoco();
        float minLevel = Settings.WaterLevel;
        float actualLevel = level / _waterSlotMax;
        logger.Information("water: min level is: {0}, actual level is: {1}, max quantity is: {2}", minLevel, actualLevel, _waterSlotMax);

        StoppedForWater = hasTender && actualLevel < minLevel;

        if (StoppedForWater)
        {
            logger.Information("{0} is low on water", _locomotive.DisplayName);
            CurrentReasonForStop = "stopped for low water";
            CurrentlyStopped = true;
        }
        return StoppedForWater;
    }

    public void ResetStoppedFlags()
    {
        logger.Information("resetting Stop flags for {0}", _locomotive.DisplayName);
        CurrentlyStopped = false;
        CurrentReasonForStop = "";
        StoppedForDiesel = false;
        StoppedForCoal = false;
        StoppedForWater = false;
    }
    public bool ShouldStayStopped()
    {
        logger.Information("checking if {0} should stay Stopped at current station", _locomotive.DisplayName);
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);

        if (cachedOrders == null)
        {
            cachedOrders = persistence.Orders;
        }

        if (Continue)
        {
            logger.Information("Continue button clicked. Continuing", _locomotive.DisplayName);
            CurrentlyStopped = false;

            _selfSentOrders = true;
            logger.Information("Cached orders are: ", cachedOrders);
            helper.SetOrdersValue(cachedOrders?.Mode(), cachedOrders?.Forward, cachedOrders?.MaxSpeedMph);
            cachedOrders = null;

            return false;
        }

        bool stayStopped = false;
        // train was requested to remain stopped
        if (Settings.StopAtNextStation)
        {
            logger.Information("StopAtNextStation is selected. {0} is remaining stopped.", _locomotive.DisplayName);
            stayStopped = true;
        }

        if (Settings.StopAtLastStation && Settings.Stations[CurrentStation.identifier].TerminusStation == true)
        {
            logger.Information("StopAtLastStation are selected. {0} is remaining stopped.", _locomotive.DisplayName);
            stayStopped = true;
        }

        if (Settings.Stations[CurrentStation.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Requested Pause at this station. {0} is remaining stopped.", _locomotive.DisplayName);
            stayStopped = true;
        }

        if (Settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Direction of Travel is still unknown. {0} is remaining stopped.", _locomotive.DisplayName);
            stayStopped = true;
        }

        // train is stopped because of low diesel, coal or water
        if (StoppedForDiesel || StoppedForCoal || StoppedForWater)
        {
            logger.Information("Locomotive is stopped due to either low diesel, coal or water. Rechecking settings to see if they have changed.");
            // first check if the setting has been set to false
            if (!Settings.StopForDiesel && StoppedForDiesel)
            {
                logger.Information("StopForDiesel no longer selected, resetting flag.");
                StoppedForDiesel = false;
            }

            if (!Settings.StopForCoal && StoppedForCoal)
            {
                logger.Information("StopForCoal no longer selected, resetting flag.");
                StoppedForCoal = false;
            }

            if (!Settings.StopForWater && StoppedForWater)
            {
                logger.Information("StopForWater no longer selected, resetting flag.");
                StoppedForWater = false;
            }

            stayStopped = StoppedForDiesel || StoppedForCoal || StoppedForWater;
        }

        if (stayStopped)
        {
            persistence.PassengerModeStatus = "Paused";

            _selfSentOrders = true;
            helper.SetOrdersValue(cachedOrders?.Mode(), cachedOrders?.Forward, 0);
        }
        else
        {
            _selfSentOrders = true;
            helper.SetOrdersValue(cachedOrders?.Mode(), cachedOrders?.Forward, cachedOrders?.MaxSpeedMph);
            cachedOrders = null;
        }

        return stayStopped;
    }

    public void ReverseLocoDirection()
    {
        _selfSentOrders = true;
        logger.Information("reversing loco direction");
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        logger.Information("Current direction is {0}", persistence.Orders.Forward == true ? "forward" : "backward");
        helper.SetOrdersValue(null, !persistence.Orders.Forward);
        logger.Information("new direction is {0}", persistence.Orders.Forward == true ? "forward" : "backward");
    }

    private Car GetFuelCar()
    {
        if (!hasTender)
        {
            _dieselFuelSlotIndex = _locomotive.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "diesel-fuel");
            _dieselSlotMax = _locomotive.Definition.LoadSlots.Where((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "diesel-fuel").First().MaximumCapacity;

            return _locomotive;
        }

        if (TryGetTender(out var tender))
        {
            _coalSlotIndex = tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "coal");
            _coalSlotMax = tender.Definition.LoadSlots.Where((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "coal").First().MaximumCapacity;

            _waterSlotIndex = tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "water");
            _waterSlotMax = tender.Definition.LoadSlots.Where((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "water").First().MaximumCapacity;

            return tender;
        }

        throw new Exception("steam engine with no tender. How????");
    }

    private bool TryGetTender(out Car tender)
    {
        if (hasTender && _locomotive.TryGetAdjacentCar(_locomotive.EndToLogical(End.R), out tender) && tender.Archetype == CarArchetype.Tender)
        {
            return true;
        }

        throw new Exception("steam engine with no tender. How????");
    }

    internal void SetPreviousStop(PassengerStop prevStop)
    {
        PreviousStation = prevStop;
        Settings.PreviousStation = prevStop.identifier;
    }

    public void PostNotice(string key, string message)
    {
        _locomotive.PostNotice(key, message);
    }
}
