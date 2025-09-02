namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using KeyValue.Runtime;
using Serilog;
using UI.Builder;

public class PassengerLocomotiveSettings
{

    [NonSerialized]
    Action<bool> onChange;

    public PassengerLocomotiveSettings()
    {
        onChange += (value) => Messenger.Default.Send(new DOTChangedEvent());
    }

    public bool PauseForDiesel { get; set; } = false;
    public float DieselLevel { get; set; } = 0.10f;
    public bool PauseForCoal { get; set; } = false;
    public float CoalLevel { get; set; } = 0.10f;
    public bool PauseForWater { get; set; } = false;
    public float WaterLevel { get; set; } = 0.10f;
    public bool PauseAtNextStation { get; set; } = false;
    public bool PauseAtTerminusStation { get; set; } = false;
    public bool PreventLoadWhenPausedAtStation { get; set; } = false;
    public bool WaitForFullPassengersTerminusStation { get; set; } = false;
    public bool Disable { get; set; } = true;
    public DirectionOfTravel DirectionOfTravel { get; set; } = DirectionOfTravel.UNKNOWN;
    private bool _dotLocked = false;
    public bool DoTLocked
    {
        get { return _dotLocked; }
        set
        {
            _dotLocked = value;
            if (onChange != null)
            {
                onChange(value);
            }
        }
    }
    public bool gameLoadFlag { get; set; } = false;

    // settings to save current status of train for next game load
    public TrainStatus TrainStatus { get; set; } = new TrainStatus();

    public SortedDictionary<string, StationSetting> StationSettings { get; set; } = new() {
            { "sylva", new StationSetting() },
            { "dillsboro", new StationSetting() },
            { "wilmot", new StationSetting() },
            { "whittier", new StationSetting() },
            { "ela", new StationSetting() },
            { "bryson", new StationSetting() },
            { "hemingway", new StationSetting() },
            { "alarkajct", new StationSetting() },
            { "cochran", new StationSetting() },
            { "alarka", new StationSetting() },
            { "almond", new StationSetting() },
            { "nantahala", new StationSetting() },
            { "topton", new StationSetting() },
            { "rhodo", new StationSetting() },
            { "andrews", new StationSetting() }
        };
    public List<string> GetStopAtStations()
    {
        return StationSettings.Where(ss => ss.Value.StopAtStation == true).Select(ss => ss.Key).ToList();
    }

    public List<string> GetTerminusStations()
    {
        return StationSettings.Where(ss => ss.Value.TerminusStation == true).Select(ss => ss.Key).ToList();
    }

    public List<string> GetPickupStations()
    {
        return StationSettings.Where(ss => ss.Value.PickupPassengersForStation == true).Select(ss => ss.Key).ToList();
    }

    internal int getStationSettingsHash()
    {
        int prime = 31;
        int result = 1;

        result = prime * result + TrainStatus.GetHashCode();
        result = prime * result + StationSettings.GetHashCode();

        return result;
    }
    internal int getSettingsHash()
    {
        int prime = 31;
        int result = 1;
        result = prime * result + PauseForDiesel.GetHashCode();
        result = prime * result + DieselLevel.GetHashCode();

        result = prime * result + PauseForCoal.GetHashCode();
        result = prime * result + CoalLevel.GetHashCode();

        result = prime * result + PauseForWater.GetHashCode();
        result = prime * result + WaterLevel.GetHashCode();

        result = prime * result + PauseAtNextStation.GetHashCode();
        result = prime * result + PauseAtTerminusStation.GetHashCode();

        result = prime * result + WaitForFullPassengersTerminusStation.GetHashCode();

        result = prime * result + Disable.GetHashCode();

        result = prime * result + DirectionOfTravel.GetHashCode();
        result = prime * result + DoTLocked.GetHashCode();

        result = prime * result + gameLoadFlag.GetHashCode();

        result = prime * result + TrainStatus.PreviousStation.GetHashCode();
        result = prime * result + TrainStatus.CurrentStation.GetHashCode();
        result = prime * result + StationSettings.GetHashCode();

        return result;
    }

