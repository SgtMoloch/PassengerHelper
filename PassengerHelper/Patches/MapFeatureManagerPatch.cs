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

            shared.passengerLocomotivesSettings
            .Select(p => p.Value)
            .ToList()
            .ForEach(setting =>
            {
                if (ps.ProgressionDisabled && setting.Stations[ps.identifier].include == true)
                {
                    logger.Information($"Station {formalName} is disabled, disbaling station stop");
                    setting.Stations[ps.identifier].include = false;
                }
            });
        });
    }
}
