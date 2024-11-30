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
    private Dictionary<BaseLocomotive, bool> settingsWindowShowing = new();

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

        passengerSettingsWindow.Title = "Passenger Helper Settings for " + locomotiveDisplayName;

        return passengerSettingsWindow;
    }

    internal void ShowSettingsWindow(BaseLocomotive _locomotive)
    {

        string locomotiveDisplayName = _locomotive.DisplayName;

        if (!this.settingsWindowShowing.TryGetValue(_locomotive, out var showing))
        {
            this.settingsWindowShowing.Add(_locomotive, false);
        }

        if (this.settingsWindowShowing[_locomotive])
        {
            return;
        }

        this.settingsWindowShowing[_locomotive] = true;

        Window passengerSettingsWindow = CreateSettingsWindow(locomotiveDisplayName);

        PassengerSettingsWindow settingsWindow = new PassengerSettingsWindow(this.uIHelper, this.plugin.stationManager);
        PassengerLocomotiveSettings passengerLocomotiveSettings = GetSettings(locomotiveDisplayName);

        settingsWindow.PopulateAndShowSettingsWindow(passengerSettingsWindow, passengerLocomotiveSettings, _locomotive);

        passengerSettingsWindow.OnShownDidChange += (showing) =>
        {
            if (!showing)
            {
                this.settingsWindowShowing[_locomotive] = false;
                SaveSettings();
            }
        };

        return;
    }
}

