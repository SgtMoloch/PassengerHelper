using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Definition;
using Model.Ops;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;

//Pause
public partial class StationManager
{
    private bool IsStoppedAndShouldStayStopped(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        bool stayStopped = false;

        if (state.CurrentlyStopped)
        {
            Loader.Log($"Train is currently Stopped due to: {state.CurrentReasonForStop}. checking if {pl._locomotive.DisplayName} should stay Stopped at current station");

            StayStoppedIncorrectStationSettings(pl, pls, state);
            StayStoppedStationPause(pl, pls, state, ctx);
            StayStoppedLowFuel(pl, pls, state);

            stayStopped = state.ShouldStayStopped();
        }

        if (!stayStopped)
        {
            state.ResetStoppedFlags();
        }

        return stayStopped;
    }

    private void StayStoppedIncorrectStationSettings(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        if (state.StoppedInsufficientStopAtStations && pls.StationSettings.Values.Where(s => s.StopAtStation).Count() < 2)
        {
            Loader.Log($"Still do not have at least 2 stop at stations selected. {pl._locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            state.StoppedInsufficientStopAtStations = false;
        }

        if (state.StoppedInsufficientTerminusStations && pls.StationSettings.Values.Where(s => s.TerminusStation).Count() != 2)
        {
            Loader.Log($"Still do not have 2 terminus stations selected. {pl._locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            state.StoppedInsufficientTerminusStations = false;
        }

        EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
        if (state.StoppedUnknownDirection && effectiveDOT.Value == DirectionOfTravel.UNKNOWN)
        {
            Loader.Log($"Direction of Travel is still unknown. {pl._locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            state.StoppedUnknownDirection = false;
        }
    }

    private void StayStoppedStationPause(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        if (state.StoppedNextStation && pls.PauseAtNextStation)
        {
            Loader.Log($"StopAtNextStation is selected. {pl._locomotive.DisplayName} is remaining stopped.");
        }
        else
        {
            state.StoppedNextStation = false;
        }

        if (pls.StationSettings.TryGetValue(ctx.CurrentStation.identifier, out StationSetting stationSetting))
        {
            if (state.StoppedTerminusStation && pls.PauseAtTerminusStation && stationSetting.TerminusStation)
            {
                Loader.Log($"StopAtTerminusStation is selected. {pl._locomotive.DisplayName} is remaining stopped.");
            }
            else
            {
                state.StoppedTerminusStation = false;
            }

            if (state.StoppedStationPause && stationSetting.PauseAtStation)
            {
                Loader.Log($"Requested Pause at {ctx.CurrentStation.DisplayName}. {pl._locomotive.DisplayName} is remaining stopped.");
            }
            else
            {
                state.StoppedStationPause = false;
            }

            if (state.StoppedWaitForFullLoad && pls.WaitForFullPassengersTerminusStation && stationSetting.TerminusStation)
            {
                bool notFull = false;
                foreach (Car coach in pl.GetCoaches())
                {
                    PassengerMarker? marker = coach.GetPassengerMarker();
                    if (marker == null)
                    {
                        Loader.Log($"Passenger car not full, remaining stopped");
                        notFull = true;
                        break;
                    }

                    int maxCapacity = PassengerCapacity(coach, ctx.CurrentStation);
                    PassengerMarker actualMarker = marker.Value;
                    bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(ctx.CurrentStation.identifier);
                    bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
                    if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                    {
                        Loader.Log($"Passenger car not full, remaining stopped");
                        notFull = true;
                        break;
                    }
                }

                state.StoppedWaitForFullLoad = notFull;
            }
        }
    }

    private void StayStoppedLowFuel(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        // train is stopped because of low diesel, coal or water
        if (state.StoppedForDiesel || state.StoppedForCoal || state.StoppedForWater)
        {
            Loader.Log($"Locomotive is stopped due to either low diesel, coal or water. Rechecking settings to see if they have changed.");
            // first check if the setting has been set to false
            if (state.StoppedForDiesel)
            {
                if (!pls.PauseForDiesel)
                {
                    Loader.Log($"StopForDiesel no longer selected, resetting flag.");
                    state.StoppedForDiesel = false;
                }
                else
                {
                    CheckDieselLevel(pl, pls, state);
                    Loader.Log($"StoppedForDiesel is now: {state.StoppedForDiesel}");
                }
            }

            if (state.StoppedForCoal)
            {
                if (!pls.PauseForCoal)
                {
                    Loader.Log($"StopForCoal no longer selected, resetting flag.");
                    state.StoppedForCoal = false;
                }
                else
                {
                    CheckCoalLevel(pl, pls, state);
                    Loader.Log($"StoppedForCoal is now: {state.StoppedForCoal}");
                }
            }

            if (state.StoppedForWater)
            {
                if (!pls.PauseForWater)
                {
                    Loader.Log($"StopForWater no longer selected, resetting flag.");
                    state.StoppedForWater = false;
                }
                else
                {
                    CheckWaterLevel(pl, pls, state);
                    Loader.Log($"StoppedForWater is now: {state.StoppedForWater}");
                }
            }
        }
    }

    private bool ValidateStationSettings(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state, StationProcedureContext ctx)
    {
        if (ctx.OrderedStopAtStations.Count < 2)
        {
            string reason = "Stations not selected";
            if (state.CurrentReasonForStop != reason)
            {
                Loader.Log($"there are less than 2 stations to stop at, current selected stations: {Dump(ctx.OrderedStopAtStations)}");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: At least 2 stations must be selected. Check your passenger settings.\"");

                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
                state.StoppedInsufficientStopAtStations = true;
            }

            return false;
        }

        // if we are here, there are two stopat stations

        if (ctx.OrderedTerminusStations.Count != 2)
        {
            string reason = "Terminus stations not selected";
            if (state.CurrentReasonForStop != reason)
            {
                Loader.Log($"there are not exactly 2 terminus stations, current selected terminus stations: {Dump(ctx.OrderedTerminusStations)}");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: 2 Terminus stations must be selected. Check your passenger settings.\"");

                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
                state.StoppedInsufficientTerminusStations = true;
            }

            return false;
        }

        // if we are here, there are 2 terminus stations

        bool hasWest = ctx.OrderedStopAtStations.Contains(ctx.OrderedTerminusStations[1]);
        bool hasEast = ctx.OrderedStopAtStations.Contains(ctx.OrderedTerminusStations[0]);

        if (!hasEast || !hasWest)
        {
            string reason = "Terminus Station stopat ordering mismatch";
            if (state.CurrentReasonForStop != reason)
            {
                Loader.LogError("Terminus station not found in ordered stopat station list; pausing to avoid invalid indexing.");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: Pausing due to terminus station ordering mismatch.\"");

                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
            }

            return false;
        }

        if (ctx.OrderedPickupStations.Count > ctx.OrderedStopAtStations.Count && ctx.OrderedTransferStations.Count == 0)
        {
            Loader.Log($"selected more pickup stations than stopat stations without selecting a transfer station.");
            pl.PostNotice("ph-tf-wrn", $"PKUP > STPAT & NO TRSFR");
        }

        // if we are here, the two terminus stations are stopat stations

        hasWest = ctx.OrderedPickupStations.Contains(ctx.OrderedTerminusStations[1]);
        hasEast = ctx.OrderedPickupStations.Contains(ctx.OrderedTerminusStations[0]);

        if (!hasEast || !hasWest)
        {
            string reason = "Terminus Station pickup ordering mismatch";
            if (state.CurrentReasonForStop != reason)
            {
                Loader.LogError("Terminus station not found in ordered pickup station list; pausing to avoid invalid indexing.");
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: Pausing due to terminus station ordering mismatch.\"");

                state.CurrentlyStopped = true;
                state.CurrentReasonForStop = reason;
            }

            return false;
        }

