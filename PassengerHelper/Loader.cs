namespace PassengerHelper.Plugin;

using System;
using System.Collections.Generic;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model.Ops;
using Support;
using Support.GameObjects;
using UnityEngine;
using UnityModManagerNet;


public static class Loader
{
    public static UnityModManager.ModEntry ModEntry { get; private set; }
    public static PassengerHelperPlugin PassengerHelper { get; private set; }
    public static PassengerHelperSettings Settings { get; private set; }

    private static bool _runtimeCreated;

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        if (ModEntry != null)
        {
            modEntry.Logger.Warning("[Loader] Passenger Helper is already loaded!");
            return false;
        }
        modEntry.Logger.Log($"[Loader] Loading Passenger Helper assembly version {Assembly.GetExecutingAssembly().GetName().Version}");

        ModEntry = modEntry;
        Settings = UnityModManager.ModSettings.Load<PassengerHelperSettings>(modEntry);
        PassengerHelper = new PassengerHelperPlugin(modEntry.Info.Id);
        CreateRuntime();

        Messenger.Default.Register<MapDidLoadEvent>(modEntry, OnMapDidLoad);

        ModEntry.OnUnload = Unload;
        ModEntry.OnGUI = OnGUI;
        ModEntry.OnSaveGUI = OnSaveGUI;

        return true;
    }

    private static bool Unload(UnityModManager.ModEntry modEntry)
    {
        PassengerHelper.harmony.UnpatchAll(modEntry.Info.Id);

        DestroyRuntime();

        return true;
    }

    private static void OnGUI(UnityModManager.ModEntry modEntry)
    {
        Settings.Draw(modEntry);
    }

    private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
    {
        Settings.Save(modEntry);
    }

    private static void OnMapDidLoad(MapDidLoadEvent @event)
    {
        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        PassengerHelper.passengerStopOrderManager.EnsureTopologyUpToDate(() =>
        {
            if (StopOrder.TryComputeOrderedStopsAnchored(out var orderedMainline, out var orderedAll, out var warn))
            {
                if (!string.IsNullOrEmpty(warn))
                {
                    Loader.Log(warn);
                }

                // TEMP DEBUG: dump ordering
                for (int i = 0; i < orderedMainline.Count; i++)
                {
                    Loader.Log($"[Loader] MainlineOrder[{i}] = {orderedMainline[i].identifier}");
                }

                for (int i = 0; i < orderedAll.Count; i++)
                {
                    Loader.Log($"[Loader] AllOrder[{i}] = {orderedAll[i].identifier}");
                }

                return new StopOrderResult { Mainline = orderedMainline, All = orderedAll, Warning = warn };
            }

            return new StopOrderResult { Mainline = new(), All = new(), Warning = "[Loader] Stop ordering failed; using empty lists." };
        });

        PassengerHelper.passengerStopOrderManager.RefreshUnlocked(stop => !stop.ProgressionDisabled);

        // 🔍 TEMP sanity log — after RefreshUnlocked
        var all = PassengerHelper.passengerStopOrderManager.OrderedAll;
        var unlocked = PassengerHelper.passengerStopOrderManager.OrderedUnlockedAll;

        Loader.Log($"[Loader] StopOrder: all={all.Count}, unlocked={unlocked.Count}");
    }

    public static void Log(string str)
    {
        ModEntry?.Logger.Log(str);
    }

    public static void LogDebug(string str)
    {
#if DEBUG
        ModEntry?.Logger.Log(str);
#endif
    }

    public static void LogError(string str)
    {
        ModEntry?.Logger.Error(str);
    }

    public static void LogVerbose(string str)
    {
        if (Settings.VerboseLogging)
            ModEntry?.Logger.Log(str);
    }

    private static void CreateRuntime()
    {
        if (_runtimeCreated)
        {
            return;
        }

        try
        {
            Log("CreateRuntime: creating PassengerHelperRuntime GO");

            GameObject go = new GameObject("PassengerHelperRuntime");
            UnityEngine.Object.DontDestroyOnLoad(go);

            PassengerHelperRuntime runtime = go.AddComponent<PassengerHelperRuntime>();
            PassengerHelper.runtime = runtime;

            runtime.Init(PassengerHelper.stationManager, PassengerHelper.trainManager, PassengerHelper.settingsManager, PassengerHelper.trainStateManager, PassengerHelper.passengerStopOrderManager);

            _runtimeCreated = true;

            Log("CreateRuntime: runtime created + initialized OK");
        }
        catch (Exception ex)
        {
            Loader.LogError($"CreateRuntime: FAILED: {ex}");
        }
    }

    private static void DestroyRuntime()
    {
        if (!_runtimeCreated)
        {
            return;
        }

        GameObject go = GameObject.Find("PassengerHelperRuntime");

        if (go != null)
        {
            UnityEngine.Object.Destroy(go);
        }

        _runtimeCreated = false;
        PassengerHelper.runtime = null;
    }

}