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
    private string KeyValueIdentifier => "pass.passhelper." + identifier;
    private readonly HashSet<string> _workingCarIds = new HashSet<string>();
    private List<PassengerMarker.Group> _transferGroups = new();
    internal readonly Dictionary<string, int> _transfersWaiting = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Waiting => _transfersWaiting;
    private Coroutine _loop;

    private IDisposable _observer;

    private readonly List<string> orderedStations = PassengerHelperPlugin.Shared.orderedStations;

    private void Awake()
    {
        this.passengerStop = base.gameObject.GetComponentInParent<PassengerStop>();
        this.identifier = this.passengerStop.identifier;

        logger.Verbose("awake in PassengerHelperPassengerStop with identifier {0}", identifier);
        _keyValueObject = gameObject.GetComponent<KeyValueObject>();

        StateManager.Shared.RegisterPropertyObject(KeyValueIdentifier, _keyValueObject, AuthorizationRequirement.HostOnly);

    }
    protected override void OnEnableWithProperties()
    {
        logger.Verbose("on enable with properties in PassengerHelperPassengerStop");
        logger.Debug("key value object pass helper state: {0}", _keyValueObject["pass-helper-state"]);
        if (StateManager.IsHost)
        {
            logger.Information("on enable with properties load state");
            LoadState();
        }
        else
        {
            logger.Verbose("on enable with properties observe state");
            _observer = _keyValueObject.Observe("pass-helper-state", delegate
            {
                LoadState();
            });
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

    private void SaveState()
    {
        Dictionary<string, Value> saveDictionary = new Dictionary<string, Value>();
        Dictionary<string, Value> transferWaitingDictionary = new Dictionary<string, Value>();

        foreach (var (key, num2) in _transfersWaiting)
        {
            if (num2 > 0)
            {
                transferWaitingDictionary[key] = Value.Int(num2);
            }
        }

        List<Value> serializableGroupList = new();

        foreach (PassengerMarker.Group group in this._transferGroups)
        {
            Dictionary<string, Value> groupDictionary = new Dictionary<string, Value>();
            groupDictionary["origin"] = Value.String(group.Origin);
            groupDictionary["destination"] = Value.String(group.Destination);
            groupDictionary["count"] = Value.Int(group.Count);
            groupDictionary["boarded"] = Value.String(group.Boarded.TotalSeconds.ToString());

            serializableGroupList.Add(Value.Dictionary(groupDictionary));
        }

        saveDictionary["transfers_waiting"] = Value.Dictionary(transferWaitingDictionary);
        saveDictionary["transfer_group_list"] = Value.Array(serializableGroupList);

        logger.Information("Pre state: {0}", _keyValueObject);
        _keyValueObject["pass-helper-state"] = Value.Dictionary(saveDictionary);
        logger.Information("Saved state: {0}", _keyValueObject);
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
            _transfersWaiting.Clear();
            Value transferWaitingValue = dictionaryValue["transfers_waiting"];
            foreach (KeyValuePair<string, Value> item in transferWaitingValue.DictionaryValue)
            {
                item.Deconstruct(out var key, out transferWaitingValue);
                string key2 = key;
                Value value2 = transferWaitingValue;
                _transfersWaiting[key2] = value2.IntValue;
            }

            List<Value> transferGroupListValue = dictionaryValue["transfer_group_list"].ArrayValue.ToList();

            this._transferGroups.Clear();

            foreach (Value groupValue in transferGroupListValue)
            {
                Dictionary<string, Value> groupDictionary = (Dictionary<string, Value>)groupValue.DictionaryValue;

                this._transferGroups.Add(new PassengerMarker.Group(groupDictionary["origin"].StringValue, groupDictionary["destination"].StringValue, groupDictionary["count"].IntValue, new GameDateTime(Double.Parse(groupDictionary["boarded"].StringValue))));
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Exception in LoadState {identifier}", identifier);
        }
    }

    public void UnloadTransferPassengers(PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.UnloadTransferComplete)
        {
            return;
        }
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        PassengerStop CurrentStop = passengerLocomotive.CurrentStation;
        string CurrentStopIdentifier = CurrentStop.identifier;
        string CurrentStopName = CurrentStop.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.IsTerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.StopAt == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> pickUpPassengerStations = settings.Stations.Where(s => s.Value.PickupPassengers).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        logger.Information("Running UnloadTransferPassengers procedure for Train {0} at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}, in the following direction: {5}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString());

        if (orderedTerminusStations.Count != 2)
        {
            logger.Information("there are not exactly 2 terminus stations, current selected terminus stations: {0}. Continuing normally", orderedTerminusStations);
            return;
        }

        // v1
        // does train consider this station a transfer station?
        if (settings.Stations[CurrentStopIdentifier].StationAction == StationAction.Transfer)
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
                foreach (Car coach in coaches)
                {
                    PassengerMarker marker = coach.GetPassengerMarker() ?? PassengerMarker.Empty();
                    if (marker.Groups.Count == 0)
                    {
                        continue;
                    }

                    logger.Information("Coach has the following passenger marker: {0}", marker);
                    StartCoroutine(UnloadTransferPassengers(marker, coach, transferStations));
                }
            }
        }
        passengerLocomotive.UnloadTransferComplete = true;
    }

    private IEnumerator UnloadTransferPassengers(PassengerMarker marker, Car coach, List<string> transferStations)
    {
        _workingCarIds.Add(coach.id);
        for (int i = 0; i < marker.Groups.Count; i++)
        {
            PassengerMarker.Group group = marker.Groups[i];
            logger.Information("Checking group: {0}", group);
            if (group.Count <= 0)
            {
                continue;
            }
            if (transferStations.Contains(group.Destination))
            {
                logger.Information("Group contains {0} passenger(s) for a transfer destination, {1}", group.Count, group.Destination);
                PassengerMarker.Group transferGroup = new PassengerMarker.Group(group.Origin, group.Destination, 0, group.Boarded);
                _transferGroups.Add(transferGroup);
                int groupIndex = _transferGroups.Count - 1;
                while (group.Count > 0)
                {
                    transferGroup.Count++;
                    group.Count--;

                    marker.Groups[i] = group;
                    coach.SetPassengerMarker(marker);
                    _transferGroups[groupIndex] = transferGroup;

                    yield return new WaitForSeconds(UnityEngine.Random.Range(1f, 2f));
                }
                marker.Groups.RemoveAt(i);
                i--;
                marker.Destinations.Remove(group.Destination);
            }
            else
            {
                continue;
            }
        }
        _workingCarIds.Remove(coach.id);
    }

    public void LoadTransferPassengers(PassengerLocomotive passengerLocomotive)
    {
        if (passengerLocomotive.LoadTransferComplete)
        {
            return;
        }
        PassengerLocomotiveSettings settings = passengerLocomotive.Settings;
        BaseLocomotive _locomotive = passengerLocomotive._locomotive;
        string LocomotiveName = _locomotive.DisplayName;
        string CurrentStopIdentifier = this.passengerStop.identifier;
        string CurrentStopName = this.passengerStop.DisplayName;
        IEnumerable<Car> coaches = _locomotive.EnumerateCoupled().Where(car => car.Archetype == CarArchetype.Coach);
        List<string> orderedTerminusStations = settings.Stations.Where(station => station.Value.IsTerminusStation == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> orderedSelectedStations = settings.Stations.Where(station => station.Value.StopAt == true).Select(station => station.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();
        List<string> pickUpPassengerStations = settings.Stations.Where(s => s.Value.PickupPassengers == true).Select(s => s.Key).OrderBy(d => orderedStations.IndexOf(d)).ToList();

        int indexEastTerminus = orderedSelectedStations.IndexOf(orderedTerminusStations[0]);
        int indexWestTerminus = orderedSelectedStations.IndexOf((orderedTerminusStations[1]));

        logger.Information("Running LoadTransferPassengers procedure for Train {0} at {1} with {2} coaches, the following selected stations: {3}, and the following terminus stations: {4}, in the following direction: {5}",
            LocomotiveName, CurrentStopName, coaches.Count(), orderedSelectedStations, orderedTerminusStations, settings.DirectionOfTravel.ToString());


        logger.Information("Station Manager has the following groups: {0}", _transferGroups);
        if (_transferGroups.Count > 0)
        {
            logger.Information("The current station {0} contains {1} groups, checking to see if any of them can be loaded onto the current train", CurrentStopName, _transferGroups.Count);
            foreach (Car coach in coaches)
            {
                PassengerMarker marker = coach.GetPassengerMarker() ?? PassengerMarker.Empty();
                logger.Information("Coach has the following passenger marker: {0}", marker);
                StartCoroutine(LoadTransferPassengers(marker, coach, orderedSelectedStations));

            }
        }

        passengerLocomotive.LoadTransferComplete = true;

        // do you have another transfer station?
        // if so, where is it in relation to current station?
        // if you are past it, ignore
        // if it is in the direction you are traveling, load all passengers for station up to and past the transfer station
    }

    private IEnumerator LoadTransferPassengers(PassengerMarker marker, Car coach, List<string> orderedSelectedStations)
    {
        for (int i = 0; i < _transferGroups.Count; i++)
        {
            PassengerMarker.Group stationGroup = _transferGroups[i];
            logger.Information("Checking group: {0}", stationGroup);
            if (stationGroup.Count <= 0)
            {
                continue;
            }
            // to what stations are you going? (terminus to terminus)
            // if selected stations inbounds match any transfer passengers I have, load them until cars at capacity or no more transfer passengers
            if (orderedSelectedStations.Contains(stationGroup.Destination))
            {
                logger.Information("Found group {0} that can be loaded onto the current train", stationGroup);

                int maxCapacity = PassengerCapacity(coach, this.passengerStop);
                logger.Information("Coach has the following capacity: {0}", maxCapacity);

                PassengerMarker.Group carGroup = new PassengerMarker.Group(stationGroup.Origin, stationGroup.Destination, 0, stationGroup.Boarded);
                marker.Groups.Add(carGroup);
                int groupIndex = marker.Groups.Count - 1;

                while (stationGroup.Count > 0 && marker.TotalPassengers < maxCapacity)
                {
                    carGroup.Count++;
                    stationGroup.Count--;

                    marker.Groups[groupIndex] = carGroup;
                    coach.SetPassengerMarker(marker);
                    _transferGroups[i] = stationGroup;

                    yield return new WaitForSeconds(UnityEngine.Random.Range(1f, 2f));
                }
                if (stationGroup.Count <= 0)
                {
                    _transferGroups.RemoveAt(i);
                    i--;
                    continue;
                }
                if (marker.TotalPassengers == maxCapacity)
                {
                    break;
                }
            }
        }
    }

    private int PassengerCapacity(Car car, PassengerStop CurrentStop)
    {
        return (int)car.Definition.LoadSlots.First((LoadSlot slot) => slot.LoadRequirementsMatch(CurrentStop.passengerLoad)).MaximumCapacity;
    }
}