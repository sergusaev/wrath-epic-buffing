using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.GameModes;
using UnityEngine;
using UnityEngine.UI;

namespace ModBar
{
    // The block of the mods' buttons in mouse mode, on top of the game's menu buttons in the bottom left corner: the
    // buffs, the time, the scrolls, side by side under one frame, whichever of these mods are installed. Every mod with
    // buttons there carries this file and adds its own segment (BarSegment.Add); the segments are children of one row
    // that the mods find in the application domain, so the copies of this file in different mods share it. One copy
    // leads: it makes the row, frames it, puts it in place and shows it; the others only keep their segments in it.
    //
    // The row is seen exactly when the game's menu buttons under it are: never in gamepad mode, nowhere during a load,
    // and with their opacity and position while they fade or slide in and out (a window opening or closing), so it never
    // stays over a window. A segment is seen when its mod also wants it (its own condition).
    internal class BarSegment
    {
        // Keep the keys, the names and the order values as they are: older and newer copies of this file share them.
        const string HostKey = "PathfinderMods.Bar.Host";
        const string LeaderKey = "PathfinderMods.Bar.Leader";
        internal const string HostName = "PathfinderMods_Bar";
        internal const int BuffsOrder = 100;
        internal const int TimeOrder = 200;
        internal const int ScrollsOrder = 300;

        static readonly List<BarSegment> s_Own = new List<BarSegment>();
        static BarDriver s_Driver;

        readonly string m_Name;
        readonly Func<bool> m_Shown;
        readonly Action<RectTransform> m_Build;

        // The segment's own object, a horizontal group the mod fills with its buttons; made again with the game's HUD.
        internal RectTransform Root { get; private set; }

        BarSegment(string mod, int order, Func<bool> shown, Action<RectTransform> build)
        {
            m_Name = "Segment_" + order.ToString("000") + "_" + mod;
            m_Shown = shown;
            m_Build = build;
        }

        // A segment of buttons: build fills the segment (buttons with a LayoutElement each, BarSegment.Button helps),
        // whenever it is made; shown is the mod's own condition, asked every frame.
        internal static BarSegment Add(string mod, int order, Func<bool> shown, Action<RectTransform> build)
        {
            var segment = new BarSegment(mod, order, shown, build);
            s_Own.Add(segment);
            if (s_Driver == null)
            {
                var go = new GameObject("PathfinderMods_BarDriver_" + mod);
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_Driver = go.AddComponent<BarDriver>();
            }
            return segment;
        }

        // The size of a button as the game's buttons next to it: 48 on the local map of Kingmaker, as its row on the
        // global map, 47.7 in Wrath of the Righteous.
        internal static float ButtonSize
        {
            get
            {
                var row = Row;
                var element = row != null ? row.GetComponent<LayoutElement>() : null;
                return element != null && element.minHeight > 0 ? element.minHeight : BarLayout.DefaultButton;
            }
        }

        // The row is in view and takes clicks: tooltips of the mods are shown only then.
        internal static bool Live
        {
            get
            {
                var row = Row;
                if (row == null || !row.gameObject.activeInHierarchy) return false;
                var group = row.GetComponent<CanvasGroup>();
                return group == null || group.interactable && group.alpha > 0.99f;
            }
        }

        // Where the mods put their tooltips: the canvas of the row, over all of it.
        internal static Transform TipParent
        {
            get
            {
                var host = Host;
                var canvas = host != null ? host.GetComponentInParent<Canvas>() : null;
                return canvas != null ? canvas.rootCanvas.transform : null;
            }
        }

        // A point above the button in the tooltip parent's space: the left edge of the button, the top of the frame.
        internal static Vector2 Above(RectTransform button, RectTransform parent)
        {
            var row = Row;
            var corners = new Vector3[4];
            button.GetWorldCorners(corners);
            float left = corners[0].x;
            if (row != null)
            {
                row.GetWorldCorners(corners);
                corners[1].x = left;
            }
            else button.GetWorldCorners(corners);
            var screen = RectTransformUtility.WorldToScreenPoint(BarLayout.Cam(button), corners[1]);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, BarLayout.Cam(parent), out local);
            return local - parent.rect.min;
        }

        // A button of the size the row gives it.
        internal static RectTransform Button(Transform segment, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(segment, false);
            var element = go.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = element.minHeight = element.preferredHeight = ButtonSize;
            return (RectTransform)go.transform;
        }

        internal static GameObject Host
        {
            get
            {
                var host = AppDomain.CurrentDomain.GetData(HostKey) as GameObject;
                return host != null ? host : null;
            }
            set { AppDomain.CurrentDomain.SetData(HostKey, value); }
        }

