namespace PassengerHelper.Support.GameObjects;

using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.AccessControl;
using Game.Messages;
using Game.Notices;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Managers;
using RollingStock;
using Serilog;
using UI.EngineControls;
using static Model.Car;
using System.Reflection;

public class PassengerLocomotive
{
    readonly ILogger logger = Log.ForContext(typeof(PassengerLocomotive));

    internal BaseLocomotive _locomotive;

    private PassengerStop? _currentStop = null;
    private PassengerStop? _previousStop = null;
    public bool StationProcedureRan { get; set; } = false;
    public PassengerStop? CurrentStation
    {
        get
        {
            return _currentStop;
        }
        set
        {
            _currentStop = value;
            PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

            if (value != null)
            {
                pls.TrainStatus.CurrentStation = value.identifier;
            }
            else
            {
                pls.TrainStatus.CurrentStation = "";
            }

            settingsManager.SaveSettings(this, pls);
        }
    }
    public PassengerStop? PreviousStation
    {
        get
        {
            return _previousStop;
        }
        set
        {
            _previousStop = value;
            PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

            if (value != null)
            {
                pls.TrainStatus.PreviousStation = value.identifier;

            }
            else
            {
                pls.TrainStatus.PreviousStation = "";
            }

            settingsManager.SaveSettings(this, pls);
        }
    }
    private bool hasTender = false;
    private Car FuelCar;
    private int _dieselFuelSlotIndex;
    private float _dieselSlotMax;
    private int _coalSlotIndex;
    private float _coalSlotMax;
    private int _waterSlotIndex;
    private float _waterSlotMax;

    internal int settingsHash = 0;
    internal int stationSettingsHash = 0;

    private bool _selfSentOrders = false;

    internal KeyValueObject _keyValueObject;

    private MethodInfo carCapacity = typeof(PassengerStop).GetMethod("PassengerCapacity", BindingFlags.NonPublic | BindingFlags.Instance);

    internal string KeyValueIdentifier => "moloch.passengerhelper";

    internal SettingsManager settingsManager;

    public PassengerLocomotive(BaseLocomotive _locomotive, SettingsManager settingsManager)
    {
        this._locomotive = _locomotive;
        this._keyValueObject = _locomotive.KeyValueObject;
        this.settingsManager = settingsManager;

        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier, _keyValueObject, AuthorizationRequirement.HostOnly);

        if (_locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            hasTender = true;
        }

        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        this.FuelCar = GetFuelCar();

        LoadSettings();