    public static PassengerLocomotiveSettings FromPropertyValue(Value value)
    {
        if (value.Type != KeyValue.Runtime.ValueType.Dictionary)
        {
            throw new Exception("Unexpected type");
        }

        IReadOnlyDictionary<string, Value> dictionaryValue = value.DictionaryValue;
        return new PassengerLocomotiveSettings
        {
            PauseForDiesel = dictionaryValue["pause_for_diesel"].BoolValue,
            DieselLevel = dictionaryValue["diesel_level"].FloatValue,
            PauseForCoal = dictionaryValue["pause_for_coal"].BoolValue,
            CoalLevel = dictionaryValue["coal_level"].FloatValue,
            PauseForWater = dictionaryValue["pause_for_water"].BoolValue,
            WaterLevel = dictionaryValue["water_level"].FloatValue,
            PauseAtNextStation = dictionaryValue["pause_next_station"].BoolValue,
            PauseAtTerminusStation = dictionaryValue["pause_terminus_station"].BoolValue,
            PreventLoadWhenPausedAtStation = dictionaryValue["prevent_load_when_paused"].BoolValue,
            WaitForFullPassengersTerminusStation = dictionaryValue["wait_for_full_load_at_terminus_station"].BoolValue,
            DirectionOfTravel = Enum.Parse<DirectionOfTravel>(dictionaryValue["direction_of_travel"].StringValue),
            DoTLocked = dictionaryValue["dot_locked"].BoolValue,
            TrainStatus = TrainStatus.FromPropertyValue(dictionaryValue["train_status"]),
            //TODO: make this dynamic for modded station support
            StationSettings = new()
            {
                { "sylva",  StationSetting.FromPropertyValue(dictionaryValue["sylva_station_settings"]) },
                { "dillsboro",  StationSetting.FromPropertyValue(dictionaryValue["dillsboro_station_settings"]) },
                { "wilmot",  StationSetting.FromPropertyValue(dictionaryValue["wilmot_station_settings"]) },
                { "whittier",  StationSetting.FromPropertyValue(dictionaryValue["whittier_station_settings"]) },
                { "ela",  StationSetting.FromPropertyValue(dictionaryValue["ela_station_settings"]) },
                { "bryson",  StationSetting.FromPropertyValue(dictionaryValue["bryson_station_settings"]) },
                { "hemingway",  StationSetting.FromPropertyValue(dictionaryValue["hemingway_station_settings"]) },
                { "alarkajct",  StationSetting.FromPropertyValue(dictionaryValue["alarkajct_station_settings"]) },
                { "cochran",  StationSetting.FromPropertyValue(dictionaryValue["cochran_station_settings"]) },
                { "alarka",  StationSetting.FromPropertyValue(dictionaryValue["alarka_station_settings"]) },
                { "almond",  StationSetting.FromPropertyValue(dictionaryValue["almond_station_settings"]) },
                { "nantahala",  StationSetting.FromPropertyValue(dictionaryValue["nantahala_station_settings"]) },
                { "topton",  StationSetting.FromPropertyValue(dictionaryValue["topton_station_settings"]) },
                { "rhodo",  StationSetting.FromPropertyValue(dictionaryValue["rhodo_station_settings"]) },
                { "andrews",  StationSetting.FromPropertyValue(dictionaryValue["andrews_station_settings"]) }
            }
        };
    }

