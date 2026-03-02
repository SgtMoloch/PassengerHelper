using UI;
using UI.Builder;
using UnityEngine;

namespace PassengerHelper.Support.UIHelp;

internal class DummyWindow : MonoBehaviour, IBuilderWindow
{
    public UIBuilderAssets BuilderAssets { get; set; }
}