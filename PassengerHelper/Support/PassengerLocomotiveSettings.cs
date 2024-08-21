namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class PassengerLocomotiveSettings
{
    public bool StopForDiesel { get; set; } = false;
    public bool StopForCoal { get; set; } = false;
    public bool StopForWater { get; set; } = false;
    public bool StopAtNextStation { get; set; } = false;
    public bool StopAtLastStation { get; set; } = false;
    public bool PointToPointMode { get; set; } = true;
    public bool LoopMode { get; set; } = false;
    public bool WaitForFullPassengersLastStation { get; set; } = false;
    public bool Disable { get; set; } = false; 

    public SortedDictionary<string, StationSetting> Stations { get; } = new() {
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
}

public class StationSetting
{
    public bool include { get; set; } = false;
    public StationAction stationAction { get; set; } = StationAction.Normal;
    public bool TerminusStation { get; set; } = false;

    public override string ToString()
    {
        return "StationSetting[ include=" + include + ", stationAction=" + stationAction + ", TerminusStation=" + TerminusStation + "]";
    }
}

public enum StationAction
{
    Normal,
    Pause
}
