using BuffIt2TheLimit.Config;
using Kingmaker;
using Newtonsoft.Json;
using System;
using System.IO;

namespace BuffIt2TheLimit {

    public enum PadDisplay { Auto, Gamepad, Keyboard }

    // Settings of the pad menu that do not depend on the playthrough, kept next to the mod.
    internal class PadSettings {

        [JsonProperty] public bool QuickCastBar = true;
        [JsonProperty] public bool Gestures = true;
        [JsonProperty] public bool ShowHidden;
        [JsonProperty] public PadDisplay Display = PadDisplay.Auto;
        [JsonProperty] public int BarOffsetX;
        [JsonProperty] public int BarOffsetY;

        private static PadSettings current;

        private static string FilePath => $"{ModSettings.ModEntry.Path}UserSettings/pad-settings.json";

        public static PadSettings Current {
            get {
                if (current != null)
                    return current;
                try {
                    if (File.Exists(FilePath))
                        current = JsonConvert.DeserializeObject<PadSettings>(File.ReadAllText(FilePath));
                } catch (Exception ex) {
                    Main.Error(ex, "reading pad settings");
                }
                current ??= new PadSettings();
                return current;
            }
        }

        public static void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
            } catch (Exception ex) {
                Main.Error(ex, "writing pad settings");
            }
        }

        // Keys instead of gamepad buttons in hints and help: by the game's control mode unless set by hand.
        public static bool KeyboardMode => Current.Display switch {
            PadDisplay.Keyboard => true,
            PadDisplay.Gamepad => false,
            _ => Game.Instance != null && !Game.Instance.IsControllerGamepad
        };
    }
}
