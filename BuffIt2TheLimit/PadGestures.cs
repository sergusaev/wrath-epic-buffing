using BuffIt2TheLimit.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // Gestures on the menu key, in both control modes; they can be switched off in the menu settings.
    // Steam Input sends the key down on press and up on release, so timing is measured here instead
    // of with Steam activators (a Steam long press also fires the regular press, double press never arrived).
    //   tap         → open/close the menu (after the double-tap window)
    //   hold 0.6 s  → apply Long
    //   double tap  → apply Important
    internal static class PadGestures {

        private const float HoldTime = 0.6f;
        private const float DoubleTapWindow = 0.35f;

        private static float downAt = -1f;
        private static bool holdFired;
        private static bool tapPending;
        private static float tapDeadline;

        public static void Tick(ShortcutBinding key) {
            if (key.IsNone)
                return;
            // Without gestures the key only opens and closes the menu, at once.
            if (!PadSettings.Current.Gestures) {
                tapPending = false;
                downAt = -1f;
                if (key.IsPressed())
                    PadQuickMenu.Toggle();
                return;
            }
            float now = Time.unscaledTime;

            if (key.IsPressed()) {
                if (tapPending && now <= tapDeadline) {
                    tapPending = false;
                    downAt = -1f;
                    Apply(BuffGroup.Important, "double");
                    return;
                }
                downAt = now;
                holdFired = false;
            }

            if (downAt >= 0f) {
                if (!holdFired && Input.GetKey(key.Key) && now - downAt >= HoldTime) {
                    holdFired = true;
                    Apply(BuffGroup.Long, "hold");
                }
                if (Input.GetKeyUp(key.Key) || !Input.GetKey(key.Key)) {
                    if (!holdFired) {
                        tapPending = true;
                        tapDeadline = now + DoubleTapWindow;
                    }
                    downAt = -1f;
                }
            }

            if (tapPending && now > tapDeadline) {
                tapPending = false;
                PadQuickMenu.Toggle();
            }
        }

        // Gestures and the quick cast bar: the result shows in a pop-up as for a menu cast.
        internal static void Apply(BuffGroup group, string gesture) {
            Main.Log($"[PAD] {gesture} → {group}");
            int before = BuffExecutor.ScheduledRoutines;
            int finishedBefore = BuffExecutor.FinishedRoutines;
            try {
                GlobalBubbleBuffer.Execute(group);
            } catch (Exception ex) {
                Main.Error(ex, "PadGestures.Apply");
            }
            string name = PadQuickMenu.GroupName(group);
            if (BuffExecutor.ScheduledRoutines == before)
                PadToast.Show(string.Format("pad.blocked".i8(), name));
            else if (BuffExecutor.FinishedRoutines == finishedBefore)
                PadToast.Show(string.Format("pad.running".i8(), name), keepOpen: true);
            // otherwise the routine had nothing to cast and its result is already on screen
        }
    }

    // Small notice at the top of the screen for results of group casts started without the menu.
    internal class PadToast : MonoBehaviour {

        private const float ShowTime = 5f;

        private static PadToast instance;
        private TextMeshProUGUI text;
        private float hideAt;
        private bool keepOpen;

        public static void Show(string message, bool keepOpen = false) {
            try {
                if (instance == null)
                    instance = Build();
                instance.text.text = message;
                instance.keepOpen = keepOpen;
                instance.hideAt = Time.unscaledTime + (keepOpen ? 30f : ShowTime);
                instance.gameObject.SetActive(true);
            } catch (Exception ex) {
                Main.Error(ex, "PadToast.Show");
            }
        }

        // Shown for casts from the open menu too: the toast sits at the top edge, the menu above it in the middle.
        public static void ShowResult(string title, int applied, int attempted, int skipped, TooltipTemplateBuffer tooltip) {
            var lines = new List<string> { string.Format("pad.result".i8(), title, applied, attempted, skipped) };
            foreach (var bad in tooltip.Bad.Take(4)) {
                var reasons = string.Join("; ", bad.messages.Select(m => m.Trim()).Take(1));
                lines.Add($"<color={PadTheme.Bad}>{bad.buff.Name}</color> {reasons}");
            }
            Show(string.Join("\n", lines));
        }

        private void Update() {
            if (Time.unscaledTime >= hideAt)
                gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (instance == this)
                instance = null;
        }

        private static PadToast Build() {
            var font = PadTheme.Font;

            var overlay = new GameObject("BI2TL_PadToast", typeof(RectTransform));
            DontDestroyOnLoad(overlay);
            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31999;
            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(overlay.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0, -40);
            rect.sizeDelta = new Vector2(760, 0);
            PadTheme.Tip(panel.AddComponent<Image>(), 0.9f);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = PadTheme.Paper ? PadTheme.TipPadding : new RectOffset(20, 20, 12, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(panel.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.fontSize = 21;
            text.color = PadTheme.Text;
            text.richText = true;
            text.enableWordWrapping = true;

            var toast = overlay.AddComponent<PadToast>();
            toast.text = text;
            return toast;
        }
    }
}
