using UnityEngine;

namespace ModBar
{
    // Keeps a console window with its hint row under it clear of the party's portraits in keyboard and mouse mode:
    // the block is centred on the screen, raised when its bottom would fall on the portraits, and made a little
    // smaller when it is too high for the room over them. With a gamepad the block is just centred.
    class AbovePortraits : MonoBehaviour
    {
        // The top of the portraits and the room left above the window, in the units of a 1920x1080 canvas.
#if KINGMAKER
        const float PortraitsTop = 200f;
#else
        // WotR's party bar and the action bar at the bottom of the screen.
        const float PortraitsTop = 150f;
#endif
        const float TopMargin = 16f;
        const float MinScale = 0.75f;

        public RectTransform Hints;
        public float Gap;

        public static void Attach(RectTransform window, RectTransform hints, float gap)
        {
            var keeper = window.gameObject.GetComponent<AbovePortraits>() ?? window.gameObject.AddComponent<AbovePortraits>();
            keeper.Hints = hints;
            keeper.Gap = gap;
        }

        void LateUpdate()
        {
            var rect = (RectTransform)transform;
            var parent = rect.parent as RectTransform;
            if (parent == null || Hints == null) return;
            float h = rect.rect.height, under = Gap + Hints.rect.height, total = h + under, room = parent.rect.height;
            float scale = 1f, y = under / 2;
            if (Kingmaker.Game.Instance != null && !Kingmaker.Game.Instance.IsControllerGamepad)
            {
                float space = room - PortraitsTop - TopMargin;
                if (total > space) scale = Mathf.Max(MinScale, space / total);
                y = under * scale / 2;
                float bottom = room / 2 + y - (h / 2 + under) * scale;
                if (bottom < PortraitsTop) y += PortraitsTop - bottom;
                float top = room / 2 + y + h * scale / 2;
                if (top > room - TopMargin) y -= top - (room - TopMargin);
            }
            if (rect.localScale.x != scale) rect.localScale = new Vector3(scale, scale, 1f);
            if (rect.anchoredPosition.y != y) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }
    }
}
