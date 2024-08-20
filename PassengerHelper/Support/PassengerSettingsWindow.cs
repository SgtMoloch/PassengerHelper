namespace PassengerHelperPlugin.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Railloader;
using RollingStock;
using Serilog;
using TMPro;
using UI.Builder;
using UI.Common;
using UnityEngine;


public class PassengerSettingsWindow
{
    private static Window? passengerSettingsWindow;
    private static List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

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
        if (passengerSettingsWindow == null)
        {
            passengerSettingsWindow = uIHelper.CreateWindow(400, 250, Window.Position.Center);
            passengerSettingsWindow.OnShownDidChange += (s) =>
            {
                if (!s)
                {
                    plugin.SaveSettings();
                }
            };
        }
        passengerSettingsWindow.Title = "Passenger Helper Settings for " + _locomotive.DisplayName;

        if (!plugin.passengerLocomotivesSettings.TryGetValue(locomotiveName, out PassengerLocomotiveSettings passengerLocomotiveSettings))
        {
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
                PopulateStations(uIHelper, builder, passengerLocomotiveSettings);
                builder.AddExpandingVerticalSpacer();
            });

            builder.Spacer();
            builder.VStack(delegate (UIPanelBuilder builder)
            {
                logger.Information("Populating settings for {0}", _locomotive.DisplayName);
                PopulateSettings(builder, passengerLocomotiveSettings, plugin);
                builder.AddExpandingVerticalSpacer();
            });
        });
        passengerSettingsWindow.ShowWindow();
    }

    private static List<PassengerStop> GetPassengerStops()
    {
        return PassengerStop.FindAll()
        .Where(ps => !ps.ProgressionDisabled)
        .OrderBy(d => orderedStations.IndexOf(d.identifier))
        .ToList();
    }
    private static void PopulateStations(IUIHelper uIHelper, UIPanelBuilder builder, PassengerLocomotiveSettings passengerLocomotiveSettings)
    {
        builder.AddLabel("Station Stops:", delegate (TMP_Text text)
        {
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }).FlexibleWidth(1f);

        StationAction[] stationActions = new StationAction[] { StationAction.Normal, StationAction.Pause };

        logger.Information("Filtering stations to only unlocked ones");
        List<string> stationActionsList = stationActions.Select(s => s.ToString()).ToList();
        List<PassengerStop> stationStops = GetPassengerStops();

        int getStationActionIndex(StationAction stationAction, StationAction[] stationActions)
        {
            int index = stationActions.ToList().FindIndex(s => s == stationAction);
            if (index == -1)
            {
                logger.Information("Couldn't find selected station action {0}", stationAction);
                return 0;
            }

            return index;
        }

        stationStops.ForEach(ps =>
        {
            string name = ps.identifier;
            string formalName = ps.name;

            builder.HStack(delegate (UIPanelBuilder builder)
            {
                int stationActionIndex = 0;
                builder.AddToggle(() => passengerLocomotiveSettings.Stations[name].include, delegate (bool on)
                {
                    logger.Information("{0} set to {1}", name, on);
                    passengerLocomotiveSettings.Stations[name].include = on;
                }).Tooltip("Enabled", $"Toggle whether {formalName} should be a station stop")
                .Width(25f);
                builder.AddLabel(formalName, delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).FlexibleWidth(125f);
                builder.Spacer();
                builder.AddLabel("Action:", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).FlexibleWidth(1f);
                builder.AddDropdown(stationActionsList, getStationActionIndex(passengerLocomotiveSettings.Stations[name].stationAction, stationActions), delegate (int index)
                {
                    logger.Information("{0} action set to {1}", name, stationActions[index]);
                    stationActionIndex = index;
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


        List<BaseLocomotive> validConnectingLocomotives = plugin._locomotives.Keys.ToList();
        List<LocomotiveNames> validConnectingLocomotiveNames = new List<LocomotiveNames>() { new LocomotiveNames() };
        validConnectingLocomotiveNames.AddRange(validConnectingLocomotives.Select(x => new LocomotiveNames(x.DisplayName, x.id)));

        logger.Information("Valid trains: {0}", validConnectingLocomotiveNames);
        int getIndex(string id)
        {
            if (id == "")
            {
                return 0;
            }

            int index = validConnectingLocomotiveNames.FindIndex(x => x.id == id);

            if (index == -1)
            {
                logger.Information("Couldn't find selected connecting locomotive with id {0}", id);
                return 0;
            }

            return index;
        }

        builder.AddLabel("Settings:", delegate (TMP_Text text)
        {
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }).FlexibleWidth(1f);
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
            }).FlexibleWidth(1f);
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
            }).FlexibleWidth(1f);
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
                }).FlexibleWidth(1f);
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
                    logger.Information("Pause at last station set to {0}", on);
                    passengerLocomotiveSettings.StopAtLastStation = on;
                }).Tooltip("Enabled", $"Toggle whether the AI should pause at the last station")
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
                logger.Information("Wait for full passengers at last station set to {0}", on);
                passengerLocomotiveSettings.WaitForFullPassengersLastStation = on;
            }).Tooltip("Enabled", $"Toggle whether the AI should wait for a full passenger load at the last station before continuing on")
            .Width(25f);
            builder.AddLabel("Wait For Full Load at Last Station", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.WaitForConnectingTrain.Wait, delegate (bool on)
            {
                logger.Information("Wait for connecting train set to {0}", on);
                passengerLocomotiveSettings.WaitForConnectingTrain.Wait = on;
            }).Tooltip("Enabled", $"Toggle whether the AI should wait for a connecting train at the last station before continuing on")
            .Width(25f);
            builder.AddLabel("Connecting Train:", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
            builder.AddDropdown(validConnectingLocomotiveNames.Select(ln => ln.name).ToList(), getIndex(passengerLocomotiveSettings.WaitForConnectingTrain.trainId), delegate (int index)
            {
                logger.Information("Selected connecting train: {0}", validConnectingLocomotiveNames[index]);
                passengerLocomotiveSettings.WaitForConnectingTrain.trainId = validConnectingLocomotiveNames[index].id;
            }).Width(100f)
            .Height(20f);
            // builder.AddLabel("At:", delegate (TMP_Text text)
            // {
            //     text.textWrappingMode = TextWrappingModes.NoWrap;
            //     text.overflowMode = TextOverflowModes.Ellipsis;
            // }).FlexibleWidth(1f);
            // builder.AddDropdown(GetPassengerStops().Select(ps => ps.DisplayName).ToList(), getIndex(passengerLocomotiveSettings.WaitForConnectingTrain.trainId), delegate (int index)
            // {
            //     logger.Information("Selected connecting train: {0}", validConnectingLocomotiveNames[index]);
            //     passengerLocomotiveSettings.WaitForConnectingTrain.trainId = validConnectingLocomotiveIds[index];
            // }).Width(100f)
            // .Height(20f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddToggle(() => passengerLocomotiveSettings.LoopMode, delegate (bool on)
                {
                    passengerLocomotiveSettings.LoopMode = on;
                    passengerLocomotiveSettings.PointToPointMode = !on;
                }).Tooltip("Enabled", $"Toggle whether the AI should continue forward at the last station. A checking the box assumes that you are using loops of some kind for your passenger trains. If you are NOT using loops, this setting should NOT be checked. The two settings are mutually exclusive with one another.")
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
                passengerLocomotiveSettings.PointToPointMode = on;
                passengerLocomotiveSettings.LoopMode = !on;
            }).Tooltip("Enabled", $"Toggle whether the AI should continue reverse at the last station. A checking the box assumes that you are not using loops for your passenger trains, as such the train will reverse direction at the last station stop. If you want to use loops, this setting should NOT be checked. The two settings are mutually exclusive with one another.")
            .Width(25f);
            builder.AddLabel("Point to Point Mode", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
    }
}
