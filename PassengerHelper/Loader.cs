using System.Collections.Generic;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model.Ops;
using PassengerHelper.Support;
using UnityModManagerNet;

namespace PassengerHelper.UMM;

public static class Loader
{
    public static UnityModManager.ModEntry ModEntry { get; private set; }
    public static PassengerHelper PassengerHelper { get; private set; }

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        if (ModEntry != null)
        {
            modEntry.Logger.Warning("Passenger Helper is already loaded!");
            return false;
        }
        modEntry.Logger.Log($"Loading Passenger Helper assembly version {Assembly.GetExecutingAssembly().GetName().Version}");

        ModEntry = modEntry;
        PassengerHelper = new PassengerHelper(modEntry.Info.Id);

        Messenger.Default.Register<MapDidLoadEvent>(modEntry, OnMapDidLoad);

        ModEntry.OnUnload = Unload;

        return true;
    }

    private static bool Unload(UnityModManager.ModEntry modEntry)
    {
        PassengerHelper.harmony.UnpatchAll(modEntry.Info.Id);

        return true;
    }

    private static void OnMapDidLoad(MapDidLoadEvent @event)
    {
        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        PassengerHelper.passengerStopOrderManager.EnsureTopologyUpToDate(() =>
        {
            if (StopOrder.TryComputeOrderedStopsAnchored(out var ordered, out var warn))
            {
                if (!string.IsNullOrEmpty(warn))
                {
                    Loader.Log(warn);
                }

                // TEMP DEBUG: dump ordering
                for (int i = 0; i < ordered.Count; i++)
                {
                    Loader.Log($"StopOrder[{i}] = {ordered[i].identifier}");
                }

                return ordered;
            }

            Loader.Log("Stop ordering failed; using empty list.");
            return new List<PassengerStop>();
        });

        PassengerHelper.passengerStopOrderManager.RefreshUnlocked(stop => !stop.ProgressionDisabled);

        // 🔍 TEMP sanity log — after RefreshUnlocked
        var all = PassengerHelper.passengerStopOrderManager.OrderedAll;
        var unlocked = PassengerHelper.passengerStopOrderManager.OrderedUnlocked;

        Loader.Log($"StopOrder: all={all.Count}, unlocked={unlocked.Count}");
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

}