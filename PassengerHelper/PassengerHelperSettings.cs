using UnityEngine;
using UnityModManagerNet;

namespace PassengerHelper.Plugin;

public class PassengerHelperSettings : UnityModManager.ModSettings, IDrawable
{
    [Header("Logging")]
    [Draw("Verbose Logging")] public bool VerboseLogging = false;

    [Header("Gameplay")]
    [Draw("Load Pax When no Engine is coupled")] public bool LoadWhenNoEngine = false;
    
    public override void Save(UnityModManager.ModEntry modEntry)
    {
        Save(this, modEntry);
    }
    public void OnChange()
    {
    }
}