        internal static RectTransform Row
        {
            get
            {
                var host = Host;
                return host != null ? host.transform.Find("Row") as RectTransform : null;
            }
        }

        // The copy that lays the row out: the first one alive.
        internal static bool Lead(GameObject driver)
        {
            var leader = AppDomain.CurrentDomain.GetData(LeaderKey) as GameObject;
            if (leader != null && leader != driver) return false;
            if (leader == null) AppDomain.CurrentDomain.SetData(LeaderKey, driver);
            return true;
        }

        // The mod's segments in the row, made when missing and shown by the mod's condition.
        internal static void Keep(RectTransform row)
        {
            foreach (var segment in s_Own)
            {
                if (row == null) return;
                bool shown;
                try { shown = segment.m_Shown(); }
                catch (Exception e)
                {
                    BarLayout.Error("segment " + segment.m_Name, e);
                    shown = false;
                }
                if (segment.Root == null || segment.Root.parent != row)
                {
                    if (segment.Root != null) UnityEngine.Object.Destroy(segment.Root.gameObject);
                    segment.Root = null;
                    var old = row.Find(segment.m_Name);
                    // Left by a copy of this mod loaded before (a reload of the mod).
                    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                    var go = new GameObject(segment.m_Name, typeof(RectTransform));
                    go.transform.SetParent(row, false);
                    var layout = go.AddComponent<HorizontalLayoutGroup>();
                    layout.spacing = BarLayout.Spacing;
                    layout.childControlWidth = layout.childControlHeight = true;
                    layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                    layout.childAlignment = TextAnchor.LowerLeft;
                    segment.Root = (RectTransform)go.transform;
                    try { segment.m_Build(segment.Root); }
                    catch (Exception e) { BarLayout.Error("building " + segment.m_Name, e); }
                    go.SetActive(shown);
                    BarLayout.Order(row);
                }
                if (segment.Root.gameObject.activeSelf != shown) segment.Root.gameObject.SetActive(shown);
            }
        }
    }

    class BarDriver : MonoBehaviour
    {
        void Update()
        {
            try
            {
                if (BarSegment.Lead(gameObject)) BarLayout.Lead();
                BarSegment.Keep(BarSegment.Row);
            }
            catch (Exception e)
            {
                BarLayout.Error("bar", e);
            }
        }
    }

    // The leading copy's work: the row in the game's HUD, its frame, its place and its visibility.
    static class BarLayout
    {
#if KINGMAKER
        internal const float DefaultButton = 48f;
        internal const float Spacing = 1f;
        // The row stands on the frame of the game's menu buttons (TBM_backButton), at the game's scale: its top as the
        // game's top, with the small ornament at the left and the corner at the right, the bottom the same turned over.
        // The plate starts where the game's sheet starts, the buttons stand over the game's, and the plate keeps the
        // game's margins round its buttons. Measured on the game's sheet; without it, the numbers it usually has.
        const float GameFrameAbove = 14f;
        const int PadSide = 0, PadTop = 14, PadBottom = 14;
        const float DefaultPadRight = 18f;
        const string SheetName = "TBM_backButton", PlateName = SheetName;
        // The parts of the sprite kept whole: the left ornament and the right corner, in its pixels; the edge between
        // them tiled; the top half turned over for the bottom.
        const float PlateLeft = 60f, PlateRight = 112f, EdgeFrom = PlateLeft, EdgeTo = 668f - PlateRight;
        const bool Mirror = true;
        const bool CleanInside = false;
        const int StripeColumn = 0, StripeFrom = 0, StripeTo = 0, RightRow = 0;
        const int HoleTop = 0, HoleRight = 0, BevelGap = 0;
        const int OrnamentFrom = 0, OrnamentLow = 0, OrnamentSide = 0, BevelDepth = 0, Tolerance = 0;
        const float ButtonArt = 0f;
#else
        internal const float DefaultButton = 47.7f;
        internal const float Spacing = 0f;
        // The row stands on the sheet of WotR's panel of HUD buttons (UI_HudBackgroundIcon) as Kingmaker's on its own: the
        // top of the sheet, the same turned over under it, no ornament at the left, which goes off the screen. The sheet's
        // wide bevel under its edge becomes its thin stripe, as far from the edge as from the buttons, as far as the buttons
        // from each other; the corner's ornament stays over it.
        const float GameFrameAbove = 10f;
        const int PadSide = 0, PadTop = 20, PadBottom = 20;
        const float DefaultPadRight = 18f;
        const string SheetName = "UI_HudBackgroundIcon", PlateName = SheetName;
        const float PlateLeft = 0f, PlateRight = 130f, EdgeFrom = 20f, EdgeTo = 180f;
        const bool Mirror = true;
        const bool CleanInside = true;
        // In the sheet's pixels from the top left: the thin stripe in one column of it, from the edge in; the inside under
        // the edge from HoleTop and left of HoleRight from the right; the right side's bevel in RightRow.
        const int StripeColumn = 100, StripeFrom = 22, StripeTo = 33, RightRow = 200;
        const int HoleTop = 22, HoleRight = 26, BevelGap = 3;
        // The corner's ornament: right of OrnamentFrom down to OrnamentLow, and all right of OrnamentSide. There a pixel
        // is the bevel when it is close to what the bevel has there, BevelDepth deep, else the ornament.
        const int OrnamentFrom = 195, OrnamentLow = 42, OrnamentSide = 262, BevelDepth = 45, Tolerance = 36;
        // The buttons' pictures stand this far in from their own edges.
        const float ButtonArt = 1.5f;
#endif
        static readonly HashSet<string> s_Errors = new HashSet<string>();
        static RectTransform s_Anchor;
        static float s_NextAnchorSearch;
        static int s_Children = -1;
        // Between the row's plate and the frame of the game's buttons.
        const float Gap = 6f;
        static Sprite s_Source;
        static float s_HalfUnits, s_HalfRate;
        // Units of the row per pixel of the game's sheet.
        static float s_UnitsPerPixel = 0.5f;

        internal static void Error(string what, Exception e)
        {
            if (s_Errors.Add(what + e.GetType().Name)) Debug.LogError("[ModBar] " + what + ": " + e);
        }

        internal static void Lead()
        {
            var anchor = Anchor();
            var host = BarSegment.Host;
            if (host == null)
            {
                if (anchor == null) return;
                host = MakeHost();
            }
            if (anchor != null) Attach(host.transform, anchor);
            var row = BarSegment.Row;
            if (row.childCount != s_Children) Order(row);
            var game = Game.Instance;
            float alpha = anchor != null ? Alpha(anchor) : 0f;
            // What hides, fades or covers the game's buttons does the same to the row, which lives next to them; the
            // conditions here are only those of the row's own.
            bool system = game != null && !game.IsControllerGamepad && anchor != null && alpha > 0.001f
                && !Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance.IsLoadingInProcess;
            bool any = false;
            for (int i = 0; i < row.childCount; i++)
                if (row.GetChild(i).name.StartsWith("Segment_") && row.GetChild(i).gameObject.activeSelf) any = true;
            bool show = system && any;
            if (row.gameObject.activeSelf != show) row.gameObject.SetActive(show);
            if (!show) return;
            var group = row.GetComponent<CanvasGroup>();
            // Under the Esc menu the row is seen, as the game's buttons, but not pressed; nor while it fades.
            bool live = alpha > 0.99f && game.CurrentMode != GameModeType.EscMode;
            if (group.interactable != live) group.interactable = group.blocksRaycasts = live;
            Size(row, ButtonSizeAt(anchor));
            Place(row, anchor);
            Frame(row);
        }

        static GameObject MakeHost()
        {
            var go = new GameObject(BarSegment.HostName, typeof(RectTransform));
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var rowGo = new GameObject("Row", typeof(RectTransform));
            var row = (RectTransform)rowGo.transform;
            row.SetParent(go.transform, false);
            row.anchorMin = row.anchorMax = row.pivot = Vector2.zero;
            var frame = new GameObject("Frame", typeof(RectTransform));
            var frameRect = (RectTransform)frame.transform;
            frameRect.SetParent(row, false);
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
            frame.AddComponent<LayoutElement>().ignoreLayout = true;
            frame.AddComponent<Image>().raycastTarget = false;
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(PadSide, PadSide, PadTop, PadBottom);
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = rowGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowGo.AddComponent<CanvasGroup>();
            // The button size for the segments, kept on the row: the copies of this file read it there.
            var size = rowGo.AddComponent<LayoutElement>();
            size.ignoreLayout = true;
            size.minHeight = DefaultButton;
            rowGo.SetActive(false);
            BarSegment.Host = go;
            s_Children = -1;
            return go;
        }

        // Right after the game's buttons in their own branch of the HUD, so that the row is shown, faded and covered with
        // them; above a masked part of the branch, so that the row over the buttons is not cut off.
        static void Attach(Transform host, RectTransform anchor)
        {
            Transform branch = anchor, parent = anchor.parent;
            for (var t = anchor.parent; t != null && t.parent != null; t = t.parent)
            {
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && canvas.isRootCanvas) break;
                if (t.GetComponent<RectMask2D>() != null || t.GetComponent<Mask>() != null)
                {
                    branch = t;
                    parent = t.parent;
                }
            }
            if (parent == null) return;
            if (host.parent != parent)
            {
                host.SetParent(parent, false);
                var rect = (RectTransform)host;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            int at = branch.GetSiblingIndex(), index = host.GetSiblingIndex();
            if (index != at + 1) host.SetSiblingIndex(index < at ? at : at + 1);
        }

        // The segments in the order of their names (the order value is in the name), after the frame.
        internal static void Order(RectTransform row)
        {
            var segments = new List<Transform>();
            for (int i = 0; i < row.childCount; i++)
                if (row.GetChild(i).name.StartsWith("Segment_")) segments.Add(row.GetChild(i));
            segments.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < segments.Count; i++) segments[i].SetSiblingIndex(i + 1);
            s_Children = row.childCount;
        }

        // The game's menu buttons the row stands on, when they exist: Kingmaker's HUD block of buttons or the sheet under
        // the global map's row of buttons, WotR's panel of HUD buttons.
        static RectTransform Anchor()
        {
            if ((s_Anchor == null || !s_Anchor.gameObject.activeInHierarchy) && Time.unscaledTime >= s_NextAnchorSearch)
            {
                s_NextAnchorSearch = Time.unscaledTime + 0.5f;
                GameObject found = null;
#if KINGMAKER
                found = GameObject.Find("Menu_Buttons48px") ?? GameObject.Find("GlobalMapCanvas/GameMenu/Background");
#else
                var canvas = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.Canvas : null;
                var panel = canvas != null ? canvas.transform.Find("NestedCanvas1/IngameMenuView/ButtonsPart") : null;
                found = panel != null ? panel.gameObject : null;
#endif
                s_Anchor = found != null ? found.transform as RectTransform : null;
            }
            return s_Anchor != null && s_Anchor.gameObject.activeInHierarchy ? s_Anchor : null;
        }

        // How opaque the game's buttons are: the canvas groups above them, as Unity multiplies them.
        static float Alpha(Transform anchor)
        {
            float alpha = 1f;
            for (var t = anchor; t != null; t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group != null && group.enabled)
                {
                    alpha *= group.alpha;
                    if (group.ignoreParentGroups) break;
                }
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled) return 0f;
            }
            return alpha;
        }

        static float ButtonSizeAt(RectTransform anchor)
        {
#if KINGMAKER
            if (anchor.name != "Background") return DefaultButton;
            var row = anchor.parent.Find("SystemButtons");
            return row != null && row.childCount > 0 ? ((RectTransform)row.GetChild(0)).rect.height : DefaultButton;
#else
            return DefaultButton;
#endif
        }

        static void Size(RectTransform row, float size)
        {
            var element = row.GetComponent<LayoutElement>();
            if (element.minHeight == size) return;
            element.minHeight = size;
            for (int i = 0; i < row.childCount; i++)
                foreach (Transform button in row.GetChild(i))
                {
                    var e = button.GetComponent<LayoutElement>();
                    if (e != null && !e.ignoreLayout) e.minWidth = e.preferredWidth = e.minHeight = e.preferredHeight = size;
                }
        }

        // Over the frame of the game's buttons with a small gap, from their left edge, measured on screen so that it follows the resolution,
        // the UI scale and the game's own slides.
        static void Place(RectTransform row, RectTransform anchor)
        {
            var cam = Cam(anchor);
            float left = float.MaxValue, top = float.MinValue, right = float.MinValue;
            var corners = new Vector3[4];
#if KINGMAKER
            // The top of the buttons themselves, the block's own rectangle may reach higher; the sheet is measured as it is.
            bool sheet = anchor.name == "Background";
            // The frame of the game's buttons may lie among them; it is not a button.
            var frameOfButtons = Sheet(anchor);
            foreach (var rect in anchor.GetComponentsInChildren<RectTransform>(false))
            {
                if (sheet ? rect != anchor : rect.parent != anchor) continue;
                if (!sheet && frameOfButtons != null && rect == frameOfButtons.rectTransform) continue;
#else
            foreach (var rect in new[] { anchor })
            {
#endif
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var screen = RectTransformUtility.WorldToScreenPoint(cam, corner);
                    left = Mathf.Min(left, screen.x);
                    right = Mathf.Max(right, screen.x);
                    top = Mathf.Max(top, screen.y);
                }
            }
            // Over the game's sheet, only the buttons that stand on it count: the block holds more than them.
            var onSheet = Sheet(anchor);
            if (onSheet != null)
            {
                onSheet.rectTransform.GetWorldCorners(corners);
                var sheetMin = RectTransformUtility.WorldToScreenPoint(Cam(onSheet), corners[0]);
                var sheetMax = RectTransformUtility.WorldToScreenPoint(Cam(onSheet), corners[2]);
                float l = float.MaxValue, r = float.MinValue, t = float.MinValue;
                var host = BarSegment.Host != null ? BarSegment.Host.transform : null;
                foreach (var button in anchor.GetComponentsInChildren<MonoBehaviour>(false))
                {
                    if (!IsButton(button) || (host != null && button.transform.IsChildOf(host))) continue;
                    ((RectTransform)button.transform).GetWorldCorners(corners);
                    var a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                    var b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                    var middle = (a + b) / 2f;
                    if (middle.x < sheetMin.x || middle.x > sheetMax.x || middle.y < sheetMin.y || middle.y > sheetMax.y) continue;
                    l = Mathf.Min(l, a.x);
                    r = Mathf.Max(r, b.x);
                    t = Mathf.Max(t, b.y);
                }
                if (t > float.MinValue)
                {
                    left = l;
                    right = r;
                    top = t;
                }
            }
            if (top == float.MinValue) return;
            var root = (RectTransform)row.parent;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(Mathf.Max(left, 0f), top), Cam(root), out local)) return;
            var pos = local - root.rect.min;
            // Where the game's sheet is and how far it reaches round the game's buttons.
            Func<float, float, Vector2> toRow = (sx, sy) =>
            {
                Vector2 p;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(sx, sy), Cam(root), out p);
                return p - root.rect.min;
            };
            var buttonsRight = toRow(right, top);
            float x, above, padRight;
            var gameSheet = Sheet(anchor);
            if (gameSheet != null)
            {
                gameSheet.rectTransform.GetWorldCorners(corners);
                var a = RectTransformUtility.WorldToScreenPoint(Cam(gameSheet), corners[1]);
                var b = RectTransformUtility.WorldToScreenPoint(Cam(gameSheet), corners[2]);
                var sheetLeft = toRow(a.x, a.y);
                x = sheetLeft.x;
                above = sheetLeft.y - pos.y;
                padRight = toRow(b.x, b.y).x - buttonsRight.x;
                // The game's pixels in the row's units: a sliced sheet shows them at the canvas' rate, a simple one stretched.
                var sprite = gameSheet.sprite;
                float sheetUnits = gameSheet.type == Image.Type.Simple
                    ? gameSheet.rectTransform.rect.height / sprite.rect.height
                    : gameSheet.canvas.referencePixelsPerUnit / sprite.pixelsPerUnit;
                float rowScale = root.lossyScale.y;
                if (rowScale > 0f) s_UnitsPerPixel = sheetUnits * gameSheet.rectTransform.lossyScale.y / rowScale;
                string report = string.Format("[ModBar] measure: above {0:0.#} right {1:0.#} left {2:0.#} units/px {3:0.###} sheet {4} scale {5:0.###}/{6:0.###} ref {7} ppu {8} buttons top {9:0.#}",
                    sheetLeft.y - pos.y, padRight, pos.x - x, s_UnitsPerPixel, gameSheet.rectTransform.rect, gameSheet.rectTransform.lossyScale.y, rowScale,
                    gameSheet.canvas.referencePixelsPerUnit, sprite.pixelsPerUnit, pos.y);
                if (report != s_Report)
                {
                    s_Report = report;
                    Debug.Log(report);
                }
            }
            else
            {
                x = toRow(0f, top).x;
                above = GameFrameAbove;
                padRight = DefaultPadRight;
            }
            above = Mathf.Clamp(above, 4f, 40f);
            int padLeft = Mathf.Max(0, Mathf.RoundToInt(pos.x - x));
            // Our frame's own margin over and under the buttons: the game's, or what the whole plate leaves round them.
            float own = above;
            var plateSource = Mirror ? null : Source();
            if (plateSource != null) own = Mathf.Max(2f, (plateSource.rect.height * s_UnitsPerPixel - ButtonSizeAt(anchor)) / 2f);
            if (CleanInside)
            {
                // The frame round the stripe: its edge, a gap, the stripe, a gap, the buttons' pictures.
                float inner = 2f * BevelGap + (StripeTo - StripeFrom + 1) - ButtonArt;
                own = (HoleTop + inner) * s_UnitsPerPixel;
                padRight = (HoleRight + inner) * s_UnitsPerPixel;
            }
            int edge = Mathf.RoundToInt(own);
            int padEnd = Mathf.Max(0, Mathf.RoundToInt(padRight));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout.padding.left != padLeft || layout.padding.right != padEnd || layout.padding.top != edge || layout.padding.bottom != edge)
            {
                layout.padding = new RectOffset(padLeft, padEnd, edge, edge);
                LayoutRebuilder.MarkLayoutForRebuild(row);
            }
            s_Above = own;
            s_PadLeft = padLeft;
            s_PadRight = padEnd;
            // The row's buttons where they are drawn: their bottom the frame's edge over the game's sheet and the gap.
            var at = new Vector2(x, pos.y + above + Gap);
            Rect buttons;
            if (Buttons(row, root, out buttons))
                at.y = row.anchoredPosition.y + (pos.y + above + Gap + own) - (buttons.yMin - root.rect.yMin);
            if (row.anchoredPosition != at) row.anchoredPosition = at;
        }

        // The frame of the game's menu buttons cut to the row's width.
        static void Frame(RectTransform row)
        {
            var image = row.Find("Frame").GetComponent<Image>();
            // The frame round the row's buttons as the game's round its own: the top of the game's frame over the top half,
            // the same turned over under the bottom half.
            image.enabled = false;
            var holderRect = ((RectTransform)image.transform).rect;
            Rect buttons;
            if (!Buttons(row, (RectTransform)image.transform, out buttons)) return;
            var frame = image.transform.Find("Plate") as RectTransform;
            if (frame == null) frame = MakePlate((RectTransform)image.transform);
            float left = buttons.xMin - s_PadLeft, bottom = buttons.yMin - s_Above;
            var size = new Vector2(buttons.width + s_PadLeft + s_PadRight, buttons.height + 2f * s_Above);
            var place = new Vector2(left - holderRect.xMin, bottom - holderRect.yMin);
            if (frame.anchoredPosition != place) frame.anchoredPosition = place;
            if (frame.sizeDelta != size) frame.sizeDelta = size;
            if (!Parts(Mirror ? size.y / 2f : size.y)) return;
            var top = (RectTransform)frame.GetChild(0);
            if (!Mirror && top.anchorMin.y != 0f)
            {
                top.anchorMin = Vector2.zero;
                frame.GetChild(1).gameObject.SetActive(false);
            }
            foreach (var half in Mirror ? new[] { frame.GetChild(0), frame.GetChild(1) } : new[] { frame.GetChild(0) })
            {
                Part(half, 0, s_Parts[0], Image.Type.Simple);
                Part(half, 1, s_Parts[1], Image.Type.Tiled);
                Part(half, 2, s_Parts[2], Image.Type.Simple);
            }
        }

        // The game's buttons: Kingmaker's are Unity's, WotR's Owlcat's own.
        static bool IsButton(MonoBehaviour c)
        {
            if (c is Selectable) return true;
            var name = c.GetType().Name;
            return name.StartsWith("OwlcatButton") || name.StartsWith("OwlcatMultiButton");
        }

        static float s_Above = GameFrameAbove, s_PadLeft, s_PadRight = DefaultPadRight;
        static readonly Sprite[] s_Parts = new Sprite[3];

        // The bounds of the row's buttons in the given rectangle's units.
        static bool Buttons(RectTransform row, RectTransform space, out Rect bounds)
        {
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            var corners = new Vector3[4];
            for (int i = 0; i < row.childCount; i++)
            {
                var segment = row.GetChild(i);
                if (!segment.name.StartsWith("Segment_") || !segment.gameObject.activeSelf) continue;
                foreach (Transform button in segment)
                {
                    var e = button.GetComponent<LayoutElement>();
                    if (!button.gameObject.activeSelf || (e != null && e.ignoreLayout)) continue;
                    ((RectTransform)button).GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var p = space.InverseTransformPoint(corner);
                        xMin = Mathf.Min(xMin, p.x);
                        xMax = Mathf.Max(xMax, p.x);
                        yMin = Mathf.Min(yMin, p.y);
                        yMax = Mathf.Max(yMax, p.y);
                    }
                }
            }
            bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return xMin < xMax && yMin < yMax;
        }

        // The plate: two halves, the lower one turned over, each of the left ornament, the tiled edge and the right corner.
        // Inside the row's frame object, which covers the row and stays behind the buttons.
        static RectTransform MakePlate(RectTransform holder)
        {
            var go = new GameObject("Plate", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(holder, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            for (int h = 0; h < 2; h++)
            {
                var half = (RectTransform)new GameObject(h == 0 ? "Top" : "Bottom", typeof(RectTransform)).transform;
                half.SetParent(rect, false);
                half.anchorMin = new Vector2(0f, h == 0 ? 0.5f : 0f);
                half.anchorMax = new Vector2(1f, h == 0 ? 1f : 0.5f);
                half.pivot = new Vector2(0.5f, 0.5f);
                half.offsetMin = half.offsetMax = Vector2.zero;
                if (h == 1) half.localScale = new Vector3(1f, -1f, 1f);
                for (int i = 0; i < 3; i++)
                {
                    var part = (RectTransform)new GameObject(i == 0 ? "Left" : i == 1 ? "Edge" : "Right", typeof(RectTransform)).transform;
                    part.SetParent(half, false);
                    part.gameObject.AddComponent<Image>().raycastTarget = false;
                }
            }
            return rect;
        }

        // Each part at the game's scale: the ornaments as wide as they are, the edge between them.
        static void Part(Transform half, int index, Sprite sprite, Image.Type type)
        {
            var rect = (RectTransform)half.GetChild(index);
            var image = rect.GetComponent<Image>();
            if (image.enabled != (sprite != null)) image.enabled = sprite != null;
            if (image.sprite != sprite)
            {
                image.sprite = sprite;
                image.type = type;
                image.color = Color.white;
            }
            float left = PlateLeft * s_UnitsPerPixel, right = PlateRight * s_UnitsPerPixel;
            rect.anchorMin = new Vector2(index == 2 ? 1f : 0f, 0f);
            rect.anchorMax = new Vector2(index == 0 ? 0f : 1f, 1f);
            rect.pivot = new Vector2(index == 2 ? 1f : 0f, 0.5f);
            rect.offsetMin = new Vector2(index == 1 ? left : index == 2 ? -right : 0f, 0f);
            rect.offsetMax = new Vector2(index == 0 ? left : index == 1 ? -right : 0f, 0f);
        }

        // The top of the game's frame as tall as half the plate, cut in three.
        static bool Parts(float units)
        {
            if (s_Parts[1] != null && s_HalfUnits == units && s_HalfRate == s_UnitsPerPixel) return true;
            var source = Source();
            if (source == null) return false;
            try
            {
                float pixels = Mirror ? Mathf.Min(source.rect.height, units / s_UnitsPerPixel) : source.rect.height;
                var texture = source.texture;
                var rect = source.textureRect;
                if (CleanInside)
                {
                    texture = Cleaned(source);
                    if (texture == null) return false;
                    rect = new Rect(0f, 0f, texture.width, texture.height);
                }
                float ppu = 100f / s_UnitsPerPixel;
                float y = rect.yMax - pixels;
                s_Parts[0] = PlateLeft > 0f ? Sprite.Create(texture, new Rect(rect.x, y, PlateLeft, pixels), new Vector2(0.5f, 0.5f), ppu) : null;
                s_Parts[1] = Sprite.Create(texture, new Rect(rect.x + EdgeFrom, y, EdgeTo - EdgeFrom, pixels), new Vector2(0.5f, 0.5f), ppu,
                    0, SpriteMeshType.FullRect);
                s_Parts[2] = Sprite.Create(texture, new Rect(rect.xMax - PlateRight, y, PlateRight, pixels), new Vector2(0.5f, 0.5f), ppu);
                s_HalfUnits = units;
                s_HalfRate = s_UnitsPerPixel;
                return true;
            }
            catch (Exception e)
            {
                Error("plate", e);
                return false;
            }
        }

        static Sprite Source()
        {
            if (s_Source != null) return s_Source;
            var sheet = s_Anchor != null ? Sheet(s_Anchor) : null;
            if (sheet != null && sheet.sprite.name == PlateName) s_Source = sheet.sprite;
            else s_Source = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(sp => sp.name == PlateName) ?? FromBundles(PlateName);
            return s_Source;
        }

        // WotR keeps the sprites of views not shown in its loaded "ui" bundle.
        static Sprite FromBundles(string name)
        {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                try
                {
                    if (bundle.isStreamedSceneAssetBundle) continue;
                    var sprite = bundle.LoadAsset<Sprite>(name);
                    if (sprite != null) return sprite;
                }
                catch (Exception) { }
            }
            return null;
        }

        static Texture2D s_Cleaned;

        // A readable copy of a sprite's pixels. It keeps the texture's own bytes: an sRGB texture through an sRGB target,
        // a linear one through a linear one.
        internal static Texture2D Readable(Sprite source)
        {
            var tex = source.texture;
            var rect = source.textureRect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
#if KINGMAKER
            var readWrite = RenderTextureReadWrite.sRGB;
#else
            var readWrite = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat)
                ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
#endif
            // Only the sprite's part of the atlas, drawn over the whole target: no coordinates to read back in.
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, readWrite);
            var was = RenderTexture.active;
            Graphics.Blit(tex, rt, new Vector2(rect.width / tex.width, rect.height / tex.height), new Vector2(rect.x / tex.width, rect.y / tex.height));
            RenderTexture.active = rt;
            // Clamped: a tiled part of an unpacked texture that repeats would show the whole texture, not its own part.
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        // A readable copy of the sheet's pixels with its wide bevel made its thin stripe and the dark of its middle: by the
        // distance from the edge, top or right, BevelGap of dark, the stripe, dark again; the corner's ornament left over it.
        static Texture2D Cleaned(Sprite source)
        {
            if (s_Cleaned != null) return s_Cleaned;
            var copy = Readable(source);
            int w = copy.width, h = copy.height;
            var px = copy.GetPixels32();
            var src = (Color32[])px.Clone();
            Func<int, int, Color32> at = (x, row) => src[(h - 1 - Mathf.Clamp(row, 0, h - 1)) * w + Mathf.Clamp(x, 0, w - 1)];
            var stripe = new Color32[StripeTo - StripeFrom + 1];
            for (int k = 0; k < stripe.Length; k++) stripe[k] = at(StripeColumn, StripeFrom + k);
            var dark = at(StripeColumn, StripeTo + 30);
            int right = w - 1 - HoleRight, filled = 0;
            for (int row = HoleTop; row < h; row++)
                for (int x = 0; x <= right; x++)
                {
                    int dt = row - HoleTop, dr = right - x;
                    if (x >= OrnamentFrom && row <= OrnamentLow || x >= OrnamentSide)
                    {
                        var c = at(x, row);
                        var top = dt < BevelDepth ? at(StripeColumn, row) : dark;
                        var side = dr < BevelDepth ? at(x, RightRow) : dark;
                        if (Mathf.Min(Mathf.Min(Diff(c, top), Diff(c, side)), Diff(c, dark)) > Tolerance) continue;
                    }
                    int k = Mathf.Min(dt, dr) - BevelGap;
                    var to = k >= 0 && k < stripe.Length ? stripe[k] : dark;
                    to.a = 255;
                    px[(h - 1 - row) * w + x] = to;
                    filled++;
                }
            copy.SetPixels32(px);
            copy.Apply();
            Debug.Log("[ModBar] plate copy: " + filled + " of " + px.Length + " pixels filled, " + w + "x" + h);
            s_Cleaned = copy;
            return copy;
        }

        static int Diff(Color32 a, Color32 b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }

        static Image s_Sheet;
        static RectTransform s_SheetFor;

        // The game's sheet under its buttons: the frame of the HUD's block, the sheet of the global map's row.
        static Image Sheet(RectTransform anchor)
        {
            if (s_SheetFor == anchor && s_Sheet != null) return s_Sheet;
            if (s_SheetFor == anchor && Time.unscaledTime < s_NextSheetSearch) return null;
            s_SheetFor = anchor;
            s_NextSheetSearch = Time.unscaledTime + 2f;
            s_Sheet = null;
            var own = anchor.GetComponent<Image>();
            if (anchor.name == "Background" && own != null && own.sprite != null) s_Sheet = own;
            var t = anchor;
            for (int depth = 0; s_Sheet == null && t != null && depth < 4; depth++, t = t.parent as RectTransform)
                s_Sheet = t.GetComponentsInChildren<Image>(false).FirstOrDefault(i => i.sprite != null && i.sprite.name == SheetName);
            if (s_Sheet == null)
            {
                var background = anchor.Find("Background");
                var image = background != null ? background.GetComponent<Image>() : null;
                if (image != null && image.sprite != null) s_Sheet = image;
            }
            Debug.Log("[ModBar] sheet: " + (s_Sheet != null ? s_Sheet.name + " / " + s_Sheet.sprite.name + " / " + s_Sheet.type : "none"));
            return s_Sheet;
        }

        static float s_NextSheetSearch;
        static string s_Report;

        // WotR draws its interface through its UI camera, whose volume holds the game's gamma: a window on an overlay of its
        // own comes out lighter than the game's windows. Kingmaker's overlays match its windows.
        internal static void ThroughGameCamera(Canvas canvas)
        {
#if !KINGMAKER
            var cam = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.UICamera : null;
            if (cam == null) return;
            int layer = LayerMask.NameToLayer("UI");
            if (layer < 0 || (cam.cullingMask & (1 << layer)) == 0)
                for (layer = 0; layer < 32 && (cam.cullingMask & (1 << layer)) == 0; layer++) { }
            if (layer >= 32) return;
            canvas.gameObject.layer = layer;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = Mathf.Min(cam.nearClipPlane + 1f, (cam.nearClipPlane + cam.farClipPlane) / 2f);
#endif
        }

        internal static Camera Cam(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }
    }
}