    public Value PropertyValue()
    {
        Dictionary<string, Value> dictionaryValue = new();
        dictionaryValue["pause_for_diesel"] = Value.Bool(PauseForDiesel);
        dictionaryValue["diesel_level"] = Value.Float(DieselLevel);
        dictionaryValue["pause_for_coal"] = Value.Bool(PauseForCoal);
        dictionaryValue["coal_level"] = Value.Float(CoalLevel);
        dictionaryValue["pause_for_water"] = Value.Bool(PauseForWater);
        dictionaryValue["water_level"] = Value.Float(WaterLevel);
        dictionaryValue["pause_next_station"] = Value.Bool(PauseAtNextStation);
        dictionaryValue["pause_terminus_station"] = Value.Bool(PauseAtTerminusStation);
        dictionaryValue["prevent_load_when_paused"] = Value.Bool(PreventLoadWhenPausedAtStation);
        dictionaryValue["wait_for_full_load_at_terminus_station"] = Value.Bool(WaitForFullPassengersTerminusStation);
        dictionaryValue["direction_of_travel"] = Value.String(DirectionOfTravel.ToString());
        dictionaryValue["dot_locked"] = Value.Bool(DoTLocked);
        dictionaryValue["train_status"] = TrainStatus.PropertyValue();

        //TODO: make this dynamic for modded station support
        dictionaryValue["sylva_station_settings"] = StationSettings["sylva"].PropertyValue();
        dictionaryValue["dillsboro_station_settings"] = StationSettings["dillsboro"].PropertyValue();
        dictionaryValue["wilmot_station_settings"] = StationSettings["wilmot"].PropertyValue();
        dictionaryValue["whittier_station_settings"] = StationSettings["whittier"].PropertyValue();
        dictionaryValue["ela_station_settings"] = StationSettings["ela"].PropertyValue();
        dictionaryValue["bryson_station_settings"] = StationSettings["bryson"].PropertyValue();
        dictionaryValue["hemingway_station_settings"] = StationSettings["hemingway"].PropertyValue();
        dictionaryValue["alarkajct_station_settings"] = StationSettings["alarkajct"].PropertyValue();
        dictionaryValue["cochran_station_settings"] = StationSettings["cochran"].PropertyValue();
        dictionaryValue["alarka_station_settings"] = StationSettings["alarka"].PropertyValue();
        dictionaryValue["almond_station_settings"] = StationSettings["almond"].PropertyValue();
        dictionaryValue["nantahala_station_settings"] = StationSettings["nantahala"].PropertyValue();
        dictionaryValue["topton_station_settings"] = StationSettings["topton"].PropertyValue();
        dictionaryValue["rhodo_station_settings"] = StationSettings["rhodo"].PropertyValue();
        dictionaryValue["andrews_station_settings"] = StationSettings["andrews"].PropertyValue();

        return Value.Dictionary(dictionaryValue);
    }
}

public class StationSetting
{
    public bool StopAtStation { get; set; } = false;
    public bool TerminusStation { get; set; } = false;
    public bool PickupPassengersForStation { get; set; } = false;
    public bool PauseAtStation { get; set; } = false;
    public bool TransferStation { get; set; } = false;
    public PassengerMode PassengerMode { get; set; } = PassengerMode.PointToPoint;

    public override string ToString()
    {
        return "StationSetting[ StopAtStation=" + StopAtStation + ", TerminusStation=" + TerminusStation + ", PickupPassengersForStation=" + PickupPassengersForStation + ", PauseAtStation=" + PauseAtStation + ", TransferStation=" + TransferStation + "PassengerMode=" + PassengerMode + "]";
    }

    public static StationSetting FromPropertyValue(Value value)
    {
        if (value.Type != KeyValue.Runtime.ValueType.Dictionary)
        {
            throw new Exception("Unexpected type");
        }

        IReadOnlyDictionary<string, Value> dictionaryValue = value.DictionaryValue;
        return new StationSetting
        {
            StopAtStation = dictionaryValue["stop_at_station"].BoolValue,
            TerminusStation = dictionaryValue["terminus_station"].BoolValue,
            PickupPassengersForStation = dictionaryValue["pick_up_station"].BoolValue,
            PauseAtStation = dictionaryValue["pause_at_station"].BoolValue,
            TransferStation = dictionaryValue["transfer_station"].BoolValue,
            PassengerMode = Enum.Parse<PassengerMode>(dictionaryValue["passenger_mode"].StringValue)
        };
    }

