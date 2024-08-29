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
                if (ps.ProgressionDisabled && setting.Stations[ps.identifier].StopAt == true)
                {
                    logger.Information($"Station {formalName} is disabled, disabling station stop");
                    setting.Stations[ps.identifier].StopAt = false;
                }

                if (ps.ProgressionDisabled && setting.Stations[ps.identifier].IsTerminusStation == true)
                {
                    logger.Information($"Station {formalName} is disabled, disabling Terminus station");
                    setting.Stations[ps.identifier].IsTerminusStation = false;
                }
            });
        });
    }
}
