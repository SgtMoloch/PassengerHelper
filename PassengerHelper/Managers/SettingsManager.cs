namespace PassengerHelperPlugin.Managers;

using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.Messages;
using Game.State;
using Model;
using Model.Definition;
using Railloader;
using RollingStock;
using Serilog;
using Support;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.UI;

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

        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    public void SaveSettings()
    {
        logger.Information("Saving settings");
        plugin.SaveSettings(this._settings);
    }

    public void SaveSettings(string locomotiveName, TrainStatus trainStatus)
    {
        _settings[locomotiveName].TrainStatus = trainStatus;
        SaveSettings();
    }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        foreach (PassengerLocomotiveSettings settings in _settings.Values)
        {
            settings.gameLoadFlag = true;
        }
        SaveSettings();
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
        Window passengerSettingsWindow = uIHelper.CreateWindow(700, 250, Window.Position.Center);
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
            builder.AddLabel("<b>Pickup Pax</b>").Width(100f);
            builder.AddLabel("<b>Pause</b>").Width(60f);
            builder.AddLabel("<b>Transfer</b>").Width(85f);
            builder.AddLabel("<b>Mode</b>").Width(100f);
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
        PassengerMode[] passengerModes = ((PassengerMode[])Enum.GetValues(typeof(PassengerMode)));
        List<string> passengerModList = passengerModes.Select(s => s.ToString()).ToList();
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddLabel(stationName, delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).Width(175f);
            builder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].StopAtStation, delegate (bool on)
            {
                logger.Information("StopAt for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].StopAtStation = on;

                if (!on)
                {
                    passengerLocomotiveSettings.StationSettings[stationId].TerminusStation = false;
                    passengerLocomotiveSettings.StationSettings[stationId].TransferStation = false;
                }

                if (on)
                {
                    passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation = true;
                }
                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
                builder.Rebuild();
            }).Tooltip("Enabled", $"Toggle whether {stationName} should be stopped at by the train")
            .Width(70f);

            RectTransform terminusToggle = builder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].TerminusStation, delegate (bool on)
            {
                int numTerminusStations = passengerLocomotiveSettings.StationSettings.Values.Where(s => s.TerminusStation == true).Count();

                logger.Information("There are currently {0} terminus stations set", numTerminusStations);
                if (numTerminusStations >= 2 && on == true)
                {
                    logger.Information("You can only select 2 terminus stations. Please unselect one before selecting another");
                }
                else
                {
                    logger.Information("IsTerminusStation for {0} set to {1}", stationId, on);
                    passengerLocomotiveSettings.StationSettings[stationId].TerminusStation = on;

                    if (on)
                    {
                        passengerLocomotiveSettings.StationSettings[stationId].StopAtStation = true;
                        SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
                    }
                    else
                    {
                        passengerLocomotiveSettings.StationSettings[stationId].TransferStation = false;
                    }
                    builder.Rebuild();
                }
            }).Tooltip("Enabled", $"Toggle whether {stationName} should be a terminus station").Width(85f);

            terminusToggle.GetComponentInChildren<Toggle>().interactable = passengerLocomotiveSettings.StationSettings[stationId].StopAtStation;

            builder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation, delegate (bool on)
            {
                logger.Information("Pickup Passengers for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation = on;

                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            }).Tooltip("Enabled", $"Toggle whether passengers for {stationName} should be picked up at the stations toggled in 'Stop At'").Width(100f);

            RectTransform pauseAtStationToggle = builder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].PauseAtStation, delegate (bool on)
            {
                logger.Information("Pause for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].PauseAtStation = on;
            }).Tooltip("Enabled", $"Toggle whether to pause at {stationName}")
            .Width(60f);

            pauseAtStationToggle.GetComponentInChildren<Toggle>().interactable = passengerLocomotiveSettings.StationSettings[stationId].StopAtStation;

            RectTransform transferStationToggle = builder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].TransferStation, delegate (bool on)
            {
                logger.Information("Transfer for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].TransferStation = on;
            }).Tooltip("Enabled", $"Toggle whether to pause at {stationName}")
            .Width(60f);

            transferStationToggle.GetComponentInChildren<Toggle>().interactable = (passengerLocomotiveSettings.StationSettings[stationId].TerminusStation || stationId == "alarkajct");

            RectTransform passengerModeDropDown = builder.AddDropdown(passengerModList, ((int)passengerLocomotiveSettings.StationSettings[stationId].PassengerMode), delegate (int index)
            {
                logger.Information("Passenger Mode for {0} set to {1}", stationId, passengerModes[index].ToString());
                passengerLocomotiveSettings.StationSettings[stationId].PassengerMode = passengerModes[index];
            }).Width(115f).Height(20f);

            passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>().interactable = passengerLocomotiveSettings.StationSettings[stationId].StopAtStation;
        });
    }

    private void SelectStationOnCoaches(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops, IEnumerable<Car> coaches)
    {
        List<string> passengerStopsToSelect = passengerLocomotiveSettings.StationSettings
        .Where(kv =>
                stationStops
                .Select(stp => stp.identifier)
                .Contains(kv.Key) && kv.Value.PickupPassengersForStation == true)
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
        builder.AddSection("Settings:");
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
                builder.AddToggle(() => passengerLocomotiveSettings.StopAtTerminusStation, delegate (bool on)
                {
                    logger.Information("Pause at terminus station set to {0}", on);
                    passengerLocomotiveSettings.StopAtTerminusStation = on;
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
            builder.AddToggle(() => passengerLocomotiveSettings.WaitForFullPassengersTerminusStation, delegate (bool on)
            {
                logger.Information("Wait for full passengers at terminus station set to {0}", on);
                passengerLocomotiveSettings.WaitForFullPassengersTerminusStation = on;
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

            RectTransform dotSlider = builder.AddSliderQuantized(() => ((int)passengerLocomotiveSettings.DirectionOfTravel), () => passengerLocomotiveSettings.DirectionOfTravel.ToString(), delegate (float value)
                {
                    int newValue = (int)value;

                    DirectionOfTravel newDirectionOfTravel = (DirectionOfTravel)Enum.GetValues(typeof(DirectionOfTravel)).GetValue(newValue);
                    logger.Information("Set direction of travel to: {0}", newDirectionOfTravel.ToString());
                    passengerLocomotiveSettings.DirectionOfTravel = newDirectionOfTravel;


                }, 1f, 0, 2).Width(150f);
            dotSlider.GetComponentInChildren<CarControlSlider>().interactable = !passengerLocomotiveSettings.DoTLocked;
            builder.RebuildOnEvent<DOTChangedEvent>();

            builder.AddField("Direction of Travel", dotSlider).Tooltip("Direction of Travel", passengerLocomotiveSettings.DoTLocked ? tooltipLocked + tooltipUnlocked : tooltipUnlocked);
            builder.Spacer().FlexibleWidth(1f);
        });

        builder.AddExpandingVerticalSpacer();
        builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.Spacer().FlexibleWidth(1f);
                builder.AddButton("test dotlocked", () =>
                {
                    passengerLocomotiveSettings.DoTLocked = !passengerLocomotiveSettings.DoTLocked;
                });
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

