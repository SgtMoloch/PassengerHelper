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
    private StationManager stationManager;
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

    internal PassengerSettingsWindow(IUIHelper uIHelper, StationManager stationManager)
    {
        this.uIHelper = uIHelper;
        this.stationManager = stationManager;
        this.stationStops = this.stationManager.GetPassengerStops();
    }

    internal void PopulateAndShowSettingsWindow(Window passengerSettingsWindow, PassengerLocomotive passengerLocomotive)
    {
        uIHelper.PopulateWindow(passengerSettingsWindow, (Action<UIPanelBuilder>)delegate (UIPanelBuilder builder)
        {
            logger.Information("Populating passenger helper settings for {0}", passengerLocomotive._locomotive.DisplayName);
            builder.AddObserver(passengerLocomotive._keyValueObject.Observe(passengerLocomotive.KeyValueIdentifier, delegate
            {
                SetInteractions(passengerLocomotive, passengerLocomotive.GetMutablePassengerSettings(), stationStops);
            }, callInitial: false));

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

    private void PopulateStationSettings(Window passengerSettingsWindow, UIPanelBuilder builder, PassengerLocomotive passengerLocomotive)
    {
        builder.AddSection("Station Settings:");

        logger.Debug("Filtering stations to only unlocked ones");


        List<Car> coaches = passengerLocomotive._locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach).ToList();

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
            BuildStationSettingsRow(builder, passengerLocomotive, stationStops, coaches, stationId, stationName);
        });

        Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
        SetInteractions(passengerLocomotive, settings, stationStops);

        builder.Spacer().Height(5f);
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddButton("StopAt All Stations", () =>
            {
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();

                settings = ClearSelections(passengerLocomotive, settings, stationStops);
                settings = SelectAllStopAt(passengerLocomotive, settings, stationStops);
                settings = SetInteractions(passengerLocomotive, settings, stationStops);
                passengerLocomotive.SaveSettings(settings);

                SelectStationOnCoaches(settings, stationStops, coaches);
            });
            hBuilder.AddButton("PickUp All Stations", () =>
            {
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();

                settings = SelectAllPickup(passengerLocomotive, settings, stationStops);
                settings = SetInteractions(passengerLocomotive, settings, stationStops);
                passengerLocomotive.SaveSettings(settings);
            });
            IConfigurableElement button = hBuilder.AddButton("Alarka Branch", () =>
            {
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                settings = ClearSelections(passengerLocomotive, settings, stationStops);
                Dictionary<string, Value> alarkaJctStationSettings = new(settings["alarkajct"].DictionaryValue);
                Dictionary<string, Value> alarkaStationSettings = new(settings["alarka"].DictionaryValue);
                Dictionary<string, Value> cochranStationSettings = new(settings["cochran"].DictionaryValue);

                alarkaJctStationSettings[StationSettingKeys.TerminusStation] = Value.Bool(true);
                alarkaJctStationSettings[StationSettingKeys.TransferStation] = Value.Bool(true);

                alarkaStationSettings[StationSettingKeys.TerminusStation] = Value.Bool(true);

                alarkaJctStationSettings[StationSettingKeys.StopAtStation] = Value.Bool(true);
                cochranStationSettings[StationSettingKeys.StopAtStation] = Value.Bool(true);
                alarkaStationSettings[StationSettingKeys.StopAtStation] = Value.Bool(true);

                settings["alarkajct"] = Value.Dictionary(alarkaJctStationSettings);
                settings["alarka"] = Value.Dictionary(alarkaStationSettings);
                settings["cochran"] = Value.Dictionary(cochranStationSettings);

                settings = SelectAllPickup(passengerLocomotive, settings, stationStops);
                settings = SetInteractions(passengerLocomotive, settings, stationStops);
                passengerLocomotive.SaveSettings(settings);

                SelectStationOnCoaches(settings, stationStops, coaches);
            });

            button.RectTransform.GetComponentInChildren<Button>().interactable = stationStops.Select(ps => ps.identifier).Contains("alarka");

            hBuilder.AddButton("Clear Selections", () =>
            {
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                settings = ClearSelections(passengerLocomotive, passengerLocomotive.GetMutablePassengerSettings(), stationStops);
                settings = SetInteractions(passengerLocomotive, settings, stationStops);
                passengerLocomotive.SaveSettings(settings);

                SelectStationOnCoaches(settings, stationStops, coaches);
            });
        });

        passengerSettingsWindow.SetContentHeight(400 + (stationStops.Count - 3) * 20);
    }

    private Dictionary<string, Value> ClearSelections(PassengerLocomotive passengerLocomotive, Dictionary<string, Value> settings, List<PassengerStop> stationStops)
    {
        stationStops.Select(ps => ps.identifier).ToList().ForEach((stationId) =>
        {
            settings[stationId] = Value.Dictionary(new StationSetting().PropertyValue().DictionaryValue);

        });

        return settings;
    }

    private Dictionary<string, Value> SelectAllPickup(PassengerLocomotive passengerLocomotive, Dictionary<string, Value> settings, List<PassengerStop> stationStops)
    {
        stationStops.Select(ps => ps.identifier).ToList().ForEach((stationId) =>
        {
            Dictionary<string, Value> stationSetting = new(settings[stationId].DictionaryValue);
            stationSetting[StationSettingKeys.PickupPassengersForStation] = Value.Bool(true);

            settings[stationId] = Value.Dictionary(stationSetting);
        });

        return settings;
    }

    private Dictionary<string, Value> SelectAllStopAt(PassengerLocomotive passengerLocomotive, Dictionary<string, Value> settings, List<PassengerStop> stationStops)
    {
        List<string> stations = stationStops.Select(ps => ps.identifier).ToList();
        for (int i = 0; i < stations.Count; i++)
        {
            string stationId = stations[i];
            Dictionary<string, Value> stationSetting = new(new StationSetting().PropertyValue().DictionaryValue);

            if (i == 0 || i == stations.Count - 1)
            {
                stationSetting[StationSettingKeys.TerminusStation] = Value.Bool(true);
            }
            stationSetting[StationSettingKeys.StopAtStation] = Value.Bool(true);
            stationSetting[StationSettingKeys.PickupPassengersForStation] = Value.Bool(true);

            settings[stationId] = Value.Dictionary(stationSetting);
        }

        return settings;
    }

    private Dictionary<string, Value> SetInteractions(PassengerLocomotive passengerLocomotive, Dictionary<string, Value> settings, List<PassengerStop> stationStops)
    {
        bool pickUpCountBigger = GetPickupStationsCount(settings, stationStops) > GetStopAtStationsCount(settings, stationStops);

        foreach (string stationId2 in stationStops.Select(ps => ps.identifier).ToList())
        {
            bool isAlarkaJct = stationId2 == "alarkajct";
            bool isAlarka = stationId2 == "alarka";
            bool isSylva = stationId2 == "sylva";
            bool isAndrews = stationId2 == "andrews";
            Dictionary<string, Value> stationSettings = new(settings[stationId2].DictionaryValue);
            logger.Debug("pre interaction station settings for {0} are: {1}", stationId2, stationSettings.Select(kv => kv.Key.ToString() + ": " + kv.Value.ToString()));

            bool stopAtSelected = stationSettings[StationSettingKeys.StopAtStation].BoolValue;

            bool pauseSetting = stationSettings[StationSettingKeys.PauseAtStation].BoolValue && stopAtSelected;
            stationSettings[StationSettingKeys.PauseAtStation] = Value.Bool(pauseSetting);

            //passengerLocomotiveSettings.StationSettings[stationId2].TerminusStation &= stopAtSelected;
            bool terminusSetting = stationSettings[StationSettingKeys.TerminusStation].BoolValue && stopAtSelected;
            stationSettings[StationSettingKeys.TerminusStation] = Value.Bool(terminusSetting);

            bool terminusSelected = terminusSetting;

            bool transferSetting = stationSettings[StationSettingKeys.TransferStation].BoolValue && stopAtSelected
                                                                                    && pickUpCountBigger
                                                                                    && (terminusSelected || isAlarkaJct)
                                                                                    && !isSylva
                                                                                    && !isAndrews;

            stationSettings[StationSettingKeys.TransferStation] = Value.Bool(transferSetting);

            if (interactableStationMap.ContainsKey(stationId2))
            {
                interactableStationMap[stationId2].Transfer = stopAtSelected
                                                                && pickUpCountBigger
                                                                && (terminusSelected || isAlarkaJct)
                                                                && !isSylva
                                                                && !isAndrews;
                interactableStationMap[stationId2].Mode = stopAtSelected && (terminusSelected || isAlarka);
                interactableStationMap[stationId2].Pause = stopAtSelected;
            }

            settings[stationId2] = Value.Dictionary(stationSettings);
            logger.Debug("post interaction station settings for {0} are: {1}", stationId2, stationSettings.Select(kv => kv.Key.ToString() + ": " + kv.Value.ToString()));
        }

        Dictionary<string, Value> alarkJctSettings = new(settings["alarkajct"].DictionaryValue);
        Dictionary<string, Value> alarkaSettings = new(settings["alarka"].DictionaryValue);

        if (alarkJctSettings[StationSettingKeys.TransferStation].BoolValue)
        {
            if (stationStops.Select(ps => ps.identifier).Contains("alarka"))
            {
                alarkaSettings[StationSettingKeys.TransferStation] = Value.Bool(false);
                settings["alarka"] = Value.Dictionary(alarkaSettings);
                interactableStationMap["alarka"].Transfer = false;
            }
        }

        if (alarkaSettings[StationSettingKeys.TransferStation].BoolValue)
        {
            alarkJctSettings[StationSettingKeys.TransferStation] = Value.Bool(false);
            settings["alarkajct"] = Value.Dictionary(alarkJctSettings);
            interactableStationMap["alarkajct"].Transfer = false;
        }

        List<string> terminusStations = settings.Where(kvp => stationStops.Select(ps => ps.identifier).Contains(kvp.Key) && kvp.Value[StationSettingKeys.TerminusStation].BoolValue == true).Select(kvp => kvp.Key).ToList();
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
                IReadOnlyDictionary<string, Value> stationSettings = settings[stationId].DictionaryValue;

                bool stopAtSelected = stationSettings[StationSettingKeys.StopAtStation].BoolValue;
                bool terminusSelected = stationSettings[StationSettingKeys.TerminusStation].BoolValue;
                interactableStationMap[stationId].Terminus = terminusSelected || stopAtSelected;
            }
        }

        return settings;
    }

    private TooltipInfo getTerminusTooltip(PassengerLocomotive passengerLocomotive, string stationName, Toggle TerminusToggle)
    {
        string tooltip = "";

        if (TerminusToggle.interactable)
        {
            tooltip = $"Toggle whether {stationName} should be a terminus station.";
        }
        else if (passengerLocomotive.GetPassengerSettings().Where(x => x.Value[StationSettingKeys.TerminusStation].BoolValue).Count() == 2)
        {
            tooltip = $"Disabled due to 2 Terminus stations being selected.";
        }
        else
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }

        return new TooltipInfo("Terminus Station Toggle", tooltip);
    }

    private TooltipInfo getPauseTooltip(PassengerLocomotive passengerLocomotive, string stationName, Toggle PauseToggle)
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

    private TooltipInfo getTransferTooltip(PassengerLocomotive passengerLocomotive, string stationName, string stationId, Toggle TransferToggle)
    {
        string tooltip = "";
        IReadOnlyDictionary<string, Value> stationSettings = passengerLocomotive.GetPassengerSettings();
        int pickUpStationCount = stationSettings.Where(kvp => kvp.Value[StationSettingKeys.PickupPassengersForStation].BoolValue).Count();
        int stopAtStationCount = stationSettings.Where(kvp => kvp.Value[StationSettingKeys.StopAtStation].BoolValue).Count();

        if (TransferToggle.interactable)
        {
            tooltip = $"Toggle whether {stationName} is a transfer station.";
        }
        else if (stationId == "sylva" || stationId == "andrews")
        {
            tooltip = $"Disabled due to {stationName} not capable of being a transfer station due to end of map.";
        }
        else if (!stationSettings[stationId][StationSettingKeys.StopAtStation].BoolValue)
        {
            tooltip = $"Disabled due to StopAt for {stationName} not being selected.";
        }
        else if (pickUpStationCount <= stopAtStationCount)
        {
            tooltip = "Disabled due to there not being more PickUp stations than StopAt stations.";
        }
        else if (stationId != "alarkajct" && !stationSettings[stationId][StationSettingKeys.TerminusStation].BoolValue)
        {
            tooltip = "Disabled due to the station not being a Terminus station.";
        }
        else if (stationId == "alarkajct" && stationSettings["alarka"][StationSettingKeys.TransferStation].BoolValue)
        {
            tooltip = "Disabled due to Alarka being a Transfer Station. Only 1 of each can be selected as a Transfer Station.";
        }
        else if (stationId == "alarka" && stationSettings["alarkajct"][StationSettingKeys.TransferStation].BoolValue)
        {
            tooltip = "Disabled due to Alarka Jct being a Transfer Station. Only 1 of each can be selected as a Transfer Station.";
        }
        else
        {
            tooltip = "Disabled.";
        }

        return new TooltipInfo("Transfer Station Toggle", tooltip);
    }

    private TooltipInfo getModeTooltip(PassengerLocomotive passengerLocomotive, string stationName, string stationId, TMP_Dropdown ModeDropdown)
    {
        string tooltip = "";

        if (ModeDropdown.interactable)
        {
            tooltip = $"Choose whether the train should be Point to Point or Loop mode at {stationName}.";
        }
        else if (stationId != "alarka" && !passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.TerminusStation].BoolValue)
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

    private void BuildStationSettingsRow(UIPanelBuilder builder, PassengerLocomotive passengerLocomotive, List<PassengerStop> stationStops, List<Car> coaches, string stationId, string stationName)
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

            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.StopAtStation].BoolValue, delegate (bool on)
            {
                logger.Debug("StopAt for {0} set to {1}", stationId, on);
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                Dictionary<string, Value> stationSettings = new(settings[stationId].DictionaryValue);
                stationSettings[StationSettingKeys.StopAtStation] = Value.Bool(on);

                if (on)
                {
                    stationSettings[StationSettingKeys.PickupPassengersForStation] = Value.Bool(true);
                }

                settings[stationId] = Value.Dictionary(stationSettings);

                if (interactableStationMap[stationId] != null)
                {
                    settings = SetInteractions(passengerLocomotive, settings, stationStops);
                }

                passengerLocomotive.SaveSettings(settings);

                SelectStationOnCoaches(settings, stationStops, coaches);
            }).Tooltip("StopAt Station Toggle", $"Toggle whether {stationName} should be stopped at by the train")
            .Width(70f);

            RectTransform terminusToggle = hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.TerminusStation].BoolValue, delegate (bool on)
            {
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                Dictionary<string, Value> stationSettings = new(settings[stationId].DictionaryValue);
                int numTerminusStations = settings.Where(s => s.Value[StationSettingKeys.TerminusStation].BoolValue).Count();

                logger.Debug("There are currently {0} terminus stations set", numTerminusStations);
                if (numTerminusStations >= 2 && on == true)
                {
                    logger.Debug("You can only select 2 terminus stations. Please unselect one before selecting another");
                }
                else
                {
                    logger.Debug("IsTerminusStation for {0} set to {1}", stationId, on);
                    stationSettings[StationSettingKeys.TerminusStation] = Value.Bool(on);

                    settings[stationId] = Value.Dictionary(stationSettings);

                    if (interactableStationMap[stationId] != null)
                    {
                        settings = SetInteractions(passengerLocomotive, settings, stationStops);
                    }

                    passengerLocomotive.SaveSettings(settings);
                }
            }).Width(85f);

            terminusToggle.Tooltip(() => getTerminusTooltip(passengerLocomotive, stationName, terminusToggle.GetComponentInChildren<Toggle>()));

            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.PickupPassengersForStation].BoolValue, delegate (bool on)
            {
                logger.Debug("Pickup Passengers for {0} set to {1}", stationId, on);
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                Dictionary<string, Value> stationSettings = new(settings[stationId].DictionaryValue);
                stationSettings[StationSettingKeys.PickupPassengersForStation] = Value.Bool(on);

                if (!on)
                {
                    stationSettings[StationSettingKeys.StopAtStation] = Value.Bool(false);
                }

                settings[stationId] = Value.Dictionary(stationSettings);

                if (interactableStationMap[stationId] != null)
                {
                    settings = SetInteractions(passengerLocomotive, settings, stationStops);
                }

                passengerLocomotive.SaveSettings(settings);

                SelectStationOnCoaches(settings, stationStops, coaches);
            }).Tooltip("PickUp Station Toggle", $"Toggle whether passengers for {stationName} should be picked up at the stations toggled in 'Stop At'")
            .Width(70f);

            RectTransform pauseAtStationToggle = hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.PauseAtStation].BoolValue, delegate (bool on)
            {
                logger.Debug("Pause for {0} set to {1}", stationId, on);
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                Dictionary<string, Value> stationSettings = new(settings[stationId].DictionaryValue);
                stationSettings[StationSettingKeys.PauseAtStation] = Value.Bool(on);
                passengerLocomotive.SaveStationSettings(stationId, stationSettings);
            })
            .Width(60f);

            pauseAtStationToggle.Tooltip(() => getPauseTooltip(passengerLocomotive, stationName, pauseAtStationToggle.GetComponentInChildren<Toggle>()));

            RectTransform transferStationToggle = hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.TransferStation].BoolValue, delegate (bool on)
            {
                logger.Debug("Transfer for {0} set to {1}", stationId, on);
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                Dictionary<string, Value> stationSettings = new(settings[stationId].DictionaryValue);
                stationSettings[StationSettingKeys.TransferStation] = Value.Bool(on);

                settings[stationId] = Value.Dictionary(stationSettings);

                if (interactableStationMap[stationId] != null)
                {
                    settings = SetInteractions(passengerLocomotive, settings, stationStops);
                }

                passengerLocomotive.SaveSettings(settings);
            })
            .Width(85f);

            transferStationToggle.Tooltip(() => getTransferTooltip(passengerLocomotive, stationName, stationId, transferStationToggle.GetComponentInChildren<Toggle>()));

            RectTransform passengerModeDropDown = hBuilder.AddDropdown(passengerModList, (int)passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.PassengerMode].IntValue, delegate (int index)
            {
                logger.Debug("Passenger Mode for {0} set to {1}", stationId, passengerModes[index].ToString());
                Dictionary<string, Value> settings = passengerLocomotive.GetMutablePassengerSettings();
                Dictionary<string, Value> stationSettings = new(settings[stationId].DictionaryValue);
                stationSettings[StationSettingKeys.PassengerMode] = Value.Int(index);
                passengerLocomotive.SaveStationSettings(stationId, stationSettings);
            }).Width(125f).Height(20f);

            passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>().gameObject.AddComponent<DropDownUpdater>().Configure(() => (int)passengerLocomotive.GetPassengerSettings()[stationId][StationSettingKeys.PassengerMode].IntValue);
            passengerModeDropDown.Tooltip(() => getModeTooltip(passengerLocomotive, stationName, stationId, passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>()));

            interactableStationMap[stationId] = new Interactable(terminusToggle.GetComponentInChildren<Toggle>(), pauseAtStationToggle.GetComponentInChildren<Toggle>(), transferStationToggle.GetComponentInChildren<Toggle>(), passengerModeDropDown.GetComponentInChildren<TMP_Dropdown>());
        });
    }

    private int GetPickupStationsCount(IReadOnlyDictionary<string, Value> stationSettings, List<PassengerStop> stationStops)
    {
        return stationSettings
        .Where(kv => stationStops
                    .Select(stp => stp.identifier).Contains(kv.Key)
                    && kv.Value[StationSettingKeys.PickupPassengersForStation].BoolValue)
        .Select(stp => stp.Key)
        .ToList().Count;
    }

    private int GetStopAtStationsCount(IReadOnlyDictionary<string, Value> stationSettings, List<PassengerStop> stationStops)
    {
        return stationSettings
        .Where(kv => stationStops
                    .Select(stp => stp.identifier).Contains(kv.Key)
                    && kv.Value[StationSettingKeys.StopAtStation].BoolValue)
        .Select(stp => stp.Key)
        .ToList().Count;
    }
    private void SelectStationOnCoaches(IReadOnlyDictionary<string, Value> stationSettings, List<PassengerStop> stationStops, List<Car> coaches)
    {
        List<string> passengerStopsToSelect = stationSettings
        .Where(kv =>
                stationStops
                .Select(stp => stp.identifier)
                .Contains(kv.Key) && kv.Value[StationSettingKeys.PickupPassengersForStation].BoolValue)
                .Select(stp => stp.Key)
                .ToList();

        logger.Information("Applying selected stations {0} to all coupled coaches", passengerStopsToSelect);

        foreach (Car coach in coaches)
        {
            StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, passengerStopsToSelect));
        }
    }

    private void PopulateSettings(UIPanelBuilder builder, PassengerLocomotive passengerLocomotive)
    {
        builder.AddSection("Settings:");
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.Disable].BoolValue, delegate (bool on)
            {
                logger.Information("Disable set to {0}", on);

                passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.Disable, Value.Bool(on));
            }).Tooltip("Disable Passenger Helper Toggle", $"Toggle whether PassengerHelper should be disabled or not")
            .Width(25f);
            hBuilder.AddLabel("Disable").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.PauseForDiesel].BoolValue, delegate (bool on)
            {
                logger.Debug("Pause for Diesel set to {0}", on);

                passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.PauseForDiesel, Value.Bool(on));
            }).Tooltip("Pause for Low Diesel Toggle", $"Toggle whether the AI should pause for low diesel")
            .Width(25f);
            hBuilder.AddLabel("Pause for low Diesel").Width(200f);
            hBuilder.AddLabel(() => "Pause at " + (passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.DieselLevel].FloatValue * 100).ToString() + "%", UIPanelBuilder.Frequency.Fast);
            hBuilder.Spacer().Width(2f);
            hBuilder.AddLabel("Set to: ");
            hBuilder.AddInputField((passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.DieselLevel].FloatValue * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Debug("Entered a Diesel Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Debug("Entered a Diesel Level: {0}%", value);

                    passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.DieselLevel, Value.Float(value / 100));
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
            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.PauseForCoal].BoolValue, delegate (bool on)
            {
                logger.Debug("Pause for Coal set to {0}", on);

                passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.PauseForCoal, Value.Bool(on));
            }).Tooltip("Pause for Low Coal Toggle", $"Toggle whether the AI should pause for low coal")
            .Width(25f);
            hBuilder.AddLabel("Pause for low Coal").Width(200f);
            hBuilder.AddLabel(() => "Pause at " + (passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.CoalLevel].FloatValue * 100).ToString() + "%", UIPanelBuilder.Frequency.Fast);
            hBuilder.Spacer().Width(2f);
            hBuilder.AddLabel("Set to: ");
            hBuilder.AddInputField((passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.CoalLevel].FloatValue * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Debug("Entered a Coal Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Debug("Entered a Coal Level: {0}%", value);

                    passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.CoalLevel, Value.Float(value / 100));
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
                hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.PauseForWater].BoolValue, delegate (bool on)
                {
                    logger.Debug("Pause for Water set to {0}", on);

                    passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.PauseForWater, Value.Bool(on));
                }).Tooltip("Pause for Low Water Toggle", $"Toggle whether the AI should pause for low water")
                .Width(25f);
                hBuilder.AddLabel("Pause for low Water", delegate (TMP_Text text)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }).Width(200f);
                hBuilder.AddLabel(() => "Pause at " + (passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.WaterLevel].FloatValue * 100).ToString() + "%", UIPanelBuilder.Frequency.Fast);
                hBuilder.Spacer().Width(2f);
                hBuilder.AddLabel("Set to: ");
                hBuilder.AddInputField((passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.WaterLevel].FloatValue * 100).ToString(), delegate (string val)
            {
                if (float.TryParse(val, out float value))
                {
                    if (value < 0 || value > 100)
                    {
                        logger.Debug("Entered a Water Level greater than 100 or lower than 0");
                        return;
                    }

                    logger.Debug("Entered a Water Level: {0}%", value);

                    passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.WaterLevel, Value.Float(value / 100));
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
            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.PauseAtNextStation].BoolValue, delegate (bool on)
            {
                logger.Debug("Pause at next station set to {0}", on);

                passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.PauseAtNextStation, Value.Bool(on));
            }).Tooltip("Pause at Next Station Toggle", $"Toggle whether the AI should pause at the next station")
            .Width(25f);
            hBuilder.AddLabel("Pause At Next Station").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
            {
                hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.PauseAtTerminusStation].BoolValue, delegate (bool on)
                {
                    logger.Debug("Pause at terminus station set to {0}", on);

                    passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.PauseAtTerminusStation, Value.Bool(on));
                }).Tooltip("Pause At Terminus Station Toggle", $"Toggle whether the AI should pause at the terminus station(s)")
                .Width(25f);
                hBuilder.AddLabel("Pause At Terminus Station").FlexibleWidth(1f);
            });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.PreventLoadWhenPausedAtStation].BoolValue, delegate (bool on)
            {
                logger.Debug("Prevent loading when paused at station set to {0}", on);

                passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.PreventLoadWhenPausedAtStation, Value.Bool(on));
            }).Tooltip("Prevent loading of passengers when paused Toggle", $"Toggle whether the AI should not load passengers if the train is paused at a station")
            .Width(25f);
            hBuilder.AddLabel("Prevent Loading of Passengers When Paused").FlexibleWidth(1f);
        });
        builder.HStack(delegate (UIPanelBuilder hBuilder)
        {
            hBuilder.AddToggle(() => passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.WaitForFullPassengersTerminusStation].BoolValue, delegate (bool on)
            {
                logger.Debug("Wait for full passengers at terminus station set to {0}", on);

                passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.WaitForFullPassengersTerminusStation, Value.Bool(on));
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

            RectTransform dotSlider = hBuilder.AddSliderQuantized(() => (passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.DirectionOfTravel].IntValue), () => Enum.GetValues(typeof(DirectionOfTravel)).GetValue(passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.DirectionOfTravel].IntValue).ToString(), delegate (float value)
                {
                    int newValue = (int)value;

                    DirectionOfTravel newDirectionOfTravel = (DirectionOfTravel)Enum.GetValues(typeof(DirectionOfTravel)).GetValue(newValue);
                    logger.Information("Set direction of travel to: {0}", newDirectionOfTravel.ToString());

                    passengerLocomotive.SaveSetting(PassengerLocomotiveSettingKeys.DirectionOfTravel, Value.Int(newValue));
                }, 1f, 0, 2).Width(150f);
            dotSlider.GetComponentInChildren<CarControlSlider>().interactable = !passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.DoTLocked].BoolValue;

            hBuilder.AddField("Direction of Travel", dotSlider).Tooltip("Direction of Travel", passengerLocomotive.GetPassengerSettings()[PassengerLocomotiveSettingKeys.DoTLocked].BoolValue ? tooltipLocked + tooltipUnlocked : tooltipUnlocked);
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