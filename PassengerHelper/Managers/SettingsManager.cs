namespace PassengerHelperPlugin.Managers;

using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.Definition;
using Railloader;
using RollingStock;
using Serilog;
using Support;
using TMPro;
using UI.Builder;
using UI.Common;

public class SettingsManager
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(SettingsManager));
    private Dictionary<string, PassengerLocomotiveSettings> _settings;
    internal IUIHelper uIHelper { get; }

    private PassengerHelperPlugin plugin;


    public SettingsManager(PassengerHelperPlugin plugin, Dictionary<string, PassengerLocomotiveSettings> _settings, IUIHelper uIHelper)
    {
        this._settings = _settings;
        this.plugin = plugin;
        this.uIHelper = uIHelper;
    }

    public void SaveSettings()
    {
        logger.Information("Saving settings");
        plugin.SaveSettings(this._settings);
    }

    public PassengerLocomotiveSettings GetSettings(string locomotiveDisplayName)
    {
        logger.Information("Getting Passenger Settings for {0}", locomotiveDisplayName);
        if (!_settings.TryGetValue(locomotiveDisplayName, out PassengerLocomotiveSettings settings))
        {
            logger.Information("Did not Find settings for {0}, creating new settings", locomotiveDisplayName);
            settings = new PassengerLocomotiveSettings();

            logger.Information("Adding new settings to internal Dictionary");
            this._settings.Add(locomotiveDisplayName, settings);

            SaveSettings();
        }

        return settings;
    }

    public Dictionary<string, PassengerLocomotiveSettings> GetAllSettings()
    {
        return this._settings;
    }

    public void AddSettings(string locomotiveDisplayName, PassengerLocomotiveSettings settings)
    {
        this._settings.Add(locomotiveDisplayName, settings);
    }

    private Window CreateSettingsWindow(string locomotiveDisplayName)
    {
        Window passengerSettingsWindow = uIHelper.CreateWindow(550, 250, Window.Position.Center);
        passengerSettingsWindow.OnShownDidChange += (s) =>
        {
            if (!s)
            {
                SaveSettings();
            }
        };

        passengerSettingsWindow.Title = "Passenger Helper Settings for " + locomotiveDisplayName;

        return passengerSettingsWindow;
    }

    internal PassengerLocomotiveSettings ShowSettingsWindow(BaseLocomotive _locomotive)
    {
        string locomotiveDisplayName = _locomotive.DisplayName;

        Window passengerSettingsWindow = CreateSettingsWindow(locomotiveDisplayName);

        PassengerLocomotiveSettings passengerLocomotiveSettings = GetSettings(locomotiveDisplayName);

        PassengerSettingsWindow settingsWindow = new PassengerSettingsWindow(this.uIHelper, this.plugin.stationManager);

        settingsWindow.PopulateAndShowSettingsWindow(passengerSettingsWindow, passengerLocomotiveSettings, _locomotive);

        return passengerLocomotiveSettings;
    }
}

