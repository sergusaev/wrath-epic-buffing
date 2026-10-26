using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModBar
{
    // The pictures of the game's own settings controls: the slider, the on/off switch, the choice with arrows, the button.
    // Nothing of the game's art is shipped with the mods. Kingmaker: the sprite when the game has it loaded, else its raw
    // RGBA32 pixels read from the game's file (as HintTile), checked against a sum of them. WotR: the sprite loaded by its
    // address from the game's open "ui" bundle. A part that is not found is drawn as a plain shape.
    static class GameArt
    {
        internal enum Part { Track, Handle, Grip, On, Off, MarkOn, MarkOff, Frame, ArrowBack, Arrow, Button, ButtonHover, ButtonDown }

#if KINGMAKER
        struct Source
        {
            public string Name, File;
            public long Offset;
            public int Width, Height;
            public Vector4 Border;
            public uint Sum;

            public Source(string name, string file, long offset, int width, int height, Vector4 border, uint sum)
            {
                Name = name;
                File = file;
                Offset = offset;
                Width = width;
                Height = height;
                Border = border;
                Sum = sum;
            }
        }

        // Kingmaker's settings: SliderArea_H with the main_menu_active handle and its grips; the on and off buttons of a
        // switch with the tick and the cross; the choice's frame between the round arrow buttons of the inventory filter.
        static readonly Dictionary<Part, Source> Sources = new Dictionary<Part, Source>
        {
            { Part.Track, new Source("SliderArea_H", "sharedassets1.assets.resS", 116808640L, 1438, 54, new Vector4(38, 0, 36, 0), 0x6EC40030u) },
            { Part.Handle, new Source("main_menu_active", "sharedassets1.assets.resS", 71108864L, 449, 71, new Vector4(8, 8, 8, 8), 0x23CD1145u) },
            { Part.On, new Source("main_menu_active", "sharedassets1.assets.resS", 71108864L, 449, 71, new Vector4(8, 8, 8, 8), 0x23CD1145u) },
            { Part.Off, new Source("main_menu", "sharedassets1.assets.resS", 151793332L, 449, 71, new Vector4(8, 8, 8, 8), 0xF44293D9u) },
            { Part.Grip, new Source("ScrollBar_Small_Handler_H", "sharedassets1.assets.resS", 71021352L, 63, 22, Vector4.zero, 0xF41C2858u) },
            { Part.Frame, new Source("blockframe", "sharedassets1.assets.resS", 99014692L, 335, 330, new Vector4(4, 4, 4, 4), 0xE7C765C3u) },
            { Part.ArrowBack, new Source("Inventory_Filter_Icon_Background_Normal", "sharedassets4.assets.resS", 93584308L, 86, 86, Vector4.zero, 0xB2519AE7u) },
            { Part.Arrow, new Source("Inventory_Filter_Arrow", "sharedassets4.assets.resS", 76647748L, 42, 42, Vector4.zero, 0xC590A361u) },
            { Part.Button, new Source("button_normal", "sharedassets4.assets.resS", 112274940L, 449, 71, new Vector4(16, 16, 16, 16), 0x5E5839A8u) },
            { Part.ButtonHover, new Source("button_hover", "sharedassets4.assets.resS", 70341144L, 449, 71, new Vector4(16, 16, 16, 16), 0x23CD1145u) },
            { Part.ButtonDown, new Source("button_pressed", "sharedassets4.assets.resS", 43605564L, 449, 71, new Vector4(16, 16, 16, 16), 0xBF8DDF72u) },
            { Part.MarkOn, new Source("globalmap_ok", "sharedassets6.assets.resS", 48856620L, 32, 35, Vector4.zero, 0xD007F93Du) },
            { Part.MarkOff, new Source("spellbook_esc", "sharedassets6.assets.resS", 54456088L, 29, 29, Vector4.zero, 0xD8ECFC7Bu) },
        };
#else
        // WotR's settings: UI_WightLine with the UI_Settings_ValueScroll handle; UI_Settings_BackValue (on, a choice) and
        // UI_Settings_BackValueBlue (off) with the arrows of the journal; UI_Button_*. Their addresses in the "ui" bundle.
        static readonly Dictionary<Part, string> Addresses = new Dictionary<Part, string>
        {
            { Part.Track, "3f5d404820135f64ba330df3db09b976" },
            { Part.Handle, "44ff4f33f84757f48b71fbba28221022" },
            { Part.On, "e0546af9f8826a6468161f3d5baa89d3" },
            { Part.Off, "9ed5cb78afa3c194fa522494a2321b9e" },
            { Part.Frame, "e0546af9f8826a6468161f3d5baa89d3" },
            { Part.Arrow, "be5d5bcb9bd801d41a8b15302633c9db" },
            { Part.Button, "f4f3f84cec4a38e46b8abc7c226f45ae" },
            { Part.ButtonHover, "33215aafa1c8db14ea7b4a2c9e4c5705" },
            { Part.ButtonDown, "6e250355a74596848b99df183a41e4b6" },
        };
#endif

        static readonly Dictionary<Part, Sprite> s_Sprites = new Dictionary<Part, Sprite>();
        static readonly HashSet<Part> s_Tried = new HashSet<Part>();

        internal static Sprite Get(Part part)
        {
            Sprite sprite;
            if (s_Sprites.TryGetValue(part, out sprite)) return sprite;
            if (s_Tried.Contains(part)) return null;
            s_Tried.Add(part);
            try
            {
                sprite = Load(part);
            }
            catch (Exception)
            {
                sprite = null;
            }
            if (sprite != null) s_Sprites[part] = sprite;
            return sprite;
        }

#if KINGMAKER
        static Dictionary<string, Sprite> s_Loaded;
        static readonly Dictionary<string, Texture2D> s_Read = new Dictionary<string, Texture2D>();

        static Sprite Load(Part part)
        {
            Source source;
            if (!Sources.TryGetValue(part, out source)) return null;
            if (s_Loaded == null)
            {
                var names = new HashSet<string>(Sources.Values.Select(s => s.Name));
                s_Loaded = new Dictionary<string, Sprite>();
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (s != null && names.Contains(s.name) && !s_Loaded.ContainsKey(s.name) && s.rect.width == SourceOf(s.name).Width)
                        s_Loaded[s.name] = s;
            }
            Sprite loaded;
            if (s_Loaded.TryGetValue(source.Name, out loaded) && loaded != null)
                return Sprite.Create(loaded.texture, loaded.textureRect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, source.Border);
            var texture = Read(source);
            if (texture == null) return null;
            return Sprite.Create(texture, new Rect(0, 0, source.Width, source.Height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, source.Border);
        }

        static Source SourceOf(string name)
        {
            return Sources.Values.First(s => s.Name == name);
        }

        // The pixels at their place in the game's file; another version of the game gives another sum, and no picture.
        static Texture2D Read(Source source)
        {
            Texture2D texture;
            if (s_Read.TryGetValue(source.Name, out texture)) return texture;
            s_Read[source.Name] = null;
            var path = Path.Combine(Application.dataPath, source.File);
            var bytes = new byte[source.Width * source.Height * 4];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < source.Offset + bytes.Length) return null;
                stream.Seek(source.Offset, SeekOrigin.Begin);
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) return null;
                    read += n;
                }
            }
            uint sum = 0x811C9DC5u;
            for (int i = 0; i < bytes.Length; i += 64) sum = unchecked((sum ^ bytes[i]) * 0x01000193u);
            if (sum != source.Sum) return null;
            texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false) { name = source.Name, wrapMode = TextureWrapMode.Clamp };
            texture.LoadRawTextureData(bytes);
            texture.Apply(false, true);
            s_Read[source.Name] = texture;
            return texture;
        }
