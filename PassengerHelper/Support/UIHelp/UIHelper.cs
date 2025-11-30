namespace PassengerHelper.Support.UIHelp;

using System;
using System.Diagnostics;
using HarmonyLib;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;

public class UIHelper
{
    private static readonly Version newWindowVersion = new Version(2024, 6, 3);

    internal static bool CanCreateWindow => UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(includeInactive: true) != null;

    internal static Window CreateWindowInternal<TWindow>(string identifier, int width, int height, Window.Position position, object sizing)
    {
        return WindowMethods.CreateWindowLegacy(UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(includeInactive: true), identifier, width, height, position, sizing);
    }

    private static object GetSizing(string methodName, Vector2Int size)
    {
        Traverse traverse = Traverse.Create<Window>().Type("Sizing");
        if (!traverse.TypeExists())
        {
#pragma warning disable CS8603 // Possible null reference return.
            return null;
#pragma warning restore CS8603 // Possible null reference return.
        }
        traverse = traverse.Method(methodName, size);
        if (!traverse.MethodExists())
        {
#pragma warning disable CS8603 // Possible null reference return.
            return null;
#pragma warning restore CS8603 // Possible null reference return.
        }
        return traverse.GetValue<object>(new object[1] { size });
    }

    internal static object Fixed(Vector2Int size)
    {
        return GetSizing("Fixed", size);
    }

    internal static object Resizable(Vector2Int size)
    {
        return GetSizing("Resizable", size);
    }

    internal static UIPanel PopulateWindowInternal(Window window, Action<UIPanelBuilder> closure)
    {
        return UIPanel.Create(window.contentRectTransform, UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(includeInactive: true).builderAssets, closure);
    }

    public Window CreateWindow(string identifier, int width, int height, Window.Position position)
    {
        return CreateWindowInternal<DummyWindow>(identifier, width, height, position, Fixed(new Vector2Int(width, height)));
    }

    public UIPanel PopulateWindow(Window window, Action<UIPanelBuilder> closure)
    {
        return PopulateWindowInternal(window, closure);
    }
}
