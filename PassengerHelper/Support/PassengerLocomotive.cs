namespace PassengerHelper.Support;

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
using PassengerHelper.Plugin;

public class PassengerLocomotive
{
    internal BaseLocomotive _locomotive;

    private Car FuelCar;
    private int _dieselFuelSlotIndex;
    private float _dieselSlotMax;
    private int _coalSlotIndex;
    private float _coalSlotMax;
    private int _waterSlotIndex;
    private float _waterSlotMax;

    public bool isDiesal;
    public bool isSteam;
    public bool hasTender;

    internal int settingsHash = 0;
    internal int _stationSettingsHash = 0;
    internal int stationSettingsHash
    {
        get => _stationSettingsHash;
        set
        {
            if (_stationSettingsHash == value)
            {
                return;
            }

            _stationSettingsHash = value;

            TrainState state = trainStateManager.GetState(this);
            state.OnStationSettingsChangedReset();
            trainStateManager.SaveState(this, state);
        }
    }
    internal int stateHash = 0;

    private bool _selfSentOrders = false;

    internal KeyValueObject _keyValueObject;

    internal string KeyValueIdentifier_Settings => "moloch.passengerhelper.settings";
    internal string KeyValueIdentifier_State => "moloch.passengerhelper.state";

    internal TrainStateManager trainStateManager;
    internal SettingsManager settingsManager;

    public PassengerLocomotive(BaseLocomotive _locomotive, TrainStateManager trainStateManager, SettingsManager settingsManager)
    {
        this._locomotive = _locomotive;
        this._keyValueObject = _locomotive.KeyValueObject;
        this.trainStateManager = trainStateManager;
        this.settingsManager = settingsManager;

        this.isDiesal = _locomotive.Archetype == CarArchetype.LocomotiveDiesel;
        this.isSteam = hasTender = _locomotive.Archetype == CarArchetype.LocomotiveSteam;

        this.FuelCar = GetFuelCar();

        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier_Settings, _keyValueObject, AuthorizationRequirement.HostOnly);
        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier_State, _keyValueObject, AuthorizationRequirement.HostOnly);

        LoadSettings();
        LoadState();

        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);

        persistence.ObserveOrders(delegate (Orders orders)
        {
            Loader.Log($"Orders changed for {_locomotive.DisplayName}. Orders are now: {orders} and selfSentOrders is: {_selfSentOrders}");
            if (!_selfSentOrders)
            {
                TrainState state = trainStateManager.GetState(this);
                // if it is the start up of the game, the game sends an updated order to get the train moving again, so ignore it

                state.InferredDirectionOfTravel = DirectionOfTravel.UNKNOWN;
                trainStateManager.SaveState(this, state);
            }
            _selfSentOrders = false;
        });
    }

    public void LoadSettings()
    {
        Value dictionaryValue = _keyValueObject[KeyValueIdentifier_Settings];
        PassengerLocomotiveSettings pls;
        if (dictionaryValue.IsNull || !_keyValueObject.Keys.Contains(KeyValueIdentifier_Settings))
        {
            Loader.LogVerbose($"Creating new settings for {_locomotive.DisplayName}");
            pls = settingsManager.CreateNewSettings(this);
        }
        else
        {
            Loader.LogVerbose($"Loading existing settings for {_locomotive.DisplayName}");
            pls = settingsManager.LoadSettings(this);
        }

        this.settingsHash = pls.getSettingsHash();
    }

    public void LoadState()
    {
        Value dictionaryValue = _keyValueObject[KeyValueIdentifier_State];
        TrainState state;
        if (dictionaryValue.IsNull || !_keyValueObject.Keys.Contains(KeyValueIdentifier_State))
        {
            Loader.LogVerbose($"Creating new state for {_locomotive.DisplayName}");
            state = trainStateManager.CreateNewState(this);
        }
        else
        {
            Loader.LogVerbose($"Loading existing state for {_locomotive.DisplayName}");
            state = trainStateManager.LoadState(this);
        }

        if ((state.Arrived || state.ReadyToDepart) && state.CurrentStationId != null)
        {
            Loader.Log($"Train did not depart yet, selecting current station on passenger cars");
            foreach (Car coach in GetCoaches())
            {
                PassengerMarker marker = coach.GetPassengerMarker() ?? new PassengerMarker();

                HashSet<string> destinations = marker.Destinations;

                destinations.Add(state.CurrentStationId);
                StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, destinations.ToList()));
            }
        }

        if (state.Departed && state.CurrentStationId == null && state.PreviousStationId != null)
        {
            Loader.Log($"Train is not at a station, but is in route, re-selecting stations to be safe");

            foreach (Car coach in GetCoaches())
            {
                PassengerMarker marker = coach.GetPassengerMarker() ?? new PassengerMarker();

                HashSet<string> destinations = marker.Destinations;

                StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, destinations.ToList()));
            }
        }

        stateHash = state.GetHashCode();
    }

    public AutoEngineerMode GetMode()
    {
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);

        return helper.Mode;
    }

    public void StopAE()
    {
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);

        if (helper.Mode == AutoEngineerMode.Off) return;

        _selfSentOrders = true;
        Loader.Log($"Train is in AE mode and should be stopped, stopping {_locomotive.DisplayName}");
        helper.SetOrdersValue(null, null, 0);
    }

    public void StartAE()
    {
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);

        if (helper.Mode == AutoEngineerMode.Off) return;

        _selfSentOrders = true;
        Loader.Log($"Train is in AE mode and should now be moving, setting max speed on {_locomotive.DisplayName}");
        helper.SetOrdersValue(null, null, 45);
    }

    public List<Car> GetCoaches()
    {
        return _locomotive.EnumerateCoupled().Where(car => car.IsPassengerCar()).ToList();
    }

    public float GetDieselLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar.GetLoadInfo(_dieselFuelSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveDiesel)
        {
            Loader.Log($"{_locomotive.DisplayName} has {loadInfo.Value.Quantity}gal of diesel fuel");
            level = loadInfo.Value.Quantity;
        }

        return level / _dieselSlotMax;
    }

    public float GetCoalLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar.GetLoadInfo(_coalSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            Loader.Log($"{_locomotive.DisplayName} has {loadInfo.Value.Quantity / 2000}T of coal");
            level = loadInfo.Value.Quantity;
        }

        return level / _coalSlotMax;
    }

    public float GetWaterLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar.GetLoadInfo(_waterSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            Loader.Log($"{_locomotive.DisplayName} has {loadInfo.Value.Quantity}gal of water");
            level = loadInfo.Value.Quantity;
        }

        return level / _waterSlotMax;
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
    }

    public void SetStopOverrideActive()
    {
        TrainState state = this.trainStateManager.GetState(this);
        state.StopOverrideActive = true;
        state.StopOverrideStationId = state.CurrentStationId;
        state.ResetStoppedFlags();
        state.ReadyToDepart = true;
        trainStateManager.SaveState(this, state);

        PostNotice("ai-stop", "PassengerHelper: Continue requested");
    }
    public void ResetTrainState()
    {
        TrainState state = this.trainStateManager.GetState(this);

        state.Reset();
        ResetStateHash();

        trainStateManager.SaveState(this, state);

        PostNotice("ai-stop", $"PassengerHelper: Train State Reset Successful");
    }

    public void PostNotice(string key, string message)
    {
        _locomotive.PostNotice(key, message);
    }

    public void ResetSettingsHash()
    {
        this.settingsHash = 0;
    }

    public void ResetStateHash()
    {
        this.stateHash = 0;
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