        persistence.ObserveOrders(delegate (Orders orders)
        {
            logger.Information("Orders changed for {0}. Orders are now: {1} and selfSentOrders is: {2}", _locomotive.DisplayName, orders, _selfSentOrders);
            if (!_selfSentOrders)
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);
                // if it is the start up of the game, the game sends an updated order to get the train moving again, so ignore it
                if (pls.gameLoadFlag)
                {
                    pls.gameLoadFlag = false;

                    settingsManager.SaveSettings(this, pls);
                    return;
                }

                // if we aren't locked, we shouldn't change to unknown
                if (!pls.DoTLocked)
                {
                    return;
                }

                pls.DirectionOfTravel = DirectionOfTravel.UNKNOWN;
                pls.DoTLocked = false;
                settingsManager.SaveSettings(this, pls);
            }
            _selfSentOrders = false;
        });
    }

    public void LoadSettings()
    {
        Value dictionaryValue = _keyValueObject[KeyValueIdentifier];
        PassengerLocomotiveSettings pls;
        if (dictionaryValue.IsNull || !_keyValueObject.Keys.Contains(KeyValueIdentifier))
        {
            logger.Information("Creating new settings for {0}", _locomotive.DisplayName);
            pls = settingsManager.CreateNewSettings(this);
        }
        else
        {
            logger.Information("Loading existing settings for {0}", _locomotive.DisplayName);
            pls = settingsManager.LoadSettings(this);
        }

        IEnumerable<PassengerStop> stations = PassengerStop.FindAll();

        if (pls.TrainStatus.CurrentStation.Length > 0)
        {
            this.CurrentStation = stations.FirstOrDefault((PassengerStop stop) => stop.identifier == pls.TrainStatus.CurrentStation);
        }

        if (pls.TrainStatus.PreviousStation.Length > 0)
        {
            this.PreviousStation = stations.FirstOrDefault((PassengerStop stop) => stop.identifier == pls.TrainStatus.PreviousStation);
        }

        if ((pls.TrainStatus.Arrived || pls.TrainStatus.ReadyToDepart) && this.CurrentStation != null)
        {
            logger.Information("Train did not depart yet, selecting current station on passenger cars");
            _locomotive.velocity = 0;

            foreach (Car coach in GetCoaches())
            {
                PassengerMarker marker = coach.GetPassengerMarker() ?? new PassengerMarker();

                HashSet<string> destinations = marker.Destinations;

                destinations.Add(pls.TrainStatus.CurrentStation);
                StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, destinations.ToList()));
            }
        }

        if (pls.TrainStatus.Departed && this.CurrentStation == null && this.PreviousStation != null)
        {
            logger.Information("Train is not at a station, but is in route, re-selecting stations to be safe");

            foreach (Car coach in GetCoaches())
            {
                PassengerMarker marker = coach.GetPassengerMarker() ?? new PassengerMarker();

                HashSet<string> destinations = marker.Destinations;

                StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, destinations.ToList()));
            }
        }



        if (pls.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            pls.DoTLocked = false;
            settingsManager.SaveSettings(this, pls);
        }

        settingsHash = pls.getSettingsHash();
        stationSettingsHash = pls.getStationSettingsHash();

    }

    public AutoEngineerMode GetMode()
    {
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);

        return helper.Mode;
    }

    public List<Car> GetCoaches()
    {
        return _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();
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
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        float minLevel = pls.DieselLevel;
        float actualLevel = level / _dieselSlotMax;
        logger.Information("diesel: min level is: {0}, actual level is: {1}, max quantity is: {2}", minLevel, actualLevel, _dieselSlotMax);

        pls.TrainStatus.StoppedForDiesel = _locomotive.Archetype == CarArchetype.LocomotiveDiesel && actualLevel < minLevel;

        if (pls.TrainStatus.StoppedForDiesel)
        {
            logger.Information("{0} is low on diesel", _locomotive.DisplayName);
            pls.TrainStatus.CurrentReasonForStop = "stopped for low diesel";
            pls.TrainStatus.CurrentlyStopped = true;
        }
        settingsManager.SaveSettings(this, pls);

        return pls.TrainStatus.StoppedForDiesel;
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
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        float minLevel = pls.CoalLevel;
        float actualLevel = level / _coalSlotMax;
        logger.Information("coal: min level is: {0}, actual level is: {1}, max quantity is: {2}", minLevel, actualLevel, _coalSlotMax);

        pls.TrainStatus.StoppedForCoal = hasTender && actualLevel < minLevel;

        if (pls.TrainStatus.StoppedForCoal)
        {
            logger.Information("{0} is low on coal", _locomotive.DisplayName);
            pls.TrainStatus.CurrentReasonForStop = "stopped for low coal";
            pls.TrainStatus.CurrentlyStopped = true;
        }
        settingsManager.SaveSettings(this, pls);

        return pls.TrainStatus.StoppedForCoal;
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
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        float minLevel = pls.WaterLevel;
        float actualLevel = level / _waterSlotMax;
        logger.Information("water: min level is: {0}, actual level is: {1}, max quantity is: {2}", minLevel, actualLevel, _waterSlotMax);

        pls.TrainStatus.StoppedForWater = hasTender && actualLevel < minLevel;

        if (pls.TrainStatus.StoppedForWater)
        {
            logger.Information("{0} is low on water", _locomotive.DisplayName);
            pls.TrainStatus.CurrentReasonForStop = "stopped for low water";
            pls.TrainStatus.CurrentlyStopped = true;
        }

        settingsManager.SaveSettings(this, pls);

        return pls.TrainStatus.StoppedForWater;
    }

    public void ResetStoppedFlags()
    {
        logger.Information("resetting Stop flags for {0}", _locomotive.DisplayName);
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);
        pls.TrainStatus.ResetStoppedFlags();

        settingsManager.SaveSettings(this, pls);
    }

    public void ResetStatusFlags()
    {
        logger.Information("resetting Status flags for {0}", _locomotive.DisplayName);
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        pls.TrainStatus.ResetStatusFlags();

        settingsManager.SaveSettings(this, pls);
    }

    public bool ShouldStayStopped()
    {
        logger.Information("checking if {0} should stay Stopped at current station", _locomotive.DisplayName);
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        if (pls.TrainStatus.Continue)
        {
            logger.Information("Continue button clicked. Continuing", _locomotive.DisplayName);
            ResetStoppedFlags();

            return false;
        }

        bool stayStopped = false;

        if (pls.TrainStatus.StoppedInsufficientStopAtStations && pls.StationSettings.Values.Where(s => s.StopAtStation).Count() < 2)
        {
            logger.Information("Still do not have at least 2 stop at stations selected. {0} is remaining stopped.", _locomotive.DisplayName);
        }
        else
        {
            pls.TrainStatus.StoppedInsufficientStopAtStations = false;
        }

        if (pls.TrainStatus.StoppedInsufficientTerminusStations && pls.StationSettings.Values.Where(s => s.TerminusStation).Count() != 2)
        {
            logger.Information("Still do not have 2 terminus stations selected. {0} is remaining stopped.", _locomotive.DisplayName);
        }
        else
        {
            pls.TrainStatus.StoppedInsufficientTerminusStations = false;
        }

        if (pls.TrainStatus.StoppedUnknownDirection && pls.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Direction of Travel is still unknown. {0} is remaining stopped.", _locomotive.DisplayName);
        }
        else
        {
            pls.TrainStatus.StoppedUnknownDirection = false;
        }

        if (pls.TrainStatus.StoppedNextStation && pls.PauseAtNextStation)
        {
            logger.Information("StopAtNextStation is selected. {0} is remaining stopped.", _locomotive.DisplayName);
        }
        else
        {
            pls.TrainStatus.StoppedNextStation = false;
        }

        if (pls.TrainStatus.StoppedTerminusStation && pls.PauseAtTerminusStation && pls.StationSettings[CurrentStation.identifier].TerminusStation)
        {
            logger.Information("StopAtTerminusStation is selected. {0} is remaining stopped.", _locomotive.DisplayName);
        }
        else
        {
            pls.TrainStatus.StoppedTerminusStation = false;
        }

        if (CurrentStation != null)
        {
            if (pls.TrainStatus.StoppedStationPause && pls.StationSettings[CurrentStation.identifier].PauseAtStation)
            {
                logger.Information("Requested Pause at this station. {0} is remaining stopped.", _locomotive.DisplayName);
            }
            else
            {
                pls.TrainStatus.StoppedStationPause = false;
            }

            if (pls.TrainStatus.StoppedWaitForFullLoad && pls.WaitForFullPassengersTerminusStation)
            {
                List<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();
                bool notFull = false;
                foreach (Car coach in coaches)
                {
                    PassengerMarker? marker = coach.GetPassengerMarker();
                    if (marker == null)
                    {
                        logger.Information("Passenger car not full, remaining stopped");
                        notFull = true;
                        break;
                    }

                    int maxCapacity = PassengerCapacity(coach, CurrentStation);
                    PassengerMarker actualMarker = marker.Value;
                    bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(CurrentStation.identifier);
                    bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
                    if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                    {
                        logger.Information("Passenger car not full, remaining stopped");
                        notFull = true;
                        break;
                    }
                }

                pls.TrainStatus.StoppedWaitForFullLoad = notFull;
            }
        }


        // train is stopped because of low diesel, coal or water
        if (pls.TrainStatus.StoppedForDiesel || pls.TrainStatus.StoppedForCoal || pls.TrainStatus.StoppedForWater)
        {
            logger.Information("Locomotive is stopped due to either low diesel, coal or water. Rechecking settings to see if they have changed.");
            // first check if the setting has been set to false
            if (pls.TrainStatus.StoppedForDiesel)
            {
                if (!pls.PauseForDiesel)
                {
                    logger.Information("StopForDiesel no longer selected, resetting flag.");
                    pls.TrainStatus.StoppedForDiesel = false;
                }
                else
                {
                    CheckDieselFuelLevel(out float level);
                    logger.Information("StoppedForDiesel is now: {0}", pls.TrainStatus.StoppedForDiesel);
                }
            }

            if (pls.TrainStatus.StoppedForCoal)
            {
                if (!pls.PauseForCoal)
                {
                    logger.Information("StopForCoal no longer selected, resetting flag.");
                    pls.TrainStatus.StoppedForCoal = false;
                }
                else
                {
                    CheckCoalLevel(out float level);
                    logger.Information("StoppedForCoal is now: {0}", pls.TrainStatus.StoppedForCoal);
                }

            }

            if (pls.TrainStatus.StoppedForWater)
            {
                if (!pls.PauseForWater)
                {
                    logger.Information("StopForWater no longer selected, resetting flag.");
                    pls.TrainStatus.StoppedForWater = false;
                }
                else
                {
                    CheckWaterLevel(out float level);
                    logger.Information("StoppedForWater is now: {0}", pls.TrainStatus.StoppedForWater);
                }

            }
        }

        stayStopped = pls.TrainStatus.ShouldStayStopped();

        if (stayStopped)
        {
            if (!pls.TrainStatus.StoppedUnknownDirection && !pls.TrainStatus.StoppedInsufficientTerminusStations && !pls.TrainStatus.StoppedInsufficientStopAtStations)
            {
                persistence.PassengerModeStatus = "Paused";
            }
        }

        settingsManager.SaveSettings(this, pls);


        return stayStopped;
    }

    public void ReverseLocoDirection()
    {
        logger.Information("reversing loco direction");
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        AutoEngineerMode mode = helper.Mode;

        _selfSentOrders = true;
        logger.Information("Current direction is {0}", persistence.Orders.Forward == true ? "forward" : "backward");
        helper.SetOrdersValue(null, !persistence.Orders.Forward);
        logger.Information("new direction is {0}", persistence.Orders.Forward == true ? "forward" : "backward");

        if (mode == AutoEngineerMode.Off)
        {
            float direction = _keyValueObject[PropertyChange.KeyForControl(PropertyChange.Control.Reverser)].FloatValue;
            logger.Information("Current direction is {0}", direction == 1 ? "forward" : "backward");
            float newDirection = direction *= -1f;
            _locomotive.SendPropertyChange(PropertyChange.Control.Reverser, newDirection);
            logger.Information("new direction is {0}", newDirection == 1 ? "forward" : "backward");
            return;
        }
    }

    public void PostNotice(string key, string message)
    {
        _locomotive.PostNotice(key, message);
    }

    public void ResetSettingsHash()
    {
        this.settingsHash = 0;
        this.stationSettingsHash = 0;
    }

    public int PassengerCapacity(Car car, PassengerStop ps)
    {
        return (int)carCapacity.Invoke(ps, new object[] { car });
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
        if (hasTender)
        {
            if (_locomotive.TryGetAdjacentCar(_locomotive.EndToLogical(End.R), out tender) && tender.Archetype == CarArchetype.Tender)
            {
                return true;
            }
            if (_locomotive.Definition.LoadSlots.FindIndex((Predicate<LoadSlot>)(loadSlot => loadSlot.RequiredLoadIdentifier == "coal")) != -1)
            {
                tender = _locomotive;
                return true;
            }
        }

        throw new Exception("steam engine with no tender. How????");
    }


}