public class PassengerSettingsWindow
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerSettingsWindow));
    private IUIHelper uIHelper;
    private StationManager stationManager;

    internal class Interactable
    {
        internal bool _terminus = false;
        internal bool Terminus
        {
            get { return _terminus; }
            set
            {
                _terminus = value;
                TerminusToggle.interactable = value;
            }
        }
        internal Toggle TerminusToggle;

        internal bool _pause = false;
        internal bool Pause
        {
            get { return _pause; }
            set
            {
                _pause = value;
                PauseToggle.interactable = value;
            }
        }
        internal Toggle PauseToggle;

        internal bool _transfer = false;
        internal bool Transfer
        {
            get { return _transfer; }
            set
            {
                _transfer = value;
                TransferToggle.interactable = value;
            }
        }
        internal Toggle TransferToggle;

        internal bool _mode = false;
        internal bool Mode
        {
            get { return _mode; }
            set
            {
                _mode = value;
                ModeDropdown.interactable = value;
            }
        }
        internal TMP_Dropdown ModeDropdown;

        public Interactable(Toggle TerminusToggle, Toggle PauseToggle, Toggle TransferToggle, TMP_Dropdown ModeDropdown)
        {
            this.TerminusToggle = TerminusToggle;
            this.PauseToggle = PauseToggle;
            this.TransferToggle = TransferToggle;
            this.ModeDropdown = ModeDropdown;
        }
    }

    internal Dictionary<string, Interactable> interactableStationMap = new();

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

        List<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();

        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddLabel("<b>Station</b>").Width(175f);
            builder.AddLabel("<b>StopAt</b>").Width(70f);
            builder.AddLabel("<b>Terminus</b>").Width(85f);
            builder.AddLabel("<b>PickUp</b>").Width(70f);
            builder.AddLabel("<b>Pause</b>").Width(60f);
            builder.AddLabel("<b>Transfer</b>").Width(85f);
            builder.AddLabel("<b>Mode</b>").Width(125f);
        });

        stationStops.ForEach(ps =>
        {
            string stationId = ps.identifier;
            string stationName = ps.name;
            BuildStationSettingsRow(builder, passengerLocomotiveSettings, stationStops, coaches, stationId, stationName);

        });
        SetInteractions(passengerLocomotiveSettings, stationStops);
        builder.Spacer().Height(5f);
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddButton("StopAt All Stations", () =>
            {
                ClearSelections(passengerLocomotiveSettings, stationStops);
                SelectAllStopAt(passengerLocomotiveSettings, stationStops);
                SetInteractions(passengerLocomotiveSettings, stationStops);
                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            });
            hBuilder.AddButton("PickUp All Stations", () =>
            {
                SelectAllPickup(passengerLocomotiveSettings, stationStops);
            });
            IConfigurableElement button = hBuilder.AddButton("Alarka Branch", () =>
            {
                ClearSelections(passengerLocomotiveSettings, stationStops);
                passengerLocomotiveSettings.StationSettings["alarkajct"].TerminusStation = true;
                passengerLocomotiveSettings.StationSettings["alarkajct"].TransferStation = true;

                passengerLocomotiveSettings.StationSettings["alarka"].TerminusStation = true;

                passengerLocomotiveSettings.StationSettings["alarkajct"].StopAtStation = true;
                passengerLocomotiveSettings.StationSettings["cochran"].StopAtStation = true;
                passengerLocomotiveSettings.StationSettings["alarka"].StopAtStation = true;

                SelectAllPickup(passengerLocomotiveSettings, stationStops);
                SetInteractions(passengerLocomotiveSettings, stationStops);
                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            });

            button.RectTransform.GetComponentInChildren<Button>().interactable = stationStops.Select(ps => ps.identifier).Contains("alarka");

            hBuilder.AddButton("Clear Selections", () =>
            {
                ClearSelections(passengerLocomotiveSettings, stationStops);
                SetInteractions(passengerLocomotiveSettings, stationStops);
                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            });
        });

        passengerSettingsWindow.SetContentHeight(400 + (stationStops.Count - 3) * 20);
    }

    private void ClearSelections(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops)
    {
        stationStops.Select(ps => ps.identifier).ToList().ForEach((stationId) => passengerLocomotiveSettings.StationSettings[stationId] = new StationSetting());
    }

    private void SelectAllPickup(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops)
    {
        stationStops.Select(ps => ps.identifier).ToList().ForEach((stationId) => passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation = true);
    }

    private void SelectAllStopAt(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops)
    {
        List<string> stations = stationStops.Select(ps => ps.identifier).ToList();
        for (int i = 0; i < stations.Count; i++)
        {
            string stationId = stations[i];
            if (i == 0 || i == stations.Count - 1)
            {
                passengerLocomotiveSettings.StationSettings[stationId].TerminusStation = true;
            }
            passengerLocomotiveSettings.StationSettings[stationId].StopAtStation = true;
            passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation = true;
        }
    }

    private void SetInteractions(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops)
    {
        bool pickUpCountBigger = GetPickupStationsCount(passengerLocomotiveSettings, stationStops) > GetStopAtStationsCount(passengerLocomotiveSettings, stationStops);

        foreach (string stationId2 in stationStops.Select(ps => ps.identifier).ToList())
        {
            bool isAlarkaJct = stationId2 == "alarkajct";
            bool isAlarka = stationId2 == "alarka";
            bool isSylva = stationId2 == "sylva";
            bool isAndrews = stationId2 == "andrews";

            bool stopAtSelected = passengerLocomotiveSettings.StationSettings[stationId2].StopAtStation;

            passengerLocomotiveSettings.StationSettings[stationId2].PauseAtStation &= stopAtSelected;
            passengerLocomotiveSettings.StationSettings[stationId2].TerminusStation &= stopAtSelected;

            bool terminusSelected = passengerLocomotiveSettings.StationSettings[stationId2].TerminusStation;

            passengerLocomotiveSettings.StationSettings[stationId2].TransferStation &= stopAtSelected
                                                                                    && pickUpCountBigger
                                                                                    && (terminusSelected || isAlarkaJct)
                                                                                    && !isSylva
                                                                                    && !isAndrews;

            interactableStationMap[stationId2].Transfer = stopAtSelected
                                                            && pickUpCountBigger
                                                            && (terminusSelected || isAlarkaJct)
                                                            && !isSylva
                                                            && !isAndrews;

            interactableStationMap[stationId2].Mode = stopAtSelected && (terminusSelected || isAlarka);
            interactableStationMap[stationId2].Pause = stopAtSelected;
        }

        if (passengerLocomotiveSettings.StationSettings["alarkajct"].TransferStation)
        {
            passengerLocomotiveSettings.StationSettings["alarka"].TransferStation = false;
            interactableStationMap["alarka"].Transfer = false;
        }

        if (passengerLocomotiveSettings.StationSettings["alarka"].TransferStation)
        {
            passengerLocomotiveSettings.StationSettings["alarkajct"].TransferStation = false;
            interactableStationMap["alarkajct"].Transfer = false;
        }

        List<string> terminusStations = passengerLocomotiveSettings.GetTerminusStations();
        bool twoTerminusStations = terminusStations.Count == 2;
        List<string> stops = stationStops.Select(ps => ps.identifier).ToList();

        if (twoTerminusStations)
        {
            int eastIndex = stops.IndexOf(terminusStations[0]);
            int westIndex = stops.IndexOf(terminusStations[1]);

            for (int i = 0; i < stops.Count; i++)
            {
                string stationId = stops[i];
                if (i != eastIndex && i != westIndex)
                {
                    interactableStationMap[stationId].Terminus = false;
                }
                else
                {
                    interactableStationMap[stationId].Terminus = true;
                }
            }
        }
        else
        {
            foreach (string stationId in stops)
            {
                bool stopAtSelected = passengerLocomotiveSettings.StationSettings[stationId].StopAtStation;
                bool terminusSelected = passengerLocomotiveSettings.StationSettings[stationId].TerminusStation;
                interactableStationMap[stationId].Terminus = terminusSelected || stopAtSelected;
            }
        }
    }

    private TooltipInfo getTerminusTooltip(PassengerLocomotiveSettings passengerLocomotiveSettings, string stationName, Toggle TerminusToggle)
    {
        string tooltip = "";
        if (TerminusToggle.interactable)
        {
            tooltip = $"Toggle whether {stationName} should be a terminus station.";
        }
        else if (passengerLocomotiveSettings.StationSettings.Where(x => x.Value.TerminusStation).Count() == 2)
        {
            tooltip = $"Disabled due to 2 Terminus stations being selected.";
        }
        else
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }

        return new TooltipInfo("Terminus Station Toggle", tooltip);
    }

    private TooltipInfo getPauseTooltip(PassengerLocomotiveSettings passengerLocomotiveSettings, string stationName, Toggle PauseToggle)
    {
        string tooltip = "";
        if (PauseToggle.interactable)
        {
            tooltip = $"Toggle whether to pause at {stationName}.";
        }
        else
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }

        return new TooltipInfo("Pause at Station Toggle", tooltip);
    }

    private TooltipInfo getTransferTooltip(PassengerLocomotiveSettings passengerLocomotiveSettings, string stationName, string stationId, Toggle TransferToggle)
    {
        string tooltip = "";

        if (TransferToggle.interactable)
        {
            tooltip = $"Toggle whether {stationName} is a transfer station.";
        }
        else if (stationId == "sylva" || stationId == "andrews")
        {
            tooltip = $"Disabled due to {stationName} not capable of being a transfer station due to end of map.";
        }
        else if (!passengerLocomotiveSettings.StationSettings[stationId].StopAtStation)
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }
        else if (passengerLocomotiveSettings.GetPickupStations().Count <= passengerLocomotiveSettings.GetStopAtStations().Count)
        {
            tooltip = "Disabled due to there not being more PickUp stations than StopAt stations.";
        }
        else if (stationId != "alarkajct" && !passengerLocomotiveSettings.StationSettings[stationId].TerminusStation)
        {
            tooltip = "Disabled due to the station not being a Terminus station.";
        }
        else if (stationId == "alarkajct" && passengerLocomotiveSettings.StationSettings["alarka"].TransferStation)
        {
            tooltip = "Disabled due to Alarka being a Transfer Station. Only 1 of each can be selected as a Transfer Station.";
        }
        else if (stationId == "alarka" && passengerLocomotiveSettings.StationSettings["alarkajct"].TransferStation)
        {
            tooltip = "Disabled due to Alarka Jct being a Transfer Station. Only 1 of each can be selected as a Transfer Station.";
        }
        else
        {
            tooltip = "Disabled.";
        }

        return new TooltipInfo("Transfer Station Toggle", tooltip);
    }

    private TooltipInfo getModeTooltip(PassengerLocomotiveSettings passengerLocomotiveSettings, string stationName, string stationId, TMP_Dropdown ModeDropdown)
    {
        string tooltip = "";
        if (ModeDropdown.interactable)
        {
            tooltip = $"Choose whether the train should be Point to Point or Loop mode at {stationName}.";
        }
        else if (stationId != "alarka" && !passengerLocomotiveSettings.StationSettings[stationId].TerminusStation)
        {
            tooltip = $"Disabled due to Terminus Station for {stationName} not being selected.";
        }
        else if (stationId == "alarka")
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }
        else
        {
            tooltip = "Disabled.";
        }

        return new TooltipInfo("Station Mode Selector", tooltip);
    }

    private void BuildStationSettingsRow(UIPanelBuilder builder, PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops, List<Car> coaches, string stationId, string stationName)
    {
        PassengerMode[] passengerModes = ((PassengerMode[])Enum.GetValues(typeof(PassengerMode)));
        List<string> passengerModList = passengerModes.Select(s => s.ToString()).ToList();
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddLabel(stationName, delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).Width(175f);

            hBuilder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].StopAtStation, delegate (bool on)
            {
                logger.Information("StopAt for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].StopAtStation = on;

                if (on)
                {
                    passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation = true;
                }

                if (interactableStationMap[stationId] != null)
                {
                    SetInteractions(passengerLocomotiveSettings, stationStops);
                }
                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            }).Tooltip("StopAt Station Toggle", $"Toggle whether {stationName} should be stopped at by the train")
            .Width(70f);

            RectTransform terminusToggle = hBuilder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].TerminusStation, delegate (bool on)
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
                    if (interactableStationMap[stationId] != null)
                    {
                        SetInteractions(passengerLocomotiveSettings, stationStops);
                    }
                }
            }).Width(85f);

            terminusToggle.Tooltip(() => getTerminusTooltip(passengerLocomotiveSettings, stationName, terminusToggle.GetComponentInChildren<Toggle>()));

            hBuilder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation, delegate (bool on)
            {
                logger.Information("Pickup Passengers for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].PickupPassengersForStation = on;

                if (!on)
                {
                    passengerLocomotiveSettings.StationSettings[stationId].StopAtStation = false;
                }

                if (interactableStationMap[stationId] != null)
                {
                    SetInteractions(passengerLocomotiveSettings, stationStops);
                }

                SelectStationOnCoaches(passengerLocomotiveSettings, stationStops, coaches);
            }).Tooltip("PickUp Station Toggle", $"Toggle whether passengers for {stationName} should be picked up at the stations toggled in 'Stop At'")
            .Width(70f);

            RectTransform pauseAtStationToggle = hBuilder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].PauseAtStation, delegate (bool on)
            {
                logger.Information("Pause for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].PauseAtStation = on;
            })
            .Width(60f);

            pauseAtStationToggle.Tooltip(() => getPauseTooltip(passengerLocomotiveSettings, stationName, pauseAtStationToggle.GetComponentInChildren<Toggle>()));

            RectTransform transferStationToggle = hBuilder.AddToggle(() => passengerLocomotiveSettings.StationSettings[stationId].TransferStation, delegate (bool on)
            {
                logger.Information("Transfer for {0} set to {1}", stationId, on);
                passengerLocomotiveSettings.StationSettings[stationId].TransferStation = on;
                SetInteractions(passengerLocomotiveSettings, stationStops);
            })
            .Width(85f);

            transferStationToggle.Tooltip(() => getTransferTooltip(passengerLocomotiveSettings, stationName, stationId, transferStationToggle.GetComponentInChildren<Toggle>()));

            RectTransform passengerModeDropDown = hBuilder.AddDropdown(passengerModList, ((int)passengerLocomotiveSettings.StationSettings[stationId].PassengerMode), delegate (int index)
            {
                logger.Information("Passenger Mode for {0} set to {1}", stationId, passengerModes[index].ToString());
                passengerLocomotiveSettings.StationSettings[stationId].PassengerMode = passengerModes[index];
            }).Width(125f).Height(20f);

            passengerModeDropDown.Tooltip(() => getModeTooltip(passengerLocomotiveSettings, stationName, stationId, passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>()));

            this.interactableStationMap.Add(stationId, new Interactable(terminusToggle.GetComponentInChildren<Toggle>(), pauseAtStationToggle.GetComponentInChildren<Toggle>(), transferStationToggle.GetComponentInChildren<Toggle>(), passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>()));
        });
    }

    private int GetPickupStationsCount(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops)
    {
        return passengerLocomotiveSettings.StationSettings
        .Where(kv => stationStops
                    .Select(stp => stp.identifier).Contains(kv.Key)
                    && kv.Value.PickupPassengersForStation == true)
        .Select(stp => stp.Key)
        .ToList().Count;
    }

    private int GetStopAtStationsCount(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops)
    {
        return passengerLocomotiveSettings.StationSettings
        .Where(kv => stationStops
                    .Select(stp => stp.identifier).Contains(kv.Key)
                    && kv.Value.StopAtStation == true)
        .Select(stp => stp.Key)
        .ToList().Count;
    }
    private void SelectStationOnCoaches(PassengerLocomotiveSettings passengerLocomotiveSettings, List<PassengerStop> stationStops, List<Car> coaches)
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
            }).Tooltip("Disable Passenger Helper Toggle", $"Toggle whether PassengerHelper should be disabled or not")
            .Width(25f);
            builder.AddLabel("Disable", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.PauseForDiesel, delegate (bool on)
            {
                logger.Information("Pause for Diesel set to {0}", on);
                passengerLocomotiveSettings.PauseForDiesel = on;
            }).Tooltip("Pause for Low Diesel Toggle", $"Toggle whether the AI should pause for low diesel")
            .Width(25f);
            builder.AddLabel("Pause for low Diesel", delegate (TMP_Text text)
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
            .Tooltip("Diesel Level Percentage", "Set the percentage of diesel remaining that should trigger a pause for low diesel")
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
            builder.AddToggle(() => passengerLocomotiveSettings.PauseForCoal, delegate (bool on)
            {
                logger.Information("Pause for Coal set to {0}", on);
                passengerLocomotiveSettings.PauseForCoal = on;
            }).Tooltip("Pause for Low Coal Toggle", $"Toggle whether the AI should pause for low coal")
            .Width(25f);
            builder.AddLabel("Pause for low Coal", delegate (TMP_Text text)
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
            .Tooltip("Coal Level Percentage", "Set the percentage of coal remaining that should trigger a pause for low coal")
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
                builder.AddToggle(() => passengerLocomotiveSettings.PauseForWater, delegate (bool on)
                {
                    logger.Information("Pause for Water set to {0}", on);
                    passengerLocomotiveSettings.PauseForWater = on;
                }).Tooltip("Pause for Low Water Toggle", $"Toggle whether the AI should pause for low water")
                .Width(25f);
                builder.AddLabel("Pause for low Water", delegate (TMP_Text text)
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
            .Tooltip("Water Level Percentage", "Set the percentage of water remaining that should trigger a pause for low water")
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
            builder.AddToggle(() => passengerLocomotiveSettings.PauseAtNextStation, delegate (bool on)
            {
                logger.Information("Pause at next station set to {0}", on);
                passengerLocomotiveSettings.PauseAtNextStation = on;
            }).Tooltip("Pause at Next Station Toggle", $"Toggle whether the AI should pause at the next station")
            .Width(25f);
            builder.AddLabel("Pause At Next Station", delegate (TMP_Text text)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }).FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddToggle(() => passengerLocomotiveSettings.PauseAtTerminusStation, delegate (bool on)
                {
                    logger.Information("Pause at terminus station set to {0}", on);
                    passengerLocomotiveSettings.PauseAtTerminusStation = on;
                }).Tooltip("Pause At Terminus Station Toggle", $"Toggle whether the AI should pause at the terminus station(s)")
                .Width(25f);
                builder.AddLabel("Pause At Terminus Station", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).FlexibleWidth(1f);
            });
        builder.HStack(delegate (UIPanelBuilder builder)
        {
            builder.AddToggle(() => passengerLocomotiveSettings.PreventLoadWhenPausedAtStation, delegate (bool on)
            {
                logger.Information("Prevent loading when paused at station set to {0}", on);
                passengerLocomotiveSettings.PreventLoadWhenPausedAtStation = on;
            }).Tooltip("Prevent loading of passengers when paused Toggle", $"Toggle whether the AI should not load passengers if the train is paused at a station")
            .Width(25f);
            builder.AddLabel("Prevent Loading of Passengers When Paused", delegate (TMP_Text text)
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
            }).Tooltip("Wait for Full Passengers at Terminus Station Toggle", $"Toggle whether the AI should wait for a full passenger load at the terminus station before continuing on")
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
            builder.AddLabel("Reminder: Going towards Sylva is East and going towards Andrews is West");
            builder.Spacer().FlexibleWidth(1f);
        });

        builder.AddExpandingVerticalSpacer();
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

