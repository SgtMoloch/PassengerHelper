namespace PassengerHelperPlugin.Patches;

using System.Linq;
using Game.Progression;
using HarmonyLib;
using RollingStock;
using Serilog;

[HarmonyPatch]
public static class MapFeatureManagerPatches
{
    static ILogger logger = Log.ForContext(typeof(MapFeatureManagerPatches));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapFeatureManager), "HandleFeatureEnablesChanged")]
    private static void HandleFeatureEnablesChanged()
    {
        logger.Information("Progressions Changed. Checking Stations");
        PassengerHelperPlugin shared = PassengerHelperPlugin.Shared;

        if (!shared.IsEnabled)
        {
            return;
        }

        PassengerStop.FindAll().ToList().ForEach(ps =>
        {
            string name = ps.identifier;
            string formalName = ps.name;

            shared.settingsManager.GetAllSettings()
            .Select(p => p.Value)
            .ToList()
            .ForEach(setting =>
            {
                if (ps.ProgressionDisabled)
                {
                    logger.Information($"Station {formalName} is disabled, disabling station stop");
                    setting.StationSettings[ps.identifier].StopAtStation = false;
                    logger.Information($"Station {formalName} is disabled, disabling Terminus station");
                    setting.StationSettings[ps.identifier].TerminusStation = false;
                    logger.Information($"Station {formalName} is disabled, disabling passenger pickup");
                    setting.StationSettings[ps.identifier].PickupPassengersForStation = false;
                    logger.Information($"Station {formalName} is disabled, disabling transfer station");
                    setting.StationSettings[ps.identifier].TransferStation = false;
                    logger.Information($"Station {formalName} is disabled, disabling pause");
                    setting.StationSettings[ps.identifier].PauseAtStation = false;
                }
            });
        });
    }
}