public class PassengerSettingsWindow
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerSettingsWindow));
    private IUIHelper uIHelper;
    private StationManager stationManager;

    internal PassengerSettingsWindow(IUIHelper uIHelper, StationManager stationManager)
    {
        this.uIHelper = uIHelper;
        this.stationManager = stationManager;
    }

    internal void PopulateAndShowSettingsWindow(Window passengerSettingsWindow, PassengerLocomotiveSettings passengerLocomotiveSettings, BaseLocomotive _locomotive)
    {
        uIHelper.PopulateWindow(passengerSettingsWindow, (Action<UIPanelBuilder>)delegate (UIPanelBuilder builder)
        {
            builder.VStack(delegate (UIPanelBuilder builder)
            {
                logger.Information("Populating station settings for {0}", _locomotive.DisplayName);
                PopulateStationSettings(passengerSettingsWindow, builder, _locomotive, passengerLocomotiveSettings);
                builder.AddExpandingVerticalSpacer();
            });

            builder.Spacer();
            builder.VStack(delegate (UIPanelBuilder builder)
            {
                logger.Information("Populating settings for {0}", _locomotive.DisplayName);
                PopulateSettings(builder, passengerLocomotiveSettings);
                builder.AddExpandingVerticalSpacer();
            });
            AddSaveButton(builder, passengerSettingsWindow);
        });

        passengerSettingsWindow.ShowWindow();
    }

    private void PopulateStationSettings(Window passengerSettingsWindow, UIPanelBuilder builder, BaseLocomotive _locomotive, PassengerLocomotiveSettings passengerLocomotiveSettings)
    {
        builder.AddSection("Station Settings:");

        logger.Information("Filtering stations to only unlocked ones");

        List<PassengerStop> stationStops = this.stationManager.GetPassengerStops();

        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);

        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddLabel("<b>Station</b>").Width(175f);
            builder.AddLabel("<b>Stop At</b>").Width(70f);
            builder.AddLabel("<b>Terminus</b>").Width(85f);
            builder.AddLabel("<b>Pickup For</b>").Width(100f);
            builder.AddLabel("<b>Action</b>").Width(100f);
        });
        stationStops.ForEach(ps =>
        {
            string stationId = ps.identifier;
            string stationName = ps.name;
            BuildStationSettingsRow(builder, passengerLocomotiveSettings, stationStops, coaches, stationId, stationName);

        });

        passengerSettingsWindow.SetContentHeight(400 + (stationStops.Count - 3) * 20);
    }

    private void BuildStationSettingsRow(UIPanelBuilder builder, PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops, IEnumerable<Car> coaches, string stationId, string stationName)
    {
        StationAction[] stationActions = ((StationAction[])Enum.GetValues(typeof(StationAction)));
        List<string> stationActionsList = stationActions.Select(s => s.ToString()).ToList();
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddLabel(stationName, delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).Width(175f);
            builder.AddToggle(() => passengerLocomotiveSettings.Stations[stationId].StopAt, delegate (bool on)
            {
                logger.Information("StopAt for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.Stations[stationId].StopAt = on;

                if (!on && passengerLocomotiveSettings.Stations[stationId].IsTerminusStation)
                {
                    passengerLocomotiveSettings.Stations[stationId].IsTerminusStation = false;
                }

                if (on)
                {
                    passengerLocomotiveSettings.Stations[stationId].PickupPassengers = true;
                }

            }).Tooltip("Enabled", $"Toggle whether {stationName} should be stopped at by the train")
            .Width(70f);
            builder.AddToggle(() => passengerLocomotiveSettings.Stations[stationId].IsTerminusStation, delegate (bool on)
            {
                int numTerminusStations = passengerLocomotiveSettings.Stations.Values.Where(s => s.IsTerminusStation == true).Count();

                logger.Information("There are currently {0} terminus stations set", numTerminusStations);
                if (numTerminusStations >= 2 && on == true)
                {
                    logger.Information("You can only select 2 terminus stations. Please unselect one before selecting another");
                }
                else
                {
                    logger.Information("IsTerminusStation for {0} set to {1}", stationId, on);
                    passengerLocomotiveSettings.Stations[stationId].IsTerminusStation = on;

                    if (on)
                    {
                        passengerLocomotiveSettings.Stations[stationId].StopAt = true;
                    }
                }
            }).Tooltip("Enabled", $"Toggle whether {stationName} should be a terminus station").Width(85f);
            builder.AddToggle(() => passengerLocomotiveSettings.Stations[stationId].PickupPassengers, delegate (bool on)
            {
                logger.Information("Pickup Passengers for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.Stations[stationId].PickupPassengers = on;

                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            }).Tooltip("Enabled", $"Toggle whether passengers for {stationName} should be picked up at the stations toggled in 'Stop At'").Width(100f);
            builder.AddDropdown(stationActionsList, ((int)passengerLocomotiveSettings.Stations[stationId].StationAction), delegate (int index)
            {
                logger.Information("Station Action for {0} set to {1}", stationId, stationActions[index].ToString());
                passengerLocomotiveSettings.Stations[stationId].StationAction = stationActions[index];
            }).Width(100f).Height(20f);
        });
    }



    private void BuildStationActionDropDown(UIPanelBuilder builder, PassengerLocomotiveSettings passengerLocomotiveSettings, string stationId)
    {

    }

    private void SelectStationOnCoaches(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops, IEnumerable<Car> coaches)
    {
        List<string> passengerStopsToSelect = passengerLocomotiveSettings.Stations
        .Where(kv =>
                stationStops
                .Select(stp => stp.identifier)
                .Contains(kv.Key) && kv.Value.StopAt == true)
                .Select(stp => stp.Key)
                .ToList();

        logger.Information("Applying selected stations {0} to all coupled coaches", passengerStopsToSelect);

        foreach (Car coach in coaches)
        {
            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, passengerStopsToSelect));
        }
    }

    private void PopulateSettings(UIPanelBuilder builder, PassengerLocomotiveSettings passengerLocomotiveSettings)
    {
        builder.AddLabel("Settings:", delegate (TMP_Text text)
        {
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }).FlexibleWidth(1f);
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.Disable, delegate (bool on)
            {
                logger.Information("Disable set to {0}", on);
                passengerLocomotiveSettings.Disable = on;
            }).Tooltip("Enabled", $"Toggle whether PassengerHelper should be disabled or not")
            .Width(25f);
            builder.AddLabel("Disable", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.StopForDiesel, delegate (bool on)
            {
                logger.Information("Stop for Diesel set to {0}", on);
                passengerLocomotiveSettings.StopForDiesel = on;
            }).Tooltip("Enabled", $"Toggle whether the AI should stop for low diesel")
            .Width(25f);
            builder.AddLabel("Stop for low Diesel", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).Width(200f);
            builder.AddInputField((passengerLocomotiveSettings.DieselLevel * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Information("Entered a Diesel Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Information("Entered a Diesel Level: {0}%", value);
                    passengerLocomotiveSettings.DieselLevel = value / 100;
                }
            }, null, 2)
            .Tooltip("Diesel Level Percentage", "Set the percentage of diesel remaining that should trigger a stop for low diesel")
            .Width(50f)
            .Height(20f);
            builder.AddLabel("%", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
            builder.Spacer();
        });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.StopForCoal, delegate (bool on)
            {
                logger.Information("Stop for Coal set to {0}", on);
                passengerLocomotiveSettings.StopForCoal = on;
            }).Tooltip("Enabled", $"Toggle whether the AI should stop for low coal")
            .Width(25f);
            builder.AddLabel("Stop for low Coal", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).Width(200f);
            builder.AddInputField((passengerLocomotiveSettings.CoalLevel * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Information("Entered a Coal Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Information("Entered a Coal Level: {0}%", value);
                    passengerLocomotiveSettings.CoalLevel = value / 100;
                }
            }, null, 2)
            .Tooltip("Coal Level Percentage", "Set the percentage of coal remaining that should trigger a stop for low coal")
            .Width(50f)
            .Height(20f);
            builder.AddLabel("%", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
            builder.Spacer();
        });
        builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddToggle(() => passengerLocomotiveSettings.StopForWater, delegate (bool on)
                {
                    logger.Information("Stop for Water set to {0}", on);
                    passengerLocomotiveSettings.StopForWater = on;
                }).Tooltip("Enabled", $"Toggle whether the AI should pause for low water")
                .Width(25f);
                builder.AddLabel("Stop for low Water", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).Width(200f);
                builder.AddInputField((passengerLocomotiveSettings.WaterLevel * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Information("Entered a Water Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Information("Entered a Water Level: {0}%", value);
                    passengerLocomotiveSettings.WaterLevel = value / 100;
                }
            }, null, 2)
            .Tooltip("Water Level Percentage", "Set the percentage of water remaining that should trigger a stop for low water")
            .Width(50f)
            .Height(20f);
                builder.AddLabel("%", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).FlexibleWidth(1f);
                builder.Spacer();
            });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.StopAtNextStation, delegate (bool on)
            {
                logger.Information("Pause at next station set to {0}", on);
                passengerLocomotiveSettings.StopAtNextStation = on;
            }).Tooltip("Enabled", $"Toggle whether the AI should pause at the next station")
            .Width(25f);
            builder.AddLabel("Pause At Next Station", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddToggle(() => passengerLocomotiveSettings.StopAtLastStation, delegate (bool on)
                {
                    logger.Information("Pause at terminus station set to {0}", on);
                    passengerLocomotiveSettings.StopAtLastStation = on;
                }).Tooltip("Enabled", $"Toggle whether the AI should pause at the terminus station")
                .Width(25f);
                builder.AddLabel("Pause At Terminus Station", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).FlexibleWidth(1f);
            });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.WaitForFullPassengersLastStation, delegate (bool on)
            {
                logger.Information("Wait for full passengers at terminus station set to {0}", on);
                passengerLocomotiveSettings.WaitForFullPassengersLastStation = on;
            }).Tooltip("Enabled", $"Toggle whether the AI should wait for a full passenger load at the terminus station before continuing on")
            .Width(25f);
            builder.AddLabel("Wait For Full Load at Terminus Station", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            string tooltipLocked = "The direction setting is currently disabled, as it is being controlled by PassengerHelper. " +
                                    "If you would like to enable it, it will become adjustable again after manually issuing any " +
                                    "order to the engine, whether that be changing the AI mode, changing direction, or changing speed. ";

            string tooltipUnlocked = "This setting helps PassengerHelper, as without it, if you changed terminus station mid route " +
                                    "it would probably reverse direction when you wouldn't want that. " +
                                    "PassengerHelper can kind of figure this out on its own, but setting it will help it help you. " +
                                    "This is more of an edge case than anything else.";

            builder.AddField("Direction of Travel", builder.AddSliderQuantized(() => ((int)passengerLocomotiveSettings.DirectionOfTravel), () => passengerLocomotiveSettings.DirectionOfTravel.ToString(), delegate (float value)
            {
                int newValue = (int)value;
                if (!passengerLocomotiveSettings.DoTLocked)
                {
                    DirectionOfTravel newDirectionOfTravel = (DirectionOfTravel)Enum.GetValues(typeof(DirectionOfTravel)).GetValue(newValue);
                    logger.Information("Set direction of travel to: {0}", newDirectionOfTravel.ToString());
                    passengerLocomotiveSettings.DirectionOfTravel = newDirectionOfTravel;
                }

            }, 1f, 0, 2).Width(150f)).Tooltip("Direction of Travel", passengerLocomotiveSettings.DoTLocked ? tooltipLocked + tooltipUnlocked : tooltipUnlocked);
            builder.Spacer().FlexibleWidth(1f);
        });

        builder.AddExpandingVerticalSpacer();
        builder.AddLabel("Passenger Train Mode:");
        builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddToggle(() => passengerLocomotiveSettings.LoopMode, delegate (bool on)
                {
                    logger.Information("Setting Loop mode to: {0} and Point to Point Mode to", on, !on);
                    passengerLocomotiveSettings.LoopMode = on;
                    passengerLocomotiveSettings.PointToPointMode = !on;
                }).Tooltip("Enabled", $"Toggle whether the AI should continue forward at the terminus station. Checking the box assumes that you are using loops of some kind for your passenger trains. If you are NOT using loops, this setting should NOT be checked. The two settings are mutually exclusive with one another.")
                .Width(25f);
                builder.AddLabel("Loop Mode", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).FlexibleWidth(1f);
            });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.PointToPointMode, delegate (bool on)
            {
                logger.Information("Setting Point to Point mode to: {0} and Loop Mode to", on, !on);
                passengerLocomotiveSettings.PointToPointMode = on;
                passengerLocomotiveSettings.LoopMode = !on;
            }).Tooltip("Enabled", $"Toggle whether the AI should continue reverse at the terminus station. Checking the box assumes that you are not using loops for your passenger trains, as such the train will reverse direction at the terminus station. If you want to use loops, this setting should NOT be checked. The two settings are mutually exclusive with one another.")
            .Width(25f);
            builder.AddLabel("Point to Point Mode", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
    }

    private void AddSaveButton(UIPanelBuilder builder, Window passengerSettingsWindow)
    {
        builder.VStack(delegate (UIPanelBuilder builder)
        {
            builder.AddExpandingVerticalSpacer();
            builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.Spacer().FlexibleWidth(1f);
                builder.AddButton("Save Settings", () =>
                {
                    passengerSettingsWindow.CloseWindow();
                });
            });
        });
    }
}