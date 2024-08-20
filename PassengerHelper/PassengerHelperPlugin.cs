using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.Progression;
using HarmonyLib;
using Model;
using PassengerHelperPlugin.Support;
using Railloader;
using RollingStock;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UI.Builder;
using UI.Console;

namespace PassengerHelperPlugin
{
    public class PassengerHelperPlugin : SingletonPluginBase<PassengerHelperPlugin>
    {
        static ILogger logger = Log.ForContext(typeof(PassengerHelperPlugin));

        private readonly IModdingContext ctx;
        private readonly IModDefinition self;
        internal IUIHelper UIHelper { get; }
        internal Dictionary<string, PassengerLocomotiveSettings> passengerLocomotivesSettings { get; }
        public PassengerHelperPlugin(IModdingContext ctx, IModDefinition self, IUIHelper uiHelper)
        {
            new Harmony(self.Id).PatchAll(GetType().Assembly);
            passengerLocomotivesSettings = ctx.LoadSettingsData<Dictionary<string, PassengerLocomotiveSettings>>(self.Id) ?? new Dictionary<string, PassengerLocomotiveSettings>();
            this.self = self;
            this.ctx = ctx;
            UIHelper = uiHelper;
        }

        public void SaveSettings()
        {
            ctx.SaveSettingsData(self.Id, passengerLocomotivesSettings);
        }

    }
}
