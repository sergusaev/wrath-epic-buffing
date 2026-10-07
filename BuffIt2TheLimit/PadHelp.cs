using BuffIt2TheLimit.Config;
#if KINGMAKER
using Kingmaker.Blueprints.Console;
#else
using Owlcat.Runtime.UI.ConsoleTools.GamepadInput;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace BuffIt2TheLimit {

    // Keyboard keys of the pad menu: each gamepad action has a key next to WASD, then the usual duplicates.
    // The game's own hotkeys are off while the menu is open, so these keys do not reach the game.
    internal static class PadKeys {
        public static readonly KeyCode[] Confirm = { KeyCode.E, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space };
        public static readonly KeyCode[] Decline = { KeyCode.Q, KeyCode.Escape, KeyCode.Backspace };
        public static readonly KeyCode[] Func01 = { KeyCode.R };
        public static readonly KeyCode[] Func02 = { KeyCode.F };
        public static readonly KeyCode[] LeftUp = { KeyCode.X, KeyCode.PageUp };
        public static readonly KeyCode[] RightUp = { KeyCode.C, KeyCode.PageDown };
        public static readonly KeyCode[] Up = { KeyCode.UpArrow, KeyCode.W };
        public static readonly KeyCode[] Down = { KeyCode.DownArrow, KeyCode.S };
        public static readonly KeyCode[] Left = { KeyCode.LeftArrow, KeyCode.A };
        public static readonly KeyCode[] Right = { KeyCode.RightArrow, KeyCode.D };

        public static KeyCode[] For(RewiredActionType action) => action switch {
            RewiredActionType.Confirm => Confirm,
            RewiredActionType.Decline => Decline,
            RewiredActionType.Func01 => Func01,
            RewiredActionType.Func02 => Func02,
            RewiredActionType.LeftUp => LeftUp,
            RewiredActionType.RightUp => RightUp,
            RewiredActionType.DPadUp => Up,
            RewiredActionType.DPadDown => Down,
            RewiredActionType.DPadLeft => Left,
            RewiredActionType.DPadRight => Right,
            _ => new KeyCode[0]
        };

        public static string Label(RewiredActionType action) => action switch {
            RewiredActionType.DPadVertical => "W/S",
            RewiredActionType.DPadHorizontal => "A/D",
            RewiredActionType.DPadUp => "W",
            RewiredActionType.DPadDown => "S",
            RewiredActionType.DPadLeft => "A",
            RewiredActionType.DPadRight => "D",
            _ => For(action).Length > 0 ? For(action)[0].ToString() : action.ToString()
        };

        public static bool Pressed(KeyCode[] keys) => keys.Any(Input.GetKeyDown);

        // Arrows always; the letter keys only when letters are not typed as text.
        public static bool Held(KeyCode[] keys, bool letters) =>
            keys.Any(k => Input.GetKey(k) && (letters || k < KeyCode.A || k > KeyCode.Z));
    }

    // Help text of the gamepad menu and button icons inside menu texts.
    // Texts mark buttons as {A} {B} {X} {Y} {LB} {RB} {UP} {DOWN} {LEFT} {RIGHT} {L5};
    // they become sprites of the game's own sprite asset (the one its console tutorials use),
    // or bracketed letters when the asset is not found. Built-in group names are {G1} {G2} {G3}.
    // Help markup, one element per line: "# " section, "## " subheading, "- " bullet,
    // "1. " numbered step, "> " tip, "{A}| text" button line, "**bold**" anywhere.
    internal static class PadHelp {

        private static bool resolved;
        private static TMP_SpriteAsset asset;
        private static string prefix;

        private static readonly Regex Bold = new(@"\*\*(.+?)\*\*");
        private static readonly Regex Numbered = new(@"^(\d+)\.\s+(.*)$");
        private static readonly Regex Token = new(@"\{(A|B|X|Y|LB|RB|UP|DOWN|LEFT|RIGHT|L5|G1|G2|G3)\}");

        public static TMP_SpriteAsset IconAsset {
            get {
                Resolve();
                return asset;
            }
        }

        // The game names its sprites "<pad prefix><action>", e.g. "XBox_Confirm"; the prefix
        // depends on the pad type, so it is taken from the game's own binding template.
        private static void Resolve() {
            if (resolved)
                return;
            resolved = true;
            string gamePrefix = null;
            try {
                var tag = new Kingmaker.TextTools.ConsoleBindingTemplate().Generate(false, new List<string> { "Confirm" });
                var match = Regex.Match(tag ?? "", "name=\"(\\w*?)Confirm\"");
                if (match.Success)
                    gamePrefix = match.Groups[1].Value;
            } catch (Exception ex) {
                Main.Log($"[PAD] icons: binding template failed: {ex.Message}");
            }
            var assets = new List<TMP_SpriteAsset>();
            try {
                if (TMP_Settings.defaultSpriteAsset != null)
                    assets.Add(TMP_Settings.defaultSpriteAsset);
            } catch (Exception) { }
            assets.AddRange(UnityEngine.Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>());
            foreach (var p in new[] { gamePrefix, "Steam_", "XBox_", "PS4_" }.Where(p => p != null).Distinct()) {
                foreach (var a in assets) {
                    try {
                        if (a == null || a.GetSpriteIndexFromName(p + "Confirm") < 0)
                            continue;
                    } catch (Exception) {
                        continue;
                    }
                    asset = a;
                    prefix = p;
                    Main.Log($"[PAD] icons: sprite asset {a.name}, prefix {p} (game {gamePrefix ?? "?"})");
                    return;
                }
            }
            Main.Log($"[PAD] icons: no sprite asset among {assets.Count}, letters instead (game {gamePrefix ?? "?"})");
        }

        public static string Button(RewiredActionType action) {
            if (PadSettings.KeyboardMode)
                return Letters(PadKeys.Label(action));
            Resolve();
            if (asset != null)
                return $"<size=135%><sprite name=\"{prefix}{action}\"></size>";
            return Letters(Fallback(action));
        }

        private static string Letters(string text) => $"<b><color={PadTheme.Accent}>[{text}]</color></b>";

        private static string Fallback(RewiredActionType action) => action switch {
            RewiredActionType.Confirm => "A",
            RewiredActionType.Decline => "B",
            RewiredActionType.Func01 => "X",
            RewiredActionType.Func02 => "Y",
            RewiredActionType.LeftUp => "LB",
            RewiredActionType.RightUp => "RB",
            RewiredActionType.DPadUp => "↑",
            RewiredActionType.DPadDown => "↓",
            RewiredActionType.DPadLeft => "←",
            RewiredActionType.DPadRight => "›",
            _ => action.ToString()
        };

        // Replaces button and group tokens; call before string.Format, the result has no braces.
        // Single-line texts may hold both variants as "gamepad text@kb keyboard text".
        public static string Tokens(string text) {
            if (!string.IsNullOrEmpty(text) && text.IndexOf('\n') < 0) {
                int split = text.IndexOf("@kb ", StringComparison.Ordinal);
                if (split >= 0)
                    text = PadSettings.KeyboardMode ? text.Substring(split + 4) : text.Substring(0, split);
            }
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
                return text;
            return Token.Replace(text, m => m.Groups[1].Value switch {
                "A" => Button(RewiredActionType.Confirm),
                "B" => Button(RewiredActionType.Decline),
                "X" => Button(RewiredActionType.Func01),
                "Y" => Button(RewiredActionType.Func02),
                "LB" => Button(RewiredActionType.LeftUp),
                "RB" => Button(RewiredActionType.RightUp),
                "UP" => Button(RewiredActionType.DPadUp),
                "DOWN" => Button(RewiredActionType.DPadDown),
                "LEFT" => Button(RewiredActionType.DPadLeft),
                "RIGHT" => Button(RewiredActionType.DPadRight),
                "L5" => Letters(MenuKeyName),
                "G1" => PadGroups.Name(BuffGroup.Long),
                "G2" => PadGroups.Name(BuffGroup.Quick),
                "G3" => PadGroups.Name(BuffGroup.Important),
                _ => m.Value
            });
        }

        // In keyboard mode the menu key is shown as the key itself.
        private static string MenuKeyName {
            get {
                if (!PadSettings.KeyboardMode)
                    return "pad.key.menu".i8();
                var key = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.GetOpenBuffMenuShortcut() ?? ShortcutBinding.None;
                return key.IsNone ? "pad.key.menu".i8() : key.ToDisplayString();
            }
        }

        // Lines that start with "@pad " are shown only with gamepad hints, "@kb " only with keyboard hints.
        private static string ForMode(string line) {
            bool keyboard = PadSettings.KeyboardMode;
            if (line.StartsWith("@pad "))
                return keyboard ? null : line.Substring(5);
            if (line.StartsWith("@kb "))
                return keyboard ? line.Substring(4) : null;
            return line;
        }

        // Rich text of the help page; section starts (indices in the result) go to sections.
        // Offsets are in pixels: the Kingmaker TextMeshPro derives "em" from the font's tab width, near zero for the book font.
        public static string Build(List<int> sections) {
            var body = "pad.help.body".i8();
#if KINGMAKER
            body += "\n\n" + "pad.help.km".i8();
#endif
            var source = "pad.help.src".i8();
#if !KINGMAKER
            source += "\n" + "pad.help.src.items".i8();
#endif
            body = body.Replace("##SRC##", source + "\n" + "pad.help.src.end".i8());
            var sb = new StringBuilder();
            foreach (var raw in body.Split('\n')) {
                var shown = ForMode(raw.TrimEnd('\r'));
                if (shown == null)
                    continue;
                var line = Bold.Replace(shown, "<b>$1</b>");
                if (line.StartsWith("# ")) {
                    if (sb.Length > 0)
                        sb.Append("<size=45%>\n</size>");
                    sb.Append($"<size=125%><b><color={PadTheme.Heading}>");
                    sections.Add(sb.Length);
                    sb.Append(PadTheme.Initial(Tokens(line.Substring(2)))).Append("</color></b></size>\n");
                    // Room for the ornamental rule the menu lays under every heading.
                    if (PadTheme.HasSeparator)
                        sb.Append("<size=70%>\n</size>");
                    continue;
                } else if (line.StartsWith("## ")) {
                    sb.Append($"<size=40%>\n</size><b><color={PadTheme.Sub}>").Append(Tokens(line.Substring(3))).Append("</color></b>\n");
                } else if (line.StartsWith("- ")) {
                    sb.Append($"<pos=10><color={PadTheme.Heading}>•</color><pos=32><indent=32>").Append(Tokens(line.Substring(2))).Append("</indent>\n");
                } else if (line.StartsWith("> ")) {
                    sb.Append($"<indent=24><color={PadTheme.Note}><i>").Append(Tokens(line.Substring(2))).Append("</i></color></indent>\n");
                } else if (Numbered.IsMatch(line)) {
                    var m = Numbered.Match(line);
                    sb.Append($"<pos=8><color={PadTheme.Heading}><b>{m.Groups[1].Value}.</b></color><pos=36><indent=36>").Append(Tokens(m.Groups[2].Value)).Append("</indent>\n");
                } else if (line.StartsWith("{") && line.Contains("|")) {
                    int bar = line.IndexOf('|');
                    sb.Append(Tokens(line.Substring(0, bar).Trim())).Append("<pos=96><indent=96>")
                        .Append(Tokens(line.Substring(bar + 1).Trim())).Append("</indent>\n");
                } else {
                    sb.Append(Tokens(line)).Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
