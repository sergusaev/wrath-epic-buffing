using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModBar
{
    // The game's own vertical scrollbar at the right of a mod's list: the game's track and handle sprites (Kingmaker's
    // parchment bar, WotR's dark one), the handle as long as the shown part of the list and where it is. Shown only
    // when the list does not fit. Dragging or a click on the track tells the list where to go. The sprites are taken
    // from the running game; without them a plain bar in their place.
    class ModScrollbar : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
#if KINGMAKER
        const string TrackName = "blockscroll_bottom_vertical";
        const string HandleName = "ScrollBar_Big_Handler";
        const float HandleWidth = 14f;
        const float TrackWidth = 5f;
#else
        const string TrackName = "UI_ScrollVertical_BackLine";
        const string HandleName = "UI_ScrollVertical_Handl_Default";
        const float HandleWidth = 12f;
        const float TrackWidth = 3f;
#endif
        // The room the bar takes at the right of a list.
        internal const float Width = HandleWidth + 6f;

        static Sprite s_Track, s_Handle;

        RectTransform m_Handle;
        CanvasGroup m_Group;
        float m_First, m_Shown, m_Total;
        float m_Grab = -1f;
        ScrollRect m_Follow;

        // Where the list should start, in the units given to Set, after a drag or a click.
        internal Action<float> Moved;

        // The bar along the right edge of the area, over it, out of its layout.
        internal static ModScrollbar Create(RectTransform area, Func<string, Sprite> find)
        {
            Resolve(find);
            var go = new GameObject("Scrollbar", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(area, false);
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(Width, 0);
            rect.anchoredPosition = Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            // The whole strip takes the clicks.
            go.AddComponent<Image>().color = Color.clear;

            var track = Part(rect, "Track", s_Track, new Color(0.45f, 0.35f, 0.2f, 0.5f));
            track.anchorMin = new Vector2(0.5f, 0);
            track.anchorMax = new Vector2(0.5f, 1);
            track.sizeDelta = new Vector2(TrackWidth, 0);

            var handle = Part(rect, "Handle", s_Handle, new Color(0.75f, 0.6f, 0.35f, 0.9f));
            handle.anchorMin = handle.anchorMax = new Vector2(0.5f, 1);
            handle.pivot = new Vector2(0.5f, 1);
            handle.sizeDelta = new Vector2(HandleWidth, 40f);

            var bar = go.AddComponent<ModScrollbar>();
            bar.m_Handle = handle;
            // Hidden by its group, not switched off: a bar that follows a view keeps reading it.
            bar.m_Group = go.AddComponent<CanvasGroup>();
            bar.Set(0f, 0f, 0f);
            return bar;
        }

        // A bar for a Unity scroll view: it follows the view and moves it.
        internal static ModScrollbar Follow(ScrollRect scroll, Func<string, Sprite> find)
        {
            var bar = Create((RectTransform)scroll.transform, find);
            bar.m_Follow = scroll;
            bar.Moved = first =>
            {
                float hidden = bar.m_Total - bar.m_Shown;
                if (hidden > 0f) scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(first / hidden);
            };
            bar.Sync();
            return bar;
        }

        // first: where the shown part starts, shown: how much is shown, total: how long the list is; any one unit.
        internal void Set(float first, float shown, float total)
        {
            m_First = first;
            m_Shown = shown;
            m_Total = total;
            bool needed = total > shown + 0.5f && shown > 0f;
            m_Group.alpha = needed ? 1f : 0f;
            m_Group.blocksRaycasts = m_Group.interactable = needed;
            if (needed) Layout();
        }

        void Layout()
        {
            float height = ((RectTransform)transform).rect.height;
            if (height <= 0f) return;
            float min = s_Handle != null ? (s_Handle.border.y + s_Handle.border.w) * 100f / s_Handle.pixelsPerUnit : 24f;
            float h = Mathf.Clamp(height * m_Shown / m_Total, Mathf.Min(min, height), height);
            float hidden = m_Total - m_Shown;
            float y = hidden > 0f ? (height - h) * Mathf.Clamp01(m_First / hidden) : 0f;
            m_Handle.sizeDelta = new Vector2(HandleWidth, h);
            m_Handle.anchoredPosition = new Vector2(0, -y);
        }

        void LateUpdate()
        {
            if (m_Follow != null) Sync();
            else if (m_Group.alpha > 0f) Layout();
        }

        void Sync()
        {
            var content = m_Follow.content;
            var view = m_Follow.viewport != null ? m_Follow.viewport : (RectTransform)m_Follow.transform;
            if (content == null) return;
            float total = content.rect.height, shown = view.rect.height;
            Set((total - shown) * (1f - m_Follow.verticalNormalizedPosition), shown, total);
        }

        public void OnPointerDown(PointerEventData data)
        {
            float y = Pointer(data);
            float top = -m_Handle.anchoredPosition.y, h = m_Handle.rect.height;
            // On the handle it is held where it was taken; on the track the handle's middle jumps to the pointer.
            m_Grab = y >= top && y <= top + h ? y - top : h / 2f;
            Drag(y);
        }

        public void OnDrag(PointerEventData data)
        {
            Drag(Pointer(data));
        }

        // The pointer's distance from the bar's top.
        float Pointer(PointerEventData data)
        {
            var rect = (RectTransform)transform;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out local);
            return rect.rect.yMax - local.y;
        }

        void Drag(float y)
        {
            float height = ((RectTransform)transform).rect.height, h = m_Handle.rect.height;
            float room = height - h;
            if (room <= 0f || Moved == null) return;
            float fraction = Mathf.Clamp01((y - m_Grab) / room);
            Moved(fraction * (m_Total - m_Shown));
        }

        static RectTransform Part(RectTransform parent, string name, Sprite sprite, Color plain)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                image.color = Color.white;
            }
            else image.color = plain;
            return rect;
        }

        // The game's sprites at the bar's width: their sliced ends keep the proportions the game shows.
        static void Resolve(Func<string, Sprite> find)
        {
            if (s_Track != null && s_Handle != null) return;
            try
            {
                var loaded = Resources.FindObjectsOfTypeAll<Sprite>();
                Func<string, Sprite> lookup = name => loaded.FirstOrDefault(s => s.name == name) ?? (find != null ? find(name) : null);
                if (s_Track == null) s_Track = Scaled(lookup(TrackName), TrackWidth);
                if (s_Handle == null) s_Handle = Scaled(lookup(HandleName), HandleWidth);
            }
            catch (Exception) { }
        }

        static Sprite Scaled(Sprite sprite, float width)
        {
            if (sprite == null) return null;
            return Sprite.Create(sprite.texture, sprite.textureRect, new Vector2(0.5f, 0.5f), 100f * sprite.rect.width / width, 0,
                SpriteMeshType.FullRect, sprite.border);
        }
    }
}
