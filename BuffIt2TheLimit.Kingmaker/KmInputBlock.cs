using HarmonyLib;
using Kingmaker.Assets.Console.GamepadInput;
using Kingmaker.UI._ConsoleUI;
using Kingmaker.View;

namespace BuffIt2TheLimit {

    // Kingmaker reads the camera keys straight from Input in CameraRig.TickScroll, past KeyboardAccess,
    // so WASD and the arrows would move the camera under the open menu.
    [HarmonyPatch(typeof(CameraRig), "AnyMoveCameraKeyIsDown", MethodType.Getter)]
    static class KmCameraKeysWhileMenuOpen {
        static void Postfix(ref bool __result) {
            if (PadQuickMenu.IsOpen)
                __result = false;
        }
    }

    // The console interface has a virtual mouse moved by the left stick (RewiredCursorController). It is on whenever the
    // game's base layer wants it (the global map, cursor mode), whatever layer is on top, and its hidden pointer lit up
    // rows of the menu: a second highlight, apart from the menu's own, that the left stick moved. Off while the menu is open.
    [HarmonyPatch(typeof(GamePad), "RestoreCursorState")]
    static class KmVirtualCursor {
        static void Postfix() {
            if (PadQuickMenu.IsOpen)
                Hide();
        }

        public static void Hide() {
            var cursor = RewiredCursorController.Instance;
            if (cursor == null)
                return;
            cursor.Enabled = false;
            if (cursor.Cursor != null)
                cursor.Cursor.SetActive(false);
        }

        public static void Restore() {
            if (GamePad.HasInstance && RewiredCursorController.Instance != null)
                GamePad.Instance.RestoreCursorState();
        }
    }
}
