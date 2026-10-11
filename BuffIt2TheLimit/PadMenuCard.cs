using BuffIt2TheLimit.Config;
using HarmonyLib;
using Kingmaker.UI.MVVM._ConsoleView.IngameMenu;
using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // The buff menu as a card of the gamepad menu, next to Formation and Rest. The card is a copy of the game's Rest card
    // with its own picture and title; it takes part in the carousel and in the navigation like the game's cards.
    // The game runs the action of the focused card when the menu closes; the buff menu opens after that.
    static class PadMenuCard {
        private const string CardName = "PadBuffs_MenuCard";
        private static Sprite big;
        private static bool dumped;
        private static IngameMenuCardsConsoleView pendingMenu;
        private static float pendingAt = -1;

        // The menu is bound: add the card before Rest and rebuild the carousel and the navigation with it.
        internal static void Bound(IngameMenuCardsConsoleView view) {
            var rest = view.m_RestView;
            if (rest == null)
                return;
            var card = rest.transform.parent.Find(CardName)?.GetComponent<InGameMenuCardEntityConsoleView>() ?? Create(rest);
            card.Initialize("pad.title".i8());
            card.Bind(() => Select(view), true);
            var list = view.m_CartCollection.m_CartList.OfType<InGameMenuCardEntityConsoleView>().Where(c => c != card).ToList();
            int index = list.IndexOf(rest);
            list.Insert(index < 0 ? list.Count : index, card);
            view.m_CartCollection.Initialize(list);
            view.m_NavigationBehaviour.SetEntitiesHorizontal(list);
            view.m_CartCollection.FocusOnFirstEntity();
            view.m_NavigationBehaviour.FocusOnFirstValidEntity();
        }

        private static InGameMenuCardEntityConsoleView Create(InGameMenuCardEntityConsoleView rest) {
            if (!dumped) {
                dumped = true;
                Main.Log("[PAD] menu card template:\n" + Dump(rest.transform, ""));
            }
            var go = UnityEngine.Object.Instantiate(rest.gameObject, rest.transform.parent, false);
            go.name = CardName;
            big ??= Load("BuffIt2TheLimit.card_buffs.png", new Color32(216, 176, 106, 255));
            // The Rest picture is the image with the rest or camp sprite; the probe log shows the rest of the card.
            foreach (var image in go.GetComponentsInChildren<Image>(true)) {
                string sprite = image.sprite != null ? image.sprite.name.ToLowerInvariant() : "";
                if (big != null && (sprite.Contains("rest") || sprite.Contains("camp"))) {
                    image.sprite = big;
                    image.preserveAspect = true;
                }
            }
            Main.Log("[PAD] menu card created");
            return go.GetComponent<InGameMenuCardEntityConsoleView>();
        }

        private static string Dump(Transform t, string indent) {
            var text = new StringBuilder();
            var rect = t as RectTransform;
            text.Append(indent).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            if (rect != null)
                text.Append(" ").Append(rect.rect.size);
            var image = t.GetComponent<Image>();
            if (image != null)
                text.Append(" image=").Append(image.sprite != null ? image.sprite.name : "-").Append(" color=").Append(ColorUtility.ToHtmlStringRGBA(image.color));
            var label = t.GetComponent<TextMeshProUGUI>();
            if (label != null)
                text.Append(" text=\"").Append(label.text).Append("\" font=").Append(label.font != null ? label.font.name : "-").Append(" size=").Append(label.fontSize);
            text.Append('\n');
            foreach (Transform child in t)
                text.Append(Dump(child, indent + "  "));
            return text.ToString();
        }

        // One colour with transparent cut-outs: the colour is spread over the transparent pixels so that the edges do not
        // turn dark when the picture is scaled down, and the texture gets mipmaps.
        private static Sprite Load(string resource, Color32 flat) {
            using var stream = typeof(PadMenuCard).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
                return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, true);
            texture.LoadImage(memory.ToArray());
            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i].r = flat.r;
                pixels[i].g = flat.g;
                pixels[i].b = flat.b;
            }
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 4;
            texture.Apply(true, false);
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private static void Select(IngameMenuCardsConsoleView view) {
            Main.Log("[PAD] menu card selected");
            pendingMenu = view;
            pendingAt = Time.unscaledTime;
        }

        // Called every frame: open the buff menu once the game's menu is gone.
        internal static void Tick() {
            if (pendingAt < 0)
                return;
            bool menuOpen = pendingMenu != null && pendingMenu.gameObject.activeInHierarchy;
            if (menuOpen && Time.unscaledTime - pendingAt < 1.5f)
                return;
            pendingAt = -1;
            pendingMenu = null;
            if (!PadQuickMenu.IsOpen)
                PadQuickMenu.Toggle();
        }
    }

    [HarmonyPatch(typeof(IngameMenuCardsConsoleView), nameof(IngameMenuCardsConsoleView.BindEntities))]
    static class PadMenuCardBound {
        static void Postfix(IngameMenuCardsConsoleView __instance) {
            try {
                PadMenuCard.Bound(__instance);
            } catch (Exception ex) {
                Main.Error(ex, "PadMenuCard.Bound");
            }
        }
    }
}