#else
        static AssetBundle s_Bundle;

        static Sprite Load(Part part)
        {
            string address;
            if (!Addresses.TryGetValue(part, out address)) return null;
            if (s_Bundle == null)
                s_Bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b != null && !b.isStreamedSceneAssetBundle && b.Contains(address));
            if (s_Bundle == null) return null;
            return s_Bundle.LoadAsset<Sprite>(address);
        }
#endif

#if KINGMAKER
        static readonly Dictionary<KeyValuePair<Part, int>, Sprite> s_Scaled = new Dictionary<KeyValuePair<Part, int>, Sprite>();
#endif

        // The part on the image at the given height (in canvas units, 100 to the unit): the borders of a stretched part
        // stay as large against the height as in the game's picture. False when the part is missing; the image then
        // shows the plain colour.
        internal static bool Paint(Image image, Part part, float height, Color plain)
        {
            var sprite = Get(part);
            if (sprite == null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = plain;
                return false;
            }
            image.color = Color.white;
            bool sliced = sprite.border != Vector4.zero;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = !sliced;
            if (!sliced)
            {
                image.sprite = sprite;
                return true;
            }
#if KINGMAKER
            var key = new KeyValuePair<Part, int>(part, Mathf.RoundToInt(height * 10f));
            Sprite scaled;
            if (!s_Scaled.TryGetValue(key, out scaled))
            {
                scaled = Sprite.Create(sprite.texture, sprite.textureRect, new Vector2(0.5f, 0.5f), 100f * sprite.rect.height / height, 0,
                    SpriteMeshType.FullRect, sprite.border);
                s_Scaled[key] = scaled;
            }
            image.sprite = scaled;
#else
            image.sprite = sprite;
            image.pixelsPerUnitMultiplier = 100f * sprite.rect.height / (sprite.pixelsPerUnit * height);
#endif
            return true;
        }

        // The width the part takes at the given height, as drawn by the game (for the parts kept whole).
        internal static float WidthAt(Part part, float height, float plain)
        {
            var sprite = Get(part);
            return sprite != null ? sprite.rect.width * height / sprite.rect.height : plain;
        }
    }

    // A control at the right of a row of a mod's window, in the look of the game's settings: a slider with the value on
    // its handle, an on/off switch, a choice between arrows, a button. The window keeps its own rows, cursor and gamepad
    // keys and tells the control what to show at every render; the control only draws it and takes the mouse:
    // Step(-1 or 1) for an arrow or a side of a switch, Slide(index) when the handle is dragged or the track clicked,
    // Press for a button. Text is centred on the visible middle of its letters (Midline).
    class ModControl : MonoBehaviour
    {
        internal enum Kind { None, Slider, Switch, Choice, Button }

#if KINGMAKER
        static readonly Color Light = new Color32(0xEE, 0xE2, 0xC2, 0xFF);
        static readonly Color Faded = new Color32(0xB8, 0xA8, 0x8A, 0xFF);
        static readonly Color ChoiceText = new Color32(0x2B, 0x17, 0x0D, 0xFF);
#else
        static readonly Color Light = new Color32(0xDC, 0xD6, 0xCC, 0xFF);
        static readonly Color Faded = new Color32(0xDC, 0xD6, 0xCC, 0xFF);
        static readonly Color ChoiceText = Color.white;
#endif
        static readonly Color PlainDark = new Color(0.18f, 0.15f, 0.12f, 0.9f);
        static readonly Color PlainMid = new Color(0.4f, 0.33f, 0.24f, 0.9f);
        static readonly Color PlainLight = new Color(0.62f, 0.52f, 0.36f, 0.9f);

        internal Action<int> Step;
        internal Action<int> Slide;
        internal Action Press;
        // A page gives its plates one width: the widest word they may show (Need); a plate is never narrower than this.
        internal float Least;

        Kind m_Kind;
        float m_Height;
        // The width given at creation; a choice or a switch with a longer word grows up to twice that.
        float m_Width;
        LayoutElement m_Element;
        TMP_Text m_Style;
        RectTransform m_Slider, m_Switch, m_Choice, m_Button;

        // Slider
        RectTransform m_Track, m_Handle;
        TextMeshProUGUI m_HandleText;
        RectTransform m_GripLeft, m_GripRight;
        int m_Count;
        int m_Index;
        // Switch
        Image m_OffBack, m_OnBack;
        TextMeshProUGUI m_OffText, m_OnText;
        Image m_OffMark, m_OnMark;
        Image m_Plate;
        TextMeshProUGUI m_PlateText;
        // Choice
        TextMeshProUGUI m_ChoiceText;
        // Button
        Image m_ButtonBack;
        TextMeshProUGUI m_ButtonText;
        bool m_ButtonOver, m_ButtonDown;

        internal Kind Shown { get { return m_Kind; } }

        // The control as the last child of the row, as wide and high as given. A row with a layout group lays it out
        // after its texts (hidden, it takes no room); in another it stands at the right edge, right units in.
        internal static ModControl Create(RectTransform row, TMP_Text style, float width, float height, float right = 0f)
        {
            var go = new GameObject("Control", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(row, false);
            var element = go.AddComponent<LayoutElement>();
            if (row.GetComponent<HorizontalOrVerticalLayoutGroup>() != null)
            {
                element.minWidth = element.preferredWidth = width;
                element.minHeight = element.preferredHeight = height;
                element.flexibleWidth = 0f;
            }
            else
            {
                element.ignoreLayout = true;
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(-right, 0f);
            }
            rect.sizeDelta = new Vector2(width, height);
            var control = go.AddComponent<ModControl>();
            control.m_Height = height;
            control.m_Width = width;
            control.m_Element = element;
            control.m_Style = style;
            go.SetActive(false);
            return control;
        }

        internal void Hide()
        {
            Show(Kind.None);
        }

        // The labels and their size: a text made on an inactive object takes TMP's default size when it wakes, so the
        // size is set again once shown.
        readonly List<KeyValuePair<TextMeshProUGUI, float>> m_Labels = new List<KeyValuePair<TextMeshProUGUI, float>>();

        void Show(Kind kind)
        {
            m_Kind = kind;
            gameObject.SetActive(kind != Kind.None);
            // Back to the given width; a choice or a switch widens again for its word (Fit).
            var rect = (RectTransform)transform;
            if (m_Width > 0f && !Mathf.Approximately(rect.sizeDelta.x, m_Width))
            {
                rect.sizeDelta = new Vector2(m_Width, rect.sizeDelta.y);
                if (m_Element != null && !m_Element.ignoreLayout) m_Element.minWidth = m_Element.preferredWidth = m_Width;
            }
            foreach (var label in m_Labels)
                if (!label.Key.enableAutoSizing || label.Key.fontSizeMax != label.Value)
                {
                    label.Key.enableAutoSizing = true;
                    label.Key.fontSizeMax = label.Value;
                    label.Key.fontSizeMin = label.Value * 0.6f;
                    label.Key.fontSize = label.Value;
                }
            if (m_Slider != null) m_Slider.gameObject.SetActive(kind == Kind.Slider);
            if (m_Switch != null) m_Switch.gameObject.SetActive(kind == Kind.Switch);
            if (m_Choice != null) m_Choice.gameObject.SetActive(kind == Kind.Choice);
            if (m_Button != null) m_Button.gameObject.SetActive(kind == Kind.Button);
        }

        // ---------- slider ----------

        // The value at index of count positions; the label on the handle.
        internal void SetSlider(int index, int count, string label)
        {
            if (m_Slider == null) BuildSlider();
            Show(Kind.Slider);
            m_Count = Math.Max(1, count);
            m_Index = Mathf.Clamp(index, 0, m_Count - 1);
            m_HandleText.text = label;
            PlaceHandle();
        }

        void BuildSlider()
        {
            float h = m_Height;
            m_Slider = Box("Slider", transform);
            m_Track = Box("Track", m_Slider);
            m_Track.anchorMin = new Vector2(0f, 0.5f);
            m_Track.anchorMax = new Vector2(1f, 0.5f);
#if KINGMAKER
            float trackHeight = h * 0.62f;
#else
            float trackHeight = h * 0.56f;
#endif
            m_Track.sizeDelta = new Vector2(0f, trackHeight);
            var track = m_Track.gameObject.AddComponent<Image>();
            if (!GameArt.Paint(track, GameArt.Part.Track, trackHeight, PlainDark)) m_Track.sizeDelta = new Vector2(0f, h * 0.2f);
            var hit = m_Slider.gameObject.AddComponent<SliderHit>();
            hit.Control = this;
            // The whole strip takes the mouse.
            m_Slider.gameObject.AddComponent<Image>().color = Color.clear;

#if KINGMAKER
            float gripHeight = h * 0.32f;
            float gripWidth = GameArt.WidthAt(GameArt.Part.Grip, gripHeight, gripHeight * 2.8f);
            // The grips run along the track only: cut off at its ornamented ends.
            var grips = Box("Grips", m_Slider);
            grips.sizeDelta = new Vector2(-2f * Inset, 0f);
            grips.gameObject.AddComponent<RectMask2D>();
            m_GripLeft = Picture("GripLeft", grips, GameArt.Part.Grip, gripWidth, gripHeight, PlainMid);
            m_GripRight = Picture("GripRight", grips, GameArt.Part.Grip, gripWidth, gripHeight, PlainMid);
            m_GripRight.localScale = new Vector3(-1f, 1f, 1f);
#endif
            m_Handle = Box("Handle", m_Slider);
            m_Handle.anchorMin = m_Handle.anchorMax = new Vector2(0f, 0.5f);
#if KINGMAKER
            float handleHeight = h * 0.94f;
#else
            float handleHeight = h;
#endif
            var handle = m_Handle.gameObject.AddComponent<Image>();
            GameArt.Paint(handle, GameArt.Part.Handle, handleHeight, PlainMid);
            m_Handle.sizeDelta = new Vector2(HandleWidth, handleHeight);
            m_HandleText = Label(m_Handle, Light);
        }

        float HandleWidth
        {
#if KINGMAKER
            get { return m_Height * 2.1f; }
#else
            get { return m_Height * 2.7f; }
#endif
        }

        // The ends of the track are its ornaments: the handle runs between them.
        float Inset
        {
#if KINGMAKER
            get { return m_Height * 0.45f; }
#else
            get { return m_Height * 0.3f; }
#endif
        }

        float m_PlacedWidth;

        // A layout group sizes the control after it was shown: the handle follows.
        void LateUpdate()
        {
            if (m_Kind == Kind.Slider && !Mathf.Approximately(((RectTransform)transform).rect.width, m_PlacedWidth)) PlaceHandle();
        }

        void PlaceHandle()
        {
            float width = ((RectTransform)transform).rect.width;
            m_PlacedWidth = width;
            if (width <= 0f) width = ((RectTransform)transform).sizeDelta.x;
            float travel = Mathf.Max(0f, width - 2f * Inset - HandleWidth);
            float t = m_Count > 1 ? (float)m_Index / (m_Count - 1) : 0f;
            float x = Inset + HandleWidth / 2f + travel * t;
            m_Handle.anchoredPosition = new Vector2(x, 0f);
            if (m_GripLeft != null)
            {
                float gap = HandleWidth / 2f + m_GripLeft.sizeDelta.x / 2f;
                m_GripLeft.anchoredPosition = new Vector2(x - Inset - gap, 0f);
                m_GripRight.anchoredPosition = new Vector2(x - Inset + gap, 0f);
            }
        }

        internal void SlideTo(Vector2 local)
        {
            if (m_Count <= 1 || Slide == null) return;
            float width = ((RectTransform)transform).rect.width;
            float travel = Mathf.Max(1f, width - 2f * Inset - HandleWidth);
            float t = Mathf.Clamp01((local.x - Inset - HandleWidth / 2f) / travel);
            int index = Mathf.RoundToInt(t * (m_Count - 1));
            if (index == m_Index) return;
            m_Index = index;
            Slide(index);
        }

        class SliderHit : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public ModControl Control;

            public void OnPointerDown(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left) Move(e);
            }

            public void OnDrag(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left) Move(e);
            }

            void Move(PointerEventData e)
            {
                Vector2 local;
                var rect = (RectTransform)Control.transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out local)) return;
                Control.SlideTo(new Vector2(local.x - rect.rect.xMin, local.y));
            }
        }

        // ---------- switch ----------

        internal void SetSwitch(bool on, string onLabel, string offLabel)
        {
            if (m_Switch == null) BuildSwitch();
            Show(Kind.Switch);
#if KINGMAKER
            GameArt.Paint(m_OnBack, on ? GameArt.Part.On : GameArt.Part.Off, m_Height, on ? PlainLight : PlainDark);
            GameArt.Paint(m_OffBack, on ? GameArt.Part.Off : GameArt.Part.On, m_Height, on ? PlainDark : PlainLight);
            m_OnText.text = onLabel;
            m_OffText.text = offLabel;
            m_OnText.color = on ? Light : Faded;
            m_OffText.color = on ? Faded : Light;
            m_OnText.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            m_OffText.fontStyle = on ? FontStyles.Normal : FontStyles.Bold;
            m_OnMark.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            m_OffMark.color = on ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
#else
            GameArt.Paint(m_Plate, on ? GameArt.Part.On : GameArt.Part.Off, m_Height, on ? PlainLight : PlainMid);
            m_PlateText.text = on ? onLabel : offLabel;
            Fit(m_PlateText, ArrowRoom);
#endif
            m_On = on;
        }

        bool m_On;

        void BuildSwitch()
        {
            m_Switch = Box("Switch", transform);
#if KINGMAKER
            // Two buttons side by side as Kingmaker's on/off setting: the cross and Off, the tick and On; the chosen one lit.
            float gap = m_Height * 0.15f;
            var off = Half(m_Switch, 0f, 0.5f, -gap / 2f, out m_OffBack);
            var on = Half(m_Switch, 0.5f, 1f, gap / 2f, out m_OnBack);
            m_OffMark = Mark(off, GameArt.Part.MarkOff);
            m_OffText = Label(off, Light);
            m_OnMark = Mark(on, GameArt.Part.MarkOn);
            m_OnText = Label(on, Light);
            Centre(off, m_OffMark, m_OffText);
            Centre(on, m_OnMark, m_OnText);
            Click(off.gameObject, () => { if (m_On && Step != null) Step(-1); });
            Click(on.gameObject, () => { if (!m_On && Step != null) Step(1); });
#else
            // WotR's on/off setting: one plate, its colour and word telling the state, an arrow at each end.
            m_Plate = m_Switch.gameObject.AddComponent<Image>();
            Arrows(m_Switch);
            m_PlateText = Label(m_Switch, ChoiceText);
            Between(m_PlateText, ArrowRoom);
            Click(m_Switch.gameObject, () => { if (Step != null) Step(m_On ? -1 : 1); });
#endif
        }

