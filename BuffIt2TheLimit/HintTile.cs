#if KINGMAKER
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ModBar
{
    // The plate under the button hints of Kingmaker's console windows (backHintTile). The console interface loads it;
    // in keyboard and mouse mode it is not loaded, so then it is read from the game's own file: the raw RGBA32 pixels
    // in sharedassets6.assets.resS. Nothing of the game's art is shipped with the mods.
    static class HintTile
    {
        const string Name = "backHintTile";
        const string File = "sharedassets6.assets.resS";
        const long Offset = 50511748;
        const int Width = 840, Height = 185;
        static readonly Vector4 Border = new Vector4(121, 81, 127, 83);

        static Sprite s_Source;
        static bool s_FileTried;
        static Sprite s_Scaled;
        static float s_ScaledHeight;

        // The console windows' hint rows: 90 high with a gamepad; lower in keyboard and mouse mode, so that the row
        // stays over the portraits of the party.
        public static float RowHeight
        {
            get { return Kingmaker.Game.Instance != null && !Kingmaker.Game.Instance.IsControllerGamepad ? 64f : 90f; }
        }

        // The plate drawn at the given height whatever its pixels (the borders keep the proportions the game shows).
        public static Sprite Get(float height)
        {
            if (s_Scaled != null && s_ScaledHeight == height) return s_Scaled;
            var source = Source();
            if (source == null) return null;
            s_Scaled = Sprite.Create(source.texture, source.textureRect, new Vector2(0.5f, 0.5f), 100f * source.rect.height / height, 0,
                SpriteMeshType.FullRect, source.border);
            s_ScaledHeight = height;
            return s_Scaled;
        }

        static Sprite Source()
        {
            if (s_Source != null) return s_Source;
            s_Source = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == Name);
            if (s_Source != null || s_FileTried) return s_Source;
            s_FileTried = true;
            try
            {
                var path = Path.Combine(Application.dataPath, File);
                var bytes = new byte[Width * Height * 4];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < Offset + bytes.Length) return null;
                    stream.Seek(Offset, SeekOrigin.Begin);
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = stream.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) return null;
                        read += n;
                    }
                }
                // A wrong guess about the file (another game version) gives no plate rather than noise: the tile is
                // opaque at its centre.
                if (bytes[((Height / 2) * Width + Width / 2) * 4 + 3] < 200) return null;
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = Name, wrapMode = TextureWrapMode.Clamp };
                texture.LoadRawTextureData(bytes);
                texture.Apply(false, true);
                s_Source = Sprite.Create(texture, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Border);
                s_Source.name = Name;
            }
            catch (Exception)
            {
                s_Source = null;
            }
            return s_Source;
        }
    }
}
#endif
