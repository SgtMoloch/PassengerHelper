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

    public bool TryGetPassengerLocomotive(Car car, out PassengerLocomotive pl)
    {
        Loader.LogVerbose($"Getting PassengerLocomotive coupled to {car.DisplayName}");
        pl = null;

        if (!TryGetPassengerLocomotive(car, new HashSet<string>(), out BaseLocomotive lm, out string failReason))
        {
            Loader.LogError($"PassenegerHelperTick: skipping {car.DisplayName} because: {failReason}");
            return false;
        }

        pl = GetPassengerLocomotive(lm);

        return true;
    }

    public bool TryGetPassengerLocomotive(Car car, HashSet<string> visitedCarIds, out BaseLocomotive lm, out string failReason)
    {
        lm = null;
        failReason = "";

        string localFail = null;

        BaseLocomotive found = null;

        foreach (Car c in car.EnumerateCoupled())
        {
            if (localFail != null) break;
            Check(c);
        }
        foreach (Car c in car.EnumerateCoupled(Car.LogicalEnd.B))
        {
            if (localFail != null) break;
            Check(c);
        }

        void Check(Car c)
        {
            if (c == null) return;

            if (string.IsNullOrEmpty(c.id)) return;

            if (!visitedCarIds.Add(c.id)) return;

            if (c is not BaseLocomotive loco) return;

            bool isMu = c.ControlProperties[PropertyChange.Control.Mu];

            if (!isMu)
            {
                if (found == null)
                {
                    found = loco;
                }
                else if (found.id != loco.id)
                {
                    localFail = $"More than 1 engine is coupled to car: {car.DisplayName} and those additional engines are NOT mu, therefore unable to determine which engine is the actual passenger locomotive";
                    return;
                }
            }
        }

        if (localFail != null)
        {
            failReason = localFail;
            return false;
        }

        if (found == null)
        {
            failReason = $"No non-MU locomotive coupled to {car.DisplayName}";
            return false;
        }

        lm = found;
        return true;
    }
}