#if KINGMAKER
        RectTransform Half(RectTransform parent, float from, float to, float shift, out Image back)
        {
            var half = Box("Half", parent);
            half.anchorMin = new Vector2(from, 0f);
            half.anchorMax = new Vector2(to, 1f);
            half.sizeDelta = new Vector2(-Mathf.Abs(shift) * 2f, 0f);
            half.anchoredPosition = new Vector2(shift, 0f);
            back = half.gameObject.AddComponent<Image>();
            return half;
        }

        Image Mark(RectTransform parent, GameArt.Part part)
        {
            float size = m_Height * 0.5f;
            var mark = Picture("Mark", parent, part, size, size, PlainLight);
            return mark.GetComponent<Image>();
        }

        // The mark and the word as one group in the middle of the button: the group's width is measured, the mark goes
        // at its left, the word after it.
        void Centre(RectTransform button, Image mark, TextMeshProUGUI text)
        {
            var group = button.gameObject.AddComponent<CentreGroup>();
            group.Mark = (RectTransform)mark.transform;
            group.Text = text;
            group.Gap = m_Height * 0.2f;
        }

        class CentreGroup : MonoBehaviour
        {
            public RectTransform Mark;
            public TextMeshProUGUI Text;
            public float Gap;
            string m_Laid;

            void LateUpdate()
            {
                if (m_Laid == Text.text) return;
                m_Laid = Text.text;
                float word = Text.GetPreferredValues(Text.text).x;
                float mark = Mark.sizeDelta.x;
                float total = mark + Gap + word;
                Mark.anchorMin = Mark.anchorMax = new Vector2(0.5f, 0.5f);
                Mark.anchoredPosition = new Vector2(-total / 2f + mark / 2f, 0f);
                var rect = Text.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(word + 2f, ((RectTransform)transform).rect.height);
                rect.anchoredPosition = new Vector2(-total / 2f + mark + Gap + word / 2f, 0f);
            }
        }
