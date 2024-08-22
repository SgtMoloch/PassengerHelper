using Game.Notices;
using Model;
using RollingStock;
using Serilog;

namespace PassengerHelperPlugin.Support;


public class StationManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(StationManager));

    public bool ShouldPauseAtCurrentStation(PassengerLocomotiveSettings settings, PassengerStop _nextStop, BaseLocomotive _locomotive, PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.Continue)
        {
            return false;
        }

        if (settings.StopAtNextStation)
        {
            logger.Information("Pausing at station because 'Pause at next Station' setting was set");
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at next station";
            return false;
        }

        if (settings.Stations[_nextStop.identifier].stationAction == StationAction.Pause)
        {
            logger.Information("Pausing at {0} due to setting", _nextStop.DisplayName);
            _locomotive.PostNotice("ai-stop", $"Paused at {Hyperlink.To(_nextStop)}.");
            passengerLocomotive.CurrentlyStopped = true;
            passengerLocomotive.CurrentReasonForStop = "Requested pause at " + _nextStop.DisplayName;
            return false;
        }

        return false;
    }
}