        // if we are here, the two terminus stations are pickup stations

        if (!TryInferDirection(pl, pls, state, ctx))
        {
            return false;
        }

        // if we are here, the direction is determined

        return true;
    }
    private bool PauseAtCurrentStation(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        if (!pls.StationSettings.TryGetValue(state.CurrentStation.identifier, out var curStationSettings))
        {
            Loader.Log($"No StationSettings entry for {state.CurrentStation.identifier}; skipping pause logic.");
            return false;
        }

        if (pls.PauseAtNextStation)
        {
            Loader.Log($"Pausing at station due to setting");
            pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(state.CurrentStation)}.");
            state.CurrentlyStopped = true;
            state.CurrentReasonForStop = "Requested pause at next station";
            state.StoppedNextStation = true;
            return true;
        }

        if (curStationSettings.PauseAtStation)
        {
            Loader.Log($"Pausing at {state.CurrentStation.DisplayName} due to setting");
            pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(state.CurrentStation)}.");
            state.CurrentlyStopped = true;
            state.CurrentReasonForStop = "Requested pause at " + state.CurrentStation.DisplayName;
            state.StoppedStationPause = true;
            return true;
        }

        if (pls.PauseAtTerminusStation && curStationSettings.TerminusStation == true)
        {
            Loader.Log($"Pausing at {state.CurrentStation.DisplayName} due to setting");
            pl.PostNotice("ai-stop", $"Paused at terminus station {Hyperlink.To(state.CurrentStation)}.");
            state.CurrentlyStopped = true;
            state.CurrentReasonForStop = "Requested pause at terminus station " + state.CurrentStation.DisplayName;
            state.StoppedTerminusStation = true;
            return true;
        }

        if (pls.WaitForFullPassengersTerminusStation && curStationSettings.TerminusStation == true)
        {
            Loader.Log($"Waiting For full Passengers at terminus.");

            if (pls.PauseAtNextStation || pls.PauseAtNextStation || pls.PauseAtTerminusStation)
            {
                Say($"PH \"{Hyperlink.To(pl._locomotive)}: Ambiguous pause settings at {state.CurrentStation.DisplayName}. Check settings. Defaulting to Waiting for full load.\"");
            }

            foreach (Car coach in pl.GetCoaches())
            {
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (!marker.HasValue)
                {
                    Loader.Log($"Passenger car not full, remaining stopped");
                    state.CurrentlyStopped = true;
                    state.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    state.StoppedWaitForFullLoad = true;
                    return true;
                }

                int maxCapacity = PassengerCapacity(coach, state.CurrentStation);
                PassengerMarker actualMarker = marker.Value;

                bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(state.CurrentStation.identifier);
                bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;

                if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                {
                    Loader.Log($"Passenger car not full, remaining stopped");
                    state.CurrentlyStopped = true;
                    state.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    state.StoppedWaitForFullLoad = true;
                    return true;
                }
            }

            Loader.Log($"Passengers are full, continuing.");
        }

        return false;
    }

    private bool HaveLowFuel(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        bool retVal = false;
        if (pls.PauseForDiesel)
        {
            Loader.Log($"Requested stop for low diesel, checking level");
            // check diesel
            bool pause = CheckDieselLevel(pl, pls, state);
            retVal |= pause;
            state.StoppedForDiesel = pause;
        }

        if (pls.PauseForCoal)
        {
            Loader.Log($"Requested stop for low coal, checking level");
            // check coal
            bool pause = CheckCoalLevel(pl, pls, state);
            retVal |= pause;
            state.StoppedForCoal = pause;
        }

        if (pls.PauseForWater)
        {
            Loader.Log($"Requested stop for low water, checking level");
            // check water
            bool pause = CheckWaterLevel(pl, pls, state);
            retVal |= pause;
            state.StoppedForWater = pause;
        }

        if (retVal)
        {
            pl.PostNotice("ai-stop", $"Stopped, low fuel at {Hyperlink.To(state.CurrentStation)}.");
            state.CurrentReasonForStop = "stopped for low fuel";
            state.CurrentlyStopped = true;
        }

        return retVal;
    }

    private bool CheckCoalLevel(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        float actualLevel = pl.GetCoalLevelForLoco();
        float minLevel = pls.CoalLevel;

        Loader.LogVerbose($"coal: min level is: {minLevel}, actual level is: {actualLevel}");

        state.StoppedForCoal = pl.hasTender && actualLevel < minLevel;

        if (state.StoppedForCoal)
        {
            Loader.Log($"{pl._locomotive.DisplayName} is low on coal");

        }

        return state.StoppedForCoal;
    }

    private bool CheckWaterLevel(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        float actualLevel = pl.GetWaterLevelForLoco();

        float minLevel = pls.WaterLevel;

        Loader.LogVerbose($"water: min level is: {minLevel}, actual level is: {actualLevel}");

        state.StoppedForWater = pl.hasTender && actualLevel < minLevel;

        if (state.StoppedForWater)
        {
            Loader.Log($"{pl._locomotive.DisplayName} is low on water");
        }

        return state.StoppedForWater;
    }

    private bool CheckDieselLevel(PassengerLocomotive pl, PassengerLocomotiveSettings pls, TrainState state)
    {
        float actualLevel = pl.GetDieselLevelForLoco();

        float minLevel = pls.DieselLevel;
        Loader.LogVerbose($"diesel: min level is: {minLevel}, actual level is: {actualLevel}");

        state.StoppedForDiesel = pl.isDiesal && actualLevel < minLevel;

        if (state.StoppedForDiesel)
        {
            Loader.Log($"{pl._locomotive.DisplayName} is low on diesel");
        }

        return state.StoppedForDiesel;
    }
}