#endif

        // The room the arrows of a WotR plate take at each end, the word kept out of it.
        float ArrowRoom
        {
            get { return m_Height * 1.1f; }
        }

        static void Between(TextMeshProUGUI text, float room)
        {
            text.rectTransform.sizeDelta = new Vector2(-2f * room, 0f);
        }

        // The control as wide as its word at full size needs, with room at each end; never narrower than given, at
        // most twice as wide (a longer word is made smaller).
        void Fit(TextMeshProUGUI text, float room)
        {
            float width = Mathf.Max(Wide(text, text.text, room), Least);
            var rect = (RectTransform)transform;
            if (Mathf.Approximately(rect.sizeDelta.x, width)) return;
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            if (m_Element != null && !m_Element.ignoreLayout) m_Element.minWidth = m_Element.preferredWidth = width;
        }

        float Wide(TextMeshProUGUI text, string word, float room)
        {
            bool auto = text.enableAutoSizing;
            float size = text.fontSize;
            text.enableAutoSizing = false;
            text.fontSize = m_Labels.Find(l => l.Key == text).Value;
            float width = text.GetPreferredValues(word).x;
            text.fontSize = size;
            text.enableAutoSizing = auto;
            return Mathf.Clamp(width + 2f * room + m_Height * 0.3f, m_Width, m_Width * 2f);
        }

        // The width a choice (and in WotR a switch: the same plate) needs for this word.
        internal float Need(string word)
        {
            if (m_Choice == null)
            {
                BuildChoice();
                m_Choice.gameObject.SetActive(m_Kind == Kind.Choice);
            }
            return Wide(m_ChoiceText, word, ChoiceRoom);
        }

        // ---------- choice ----------

