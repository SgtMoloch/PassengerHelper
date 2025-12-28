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
            AutoEngineerMode mode2 = helper.Mode;

            bool AEMode = mode2 == AutoEngineerMode.Road || mode2 == AutoEngineerMode.Waypoint;

            PassengerLocomotive pl = plugin.trainManager.GetPassengerLocomotive(_locomotive);
            TrainState state = plugin.trainStateManager.GetState(pl);

            if (_locomotive.EnumerateCoupled().Where(c => c.IsPassengerCar()).Any())
            {
                builder.Spacer(5f);
                builder.HStack(delegate (UIPanelBuilder builder)
                {
                    builder.AddButton("PassengerSettings", delegate
                    {
                        plugin.settingsManager.ShowSettingsWindow(pl);
                    }).Tooltip("Open Passenger Settings menu", "Open Passenger Settings menu");

                    if (AEMode && state.CurrentlyStopped)
                    {
                        builder.AddButton("Continue", delegate
                        {
                            Loader.Log($"CONTINUE CLICK: loco={_locomotive.DisplayName} id={_locomotive.id} setting Continue=true");

                            pl.SetStopOverrideActive();
                            builder.Rebuild();
                        }).Tooltip("Resume travel", "Resume travel");
                    }
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
