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
using UI.EngineControls;
using static Model.Car;
using System.Reflection;
using PassengerHelper.UMM;

public class PassengerLocomotive
{
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
            Loader.Log($"Orders changed for {_locomotive.DisplayName}. Orders are now: {orders} and selfSentOrders is: {_selfSentOrders}");
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
            Loader.Log($"Creating new settings for {_locomotive.DisplayName}");
            pls = settingsManager.CreateNewSettings(this);
        }
        else
        {
            Loader.Log($"Loading existing settings for {_locomotive.DisplayName}");
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
            Loader.Log($"Train did not depart yet, selecting current station on passenger cars");
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
            Loader.Log($"Train is not at a station, but is in route, re-selecting stations to be safe");

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
            Loader.Log($"{_locomotive.DisplayName} has {loadInfo.Value.Quantity}gal of diesel fuel");
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
        Loader.Log($"diesel: min level is: {minLevel}, actual level is: {actualLevel}, max quantity is: {_dieselSlotMax}");

        pls.TrainStatus.StoppedForDiesel = _locomotive.Archetype == CarArchetype.LocomotiveDiesel && actualLevel < minLevel;

        if (pls.TrainStatus.StoppedForDiesel)
        {
            Loader.Log($"{_locomotive.DisplayName} is low on diesel");
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
            Loader.Log($"{_locomotive.DisplayName} has {loadInfo.Value.Quantity / 2000}T of coal");
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
        Loader.Log($"coal: min level is: {minLevel}, actual level is: {actualLevel}, max quantity is: {_coalSlotMax}");

        pls.TrainStatus.StoppedForCoal = hasTender && actualLevel < minLevel;

        if (pls.TrainStatus.StoppedForCoal)
        {
            Loader.Log($"{_locomotive.DisplayName} is low on coal");
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
            Loader.Log($"{_locomotive.DisplayName} has {loadInfo.Value.Quantity}gal of water");
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
        Loader.Log($"water: min level is: {minLevel}, actual level is: {actualLevel}, max quantity is: {_waterSlotMax}");

        pls.TrainStatus.StoppedForWater = hasTender && actualLevel < minLevel;

        if (pls.TrainStatus.StoppedForWater)
        {
            Loader.Log($"{_locomotive.DisplayName} is low on water");
            pls.TrainStatus.CurrentReasonForStop = "stopped for low water";
            pls.TrainStatus.CurrentlyStopped = true;
        }

        settingsManager.SaveSettings(this, pls);

        return pls.TrainStatus.StoppedForWater;
    }

    public void ResetStoppedFlags()
    {
        Loader.Log($"resetting Stop flags for {_locomotive.DisplayName}");
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);
        pls.TrainStatus.ResetStoppedFlags();

        settingsManager.SaveSettings(this, pls);
    }

    public void ResetStatusFlags()
    {
        Loader.Log($"resetting Status flags for {_locomotive.DisplayName}");
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        pls.TrainStatus.ResetStatusFlags();

        settingsManager.SaveSettings(this, pls);
    }

    public bool ShouldStayStopped()
    {
        Loader.Log($"checking if {_locomotive.DisplayName} should stay Stopped at current station");
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(this);

        if (pls.TrainStatus.Continue)
        {
            Loader.Log($"Continue button clicked {_locomotive.DisplayName}. Continuing");
            ResetStoppedFlags();

            return false;
        }

        bool stayStopped = false;

        if (pls.TrainStatus.StoppedInsufficientStopAtStations && pls.StationSettings.Values.Where(s => s.StopAtStation).Count() < 2)
        {
            Loader.Log($"Still do not have at least 2 stop at stations selected. {_locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            pls.TrainStatus.StoppedInsufficientStopAtStations = false;
        }

        if (pls.TrainStatus.StoppedInsufficientTerminusStations && pls.StationSettings.Values.Where(s => s.TerminusStation).Count() != 2)
        {
            Loader.Log($"Still do not have 2 terminus stations selected. {_locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            pls.TrainStatus.StoppedInsufficientTerminusStations = false;
        }

        if (pls.TrainStatus.StoppedUnknownDirection && pls.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            Loader.Log($"Direction of Travel is still unknown. {_locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            pls.TrainStatus.StoppedUnknownDirection = false;
        }

        if (pls.TrainStatus.StoppedNextStation && pls.PauseAtNextStation)
        {
            Loader.Log($"StopAtNextStation is selected. {_locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            pls.TrainStatus.StoppedNextStation = false;
        }

        if (pls.TrainStatus.StoppedTerminusStation && pls.PauseAtTerminusStation && pls.StationSettings[CurrentStation.identifier].TerminusStation)
        {
            Loader.Log($"StopAtTerminusStation is selected. {_locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            pls.TrainStatus.StoppedTerminusStation = false;
        }

        if (CurrentStation != null)
        {
            if (pls.TrainStatus.StoppedStationPause && pls.StationSettings[CurrentStation.identifier].PauseAtStation)
            {
                Loader.Log($"Requested Pause at this station. {_locomotive.DisplayName} is remaining stopped.");
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
                        Loader.Log($"Passenger car not full, remaining stopped");
                        notFull = true;
                        break;
                    }

                    int maxCapacity = PassengerCapacity(coach, CurrentStation);
                    PassengerMarker actualMarker = marker.Value;
                    bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(CurrentStation.identifier);
                    bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
                    if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                    {
                        Loader.Log($"Passenger car not full, remaining stopped");
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
            Loader.Log($"Locomotive is stopped due to either low diesel, coal or water. Rechecking settings to see if they have changed.");
            // first check if the setting has been set to false
            if (pls.TrainStatus.StoppedForDiesel)
            {
                if (!pls.PauseForDiesel)
                {
                    Loader.Log($"StopForDiesel no longer selected, resetting flag.");
                    pls.TrainStatus.StoppedForDiesel = false;
                }
                else
                {
                    CheckDieselFuelLevel(out float level);
                    Loader.Log($"StoppedForDiesel is now: {pls.TrainStatus.StoppedForDiesel}");
                }
            }

            if (pls.TrainStatus.StoppedForCoal)
            {
                if (!pls.PauseForCoal)
                {
                    Loader.Log($"StopForCoal no longer selected, resetting flag.");
                    pls.TrainStatus.StoppedForCoal = false;
                }
                else
                {
                    CheckCoalLevel(out float level);
                    Loader.Log($"StoppedForCoal is now: {pls.TrainStatus.StoppedForCoal}");
                }

            }

            if (pls.TrainStatus.StoppedForWater)
            {
                if (!pls.PauseForWater)
                {
                    Loader.Log($"StopForWater no longer selected, resetting flag.");
                    pls.TrainStatus.StoppedForWater = false;
                }
                else
                {
                    CheckWaterLevel(out float level);
                    Loader.Log($"StoppedForWater is now: {pls.TrainStatus.StoppedForWater}");
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
        Loader.Log($"reversing loco direction");
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        AutoEngineerMode mode = helper.Mode;

        _selfSentOrders = true;
        Loader.Log($"Current direction is {(persistence.Orders.Forward == true ? "forward" : "backward")}");
        helper.SetOrdersValue(null, !persistence.Orders.Forward);
        Loader.Log($"new direction is {(persistence.Orders.Forward == true ? "forward" : "backward")}");

        if (mode == AutoEngineerMode.Off)
        {
            float direction = _keyValueObject[PropertyChange.KeyForControl(PropertyChange.Control.Reverser)].FloatValue;
            Loader.Log($"Current direction is {(direction == 1 ? "forward" : "backward")}");
            float newDirection = direction *= -1f;
            _locomotive.SendPropertyChange(PropertyChange.Control.Reverser, newDirection);
            Loader.Log($"new direction is {(newDirection == 1 ? "forward" : "backward")}");
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
