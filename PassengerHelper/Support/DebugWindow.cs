using PassengerHelper.Managers;
using PassengerHelper.Support.UIHelp;
using PassengerHelper.Plugin;
using UI.Common;
using System;
using UI.Builder;
using KeyValue.Runtime;

namespace PassengerHelper.Support;

public class DebugWindow
{
    internal SettingsManager settingsManager;
    internal TrainStateManager trainStateManager;

    private UIHelper uIHelper;

    internal DebugWindow(UIHelper uIHelper, PassengerHelperPlugin plugin)
    {
        this.uIHelper = uIHelper;
        this.settingsManager = plugin.settingsManager;
        this.trainStateManager = plugin.trainStateManager;
    }

    internal void PopulateAndShowDebugWindow(Window debugWindow, PassengerLocomotive pl)
    {
        uIHelper.PopulateWindow(debugWindow, (Action<UIPanelBuilder>)delegate (UIPanelBuilder builder)
        {
            Loader.Log($"Populating debug for {pl._locomotive.DisplayName}");
            builder.VScrollView(delegate (UIPanelBuilder vBuilder)
            {
                PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                TrainState state = trainStateManager.GetState(pl);

                vBuilder.AddSection("Locomotive Info");
                vBuilder.AddField("Locomotive:", $"{pl._locomotive.DisplayName}");
                vBuilder.AddField("LocomotiveId:", $"{pl._locomotive.id}");
                vBuilder.AddField("Locomotive Settings Hash:", $"{pl.settingsHash}");
                vBuilder.AddField("Current Settings Hash:", $"{pls.getSettingsHash()}");
                vBuilder.AddField("Locomotive Station Settings Hash:", $"{pl.stationSettingsHash}");
                vBuilder.AddField("Current Station Settings Hash:", $"{pls.getStationSettingsHash()}");
                vBuilder.AddField("Locomotive State Hash:", $"{pl.stateHash}");
                vBuilder.AddField("Current State Hash:", $"{state.GetHashCode()}");

                vBuilder.AddSection("Current Station Info");
                vBuilder.AddField("At Station:", $"{TFToYN(state.CurrentStation != null)}");
                vBuilder.AddField("CurrentStation:", $"{state.CurrentStation?.DisplayName}");
                vBuilder.AddField("CurrentStationId:", $"{state.CurrentStationId}");
                vBuilder.AddField("PreviousStation:", $"{state.PreviousStation?.DisplayName}");
                vBuilder.AddField("PreviousStationId:", $"{state.PreviousStationId}");

                vBuilder.AddSection("Direction of Travel Info");
                EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
                vBuilder.AddField("UserDOT:", $"{pls.UserDirectionOfTravel}");
                vBuilder.AddField("InferredDOT:", $"{state.InferredDirectionOfTravel}");
                vBuilder.AddField("EffectiveDOT:", $"{effectiveDOT.Value}");

                vBuilder.AddSection("Train State");
                vBuilder.AddField("Disabled:", $"{TFToYN(pls.Disable)}");
                vBuilder.AddField("Currently Stopped:", $"{TFToYN(state.CurrentlyStopped)} for reason: {state.CurrentReasonForStop}");
                vBuilder.AddField("Arrived:", $"{TFToYN(state.Arrived)}");
                vBuilder.AddField("AtTerminusStationEast:", $"{TFToYN(state.AtTerminusStationEast)}");
                vBuilder.AddField("AtTerminusStationWest:", $"{TFToYN(state.AtTerminusStationWest)}");
                vBuilder.AddField("AtAlarka:", $"{TFToYN(state.AtAlarka)}");
                vBuilder.AddField("AtCochran:", $"{TFToYN(state.AtCochran)}");
                vBuilder.AddField("TerminusStationProcedureComplete:", $"{TFToYN(state.TerminusStationProcedureComplete)}");
                vBuilder.AddField("NonTerminusStationProcedureComplete:", $"{TFToYN(state.NonTerminusStationProcedureComplete)}");
                vBuilder.AddField("StoppedUnknownDirection:", $"{TFToYN(state.StoppedUnknownDirection)}");
                vBuilder.AddField("StoppedUnsupportedStation:", $"{TFToYN(state.StoppedUnsupportedStation)}");
                vBuilder.AddField("StoppedInsufficientTerminusStations:", $"{TFToYN(state.StoppedInsufficientTerminusStations)}");
                vBuilder.AddField("StoppedInsufficientStopAtStations:", $"{TFToYN(state.StoppedInsufficientStopAtStations)}");
                vBuilder.AddField("StoppedForDiesel:", $"{TFToYN(state.StoppedForDiesel)}");
                vBuilder.AddField("StoppedForCoal:", $"{TFToYN(state.StoppedForCoal)}");
                vBuilder.AddField("StoppedForWater:", $"{TFToYN(state.StoppedForWater)}");
                vBuilder.AddField("StoppedNextStation:", $"{TFToYN(state.StoppedNextStation)}");
                vBuilder.AddField("StoppedTerminusStation:", $"{TFToYN(state.StoppedTerminusStation)}");
                vBuilder.AddField("StoppedStationPause:", $"{TFToYN(state.StoppedStationPause)}");
                vBuilder.AddField("StoppedWaitForFullLoad:", $"{TFToYN(state.StoppedWaitForFullLoad)}");
                vBuilder.AddField("ReadyToDepart:", $"{TFToYN(state.ReadyToDepart)}");
                vBuilder.AddField("Departed:", $"{TFToYN(state.Departed)}");
                vBuilder.AddField("StopOverrideActive:", $"{TFToYN(state.StopOverrideActive)}");
                vBuilder.AddField("StopOverrideStationId:", $"{state.StopOverrideStationId}");

                vBuilder.AddSection("Runtime");
                vBuilder.AddField("PH Runtime Active:", $"{TFToYN(Loader.PassengerHelper.runtime.IsRunning)}");
                vBuilder.AddField("PH Runtime Interval:", $"{Loader.PassengerHelper.runtime.IntervalSeconds}s");


                vBuilder.RebuildOnInterval(1f);

            });
        });

        debugWindow.ShowWindow();
    }

    private string TFToYN(bool val)
    {
        return val ? "yes" : "no";
    }
}