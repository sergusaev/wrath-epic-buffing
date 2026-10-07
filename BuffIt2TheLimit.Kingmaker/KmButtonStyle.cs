using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuffIt2TheLimit {

    // Bar buttons in the look of the Kingmaker menu buttons: the dark square in a bronze frame with the mod's fist in the
    // pale olive of the game's pictograms; hovered, as the game's: a red square in a gold frame, the fist in gold. The game has no empty button, so
    // the square is rebuilt from its menu buttons: at every pixel the pictograms differ and the square does not, so the
    // median of the samples that look like the background, without the bright pictograms and their dark outlines, is
    // the square itself. Nothing of the game is stored: it is drawn from the loaded sprites once per session.
    internal static class KmButtonStyle {

        private const int Size = 106;
        private const int Edge = 12;
        private static readonly string[] MenuButtons = {
            "Inventory", "Character", "Journal", "Map", "Rest", "Esc", "Stop", "Hold", "SelectAll", "Spellbook", "Formation"
        };
        // Light parts of the game's pictograms, top and bottom rows (measured on its menu buttons).
        private static readonly Color TopPlain = new Color32(160, 152, 96, 255);
        private static readonly Color BottomPlain = new Color32(112, 106, 68, 255);
        private static readonly Color TopGold = new Color32(238, 222, 111, 255);
        private static readonly Color BottomGold = new Color32(146, 133, 69, 255);

        private static bool tried;
        private static Color32[] normalBase;
        private static Color32[] hoverBase;
        private static readonly Dictionary<string, Sprite> Cache = new();

        public enum State { Normal, Hover, Down }

        // Null when the game's menu buttons are not loaded; the bar then keeps the mod's own icons.
        public static Sprite Get(string icon, Texture2D source, State state) {
            string key = icon + "_" + state;
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            Sprite sprite = null;
            try {
                if (Prepare() && source != null)
                    sprite = state == State.Normal
                        ? Compose(source, normalBase, TopPlain, BottomPlain, 1.25f)
                        : Compose(source, hoverBase, TopGold, BottomGold, state == State.Hover ? 1.2f : 1f);
            } catch (Exception ex) {
                Main.Error(ex, "KmButtonStyle " + key);
            }
            Cache[key] = sprite;
            return sprite;
        }

        public static bool Ready => normalBase != null;

        // The menu buttons are loaded with the HUD; a bar built before it tries again once the HUD is there.
        public static void Retry() {
            if (normalBase != null)
                return;
            tried = false;
            Cache.Clear();
        }

        private static bool Prepare() {
            if (tried)
                return normalBase != null;
            tried = true;
            var sprites = UnityEngine.Resources.FindObjectsOfTypeAll<Sprite>()
                .GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
            normalBase = Square(sprites, "Normal");
            hoverBase = Square(sprites, "Hover");
            if (normalBase == null || hoverBase == null)
                normalBase = hoverBase = null;
            Main.Log($"[PAD] bar buttons: Kingmaker style {(normalBase != null ? "built" : "unavailable, mod icons")}");
            return normalBase != null;
        }

        private static Color32[] Square(Dictionary<string, Sprite> sprites, string state) {
            var samples = new List<Color32[]>();
            foreach (var name in MenuButtons) {
                if (sprites.TryGetValue($"Ingame_Menu_{name}_{state}", out var sprite))
                    samples.Add(Read(sprite));
            }
            if (samples.Count < 5)
                return null;
            int n = samples.Count;
            // Brightness of the plain square next to the frame, where no pictogram reaches.
            var inner = new List<float>();
            foreach (var s in samples)
                for (int y = 30; y < 76; y++)
                    for (int x = 16; x < 22; x++)
                        inner.Add(Lum(s[y * Size + x]));
            inner.Sort();
            float plain = inner[inner.Count / 2];
            var result = new Color32[Size * Size];
            var pick = new List<Color32>(n);
            for (int i = 0; i < result.Length; i++) {
                int x = i % Size, y = i / Size;
                pick.Clear();
                bool frame = x < 10 || y < 10 || x >= Size - 10 || y >= Size - 10;
                foreach (var s in samples) {
                    float l = Lum(s[i]);
                    if (frame || (l > plain * 0.65f && l < plain * 1.5f))
                        pick.Add(s[i]);
                }
                if (pick.Count == 0)
                    pick.AddRange(samples.Select(s => s[i]));
                pick.Sort((a, b) => Lum(a).CompareTo(Lum(b)));
                result[i] = pick[pick.Count / 2];
            }
            return result;
        }

        private static Sprite Compose(Texture2D icon, Color32[] square, Color top, Color bottom, float bright) {
            var mask = new float[Size * Size];
            var lum = new float[Size * Size];
            for (int y = Edge; y < Size - Edge; y++) {
                for (int x = Edge; x < Size - Edge; x++) {
                    var c = icon.GetPixelBilinear((x + 0.5f) / Size, (y + 0.5f) / Size);
                    float l = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) * 255f;
                    lum[y * Size + x] = l;
                    mask[y * Size + x] = Mathf.Clamp01((l - 70f) / 60f) * c.a;
                }
            }
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++) {
                // Rows go bottom up; the pictogram is lighter at the top.
                var gold = Color.Lerp(bottom, top, (float)y / (Size - 1));
                for (int x = 0; x < Size; x++) {
                    int i = y * Size + x;
                    // A dark outline around the pictogram, as the game's have.
                    float shadow = 0f;
                    for (int dy = -2; dy <= 1; dy++)
                        for (int dx = -1; dx <= 2; dx++) {
                            int sx = x - dx, sy = y - dy;
                            if (sx >= 0 && sy >= 0 && sx < Size && sy < Size)
                                shadow = Mathf.Max(shadow, mask[sy * Size + sx]);
                        }
                    Color back = square[i];
                    back = new Color(back.r * (1 - 0.75f * shadow), back.g * (1 - 0.75f * shadow), back.b * (1 - 0.75f * shadow), back.a);
                    float shade = Mathf.Clamp(lum[i] / 200f, 0.55f, 1f) * bright;
                    var front = new Color(Mathf.Clamp01(gold.r * shade), Mathf.Clamp01(gold.g * shade), Mathf.Clamp01(gold.b * shade), 1f);
                    float m = mask[i];
                    pixels[i] = new Color(Mathf.Lerp(back.r, front.r, m), Mathf.Lerp(back.g, front.g, m), Mathf.Lerp(back.b, front.b, m), back.a);
                }
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }

        // The game's sprites are not readable: the GPU copies the sprite's part of its texture, scaled to Size.
        private static Color32[] Read(Sprite sprite) {
            var texture = sprite.texture;
            var rect = sprite.textureRect;
            var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            try {
                Graphics.Blit(texture, target, new Vector2(rect.width / texture.width, rect.height / texture.height),
                    new Vector2(rect.x / texture.width, rect.y / texture.height));
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply();
                return image.GetPixels32();
            } finally {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }

        private static float Lum(Color32 c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
    }
}
