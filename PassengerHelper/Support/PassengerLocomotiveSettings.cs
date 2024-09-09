namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
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

    public bool StopForDiesel { get; set; } = false;
    public float DieselLevel { get; set; } = 0.10f;
    public bool StopForCoal { get; set; } = false;
    public float CoalLevel { get; set; } = 0.10f;
    public bool StopForWater { get; set; } = false;
    public float WaterLevel { get; set; } = 0.10f;
    public bool StopAtNextStation { get; set; } = false;
    public bool StopAtTerminusStation { get; set; } = false;
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

    public SortedDictionary<string, StationSetting> StationSettings { get; } = new() {
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
        result = prime * result + StopForDiesel.GetHashCode();
        result = prime * result + DieselLevel.GetHashCode();

        result = prime * result + StopForCoal.GetHashCode();
        result = prime * result + CoalLevel.GetHashCode();

        result = prime * result + StopForWater.GetHashCode();
        result = prime * result + WaterLevel.GetHashCode();

        result = prime * result + StopAtNextStation.GetHashCode();
        result = prime * result + StopAtTerminusStation.GetHashCode();

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
}

public enum PassengerMode
{
    PointToPoint,
    Loop
}

public enum DirectionOfTravel
{
    EAST,
    UNKNOWN,
    WEST
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
}

public class DOTChangedEvent
{

}