    public Value PropertyValue()
    {
        Dictionary<string, Value> dictionaryValue = new();

        dictionaryValue["stop_at_station"] = Value.Bool(StopAtStation);
        dictionaryValue["terminus_station"] = Value.Bool(TerminusStation);
        dictionaryValue["pick_up_station"] = Value.Bool(PickupPassengersForStation);
        dictionaryValue["pause_at_station"] = Value.Bool(PauseAtStation);
        dictionaryValue["transfer_station"] = Value.Bool(TransferStation);
        dictionaryValue["passenger_mode"] = Value.String(PassengerMode.ToString());

        return Value.Dictionary(dictionaryValue);
    }
}

public enum PassengerMode
{
    PointToPoint,
    Loop
}

public enum DirectionOfTravel
{
    WEST,
    UNKNOWN,
    EAST
}

public class TrainStatus
{
    public string PreviousStation { get; set; } = "";
    public string CurrentStation { get; set; } = "";
    public bool Arrived { get; set; } = false;
    public bool AtTerminusStationEast { get; set; } = false;
    public bool AtTerminusStationWest { get; set; } = false;
    public bool AtAlarka { get; set; } = false;
    public bool AtCochran { get; set; } = false;
    public bool TerminusStationProcedureComplete { get; set; } = false;
    public bool NonTerminusStationProcedureComplete { get; set; } = false;
    public bool CurrentlyStopped { get; set; } = false;
    public string CurrentReasonForStop { get; set; } = "";
    public bool StoppedUnknownDirection { get; set; } = false;
    public bool StoppedInsufficientTerminusStations { get; set; } = false;
    public bool StoppedInsufficientStopAtStations { get; set; } = false;
    public bool StoppedForDiesel { get; set; } = false;
    public bool StoppedForCoal { get; set; } = false;
    public bool StoppedForWater { get; set; } = false;
    public bool StoppedNextStation { get; set; } = false;
    public bool StoppedTerminusStation { get; set; } = false;
    public bool StoppedStationPause { get; set; } = false;
    public bool StoppedWaitForFullLoad { get; set; } = false;
    public bool ReadyToDepart { get; set; } = false;
    public bool Departed { get; set; } = false;
    public bool Continue { get; set; } = false;

    public void ResetStoppedFlags()
    {
        CurrentlyStopped = false;
        CurrentReasonForStop = "";
        StoppedForDiesel = false;
        StoppedForCoal = false;
        StoppedForWater = false;
        StoppedNextStation = false;
        StoppedTerminusStation = false;
        StoppedStationPause = false;
        StoppedWaitForFullLoad = false;
        StoppedInsufficientStopAtStations = false;
        StoppedInsufficientTerminusStations = false;
        StoppedUnknownDirection = false;
    }

    public void ResetStatusFlags()
    {
        ResetStoppedFlags();
        Arrived = false;
        AtTerminusStationEast = false;
        AtTerminusStationWest = false;
        AtAlarka = false;
        AtCochran = false;
        TerminusStationProcedureComplete = false;
        NonTerminusStationProcedureComplete = false;
        ReadyToDepart = false;
        Departed = false;
        Continue = false;
    }

    public bool ShouldStayStopped()
    {
        return StoppedForDiesel ||
                StoppedForCoal ||
                StoppedForWater ||
                StoppedNextStation ||
                StoppedTerminusStation ||
                StoppedStationPause ||
                StoppedWaitForFullLoad ||
                StoppedInsufficientStopAtStations ||
                StoppedInsufficientTerminusStations ||
                StoppedUnknownDirection;
    }

