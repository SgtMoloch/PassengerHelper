namespace PassengerHelperPlugin.Patches;

using System.Reflection;
using Game.Messages;
using Support;
using HarmonyLib;
using Model;
using Model.AI;
using Serilog;
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

[HarmonyPatch]
public static class CarInspectorPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(CarInspectorPatches));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "PopulateAIPanel")]
    private static void PopulateAIPanel(UIPanelBuilder builder, CarInspector __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;

        if (!plugin.IsEnabled)
        {
            return;
        }

        BaseLocomotive _car = (BaseLocomotive)(typeof(CarInspector).GetField("_car", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance));

        AutoEngineerPersistence persistence = new AutoEngineerPersistence(_car.KeyValueObject);
        AutoEngineerOrdersHelper helper = new AutoEngineerOrdersHelper(_car as BaseLocomotive, persistence);
        AutoEngineerMode mode2 = helper.Mode;

        if (mode2 == AutoEngineerMode.Road && _car.EnumerateCoupled().Where(c => c.IsPassengerCar()).Any())
        {
            builder.HStack(delegate (UIPanelBuilder builder)
            {
                builder.AddButton("PassengerSettings", delegate
                {
                    plugin.settingsManager.ShowSettingsWindow(_car);
                }).Tooltip("Open Passenger Settings menu", "Open Passenger Settings menu");

                PassengerLocomotive passengerLocomotive = plugin.trainManager.GetPassengerLocomotive(_car);
                if (passengerLocomotive.TrainStatus.CurrentlyStopped)
                {
                    builder.AddButton("Continue", delegate
                    {
                        passengerLocomotive.TrainStatus.Continue = true;
                    }).Tooltip("Resume travel", "Resume travel");
                }
            });
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "OnEnable")]
    private static void OnEnable(Window ____window)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;

        if (!plugin.IsEnabled)
        {
            return;
        }

        Vector2 cs = ____window.GetContentSize();

        if (cs.y < 430)
        {
            ____window.SetContentSize(new Vector2(cs.x, cs.y + 30));

        }

    }
}