#if KINGMAKER
        float ChoiceRoom { get { return m_Height * 1.27f; } }
#else
        float ChoiceRoom { get { return ArrowRoom; } }
#endif

        internal void SetChoice(string label)
        {
            if (m_Choice == null) BuildChoice();
            Show(Kind.Choice);
            m_ChoiceText.text = label;
            Fit(m_ChoiceText, ChoiceRoom);
        }

        void BuildChoice()
        {
            m_Choice = Box("Choice", transform);
#if KINGMAKER
            // Kingmaker's choice: the value in a thin frame between two round arrow buttons.
            float size = m_Height;
            var left = ArrowButton(m_Choice, size, -1);
            var right = ArrowButton(m_Choice, size, 1);
            left.anchorMin = left.anchorMax = new Vector2(0f, 0.5f);
            left.anchoredPosition = new Vector2(size / 2f, 0f);
            right.anchorMin = right.anchorMax = new Vector2(1f, 0.5f);
            right.anchoredPosition = new Vector2(-size / 2f, 0f);
            var frame = Box("Frame", m_Choice);
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(1f, 1f);
            frame.sizeDelta = new Vector2(-2f * (size + m_Height * 0.12f), 0f);
            GameArt.Paint(frame.gameObject.AddComponent<Image>(), GameArt.Part.Frame, m_Height, PlainMid);
            m_ChoiceText = Label(frame, ChoiceText);
            Between(m_ChoiceText, m_Height * 0.15f);
            Click(frame.gameObject, () => { if (Step != null) Step(1); });
#else
            // WotR's choice: the plate of its settings with an arrow at each end.
            GameArt.Paint(m_Choice.gameObject.AddComponent<Image>(), GameArt.Part.Frame, m_Height, PlainLight);
            Arrows(m_Choice);
            m_ChoiceText = Label(m_Choice, ChoiceText);
            Between(m_ChoiceText, ArrowRoom);
            var halves = m_Choice.gameObject.AddComponent<HalfClick>();
            halves.Control = this;
#endif
        }

