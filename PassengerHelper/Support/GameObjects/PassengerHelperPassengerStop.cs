namespace PassengerHelperPlugin.GameObjects;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.AccessControl;
using Game.Messages;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model.OpsNew;
using RollingStock;
using Serilog;
using UnityEngine;
using Support;
using Model;
using Model.Definition;
using Model.Definition.Data;

public class PassengerHelperPassengerStop : GameBehaviour
{
    static Serilog.ILogger logger = Log.ForContext(typeof(PassengerHelperPassengerStop));

    public string identifier;
    private PassengerStop passengerStop;
    private KeyValueObject _keyValueObject;
    private Coroutine _loop;
    private string KeyValueIdentifier => "pass.passhelper." + identifier;
    internal List<PassengerMarker.Group> _stationTransferGroups = new();
    internal Dictionary<string, int> _stationTransfersWaiting => _stationTransferGroups.GroupBy(g => g.Destination).ToDictionary(k => k.Key, v => v.Sum(g => g.Count));
    private IDisposable _observer;
    private readonly List<string> orderedStations = PassengerHelperPlugin.Shared.orderedStations;

    private void Awake()
    {
        this.passengerStop = base.gameObject.GetComponentInParent<PassengerStop>();
        this.identifier = this.passengerStop.identifier;
        _keyValueObject = gameObject.GetComponent<KeyValueObject>();

        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier, _keyValueObject, AuthorizationRequirement.HostOnly);
    }

    protected override void OnEnableWithProperties()
    {
        if (StateManager.IsHost)
        {
            logger.Information("on enable with properties load state");
            LoadState();
        }
        else
        {
            _observer = _keyValueObject.Observe("pass-helper-state", delegate
            {
                LoadState();
            });
        }

        if (StateManager.IsHost)
        {
            _loop = StartCoroutine(Loop());
        }
    }
    private void OnDestroy()
    {
        if (StateManager.Shared != null)
        {
            StateManager.Shared.UnregisterPropertyObject(KeyValueIdentifier);
        }
    }
    protected override void OnDisable()
    {
        base.OnDisable();

        _observer?.Dispose();
        _observer = null;
        if (_loop != null)
        {
            StopCoroutine(_loop);
            _loop = null;
        }
    }

    private IEnumerator Loop()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);
            SaveState();
        }
    }

    private void SaveState()
    {
        Dictionary<string, Value> saveDictionary = new Dictionary<string, Value>();

        List<Value> serializableGroupList = this._stationTransferGroups.Select(g => g.PropertyValue()).ToList();

        saveDictionary["transfer_group_list"] = Value.Array(serializableGroupList);
        _keyValueObject["pass-helper-state"] = Value.Dictionary(saveDictionary);
    }

    private void LoadState()
    {
        IReadOnlyDictionary<string, Value> dictionaryValue = _keyValueObject["pass-helper-state"].DictionaryValue;
        logger.Information("loaded state is: {0}", dictionaryValue);

        if (!dictionaryValue.Any())
        {
            return;
        }
        try
        {
            List<Value> transferGroupListValue = dictionaryValue["transfer_group_list"].ArrayValue.ToList();

            this._stationTransferGroups.Clear();
            this._stationTransferGroups.AddRange(transferGroupListValue.Select(g => PassengerMarker.Group.FromPropertyValue(g)));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Exception in LoadState {identifier}", identifier);
        }
    }

    public bool UnloadTransferPassengers(PassengerLocomotive passengerLocomotive, Car coach, PassengerMarker carMarker, PassengerMarker.Group carGroup, ref int carGroupIndex)
    {
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStation;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.StopAt == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> pickUpPassengerStations = settings.Stations.Where(s => s.Value.PickupPassengers).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        logger.Information("Running UnloadTransferPassengers procedure for Train {0} at {1} with the following selected stations: {2}, and the following terminus stations: {3}, in the following direction: {4}",
            LocomotiveName, CurrentStopName, orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString());

        if (passengerLocomotive.Settings.Disable)
        {
            logger.Information("Passenger Helper is disabled, proceeding with normal unload of passengers that aren't selected on the passenger car");
            return false;
        }

        // v1
        // does train consider this station a transfer station?
        if (settings.Stations[CurrentStopIdentifier].PassengerMode == PassengerMode.Transfer)
        {

            logger.Information("Train has this station as a transfer station");
            // does train have transfer passengers?
            bool maybeHasTransferPassengers = pickUpPassengerStations.Count > orderedSelectedStations.Count;

            List<string> transferStations = pickUpPassengerStations.Except(orderedSelectedStations).ToList();
            logger.Information("Train has the following terminus stations: {0}, the following selected stations: {1}, with the following transfer stations: {2}, and the train maybe has transfer passengers: {3}",
            orderedTerminusStations, orderedSelectedStations, transferStations, maybeHasTransferPassengers);

            // if yes, unload them into the manager
            if (maybeHasTransferPassengers)
            {
                logger.Information("Train maybe has transfer passengers");
                logger.Information("Coach has the following passenger marker: {0}", carMarker);
                logger.Information("Checking group: {0}", carGroup);

                if (transferStations.Contains(carGroup.Destination))
                {
                    logger.Information("Car Group contains {0} passenger(s) for a transfer destination, {1}", carGroup.Count, carGroup.Destination);

                    string groupDestination = carGroup.Destination;
                    string groupOrigin = carGroup.Origin;
                    GameDateTime groupBoarded = carGroup.Boarded;

                    PassengerMarker.Group stationGroup;
                    int groupIndex = -1;
                    for (int i = 0; i < this._stationTransferGroups.Count; i++)
                    {
                        PassengerMarker.Group group = this._stationTransferGroups[i];
                        if (group.Destination == groupDestination && group.Origin == groupOrigin && group.Boarded == groupBoarded)
                        {
                            groupIndex = i;
                            break;
                        }
                    }

                    if (groupIndex == -1)
                    {
                        stationGroup = new PassengerMarker.Group(carGroup.Origin, carGroup.Destination, 0, carGroup.Boarded);
                        this._stationTransferGroups.Add(stationGroup);
                        groupIndex = this._stationTransferGroups.Count - 1;
                    }
                    else
                    {
                        stationGroup = this._stationTransferGroups[groupIndex];
                    }

                    stationGroup.Count++;
                    coach.SetPassengerMarker(carMarker);
                    _stationTransferGroups[groupIndex] = stationGroup;
                }

                return true;
            }
        }

        return false;
    }
    public bool LoadTransferPassengers(PassengerLocomotive passengerLocomotive, Car coach, PassengerMarker carMarker, int carCapacity)
    {
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        string CurrentStopIdentifier = this.passengerStop.identifier;
        string CurrentStopName = this.passengerStop.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.TerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.StopAt == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> pickUpPassengerStations = settings.Stations.Where(s => s.Value.PickupPassengers == true).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        logger.Information("Running LoadTransferPassengers procedure for Train {0} at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}, in the following direction: {5}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString());


        logger.Information("Station Manager has the following groups: {0}", _stationTransferGroups);
        logger.Information("The current station {0} contains {1} groups, checking to see if any of them can be loaded onto the current train", CurrentStopName, _stationTransferGroups.Count);
        if (_stationTransferGroups.Count > 0)
        {
            logger.Information("Coach has the following passenger marker: {0}", carMarker);
            foreach (string destination in carMarker.Destinations)
            {
                PassengerMarker.Group stationGroup;
                int groupIndex = -1;
                for (int i = 0; i < this._stationTransferGroups.Count; i++)
                {
                    PassengerMarker.Group group = this._stationTransferGroups[i];
                    logger.Information("Checking group: {0}", group);
                    if (group.Count <= 0)
                    {
                        this._stationTransferGroups.RemoveAt(i);
                        i--;
                        continue;
                    }
                    if (group.Destination == destination)
                    {
                        logger.Information("group found with matching destination");
                        groupIndex = i;
                        break;
                    }
                }

                if (groupIndex == -1)
                {
                    logger.Information("group with matching destination not found");
                    continue;
                }

                stationGroup = this._stationTransferGroups[groupIndex];

                logger.Information("Found group {0} that can be loaded onto the current train", stationGroup);
                logger.Information("Coach has the following capacity: {0}", carCapacity);

                for (int i = 0; i < carMarker.Groups.Count; i++)
                {
                    PassengerMarker.Group carGroup = carMarker.Groups[i];
                    if (carGroup.Destination == stationGroup.Destination && carGroup.Origin == stationGroup.Origin && carGroup.Boarded == stationGroup.Boarded)
                    {
                        logger.Information("Found existing group on car to add too: {0}", carGroup);
                        carGroup.Count++;
                        stationGroup.Count--;
                        carMarker.Groups[i] = carGroup;
                        this._stationTransferGroups[groupIndex] = stationGroup;
                        coach.SetPassengerMarker(carMarker);
                        SaveState();
                        return true;
                    }
                }
                logger.Information("Did not find existing group on car to add too, creating new group");
                stationGroup.Count--;
                this._stationTransferGroups[groupIndex] = stationGroup;
                carMarker.Groups.Add(new PassengerMarker.Group(stationGroup.Origin, stationGroup.Destination, 1, stationGroup.Boarded));
                coach.SetPassengerMarker(carMarker);
                SaveState();
                return true;
            }
        }

        return false;
        // do you have another transfer station?
        // if so, where is it in relation to current station?
        // if you are past it, ignore
        // if it is in the direction you are traveling, load all passengers for station up to and past the transfer station
    }



    private int PassengerCapacity(Car car, PassengerStop CurrentStop)
    {
        return (int)car.Definition.LoadSlots.First((LoadSlot slot) => slot.LoadRequirementsMatch(CurrentStop.passengerLoad)).MaximumCapacity;
    }
}