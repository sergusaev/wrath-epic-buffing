using System;
using System.Linq;
using System.Text.RegularExpressions;
using Kingmaker.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // Look of the mod's own screens: the game's paper sheets, its book font and its red initial letters.
    // Kingmaker: the parchment of the HUD tooltips; WotR: the paper of its modal windows and tooltips.
    // Without the game's sprites the screens stay dark, with the light palette.
    internal static class PadTheme {

#if KINGMAKER
        private const string SeparatorPrefix = "Separator_";
        private const string PanelSpriteName = "dialogue_backsheet";
        private const string TipSpriteName = "dialogue_backsheet";
        // Inner margins that keep the text off the torn edges of the sheet.
        public static readonly RectOffset PanelPadding = new(58, 58, 48, 44);
        public static readonly RectOffset TipPadding = new(30, 30, 24, 22);
        private static readonly Color PaperText = new(0.17f, 0.09f, 0.05f);
        private static readonly Color PaperTitle = new(0.16f, 0.07f, 0.04f);
        private static readonly Color PaperDim = new(0.36f, 0.25f, 0.16f);
        private const string PaperAccent = "#8C2F14";
        private const string PaperSub = "#5C2A10";
        private const string PaperGood = "#2F6420";
        private const string PaperBad = "#A3241A";
        private const string PaperPartial = "#8A5408";
        private const string PaperMuted = "#6E5743";
        private const string PaperSoft = "#5A4433";
        private const string PaperFaint = "#9C8670";
        private const string PaperNote = "#2E4A66";
#else
        private const string SeparatorPrefix = "UI_Separator_";
        private const string PanelSpriteName = "UI_BackgroundModalWindow";
        private const string TipSpriteName = "UI_BackgroundTooltipPaper";
        public static readonly RectOffset PanelPadding = new(56, 56, 46, 40);
        public static readonly RectOffset TipPadding = new(28, 28, 22, 20);
        private static readonly Color PaperText = new(0.15f, 0.12f, 0.1f);
        private static readonly Color PaperTitle = new(0.12f, 0.08f, 0.06f);
        private static readonly Color PaperDim = new(0.34f, 0.29f, 0.25f);
        private const string PaperAccent = "#86301A";
        private const string PaperSub = "#55301C";
        private const string PaperGood = "#2E5E24";
        private const string PaperBad = "#A0281E";
        private const string PaperPartial = "#8A5408";
        private const string PaperMuted = "#6A5E54";
        private const string PaperSoft = "#54483F";
        private const string PaperFaint = "#9A8E84";
        private const string PaperNote = "#2C4862";
#endif
        private const string FontName = "NexusSerif-Regular SDF";

        private static bool resolved;
        private static Sprite panelSprite;
        private static Sprite tipSprite;
        private static TMP_FontAsset font;
        // left, middle, right of the ornamental rule under tooltip titles, at half size.
        private static Sprite[] separator;

        public const float SeparatorHeight = 12f;

        // The game's paper is found: dark text on light paper.
        public static bool Paper {
            get {
                Resolve();
                return panelSprite != null;
            }
        }

        public static TMP_FontAsset Font {
            get {
                Resolve();
                return font;
            }
        }

        public static Color Text => Paper ? PaperText : new Color(0.92f, 0.9f, 0.85f);
        public static Color Title => Paper ? PaperTitle : new Color(0.93f, 0.8f, 0.5f);
        public static Color Dim => Paper ? PaperDim : new Color(0.65f, 0.62f, 0.56f);
        public static Color Row => Paper ? new Color(0.36f, 0.22f, 0.1f, 0.07f) : new Color(1f, 1f, 1f, 0.04f);
        public static Color RowOutside => Paper ? new Color(0.36f, 0.22f, 0.1f, 0.025f) : new Color(1f, 1f, 1f, 0.015f);
        public static Color RowSelected => Paper ? new Color(0.55f, 0.3f, 0.1f, 0.3f) : new Color(0.62f, 0.47f, 0.2f, 0.55f);
        // Under the mouse pointer, laid over the row's own colour.
        public static Color RowHover => Paper ? new Color(0.55f, 0.3f, 0.1f, 0.14f) : new Color(0.93f, 0.8f, 0.5f, 0.14f);
        public static Color Chip => Paper ? new Color(0.36f, 0.22f, 0.1f, 0.1f) : new Color(1f, 1f, 1f, 0.06f);
        public static Color ChipWanted => Paper ? new Color(0.3f, 0.5f, 0.2f, 0.45f) : new Color(0.3f, 0.55f, 0.28f, 0.75f);
        public static Color ChipCursor => Paper ? new Color(0.62f, 0.36f, 0.12f, 0.5f) : new Color(0.85f, 0.66f, 0.28f, 0.9f);
        public static Color ChipCursorWanted => Paper ? new Color(0.4f, 0.55f, 0.18f, 0.65f) : new Color(0.55f, 0.75f, 0.3f, 0.95f);
        public static Color Box => Paper ? new Color(0.3f, 0.18f, 0.09f, 0.9f) : new Color(0.85f, 0.8f, 0.7f, 0.85f);
        public static Color BoxInner => Paper ? new Color(0.96f, 0.91f, 0.8f, 1f) : new Color(0.08f, 0.07f, 0.06f, 1f);
        public static Color Tick => Paper ? new Color(0.22f, 0.5f, 0.16f, 1f) : new Color(0.5f, 0.82f, 0.4f, 1f);
        public static Color Key => Paper ? new Color(0.36f, 0.22f, 0.1f, 0.1f) : new Color(1f, 1f, 1f, 0.07f);
        public static Color Line => Paper ? new Color(0.3f, 0.18f, 0.08f, 0.3f) : new Color(1f, 1f, 1f, 0.12f);
        public static Color Track => Paper ? new Color(0.3f, 0.18f, 0.08f, 0.12f) : new Color(1f, 1f, 1f, 0.08f);
        public static Color Thumb => Paper ? new Color(0.5f, 0.24f, 0.1f, 0.7f) : new Color(0.93f, 0.8f, 0.5f, 0.7f);

        // Rich-text colours.
        public static string Accent => Paper ? PaperAccent : "#E8C46A";
        public static string Heading => Paper ? "#" + ColorUtility.ToHtmlStringRGB(PaperTitle) : "#E8C46A";
        public static string Sub => Paper ? PaperSub : "#D8C08A";
        public static string Good => Paper ? PaperGood : "#9AD08A";
        public static string Bad => Paper ? PaperBad : "#E08A7A";
        // Some of the wanted buffs are missing.
        public static string Partial => Paper ? PaperPartial : "#E8C46A";
        public static string Muted => Paper ? PaperMuted : "#8A847A";
        public static string Soft => Paper ? PaperSoft : "#B8B0A0";
        public static string Off => Paper ? PaperMuted : "#B0A898";
        public static string Faint => Paper ? PaperFaint : "#6A655C";
        public static string Note => Paper ? PaperNote : "#9FC3D9";
        public static string Struck => Paper ? PaperFaint : "#7A746A";
        public static string Outside => Paper ? PaperSoft : "#A8A296";
        public static string Blocked => Paper ? "#9C6A5E" : "#7A5A55";

        // Background of a whole screen; returns false when it stays a flat dark panel.
        public static void Panel(Image image, float darkAlpha = 0.94f) => Sheet(image, panelSprite, darkAlpha);

        // Background of a small floating note.
        public static void Tip(Image image, float darkAlpha = 0.94f) => Sheet(image, tipSprite ?? panelSprite, darkAlpha);

        // The sheet is a child outside the layout: a sliced Image reports its borders as its minimum size,
        // which would hold a fitted note at the sprite's size whatever its text.
        private static void Sheet(Image image, Sprite sprite, float darkAlpha) {
            Resolve();
            if (sprite != null && panelSprite != null) {
                image.sprite = null;
                image.color = Color.clear;
                var go = new GameObject("Sheet", typeof(RectTransform));
                go.transform.SetParent(image.transform, false);
                go.transform.SetAsFirstSibling();
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                var sheet = go.AddComponent<Image>();
                sheet.sprite = sprite;
                sheet.type = Image.Type.Sliced;
                sheet.fillCenter = true;
                sheet.raycastTarget = false;
            } else {
                image.sprite = null;
                image.color = new Color(0.06f, 0.05f, 0.04f, darkAlpha);
            }
        }

        public static void Apply(TextMeshProUGUI text) {
            var asset = Font;
            if (asset != null)
                text.font = asset;
        }

        public static bool HasSeparator {
            get {
                Resolve();
                return Paper && separator != null;
            }
        }

        // The game's tooltip rule: curled ends, a line on each side and a dot in the middle, as wide as its parent
        // lets it be. Null without the game's sprites.
        public static RectTransform Separator(Transform parent) {
            if (!HasSeparator)
                return null;
            var go = new GameObject("Separator", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = SeparatorHeight;
            element.minHeight = SeparatorHeight;
            element.flexibleWidth = 1;
            for (int i = 0; i < 3; i++) {
                var part = new GameObject(i == 1 ? "Middle" : i == 0 ? "Left" : "Right", typeof(RectTransform));
                part.transform.SetParent(go.transform, false);
                var image = part.AddComponent<Image>();
                image.sprite = separator[i];
                image.type = i == 1 ? Image.Type.Simple : Image.Type.Sliced;
                image.raycastTarget = false;
                var size = part.AddComponent<LayoutElement>();
                if (i == 1) {
                    size.preferredWidth = size.minWidth = SeparatorHeight * 50f / 23f;
                } else {
                    size.flexibleWidth = 1;
                    size.minWidth = 32;
                }
            }
            return (RectTransform)go.transform;
        }

        // The rule's sprites are 23 px high; a copy at double pixel density keeps the sliced ends in proportion at half height.
        private static Sprite Half(Sprite sprite) {
            if (sprite == null)
                return null;
            try {
                return Sprite.Create(sprite.texture, sprite.textureRect, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit * 2f, 0,
                    SpriteMeshType.FullRect, sprite.border);
            } catch (Exception) {
                return sprite;
            }
        }

        private static readonly Regex LeadingTags = new(@"^(\s*(<[^>]*>\s*)*)");

        // The game's book initial: the first letter larger, in the Saber font and the paper red.
        // Leading rich-text tags stay in front; text that does not open with a letter (e.g. "10 min/level") is left as is:
        // initials are letters, and the Saber font has no digits.
        public static string Initial(string text) {
            if (string.IsNullOrEmpty(text) || !Paper)
                return text;
            int start = LeadingTags.Match(text).Length;
            if (start >= text.Length || !char.IsLetter(text[start]))
                return text;
            try {
                return text.Substring(0, start) + UIUtility.GetSaberBookFormat(text.Substring(start));
            } catch (Exception) {
                return text;
            }
        }

        private static void Resolve() {
            if (resolved)
                return;
            resolved = true;
            try {
                var sprites = UnityEngine.Resources.FindObjectsOfTypeAll<Sprite>();
                panelSprite = sprites.FirstOrDefault(s => s.name == PanelSpriteName) ?? FromBundles(PanelSpriteName);
                tipSprite = sprites.FirstOrDefault(s => s.name == TipSpriteName) ?? FromBundles(TipSpriteName);
                var parts = new[] { "left", "middle", "right" }
                    .Select(n => sprites.FirstOrDefault(s => s.name == SeparatorPrefix + n) ?? FromBundles(SeparatorPrefix + n))
                    .ToArray();
                if (parts.All(p => p != null))
                    separator = parts.Select(Half).ToArray();
                font = UnityEngine.Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == FontName)
                    ?? UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>().Select(t => t.font).FirstOrDefault(f => f != null);
                Main.Log($"[PAD] theme: panel={(panelSprite != null ? panelSprite.name : "none")}, tip={(tipSprite != null ? tipSprite.name : "none")}, separator={separator != null}, font={(font != null ? font.name : "none")}");
            } catch (Exception ex) {
                Main.Error(ex, "resolving the theme");
            }
        }

        // WotR keeps its UI sprites in the loaded "ui" asset bundle; they are not in memory until a view uses them.
        private static Sprite FromBundles(string name) {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles()) {
                try {
                    if (bundle.isStreamedSceneAssetBundle)
                        continue;
                    var sprite = bundle.LoadAsset<Sprite>(name);
                    if (sprite != null)
                        return sprite;
                } catch (Exception) { }
            }
            return null;
        }
    }
}
