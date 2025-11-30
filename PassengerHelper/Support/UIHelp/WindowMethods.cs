namespace PassengerHelper.Support.UIHelp;


using System;
using System.Reflection;
using HarmonyLib;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;


internal class WindowMethods
{
    private static ProgrammaticWindowCreator _programmaticWindowCreator;
    private static ProgrammaticWindowCreator ProgrammaticWindowCreator
    {
        get
        {
            if (_programmaticWindowCreator == null)
            {
                _programmaticWindowCreator = GameObject.FindObjectOfType<ProgrammaticWindowCreator>(true);
            }
            return _programmaticWindowCreator;
        }
    }

    private static Func<ProgrammaticWindowCreator, string, int, int, Window.Position, object, Window>? createWindowLambda;

    private static void AssureLambda()
    {
        if (createWindowLambda != null)
        {
            return;
        }
        MethodInfo method = AccessTools.Method(typeof(ProgrammaticWindowCreator), "CreateWindow", new Type[3]
        {
            typeof(int),
            typeof(int),
            typeof(Window.Position)
        });
        if (method != null)
        {
            createWindowLambda = (ProgrammaticWindowCreator pwc, string id, int w, int h, Window.Position p, object s) => (Window)method.Invoke(pwc, new object[3] { w, h, p });
            return;
        }
        method = AccessTools.Method(typeof(ProgrammaticWindowCreator), "CreateWindow", Array.Empty<Type>());
        if (method != null)
        {
            MethodInfo setInitialPosAndSize = AccessTools.Method("UI.Common.WindowPersistence:SetInitialPositionSize");
            createWindowLambda = delegate (ProgrammaticWindowCreator pwc, string id, int w, int h, Window.Position p, object s)
            {
                Window window = (Window)method.Invoke(pwc, Array.Empty<object>());
                setInitialPosAndSize.Invoke(null, new object[5]
                {
                    window,
                    id,
                    new Vector2(w, h),
                    p,
                    s
                });
                return window;
            };
            return;
        }
        throw new NotSupportedException("Cannot find fitting CreateWindow method");
    }








    internal static Window CreateWindowLegacy(ProgrammaticWindowCreator __instance, string identifier, int width, int height, Window.Position position, object sizing)
    {
        if (__instance == null)
        {
            throw new ArgumentException("Could not find ProgrammaticWindowCreator; did you try to create a window when that prefab isn't loaded yet?");
        }
        AssureLambda();
        return createWindowLambda(__instance, identifier, width, height, position, sizing);
    }
}

