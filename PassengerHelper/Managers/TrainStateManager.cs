using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game;
using Game.Events;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using PassengerHelper.Support;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.Plugin;

namespace PassengerHelper.Managers;


public class TrainStateManager
{
    private Dictionary<PassengerLocomotive, TrainState> stateMap = new();
    private Dictionary<PassengerLocomotive, IDisposable> plKeyObvDisposeMap = new();

    public TrainStateManager()
    {
        Messenger.Default.Register<MapDidUnloadEvent>(this, OnMapDidUnload);
    }

    public void SaveState(PassengerLocomotive pl, TrainState state)
    {
        StateManager.ApplyLocal(new PropertyChange(pl._locomotive.id, pl.KeyValueIdentifier_State, PropertyValueConverter.RuntimeToSnapshot(state.PropertyValue())));

        pl.stateHash = state.GetHashCode();

        stateMap[pl] = state;
    }

    public TrainState CreateNewState(PassengerLocomotive pl)
    {
        TrainState state = new();
        stateMap.Add(pl, state);

        SaveState(pl, state);

        IDisposable plObv = pl._keyValueObject.Observe(pl.KeyValueIdentifier_State, delegate (Value val)
        {
            Loader.LogVerbose($"updating state map for loco {pl._locomotive.DisplayName}, new values: {val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString())}");
            TrainState state = TrainState.FromPropertyValue(val);
            Loader.LogVerbose($"new state for {pl._locomotive.DisplayName}: {state.ToString()}");
            stateMap[pl] = state;
        }, callInitial: false);

        plKeyObvDisposeMap[pl] = plObv;

        return state;
    }

    public TrainState LoadState(PassengerLocomotive pl)
    {
        TrainState state = TrainState.FromPropertyValue(pl._keyValueObject[pl.KeyValueIdentifier_State]);

        Loader.Log($"loaded state for {pl._locomotive.DisplayName}");
        if (!stateMap.ContainsKey(pl))
        {
            Loader.Log($"pass loco not in state map, adding observer");
            stateMap.Add(pl, state);

            IDisposable plObv = pl._keyValueObject.Observe(pl.KeyValueIdentifier_State, delegate (Value val)
            {
                Loader.LogVerbose($"updating state map for loco {pl._locomotive.DisplayName}, new values: {val.DictionaryValue.Select(kvp => kvp.Key.ToString() + ": " + kvp.Value.ToString())}");
                TrainState state = TrainState.FromPropertyValue(val);
                Loader.LogVerbose($"new state for {pl._locomotive.DisplayName}: {state.ToString()}");
                stateMap[pl] = state;
            }, callInitial: false);
            plKeyObvDisposeMap[pl] = plObv;
        }

        return state;
    }

    public TrainState GetState(PassengerLocomotive pl)
    {
        if (!stateMap.ContainsKey(pl))
        {
            return CreateNewState(pl);
        }

        return stateMap[pl];
    }

    private void OnMapDidUnload(MapDidUnloadEvent @event)
    {
        foreach (PassengerLocomotive pl in plKeyObvDisposeMap.Keys)
        {
            plKeyObvDisposeMap[pl].Dispose();
        }

        foreach (KeyValuePair<PassengerLocomotive, TrainState> kvp in stateMap)
        {
            TrainState state = kvp.Value;
            state.gameLoadFlag = true;

            SaveState(kvp.Key, state);
        }
        stateMap.Clear();

        plKeyObvDisposeMap.Clear();
    }
}