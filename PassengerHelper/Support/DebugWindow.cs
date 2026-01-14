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
                vBuilder.VStack(delegate (UIPanelBuilder vsBuilder)
                {
                    PassengerLocomotiveSettings pls = settingsManager.GetSettings(pl);
                    TrainState state = trainStateManager.GetState(pl);

                    vsBuilder.AddSection("Locomotive Info");
                    vsBuilder.AddField("Locomotive:", $"{pl._locomotive.DisplayName}");
                    vsBuilder.AddField("LocomotiveId:", $"{pl._locomotive.id}");
                    vsBuilder.AddField("Locomotive Settings Hash:", $"{pl.settingsHash}");
                    vsBuilder.AddField("Current Settings Hash:", $"{pls.getSettingsHash()}");
                    vsBuilder.AddField("Locomotive Station Settings Hash:", $"{pl.stationSettingsHash}");
                    vsBuilder.AddField("Current Station Settings Hash:", $"{pls.getStationSettingsHash()}");
                    vsBuilder.AddField("Locomotive State Hash:", $"{pl.stateHash}");
                    vsBuilder.AddField("Current State Hash:", $"{state.GetHashCode()}");

                    vsBuilder.AddSection("Current Station Info");
                    vsBuilder.AddField("At Station:", $"{TFToYN(state.CurrentStation != null)}");
                    vsBuilder.AddField("CurrentStation:", $"{state.CurrentStation?.DisplayName}");
                    vsBuilder.AddField("CurrentStationId:", $"{state.CurrentStationId}");
                    vsBuilder.AddField("PreviousStation:", $"{state.PreviousStation?.DisplayName}");
                    vsBuilder.AddField("PreviousStationId:", $"{state.PreviousStationId}");

                    vsBuilder.AddSection("Direction of Travel Info");
                    EffectiveDOT effectiveDOT = DirectionOfTravelResolver.Compute(pls.UserDirectionOfTravel, state.InferredDirectionOfTravel);
                    vsBuilder.AddField("UserDOT:", $"{pls.UserDirectionOfTravel}");
                    vsBuilder.AddField("InferredDOT:", $"{state.InferredDirectionOfTravel}");
                    vsBuilder.AddField("EffectiveDOT:", $"{effectiveDOT.Value}");

                    vsBuilder.AddSection("Train State");
                    vsBuilder.AddField("Disabled:", $"{TFToYN(pls.Disable)}");
                    vsBuilder.AddField("Currently Stopped:", $"{TFToYN(state.CurrentlyStopped)} for reason: {state.CurrentReasonForStop}");
                    vsBuilder.AddField("Arrived:", $"{TFToYN(state.Arrived)}");
                    vsBuilder.AddField("AtTerminusStationEast:", $"{TFToYN(state.AtTerminusStationEast)}");
                    vsBuilder.AddField("AtTerminusStationWest:", $"{TFToYN(state.AtTerminusStationWest)}");
                    vsBuilder.AddField("AtAlarka:", $"{TFToYN(state.AtAlarka)}");
                    vsBuilder.AddField("AtCochran:", $"{TFToYN(state.AtCochran)}");
                    vsBuilder.AddField("TerminusStationProcedureComplete:", $"{TFToYN(state.TerminusStationProcedureComplete)}");
                    vsBuilder.AddField("NonTerminusStationProcedureComplete:", $"{TFToYN(state.NonTerminusStationProcedureComplete)}");
                    vsBuilder.AddField("StoppedUnknownDirection:", $"{TFToYN(state.StoppedUnknownDirection)}");
                    vsBuilder.AddField("StoppedUnsupportedStation:", $"{TFToYN(state.StoppedUnsupportedStation)}");
                    vsBuilder.AddField("StoppedInsufficientTerminusStations:", $"{TFToYN(state.StoppedInsufficientTerminusStations)}");
                    vsBuilder.AddField("StoppedInsufficientStopAtStations:", $"{TFToYN(state.StoppedInsufficientStopAtStations)}");
                    vsBuilder.AddField("StoppedForDiesel:", $"{TFToYN(state.StoppedForDiesel)}");
                    vsBuilder.AddField("StoppedForCoal:", $"{TFToYN(state.StoppedForCoal)}");
                    vsBuilder.AddField("StoppedForWater:", $"{TFToYN(state.StoppedForWater)}");
                    vsBuilder.AddField("StoppedNextStation:", $"{TFToYN(state.StoppedNextStation)}");
                    vsBuilder.AddField("StoppedTerminusStation:", $"{TFToYN(state.StoppedTerminusStation)}");
                    vsBuilder.AddField("StoppedStationPause:", $"{TFToYN(state.StoppedStationPause)}");
                    vsBuilder.AddField("StoppedWaitForFullLoad:", $"{TFToYN(state.StoppedWaitForFullLoad)}");
                    vsBuilder.AddField("ReadyToDepart:", $"{TFToYN(state.ReadyToDepart)}");
                    vsBuilder.AddField("Departed:", $"{TFToYN(state.Departed)}");
                    vsBuilder.AddField("StopOverrideActive:", $"{TFToYN(state.StopOverrideActive)}");
                    vsBuilder.AddField("StopOverrideStationId:", $"{state.StopOverrideStationId}");

                    vsBuilder.AddSection("Runtime");
                    vsBuilder.AddField("PH Runtime Active:", $"{TFToYN(Loader.PassengerHelper.runtime.IsRunning)}");
                    vsBuilder.AddField("PH Runtime Interval:", $"{Loader.PassengerHelper.runtime.IntervalSeconds}s");

                    vsBuilder.RebuildOnInterval(1f);
                });
            });
        });

        debugWindow.ShowWindow();
    }

    private string TFToYN(bool val)
    {
        return val ? "yes" : "no";
    }
}