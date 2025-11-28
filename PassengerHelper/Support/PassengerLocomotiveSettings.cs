namespace PassengerHelper.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Model;
using KeyValue.Runtime;
using Serilog;
using UI.Builder;
using Game.Messages;
using System.Reflection;
using Game.State;
using Support.GameObjects;
using System.Text;

public class PassengerLocomotiveSettings
{
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
        }
    }
    public bool gameLoadFlag { get; set; } = false;

    // settings to save current status of train for next game load
    public TrainStatus TrainStatus { get; set; } = new TrainStatus();

    public Dictionary<string, StationSetting> StationSettings { get; set; } = new();

    internal int getStationSettingsHash()
    {
        int prime = 31;
        int result = 1;

        result = prime * result + TrainStatus.PreviousStation.GetHashCode();
        result = prime * result + TrainStatus.CurrentStation.GetHashCode();
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

        result = prime * result + gameLoadFlag.GetHashCode();

        result = prime * result + TrainStatus.GetHashCode();
        result = prime * result + StationSettings.GetHashCode();

        return result;
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.Append("PassengerLocomotiveSettings[");
        sb.Append("PauseForDiesel=");
        sb.Append(PauseForDiesel + ", ");
        sb.Append("DieselLevel=");
        sb.Append(DieselLevel + ", ");
        sb.Append("PauseForCoal=");
        sb.Append(PauseForCoal + ", ");
        sb.Append("CoalLevel=");
        sb.Append(CoalLevel + ", ");
        sb.Append("PauseForWater=");
        sb.Append(PauseForWater + ", ");
        sb.Append("WaterLevel=");
        sb.Append(WaterLevel + ", ");
        sb.Append("PauseAtNextStation=");
        sb.Append(PauseAtNextStation + ", ");
        sb.Append("PauseAtTerminusStation=");
        sb.Append(PauseAtTerminusStation + ", ");
        sb.Append("PreventLoadWhenPausedAtStation=");
        sb.Append(PreventLoadWhenPausedAtStation + ", ");
        sb.Append("WaitForFullPassengersTerminusStation=");
        sb.Append(WaitForFullPassengersTerminusStation + ", ");
        sb.Append("Disable=");
        sb.Append(Disable + ", ");
        sb.Append("DirectionOfTravel=");
        sb.Append(DirectionOfTravel + ", ");
        sb.Append("TrainStatus=[");
        sb.Append(TrainStatus.ToString() + ", ");
        sb.Append("StationSettings=[");
        foreach (string station in StationSettings.Keys)
        {
            sb.Append( station + ": " + StationSettings[station].ToString() + ", ");
        }
        
        sb.Append("]");
        sb.Append("]");
        return sb.ToString();
    }

    public PassengerLocomotiveSettings(List<string> stationIds)
    {
        foreach (string stationId in stationIds)
        {
            StationSettings[stationId] = new();
        }
    }

    public PassengerLocomotiveSettings()
    {
        
    }

    public static PassengerLocomotiveSettings FromPropertyValue(Value value, List<string> stations)
    {
        if (value.Type != KeyValue.Runtime.ValueType.Dictionary)
        {
            throw new Exception("Unexpected type");
        }

        IReadOnlyDictionary<string, Value> dictionaryValue = value.DictionaryValue;
        Dictionary<string, StationSetting> stationSettingDict = new();

        foreach (string stationId in stations)
        {
            stationSettingDict[stationId] = new();
            if (dictionaryValue.ContainsKey(stationId))
            {
                stationSettingDict[stationId] = StationSetting.FromPropertyValue(dictionaryValue[stationId]);
            }
        }

        PassengerLocomotiveSettings pls = new PassengerLocomotiveSettings
        {
            PauseForDiesel = dictionaryValue[SettingKey.PauseForDiesel].BoolValue,
            DieselLevel = dictionaryValue[SettingKey.DieselLevel].FloatValue,
            PauseForCoal = dictionaryValue[SettingKey.PauseForCoal].BoolValue,
            CoalLevel = dictionaryValue[SettingKey.CoalLevel].FloatValue,
            PauseForWater = dictionaryValue[SettingKey.PauseForWater].BoolValue,
            WaterLevel = dictionaryValue[SettingKey.WaterLevel].FloatValue,
            PauseAtNextStation = dictionaryValue[SettingKey.PauseAtNextStation].BoolValue,
            PauseAtTerminusStation = dictionaryValue[SettingKey.PauseAtTerminusStation].BoolValue,
            PreventLoadWhenPausedAtStation = dictionaryValue[SettingKey.PreventLoadWhenPausedAtStation].BoolValue,
            WaitForFullPassengersTerminusStation = dictionaryValue[SettingKey.WaitForFullPassengersTerminusStation].BoolValue,
            Disable = dictionaryValue[SettingKey.Disable].BoolValue,
            DirectionOfTravel = (DirectionOfTravel)dictionaryValue[SettingKey.DirectionOfTravel].IntValue,
            DoTLocked = dictionaryValue[SettingKey.DoTLocked].BoolValue,
            TrainStatus = TrainStatus.FromPropertyValue(dictionaryValue[SettingKey.TrainStatus]),
            StationSettings = stationSettingDict
        };

        return pls;
    }

    public Value PropertyValue()
    {
        Dictionary<string, Value> _settingsDict = new();

        _settingsDict[SettingKey.PauseForDiesel] = Value.Bool(PauseForDiesel);
        _settingsDict[SettingKey.DieselLevel] = Value.Float(DieselLevel);
        _settingsDict[SettingKey.PauseForCoal] = Value.Bool(PauseForCoal);
        _settingsDict[SettingKey.CoalLevel] = Value.Float(CoalLevel);
        _settingsDict[SettingKey.PauseForWater] = Value.Bool(PauseForWater);
        _settingsDict[SettingKey.WaterLevel] = Value.Float(WaterLevel);
        _settingsDict[SettingKey.PauseAtNextStation] = Value.Bool(PauseAtNextStation);
        _settingsDict[SettingKey.PauseAtTerminusStation] = Value.Bool(PauseAtTerminusStation);
        _settingsDict[SettingKey.PreventLoadWhenPausedAtStation] = Value.Bool(PreventLoadWhenPausedAtStation);
        _settingsDict[SettingKey.WaitForFullPassengersTerminusStation] = Value.Bool(WaitForFullPassengersTerminusStation);
        _settingsDict[SettingKey.Disable] = Value.Bool(Disable);
        _settingsDict[SettingKey.DirectionOfTravel] = Value.Int((int)DirectionOfTravel);
        _settingsDict[SettingKey.DoTLocked] = Value.Bool(DoTLocked);
        _settingsDict[SettingKey.TrainStatus] = TrainStatus.PropertyValue();
        _createStationSettings(_settingsDict);

        return Value.Dictionary(_settingsDict);
    }
    
    private void _createStationSettings(Dictionary<string, Value> _settings)
    {
        foreach (string ps in StationSettings.Keys)
        {
            _settings[ps] = StationSettings[ps].PropertyValue();
        }
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
        StringBuilder sb = new();
        sb.Append("StationSetting[");
        sb.Append("StopAtStation=");
        sb.Append(StopAtStation + ", ");
        sb.Append("TerminusStation=");
        sb.Append(TerminusStation + ", ");
        sb.Append("PickupPassengersForStation=");
        sb.Append(PickupPassengersForStation + ", ");
        sb.Append("PauseAtStation=");
        sb.Append(PauseAtStation + ", ");
        sb.Append("TransferStation=");
        sb.Append(TransferStation + ", ");
        sb.Append("PassengerMode=");
        sb.Append(PassengerMode);
        sb.Append("]");
        return sb.ToString();
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
            StopAtStation = dictionaryValue[StationSettingKey.StopAtStation].BoolValue,
            TerminusStation = dictionaryValue[StationSettingKey.TerminusStation].BoolValue,
            PickupPassengersForStation = dictionaryValue[StationSettingKey.PickupPassengersForStation].BoolValue,
            PauseAtStation = dictionaryValue[StationSettingKey.PauseAtStation].BoolValue,
            TransferStation = dictionaryValue[StationSettingKey.TransferStation].BoolValue,
            PassengerMode = (PassengerMode)dictionaryValue[StationSettingKey.PassengerMode].IntValue
        };
    }

    public Value PropertyValue()
    {
        Dictionary<string, Value> _stationSetting = new();

        _stationSetting[StationSettingKey.StopAtStation] = Value.Bool(StopAtStation);
        _stationSetting[StationSettingKey.TerminusStation] = Value.Bool(TerminusStation);
        _stationSetting[StationSettingKey.PickupPassengersForStation] = Value.Bool(PickupPassengersForStation);
        _stationSetting[StationSettingKey.PauseAtStation] = Value.Bool(PauseAtStation);
        _stationSetting[StationSettingKey.TransferStation] = Value.Bool(TransferStation);
        _stationSetting[StationSettingKey.PassengerMode] = Value.Int(((int)PassengerMode));

        return Value.Dictionary(_stationSetting);
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
            PreviousStation = dictionaryValue[TrainStatusKey.PreviousStation].StringValue,
            CurrentStation = dictionaryValue[TrainStatusKey.CurrentStation].StringValue,
            Arrived = dictionaryValue[TrainStatusKey.ArrivedAtStation].BoolValue,
            AtTerminusStationEast = dictionaryValue[TrainStatusKey.AtTerminusStationEast].BoolValue,
            AtTerminusStationWest = dictionaryValue[TrainStatusKey.AtTerminusStationWest].BoolValue,
            AtAlarka = dictionaryValue[TrainStatusKey.AtAlarkaStation].BoolValue,
            AtCochran = dictionaryValue[TrainStatusKey.AtCochranStation].BoolValue,
            TerminusStationProcedureComplete = dictionaryValue[TrainStatusKey.TerminusStationProcedureComplete].BoolValue,
            NonTerminusStationProcedureComplete = dictionaryValue[TrainStatusKey.StationProcedureComplete].BoolValue,
            CurrentlyStopped = dictionaryValue[TrainStatusKey.CurrentlyStopped].BoolValue,
            CurrentReasonForStop = dictionaryValue[TrainStatusKey.CurrentStopReason].StringValue,
            StoppedUnknownDirection = dictionaryValue[TrainStatusKey.StoppedUnknownDirection].BoolValue,
            StoppedInsufficientTerminusStations = dictionaryValue[TrainStatusKey.StoppedInvalidTerminusStations].BoolValue,
            StoppedInsufficientStopAtStations = dictionaryValue[TrainStatusKey.StoppedInvalidStations].BoolValue,
            StoppedForDiesel = dictionaryValue[TrainStatusKey.StoppedDiesel].BoolValue,
            StoppedForCoal = dictionaryValue[TrainStatusKey.StoppedCoal].BoolValue,
            StoppedForWater = dictionaryValue[TrainStatusKey.StoppedWater].BoolValue,
            StoppedNextStation = dictionaryValue[TrainStatusKey.StoppedNextStation].BoolValue,
            StoppedTerminusStation = dictionaryValue[TrainStatusKey.StoppedTerminusStation].BoolValue,
            StoppedStationPause = dictionaryValue[TrainStatusKey.StoppedPause].BoolValue,
            StoppedWaitForFullLoad = dictionaryValue[TrainStatusKey.StoppedFullLoad].BoolValue,
            ReadyToDepart = dictionaryValue[TrainStatusKey.ReadyToDepart].BoolValue,
            Departed = dictionaryValue[TrainStatusKey.Departed].BoolValue,
            Continue = dictionaryValue[TrainStatusKey.Continue].BoolValue,
        };
    }

    public Value PropertyValue()
    {
        Dictionary<string, Value> _trainStatus = new();

        _trainStatus[TrainStatusKey.PreviousStation] = Value.String(PreviousStation);
        _trainStatus[TrainStatusKey.CurrentStation] = Value.String(CurrentStation);
        _trainStatus[TrainStatusKey.ArrivedAtStation] = Value.Bool(Arrived);
        _trainStatus[TrainStatusKey.AtTerminusStationEast] = Value.Bool(AtTerminusStationEast);
        _trainStatus[TrainStatusKey.AtTerminusStationWest] = Value.Bool(AtTerminusStationWest);
        _trainStatus[TrainStatusKey.AtAlarkaStation] = Value.Bool(AtAlarka);
        _trainStatus[TrainStatusKey.AtCochranStation] = Value.Bool(AtCochran);
        _trainStatus[TrainStatusKey.TerminusStationProcedureComplete] = Value.Bool(TerminusStationProcedureComplete);
        _trainStatus[TrainStatusKey.StationProcedureComplete] = Value.Bool(NonTerminusStationProcedureComplete);
        _trainStatus[TrainStatusKey.CurrentlyStopped] = Value.Bool(CurrentlyStopped);
        _trainStatus[TrainStatusKey.CurrentStopReason] = Value.String(CurrentReasonForStop);
        _trainStatus[TrainStatusKey.StoppedUnknownDirection] = Value.Bool(StoppedUnknownDirection);
        _trainStatus[TrainStatusKey.StoppedInvalidTerminusStations] = Value.Bool(StoppedInsufficientTerminusStations);
        _trainStatus[TrainStatusKey.StoppedInvalidStations] = Value.Bool(StoppedInsufficientStopAtStations);
        _trainStatus[TrainStatusKey.StoppedDiesel] = Value.Bool(StoppedForDiesel);
        _trainStatus[TrainStatusKey.StoppedCoal] = Value.Bool(StoppedForCoal);
        _trainStatus[TrainStatusKey.StoppedWater] = Value.Bool(StoppedForWater);
        _trainStatus[TrainStatusKey.StoppedNextStation] = Value.Bool(StoppedNextStation);
        _trainStatus[TrainStatusKey.StoppedTerminusStation] = Value.Bool(StoppedTerminusStation);
        _trainStatus[TrainStatusKey.StoppedPause] = Value.Bool(StoppedStationPause);
        _trainStatus[TrainStatusKey.StoppedFullLoad] = Value.Bool(StoppedWaitForFullLoad);
        _trainStatus[TrainStatusKey.ReadyToDepart] = Value.Bool(ReadyToDepart);
        _trainStatus[TrainStatusKey.Departed] = Value.Bool(Departed);
        _trainStatus[TrainStatusKey.Continue] = Value.Bool(Continue);

        return Value.Dictionary(_trainStatus);
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.Append("PassengerLocomotiveSettings[");
        sb.Append("PreviousStation=");
        sb.Append(PreviousStation == "" ? "None" : PreviousStation + ", ");
        sb.Append("CurrentStation=");
        sb.Append(CurrentStation == "" ? "None" : CurrentStation + ", ");
        sb.Append("Arrived=");
        sb.Append(Arrived + ", ");
        sb.Append("AtTerminusStationEast=");
        sb.Append(AtTerminusStationEast + ", ");
        sb.Append("AtTerminusStationWest=");
        sb.Append(AtTerminusStationWest + ", ");
        sb.Append("AtAlarka=");
        sb.Append(AtAlarka + ", ");
        sb.Append("AtCochran=");
        sb.Append(AtCochran + ", ");
        sb.Append("TerminusStationProcedureComplete=");
        sb.Append(TerminusStationProcedureComplete + ", ");
        sb.Append("NonTerminusStationProcedureComplete=");
        sb.Append(NonTerminusStationProcedureComplete + ", ");
        sb.Append("CurrentlyStopped=");
        sb.Append(CurrentlyStopped + ", ");
        sb.Append("CurrentReasonForStop=");
        sb.Append(CurrentReasonForStop + ", ");
        sb.Append("StoppedUnknownDirection=");
        sb.Append(StoppedUnknownDirection + ", ");
        sb.Append("StoppedInsufficientTerminusStations=");
        sb.Append(StoppedInsufficientTerminusStations + ", ");
        sb.Append("StoppedInsufficientStopAtStations=");
        sb.Append(StoppedInsufficientStopAtStations + ", ");
        sb.Append("StoppedForDiesel=");
        sb.Append(StoppedForDiesel + ", ");
        sb.Append("StoppedForCoal=");
        sb.Append(StoppedForCoal + ", ");
        sb.Append("StoppedForWater=");
        sb.Append(StoppedForWater + ", ");
        sb.Append("StoppedNextStation=");
        sb.Append(StoppedNextStation + ", ");
        sb.Append("StoppedTerminusStation=");
        sb.Append(StoppedTerminusStation + ", ");
        sb.Append("StoppedStationPause=");
        sb.Append(StoppedStationPause + ", ");
        sb.Append("StoppedWaitForFullLoad=");
        sb.Append(StoppedWaitForFullLoad + ", ");
        sb.Append("ReadyToDepart=");
        sb.Append(ReadyToDepart + ", ");
        sb.Append("Departed=");
        sb.Append(Departed + ", ");
        sb.Append("Continue=");
        sb.Append(Continue + ", ");
        sb.Append("]");
        return sb.ToString();
    }
}

public class DOTChangedEvent
{

}