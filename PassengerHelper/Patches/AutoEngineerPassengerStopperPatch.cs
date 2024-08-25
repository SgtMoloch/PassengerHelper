namespace PassengerHelperPlugin.Patches;

using Game.State;
using HarmonyLib;
using Model;
using Model.AI;
using Model.OpsNew;
using Railloader;
using Serilog;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Model.Definition.Data;
using Model.Definition;
using System.Linq;
using Game.Messages;
using UI.EngineControls;
using UnityEngine;
using Network.Messages;
using Game;
using UI.Common;
using RollingStock;
using Game.Notices;
using Support;
using Network;
using System.Reflection.Emit;

[HarmonyPatch]
public static class AutoEngineerPassengerStopperPatches
{
    static readonly Serilog.ILogger logger = Log.ForContext(typeof(AutoEngineerPassengerStopperPatches));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "ShouldStayStopped")]
    private static bool ShouldStayStopped(ref bool __result, AutoEngineerPassengerStopper __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return true;
        }

        var _locomotive = typeof(AutoEngineerPassengerStopper).GetField("_locomotive", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as BaseLocomotive;
        var _currentStop = typeof(AutoEngineerPassengerStopper).GetField("_nextStop", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as PassengerStop;

        if (_locomotive == null || _currentStop == null)
        {
            return true;
        }

        if (plugin.stationManager.HandleTrainAtStation(_locomotive, _currentStop))
        {
            __result = true;
            return false;
        }

        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AutoEngineerPassengerStopper), "_UpdateFor")]
    private static void _UpdateFor(AutoEngineerPassengerStopper __instance)
    {
        PassengerHelperPlugin plugin = PassengerHelperPlugin.Shared;
        if (!plugin.IsEnabled)
        {
            return;
        }

        var _locomotive = typeof(AutoEngineerPassengerStopper).GetField("_locomotive", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as BaseLocomotive;

        if (_locomotive == null)
        {
            return;
        }

        if (!plugin._locomotives.TryGetValue(_locomotive, out PassengerLocomotive passengerLocomotive))
        {
            if (!plugin.passengerLocomotivesSettings.TryGetValue(_locomotive.DisplayName, out PassengerLocomotiveSettings _settings))
            {
                _settings = new PassengerLocomotiveSettings();
            }
            passengerLocomotive = new PassengerLocomotive(_locomotive, _settings);
            plugin._locomotives.Add(_locomotive, passengerLocomotive);
        }

        if (passengerLocomotive.ReadyToDepart && _locomotive.VelocityMphAbs > 1f)
        {

            logger.Information("Train {0} has departed {1} at {2}.", passengerLocomotive._locomotive.DisplayName, passengerLocomotive.CurrentStop.DisplayName, TimeWeather.Now);
            passengerLocomotive.Arrived = false;
            passengerLocomotive.ReadyToDepart = false;
            passengerLocomotive.Departed = true;
            passengerLocomotive.CurrentStop = null;
            passengerLocomotive.departureTime = TimeWeather.Now;
        }

    }
}
