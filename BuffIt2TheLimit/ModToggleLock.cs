using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace ModToggleLock
{
    // Mods are chosen in the main menu only. Unity Mod Manager switches a mod off at once only when the mod can undo
    // itself, otherwise at the next start; a mod switched in a game would leave the save half with it, half without.
    // In a game the checkboxes of the mod list are greyed out and, hovered, say why. Every mod of this collection
    // carries this patch: the first one loaded replaces the checkbox, the others find it already replaced.
    [HarmonyPatch]
    static class ModToggleLock
    {
        const string Hint = "Mods can be enabled or disabled only in the main menu.";
        static readonly MethodInfo s_Toggle = AccessTools.Method(typeof(GUILayout), "Toggle", new[] { typeof(bool), typeof(string), typeof(GUILayoutOption[]) });
        static readonly MethodInfo s_ShowTooltip = AccessTools.Method(typeof(UnityModManager.UI), "ShowTooltip", new[] { typeof(string) });
        static readonly FieldInfo s_Game = AccessTools.Field(typeof(Game), "s_Instance");
        static readonly object[] s_Hint = { Hint };

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(UnityModManager.UI), "DrawTab");
        }

        static bool Prepare()
        {
            return TargetMethod() != null && s_Toggle != null && s_Game != null;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var toggle = AccessTools.Method(typeof(ModToggleLock), "Toggle");
            foreach (var instruction in code)
            {
                if (instruction.Calls(s_Toggle))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = toggle;
                }
                yield return instruction;
            }
        }

        // The game is not running (Game.Instance would create it in WotR), no area has been loaded yet, or the main menu
        // scene is up (the game keeps the last area after a return to the main menu). The scene is named as in
        // Kingmaker.SceneName: MainMenu, the console interface of Kingmaker has its own.
        static bool Allowed
        {
            get
            {
                try
                {
                    var game = s_Game.GetValue(null) as Game;
                    return game == null || game.CurrentlyLoadedArea == null
                        || SceneManager.GetSceneByName("MainMenu").isLoaded || SceneManager.GetSceneByName("UI_MainMenu_Scene_Console").isLoaded;
                }
                catch (System.Exception)
                {
                    // The game half made: not in a game yet.
                    return true;
                }
            }
        }

        public static bool Toggle(bool value, string text, GUILayoutOption[] options)
        {
            if (Allowed) return GUILayout.Toggle(value, text, options);
            bool enabled = GUI.enabled;
            GUI.enabled = false;
            GUILayout.Toggle(value, text, options);
            GUI.enabled = enabled;
            var e = Event.current;
            if (e.type == EventType.Repaint && s_ShowTooltip != null && GUILayoutUtility.GetLastRect().Contains(e.mousePosition))
                s_ShowTooltip.Invoke(null, s_Hint);
            return value;
        }
    }
}
