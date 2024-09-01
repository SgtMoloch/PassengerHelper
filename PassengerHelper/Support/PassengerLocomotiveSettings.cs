namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using Serilog;
using UI.Builder;

public class PassengerLocomotiveSettings
{
    [NonSerialized]
    DirectionObserver dotLockedObserver = new DirectionObserver();

    [NonSerialized]
    DOTReporter dotLockedReporter;

    [NonSerialized]
    Action<bool> onChange;
    public PassengerLocomotiveSettings()
    {
        onChange += (value) => dotLockedObserver.TrackDOT(value);
    }

    internal IDisposable getDOTLockedObserver(UIPanelBuilder builder)
    {
        this.dotLockedReporter = new DOTReporter(builder);
        return dotLockedObserver.Subscribe(this.dotLockedReporter);
    }

    internal void removeDotLockedObserver()
    {
        dotLockedReporter.Unsubscribe();
        dotLockedObserver.EndTrackDOT();
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
    public bool Disable { get; set; } = false;
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
}

class DirectionObserver : IObservable<bool>
{
    private List<IObserver<bool>> observers;
    public DirectionObserver()
    {
        observers = new();
    }
    public IDisposable Subscribe(IObserver<bool> observer)
    {
        if (!observers.Contains(observer))
            observers.Add(observer);
        return new Unsubscribe(observers, observer);
    }

    private class Unsubscribe : IDisposable
    {
        private List<IObserver<bool>> _observers;
        private IObserver<bool> _observer;

        public Unsubscribe(List<IObserver<bool>> observers, IObserver<bool> observer)
        {
            this._observers = observers;
            this._observer = observer;
        }

        public void Dispose()
        {
            if (_observer != null && _observers.Contains(_observer))
                _observers.Remove(_observer);
        }
    }
    public void TrackDOT(bool dot)
    {
        foreach (var observer in observers)
        {
            observer.OnNext(dot);
        }
    }

    public void EndTrackDOT()
    {
        foreach (var observer in observers.ToArray())
            if (observers.Contains(observer))
                observer.OnCompleted();

        observers.Clear();
    }
}

class DOTReporter : IObserver<bool>
{
    private IDisposable unsubscribe;
    private UIPanelBuilder builder;
    public DOTReporter(UIPanelBuilder builder)
    {
        this.builder = builder;
    }
    public virtual void OnCompleted()
    {
        throw new NotImplementedException();
    }

    public virtual void Subscribe(IObservable<bool> provider)
    {
        if (provider != null)
            unsubscribe = provider.Subscribe(this);
    }
    public virtual void OnError(Exception error)
    {
    }

    public virtual void OnNext(bool value)
    {
        builder.Rebuild();
    }

    public virtual void Unsubscribe()
    {
        unsubscribe.Dispose();
    }
}