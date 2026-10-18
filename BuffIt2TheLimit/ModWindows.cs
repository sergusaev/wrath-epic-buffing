using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ModWindows
{
    // Types of the game by name, looked up in its own assembly: AccessTools.TypeByName walks every type of every
    // assembly and took seconds per call, so the game started a minute late with this file in every mod.
    static class GameTypes
    {
        static readonly Dictionary<string, Type> s_Found = new Dictionary<string, Type>();

        public static Type Get(string name)
        {
            Type type;
            if (!s_Found.TryGetValue(name, out type))
            {
                type = typeof(Kingmaker.Game).Assembly.GetType(name, false);
                s_Found[name] = type;
            }
            return type;
        }
    }

    // The windows the mods of this collection open in the game's interface. While one is open, the whole input is its:
    // the camera does not scroll or zoom behind it, the other mods do not open their windows over it,
    // and their alerts wait until it closes. Every mod carries this file; the list of open windows lives in the
    // application domain, so the mods see each other's windows whichever copy of the file asks.
    static class ModWindow
    {
        // Keep the key and the type of the list as they are: copies of this file in older and newer mods share it.
        const string Key = "PathfinderMods.ModWindows.Open";
        static List<KeyValuePair<string, GameObject>> s_Own;

        static List<KeyValuePair<string, GameObject>> Open
        {
            get
            {
                var data = AppDomain.CurrentDomain.GetData(Key);
                var list = data as List<KeyValuePair<string, GameObject>>;
                if (list == null)
                {
                    // Another kind of list under the key (a changed copy of this file): not overwritten, this mod
                    // keeps its own and only loses sight of the other mods' windows.
                    if (data != null) list = s_Own ?? (s_Own = new List<KeyValuePair<string, GameObject>>());
                    else
                    {
                        list = new List<KeyValuePair<string, GameObject>>();
                        AppDomain.CurrentDomain.SetData(Key, list);
                    }
                }
                // A window destroyed without a word (a scene change) is closed.
                list.RemoveAll(w => w.Value == null);
                return list;
            }
        }

        // The window of a mod is up; mod is its Id.
        public static void Opened(string mod, GameObject window)
        {
            var list = Open;
            if (!list.Exists(w => w.Value == window)) list.Add(new KeyValuePair<string, GameObject>(mod, window));
        }

        public static void Closed(GameObject window)
        {
            Open.RemoveAll(w => w.Value == window);
        }

        public static bool AnyOpen
        {
            get { return Open.Count > 0; }
        }

        // A window of another mod is open: this one keeps its window and alerts to itself.
        public static bool OtherOpen(string mod)
        {
            return Open.Exists(w => w.Key != mod);
        }
    }

    // The camera scrolls by the keys and the screen edges and zooms by the wheel past the game's keyboard lock.
    // The names are the same in Kingmaker and Wrath of the Righteous.
    [HarmonyPatch]
    static class CameraScrollLock
    {
        static MethodBase TargetMethod()
        {
            var type = GameTypes.Get("Kingmaker.View.CameraRig");
            return type != null ? AccessTools.Method(type, "TickScroll") : null;
        }

        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static bool Prefix()
        {
            return !ModWindow.AnyOpen;
        }
    }

    // The console interface of both games keeps its base layer (the local map's or the global map's) bound under every
    // pushed layer: the triggers would still open the party list and the card wheel, toggle pause and the action bar
    // over a window. They do nothing while one is open; Start still opens the game's menu, as over the game's own
    // windows. Only what shows is stopped: a hide (false, 0) goes through, so nothing opened before stays stuck.
    // Three patch classes instead of one prefix with object[] __args: the Harmony that WotR ships (2.0.4) cannot
    // inject __args, and with some loaders it is the one in use. Arguments are bound by name, which every version knows.
    static class BaseLayerLock
    {
        static readonly string[][] Targets = {
            // Kingmaker
            new[] { "Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayerVM",
                "OnShowMenu", "OnShowInGameGroupManager", "PauseTrigger", "SetActionBarActive", "ChangeUnitColiderIgnore" },
            new[] { "Kingmaker.UI._ConsoleUI.GlobalMap.GlobalMapUiStaticPartVM", "ShowMenu", "ShowGroupManager" },
            new[] { "Kingmaker.UI._ConsoleUI.GlobalMap.GlobalMapUiStaticPartView", "OnFuncAdditional" },
            // Wrath of the Righteous
            new[] { "Kingmaker.UI.MVVM._ConsoleView.InGame.InGameConsoleView", "OnShowMenu", "OnLeftBottomPressed" },
            new[] { "Kingmaker.UI.MVVM._ConsoleView.GlobalMap.GlobalMapConsoleView", "ShowCards" },
        };

        // The target methods whose first argument is "value" of this type; null = all the others.
        internal static List<MethodBase> Methods(Type valueType)
        {
            var methods = new List<MethodBase>();
            foreach (var target in Targets)
            {
                var type = GameTypes.Get(target[0]);
                if (type == null) continue;
                for (int i = 1; i < target.Length; i++)
                {
                    var method = AccessTools.Method(type, target[i]);
                    if (method == null) continue;
                    var parameters = method.GetParameters();
                    Type own = parameters.Length > 0 && parameters[0].Name == "value"
                        && (parameters[0].ParameterType == typeof(bool) || parameters[0].ParameterType == typeof(float))
                        ? parameters[0].ParameterType : null;
                    if (own == valueType) methods.Add(method);
                }
            }
            return methods;
        }
    }

    [HarmonyPatch]
    static class BaseLayerLockBool
    {
        static IEnumerable<MethodBase> TargetMethods() { return BaseLayerLock.Methods(typeof(bool)); }
        static bool Prepare() { return BaseLayerLock.Methods(typeof(bool)).Count > 0; }
        static bool Prefix(bool value) { return !ModWindow.AnyOpen || !value; }
    }

    [HarmonyPatch]
    static class BaseLayerLockFloat
    {
        static IEnumerable<MethodBase> TargetMethods() { return BaseLayerLock.Methods(typeof(float)); }
        static bool Prepare() { return BaseLayerLock.Methods(typeof(float)).Count > 0; }
        static bool Prefix(float value) { return !ModWindow.AnyOpen || value == 0f; }
    }

    [HarmonyPatch]
    static class BaseLayerLockOther
    {
        static IEnumerable<MethodBase> TargetMethods() { return BaseLayerLock.Methods(null); }
        static bool Prepare() { return BaseLayerLock.Methods(null).Count > 0; }
        static bool Prefix() { return !ModWindow.AnyOpen; }
    }

    [HarmonyPatch]
    static class CameraZoomLock
    {
        static MethodBase TargetMethod()
        {
            var type = GameTypes.Get("Kingmaker.View.CameraZoom");
            return type != null ? AccessTools.Method(type, "TickZoom") : null;
        }

        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static bool Prefix()
        {
            return !ModWindow.AnyOpen;
        }
    }
}
