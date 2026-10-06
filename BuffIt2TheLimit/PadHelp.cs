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

namespace BuffIt2TheLimit {

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
            Resolve();
            if (asset != null)
                return $"<size=135%><sprite name=\"{prefix}{action}\"></size>";
            return Letters(Fallback(action));
        }

        private static string Letters(string text) => $"<b><color=#E8C46A>[{text}]</color></b>";

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
            RewiredActionType.DPadRight => "→",
            _ => action.ToString()
        };

        // Replaces button and group tokens; call before string.Format, the result has no braces.
        public static string Tokens(string text) {
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
                "L5" => Letters("pad.key.menu".i8()),
                "G1" => PadGroups.Name(BuffGroup.Long),
                "G2" => PadGroups.Name(BuffGroup.Quick),
                "G3" => PadGroups.Name(BuffGroup.Important),
                _ => m.Value
            });
        }

        // Rich text of the help page; section starts (indices in the result) go to sections.
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
                var line = Bold.Replace(raw.TrimEnd('\r'), "<b>$1</b>");
                if (line.StartsWith("# ")) {
                    if (sb.Length > 0)
                        sb.Append("<size=45%>\n</size>");
                    sb.Append("<size=125%><b><color=#E8C46A>");
                    sections.Add(sb.Length);
                    sb.Append(Tokens(line.Substring(2))).Append("</color></b></size>\n");
                } else if (line.StartsWith("## ")) {
                    sb.Append("<size=40%>\n</size><b><color=#D8C08A>").Append(Tokens(line.Substring(3))).Append("</color></b>\n");
                } else if (line.StartsWith("- ")) {
                    sb.Append("  •<indent=1.8em>").Append(Tokens(line.Substring(2))).Append("</indent>\n");
                } else if (line.StartsWith("> ")) {
                    sb.Append("<indent=1.2em><color=#9FC3D9><i>").Append(Tokens(line.Substring(2))).Append("</i></color></indent>\n");
                } else if (Numbered.IsMatch(line)) {
                    var m = Numbered.Match(line);
                    sb.Append($"  <color=#E8C46A><b>{m.Groups[1].Value}.</b></color><indent=2em>").Append(Tokens(m.Groups[2].Value)).Append("</indent>\n");
                } else if (line.StartsWith("{") && line.Contains("|")) {
                    int bar = line.IndexOf('|');
                    sb.Append(Tokens(line.Substring(0, bar).Trim())).Append("<indent=3.6em>")
                        .Append(Tokens(line.Substring(bar + 1).Trim())).Append("</indent>\n");
                } else {
                    sb.Append(Tokens(line)).Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