#if KINGMAKER
        RectTransform ArrowButton(RectTransform parent, float size, int direction)
        {
            var button = Picture(direction < 0 ? "Back" : "Forth", parent, GameArt.Part.ArrowBack, size, size, PlainMid);
            float arrowSize = size * 0.48f;
            var arrow = Picture("Arrow", button, GameArt.Part.Arrow, arrowSize, arrowSize, PlainLight);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0.5f);
            // The arrow of the sprite points down.
            arrow.localEulerAngles = new Vector3(0f, 0f, direction < 0 ? -90f : 90f);
            Click(button.gameObject, () => { if (Step != null) Step(direction); });
            return button;
        }
#else
        // The journal's arrows, pointing out, at the ends of a plate; they show the value can be stepped.
        void Arrows(RectTransform plate)
        {
            float size = m_Height * 0.42f;
            float width = GameArt.WidthAt(GameArt.Part.Arrow, size, size) ;
            for (int side = -1; side <= 1; side += 2)
            {
                var arrow = Picture(side < 0 ? "ArrowLeft" : "ArrowRight", plate, GameArt.Part.Arrow, width, size, PlainLight);
                arrow.anchorMin = arrow.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
                arrow.anchoredPosition = new Vector2(-side * m_Height * 0.75f, 0f);
                // The arrow of the sprite points down.
                arrow.localEulerAngles = new Vector3(0f, 0f, side < 0 ? -90f : 90f);
                arrow.GetComponent<Image>().raycastTarget = false;
            }
        }

        class HalfClick : MonoBehaviour, IPointerClickHandler
        {
            public ModControl Control;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left || Control.Step == null) return;
                Vector2 local;
                var rect = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out local)) return;
                Control.Step(local.x < rect.rect.center.x ? -1 : 1);
            }
        }
