namespace PassengerHelper.Support;


public static class SettingKey
{
    public static string PauseForDiesel = "pause_for_diesel";
    public static string DieselLevel = "diesel_level";
    public static string PauseForCoal = "pause_for_coal";
    public static string CoalLevel = "coal_level";
    public static string PauseForWater = "pause_for_water";
    public static string WaterLevel = "water_level";
    public static string PauseAtNextStation = "pause_next_station";
    public static string PauseAtTerminusStation = "pause_terminus_station";
    public static string PreventLoadWhenPausedAtStation = "prevent_load_when_paused";
    public static string WaitForFullPassengersTerminusStation = "wait_for_full_load_at_terminus_station";
    public static string DepartStationsWhenFull = "depart_stations_when_full";
    public static string Disable = "disable";
    public static string DirectionOfTravel = "direction_of_travel";
    public static string DOTMode = "dot_mode";
    public static string TrainStatus = "train_status";
    public static string StationSettings = "station_settings";
}

public static class StationSettingKey
{
    public static string StopAtStation = "stop_at_station";
    public static string TerminusStation = "terminus_station";
    public static string PickupPassengersForStation = "pick_up_station";
    public static string PauseAtStation = "pause_at_station";
    public static string TransferStation = "transfer_station";
    public static string PassengerMode = "passenger_mode";
}

internal static class TrainStatusKey
{
    internal static string PreviousStation = "previous_station";
    internal static string CurrentStation = "current_station";
    internal static string ArrivedAtStation = "arrived_at_station";
    internal static string AtTerminusStationEast = "at_terminus_station_east";
    internal static string AtTerminusStationWest = "at_terminus_station_west";
    internal static string AtAlarkaStation = "at_alarka_station";
    internal static string AtCochranStation = "at_cochran_station";
    internal static string TerminusStationProcedureComplete = "terminus_station_procedure_complete";
    internal static string StationProcedureComplete = "station_procedure_complete";
    internal static string CurrentlyStopped = "currently_stopped";
    internal static string CurrentStopReason = "current_stop_reason";
    internal static string StoppedUnknownDirection = "stopped_unknown_direction";
    internal static string StoppedInvalidTerminusStations = "stopped_invalid_terminus_stations";
    internal static string StoppedInvalidStations = "stopped_invalid_stations";
    internal static string StoppedUnsupportedStation = "stopped_unsupported_station";
    internal static string StoppedDiesel = "stopped_diesel";
    internal static string StoppedCoal = "stopped_coal";
    internal static string StoppedWater = "stopped_water";
    internal static string StoppedNextStation = "stopped_next_station";
    internal static string StoppedTerminusStation = "stopped_terminus_station";
    internal static string StoppedPause = "stopped_pause";
    internal static string StoppedFullLoad = "stopped_full_load";
    internal static string ReadyToDepart = "ready_to_depart";
    internal static string Departed = "departed";
    internal static string Continue = "continue";
    internal static string InferredDirectionOfTravel = "inferred_dot";
    internal static string DoTLocked = "dot_locked";
}
