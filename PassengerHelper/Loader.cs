using System.Reflection;
using Serilog;
using UnityModManagerNet;

namespace PassengerHelper.UMM;

public static class Loader
{
    static ILogger logger = Log.ForContext(typeof(Loader));
    public static UnityModManager.ModEntry ModEntry { get; private set; }
    public static PassengerHelper passengerHelper { get; private set; }

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        if (ModEntry != null)
        {
            modEntry.Logger.Warning("WaypointQueue is already loaded!");
            return false;
        }
        logger.Information($"Loading WaypointQueue assembly version {Assembly.GetExecutingAssembly().GetName().Version}");

        ModEntry = modEntry;
        passengerHelper = new PassengerHelper(modEntry.Info.Id);

        return true;
    }

    private static bool Unload(UnityModManager.ModEntry modEntry)
    {
        passengerHelper.harmony.UnpatchAll(modEntry.Info.Id);

        return true;
    }

}