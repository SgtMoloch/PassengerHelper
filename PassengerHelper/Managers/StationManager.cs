namespace PassengerHelperPlugin.Managers;

using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Messages;
using Game.State;
using Support;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Network;
using RollingStock;
using Serilog;
using UnityEngine;
using System.Collections;
using Support.GameObjects;
using Model.AI;
using UI.EngineControls;
using System.Reflection;

public class StationManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(StationManager));

    internal readonly List<string> orderedStations;

    private Dictionary<PassengerLocomotive, PassengerStop> passengerTrainToStationMap = new();

    internal TrainManager trainManager;
    internal SettingsManager settingsManager;
    internal UtilManager utilManager;

    private readonly string cochranIdentifier = "cochran";
    private readonly string alarkaIdentifier = "alarka";
    private readonly string almondIdentifier = "almond";
    private readonly string alarkajctIdentifier = "alarkajct";

    public StationManager(SettingsManager settingsManager, TrainManager trainManager, UtilManager utilManager)
    {
        this.orderedStations = utilManager.orderedStations;
        this.trainManager = trainManager;
        this.settingsManager = settingsManager;
        this.utilManager = utilManager;
    }

    public bool ShouldRunStationProcedure(PassengerLocomotive pl, PassengerStop currentStop)
    {
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
        BaseLocomotive locomotive = pl._locomotive;

        if (pls.Disable)
        {
            logger.Information("Passenger Helper is currently disabled for {0} due to disabled setting.", locomotive.DisplayName);
            // return to original game logic
            return false;
        }

        if (pls.StationSettings.Select(s => s.Value.StopAtStation).Count() < 2 && pls.StationSettings.Select(s => s.Value.TerminusStation).Count() != 2)
        {
            logger.Information("Invalid settings detected. Alerting player and stopping.");
            pl.PostNotice("ai-stop", $"Paused, invalid settings.");
            Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Invalid settings. Check your passenger settings.\"");

            return false;
        }

        if (currentStop != pl.CurrentStation)
        {
            logger.Information("Train {0} has arrived at station {1}", locomotive.DisplayName, currentStop.DisplayName);
            pl.CurrentStation = currentStop;
            pl.ResetSettingsHash();
            pl.ResetStatusFlags();
        }

        logger.Information("locomotive cached settings hash: {0}, actual setting hash: {1}", pl.settingsHash, pls.getSettingsHash());
        logger.Information("locomotive cached station settings hash: {0}, actual station settings hash: {1}", pl.stationSettingsHash, pls.getStationSettingsHash());

        if (pl.settingsHash == 0 && pl.stationSettingsHash == 0)
        {
            logger.Information("running due to both station settings hash and settings hash being 0");
            return true;
        }

        if (pl.settingsHash != 0 && pl.settingsHash != pls.getSettingsHash())
        {
            logger.Information("running due to mis match between cached settings hash and actual settings hash");
            return true;
        }

        if (pl.stationSettingsHash != 0 && pl.stationSettingsHash != pls.getStationSettingsHash())
        {
            logger.Information("running due to mis match between cached station settings hash and actual station settings hash");
            return true;
        }

        return false;
    }

    public bool HandleTrainAtStation(PassengerLocomotive pl, PassengerStop currentStop)
    {
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

        if (pls.TrainStatus.Continue)
        {
            pl.ResetStoppedFlags();
        }

        logger.Information("locomotive cached settings hash: {0}, actual setting hash: {1}", pl.settingsHash, pls.getSettingsHash());
        // if we have departed, cease all procedures unless a setting was changed after running the procedure
        if (pls.TrainStatus.ReadyToDepart && pl.settingsHash == pls.getSettingsHash())
        {
            return false;
        }

        if (pl.CurrentStation == null)
        {
            return false;
        }

        // v2
        // is next station occupied?
        // is train en-route to current station?
        // how many tracks does current station have?
        // route appropriately

        // if train is currently Stopped
        if (IsStoppedAndShouldStayStopped(pl))
        {
            return true;
        }

        if (pl.settingsHash != pls.getSettingsHash())
        {
            if (!pls.TrainStatus.Continue)
            {
                logger.Information("Passenger settings have changed, checking for pause conditions");
                pl.ResetStoppedFlags();

                if (PauseAtCurrentStation(pl, pls))
                {
                    settingsManager.SaveSettings(pl, pls);
                    return true;
                }

                if (HaveLowFuel(pl, pls))
                {
                    settingsManager.SaveSettings(pl, pls);
                    return true;
                }
            }

            pl.settingsHash = pls.getSettingsHash();
        }
        else
        {
            logger.Information("Passenger settings have not changed, skipping check for pause conditions");
        }

        if (!pls.TrainStatus.CurrentlyStopped)
        {
            logger.Information("Train {0} is ready to depart station {1}", pl._locomotive.DisplayName, currentStop.DisplayName);
            pls.TrainStatus.ReadyToDepart = true;
        }

        settingsManager.SaveSettings(pl, pls);

        return pls.TrainStatus.CurrentlyStopped;
    }

    private bool IsStoppedAndShouldStayStopped(PassengerLocomotive pl)
    {
        PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

        if (pls.TrainStatus.CurrentlyStopped)
        {
            logger.Information("Train is currently Stopped due to: {0}", pls.TrainStatus.CurrentReasonForStop);
            if (pl.ShouldStayStopped())
            {
                return true;
            }
        }

        pls.TrainStatus.CurrentlyStopped = false;
        pls.TrainStatus.CurrentReasonForStop = "";
        return false;
    }

    private bool PauseAtCurrentStation(PassengerLocomotive pl, PassengerLocomotiveSettings pls)
    {
        if (pls.PauseAtNextStation)
        {
            logger.Information("Pausing at station due to setting");
            pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(pl.CurrentStation)}.");
            pls.TrainStatus.CurrentlyStopped = true;
            pls.TrainStatus.CurrentReasonForStop = "Requested pause at next station";
            pls.TrainStatus.StoppedNextStation = true;
            return true;
        }

        if (pls.StationSettings[pl.CurrentStation.identifier].PauseAtStation)
        {
            logger.Information("Pausing at {0} due to setting", pl.CurrentStation.DisplayName);
            pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(pl.CurrentStation)}.");
            pls.TrainStatus.CurrentlyStopped = true;
            pls.TrainStatus.CurrentReasonForStop = "Requested pause at " + pl.CurrentStation.DisplayName;
            pls.TrainStatus.StoppedStationPause = true;
            return true;
        }

        if (pls.PauseAtTerminusStation && pls.StationSettings[pl.CurrentStation.identifier].TerminusStation == true)
        {
            logger.Information("Pausing at {0} due to setting", pl.CurrentStation.DisplayName);
            pl.PostNotice("ai-stop", $"Paused at terminus station {Hyperlink.To(pl.CurrentStation)}.");
            pls.TrainStatus.CurrentlyStopped = true;
            pls.TrainStatus.CurrentReasonForStop = "Requested pause at terminus station " + pl.CurrentStation.DisplayName;
            pls.TrainStatus.StoppedTerminusStation = true;
            return true;
        }

        if (pls.WaitForFullPassengersTerminusStation && pls.StationSettings[pl.CurrentStation.identifier].TerminusStation == true)
        {
            logger.Information("Waiting For full Passengers at terminus.");

            foreach (Car coach in pl.GetCoaches())
            {
                PassengerMarker? marker = coach.GetPassengerMarker();
                if (marker == null)
                {
                    logger.Information("Passenger car not full, remaining stopped");
                    pls.TrainStatus.CurrentlyStopped = true;
                    pls.TrainStatus.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    pls.TrainStatus.StoppedWaitForFullLoad = true;
                    return true;
                }

                int maxCapacity = pl.PassengerCapacity(coach, pl.CurrentStation);
                PassengerMarker actualMarker = marker.Value;
                bool containsPassengersForCurrentStation = actualMarker.Destinations.Contains(pl.CurrentStation.identifier);
                bool isNotAtMaxCapacity = actualMarker.TotalPassengers < maxCapacity;
                if (containsPassengersForCurrentStation || isNotAtMaxCapacity)
                {
                    logger.Information("Passenger car not full, remaining stopped");
                    pls.TrainStatus.CurrentlyStopped = true;
                    pls.TrainStatus.CurrentReasonForStop = "Waiting for full passengers at terminus station";
                    pls.TrainStatus.StoppedWaitForFullLoad = true;
                    return true;
                }
            }

            logger.Information("Passengers are full, continuing.");
        }

        return false;
    }

    private bool HaveLowFuel(PassengerLocomotive pl, PassengerLocomotiveSettings pls)
    {
        bool retVal = false;
        if (pls.PauseForDiesel)
        {
            logger.Information("Requested stop for low diesel, checking level");
            // check diesel
            if (pl.CheckDieselFuelLevel(out float diesel))
            {
                pl.PostNotice("ai-stop", $"Stopped, low diesel at {Hyperlink.To(pl.CurrentStation)}.");
                retVal = true;
            }
        }

        if (pls.PauseForCoal)
        {
            logger.Information("Requested stop for low coal, checking level");
            // check coal
            if (pl.CheckCoalLevel(out float coal))
            {
                pl.PostNotice("ai-stop", $"Stopped, low coal at {Hyperlink.To(pl.CurrentStation)}.");
                retVal = true;
            }
        }

        if (pls.PauseForWater)
        {
            logger.Information("Requested stop for low water, checking level");
            // check water
            if (pl.CheckWaterLevel(out float water))
            {
                pl.PostNotice("ai-stop", $"Stopped, low water at {Hyperlink.To(pl.CurrentStation)}.");
                retVal = true;
            }
        }

        return retVal;
    }

    public void RunStationProcedure(PassengerLocomotive pl, PassengerStop ps)
    {
        logger.Information("StationManager::RunStationProcedure::running Station Procedure for {0} at {1}", pl._locomotive.DisplayName, ps.DisplayName);

        PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
        string currentStopName = ps.DisplayName;
        string currentStopId = ps.identifier;
        string prevStopId = pl.PreviousStation != null ? pl.PreviousStation.identifier : "";

        List<string> orderedStopAtStations = pls.StationSettings.Where(station => station.Value.StopAtStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedTerminusStations = pls.StationSettings.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        logger.Information("StationManager::RunStationProcedure::ordered stop at stations are: {0}", orderedStopAtStations);
        logger.Information("StationManager::RunStationProcedure::ordered terminus stations are: {0}", orderedTerminusStations);

        if (!BaseStationProcedure(pl, pls, ps, orderedStopAtStations, orderedTerminusStations))
        {
            pl.stationSettingsHash = pls.getStationSettingsHash();
            settingsManager.SaveSettings(pl, pls);
            return;
        }

        pl.CurrentStation = ps;
        List<Car> coaches = pl.GetCoaches();

        int westTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[1]);
        int eastTerminusIndex = orderedStopAtStations.IndexOf(orderedTerminusStations[0]);

        bool atTerminusStationWest = orderedTerminusStations[1] == currentStopId;
        bool atTerminusStationEast = orderedTerminusStations[0] == currentStopId;
        bool atTerminus = atTerminusStationEast || atTerminusStationWest;

        int currentIndex = orderedStopAtStations.IndexOf(currentStopId);
        logger.Information("StationManager::RunStationProcedure::current index is: {0}, current stop id is: {1}", currentIndex, currentStopId);

        HashSet<string> expectedSelectedDestinations = new();

        pls.TrainStatus.AtTerminusStationWest = false;
        pls.TrainStatus.AtTerminusStationEast = false;

        if (!DetermineDOT(pl, pls, ps, orderedStopAtStations, orderedTerminusStations))
        {
            pl.settingsHash = pls.getSettingsHash();
            settingsManager.SaveSettings(pl, pls);
            return;
        }

        SelectExpectedStations(pls, orderedStopAtStations, currentIndex, westTerminusIndex, eastTerminusIndex, currentStopId, prevStopId, expectedSelectedDestinations);

        logger.Information("StationManager::RunStationProcedure::Expected selected stations are: {0}", expectedSelectedDestinations);

        // transfer station check
        CheckTransferStation(pls, orderedStopAtStations, orderedTerminusStations, currentIndex, currentStopId, expectedSelectedDestinations);

        SelectStationsOnCoaches(coaches, expectedSelectedDestinations);

        logger.Information("StationManager::RunStationProcedure::Setting Previous stop to the current stop");
        pl.PreviousStation = ps;
        pl.StationProcedureRan = true;

        pls.TrainStatus.NonTerminusStationProcedureComplete = !atTerminus;
        pls.TrainStatus.TerminusStationProcedureComplete = atTerminus;

        pls.TrainStatus.AtTerminusStationWest = atTerminusStationWest;
        pls.TrainStatus.AtTerminusStationEast = atTerminusStationEast;

        pls.DoTLocked = true;


        // setting the previous stop on the settings changes the hash, so re-cache the settings
        pl.stationSettingsHash = pls.getStationSettingsHash();
        pl.settingsHash = pls.getSettingsHash();

        settingsManager.SaveSettings(pl, pls);
    }

    public bool BaseStationProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings pls, PassengerStop ps, List<string> orderedStopAtStations, List<string> orderedTerminusStations)
    {
        logger.Information("StationManager::BaseStationProcedure::running Base Station Procedure");

        if (!CheckStopAtStationCount(orderedStopAtStations, pl, pls, ps))
        {
            return false;
        }

        if (!CheckTerminusStationCount(orderedTerminusStations, pl, pls, ps))
        {
            return false;
        }

        if (!CheckTrainAtStopAtStation(orderedStopAtStations, pl, ps.identifier))
        {
            return false;
        }

        return true;
    }

    private bool CheckStopAtStationCount(List<string> orderedStopAtStations, PassengerLocomotive pl, PassengerLocomotiveSettings pls, PassengerStop CurrentStop)
    {
        AutoEngineerMode mode = pl.GetMode();

        if (orderedStopAtStations.Count < 2)
        {
            logger.Information("StationManager::CheckStopAtStationCount::there are at least 2 stations to stop at, current selected stations: {0}", orderedStopAtStations);
            Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"At least 2 stop at stations must be selected. Check your passenger settings.\"");
            if (mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint)
            {
                pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(CurrentStop)}: Insufficient StopAt Stations.");

                pls.TrainStatus.CurrentlyStopped = true;
                pls.TrainStatus.CurrentReasonForStop = "Insufficient StopAt Stations";
                pls.TrainStatus.StoppedInsufficientStopAtStations = true;
            }

            return false;
        }

        return true;
    }

    private bool CheckTerminusStationCount(List<string> orderedTerminusStations, PassengerLocomotive pl, PassengerLocomotiveSettings pls, PassengerStop CurrentStop)
    {
        AutoEngineerMode mode = pl.GetMode();

        if (orderedTerminusStations.Count != 2)
        {
            logger.Information("StationManager::CheckTerminusStationCount::there are not exactly 2 terminus stations, current selected terminus stations: {0}", orderedTerminusStations);
            Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"2 Terminus stations must be selected. Check your passenger settings.\"");

            if (mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint)
            {
                pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(CurrentStop)}: Insufficient Terminus Stations.");

                pls.TrainStatus.CurrentlyStopped = true;
                pls.TrainStatus.CurrentReasonForStop = "Insufficient Terminus Stations";
                pls.TrainStatus.StoppedInsufficientTerminusStations = true;
            }

            return false;
        }

        return true;
    }

    private bool CheckTrainAtStopAtStation(List<string> orderedStopAtStations, PassengerLocomotive pl, string currentStop)
    {
        if (!orderedStopAtStations.Contains(currentStop))
        {
            logger.Information("StationManager::CheckTrainAtStopAtStation::Train is at {0} which is not a stop at station.", currentStop);
            Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Not at a stop at station.\"");

            return false;
        }

        return true;
    }

    private bool DetermineDOT(PassengerLocomotive pl, PassengerLocomotiveSettings pls, PassengerStop CurrentStop, List<string> orderedStopAtStations, List<string> orderedTerminusStations)
    {
        string currentStopId = CurrentStop.identifier;
        string prevStopId = pl.PreviousStation != null ? pl.PreviousStation.identifier : "";

        bool atTerminusStationWest = orderedTerminusStations[1] == currentStopId;
        bool atTerminusStationEast = orderedTerminusStations[0] == currentStopId;

        bool atTerminus = atTerminusStationEast || atTerminusStationWest;
        bool atAlarka = currentStopId == alarkaIdentifier && !pls.TrainStatus.AtAlarka;
        bool atCochran = currentStopId == cochranIdentifier && !pls.TrainStatus.AtCochran && !orderedStopAtStations.Contains(alarkaIdentifier);

        AutoEngineerMode mode = pl.GetMode();

        if (pls.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            // if the previous stop is unknown, cannot determine direction of travel
            if (pl.PreviousStation == null)
            {
                logger.Information("StationManager::DetermineDOT::train was not previously at a station.");
                logger.Information("StationManager::DetermineDOT::Waiting for input from engineer about which direction to travel in");

                Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Set Direction of Travel in Settings.\"");

                if (mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint)
                {
                    pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(CurrentStop)}: Unknown Direction.");

                    pls.TrainStatus.CurrentlyStopped = true;
                    pls.TrainStatus.CurrentReasonForStop = "Unknown Direction";
                    pls.TrainStatus.StoppedUnknownDirection = true;
                }

                return false;
            }

            if (pl.PreviousStation != CurrentStop)
            {
                logger.Information("StationManager::DetermineDOT::Determining direction of travel based on previous stations");

                string reason = "Unknown Direction";
                // if at cochran and prev stop was alarka, and alarka is not a terminus station, cannot determine direction of travel
                if (currentStopId == cochranIdentifier && prevStopId == alarkaIdentifier && !orderedTerminusStations.Contains(alarkaIdentifier))
                {
                    logger.Information("StationManager::DetermineDOT::Direction of travel is unknown, Train is in Cochran, previous stop was alarka, and alarka is not a terminus station; Cannot accurately determine direction.");
                    Say($"PassengerHelper {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Set Direction of Travel in Settings.\"");

                    if (mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint)
                    {
                        pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(CurrentStop)}: Unknown Direction.");

                        pls.TrainStatus.CurrentlyStopped = true;
                        pls.TrainStatus.CurrentReasonForStop = reason;
                        pls.TrainStatus.AtCochran = true;
                        pls.TrainStatus.StoppedUnknownDirection = true;
                    }

                    return false;
                }

                // if current stop is alarka and previous stop was cochran, cannot determine direction of travel
                if (currentStopId == alarkaIdentifier && prevStopId == cochranIdentifier)
                {
                    logger.Information("StationManager::DetermineDOT::Direction of travel is unknown, train is in Alarka, previous stop was cochran; Cannot accurately determine direction.");
                    Say($"PassengerHelper {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Set Direction of Travel in Settings.\"");

                    if (mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint)
                    {
                        pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(CurrentStop)}: Unknown Direction.");

                        pls.TrainStatus.CurrentlyStopped = true;
                        pls.TrainStatus.CurrentReasonForStop = reason;
                        pls.TrainStatus.AtAlarka = true;
                        pls.TrainStatus.StoppedUnknownDirection = true;
                    }

                    return false;
                }

                if (orderedStopAtStations.Contains(prevStopId))
                {
                    logger.Information("StationManager::DetermineDOT::Previous stop was inside terminus bounds.");
                    int currentIndex = orderedStopAtStations.IndexOf(currentStopId);
                    int indexPrev = orderedStopAtStations.IndexOf(prevStopId);

                    if (indexPrev < currentIndex)
                    {
                        logger.Information("StationManager::DetermineDOT::Direction of Travel: WEST");
                        pls.DirectionOfTravel = DirectionOfTravel.WEST;
                    }
                    else
                    {
                        logger.Information("StationManager::DetermineDOT::Direction of Travel: EAST");
                        pls.DirectionOfTravel = DirectionOfTravel.EAST;
                    }
                }
                else
                {
                    logger.Information("StationManager::DetermineDOT::Previous stop was outside terminus bounds.");

                    if (orderedTerminusStations[0] == currentStopId)
                    {
                        logger.Information("StationManager::DetermineDOT::Direction of Travel: WEST");
                        pls.DirectionOfTravel = DirectionOfTravel.WEST;

                        return true;
                    }
                    else if (orderedTerminusStations[1] == currentStopId)
                    {
                        logger.Information("StationManager::DetermineDOT::Direction of Travel: EAST");
                        pls.DirectionOfTravel = DirectionOfTravel.EAST;

                        return true;
                    }
                    else
                    {
                        logger.Information("StationManager::DetermineDOT::Cannot determine direction of travel. Waiting for input from engineer about which direction to travel in");

                        Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Unknown Direction. Set Direction of Travel in Settings.\"");

                        if (mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint)
                        {
                            pl.PostNotice("ai-stop", $"Paused at {Hyperlink.To(CurrentStop)}: Unknown Direction.");

                            pls.TrainStatus.CurrentlyStopped = true;
                            pls.TrainStatus.CurrentReasonForStop = "Unknown Direction";
                            pls.TrainStatus.StoppedUnknownDirection = true;
                        }

                        return false;
                    }
                }
            }
        }

        // if we get here, then DOT is known
        pls.TrainStatus.AtAlarka = atAlarka;
        pls.TrainStatus.AtCochran = atCochran;
        pls.TrainStatus.AtTerminusStationEast = atTerminusStationEast;
        pls.TrainStatus.AtTerminusStationWest = atTerminusStationWest;

        // if prev and curr are the same, we are rerunning procedure, do nothing
        if (prevStopId == currentStopId)
        {
            return true;
        }

        logger.Information("StationManager::DetermineDOT::Direction is known, correcting if needed.");
        if (orderedStopAtStations.Contains(prevStopId) && !atTerminus)
        {
            logger.Information("StationManager::DetermineDOT::At nonterminus station.");

            int currentIndex = orderedStopAtStations.IndexOf(currentStopId);
            int indexPrev = orderedStopAtStations.IndexOf(prevStopId);

            if (indexPrev < currentIndex && pls.DirectionOfTravel != DirectionOfTravel.WEST)
            {
                logger.Information("StationManager::DetermineDOT::New Direction of Travel: WEST");
                pls.DirectionOfTravel = DirectionOfTravel.WEST;
            }
            else if (indexPrev > currentIndex && pls.DirectionOfTravel != DirectionOfTravel.EAST)
            {
                logger.Information("StationManager::DetermineDOT::New Direction of Travel: EAST");
                pls.DirectionOfTravel = DirectionOfTravel.EAST;
            }

        }
        else if (atTerminus)
        {
            logger.Information("StationManager::DetermineDOT::At terminus station.");
            Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Reached terminus station at {Hyperlink.To(CurrentStop)}.\"");
            if (atTerminusStationEast && pls.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("StationManager::DetermineDOT::New Direction of Travel: WEST");
                pls.DirectionOfTravel = DirectionOfTravel.WEST;

                ReverseDirectionProcedure(pl, pls, currentStopId);
            }
            else if (atTerminusStationWest && pls.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                logger.Information("StationManager::DetermineDOT::Direction of Travel: EAST");
                pls.DirectionOfTravel = DirectionOfTravel.EAST;

                ReverseDirectionProcedure(pl, pls, currentStopId);
            }
        }
        else if (atAlarka)
        {
            logger.Information("StationManager::DetermineDOT::Train is in Alarka, there are more stops. Reversing train if in Point to Point mode.");
            ReverseDirectionProcedure(pl, pls, currentStopId);
        }
        else if (atCochran)
        {
            logger.Information("StationManager::DetermineDOT::Train is in Cochran, there are more stops. Reversing train if in Point to Point mode.");
            ReverseDirectionProcedure(pl, pls, currentStopId);
        }

        return true;
    }

    private void SelectExpectedStations(PassengerLocomotiveSettings pls, List<string> orderedStopAtStations, int currentIndex, int westTerminusIndex, int eastTerminusIndex, string currentStopIdentifier, string prevStopIdentifier, HashSet<string> expectedSelectedDestinations)
    {
        if (pls.DirectionOfTravel == DirectionOfTravel.WEST)
        {
            logger.Information("StationManager::SelectExpectedStations::selecting stations from {0} index {1} to {2} index {3}", currentStopIdentifier, currentIndex, orderedStopAtStations[westTerminusIndex], westTerminusIndex);
            // add one to range to include terminus station
            expectedSelectedDestinations.UnionWith(orderedStopAtStations.GetRange(currentIndex, westTerminusIndex - currentIndex + 1));
            logger.Information("StationManager::SelectExpectedStations::Expected selected stations are: {0}", expectedSelectedDestinations);

            if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == alarkaIdentifier)
            {
                logger.Information("StationManager::SelectExpectedStations::Train is at cochran, heading west and alarka was previous station. Remove Alarka from expected stations");
                expectedSelectedDestinations.Remove(alarkaIdentifier);
            }
        }
        else if (pls.DirectionOfTravel == DirectionOfTravel.EAST)
        {
            logger.Information("StationManager::SelectExpectedStations::selecting stations from {0} index {1} to {2} index {3}", currentStopIdentifier, currentIndex, orderedStopAtStations[eastTerminusIndex], eastTerminusIndex);
            // add one to range to include current station
            expectedSelectedDestinations.UnionWith(orderedStopAtStations.GetRange(eastTerminusIndex, currentIndex + 1));
            logger.Information("StationManager::SelectExpectedStations::Expected selected stations are: {0}", expectedSelectedDestinations);

            if (currentStopIdentifier == cochranIdentifier && prevStopIdentifier == almondIdentifier)
            {
                logger.Information("StationManager::SelectExpectedStations::Train is at cochran, heading east and almond was previous station. Add Alarka to expected stations");
                expectedSelectedDestinations.Add(alarkaIdentifier);
            }
        }

        if (currentStopIdentifier == alarkaIdentifier && orderedStopAtStations.Contains(cochranIdentifier))
        {
            logger.Information("StationManager::SelectExpectedStations::Train is at alarka, heading {0} and alarka is not a terminus. Add cochran to expected stations", pls.DirectionOfTravel.ToString());
            expectedSelectedDestinations.Add(cochranIdentifier);
        }
    }

    private void CheckTransferStation(PassengerLocomotiveSettings pls, List<string> orderedStopAtStations, List<string> orderedTerminusStations, int currentIndex, string currentStopIdentifier, HashSet<string> expectedSelectedDestinations)
    {
        List<string> orderedTransferStations = pls.StationSettings.Where(s => s.Value.TransferStation && s.Value.StopAtStation).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        int numTransferStations = orderedTransferStations.Count();
        bool transferStationSelected = numTransferStations > 0;

        if (transferStationSelected)
        {
            logger.Information("StationManager::CheckTransferStation::Transfer station selected, checking direction and modifying expected selected stations");
            List<string> pickUpPassengerStations = pls.StationSettings.Where(s => s.Value.PickupPassengersForStation).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
            int westTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[1]);
            int eastTerminusIndex_Pickup = pickUpPassengerStations.IndexOf(orderedTerminusStations[0]);

            bool atEastTerminus = orderedTerminusStations[0] == currentStopIdentifier;
            bool atWestTerminus = orderedTerminusStations[1] == currentStopIdentifier;

            logger.Information("StationManager::CheckTransferStation::The following stations are pickup stations: {0}", pickUpPassengerStations);
            bool useNormalLogic = true;

            if (orderedTransferStations.Contains(alarkajctIdentifier))
            {
                logger.Information("StationManager::CheckTransferStation::Train has alarkajct as a transfer station");
                useNormalLogic = RunAlarkaJctTransferStationProcedure(pls, currentStopIdentifier, expectedSelectedDestinations, orderedStopAtStations, orderedTerminusStations, orderedTransferStations, pickUpPassengerStations);
            }

            if (useNormalLogic)
            {
                if (pls.DirectionOfTravel == DirectionOfTravel.WEST)
                {
                    if (orderedTransferStations.Contains(orderedTerminusStations[1]))
                    {
                        logger.Information("StationManager::CheckTransferStation::Selecting pickup stations {0} that are further west of the west terminus station: {1}", pickUpPassengerStations.GetRange(westTerminusIndex_Pickup, pickUpPassengerStations.Count - westTerminusIndex_Pickup), orderedTerminusStations[1]);
                        // select all to the west of the west terminus station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(westTerminusIndex_Pickup, pickUpPassengerStations.Count - westTerminusIndex_Pickup));
                    }
                    else
                    {
                        int nextStopAtStationPickupIndex = atEastTerminus ? westTerminusIndex_Pickup : pickUpPassengerStations.IndexOf(orderedStopAtStations[currentIndex + 1]);
                        logger.Information("StationManager::CheckTransferStation::Selecting pickup stations {0} that are further west of the next StopAt station: {1}", pickUpPassengerStations.GetRange(nextStopAtStationPickupIndex, pickUpPassengerStations.Count - nextStopAtStationPickupIndex), orderedStopAtStations[currentIndex + 1]);
                        // select all to the west of the current station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(nextStopAtStationPickupIndex, pickUpPassengerStations.Count - nextStopAtStationPickupIndex));
                    }
                }

                if (pls.DirectionOfTravel == DirectionOfTravel.EAST)
                {
                    if (orderedTransferStations.Contains(orderedTerminusStations[0]))
                    {
                        logger.Information("StationManager::CheckTransferStation::Selecting pickup stations {0} that are further east of the east terminus station: {1}", pickUpPassengerStations.GetRange(0, eastTerminusIndex_Pickup + 1), orderedTerminusStations[0]);
                        // select all to the east of the east terminus station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(0, eastTerminusIndex_Pickup + 1));
                    }
                    else
                    {
                        int nextStopAtStationPickupIndex = atWestTerminus ? eastTerminusIndex_Pickup : pickUpPassengerStations.IndexOf(orderedStopAtStations[currentIndex - 1]);
                        logger.Information("StationManager::CheckTransferStation::Selecting pickup stations {0} that are further east of the next StopAt station: {1}", pickUpPassengerStations.GetRange(0, nextStopAtStationPickupIndex + 1), orderedStopAtStations[currentIndex - 1]);
                        // select all to the east of the current station
                        expectedSelectedDestinations.UnionWith(pickUpPassengerStations.GetRange(0, nextStopAtStationPickupIndex + 1));
                    }

                }
            }
        }
    }

    private void SelectStationsOnCoaches(List<Car> coaches, HashSet<string> expectedSelectedDestinations)
    {
        logger.Information("StationManager::SelectStationsOnCoaches::Checking passenger cars to make sure they have the proper selected stations");
        logger.Information("StationManager::SelectStationsOnCoaches::Setting the following stations: {0}", expectedSelectedDestinations);

        foreach (Car coach in coaches)
        {
            logger.Information("StationManager::SelectStationsOnCoaches::Checking Car {0}", coach.DisplayName);
            PassengerMarker? marker = coach.GetPassengerMarker();
            if (marker != null && marker.HasValue)
            {
                PassengerMarker actualMarker = marker.Value;

                logger.Information("StationManager::SelectStationsOnCoaches::Current selected stations are: {0}", actualMarker.Destinations);
                if (!actualMarker.Destinations.SetEquals(expectedSelectedDestinations))
                {
                    StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, expectedSelectedDestinations.ToList()));
                }
            }
        }
    }

    private void ReverseDirectionProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings settings, string currentStopId)
    {
        logger.Information("StationManager::ReverseDirectionProcedure::Checking if in loop mode");
        // if we don't want to reverse, return to original logic
        if (settings.StationSettings[currentStopId].PassengerMode == PassengerMode.Loop)
        {
            logger.Information("StationManager::ReverseDirectionProcedure::Loop Mode is set to true. Continuing in current direction.");
            return;
        }

        logger.Information("StationManager::ReverseDirectionProcedure::Reversing direction");
        Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Reversing direction.\"");

        // reverse the direction of the loco
        pl.ReverseLocoDirection();
    }

    private void NotAtASelectedStationProcedure(PassengerLocomotive pl, PassengerLocomotiveSettings settings, PassengerStop CurrentStop, List<string> orderedSelectedStations, List<string> orderedTerminusStations)
    {
        logger.Information("Train is at a station: {0} that is not within the terminus bounds: {1}", CurrentStop.identifier, orderedTerminusStations);

        if (settings.DirectionOfTravel == DirectionOfTravel.UNKNOWN)
        {
            logger.Information("Travel direction is unknown, so unable to correct. Continuing in the current direction and will check again at the next station");
            return;
        }

        logger.Information("Travel direction is known.");
        logger.Information("Getting direction train should continue in");

        int currentStationIndex = orderedStations.IndexOf(CurrentStop.identifier);
        int indexWestTerminus_All = orderedStations.IndexOf(orderedTerminusStations[1]);
        int indexEastTerminus_All = orderedStations.IndexOf(orderedTerminusStations[0]);

        if (currentStationIndex > indexEastTerminus_All && currentStationIndex < indexWestTerminus_All)
        {
            logger.Information("station is inbounds of the terminus stations");
            return;
        }

        // if not in bounds of terminus
        if (currentStationIndex < indexEastTerminus_All)
        {
            if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                logger.Information("train is already going in right direction to east terminus station {0} -> {1}", CurrentStop.identifier, orderedTerminusStations[0]);
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("train is going wrong way from east terminus, revering direction based on loop/point to point setting");
                logger.Information("Checking if in loop mode");

                if (settings.StationSettings[CurrentStop.identifier].PassengerMode == PassengerMode.Loop)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Continuing direction to loop back to East Terminus.\"");
                }
                else
                {
                    logger.Information("Reversing direction");
                    Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Reversing direction to return back to East Terminus.\"");

                    // reverse the direction of the loco
                    pl.ReverseLocoDirection();
                }
            }
        }
        else if (currentStationIndex > indexWestTerminus_All)
        {
            if (settings.DirectionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("train is already going in right direction to west terminus station {0} -> {1}", CurrentStop.identifier, orderedTerminusStations[1]);
            }
            else if (settings.DirectionOfTravel == DirectionOfTravel.WEST)
            {
                logger.Information("train is going wrong way from west terminus, revering direction based on loop/point to point setting");
                logger.Information("Checking if in loop mode");

                if (settings.StationSettings[CurrentStop.identifier].PassengerMode == PassengerMode.Loop)
                {
                    logger.Information("Loop Mode is set to true. Continuing in current direction.");
                    Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Continuing direction to loop back to West Terminus.\"");
                }
                else
                {
                    logger.Information("Reversing direction");
                    Say($"Passenger Helper Engineer {Hyperlink.To(pl._locomotive)}: \"Reversing direction to return back to West Terminus.\"");

                    // reverse the direction of the loco
                    pl.ReverseLocoDirection();
                }
            }
        }

    }

    private bool RunAlarkaJctTransferStationProcedure(PassengerLocomotiveSettings pls, string currentStopIdentifier, HashSet<string> expectedSelectedDestinations, List<string> orderedStopAtStations, List<string> orderedTerminusStations, List<string> orderedTransferStations, List<string> pickUpPassengerStations, DirectionOfTravel? directionOfTravel = null)
    {
        int currentIndex_Pickup = pickUpPassengerStations.IndexOf(currentStopIdentifier);

        bool jctIsWestTerminus = orderedTerminusStations[1] == alarkajctIdentifier;
        bool jctIsEastTerminus = orderedTerminusStations[0] == alarkajctIdentifier;

        bool alarkaIsWestTerminus = orderedTerminusStations[1] == alarkaIdentifier;
        bool alarkaIsEastTerminus = orderedTerminusStations[0] == alarkaIdentifier;

        if (directionOfTravel == null)
        {
            directionOfTravel = pls.DirectionOfTravel;
        }

        if (jctIsWestTerminus)
        {
            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train has alarkajct as the west terminus station");
            return true; // select all pickup stations west of west terminus
        }

        if (jctIsEastTerminus)
        {
            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train has alarkajct as the east terminus station");
            if (alarkaIsWestTerminus)
            {
                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train has alarka as the west terminus station");
                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is doing the alarka branch only");

                if (directionOfTravel == DirectionOfTravel.EAST)
                {
                    logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is heading East so selecting all pickup stations");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);
                    expectedSelectedDestinations.Remove(alarkaIdentifier);
                }

                if (directionOfTravel == DirectionOfTravel.WEST)
                {
                    logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is heading West, so selecting only cochran and alarka as normal");
                }

                return false;
            }

            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is now heading East, so selecting alarka and cochran as pickup stations if needed");
                if (pickUpPassengerStations.Contains(alarkaIdentifier))
                {
                    logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::adding alarka");
                    expectedSelectedDestinations.Add(alarkaIdentifier);
                }

                if (pickUpPassengerStations.Contains(cochranIdentifier))
                {
                    logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::adding cochran");
                    expectedSelectedDestinations.Add(cochranIdentifier);
                }
            }

            return true;
        }

        logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train has a station other than alarkajct for the east terminus");

        if (alarkaIsWestTerminus)
        {
            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train has alarka as the west terminus station");

            if (directionOfTravel == DirectionOfTravel.EAST)
            {
                if (currentIndex_Pickup > pickUpPassengerStations.IndexOf(alarkajctIdentifier))
                {
                    logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is heading East, selecting all pickup stations except alarka");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);
                    expectedSelectedDestinations.Remove(alarkaIdentifier);

                    return false;
                }

                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is going east and is at or past alarkajct, using normal logic");
                return true;
            }

            if (directionOfTravel == DirectionOfTravel.WEST)
            {
                if (currentIndex_Pickup < pickUpPassengerStations.IndexOf(alarkajctIdentifier))
                {
                    logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is heading West, selecting all pickup stations");
                    expectedSelectedDestinations.UnionWith(pickUpPassengerStations);

                    return false;
                }

                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is going west, using normal logic if after alarka jct");
                return true;
            }
        }

        logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Train is long distance train with no alarka branch.");

        bool addAlarkaAndCochran = true;

        logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Checking if alarka and cochran are pickup stations");
        bool pickUpContainsAlarkaAndCochran = pickUpPassengerStations.Contains(alarkaIdentifier) && pickUpPassengerStations.Contains(cochranIdentifier);
        logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Alarka and cochran are pickup stations: {0}", pickUpContainsAlarkaAndCochran);

        bool dotEastAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.EAST;
        bool dotWestAndBeforeAlarkaJct = directionOfTravel == DirectionOfTravel.WEST;

        if (pickUpContainsAlarkaAndCochran)
        {
            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Checking if train is before Alarka jct, based on current direction of travel");
            int alarkaJctIndex_Pickup = pickUpPassengerStations.IndexOf(alarkajctIdentifier);
            dotEastAndBeforeAlarkaJct &= currentIndex_Pickup > alarkaJctIndex_Pickup;
            dotWestAndBeforeAlarkaJct &= currentIndex_Pickup < alarkaJctIndex_Pickup;
            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::train is before Alarka jct going west: {0}, train is before Alarka jct going east: {1}", dotWestAndBeforeAlarkaJct, dotEastAndBeforeAlarkaJct);

            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::Ensuring that cochran and alarka are not stop at stations");
            bool stopDoesNotIncludeAlarkaAndCochran = !orderedStopAtStations.Contains(alarkaIdentifier) && !orderedStopAtStations.Contains(cochranIdentifier);
            logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::cochran and alarka are not stop at stations: {0}", stopDoesNotIncludeAlarkaAndCochran);

            addAlarkaAndCochran &= stopDoesNotIncludeAlarkaAndCochran && (dotEastAndBeforeAlarkaJct || dotWestAndBeforeAlarkaJct);

            if (addAlarkaAndCochran)
            {
                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::adding alarka");
                expectedSelectedDestinations.Add(alarkaIdentifier);
                logger.Information("StationManager::RunAlarkaJctTransferStationProcedure::adding cochran");
                expectedSelectedDestinations.Add(cochranIdentifier);
            }
        }

        int transferStationCount = orderedTransferStations.Count;

        if (transferStationCount == 1)
        {
            return false;
        }

        return true;
    }

    private void Say(string message)
    {
        Multiplayer.Broadcast(message);
    }
}