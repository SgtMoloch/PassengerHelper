namespace PassengerHelper.Patches;

using System.Reflection;
using Game.Messages;
using Support;
using HarmonyLib;
using Model;
using Model.AI;
using UI.Builder;
using UI.CarInspector;
using UI.EngineControls;
using UnityEngine;
using UI.Common;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using Model.Ops;
using Model.Definition;
using PassengerHelper.Support.GameObjects;
using PassengerHelper.Plugin;
using KeyValue.Runtime;

[HarmonyPatch]
public static class CarInspectorPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "PopulateCarPanel")]
    private static void PopulateCarPanel(UIPanelBuilder builder, Car ____car)
    {
        PassengerHelperPlugin plugin = Loader.PassengerHelper;

        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        if (____car.IsLocomotive)
        {
            BaseLocomotive _locomotive = (BaseLocomotive)____car;

            AutoEngineerPersistence persistence = new AutoEngineerPersistence(_locomotive.KeyValueObject);
            AutoEngineerOrdersHelper helper = new AutoEngineerOrdersHelper(_locomotive as BaseLocomotive, persistence);
            AutoEngineerMode mode = helper.Mode;

            bool AEMode = mode == AutoEngineerMode.Road || mode == AutoEngineerMode.Waypoint;

            PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(_locomotive);
            TrainState state = plugin.trainStateManager.GetState(pl);

            if (pl.GetCoaches().Any())
            {
                builder.Spacer(5f);
                builder.HStack(delegate (UIPanelBuilder hBuilder)
                {
                    hBuilder.AddButton("PassengerSettings", delegate
                    {
                        plugin.settingsManager.ShowSettingsWindow(pl);
                    }).Tooltip("Open Passenger Settings menu", "Open Passenger Settings menu");

                    if (AEMode && state.isStoppedOverrideable())
                    {
                        hBuilder.AddButton("Continue", delegate
                        {
                            Loader.Log($"CONTINUE CLICK: loco={_locomotive.DisplayName} id={_locomotive.id} setting Continue=true");

                            pl.SetStopOverrideActive();
                            hBuilder.Rebuild();
                        }).Tooltip("Resume travel", "Resume travel");
                    }

                    hBuilder.AddObserver(pl._keyValueObject.Observe(pl.KeyValueIdentifier_State, delegate (Value val)
                    {
                        TrainState state = plugin.trainStateManager.GetState(pl);

                        if (state.isStoppedOverrideable() || !state.CurrentlyStopped)
                        {
                            hBuilder.Rebuild();
                        }

                    }, callInitial: false));
                });
            }
        }
    }

    private static Vector2? originalWindowSize;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "PopulatePanel")]
    private static void PopulatePanel(Window ____window)
    {
        PassengerHelperPlugin plugin = Loader.PassengerHelper;

        if (!Loader.ModEntry.Enabled)
        {
            return;
        }

        int baseHeight = 322;
        int heightToAdd = 30;
        int newHeight = baseHeight + heightToAdd;

        bool waypointToDestination = Harmony.HasAnyPatches("SwitchToDestination");
        bool smartOrders = Harmony.HasAnyPatches("SmartOrders");

        if (smartOrders)
        {
            newHeight = 424;
        }

        if (waypointToDestination)
        {
            if (newHeight < 392)
            {
                newHeight = 392;
            }
        }

        if (originalWindowSize == null)
        {
            originalWindowSize = ____window.GetContentSize();
        }

        ____window.SetContentSize(new Vector2(originalWindowSize.Value.x - 2, newHeight));
    }
}
