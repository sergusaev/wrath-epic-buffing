using BuffIt2TheLimit.Config;
using Kingmaker;
using Kingmaker.GameModes;
using ModBar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // Quick cast bar of Kingmaker for mouse and keyboard: one button per group shown in the menu, numbered as the menu
    // numbers them, then a button that opens the menu. Left click casts the group, right click opens its buffs in the
    // menu. The buttons are the first segment of the block of the mods' buttons over the game's menu buttons (ModBar),
    // under one frame with the buttons of the other mods.
    internal static class PadBar {
        public static void Refresh() => PadBarView.Rebuild();

        public static void UpdateInteractable() => PadBarView.UpdateInteractable();
    }

    internal class PadBarView : MonoBehaviour {

        private const float Margin = 8f;
        private const float TipIconSize = 26f;
        private const int TipBuffs = 12;

        private static PadBarView instance;
        private static readonly Dictionary<string, Sprite> Sprites = new();

        private GameObject tipBox;
        private bool tipPaper;
        private TextMeshProUGUI tipTitle;
        private TextMeshProUGUI tipText;
        private RectTransform tipList;
        private TMP_FontAsset font;
        private readonly List<(BuffGroup group, Button button)> buttons = new();
        private BarSegment segment;
        private BufferState builtFor;
        private bool styled;
        private float nextStyle;
        private RectTransform anchorBlock;
        private float nextAnchorSearch;

        private static BufferState State => GlobalBubbleBuffer.Instance?.SpellbookController?.state;

        public static void Rebuild() {
            try {
                if (instance == null) {
                    var go = new GameObject("BI2TL_PadBar");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<PadBarView>();
                    instance.segment = BarSegment.Add(ModSettings.ModEntry.Info.Id, BarSegment.BuffsOrder, instance.Shown, instance.Fill);
                }
                if (instance.segment.Root != null)
                    instance.Fill(instance.segment.Root);
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

        // The mod's own condition; the block adds the game's (its menu buttons in view, mouse mode). On the global map there
        // is no bar.
        private bool Shown() {
            var game = Game.Instance;
            return game != null && State != null && game.RootUiContext != null && !game.RootUiContext.IsMainMenu
                && (game.CurrentMode == GameModeType.Default || game.CurrentMode == GameModeType.Pause
                    || game.CurrentMode == GameModeType.EscMode && FindAnchor() != null)
                && !game.CutsceneLock;
        }

        // The tooltip on the game's parchment; made again once the parchment is loaded, if it was made before.
        private void MakeTip(Transform parent) {
            tipPaper = PadTheme.Paper;
            font = PadTheme.Font;
            tipBox = new GameObject("BI2TL_PadBarTip", typeof(RectTransform));
            tipBox.transform.SetParent(parent, false);
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

        private void Fill(RectTransform root) {
            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyImmediate(root.GetChild(i).gameObject);
            buttons.Clear();
            builtFor = State;
            styled = KmButtonStyle.Ready;
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
                var button = MakeButton(root, icon, () => PadCast.Apply(group, "bar"),
                    () => PadQuickMenu.OpenGroup(group), () => GroupTip(group), i < 9 ? (i + 1).ToString() : null);
                buttons.Add((group, button));
            }
            MakeButton(root, "open_buffs", PadQuickMenu.Toggle, PadQuickMenu.Toggle,
                () => ("pad.title".i8(), PadHelp.Tokens("pad.bar.menu.tip".i8()), null), null);
            UpdateInteractable();
        }

        private Button MakeButton(RectTransform root, string icon, Action left, Action right, Func<(string title, string body, List<BubbleBuff> buffs)> tip, string number) {
            var rect = BarSegment.Button(root, "Button");
            var go = rect.gameObject;
            var image = go.AddComponent<Image>();
            var source = Load(icon + "_normal");
            var normal = KmButtonStyle.Get(icon, source?.texture, KmButtonStyle.State.Normal);
            image.sprite = normal ?? source;
            image.preserveAspect = true;
            var button = go.AddComponent<Button>();
            // Not a keyboard target: a clicked button would stay selected, lit, and move with the arrow keys.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
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
                ShowTip(title, body, buffs, rect);
            };
            events.Exit = HideTip;
            if (number != null) {
                var numGo = new GameObject("Number", typeof(RectTransform));
                numGo.transform.SetParent(go.transform, false);
                var numRect = (RectTransform)numGo.transform;
                numRect.anchorMin = numRect.anchorMax = numRect.pivot = new Vector2(1, 0);
                numRect.anchoredPosition = new Vector2(-2, 1);
                numRect.sizeDelta = new Vector2(20, 20);
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
            var text = "pad.bar.tip".i8();
            var buffs = state?.BuffList?.Where(b => PadGroups.IsMember(b, group) && b.ActiveIn(group))
                .OrderBy(b => b.Name).ToList() ?? new List<BubbleBuff>();
            if (buffs.Count == 0)
                text += $"\n<color={PadTheme.Muted}>{"group.overview.empty".i8()}</color>";
            return (PadGroups.Name(group), text, buffs);
        }

        private void ShowTip(string title, string text, List<BubbleBuff> buffs, RectTransform over) {
            var parent = BarSegment.TipParent as RectTransform;
            if (parent == null || !BarSegment.Live)
                return;
            if (tipBox == null || tipPaper != PadTheme.Paper || tipBox.transform.parent != parent) {
                if (tipBox != null)
                    DestroyImmediate(tipBox);
                MakeTip(parent);
            }
            tipBox.transform.SetAsLastSibling();
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
            ((RectTransform)tipBox.transform).anchoredPosition = BarSegment.Above(over, parent) + new Vector2(0, Margin);
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

        private void HideTip() {
            if (tipBox != null)
                tipBox.SetActive(false);
        }

        private void Update() {
            try {
                var root = segment?.Root;
                bool seen = root != null && root.gameObject.activeInHierarchy;
                // Another party (a load) or the game's sprites came: the buttons are made again. The sprites come with
                // the HUD, not with the mod; until the buttons have the game's look, it is tried every second.
                if (seen && (State != builtFor || !styled && Time.unscaledTime >= nextStyle)) {
                    nextStyle = Time.unscaledTime + 1f;
                    if (!styled)
                        KmButtonStyle.Retry();
                    if (State != builtFor || KmButtonStyle.Ready)
                        Fill(root);
                }
                // The block faded or went away under the pointer: the tooltip goes with it.
                if (tipBox != null && tipBox.activeSelf && (!seen || !BarSegment.Live))
                    HideTip();
            } catch (Exception ex) {
                Main.Error(ex, "PadBar.Update");
                enabled = false;
            }
        }

        // The game's menu buttons of the local map, when they are in view.
        private RectTransform FindAnchor() {
            if (anchorBlock == null && Time.unscaledTime >= nextAnchorSearch) {
                nextAnchorSearch = Time.unscaledTime + 1f;
                anchorBlock = GameObject.Find("Menu_Buttons48px")?.transform as RectTransform;
            }
            return anchorBlock != null && anchorBlock.gameObject.activeInHierarchy ? anchorBlock : null;
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
            Deselect();
        }

        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();

        public void OnPointerExit(PointerEventData eventData) {
            Deselect();
            Exit?.Invoke();
        }

        private void Deselect() {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == gameObject)
                events.SetSelectedGameObject(null);
        }
    }
}
