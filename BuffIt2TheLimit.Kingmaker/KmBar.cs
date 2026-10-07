using BuffIt2TheLimit.Config;
using Kingmaker;
using Kingmaker.GameModes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // Quick cast bar of Kingmaker for mouse and keyboard: the icons of the WotR mod on the mod's own canvas,
    // one button per group shown in the menu, numbered as the menu numbers them, then a button that opens
    // the menu. Left click casts the group, right click opens its buffs in the menu.
    internal static class PadBar {
        public static void Refresh() => PadBarView.Rebuild();

        public static void UpdateInteractable() => PadBarView.UpdateInteractable();
    }

    internal class PadBarView : MonoBehaviour {

        private const float ButtonSize = 48f;
        private const float Margin = 8f;
        // Fallback when the game's menu buttons are not found: above two rows of 48px buttons.
        private static readonly Vector2 FallbackPos = new(12f, 132f);

        private static PadBarView instance;
        private static readonly Dictionary<string, Sprite> Sprites = new();

        private RectTransform row;
        private GameObject tipBox;
        private TextMeshProUGUI tipTitle;
        private TextMeshProUGUI tipText;
        private RectTransform tipList;
        private const float TipIconSize = 26f;
        private const int TipBuffs = 12;
        // The game's HUD canvas: the bar is laid into it just under the game's tooltips, so they cover it.
        private RectTransform host;
        private float nextHostSearch;
        private TMP_FontAsset font;
        private readonly List<(BuffGroup group, Button button)> buttons = new();
        private BufferState builtFor;
        private RectTransform anchorBlock;
        private float nextAnchorSearch;

        private static BufferState State => GlobalBubbleBuffer.Instance?.SpellbookController?.state;

        public static void Rebuild() {
            try {
                if (instance == null) {
                    var overlay = new GameObject("BI2TL_PadBar", typeof(RectTransform));
                    DontDestroyOnLoad(overlay);
                    var canvas = overlay.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 31000;
                    var scaler = overlay.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = 0.5f;
                    overlay.AddComponent<GraphicRaycaster>();
                    instance = overlay.AddComponent<PadBarView>();
                    instance.Build();
                }
                instance.Fill();
            } catch (Exception ex) {
                Main.Error(ex, "PadBar.Rebuild");
            }
        }

        public static void UpdateInteractable() {
            if (instance == null)
                return;
            bool allow = !Game.Instance.Player.IsInCombat || (State?.AllowInCombat ?? false);
            foreach (var (_, button) in instance.buttons) {
                if (button != null)
                    button.interactable = allow;
            }
        }

        private void Build() {
            font = PadTheme.Font;

            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(transform, false);
            row = (RectTransform)rowGo.transform;
            row.anchorMin = row.anchorMax = row.pivot = Vector2.zero;
            MakeFrame(row);
            // The grid of the game's menu buttons: 48 px cells 1 px apart, the frame's ornament above and to the right.
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(2, 20, 18, 2);
            layout.spacing = 1;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            rowGo.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tipBox = new GameObject("Tip", typeof(RectTransform));
            tipBox.transform.SetParent(transform, false);
            var tipRect = (RectTransform)tipBox.transform;
            tipRect.anchorMin = tipRect.anchorMax = tipRect.pivot = Vector2.zero;
            var tipBg = tipBox.AddComponent<Image>();
            PadTheme.Tip(tipBg);
            tipBg.raycastTarget = false;
            var tipLayout = tipBox.AddComponent<VerticalLayoutGroup>();
            tipLayout.padding = PadTheme.Paper ? PadTheme.TipPadding : new RectOffset(14, 14, 10, 10);
            tipLayout.spacing = 6;
            tipLayout.childControlWidth = true;
            tipLayout.childControlHeight = true;
            tipLayout.childForceExpandWidth = true;
            tipLayout.childForceExpandHeight = false;
            var fitter = tipBox.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // As the game's tooltips: the title centred, the rule, then the text.
            tipTitle = MakeTipText("Title", 22, FontStyles.Bold, TextAlignmentOptions.Center);
            PadTheme.Separator(tipBox.transform);
            tipText = MakeTipText("Text", 18, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            // The group's buffs, each with its picture, as the WotR tooltip lists them.
            tipList = (RectTransform)new GameObject("Buffs", typeof(RectTransform)).transform;
            tipList.SetParent(tipBox.transform, false);
            var listLayout = tipList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 2;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = false;
            listLayout.childForceExpandHeight = false;
            tipBox.SetActive(false);
        }

        // The frame of the game's menu buttons, at the game's size, as a child outside the layout (a tiled Image reports
        // its borders as its minimum size). Its bottom edge is dropped: the game's sheet is open at the bottom, and the
        // bar's single row is lower than the frame's top and bottom edges together.
        private static void MakeFrame(RectTransform parent) {
            var go = new GameObject("Frame", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            var plate = UnityEngine.Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(sp => sp.name == "TBM_backButton");
            if (plate != null) {
                try {
                    var border = plate.border;
                    image.sprite = Sprite.Create(plate.texture, plate.textureRect, new Vector2(0.5f, 0.5f), plate.pixelsPerUnit, 0,
                        SpriteMeshType.FullRect, new Vector4(border.x, 0, border.z, border.w));
                    image.type = Image.Type.Tiled;
                    // Not narrower than its two ornamented ends (canvas units at 100 pixels per unit).
                    (parent.GetComponent<LayoutElement>() ?? parent.gameObject.AddComponent<LayoutElement>()).minWidth = (border.x + border.z) * 100f / plate.pixelsPerUnit;
                } catch (Exception) {
                    image.sprite = null;
                }
            }
            if (image.sprite == null)
                image.color = new Color(0.06f, 0.05f, 0.04f, 0.75f);
        }

        private TextMeshProUGUI MakeTipText(string name, float size, FontStyles style, TextAlignmentOptions alignment) {
            var textGo = new GameObject(name, typeof(RectTransform));
            textGo.transform.SetParent(tipBox.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = style == FontStyles.Bold ? PadTheme.Title : PadTheme.Text;
            text.richText = true;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }

        private void Fill() {
            foreach (Transform child in row) {
                if (child.name != "Frame")
                    Destroy(child.gameObject);
            }
            buttons.Clear();
            builtFor = State;
            if (builtFor == null)
                return;
            var groups = PadGroups.Visible();
            for (int i = 0; i < groups.Count; i++) {
                var group = groups[i];
                string icon = group switch {
                    BuffGroup.Important => "apply_buffs_important",
                    BuffGroup.Quick => "apply_buffs_short",
                    _ => "apply_buffs"
                };
                var button = MakeButton(icon, () => PadGestures.Apply(group, "bar"),
                    () => PadQuickMenu.OpenGroup(group), () => GroupTip(group), i < 9 ? (i + 1).ToString() : null);
                buttons.Add((group, button));
            }
            MakeButton("open_buffs", PadQuickMenu.Toggle, PadQuickMenu.Toggle,
                () => ("pad.title".i8(), PadHelp.Tokens("pad.bar.menu.tip".i8()), null), null);
            UpdateInteractable();
        }

        private Button MakeButton(string icon, Action left, Action right, Func<(string title, string body, List<BubbleBuff> buffs)> tip, string number) {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(row, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(ButtonSize, ButtonSize);
            var image = go.AddComponent<Image>();
            var source = Load(icon + "_normal");
            var normal = KmButtonStyle.Get(icon, source?.texture, KmButtonStyle.State.Normal);
            image.sprite = normal ?? source;
            image.preserveAspect = true;
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = normal != null
                ? new SpriteState {
                    highlightedSprite = KmButtonStyle.Get(icon, source.texture, KmButtonStyle.State.Hover),
                    pressedSprite = KmButtonStyle.Get(icon, source.texture, KmButtonStyle.State.Down),
                    disabledSprite = normal
                }
                : new SpriteState {
                    highlightedSprite = Load(icon + "_hover"),
                    pressedSprite = Load(icon + "_down"),
                    disabledSprite = source
                };
            var colors = button.colors;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            button.colors = colors;
            var events = go.AddComponent<BarButtonEvents>();
            events.Left = left;
            events.Right = right;
            events.Enter = () => {
                var (title, body, buffs) = tip();
                ShowTip(title, body, buffs, (RectTransform)go.transform);
            };
            events.Exit = HideTip;
            if (number != null) {
                var numGo = new GameObject("Number", typeof(RectTransform));
                numGo.transform.SetParent(go.transform, false);
                var rect = (RectTransform)numGo.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
                rect.anchoredPosition = new Vector2(-2, 1);
                rect.sizeDelta = new Vector2(20, 20);
                var text = numGo.AddComponent<TextMeshProUGUI>();
                if (font != null)
                    text.font = font;
                text.text = number;
                text.fontSize = 16;
                text.fontStyle = FontStyles.Bold;
                text.color = new Color(0.93f, 0.8f, 0.5f);
                text.alignment = TextAlignmentOptions.BottomRight;
                text.raycastTarget = false;
            }
            return button;
        }

        private static (string, string, List<BubbleBuff>) GroupTip(BuffGroup group) {
            var state = State;
            var key = state?.GetShortcut(group) ?? ShortcutBinding.None;
            var text = string.Format("pad.bar.tip".i8(), key.IsNone ? "shortcut.none".i8() : key.ToDisplayString());
            var buffs = state?.BuffList?.Where(b => PadGroups.IsMember(b, group) && b.ActiveIn(group))
                .OrderBy(b => b.Name).ToList() ?? new List<BubbleBuff>();
            if (buffs.Count == 0)
                text += $"\n<color={PadTheme.Muted}>{"group.overview.empty".i8()}</color>";
            return (PadGroups.Name(group), text, buffs);
        }

        private void ShowTip(string title, string text, List<BubbleBuff> buffs, RectTransform over) {
            tipTitle.text = PadTheme.Initial(title);
            tipText.text = text;
            // Gone at once, so the tooltip is measured without the previous group's lines.
            for (int i = tipList.childCount - 1; i >= 0; i--)
                DestroyImmediate(tipList.GetChild(i).gameObject);
            if (buffs != null) {
                foreach (var buff in buffs.Take(TipBuffs))
                    AddTipBuff(buff.Icon, buff.Name);
                if (buffs.Count > TipBuffs)
                    AddTipBuff(null, $"<color={PadTheme.Muted}>… +{buffs.Count - TipBuffs}</color>");
            }
            tipList.gameObject.SetActive(buffs != null && buffs.Count > 0);
            tipBox.SetActive(true);
            var tipRect = (RectTransform)tipBox.transform;
            float x = row.anchoredPosition.x + over.anchoredPosition.x - ButtonSize / 2;
            tipRect.anchoredPosition = new Vector2(x, row.anchoredPosition.y + row.rect.height + Margin);
        }

        private void AddTipBuff(Sprite icon, string name) {
            var line = new GameObject("Buff", typeof(RectTransform));
            line.transform.SetParent(tipList, false);
            var layout = line.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var slot = new GameObject("Icon", typeof(RectTransform));
            slot.transform.SetParent(line.transform, false);
            var element = slot.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = TipIconSize;
            element.minHeight = element.preferredHeight = TipIconSize;
            if (icon != null) {
                var image = slot.AddComponent<Image>();
                image.sprite = icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            var textGo = new GameObject("Name", typeof(RectTransform));
            textGo.transform.SetParent(line.transform, false);
            var label = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.fontSize = 18;
            label.color = PadTheme.Text;
            label.richText = true;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = name;
        }

        private void HideTip() => tipBox.SetActive(false);

        private void Update() {
            try {
                // The HUD went away with its scene, and the bar with it: start again on the bar's own canvas.
                if (row == null) {
                    host = null;
                    Build();
                    Fill();
                }
                AttachToHud();
                var game = Game.Instance;
                var pad = PadSettings.Current;
                bool show = pad.QuickCastBar && game != null && !game.IsControllerGamepad && State != null
                    && game.RootUiContext != null && !game.RootUiContext.IsMainMenu
                    && !Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance.IsLoadingInProcess
                    && (game.CurrentMode == GameModeType.Default || game.CurrentMode == GameModeType.Pause)
                    && !game.CutsceneLock && !PadQuickMenu.IsOpen;
                if (show && State != builtFor)
                    Fill();
                if (row.gameObject.activeSelf != show) {
                    row.gameObject.SetActive(show);
                    if (!show)
                        HideTip();
                }
                if (show)
                    row.anchoredPosition = BasePosition() + new Vector2(pad.BarOffsetX, pad.BarOffsetY);
            } catch (Exception ex) {
                Main.Error(ex, "PadBar.Update");
                enabled = false;
            }
        }

        // Into the game's HUD canvas, just before its tooltips: the HUD is drawn under the bar and the tooltips over it.
        // Until the HUD exists the bar stays on its own canvas.
        private void AttachToHud() {
            if (host != null || Time.unscaledTime < nextHostSearch)
                return;
            nextHostSearch = Time.unscaledTime + 1f;
            var tooltips = GameObject.Find("StaticCanvas")?.transform.Find("Tooltips");
            if (tooltips == null)
                return;
            host = (RectTransform)new GameObject("BI2TL_PadBarHost", typeof(RectTransform)).transform;
            host.SetParent(tooltips.parent, false);
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = host.offsetMax = Vector2.zero;
            host.SetSiblingIndex(tooltips.GetSiblingIndex());
            row.SetParent(host, false);
            tipBox.transform.SetParent(host, false);
            // The game's sprites came with the HUD: a bar built before it gets its frame and buttons now.
            if (!KmButtonStyle.Ready || row.Find("Frame")?.GetComponent<Image>()?.sprite == null) {
                KmButtonStyle.Retry();
                var old = row.Find("Frame");
                if (old != null)
                    DestroyImmediate(old.gameObject);
                MakeFrame(row);
                row.Find("Frame").SetAsFirstSibling();
                Fill();
            }
        }

        // The bar sits in the bottom left corner on top of the game's two rows of menu buttons (inventory,
        // map, formation and the rest), measured on screen so it follows the resolution and the UI scale.
        private Vector2 BasePosition() {
            if (anchorBlock == null && Time.unscaledTime >= nextAnchorSearch) {
                nextAnchorSearch = Time.unscaledTime + 1f;
                anchorBlock = GameObject.Find("Menu_Buttons48px")?.transform as RectTransform;
            }
            if (anchorBlock == null || !anchorBlock.gameObject.activeInHierarchy)
                return FallbackPos;
            var canvas = anchorBlock.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            float left = float.MaxValue, top = float.MinValue;
            var corners = new Vector3[4];
            foreach (var rect in anchorBlock.GetComponentsInChildren<RectTransform>(false)) {
                if (rect != anchorBlock && rect.parent != anchorBlock)
                    continue;
                rect.GetWorldCorners(corners);
                foreach (var corner in corners) {
                    var screen = RectTransformUtility.WorldToScreenPoint(cam, corner);
                    left = Mathf.Min(left, screen.x);
                    top = Mathf.Max(top, screen.y);
                }
            }
            var root = (RectTransform)row.parent;
            var rootCanvas = root.GetComponentInParent<Canvas>()?.rootCanvas;
            var rootCam = rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(Mathf.Max(left, 0f), top), rootCam, out var local))
                return FallbackPos;
            // Right on top of the game's sheet, from the screen edge as the sheet is.
            var pos = local - root.rect.min;
            return new Vector2(Mathf.Max(pos.x, 0f), pos.y);
        }

        // The icons are embedded in the DLL, so the release zip stays a DLL and Info.json.
        private static Sprite Load(string name) {
            if (Sprites.TryGetValue(name, out var cached))
                return cached;
            Sprite sprite = null;
            try {
                using var stream = typeof(PadBarView).Assembly.GetManifestResourceStream($"BuffIt2TheLimit.icons.{name}.png");
                if (stream != null) {
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    texture.LoadImage(memory.ToArray());
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            } catch (Exception ex) {
                Main.Error(ex, "PadBar.Load " + name);
            }
            Sprites[name] = sprite;
            return sprite;
        }
    }

    internal class BarButtonEvents : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {
        public Action Left;
        public Action Right;
        public Action Enter;
        public Action Exit;

        public void OnPointerClick(PointerEventData eventData) {
            var button = GetComponent<Button>();
            if (button != null && !button.interactable && eventData.button == PointerEventData.InputButton.Left)
                return;
            if (eventData.button == PointerEventData.InputButton.Left)
                Left?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right)
                Right?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
    }
}
