using BuffIt2TheLimit.Config;
using HarmonyLib;
using Kingmaker.UI.MVVM._ConsoleView.IngameMenu;
using System;
using System.Collections.Generic;
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
        // A muted amber of its own, in the tone of the game's cards (Rest is 9DA476 with its frame 6F764B).
        private static readonly Color CardColor = new Color32(0xC4, 0xA2, 0x68, 0xFF);
        private static readonly Color FrameColor = new Color32(0x86, 0x6B, 0x3E, 0xFF);
        private static Sprite big;
        private static Sprite small;
        private static bool dumped;
        private static bool stacked;
        private static IngameMenuCardsConsoleView pendingMenu;
        private static float pendingAt = -1;

        // The menu is bound: add the card after Rest and rebuild the carousel and the navigation with it.
        internal static void Bound(IngameMenuCardsConsoleView view) {
            var rest = view.m_RestView;
            if (rest == null)
                return;
            // Not on the global map of the crusade: buffs are cast in an area.
            bool globalMap = Kingmaker.UI.MVVM.RootUIContext.Instance?.IsGlobalMap ?? false;
            var card = rest.transform.parent.Find(CardName)?.GetComponent<InGameMenuCardEntityConsoleView>();
            if (card == null && globalMap)
                return;
            card ??= Create(rest);
            var list = view.m_CartCollection.m_CartList.OfType<InGameMenuCardEntityConsoleView>().Where(c => c != card).ToList();
            if (globalMap) {
                card.gameObject.SetActive(false);
            } else {
                card.Initialize("pad.title".i8());
                card.Bind(() => Select(view), true);
                // The last card of the fan, laid over or under the card before it as the game's cards lie on each other.
                var previous = list.LastOrDefault();
                list.Add(card);
                if (previous != null && list.Count > 2)
                    Stack(card.transform, previous.transform, list[1].transform.GetSiblingIndex() > list[0].transform.GetSiblingIndex());
            }
            view.m_CartCollection.Initialize(list);
            view.m_NavigationBehaviour.SetEntitiesHorizontal(list);
            view.m_CartCollection.FocusOnFirstEntity();
            view.m_NavigationBehaviour.FocusOnFirstValidEntity();
        }

        // Cards are drawn in the order of the hierarchy, a later one over an earlier one: right after the card before it
        // when the next card lies over the one before, right before it when under.
        private static void Stack(Transform card, Transform previous, bool overPrevious) {
            card.SetAsLastSibling();
            int index = previous.GetSiblingIndex();
            card.SetSiblingIndex(overPrevious ? index + 1 : index);
            if (!stacked) {
                stacked = true;
                Main.Verbose($"[PAD] menu card laid {(overPrevious ? "over" : "under")} {previous.name}");
            }
        }

        private static InGameMenuCardEntityConsoleView Create(InGameMenuCardEntityConsoleView rest) {
            if (!dumped) {
                dumped = true;
                Main.Verbose("[PAD] menu card template:\n" + Dump(rest.transform, ""));
            }
            var go = UnityEngine.Object.Instantiate(rest.gameObject, rest.transform.parent, false);
            go.name = CardName;
            // The card as the game's: Back is its colour, Back/Frame the same colour darker, Back/Symbol_Main the drawing
            // over the whole card, Back/Symbol_Corner the small dark sign in its corner.
            SetColor(go.transform, "Back", CardColor);
            SetColor(go.transform, "Back/Frame", FrameColor);
            var main = go.transform.Find("Back/Symbol_Main")?.GetComponent<Image>();
            if (main != null) {
                var rect = ((RectTransform)main.transform).rect;
                big ??= Drawing.Card("BuffIt2TheLimit.card_buffs.png", rect.width / rect.height);
                if (big != null) {
                    main.sprite = big;
                    main.preserveAspect = false;
                }
            }
            var corner = go.transform.Find("Back/Symbol_Corner")?.GetComponent<Image>();
            if (corner != null) {
                // In the colour of the card's frame, as the game's corner signs are, in the ink line of the big drawing.
                small ??= Drawing.Sign("BuffIt2TheLimit.card_buffs_small.png", FrameColor);
                if (small != null) {
                    corner.sprite = small;
                    corner.preserveAspect = true;
                }
            }
            Main.Verbose("[PAD] menu card created");
            return go.GetComponent<InGameMenuCardEntityConsoleView>();
        }

        private static void SetColor(Transform card, string path, Color color) {
            var image = card.Find(path)?.GetComponent<Image>();
            if (image != null)
                image.color = color;
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

        private static void Select(IngameMenuCardsConsoleView view) {
            Main.Verbose("[PAD] menu card selected");
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

    // The card drawing in the manner of WotR's own (Rest, Map, Formation): a pale paper shape in a dark ink line of uneven
    // width with a ragged edge, the inner lines in ink as well, here and there a careless hatching. Drawn from the mask of
    // the Kingmaker card picture (its transparent cut-outs become the inner lines), once per session.
    static class Drawing {
        internal static readonly Color32 Ink = new Color32(0x2B, 0x24, 0x1F, 0xFF);
        private static readonly Color Paper = new Color32(0xF2, 0xEC, 0xDD, 0xFF);
        private const int Width = 378;
        // The outer ink line: Outline wide, more or less by up to OutlineVary; inside the shape the ink eats up to InkIn.
        private const float Outline = 3.4f;
        private const float OutlineVary = 2.8f;
        private const float InkIn = 1.8f;
        private const int Reach = 8;

        internal static Sprite Card(string resource, float aspect) {
            var source = Read(resource);
            if (source == null || aspect <= 0)
                return null;
            int sw = source.width, sh = source.height;
            var from = source.GetPixels32();
            UnityEngine.Object.Destroy(source);
            DropSpecks(from, sw, sh);
            int x0 = sw, x1 = -1, y0 = sh, y1 = -1;
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                    if (from[y * sw + x].a > 127) {
                        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                        y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    }
            if (x1 < 0)
                return null;

            // As large as the game's drawings next to it (Rest, Map): under half the card high, a little below its middle.
            int w = Width, h = Mathf.RoundToInt(Width / aspect);
            float scale = Mathf.Min(0.44f * h / (y1 - y0 + 1), 0.56f * w / (x1 - x0 + 1));
            float cx = (x0 + x1) / 2f, cy = (y0 + y1) / 2f, tx = w * 0.5f, ty = h * 0.44f;
            var inside = new bool[w * h];
            int bx0 = w, bx1 = -1, by0 = h, by1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) {
                    float a = Alpha(from, sw, sh, cx + (x + 0.5f - tx) / scale, cy + (y + 0.5f - ty) / scale);
                    if (a > 0.5f) {
                        inside[y * w + x] = true;
                        bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x);
                        by0 = Math.Min(by0, y); by1 = Math.Max(by1, y);
                    }
                }

            // Offsets of the disc in order of distance: the first one of the other kind is the distance to the edge.
            var offsets = new List<(int dx, int dy, float d)>();
            for (int dy = -Reach; dy <= Reach; dy++)
                for (int dx = -Reach; dx <= Reach; dx++) {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= Reach)
                        offsets.Add((dx, dy, d));
                }
            offsets.Sort((a, b) => a.d.CompareTo(b.d));

            var decor = Background(w, h, tx, ty);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(Ink.r, Ink.g, Ink.b, (byte)(255 * decor[i]));
            for (int y = Math.Max(0, by0 - Reach); y <= Math.Min(h - 1, by1 + Reach); y++) {
                for (int x = Math.Max(0, bx0 - Reach); x <= Math.Min(w - 1, bx1 + Reach); x++) {
                    int i = y * w + x;
                    bool fill = inside[i];
                    float edge = Reach + 1;
                    foreach (var (dx, dy, d) in offsets) {
                        int sx = x + dx, sy = y + dy;
                        bool other = sx < 0 || sy < 0 || sx >= w || sy >= h ? fill : inside[sy * w + sx] != fill;
                        if (other) {
                            edge = d;
                            break;
                        }
                    }
                    if (!fill) {
                        // The outer line, wider and thinner along the shape, its edge torn by a finer noise.
                        float width = Outline + OutlineVary * (Noise(x, y, 15f, 1) - 0.5f) * 2f + 1.2f * (Noise(x, y, 3f, 2) - 0.5f);
                        float ink = Mathf.Clamp01(width - edge + 0.5f);
                        if (ink > 0)
                            pixels[i] = new Color32(Ink.r, Ink.g, Ink.b, (byte)(255 * (ink + decor[i] * (1 - ink))));
                        continue;
                    }
                    float inkIn = Mathf.Clamp01(InkIn * Noise(x, y, 9f, 3) - edge + 1f);
                    // Patches of hatching, diagonal strokes that wander a little.
                    float hatch = 0f;
                    if (Noise(x, y, 34f, 4) > 0.6f) {
                        float stroke = Mathf.Repeat((x - y) / 6.5f + Noise(x, y, 12f, 5) * 0.8f, 1f);
                        hatch = stroke < 0.2f ? 0.6f : 0f;
                    }
                    float shade = 1f - 0.07f * Noise(x, y, 4f, 6) - 0.05f * Noise(x, y, 40f, 7);
                    var paper = new Color(Paper.r * shade, Paper.g * shade, Paper.b * shade, 1f);
                    pixels[i] = Color.Lerp(paper, Ink, Mathf.Max(inkIn, hatch));
                }
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                // The card is shown at about a third of the picture: a sharper mipmap keeps its lines from going soft.
                mipMapBias = -0.7f
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        // The small sign of the card's corner: the fist in the given colour in a dark ink line, cut to the fist.
        // The sign is shown about a third of its size, so the line is wide here to stay a line there.
        internal static Sprite Sign(string resource, Color color) {
            const int Pad = 6;
            const float Line = 2.6f;
            var source = Read(resource);
            if (source == null)
                return null;
            int sw = source.width, sh = source.height;
            var from = source.GetPixels32();
            UnityEngine.Object.Destroy(source);
            DropSpecks(from, sw, sh);
            int x0 = sw, x1 = -1, y0 = sh, y1 = -1;
            for (int i = 0; i < from.Length; i++)
                if (from[i].a > 127) {
                    int x = i % sw, y = i / sw;
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
            if (x1 < 0)
                return null;
            int w = x1 - x0 + 1 + 2 * Pad, h = y1 - y0 + 1 + 2 * Pad;
            var inside = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) {
                    int sx = x + x0 - Pad, sy = y + y0 - Pad;
                    inside[y * w + x] = sx >= 0 && sy >= 0 && sx < sw && sy < sh && from[sy * sw + sx].a > 127;
                }
            var offsets = Offsets(Pad);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) {
                    int i = y * w + x;
                    bool fill = inside[i];
                    float edge = Edge(inside, w, h, x, y, offsets, Pad);
                    if (!fill) {
                        float ink = Mathf.Clamp01(Line + 0.8f * (Noise(x, y, 6f, 8) - 0.5f) - edge + 0.5f);
                        pixels[i] = new Color32(Ink.r, Ink.g, Ink.b, (byte)(255 * ink));
                    } else {
                        pixels[i] = Color.Lerp(color, Ink, Mathf.Clamp01(1f - edge));
                    }
                }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                // The card is shown at about a third of the picture: a sharper mipmap keeps its lines from going soft.
                mipMapBias = -0.7f
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        // Offsets of a disc in order of distance: the first one of the other kind is the distance to the edge.
        private static List<(int dx, int dy, float d)> Offsets(int reach) {
            var offsets = new List<(int dx, int dy, float d)>();
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++) {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= reach)
                        offsets.Add((dx, dy, d));
                }
            offsets.Sort((a, b) => a.d.CompareTo(b.d));
            return offsets;
        }

        private static float Edge(bool[] inside, int w, int h, int x, int y, List<(int dx, int dy, float d)> offsets, int reach) {
            bool fill = inside[y * w + x];
            foreach (var (dx, dy, d) in offsets) {
                int sx = x + dx, sy = y + dy;
                bool other = sx < 0 || sy < 0 || sx >= w || sy >= h ? fill : inside[sy * w + sx] != fill;
                if (other)
                    return d;
            }
            return reach + 1;
        }

        // Faint marks on the card around the drawing, as around the star of the Map card: broken rays from the drawing
        // towards the edges and a few small signs between them. Ink at a low opacity, so they take the card's colour.
        private const float RayInk = 0.22f;
        private const float SignInk = 0.26f;
        // Signs in a square of -1…1: segments x0, y0, x1, y1 and a ring radius (0 for none).
        private static readonly (float[][] lines, float ring)[] Signs = {
            (new[] { new[] { 0f, 0f, 0f, 0.05f } }, 0.8f),
            (new[] { new[] { -1f, 0f, 1f, 0f }, new[] { 0f, -1f, 0f, 1f } }, 0f),
            (new[] { new[] { -0.9f, -0.7f, 0.9f, -0.7f }, new[] { 0.9f, -0.7f, 0f, 0.9f }, new[] { 0f, 0.9f, -0.9f, -0.7f } }, 0f),
            (new[] { new[] { 0f, -1f, 0f, 1f }, new[] { 0f, 0.4f, 0.7f, 1f }, new[] { 0f, 0f, 0.7f, 0.6f } }, 0f),
            (new[] { new[] { 0f, -1.2f, 0f, 1.2f } }, 0.75f),
            (new[] { new[] { -1f, -0.5f, -0.3f, 0.5f }, new[] { -0.3f, 0.5f, 0.3f, -0.5f }, new[] { 0.3f, -0.5f, 1f, 0.5f } }, 0f),
            (new[] { new[] { -0.8f, -0.8f, 0.8f, 0.8f }, new[] { -0.8f, 0.8f, 0.8f, -0.8f } }, 0.45f),
        };

        private static float[] Background(int w, int h, float cx, float cy) {
            var alpha = new float[w * h];
            const int Rays = 20;
            var angle = new float[Rays];
            var from = new float[Rays];
            var to = new float[Rays];
            for (int k = 0; k < Rays; k++) {
                angle[k] = (k + (Hash(k, 0, 11) - 0.5f) * 0.6f) * Mathf.PI * 2f / Rays;
                from[k] = 0.44f + 0.085f * Hash(k, 1, 11);
                to[k] = 0.65f + 0.1f * Hash(k, 2, 11);
            }
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    // Distance from the middle as a share of the card, so the rays end at the same margin all round.
                    float e = Mathf.Sqrt(dx * dx / (0.25f * w * w) + dy * dy / (0.25f * h * h));
                    if (e < 0.5f)
                        continue;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), at = Mathf.Atan2(dy, dx), a = 0f;
                    for (int k = 0; k < Rays; k++) {
                        if (e < from[k] || e > to[k])
                            continue;
                        float off = Mathf.Abs(Mathf.DeltaAngle(at * Mathf.Rad2Deg, angle[k] * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                        if (off > 1.6f || Noise(r, k * 13f, 10f, 12) < 0.3f)
                            continue;
                        float fade = Mathf.Clamp01(Mathf.Min(e - from[k], to[k] - e) / 0.05f);
                        a = Mathf.Max(a, RayInk * fade * Mathf.Clamp01(1.6f - off));
                    }
                    alpha[y * w + x] = a;
                }
            }
            // The signs on an oval around the drawing, between its rays.
            for (int n = 0; n < Signs.Length; n++) {
                float t = (n + 0.5f + (Hash(n, 3, 11) - 0.5f) * 0.4f) * Mathf.PI * 2f / Signs.Length;
                float sx = cx + Mathf.Cos(t) * 0.35f * w, sy = cy + Mathf.Sin(t) * 0.32f * h;
                float size = 11f + 4f * Hash(n, 4, 11);
                var (lines, ring) = Signs[(n * 3 + 1) % Signs.Length];
                for (int y = Mathf.FloorToInt(sy - size * 1.4f); y <= Mathf.CeilToInt(sy + size * 1.4f); y++) {
                    for (int x = Mathf.FloorToInt(sx - size * 1.4f); x <= Mathf.CeilToInt(sx + size * 1.4f); x++) {
                        if (x < 0 || y < 0 || x >= w || y >= h)
                            continue;
                        float px = (x + 0.5f - sx) / size, py = (y + 0.5f - sy) / size, d = float.MaxValue;
                        foreach (var l in lines)
                            d = Mathf.Min(d, Segment(px, py, l[0], l[1], l[2], l[3]));
                        if (ring > 0)
                            d = Mathf.Min(d, Mathf.Abs(Mathf.Sqrt(px * px + py * py) - ring));
                        float a = SignInk * Mathf.Clamp01(1.8f - d * size);
                        int i = y * w + x;
                        alpha[i] = Mathf.Max(alpha[i], a);
                    }
                }
            }
            return alpha;
        }

        private static float Segment(float px, float py, float x0, float y0, float x1, float y1) {
            float vx = x1 - x0, vy = y1 - y0;
            float t = Mathf.Clamp01(((px - x0) * vx + (py - y0) * vy) / Mathf.Max(vx * vx + vy * vy, 1e-6f));
            float dx = px - x0 - t * vx, dy = py - y0 - t * vy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // The Kingmaker picture has a small sparkle next to the fist; the WotR drawings have nothing around them. Parts of the
        // mask much smaller than the largest one are cleared.
        internal static void DropSpecks(Color32[] pixels, int w, int h) {
            var label = new int[w * h];
            var sizes = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int start = 0; start < pixels.Length; start++) {
                if (pixels[start].a == 0 || label[start] != 0)
                    continue;
                int id = sizes.Count, size = 0;
                label[start] = id;
                stack.Push(start);
                while (stack.Count > 0) {
                    int i = stack.Pop();
                    size++;
                    int x = i % w, y = i / w;
                    foreach (int j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 }) {
                        if (j >= 0 && label[j] == 0 && pixels[j].a > 0) {
                            label[j] = id;
                            stack.Push(j);
                        }
                    }
                }
                sizes.Add(size);
            }
            int largest = sizes.Max();
            for (int i = 0; i < pixels.Length; i++) {
                if (label[i] != 0 && sizes[label[i]] < largest / 10)
                    pixels[i].a = 0;
            }
        }

        private static Texture2D Read(string resource) {
            using var stream = typeof(Drawing).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
                return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            texture.LoadImage(memory.ToArray());
            return texture;
        }

        private static float Alpha(Color32[] pixels, int w, int h, float x, float y) {
            x -= 0.5f;
            y -= 0.5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float At(int px, int py) => px < 0 || py < 0 || px >= w || py >= h ? 0f : pixels[py * w + px].a / 255f;
            return Mathf.Lerp(Mathf.Lerp(At(ix, iy), At(ix + 1, iy), fx), Mathf.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), fx), fy);
        }

        // Smooth value noise in 0…1, cells of the given size in pixels.
        private static float Noise(float x, float y, float cell, int seed) {
            x /= cell;
            y /= cell;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), fx),
                Mathf.Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), fx), fy);
        }

        private static float Hash(int x, int y, int seed) {
            unchecked {
                uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                n = (n ^ (n >> 13)) * 1274126177u;
                return ((n ^ (n >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
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
