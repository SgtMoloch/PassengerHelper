using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Model;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;

// core
public partial class StationManager
{
    class StationProcedureContext
    {
        internal Dictionary<string, int> OrderIndex = new(StringComparer.Ordinal);
        internal Dictionary<string, int> StopAtIndex = new(StringComparer.Ordinal);
        internal Dictionary<string, int> TerminusIndex = new(StringComparer.Ordinal);
        internal Dictionary<string, int> PickupIndex = new(StringComparer.Ordinal);
        internal Dictionary<string, int> TransferIndex = new(StringComparer.Ordinal);


        internal List<string> OrderedTerminusStations;
        internal List<string> OrderedStopAtStations;
        internal List<string> OrderedPickupStations;
        internal List<string> OrderedTransferStations;

        internal string EastTerminusId => OrderedTerminusStations[0];
        internal string WestTerminusId => OrderedTerminusStations[1];

        internal List<Car> Coaches;

        internal PassengerStop CurrentStation;
    }

    /* 
        returns true if the train should stay stopped at the station
        returns false if there is no reason for the train to stay stopped and defer to base game logic for departure
     */
    public void HandleTrainAtStation(BaseLocomotive locomotive, PassengerStop currentStop)
    {
        PassengerLocomotive pl = this.trainManager.GetPassengerLocomotive(locomotive);
        PassengerLocomotiveSettings pls = this.settingsManager.GetSettings(pl);
        TrainState state = this.trainStateManager.GetState(pl);

        StationProcedureContext ctx = new StationProcedureContext();

        /* 
            1. Check if PH is disabled, if so, return false for base game departure logic
         */
        if (pls.Disable)
        {
            Loader.Log($"Passenger Helper is currently disabled for {locomotive.DisplayName} due to disabled setting.");
            // return to original game logic
            return;
        }

        Loader.LogVerbose($"AT STATION: loco={locomotive.DisplayName} id={locomotive.id} Station={currentStop.DisplayName}");

        /* 
            2. build order indexe, saves computation time later on when doing index based checking
         */
        Dictionary<string, int> orderIndex = BuildOrderIndex(getOrderedStations());

        List<string> orderedStopAtStations = pls.StationSettings
            .Where(kvp => kvp.Value.StopAtStation)
            .Select(kvp => kvp.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        List<string> orderedTerminusStations = pls.StationSettings
            .Where(kvp => kvp.Value.TerminusStation)
            .Select(kvp => kvp.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        List<string> orderedPickupStations = pls.StationSettings
            .Where(kvp => kvp.Value.PickupPassengersForStation)
            .Select(kvp => kvp.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        List<string> orderedTransferStations = pls.StationSettings
            .Where(s => s.Value.TransferStation && s.Value.StopAtStation)
            .Select(station => station.Key)
            .OrderBy(id => GetOrder(orderIndex, id))
            .ToList();

        /* 
            3. Check station is supported -> check station is a stopat station -> validate settings
            Validate that there are at least 2 stopat stations and 2 terminus stations and the current station is supported
            if not, quick return
         */

        if (!orderIndex.ContainsKey(currentStop.identifier))
        {
            string reason = "Station is not supported";
            if (state.CurrentReasonForStop != reason)
            {
                Loader.Log($"Current station is not supported.");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: Current station is not supported by PassengerHelper.\"");

                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
                state.StoppedUnsupportedStation = true;
            }

            return;
        }

        if (!orderedStopAtStations.Contains(currentStop.identifier))
        {
            NotAtASelectedStationProcedure(pl, pls, state, currentStop, orderIndex, orderedTerminusStations);
            Loader.LogVerbose("not at a selected station quick return");
            trainStateManager.SaveState(pl, state);
            return;
        }

        ctx.OrderIndex = orderIndex;
        ctx.CurrentStation = currentStop;
        ctx.OrderedStopAtStations = orderedStopAtStations;
        ctx.OrderedPickupStations = orderedPickupStations;
        ctx.OrderedTerminusStations = orderedTerminusStations;
        ctx.OrderedTransferStations = orderedTransferStations;

        bool validSettings = ValidateStationSettings(pl, pls, state, ctx);
        trainStateManager.SaveState(pl, state);

        if (!validSettings)
        {
            Loader.LogVerbose("invalid settings quick return");
            pl.StopAE();
            return;
        }

        /* 
            4. arrival transition; if the state of the train is not indicative of being at this station, update the state, and reset the settings hash and state status flags, disarm departure for safety
         */
        if (currentStop != state.CurrentStation)
        {
            Loader.Log($"Train " + locomotive.DisplayName + " has arrived at station " + currentStop.DisplayName);
            state.CurrentStation = currentStop;
            pl.ResetSettingsHash();
            state.ResetStatusFlags();
            state.Arrived = true;
            state.StopOverrideActive = false;
            state.StopOverrideStationId = null;
            DisarmDepartureCheck(pl);
            trainStateManager.SaveState(pl, state);
            pl.StopAE();
        }

        /* 
            5. Check if stopoverride is active and if so, quick return
         */
        bool stopOverrideActive = state.StopOverrideActive && state.StopOverrideStationId == state.CurrentStationId;

        if (stopOverrideActive)
        {
            Loader.LogVerbose("stop override quick return");
            ArmDepartureCheck(pl);
            return;
        }

        /*
            6. if the train is ready to depart and the settings have not changed, do not procede, early return and defer to base game departure logic.
            on first arrival at station, neither readyToDepart nor the settingsHash will be true. This is for quick return after running the station procedure.
         */
        if (state.ReadyToDepart)
        {
            // this is for if the game loads when train is at station, the arm isn't saved, so rearm
            Loader.LogVerbose("ready to depart quick return");
            ArmDepartureCheck(pl);
            pl.StartAE();
            return;
        }

        /*
            7. Build station procedure context and disarm departure
         */
        DisarmDepartureCheck(pl);
        pl.StopAE();

        ctx.StopAtIndex = BuildOrderIndex(orderedStopAtStations);
        ctx.TerminusIndex = BuildOrderIndex(orderedTerminusStations);
        ctx.PickupIndex = BuildOrderIndex(orderedPickupStations);
        ctx.TransferIndex = BuildOrderIndex(orderedTransferStations);
        ctx.Coaches = pl.GetCoaches();

        /*
            8. Check if train is currently stopped, and if so, should it stay stopped.
            if in AE mode, and stopped, will set AE speed to 0
         */
        if (state.CurrentlyStopped)
        {
            bool shouldStayStopped = IsStoppedAndShouldStayStopped(pl, pls, state, ctx);

            if (!shouldStayStopped)
            {
                trainStateManager.SaveState(pl, state);
            }

            if (shouldStayStopped)
            {
                return;
            }
        }


        /*
            9. Determine if at terminus station, midway station or out of bounds station
         */
        bool atTerminus = ctx.TerminusIndex.TryGetValue(state.CurrentStationId, out _);
        bool hasCurrent = ctx.StopAtIndex.TryGetValue(ctx.CurrentStation.identifier, out _);

        /*
            10. run relevant station procedure 
         */
        if (atTerminus && !state.TerminusStationProcedureComplete)
        {
            Say($"PH \"{Hyperlink.To(pl._locomotive)}: running terminus station procedure at {currentStop.DisplayName}\"");
            RunTerminusStationProcedure(pl, pls, state, ctx);
        }
        else if (!atTerminus && !state.NonTerminusStationProcedureComplete)
        {
            Say($"PH \"{Hyperlink.To(pl._locomotive)}: running station procedure at {currentStop.DisplayName}\"");
            RunStationProcedure(pl, pls, state, ctx);
        }
        else
        {
            Loader.Log($"Station Procedure already completed, skipping.");
        }

        /*
            11. Check Pause conditions
            if in AE mode, and pause conditions are met, will set AE speed to 0
         */
        if (!state.StopOverrideActive)
        {
            Loader.Log($"Settings have changed, checking for pause conditions");
            if (PauseAtCurrentStation(pl, pls, state))
            {
                trainStateManager.SaveState(pl, state);
                return;
            }

            if (HaveLowFuel(pl, pls, state))
            {
                trainStateManager.SaveState(pl, state);
                return;
            }
        }
        else
        {
            Loader.Log($"Settings have not changed, skipping check for pause conditions");
        }

        /*
            12. if train is not currently stopped, set ready to depart and arm departure 
         */
        if (!state.CurrentlyStopped)
        {
            Loader.Log($"Train {locomotive.DisplayName} is ready to depart station {currentStop.DisplayName}");
            state.ReadyToDepart = true;
            ArmDepartureCheck(pl);
            pl.StartAE();
        }

        /*
            13. save state and return 
         */
        trainStateManager.SaveState(pl, state);

    }
}