#endif

        // ---------- button ----------

        internal void SetButton(string label)
        {
            if (m_Button == null) BuildButton();
            Show(Kind.Button);
            m_ButtonText.text = label;
            PaintButton();
        }

        void BuildButton()
        {
            m_Button = Box("Button", transform);
            m_ButtonBack = m_Button.gameObject.AddComponent<Image>();
            m_ButtonText = Label(m_Button, Light);
            var hit = m_Button.gameObject.AddComponent<ButtonHit>();
            hit.Control = this;
        }

        void PaintButton()
        {
            var part = m_ButtonDown ? GameArt.Part.ButtonDown : m_ButtonOver ? GameArt.Part.ButtonHover : GameArt.Part.Button;
            GameArt.Paint(m_ButtonBack, part, m_Height, m_ButtonOver ? PlainLight : PlainMid);
        }

        class ButtonHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
        {
            public ModControl Control;

            public void OnPointerEnter(PointerEventData e)
            {
                Control.m_ButtonOver = true;
                Control.PaintButton();
            }

            public void OnPointerExit(PointerEventData e)
            {
                Control.m_ButtonOver = false;
                Control.m_ButtonDown = false;
                Control.PaintButton();
            }

            public void OnPointerDown(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left) return;
                Control.m_ButtonDown = true;
                Control.PaintButton();
            }

            public void OnPointerUp(PointerEventData e)
            {
                Control.m_ButtonDown = false;
                Control.PaintButton();
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left && Control.Press != null) Control.Press();
            }
        }

        // ---------- parts ----------

        static RectTransform Box(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        static RectTransform Picture(string name, RectTransform parent, GameArt.Part part, float width, float height, Color plain)
        {
            var rect = Box(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            var image = rect.gameObject.AddComponent<Image>();
            GameArt.Paint(image, part, height, plain);
            image.preserveAspect = true;
            return rect;
        }

        // A word over the whole part, centred both ways on its letters, never wrapped.
        TextMeshProUGUI Label(RectTransform parent, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (m_Style != null)
            {
                text.font = m_Style.font;
                text.fontSharedMaterial = m_Style.fontSharedMaterial;
            }
            // As the game's controls: the word about half as high as the control.
            float size = Mathf.Min(m_Style != null ? m_Style.fontSize * 0.9f : 18f, m_Height * 0.62f);
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = size * 0.6f;
            m_Labels.Add(new KeyValuePair<TextMeshProUGUI, float>(text, size));
            text.color = color;
            text.alignment = TextAlignmentOptions.Midline;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = Vector4.zero;
            text.raycastTarget = false;
            return text;
        }

        static void Click(GameObject go, Action action)
        {
            if (go.GetComponent<Graphic>() == null) go.AddComponent<Image>().color = Color.clear;
            go.AddComponent<ClickHit>().Action = action;
        }

        class ClickHit : MonoBehaviour, IPointerClickHandler
        {
            public Action Action;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left && Action != null) Action();
            }
        }
    }
}
