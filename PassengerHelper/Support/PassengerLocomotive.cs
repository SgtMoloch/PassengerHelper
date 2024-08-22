namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.State;
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
    public bool CurrentlyStopped = false;
    public bool Continue = false;
    public string CurrentReasonForStop = "";
    public bool StoppedForDiesel = false;
    public bool StoppedForCoal = false;
    public bool StoppedForWater = false;
    public bool AtTerminusStationEast = false;
    public bool AtTerminusStationWest = false;
    private readonly BaseLocomotive _locomotive;
    private readonly bool hasTender = false;
    public bool HasMoreStops = false;
    public PassengerStop? CurrentStop;
    public PassengerLocomotiveSettings Settings;

    private int _dieselFuelSlotIndex;
    private float _dieselSlotMax;
    private int _coalSlotIndex;
    private float _coalSlotMax;
    private int _waterSlotIndex;
    private float _waterSlotMax;

    public PassengerLocomotive(BaseLocomotive _locomotive, PassengerLocomotiveSettings Settings)
    {
        this._locomotive = _locomotive;
        if (_locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            hasTender = true;
        }
        this.Settings = Settings;
    }

    private float GetDieselLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar().GetLoadInfo(_dieselFuelSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveDiesel)
        {
            logger.Information("{0} has {1}gal of diesel fuel", _locomotive.DisplayName, loadInfo.Value.Quantity);
            level = loadInfo.Value.Quantity;
        }

        return level;
    }

    private float GetCoalLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar().GetLoadInfo(_coalSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            logger.Information("{0} has {1}T of coal", _locomotive.DisplayName, loadInfo.Value.Quantity / 2000);
            level = loadInfo.Value.Quantity;
        }

        return level;
    }

    private float GetWaterLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar().GetLoadInfo(_waterSlotIndex);
        if (loadInfo.HasValue && _locomotive.Archetype == CarArchetype.LocomotiveSteam)
        {
            logger.Information("{0} has {1}gal of water", _locomotive.DisplayName, loadInfo.Value.Quantity);
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
        logger.Information("reseting Stop flags for {0}", _locomotive.DisplayName);
        CurrentlyStopped = false;
        CurrentReasonForStop = "";
        StoppedForDiesel = false;
        StoppedForCoal = false;
        StoppedForWater = false;
    }
    public bool ShouldStayStopped()
    {
        logger.Information("checking if {0} should stay Stopd at current station", _locomotive.DisplayName);
        if (Continue)
        {
            logger.Information("Continue button clicked. Continuing", _locomotive.DisplayName);
            return false;
        }
        // train was requested to remain stopped
        if (Settings.StopAtNextStation || Settings.StopAtLastStation)
        {
            logger.Information("StopAtNextStation or StopAtLastStation are selected. {0} is remaining stopped.", _locomotive.DisplayName);
            return true;
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
        }

        if ((AtTerminusStationWest || AtTerminusStationEast) && Settings.WaitForFullPassengersLastStation)
        {
            logger.Information("Checking to see if all passenger cars are full");
            IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);

            foreach (Car coach in coaches)
            {
                PassengerMarker? marker = coach.GetPassengerMarker();


                if (marker != null && marker.HasValue)
                {
                    LoadSlot loadSlot = coach.Definition.LoadSlots.FirstOrDefault((LoadSlot slot) => slot.RequiredLoadIdentifier == "passengers");
                    int maxCapacity = (int)loadSlot.MaximumCapacity;
                    PassengerMarker actualMarker = marker.Value;
                    if (actualMarker.TotalPassengers < maxCapacity)
                    {
                        logger.Information("Passenger car not full, remaining stopped");
                        return true;
                    }
                }
            }
            AtTerminusStationWest = false;
        }

        return StoppedForDiesel || StoppedForCoal || StoppedForWater;
    }

    public void ReverseLocoDirection()
    {
        logger.Information("reversing loco direction");
        AutoEngineerPersistence persistence = new(_locomotive.KeyValueObject);
        AutoEngineerOrdersHelper helper = new(_locomotive, persistence);
        logger.Information("Current direction is {0}", persistence.Orders.Forward == true ? "forward" : "backward");
        helper.SetOrdersValue(null, !persistence.Orders.Forward);
        logger.Information("new direction is {0}", persistence.Orders.Forward == true ? "forward" : "backward");
    }

    private Car FuelCar()
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
}
