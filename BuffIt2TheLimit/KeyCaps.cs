using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModBar
{
    // Keyboard keys drawn as keys: a square with rounded corners in the style of Kingmaker's round gamepad buttons (a
    // dark outline, a grey rim lighter at the top, a black groove, a dark face), as high as a button, wider for a longer
    // name; the name on it in light letters. The key is drawn by the code, nothing is shipped.
    // Two uses: Create, a key as an element of a layout (hint bars); Inline, a key inside a text — the text keeps room
    // for it and KeyCapRoot, on the window, lays the keys over that room.
    static class KeyCaps
    {
        const int Size = 64;
        const int Radius = 15;
        const int Border = 21;
        internal static readonly Color LabelColor = new Color32(230, 223, 208, 255);

        static Texture2D s_Texture;
        static readonly Dictionary<int, Sprite> s_Sprites = new Dictionary<int, Sprite>();

        // Layers from the outside in: inset in pixels, colour at the top, colour at the bottom.
        static readonly int[] Insets = { 0, 1, 4, 5 };
        static readonly Color32[] Tops = { new Color32(33, 28, 24, 225), new Color32(90, 93, 90, 255), new Color32(16, 12, 8, 255), new Color32(40, 38, 34, 255) };
        static readonly Color32[] Bottoms = { new Color32(24, 20, 16, 220), new Color32(44, 40, 42, 255), new Color32(8, 8, 8, 255), new Color32(26, 25, 23, 255) };

        static Texture2D Texture
        {
            get
            {
                if (s_Texture == null) s_Texture = Draw();
                return s_Texture;
            }
        }

        static Texture2D Draw()
        {
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                // Texture rows go up; the gradients go down from the top.
                float down = (Size - 1 - y) / (float)(Size - 1);
                for (int x = 0; x < Size; x++)
                {
                    var c = new Color(0, 0, 0, 0);
                    for (int l = 0; l < Insets.Length; l++)
                    {
                        float cover = Cover(x, y, Insets[l], Radius - Insets[l]);
                        if (cover <= 0f) continue;
                        var layer = Color.Lerp(Tops[l], Bottoms[l], down);
                        layer.a *= cover;
                        c = Over(c, layer);
                    }
                    // A faint light over the upper half of the face.
                    if (down <= 0.5f)
                    {
                        float glow = Cover(x, y, 6, Radius - 6) * 28f / 255f;
                        if (glow > 0f) c = Over(c, new Color(1f, 1f, 1f, glow));
                    }
                    pixels[y * Size + x] = c;
                }
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "ModKeyCap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // How much of the pixel lies in the rounded square inset by the given pixels.
        static float Cover(int x, int y, int inset, int radius)
        {
            float half = Size / 2f - inset;
            float px = Mathf.Abs(x + 0.5f - Size / 2f), py = Mathf.Abs(y + 0.5f - Size / 2f);
            float qx = Mathf.Max(px - (half - radius), 0f), qy = Mathf.Max(py - (half - radius), 0f);
            float outside = Mathf.Sqrt(qx * qx + qy * qy) - radius + Mathf.Min(Mathf.Max(px - (half - radius), py - (half - radius)), 0f);
            return Mathf.Clamp01(0.5f - outside);
        }

        static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f) return new Color(0, 0, 0, 0);
            var rgb = ((Vector4)over * over.a + (Vector4)under * under.a * (1f - over.a)) / a;
            return new Color(rgb.x, rgb.y, rgb.z, a);
        }

        // The key stretched to the given height (canvas units, 100 to the unit): its corners and rim keep their size
        // against the height.
        internal static void Paint(Image image, float height)
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(height * 4f));
            Sprite sprite;
            if (!s_Sprites.TryGetValue(key, out sprite) || sprite == null)
            {
                sprite = Sprite.Create(Texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f * Size / height, 0,
                    SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
                s_Sprites[key] = sprite;
            }
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        // The name a key is shown with.
        internal static string Name(string key)
        {
            switch (key)
            {
                case "Return": return "Enter";
                case "KeypadEnter": return "Enter";
                case "Escape": return "Esc";
                case "PageUp": return "PgUp";
                case "PageDown": return "PgDn";
                case "LeftArrow": return "←";
                case "RightArrow": return "→";
                case "UpArrow": return "↑";
                case "DownArrow": return "↓";
                case "Delete": return "Del";
                case "LeftShift": case "RightShift": return "Shift";
                case "LeftAlt": case "RightAlt": return "Alt";
                case "LeftControl": case "RightControl": return "Ctrl";
                case "Mouse0": return "LMB";
                case "Mouse1": return "RMB";
                case "Mouse2": return "MMB";
            }
            if (key.Length == 6 && key.StartsWith("Alpha")) return key.Substring(5);
            if (key.StartsWith("Keypad") && key.Length == 7) return key.Substring(6);
            return key;
        }

        // The keys of a label: "W/S" is two keys.
        static string[] Split(string label)
        {
            if (label.Length > 2 && label.IndexOf('/') > 0 && label.IndexOf('/') < label.Length - 1) return label.Split('/');
            return new[] { label };
        }

        // The size of a name against the key's height: a letter fills the key more than a word.
        static float LabelSize(string name)
        {
            return name.Length <= 1 ? 0.5f : 0.38f;
        }

        // A key (or keys, for "W/S") as an element of a layout, as high as given; its parts take no clicks of their
        // own, so the element can be made clickable as a whole.
        internal static GameObject Create(Transform parent, string label, float height, TMP_FontAsset font)
        {
            var keys = Split(label);
            if (keys.Length > 1)
            {
                var group = new GameObject("Keys", typeof(RectTransform));
                group.transform.SetParent(parent, false);
                var layout = group.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = height * 0.12f;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                // A clear backing, so the group is hit as one.
                group.AddComponent<Image>().color = Color.clear;
                foreach (var k in keys) Create(group.transform, k, height, font);
                return group;
            }
            string name = Name(label.Trim());
            var go = new GameObject("Key", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            Paint(image, height);
            var text = Label(go.transform, name, height, font);
            text.ForceMeshUpdate();
            float width = Mathf.Max(height, text.GetPreferredValues(name).x + height * 0.5f);
            var element = go.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = height;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            return go;
        }

        internal const string LabelName = "KeyCapLabel";

        static TextMeshProUGUI Label(Transform parent, string name, float height, TMP_FontAsset font)
        {
            var go = new GameObject(LabelName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = name;
            text.fontSize = height * LabelSize(name);
            text.fontStyle = FontStyles.Bold;
            text.color = LabelColor;
            text.alignment = TextAlignmentOptions.Midline;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = Vector4.zero;
            text.raycastTarget = false;
            go.AddComponent<Kept>().Size = text.fontSize;
            return text;
        }

        // A text made on an inactive object takes TMP's defaults when it wakes (Kingmaker): the size and the middle
        // are set again whenever the key is shown.
        sealed class Kept : MonoBehaviour
        {
            internal float Size;

            void OnEnable()
            {
                var text = GetComponent<TextMeshProUGUI>();
                if (text == null) return;
                text.enableAutoSizing = false;
                text.fontSize = Size;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Midline;
                text.enableWordWrapping = false;
                text.margin = Vector4.zero;
                var rect = (RectTransform)transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }
        }

        // ---------- keys inside a text ----------

        const string LinkPrefix = "key:";
        // The key's height against the size of its line; the name inside, against the line.
        internal const float InlineHeight = 1.2f;

        static float InlineScale(string name)
        {
            return InlineHeight * LabelSize(name);
        }

        // Room in a text for a key (or keys, for "W/S"): its name, invisible, between spaces, as wide as the key will
        // be. The window's KeyCapRoot lays the key over it.
        internal static string Inline(string label)
        {
            var keys = Split(label);
            var parts = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string name = Name(keys[i].Trim());
                float scale = InlineScale(name);
                // A letter is about 0.65 of its size wide: the room round it makes the key square.
                float pad = name.Length <= 1 ? Mathf.Max(0.1f, (InlineHeight - scale * 0.65f) / 2f) : InlineHeight * 0.25f;
                parts[i] = "<space=" + pad.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "em><link=\"" + LinkPrefix
                    + name + "\"><size=" + Mathf.RoundToInt(scale * 100f) + "%><b><color=#00000000>" + name + "</color></b></size></link><space="
                    + pad.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "em>";
            }
            return string.Join("<space=0.1em>", parts);
        }

        static readonly Regex Marked = new Regex(@"\[\[(.+?)\]\]");

        // Help texts write keys as [[E]], [[Esc]], [[W/S]].
        internal static string Keys(string text)
        {
            if (text == null || text.IndexOf("[[", StringComparison.Ordinal) < 0) return text;
            return Marked.Replace(text, m => Inline(m.Groups[1].Value));
        }

        internal static bool HasKeys(string text)
        {
            return text != null && text.IndexOf("\"" + LinkPrefix, StringComparison.Ordinal) >= 0;
        }

        internal const string CapName = "KeyCap";

        // The keys of a text over the room it keeps for them; the old ones go.
        internal static void Place(TMP_Text text)
        {
            for (int i = text.transform.childCount - 1; i >= 0; i--)
            {
                var child = text.transform.GetChild(i);
                if (child.name == CapName) UnityEngine.Object.Destroy(child.gameObject);
            }
            if (!HasKeys(text.text)) return;
            text.ForceMeshUpdate();
            var info = text.textInfo;
            for (int l = 0; l < info.linkCount; l++)
            {
                var link = info.linkInfo[l];
                string id = link.GetLinkID();
                if (!id.StartsWith(LinkPrefix, StringComparison.Ordinal) || link.linkTextLength == 0) continue;
                int firstIndex = link.linkTextfirstCharacterIndex, lastIndex = firstIndex + link.linkTextLength - 1;
                if (lastIndex >= info.characterCount) continue;
                var first = info.characterInfo[firstIndex];
                var last = info.characterInfo[lastIndex];
                // A line folded inside the name: no key.
                if (first.lineNumber != last.lineNumber) continue;
                string name = id.Substring(LinkPrefix.Length);
                float line = first.pointSize / InlineScale(name);
                float height = line * InlineHeight;
                float word = last.xAdvance - first.origin;
                float width = Mathf.Max(height, word + height * 0.5f);
                // The middle of the line's capitals.
                float middle = first.baseLine + line * 0.36f;
                var go = new GameObject(CapName, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(text.transform, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                // The text's local space starts at its pivot.
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchorMin = rect.anchorMax = text.rectTransform.pivot;
                rect.anchoredPosition = new Vector2((first.origin + last.xAdvance) / 2f, middle);
                rect.sizeDelta = new Vector2(width, height);
                var image = go.AddComponent<Image>();
                Paint(image, height);
                image.raycastTarget = false;
                var label = Label(go.transform, name, height, text.font);
                label.fontSize = first.pointSize;
            }
        }
    }

    // On a window: lays the keys of its texts (KeyCaps.Inline) over the room the texts keep for them, again whenever a
    // text or its size changes.
    class KeyCapRoot : MonoBehaviour
    {
        class State
        {
            public string Text;
            public Vector2 Size;
        }

        readonly Dictionary<TMP_Text, State> m_States = new Dictionary<TMP_Text, State>();
        readonly List<TMP_Text> m_Texts = new List<TMP_Text>();
        int m_NextScan;

        internal static void Attach(GameObject window)
        {
            if (window.GetComponent<KeyCapRoot>() == null) window.AddComponent<KeyCapRoot>();
        }

        void LateUpdate()
        {
            if (Time.frameCount >= m_NextScan)
            {
                m_NextScan = Time.frameCount + 20;
                m_Texts.Clear();
                foreach (var t in GetComponentsInChildren<TMP_Text>(false))
                    if (t.name != KeyCaps.LabelName) m_Texts.Add(t);
            }
            foreach (var text in m_Texts)
            {
                if (text == null || !text.isActiveAndEnabled) continue;
                State state;
                m_States.TryGetValue(text, out state);
                string now = text.text;
                var size = text.rectTransform.rect.size;
                if (state == null)
                {
                    // A text without keys that never had any: nothing to do.
                    if (!KeyCaps.HasKeys(now)) continue;
                    state = new State();
                    m_States[text] = state;
                }
                else if (ReferenceEquals(state.Text, now) && state.Size == size) continue;
                state.Text = now;
                state.Size = size;
                try
                {
                    KeyCaps.Place(text);
                }
                catch (Exception) { }
            }
        }
    }
}
