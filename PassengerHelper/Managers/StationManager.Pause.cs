using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

//Pause
public partial class StationManager
{
    private bool IsStoppedAndShouldStayStopped(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (settings.TrainStatus.CurrentlyStopped)
        {
            Loader.Log($"Train is currently Stopped due to: {settings.TrainStatus.CurrentReasonForStop}");
            if (passengerLocomotive.ShouldStayStopped())
            {
                return true;
            }
        }

        bool wasStopped = settings.TrainStatus.CurrentlyStopped || settings.TrainStatus.CurrentReasonForStop.Length > 0;

        settings.TrainStatus.CurrentlyStopped = false;
        settings.TrainStatus.CurrentReasonForStop = "";
        if (wasStopped)
        {
            settingsManager.SaveSettings(passengerLocomotive, settings);
        }

        return false;
    }

    private bool PauseAtCurrentStation(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        if (settings.PauseAtNextStation)
        {
            Loader.Log($"Pausing at station due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Requested pause at next station";
            settings.TrainStatus.StoppedNextStation = true;
            return true;
        }

        if (!settings.StationSettings.TryGetValue(passengerLocomotive.CurrentStation.identifier, out var curStationSettings))
        {
            Loader.Log($"No StationSettings entry for {passengerLocomotive.CurrentStation.identifier}; skipping pause logic.");
            return false;
        }

        if (curStationSettings.PauseAtStation)
        {
            Loader.Log($"Pausing at {passengerLocomotive.CurrentStation.DisplayName} due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Requested pause at " + passengerLocomotive.CurrentStation.DisplayName;
            settings.TrainStatus.StoppedStationPause = true;
            return true;
        }

        if (settings.PauseAtTerminusStation && curStationSettings.TerminusStation == true)
        {
            Loader.Log($"Pausing at {passengerLocomotive.CurrentStation.DisplayName} due to setting");
            passengerLocomotive.PostNotice("ai-stop", $"Paused at terminus station {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
            settings.TrainStatus.CurrentlyStopped = true;
            settings.TrainStatus.CurrentReasonForStop = "Requested pause at terminus station " + passengerLocomotive.CurrentStation.DisplayName;
            settings.TrainStatus.StoppedTerminusStation = true;
            return true;
        }

        if (settings.WaitForFullPassengersTerminusStation && curStationSettings.TerminusStation == true)
        {
            Loader.Log($"Waiting For full Passengers at terminus.");

            List<Car> coaches = passengerLocomotive._locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();
            foreach (Car coach in coaches)
            {
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (!marker.HasValue)
                {
                    Loader.Log($"Passenger car not full, remaining stopped");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    settings.TrainStatus.StoppedWaitForFullLoad = true;
                    return true;
                }

                LoadSlot loadSlot = coach.Definition.LoadSlots.FirstOrDefault((LoadSlot slot) => slot.RequiredLoadIdentifier == "passengers");
                int maxCapacity = (int)loadSlot.MaximumCapacity;
                PassengerMarker actualMarker = marker.Value;
                bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(passengerLocomotive.CurrentStation.identifier);
                bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
                if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                {
                    Loader.Log($"Passenger car not full, remaining stopped");
                    settings.TrainStatus.CurrentlyStopped = true;
                    settings.TrainStatus.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    settings.TrainStatus.StoppedWaitForFullLoad = true;
                    return true;
                }
            }

            Loader.Log($"Passengers are full, continuing.");
        }

        return false;
    }

    private bool HaveLowFuel(PassengerLocomotive passengerLocomotive, PassengerLocomotiveSettings settings)
    {
        bool retVal = false;
        if (settings.PauseForDiesel)
        {
            Loader.Log($"Requested stop for low diesel, checking level");
            // check diesel
            if (passengerLocomotive.CheckDieselFuelLevel(out float diesel))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        if (settings.PauseForCoal)
        {
            Loader.Log($"Requested stop for low coal, checking level");
            // check coal
            if (passengerLocomotive.CheckCoalLevel(out float coal))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        if (settings.PauseForWater)
        {
            Loader.Log($"Requested stop for low water, checking level");
            // check water
            if (passengerLocomotive.CheckWaterLevel(out float water))
            {
                passengerLocomotive.PostNotice("ai-stop", $"Stopped, low water at {Hyperlink.To(passengerLocomotive.CurrentStation)}.");
                retVal = true;
            }
        }

        return retVal;
    }
}
