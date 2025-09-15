namespace PassengerHelperPlugin.Support;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Managers;
using Model;
using Model.Definition;
using Model.Ops;
using Railloader;
using RollingStock;
using Serilog;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.UI;
using Support.GameObjects;
using Helpers;
using Support;
using static global::PassengerHelperPlugin.Support.PassengerLocomotiveSettings;

public class PassengerSettingsWindow
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(PassengerSettingsWindow));
    private IUIHelper uIHelper;
    private List<PassengerStop> stationStops;

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
        internal Toggle _terminusToggle;
        internal Toggle TerminusToggle
        {
            get { return _terminusToggle; }
            set
            {
                _terminusToggle = value;
                _terminusToggle.interactable = _terminus;
            }
        }

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
        internal Toggle _pauseToggle;
        internal Toggle PauseToggle
        {
            get { return _pauseToggle; }
            set
            {
                _pauseToggle = value;
                _pauseToggle.interactable = _pause;
            }
        }

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

        internal Toggle _transferToggle;
        internal Toggle TransferToggle
        {
            get { return _transferToggle; }
            set
            {
                _transferToggle = value;
                _transferToggle.interactable = _transfer;
            }
        }

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

        internal TMP_Dropdown _modeDropdown;
        internal TMP_Dropdown ModeDropdown
        {
            get { return _modeDropdown; }
            set
            {
                _modeDropdown = value;
                _modeDropdown.interactable = _mode;
            }
        }

        public Interactable(Toggle TerminusToggle, Toggle PauseToggle, Toggle TransferToggle, TMP_Dropdown ModeDropdown)
        {
            this.TerminusToggle = TerminusToggle;
            this.PauseToggle = PauseToggle;
            this.TransferToggle = TransferToggle;
            this.ModeDropdown = ModeDropdown;
        }
    }

    internal Dictionary<string, Interactable> interactableStationMap = new();
    internal SettingsManager settingsManager;

    internal PassengerSettingsWindow(IUIHelper uIHelper, List<PassengerStop> stationStops, SettingsManager settingsManager)
    {
        this.uIHelper = uIHelper;
        this.stationStops = stationStops;
        this.settingsManager = settingsManager;
    }

    internal void PopulateAndShowSettingsWindow(Window passengerSettingsWindow, PassengerLocomotive passengerLocomotive)
    {
        uIHelper.PopulateWindow(passengerSettingsWindow, (Action<UIPanelBuilder>)delegate (UIPanelBuilder builder)
        {
            logger.Information("Populating passenger helper settings for {0}", passengerLocomotive._locomotive.DisplayName);

            builder.VStack(delegate (UIPanelBuilder builder)
            {
                PopulateStationSettings(passengerSettingsWindow, builder, passengerLocomotive);
                builder.AddExpandingVerticalSpacer();
            });

            builder.Spacer();
            builder.VStack(delegate (UIPanelBuilder builder)
            {
                PopulateSettings(builder, passengerLocomotive);
                builder.AddExpandingVerticalSpacer();
            });
            AddSaveButton(builder, passengerSettingsWindow);
        });

        passengerSettingsWindow.ShowWindow();
    }

    private void PopulateStationSettings(Window passengerSettingsWindow, UIPanelBuilder builder, PassengerLocomotive pl)
    {
        builder.AddSection("Station Settings:");

        logger.Debug("Filtering stations to only unlocked ones");

        List<Car> coaches = pl._locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();

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
            BuildStationSettingsRow(builder, pl, stationStops, coaches, stationId, stationName);
        });

        SetInteractions(settingsManager.GetSettings(pl), stationStops);

        builder.Spacer().Height(5f);
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddButton("StopAt All Stations", () =>
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                logger.Information("Pls is: {0}", pls.ToString());

                ClearSelections(pls, stationStops);
                SelectAllStopAt(pls, stationStops);
                SetInteractions(pls, stationStops);

                logger.Information("Pls is now: {0}", pls.ToString());

                settingsManager.SaveSettings(pl, pls);

                SelectStationOnCoaches(pls.StationSettings, stationStops, coaches);
            });
            hBuilder.AddButton("PickUp All Stations", () =>
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                SelectAllPickup(pls, stationStops);
                SetInteractions(pls, stationStops);

                settingsManager.SaveSettings(pl, pls);
            });
            IConfigurableElement button = hBuilder.AddButton("Alarka Branch", () =>
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                ClearSelections(pls, stationStops);
                StationSetting alarkaJctStationSettings = new();
                StationSetting alarkaStationSettings = new();
                StationSetting cochranStationSettings = new();

                alarkaJctStationSettings.TerminusStation = true;
                alarkaJctStationSettings.TransferStation = true;

                alarkaStationSettings.TerminusStation = true;

                alarkaJctStationSettings.StopAtStation = true;
                cochranStationSettings.StopAtStation = true;
                alarkaStationSettings.StopAtStation = true;

                pls.StationSettings["alarkajct"] = alarkaJctStationSettings;
                pls.StationSettings["alarka"] = alarkaStationSettings;
                pls.StationSettings["cochran"] = cochranStationSettings;

                SelectAllPickup(pls, stationStops);
                SetInteractions(pls, stationStops);

                settingsManager.SaveSettings(pl, pls);

                SelectStationOnCoaches(pls.StationSettings, stationStops, coaches);
            });

            button.RectTransform.GetComponentInChildren<Button>().interactable = stationStops.Select(ps => ps.identifier).Contains("alarka");

            hBuilder.AddButton("Clear Selections", () =>
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                ClearSelections(pls, stationStops);
                SetInteractions(pls, stationStops);

                settingsManager.SaveSettings(pl, pls);

                SelectStationOnCoaches(pls.StationSettings, stationStops, coaches);
            });
        });

        passengerSettingsWindow.SetContentHeight(400 + (stationStops.Count - 3) * 20);
    }

    private void ClearSelections(PassengerLocomotiveSettings pls, List<PassengerStop> stationStops)
    {
        stationStops.Select(ps => ps.identifier).ToList().ForEach((stationId) =>
        {
            pls.StationSettings[stationId] = new();

        });
    }

    private void SelectAllPickup(PassengerLocomotiveSettings pls, List<PassengerStop> stationStops)
    {
        stationStops.Select(ps => ps.identifier).ToList().ForEach((stationId) =>
        {
            pls.StationSettings[stationId].PickupPassengersForStation = true;
        });
    }

    private void SelectAllStopAt(PassengerLocomotiveSettings pls, List<PassengerStop> stationStops)
    {
        List<string> stations = stationStops.Select(ps => ps.identifier).ToList();
        for (int i = 0; i < stations.Count; i++)
        {
            string stationId = stations[i];
            StationSetting stationSetting = new();

            if (i == 0 || i == stations.Count - 1)
            {
                stationSetting.TerminusStation = true;
            }

            stationSetting.StopAtStation = true;
            stationSetting.PickupPassengersForStation = true;

            pls.StationSettings[stationId] = stationSetting;
        }
    }

    private void SetInteractions(PassengerLocomotiveSettings pls, List<PassengerStop> stationStops)
    {
        Dictionary<string, StationSetting> allStationSettings = pls.StationSettings;

        bool pickUpCountBigger = GetPickupStationsCount(allStationSettings, stationStops) > GetStopAtStationsCount(allStationSettings, stationStops);

        foreach (string stationId in stationStops.Select(ps => ps.identifier).ToList())
        {
            bool isAlarkaJct = stationId == "alarkajct";
            bool isAlarka = stationId == "alarka";
            bool isSylva = stationId == "sylva";
            bool isAndrews = stationId == "andrews";
            StationSetting stationSettings = allStationSettings[stationId];

            logger.Debug("pre interaction station settings for {0} are: {1}", stationId, stationSettings);

            bool stopAtSelected = stationSettings.StopAtStation;

            bool pauseSetting = stationSettings.PauseAtStation && stopAtSelected;
            stationSettings.PauseAtStation = pauseSetting;

            bool terminusSetting = stationSettings.TerminusStation && stopAtSelected;
            stationSettings.TerminusStation = terminusSetting;

            bool terminusSelected = terminusSetting;

            bool transferSetting = stationSettings.TransferStation && stopAtSelected
                                                                    && pickUpCountBigger
                                                                    && (terminusSelected || isAlarkaJct)
                                                                    && !isSylva
                                                                    && !isAndrews;

            stationSettings.TransferStation = transferSetting;

            if (interactableStationMap.ContainsKey(stationId))
            {
                interactableStationMap[stationId].Transfer = stopAtSelected
                                                                && pickUpCountBigger
                                                                && (terminusSelected || isAlarkaJct)
                                                                && !isSylva
                                                                && !isAndrews;
                interactableStationMap[stationId].Mode = stopAtSelected && (terminusSelected || isAlarka);
                interactableStationMap[stationId].Pause = stopAtSelected;
            }

            logger.Debug("post interaction station settings for {0} are: {1}", stationId, stationSettings);
        }

        StationSetting alarkJctSettings = allStationSettings["alarkajct"];
        StationSetting alarkaSettings = allStationSettings["alarka"];

        if (alarkJctSettings.TransferStation)
        {
            if (stationStops.Select(ps => ps.identifier).Contains("alarka"))
            {
                alarkaSettings.TransferStation = false;
                // settings["alarka"] = Value.Dictionary(alarkaSettings);
                interactableStationMap["alarka"].Transfer = false;
            }
        }

        if (alarkaSettings.TransferStation)
        {
            alarkJctSettings.TransferStation = false;
            // settings["alarkajct"] = Value.Dictionary(alarkJctSettings);
            interactableStationMap["alarkajct"].Transfer = false;
        }

        List<string> terminusStations = allStationSettings.Where(kvp => stationStops.Select(ps => ps.identifier).Contains(kvp.Key) && kvp.Value.TerminusStation).Select(kvp => kvp.Key).ToList();
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
                StationSetting stationSettings = allStationSettings[stationId];

                bool stopAtSelected = stationSettings.StopAtStation;
                bool terminusSelected = stationSettings.TerminusStation;
                interactableStationMap[stationId].Terminus = terminusSelected || stopAtSelected;
            }
        }
    }

    private TooltipInfo getTerminusTooltip(PassengerLocomotiveSettings pls, string stationName, Toggle TerminusToggle)
    {
        string tooltip = "";
        Dictionary<string, StationSetting> allStationSettings = pls.StationSettings;
        int terminusCount = allStationSettings.Where(kvp => kvp.Value.PickupPassengersForStation).Count();

        if (TerminusToggle.interactable)
        {
            tooltip = $"Toggle whether {stationName} should be a terminus station.";
        }
        else if (terminusCount == 2)
        {
            tooltip = $"Disabled due to 2 Terminus stations being selected.";
        }
        else
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }

        return new TooltipInfo("Terminus Station Toggle", tooltip);
    }

    private TooltipInfo getPauseTooltip(string stationName, Toggle PauseToggle)
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

    private TooltipInfo getTransferTooltip(PassengerLocomotiveSettings pls, string stationName, string stationId, Toggle TransferToggle)
    {
        string tooltip = "";
        Dictionary<string, StationSetting> allStationSettings = pls.StationSettings;
        int pickUpStationCount = allStationSettings.Where(kvp => kvp.Value.PickupPassengersForStation).Count();
        int stopAtStationCount = allStationSettings.Where(kvp => kvp.Value.StopAtStation).Count();

        if (TransferToggle.interactable)
        {
            tooltip = $"Toggle whether {stationName} is a transfer station.";
        }
        else if (stationId == "sylva" || stationId == "andrews")
        {
            tooltip = $"Disabled due to {stationName} not capable of being a transfer station due to end of map.";
        }
        else if (!allStationSettings[stationId].StopAtStation)
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }
        else if (pickUpStationCount <= stopAtStationCount)
        {
            tooltip = "Disabled due to there not being more PickUp stations than StopAt stations.";
        }
        else if (stationId != "alarkajct" && !allStationSettings[stationId].TerminusStation)
        {
            tooltip = "Disabled due to the station not being a Terminus station.";
        }
        else if (stationId == "alarkajct" && allStationSettings["alarka"].TransferStation)
        {
            tooltip = "Disabled due to Alarka being a Transfer Station. Only 1 of each can be selected as a Transfer Station.";
        }
        else if (stationId == "alarka" && allStationSettings["alarkajct"].TransferStation)
        {
            tooltip = "Disabled due to Alarka Jct being a Transfer Station. Only 1 of each can be selected as a Transfer Station.";
        }
        else
        {
            tooltip = "Disabled.";
        }

        return new TooltipInfo("Transfer Station Toggle", tooltip);
    }

    private TooltipInfo getModeTooltip(bool IsTerminusStation, string stationName, string stationId, TMP_Dropdown ModeDropdown)
    {
        string tooltip = "";

        if (ModeDropdown.interactable)
        {
            tooltip = $"Choose whether the train should be Point to Point or Loop mode at {stationName}.";
        }
        else if (stationId != "alarka" && !IsTerminusStation)
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

    private void BuildStationSettingsRow(UIPanelBuilder builder, PassengerLocomotive pl, List<PassengerStop> stationStops, List<Car> coaches, string stationId, string stationName)
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

            hBuilder.AddToggle(() => settingsManager.GetStationSetting(pl, stationId).StopAtStation, delegate (bool on)
            {
                logger.Debug("StopAt for {0} set to {1}", stationId, on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.StationSettings[stationId].StopAtStation = on;

                if (on)
                {
                    pls.StationSettings[stationId].PickupPassengersForStation = on;
                }

                if (interactableStationMap[stationId] != null)
                {
                    SetInteractions(pls, stationStops);
                }

                settingsManager.SaveSettings(pl, pls);

                SelectStationOnCoaches(pls.StationSettings, stationStops, coaches);
            }).Tooltip("StopAt Station Toggle", $"Toggle whether {stationName} should be stopped at by the train")
            .Width(70f);

            RectTransform terminusToggle = hBuilder.AddToggle(() => settingsManager.GetStationSetting(pl, stationId).TerminusStation, delegate (bool on)
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                int numTerminusStations = pls.StationSettings.Where(s => s.Value.TerminusStation).Count();

                logger.Debug("There are currently {0} terminus stations set", numTerminusStations);
                if (numTerminusStations >= 2 && on == true)
                {
                    logger.Debug("You can only select 2 terminus stations. Please unselect one before selecting another");
                }
                else
                {
                    logger.Debug("IsTerminusStation for {0} set to {1}", stationId, on);
                    pls.StationSettings[stationId].TerminusStation = on;

                    if (interactableStationMap[stationId] != null)
                    {
                        SetInteractions(pls, stationStops);
                    }

                    settingsManager.SaveSettings(pl, pls);
                }
            }).Width(85f);

            terminusToggle.Tooltip(() => getTerminusTooltip(settingsManager.GetSettings(pl), stationName, terminusToggle.GetComponentInChildren<Toggle>()));

            hBuilder.AddToggle(() => settingsManager.GetStationSetting(pl, stationId).PickupPassengersForStation, delegate (bool on)
            {
                logger.Debug("Pickup Passengers for {0} set to {1}", stationId, on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                pls.StationSettings[stationId].PickupPassengersForStation = on;

                if (!on)
                {
                    pls.StationSettings[stationId].StopAtStation = false;
                }

                if (interactableStationMap[stationId] != null)
                {
                    SetInteractions(pls, stationStops);
                }

                settingsManager.SaveSettings(pl, pls);

                SelectStationOnCoaches(pls.StationSettings, stationStops, coaches);
            }).Tooltip("PickUp Station Toggle", $"Toggle whether passengers for {stationName} should be picked up at the stations toggled in 'Stop At'")
            .Width(70f);

            RectTransform pauseAtStationToggle = hBuilder.AddToggle(() => settingsManager.GetStationSetting(pl, stationId).PauseAtStation, delegate (bool on)
            {
                logger.Debug("Pause for {0} set to {1}", stationId, on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.StationSettings[stationId].PauseAtStation = on;
                settingsManager.SaveSettings(pl, pls);
            })
            .Width(60f);

            pauseAtStationToggle.Tooltip(() => getPauseTooltip(stationName, pauseAtStationToggle.GetComponentInChildren<Toggle>()));

            RectTransform transferStationToggle = hBuilder.AddToggle(() => settingsManager.GetStationSetting(pl, stationId).TransferStation, delegate (bool on)
            {
                logger.Debug("Transfer for {0} set to {1}", stationId, on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.StationSettings[stationId].TransferStation = on;

                if (interactableStationMap[stationId] != null)
                {
                    SetInteractions(pls, stationStops);
                }

                settingsManager.SaveSettings(pl, pls);
            })
            .Width(85f);

            transferStationToggle.Tooltip(() => getTransferTooltip(settingsManager.GetSettings(pl), stationName, stationId, transferStationToggle.GetComponentInChildren<Toggle>()));

            RectTransform passengerModeDropDown = hBuilder.AddDropdown(passengerModList, (int)settingsManager.GetStationSetting(pl, stationId).PassengerMode, delegate (int index)
            {
                logger.Debug("Passenger Mode for {0} set to {1}", stationId, passengerModes[index].ToString());
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.StationSettings[stationId].PassengerMode = (PassengerMode)index;
                settingsManager.SaveSettings(pl, pls);
            }).Width(125f).Height(20f);

            passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>().gameObject.AddComponent<DropDownUpdater>().Configure(() => (int)settingsManager.GetStationSetting(pl, stationId).PassengerMode);
            passengerModeDropDown.Tooltip(() => getModeTooltip(settingsManager.GetStationSetting(pl, stationId).TerminusStation, stationName, stationId, passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>()));

            interactableStationMap[stationId] = new Interactable(terminusToggle.GetComponentInChildren<Toggle>(), pauseAtStationToggle.GetComponentInChildren<Toggle>(), transferStationToggle.GetComponentInChildren<Toggle>(), passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>());
        });
    }

    private int GetPickupStationsCount(Dictionary<string, StationSetting> stationSettings, List<PassengerStop> stationStops)
    {
        return stationSettings
        .Where(kv => stationStops
                    .Select(stp => stp.identifier).Contains(kv.Key)
                    && kv.Value.PickupPassengersForStation)
        .Select(stp => stp.Key)
        .ToList().Count;
    }

    private int GetStopAtStationsCount(Dictionary<string, StationSetting> stationSettings, List<PassengerStop> stationStops)
    {
        return stationSettings
        .Where(kv => stationStops
                    .Select(stp => stp.identifier).Contains(kv.Key)
                    && kv.Value.StopAtStation)
        .Select(stp => stp.Key)
        .ToList().Count;
    }
    private void SelectStationOnCoaches(Dictionary<string, StationSetting> stationSettings, List<PassengerStop> stationStops, List<Car> coaches)
    {
        List<string> passengerStopsToSelect = stationSettings
        .Where(kv =>
                stationStops
                .Select(stp => stp.identifier)
                .Contains(kv.Key) && kv.Value.PickupPassengersForStation)
                .Select(stp => stp.Key)
                .ToList();

        logger.Information("Applying selected stations {0} to all coupled coaches", passengerStopsToSelect);

        foreach (Car coach in coaches)
        {
            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, passengerStopsToSelect));
        }
    }

    private void PopulateSettings(UIPanelBuilder builder, PassengerLocomotive pl)
    {
        builder.AddSection("Settings:");
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => settingsManager.GetSettings(pl).Disable, delegate (bool on)
            {
                logger.Information("Disable set to {0}", on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.Disable = on;

                settingsManager.SaveSettings(pl, pls);
            }).Tooltip("Disable Passenger Helper Toggle", $"Toggle whether PassengerHelper should be disabled or not")
            .Width(25f);
            hBuilder.AddLabel("Disable").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => settingsManager.GetSettings(pl).PauseForDiesel, delegate (bool on)
            {
                logger.Debug("Pause for Diesel set to {0}", on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.PauseForDiesel = on;

                settingsManager.SaveSettings(pl, pls);
            }).Tooltip("Pause for Low Diesel Toggle", $"Toggle whether the AI should pause for low diesel")
            .Width(25f);
            hBuilder.AddLabel("Pause for low Diesel").Width(200f);
            hBuilder.AddLabel(() => "Pause at: " + (settingsManager.GetSettings(pl).DieselLevel * 100).ToString() + "%", UIPanelBuilder.Frequency.Fast);
            hBuilder.Spacer().Width(2f);
            hBuilder.AddLabel("Set to: ");
            hBuilder.AddInputField((settingsManager.GetSettings(pl).DieselLevel * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Debug("Entered a Diesel Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Debug("Entered a Diesel Level: {0}%", value);
                    PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                    pls.DieselLevel = value / 100;
                    settingsManager.SaveSettings(pl, pls);
                }
            }, null, 2)
            .Tooltip("Diesel Level Percentage", "Set the percentage of diesel remaining that should trigger a pause for low diesel")
            .Width(50f)
            .Height(20f);
            hBuilder.AddLabel("%").FlexibleWidth(1f);
            hBuilder.Spacer();
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => settingsManager.GetSettings(pl).PauseForCoal, delegate (bool on)
            {
                logger.Debug("Pause for Coal set to {0}", on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                pls.PauseForCoal = on;

                settingsManager.SaveSettings(pl, pls);
            }).Tooltip("Pause for Low Coal Toggle", $"Toggle whether the AI should pause for low coal")
                .Width(25f);
            hBuilder.AddLabel("Pause for low Coal").Width(200f);
            hBuilder.AddLabel(() => "Pause at: " + (settingsManager.GetSettings(pl).CoalLevel * 100).ToString() + "%", UIPanelBuilder.Frequency.Fast);
            hBuilder.Spacer().Width(2f);
            hBuilder.AddLabel("Set to: ");
            hBuilder.AddInputField((settingsManager.GetSettings(pl).CoalLevel * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Debug("Entered a Coal Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Debug("Entered a Coal Level: {0}%", value);
                    PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                    pls.CoalLevel = value / 100;
                    settingsManager.SaveSettings(pl, pls);
                }
            }, null, 2)
            .Tooltip("Coal Level Percentage", "Set the percentage of coal remaining that should trigger a pause for low coal")
            .Width(50f)
            .Height(20f);
            hBuilder.AddLabel("%").FlexibleWidth(1f);
            hBuilder.Spacer();
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
            {
                hBuilder.AddToggle(() => settingsManager.GetSettings(pl).PauseForWater, delegate (bool on)
                {
                    logger.Debug("Pause for Water set to {0}", on);
                    PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                    pls.PauseForWater = on;

                    settingsManager.SaveSettings(pl, pls);
                }).Tooltip("Pause for Low Water Toggle", $"Toggle whether the AI should pause for low water")
                .Width(25f);
                hBuilder.AddLabel("Pause for low Water", delegate (TMP_Text text)
                                {
                                    text.textWrappingMode = TextWrappingModes.NoWrap;
                                    text.overflowMode = TextOverflowModes.Ellipsis;
                                }).Width(200f);
                hBuilder.AddLabel(() => "Pause at: " + (settingsManager.GetSettings(pl).WaterLevel * 100).ToString() + "%", UIPanelBuilder.Frequency.Fast);
                hBuilder.Spacer().Width(2f);
                hBuilder.AddLabel("Set to: ");
                hBuilder.AddInputField((settingsManager.GetSettings(pl).WaterLevel * 100).ToString(), delegate (string val)
                {
                    if (float.TryParse(val, out float value))
                    {
                        if (value < 0 || value > 100)
                        {
                            logger.Debug("Entered a Water Level greater than 100 or lower than 0");
                            return;
                        }

                        logger.Debug("Entered a Water Level: {0}%", value);
                        PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                        pls.WaterLevel = value / 100;
                        settingsManager.SaveSettings(pl, pls);
                    }
                }, null, 2)
                .Tooltip("Water Level Percentage", "Set the percentage of water remaining that should trigger a pause for low water")
                .Width(50f)
                .Height(20f);
                hBuilder.AddLabel("%").FlexibleWidth(1f);
                hBuilder.Spacer();
            });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => settingsManager.GetSettings(pl).PauseAtNextStation, delegate (bool on)
            {
                logger.Debug("Pause at next station set to {0}", on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                pls.PauseAtNextStation = on;

                settingsManager.SaveSettings(pl, pls);
            }).Tooltip("Pause at Next Station Toggle", $"Toggle whether the AI should pause at the next station")
        .Width(25f);
            hBuilder.AddLabel("Pause At Next Station").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
            {
                hBuilder.AddToggle(() => settingsManager.GetSettings(pl).PauseAtTerminusStation, delegate (bool on)
                {
                    logger.Debug("Pause at terminus station set to {0}", on);
                    PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                    pls.PauseAtTerminusStation = on;

                    settingsManager.SaveSettings(pl, pls);
                }).Tooltip("Pause At Terminus Station Toggle", $"Toggle whether the AI should pause at the terminus station(s)")
            .Width(25f);
                hBuilder.AddLabel("Pause At Terminus Station").FlexibleWidth(1f);
            });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => settingsManager.GetSettings(pl).PreventLoadWhenPausedAtStation, delegate (bool on)
            {
                logger.Debug("Prevent loading when paused at station set to {0}", on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                pls.PreventLoadWhenPausedAtStation = on;

                settingsManager.SaveSettings(pl, pls);
            }).Tooltip("Prevent loading of passengers when paused Toggle", $"Toggle whether the AI should not load passengers if the train is paused at a station")
        .Width(25f);
            hBuilder.AddLabel("Prevent Loading of Passengers When Paused").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => settingsManager.GetSettings(pl).WaitForFullPassengersTerminusStation, delegate (bool on)
            {
                logger.Debug("Wait for full passengers at terminus station set to {0}", on);
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);

                pls.WaitForFullPassengersTerminusStation = on;

                settingsManager.SaveSettings(pl, pls);
            }).Tooltip("Wait for Full Passengers at Terminus Station Toggle", $"Toggle whether the AI should wait for a full passenger load at the terminus station before continuing on")
        .Width(25f);
            hBuilder.AddLabel("Wait For Full Load at Terminus Station").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            string tooltipLocked = "The direction setting is currently disabled, as it is being controlled by PassengerHelper. " +
                                            "If you would like to enable it, it will become adjustable again after manually issuing any " +
                                            "order to the engine, whether that be changing the AI mode, changing direction, or changing speed. ";

            string tooltipUnlocked = "This setting helps PassengerHelper, as without it, if you changed terminus station mid route " +
                                            "it would probably reverse direction when you wouldn't want that. " +
                                            "PassengerHelper can kind of figure this out on its own, but setting it will help it help you. " +
                                            "This is more of an edge case than anything else.";

            RectTransform dotSlider = hBuilder.AddSliderQuantized(() => (int)settingsManager.GetSettings(pl).DirectionOfTravel, () => settingsManager.GetSettings(pl).DirectionOfTravel.ToString(), delegate (float value)
                        {
                            int newValue = (int)value;

                            DirectionOfTravel newDirectionOfTravel = (DirectionOfTravel)newValue;
                            logger.Information("Set direction of travel to: {0}", newDirectionOfTravel.ToString());
                            PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);


                            pls.DirectionOfTravel = (DirectionOfTravel)newValue;
                            settingsManager.SaveSettings(pl, pls);
                        }, 1f, 0, 2).Width(150f);
            dotSlider.GetComponentInChildren<CarControlSlider>().interactable = !settingsManager.GetSettings(pl).DoTLocked;

            hBuilder.AddField("Direction of Travel", dotSlider).Tooltip("Direction of Travel", settingsManager.GetSettings(pl).DoTLocked ? tooltipLocked + tooltipUnlocked : tooltipUnlocked);
            hBuilder.Spacer().Width(5f);
            hBuilder.AddButton("?", () =>
            {
                Window dotHelpWindow = uIHelper.CreateWindow("Moloch.PH.settings.DOTHELP", 450, 200, Window.Position.Center);

                dotHelpWindow.Title = "Direction of Travel Help";

                uIHelper.PopulateWindow(dotHelpWindow, (Action<UIPanelBuilder>)delegate (UIPanelBuilder builder)
                {
                    builder.VStack(delegate (UIPanelBuilder builder)
                    {
                        builder.AddLabel("Direction of travel is a setting that Passenger Helper uses to determine the cardinal direction of the train. "
                        + "Because a train can be in forward or reverse, it is impossible to know which way the train is actually heading with the reverser setting alone. "
                        + "This allows passenger helper to select the right stations and become automated.");
                        builder.Spacer().Height(5f);
                        builder.AddLabel("For clarity, Going towards Sylva is East and going towards Andrews is West");
                    });
                });

                dotHelpWindow.ShowWindow();
            });
            hBuilder.Spacer().FlexibleWidth(1f);
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