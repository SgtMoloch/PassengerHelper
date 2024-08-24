namespace PassengerHelperPlugin.Patches;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Messages;
using Game.State;
using Support;
using HarmonyLib;
using Model;
using Model.AI;
using Railloader;
using Serilog;
using UI.Builder;
using UI.CarInspector;
using UI.EngineControls;
using UnityEngine;
using UI.Common;

[HarmonyPatch]
public static class CarInspectorPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(CarInspectorPatches));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CarInspector), "PopulateAIPanel")]
    private static bool PopulateAIPanel(UIPanelBuilder builder, CarInspector __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;

        if (!plugin.IsEnabled)
        {
            return true;
        }

        BaseLocomotive _car = (BaseLocomotive)(typeof(CarInspector).GetField("_car", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance));
        MethodInfo buildContextOrders = (typeof(CarInspector).GetMethod("BuildContextualOrders", BindingFlags.Instance | BindingFlags.NonPublic));

        /* 
         * Original Railroader logic unless otherwise noted
         */
        builder.FieldLabelWidth = 100f;
        builder.Spacing = 8f;
        AutoEngineerPersistence persistence = new AutoEngineerPersistence(_car.KeyValueObject);
        AutoEngineerOrdersHelper helper = new AutoEngineerOrdersHelper(_car as BaseLocomotive, persistence);
        AutoEngineerMode mode2 = helper.Mode();
        builder.AddObserver(persistence.ObserveOrders(delegate
        {
            if (helper.Mode() != mode2)
            {
                builder.Rebuild();
            }
        }, callInitial: false));
        builder.AddField("Mode", builder.ButtonStrip(delegate (UIPanelBuilder builder)
        {
            builder.AddButtonSelectable("Manual", mode2 == AutoEngineerMode.Off, delegate
            {
                SetOrdersValue(AutoEngineerMode.Off);
            });
            builder.AddButtonSelectable("Road", mode2 == AutoEngineerMode.Road, delegate
            {
                SetOrdersValue(AutoEngineerMode.Road);
            });
            builder.AddButtonSelectable("Yard", mode2 == AutoEngineerMode.Yard, delegate
            {
                SetOrdersValue(AutoEngineerMode.Yard);
            });
        }));
        if (!persistence.Orders.Enabled)
        {
            builder.AddExpandingVerticalSpacer();
            return false;
        }
        builder.AddField("Direction", builder.ButtonStrip(delegate (UIPanelBuilder builder)
        {
            builder.AddObserver(persistence.ObserveOrders(delegate
            {
                builder.Rebuild();
            }, callInitial: false));
            builder.AddButtonSelectable("Reverse", !persistence.Orders.Forward, delegate
            {
                bool? forward3 = false;
                SetOrdersValue(null, forward3);
            });
            builder.AddButtonSelectable("Forward", persistence.Orders.Forward, delegate
            {
                bool? forward2 = true;
                SetOrdersValue(null, forward2);
            });
        }));
        if (mode2 == AutoEngineerMode.Road)
        {
            int num = mode2.MaxSpeedMph();
            RectTransform control = builder.AddSliderQuantized(() => persistence.Orders.MaxSpeedMph, delegate
            {
                int maxSpeedMph3 = persistence.Orders.MaxSpeedMph;
                return maxSpeedMph3.ToString();
            }, delegate (float value)
            {
                int? maxSpeedMph2 = (int)value;
                SetOrdersValue(null, null, maxSpeedMph2);
            }, 5f, 0f, num);
            builder.AddField("Max Speed", control);
        }
        if (mode2 == AutoEngineerMode.Yard)
        {
            RectTransform control2 = builder.ButtonStrip(delegate (UIPanelBuilder builder)
            {
                builder.AddButton("Stop", delegate
                {
                    float? distance8 = 0f;
                    SetOrdersValue(null, null, null, distance8);
                });
                builder.AddButton("½", delegate
                {
                    float? distance7 = 6.1f;
                    SetOrdersValue(null, null, null, distance7);
                });
                builder.AddButton("1", delegate
                {
                    float? distance6 = 12.2f;
                    SetOrdersValue(null, null, null, distance6);
                });
                builder.AddButton("2", delegate
                {
                    float? distance5 = 24.4f;
                    SetOrdersValue(null, null, null, distance5);
                });
                builder.AddButton("5", delegate
                {
                    float? distance4 = 61f;
                    SetOrdersValue(null, null, null, distance4);
                });
                builder.AddButton("10", delegate
                {
                    float? distance3 = 122f;
                    SetOrdersValue(null, null, null, distance3);
                });
                builder.AddButton("20", delegate
                {
                    float? distance2 = 244f;
                    SetOrdersValue(null, null, null, distance2);
                });
            }, 4);
            builder.AddField("Car Lengths", control2);
        }
        builder.AddObserver(persistence.ObservePassengerModeStatusChanged(delegate
        {
            builder.Rebuild();
        }));
        string passengerModeStatus = persistence.PassengerModeStatus;
        if (mode2 == AutoEngineerMode.Road && !string.IsNullOrEmpty(passengerModeStatus))
        {
            builder.AddField("Station Stops", passengerModeStatus).Tooltip("AI Passenger Stops", "When stations are checked on passenger cars in the train, the AI engineer will perform stops as those stations are encountered.");

            /* 
             * Begin custom logic
             */
            builder.HStack(delegate (UIPanelBuilder builder)
            {

                builder.AddButton("PassengerSettings", delegate
                {
                    PassengerSettingsWindow.Show(_car);
                }).Tooltip("Open Passeneger Settings menu", "Open Passeneger Settings menu");
                builder.AddObserver(persistence.ObservePassengerModeStatusChanged(delegate
                {
                    builder.Rebuild();
                }));
                if (plugin._locomotives.TryGetValue(_car, out var locomotive) && locomotive.CurrentlyStopped)
                {
                    builder.AddButton("Continue", delegate
                    {
                        locomotive.Continue = true;
                    }).Tooltip("Resume travel", "Resume travel");
                }
            });
            /* 
             * End custom logic
             */
        }
        builder.AddObserver(persistence.ObservePlannerStatusChanged(delegate
        {
            builder.Rebuild();
        }));
        builder.AddField("Status", persistence.PlannerStatus);
        buildContextOrders.Invoke(__instance, new object[] { builder, persistence });
        void SetOrdersValue(AutoEngineerMode? mode = null, bool? forward = null, int? maxSpeedMph = null, float? distance = null)
        {
            helper.SetOrdersValue(mode, forward, maxSpeedMph, distance);
        }

        builder.AddExpandingVerticalSpacer();
        return false;

    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "PopulateAIPanel")]
    [HarmonyAfter(new string[] { "FlyShuntUI", "SmartOrders" })]
    private static void PopulateAIPanel(Window ____window)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;

        if (!plugin.IsEnabled)
        {
            return;
        }

        ____window.SetResizable(new Vector2(400, 350), new Vector2(400, 515));
    }
}
