namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.State;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.OpsNew;
using RollingStock;
using Serilog;
using static Model.Car;


public class PassengerLocomotive
{
    readonly ILogger logger = Log.ForContext(typeof(PassengerLocomotive));
    public bool CurrentlyStopped = false;
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
    private int _coalSlotIndex;
    private int _waterSlotIndex;

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
        if (loadInfo.HasValue)
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
        if (loadInfo.HasValue)
        {
            logger.Information("{0} has {1}T of coal", _locomotive.DisplayName, loadInfo.Value.Quantity / 2000);
            level = loadInfo.Value.Quantity / 2000;
        }

        return level;
    }

    private float GetWaterLevelForLoco()
    {
        float level = 0f;
        CarLoadInfo? loadInfo = FuelCar().GetLoadInfo(_waterSlotIndex);
        if (loadInfo.HasValue)
        {
            logger.Information("{0} has {1}gal of water", _locomotive.DisplayName, loadInfo.Value.Quantity);
            level = loadInfo.Value.Quantity;
        }

        return level;
    }

    public bool CheckDieselFuelLevel(float minLevel, out float level)
    {
        level = GetDieselLevelForLoco();

        StoppedForDiesel = _locomotive.Archetype == CarArchetype.LocomotiveDiesel && level < minLevel;

        if (StoppedForDiesel)
        {
            logger.Information("{0} is low on diesel", _locomotive.DisplayName);
            CurrentReasonForStop = "stopped for low diesel";
            CurrentlyStopped = true;
        }
        return StoppedForDiesel;
    }

    public bool CheckCoalLevel(float minLevel, out float level)
    {
        level = GetCoalLevelForLoco();

        StoppedForCoal = hasTender && level < minLevel;

        if (StoppedForCoal)
        {
            logger.Information("{0} is low on coal", _locomotive.DisplayName);
            CurrentReasonForStop = "stopped for low coal";
            CurrentlyStopped = true;
        }
        return StoppedForCoal;
    }

    public bool CheckWaterLevel(float minLevel, out float level)
    {
        level = GetWaterLevelForLoco();

        StoppedForWater = hasTender && level < minLevel;

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

        if (AtTerminusStationWest && Settings.WaitForFullPassengersLastStation)
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
                    if(actualMarker.TotalPassengers < maxCapacity) {
                        logger.Information("Passenger car not full, remaining stopped");
                        return true;
                    }
                }
            }
            AtTerminusStationWest = false;
        }

        // for sandbox use, check every time
        if (StateManager.IsSandbox)
        {
            CheckCoalLevel(0, out float coal);
            CheckWaterLevel(0, out float water);
            CheckDieselFuelLevel(0, out float diesel);
        }

        return StoppedForDiesel || StoppedForCoal || StoppedForWater;
    }

    private Car FuelCar()
    {
        if (!hasTender)
        {
            _dieselFuelSlotIndex = _locomotive.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "diesel-fuel");
            return _locomotive;
        }

        if (TryGetTender(out var tender))
        {
            _coalSlotIndex = tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "coal");
            _waterSlotIndex = tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == "water");

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
