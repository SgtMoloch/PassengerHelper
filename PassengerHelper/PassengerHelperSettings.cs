using UnityEngine;
using UnityModManagerNet;

namespace PassengerHelper.Plugin;

public class PassengerHelperSettings : UnityModManager.ModSettings, IDrawable
{
    [Header("Logging")]
    [Draw("Verbose Logging")] public bool VerboseLogging = false;
    
    public override void Save(UnityModManager.ModEntry modEntry)
    {
        Save(this, modEntry);
    }
    public void OnChange()
    {
    }
}
