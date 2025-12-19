using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

public class UtilManager
{
    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    public List<PassengerStop> GetPassengerStops()
    {
        return PassengerStop.FindAll()
        .Where(ps => !ps.ProgressionDisabled && orderedStations.Contains(ps.identifier))
        .OrderBy(d => orderedStations.IndexOf(d.identifier))
        .ToList();
    }
}