    public static TrainStatus FromPropertyValue(Value value)
    {
        if (value.Type != KeyValue.Runtime.ValueType.Dictionary)
        {
            throw new Exception("Unexpected type");
        }

        IReadOnlyDictionary<string, Value> dictionaryValue = value.DictionaryValue;
        return new TrainStatus
        {
            PreviousStation = dictionaryValue["previous_station"].StringValue,
            CurrentStation = dictionaryValue["current_station"].StringValue,
            Arrived = dictionaryValue["arrived_at_station"].BoolValue,
            AtTerminusStationEast = dictionaryValue["at_terminus_station_east"].BoolValue,
            AtTerminusStationWest = dictionaryValue["at_terminus_station_west"].BoolValue,
            AtAlarka = dictionaryValue["at_alarka_station"].BoolValue,
            AtCochran = dictionaryValue["at_cochran_station"].BoolValue,
            TerminusStationProcedureComplete = dictionaryValue["terminus_station_procedure_complete"].BoolValue,
            NonTerminusStationProcedureComplete = dictionaryValue["station_procedure_complete"].BoolValue,
            CurrentlyStopped = dictionaryValue["currently_stopped"].BoolValue,
            CurrentReasonForStop = dictionaryValue["current_stop_reason"].StringValue,
            StoppedUnknownDirection = dictionaryValue["stopped_unknown_direction"].BoolValue,
            StoppedInsufficientTerminusStations = dictionaryValue["stopped_invalid_terminus_stations"].BoolValue,
            StoppedInsufficientStopAtStations = dictionaryValue["stopped_invalid_stations"].BoolValue,
            StoppedForDiesel = dictionaryValue["stopped_diesel"].BoolValue,
            StoppedForCoal = dictionaryValue["stopped_coal"].BoolValue,
            StoppedForWater = dictionaryValue["stopped_water"].BoolValue,
            StoppedNextStation = dictionaryValue["stopped_next_station"].BoolValue,
            StoppedTerminusStation = dictionaryValue["stopped_terminus_station"].BoolValue,
            StoppedStationPause = dictionaryValue["stopped_pause"].BoolValue,
            StoppedWaitForFullLoad = dictionaryValue["stopped_full_load"].BoolValue,
            ReadyToDepart = dictionaryValue["ready_to_depart"].BoolValue,
            Departed = dictionaryValue["departed"].BoolValue,
            Continue = dictionaryValue["continue"].BoolValue,
        };
    }

    public Value PropertyValue()
    {
        Dictionary<string, Value> dictionaryValue = new();

        dictionaryValue["previous_station"] = Value.String(PreviousStation);
        dictionaryValue["current_station"] = Value.String(CurrentStation);
        dictionaryValue["arrived_at_station"] = Value.Bool(Arrived);
        dictionaryValue["at_terminus_station_east"] = Value.Bool(AtTerminusStationEast);
        dictionaryValue["at_terminus_station_west"] = Value.Bool(AtTerminusStationWest);
        dictionaryValue["at_alarka_station"] = Value.Bool(AtAlarka);
        dictionaryValue["at_cochran_station"] = Value.Bool(AtCochran);
        dictionaryValue["terminus_station_procedure_complete"] = Value.Bool(TerminusStationProcedureComplete);
        dictionaryValue["station_procedure_complete"] = Value.Bool(NonTerminusStationProcedureComplete);
        dictionaryValue["currently_stopped"] = Value.Bool(CurrentlyStopped);
        dictionaryValue["current_stop_reason"] = Value.String(CurrentReasonForStop);
        dictionaryValue["stopped_unknown_direction"] = Value.Bool(StoppedUnknownDirection);
        dictionaryValue["stopped_invalid_terminus_stations"] = Value.Bool(StoppedInsufficientTerminusStations);
        dictionaryValue["stopped_invalid_stations"] = Value.Bool(StoppedInsufficientStopAtStations);
        dictionaryValue["stopped_diesel"] = Value.Bool(StoppedForDiesel);
        dictionaryValue["stopped_coal"] = Value.Bool(StoppedForCoal);
        dictionaryValue["stopped_water"] = Value.Bool(StoppedForWater);
        dictionaryValue["stopped_next_station"] = Value.Bool(StoppedNextStation);
        dictionaryValue["stopped_terminus_station"] = Value.Bool(StoppedTerminusStation);
        dictionaryValue["stopped_pause"] = Value.Bool(StoppedStationPause);
        dictionaryValue["stopped_full_load"] = Value.Bool(StoppedWaitForFullLoad);
        dictionaryValue["ready_to_depart"] = Value.Bool(ReadyToDepart);
        dictionaryValue["departed"] = Value.Bool(Departed);
        dictionaryValue["continue"] = Value.Bool(Continue);

        return Value.Dictionary(dictionaryValue);
    }
}

public class DOTChangedEvent
{

}