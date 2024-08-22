namespace PassengerHelperPlugin.Support;

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
using TMPro;
using UI.Builder;
using UI.Common;
using UnityEngine;


public class PassengerSettingsWindow
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerSettingsWindow));
    public static void Show(Car car)
    {
        BaseLocomotive _locomotive = (BaseLocomotive)car;
        string locomotiveName = _locomotive.DisplayName;

        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }

        IUIHelper uIHelper = plugin.UIHelper;
        Window passengerSettingsWindow = uIHelper.CreateWindow(500, 250, Window.Position.Center);
        passengerSettingsWindow.OnShownDidChange += (s) =>
        {
            if (!s)
            {
                plugin.SaveSettings();
            }
        };

        passengerSettingsWindow.Title = "Passenger Helper Settings for " + locomotiveName;

        if (!plugin.passengerLocomotivesSettings.TryGetValue(locomotiveName, out PassengerLocomotiveSettings passengerLocomotiveSettings))
        {
            logger.Information("Did not Find settings for {0}", locomotiveName, passengerLocomotiveSettings);
            passengerLocomotiveSettings = new PassengerLocomotiveSettings();
            plugin.passengerLocomotivesSettings.Add(locomotiveName, passengerLocomotiveSettings);
        }

        if (!plugin._locomotives.ContainsKey(_locomotive))
        {
            plugin._locomotives.Add(_locomotive, new PassengerLocomotive(_locomotive, passengerLocomotiveSettings));
        }

        uIHelper.PopulateWindow(passengerSettingsWindow, (Action<UIPanelBuilder>)delegate (UIPanelBuilder builder)
        {
            builder.VStack(delegate (UIPanelBuilder builder)
            {
                logger.Information("Populating stations for {0}", _locomotive.DisplayName);
                PopulateStations(passengerSettingsWindow, uIHelper, builder, _locomotive, passengerLocomotiveSettings, plugin);
                builder.AddExpandingVerticalSpacer();
            });

            builder.Spacer();
            builder.VStack(delegate (UIPanelBuilder builder)
            {
                logger.Information("Populating settings for {0}", _locomotive.DisplayName);
                PopulateSettings(builder, passengerLocomotiveSettings, plugin);
                builder.AddExpandingVerticalSpacer();
            });
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
        });
        passengerSettingsWindow.ShowWindow();
    }

    private static List<PassengerStop> GetPassengerStops(PassengerHelperPlugin plugin)
    {
        return PassengerStop.FindAll()
        .Where(ps => !ps.ProgressionDisabled)
        .OrderBy(d => plugin.orderedStations.IndexOf(d.identifier))
        .ToList();
    }

    private static void PopulateStations(Window passengerSettingsWindow, IUIHelper uIHelper, UIPanelBuilder builder, BaseLocomotive _locomotive, PassengerLocomotiveSettings passengerLocomotiveSettings, PassengerHelperPlugin plugin)
    {
        builder.AddLabel("Station Stops:", delegate (TMP_Text text)
        {
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }).FlexibleWidth(1f);

        StationAction[] stationActions = ((StationAction[])Enum.GetValues(typeof(StationAction)));

        logger.Information("Filtering stations to only unlocked ones");
        List<string> stationActionsList = stationActions.Select(s => s.ToString()).ToList();
        List<PassengerStop> stationStops = GetPassengerStops(plugin);
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);

        stationStops.ForEach(ps =>
        {
            string name = ps.identifier;
            string formalName = ps.name;

            builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddToggle(() => passengerLocomotiveSettings.Stations[name].include, delegate (bool on)
                {
                    logger.Information("{0} set to {1}", name, on);
                    passengerLocomotiveSettings.Stations[name].include = on;
                    List<string> passengerStopsToSelect = passengerLocomotiveSettings.Stations
                    .Where(kv =>
                            stationStops
                            .Select(stp => stp.identifier)
                            .Contains(kv.Key) && kv.Value.include == true)
                            .Select(stp => stp.Key)
                            .ToList();

                    foreach (Car coach in coaches)
                    {
                        StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, passengerStopsToSelect));
                    }

                    if (!on && passengerLocomotiveSettings.Stations[name].TerminusStation)
                    {
                        passengerLocomotiveSettings.Stations[name].TerminusStation = false;
                    }

                }).Tooltip("Enabled", $"Toggle whether {formalName} should be a station stop")
                    .Width(25f);
                builder.AddLabel(formalName, delegate (TMP_Text text)
                            {
                                text.textWrappingMode = TextWrappingModes.NoWrap;
                                text.overflowMode = TextOverflowModes.Ellipsis;
                            }).Width(175f);
                builder.AddToggle(() => passengerLocomotiveSettings.Stations[name].TerminusStation, delegate (bool on)
                {
                    int numTerminusStations = passengerLocomotiveSettings.Stations.Values.Where(s => s.TerminusStation == true).Count();

                    logger.Information("There are currently {0} terminus stations set", numTerminusStations);
                    if (numTerminusStations >= 2 && on == true)
                    {
                        logger.Information("You can only select 2 terminus stations. Please unselect one before selecting another");
                    }
                    else
                    {
                        logger.Information("{0} terminus station set to {1}", name, on);
                        passengerLocomotiveSettings.Stations[name].TerminusStation = on;

                        if (on)
                        {
                            passengerLocomotiveSettings.Stations[name].include = on;
                        }
                    }
                }).Tooltip("Enabled", $"Toggle whether {formalName} should be a terminus station")
                .Width(25f);
                builder.AddLabel("Terminus", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
                builder.Spacer();
                builder.AddLabel("Action:", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
                builder.AddDropdown(stationActionsList, ((int)passengerLocomotiveSettings.Stations[name].stationAction), delegate (int index)
            {
                logger.Information("{0} action set to {1}", name, stationActions[index]);
                passengerLocomotiveSettings.Stations[name].stationAction = stationActions[index];
            }).Width(100f)
            .Height(20f);
            });
        });

        passengerSettingsWindow.SetContentHeight(400 + (stationStops.Count - 3) * 20);
    }

    struct LocomotiveNames
    {
        public LocomotiveNames(string name, string id)
        {
            this.name = name;
            this.id = id;
        }

        internal string name { get; set; } = "None";
        internal string id { get; set; } = "";
    }
    private static void PopulateSettings(UIPanelBuilder builder, PassengerLocomotiveSettings passengerLocomotiveSettings, PassengerHelperPlugin plugin)
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
            .Tooltip("Diesel Level Percentage", "Set the percentage of diesel remaining should trigger a stop for low diesel")
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
            .Tooltip("Coal Level Percentage", "Set the percentage of coal remaining should trigger a stop for low coal")
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
            .Tooltip("Water Level Percentage", "Set the percentage of water remaining should trigger a stop for low water")
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
                builder.AddLabel("Pause At Last Station", delegate (TMP_Text text)
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
            builder.AddLabel("Wait For Full Load at Last Station", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            string tooltipLocked = "The direction setting is currently disabled, as it is being controled by PassengerHelper. " +
                                    "If you would like to enable it, it will become adjustabled again after manually issuing any " +
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
}
