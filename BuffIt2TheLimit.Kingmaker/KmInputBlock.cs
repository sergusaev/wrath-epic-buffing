using HarmonyLib;
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
}
