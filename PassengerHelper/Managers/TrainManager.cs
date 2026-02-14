namespace PassengerHelper.Managers;

using System.Collections.Generic;
using Support;
using Model;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model.Definition;
using Game.Messages;
using static Model.Car;
using Support.GameObjects;
using PassengerHelper.Plugin;

public class TrainManager
{
    private Dictionary<string, PassengerLocomotive> passengerLocomotives = new();

    private SettingsManager settingsManager;
    private TrainStateManager trainStateManager;

    public TrainManager(SettingsManager settingsManager, TrainStateManager trainStateManager)
    {
        this.settingsManager = settingsManager;
        this.trainStateManager = trainStateManager;

        Messenger.Default.Register<MapDidUnloadEvent>(this, (@event) => this.passengerLocomotives.Clear());
    }

    public PassengerLocomotive GetPassengerLocomotive(BaseLocomotive locomotive)
    {
        Loader.LogVerbose($"Getting PassengerLocomotive for {locomotive.DisplayName}");
        if (!this.passengerLocomotives.TryGetValue(locomotive.id, out PassengerLocomotive passengerLocomotive))
        {
            Loader.Log($"Did not find existing PassengerLocomotive for {locomotive.DisplayName}, looking for existing PassengerSettings and creating a new Passenger Locomotive");
            passengerLocomotive = new PassengerLocomotive(locomotive, trainStateManager, settingsManager);

            Loader.LogVerbose($"Adding new Passenger Locomotive to internal Dictionary");

            this.passengerLocomotives.Add(locomotive.id, passengerLocomotive);
        }

        return passengerLocomotive;
    }

    public PassengerLocomotive GetPassengerLocomotive(string locoId)
    {
        Loader.LogVerbose($"Getting PassengerLocomotive for {locoId}");
        return passengerLocomotives.TryGetValue(locoId, out var pl) ? pl : null;
    }

    public PassengerLocomotive GetPassengerLocomotive(Car car)
    {
        Loader.LogVerbose($"Getting PassengerLocomotive coupled to {car.DisplayName}");

        // find all cars coupled to car
        // filter to only locomotives
        // filter out MU locomotives
        List<Car> engines = car.EnumerateCoupled()
        .Where(car => car.IsLocomotive)
        .Where(loco => loco.ControlProperties[PropertyChange.Control.Mu] == false)
        .ToList();

        if (engines.Count == 0)
        {
            throw new System.Exception($"No non-MU locomotive coupled to {car.DisplayName}");
        }

        if (engines.Count > 1)
        {
            throw new System.Exception("More than 1 engine is coupled to car: " + car.DisplayName + " and those additional engines are NOT mu, therefore unable to determine which engine is the actual passenger locomotive");
        }

        return GetPassengerLocomotive((BaseLocomotive)engines[0]);
    }

    public bool TryGetPassengerLocomotive(Car car, out PassengerLocomotive pl)
    {
        Loader.LogVerbose($"Getting PassengerLocomotive coupled to {car.DisplayName}");
        pl = null;

        // find all cars coupled to car
        // filter to only locomotives
        // filter out MU locomotives
        List<Car> engines = car.EnumerateCoupled()
        .Where(car => car.IsLocomotive)
        .Where(loco => loco.ControlProperties[PropertyChange.Control.Mu] == false)
        .ToList();

        if (engines.Count == 0)
        {
            return false;
        }

        if (engines.Count > 1)
        {
            return false;
        }
        
        pl = GetPassengerLocomotive((BaseLocomotive)engines[0]);

        return true;
    }
}