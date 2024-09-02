namespace PassengerHelperPlugin.Managers;

using System.Collections.Generic;
using Support;
using Serilog;
using Model;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;

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

    public bool GetPassengerLocomotive(BaseLocomotive locomotive, out PassengerLocomotive passengerLocomotive)
    {
        if (!this.passengerLocomotives.TryGetValue(locomotive.DisplayName, out passengerLocomotive))
        {
            return false;
        }

        return true;
    }

    public PassengerLocomotive GetPassengerLocomotive(BaseLocomotive locomotive)
    {
        logger.Information("Getting PassengerLocomotive for {0}", locomotive.DisplayName);
        if (!this.passengerLocomotives.TryGetValue(locomotive.DisplayName, out PassengerLocomotive passengerLocomotive))
        {
            logger.Information("Did not find existing PassengerLocomotive for {0}, looking for existing PassengerSettings and creating a new Passenger Locomotive", locomotive.DisplayName);
            passengerLocomotive = new PassengerLocomotive(locomotive, settingsManager.GetSettings(locomotive.DisplayName));

            logger.Information("Adding new Passenger Locomotive to internal Dictionary");

            this.passengerLocomotives.Add(locomotive.DisplayName, passengerLocomotive);
        }

        return passengerLocomotive;
    }
}