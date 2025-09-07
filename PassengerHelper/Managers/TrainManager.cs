namespace PassengerHelperPlugin.Managers;

using System.Collections.Generic;
using Support;
using Serilog;
using Model;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model.Definition;
using Game.Messages;
using static Model.Car;
using Support.GameObjects;

public class TrainManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(TrainManager));
    private Dictionary<string, PassengerLocomotive> passengerLocomotives = new();

    private SettingsManager settingsManager;

    public TrainManager(SettingsManager settingsManager)
    {
        this.settingsManager = settingsManager;

        Messenger.Default.Register<MapDidUnloadEvent>(this, (@event) => this.passengerLocomotives.Clear());
    }

    public void SetLocomotive(string locomotiveName, PassengerLocomotive _passengerLocomotive)
    {

        if (!this.passengerLocomotives.TryGetValue(locomotiveName, out PassengerLocomotive passengerLocomotive))
        {
            this.passengerLocomotives.Add(locomotiveName, _passengerLocomotive);
        }
        else
        {
            this.passengerLocomotives[locomotiveName] = _passengerLocomotive;
        }
    }

    public bool GetPassengerLocomotive(BaseLocomotive locomotive, out PassengerLocomotive passengerLocomotive)
    {
        if (!this.passengerLocomotives.TryGetValue(locomotive.DisplayName, out passengerLocomotive))
        {
            return false;
        }

        return true;
    }

    public bool GetPassengerLocomotive(string locomotiveDisplayName, out PassengerLocomotive passengerLocomotive)
    {
        if (!this.passengerLocomotives.TryGetValue(locomotiveDisplayName, out passengerLocomotive))
        {
            return false;
        }

        return true;
    }

    public PassengerLocomotive GetPassengerLocomotive(BaseLocomotive locomotive)
    {
        logger.Debug("Getting PassengerLocomotive for {0}", locomotive.DisplayName);
        if (!this.passengerLocomotives.TryGetValue(locomotive.DisplayName, out PassengerLocomotive passengerLocomotive))
        {
            logger.Information("Did not find existing PassengerLocomotive for {0}, looking for existing PassengerSettings and creating a new Passenger Locomotive", locomotive.DisplayName);
            passengerLocomotive = new PassengerLocomotive(locomotive);

            logger.Debug("Adding new Passenger Locomotive to internal Dictionary");

            this.passengerLocomotives.Add(locomotive.DisplayName, passengerLocomotive);
        }

        return passengerLocomotive;
    }

    public PassengerLocomotive GetPassengerLocomotive(Car car)
    {
        logger.Debug("Getting PassengerLocomotive coupled to {0}", car.DisplayName);

        // find all cars coupled to car
        // filter to only locomotives
        // filter out MU locomotives
        List<Car> engines = car.EnumerateCoupled()
        .Where(car => car.IsLocomotive)
        .Where(loco => loco.ControlProperties[PropertyChange.Control.Mu] == false)
        .ToList();

        if (engines.Count > 1)
        {
            throw new System.Exception("More than 1 engine is coupled to car: " + car.DisplayName + " and those additional engines are NOT mu, therefore unable to determine which engine is the actual passenger locomotive");
        }

        return GetPassengerLocomotive((BaseLocomotive)engines[0]);
    }
}