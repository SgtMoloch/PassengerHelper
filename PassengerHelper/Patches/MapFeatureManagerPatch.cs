namespace PassengerHelperPlugin.Patches;

using System.Linq;
using Game.Progression;
using HarmonyLib;
using Model.Ops;
using RollingStock;
using Serilog;
using Support;

[HarmonyPatch]
public static class MapFeatureManagerPatches
{
    static ILogger logger = Log.ForContext(typeof(MapFeatureManagerPatches));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapFeatureManager), "HandleFeatureEnablesChanged")]
    private static void HandleFeatureEnablesChanged()
    {
        logger.Debug("Progressions Changed. Checking Stations");
        PassengerHelperPlugin shared = PassengerHelperPlugin.Shared;

        if (!shared.IsEnabled || !shared.passengerHelperSettingsGO.Loaded)
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
                    logger.Debug($"Station {formalName} is disabled, disabling Station stop At, Terminus station, Passenger Pickup, Transfer Station, and Pause");
                    setting.StationSettings[ps.identifier] = new StationSetting();
                }
            });
        });
    }
}
