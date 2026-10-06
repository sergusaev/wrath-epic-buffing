using BuffIt2TheLimit.Config;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.PubSubSystem;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace BuffIt2TheLimit {

    // Kingmaker entry point. The game has no spellbook UI to hook into here, so the mod is
    // only the buff engine plus the gamepad menu (PadQuickMenu), opened by the menu key.
    static class Main {
        public static string ModPath;
        private static Harmony harmony;

        static bool Load(UnityModManager.ModEntry modEntry) {
            ModSettings.ModEntry = modEntry;
            ModPath = modEntry.Path;
            Log("LOADING Pad Buffs for Kingmaker");
            Language.Initialise();
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();
            GlobalBubbleBuffer.Install();
            modEntry.OnUpdate = OnUpdate;
            return true;
        }

        // UMM calls this every frame; the controller is created lazily once a game is loaded.
        static void OnUpdate(UnityModManager.ModEntry modEntry, float delta) {
            try {
                GlobalBubbleBuffer.Instance?.EnsureController();
            } catch (Exception ex) {
                Error(ex, "OnUpdate");
            }
        }

        public static void Safely(Action a) {
            try {
                a();
            } catch (Exception ex) {
                Error(ex);
            }
        }

        public static void Log(string msg) => ModSettings.ModEntry.Logger.Log(msg);

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebug(string msg) => ModSettings.ModEntry.Logger.Log(msg);

        public static void Error(Exception e) => Log(e.ToString());

        public static void Error(Exception e, string message) {
            Log(message);
            Log(e.ToString());
        }

        public static void Error(string message) => Log(message);

        internal static void Verbose(string v, string filter = null) {
#if DEBUG
            Log(v);
#endif
        }
    }

    static class BlueprintIds {
        private static readonly Dictionary<string, Guid> cache = new();

        public static Guid Gid(this BlueprintScriptableObject blueprint) {
            var id = blueprint.AssetGuid;
            if (!cache.TryGetValue(id, out var guid)) {
                guid = Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty;
                cache[id] = guid;
            }
            return guid;
        }
    }

    static class Resources {
        public static T GetBlueprint<T>(string id) where T : BlueprintScriptableObject {
            var value = ResourcesLibrary.TryGetBlueprint<T>(id);
            if (value == null)
                Main.Log($"COULD NOT LOAD: {id} - {typeof(T)}");
            return value;
        }
    }

    // Results of one cast routine; in WotR the same class is also the combat-log tooltip.
    class TooltipTemplateBuffer {
        public class BuffResult {
            public BubbleBuff buff;
            public List<string> messages;
            public int count;
            public Dictionary<BuffSourceType, int> sourceCounts = new();
            public bool ExtendRodUsed;
            public BuffResult(BubbleBuff buff) {
                this.buff = buff;
            }
        }
        private readonly List<BuffResult> good = new();
        private readonly List<BuffResult> bad = new();
        private readonly List<BuffResult> skipped = new();
        internal IReadOnlyList<BuffResult> Bad => bad;

        public BuffResult AddBad(BubbleBuff buff) {
            BuffResult result = new(buff);
            result.messages = new();
            bad.Add(result);
            return result;
        }
        public BuffResult AddSkip(BubbleBuff buff) {
            BuffResult result = new(buff);
            skipped.Add(result);
            return result;
        }
        public BuffResult AddGood(BubbleBuff buff) {
            BuffResult result = new(buff);
            good.Add(result);
            return result;
        }
    }

    static class UnitBuffPartView {
        public static void StartSuppression() { }
        public static void EndSuppresion() { }
    }

    // Holder of the buff state for the loaded game; WotR keeps it on the spellbook screen.
    public class BubbleBuffSpellbookController : MonoBehaviour {
        public BufferState state;
        public BuffExecutor Executor;
        private string gameId;

        public static string SettingsPath => $"{ModSettings.ModEntry.Path}UserSettings/bi2tl-{Game.Instance.Player.GameId}.json";

        public bool IsFor(string id) => gameId == id;

        public void CreateBuffstate() {
            gameId = Game.Instance.Player.GameId;
            Directory.CreateDirectory($"{ModSettings.ModEntry.Path}UserSettings");
            SavedBufferState save = null;
            if (File.Exists(SettingsPath)) {
                try {
                    using var settingsReader = File.OpenText(SettingsPath);
                    using var jsonReader = new JsonTextReader(settingsReader);
                    save = JsonSerializer.CreateDefault().Deserialize<SavedBufferState>(jsonReader);
                } catch (Exception ex) {
                    Main.Error(ex, "reading settings, starting over");
                }
            }
            save ??= new SavedBufferState { Version = 1 };
            // L5 sends F7 through Steam Input; the menu has no other way to open in Kingmaker.
            if (save.OpenBuffMenuKey.IsNone)
                save.OpenBuffMenuKey = new ShortcutBinding(KeyCode.F7);
            state = new(save);
            Executor = new(state);
            Main.Log($"[PAD] buff state for game {gameId}, {save.Buffs.Count} saved buffs");
        }

        internal void Execute(BuffGroup group) {
            Executor.Execute(group);
        }

        internal void ExecuteCombatStart() {
            Executor.ExecuteCombatStart();
        }

        internal void RevalidateSpells() {
            if (state != null) {
                state.InputDirty = true;
            }
        }
    }

    class GlobalBubbleBuffer {
        public static GlobalBubbleBuffer Instance;
        public static RoundLimitHandler RoundLimitWatcher;
        private static KmEventWatcher Watcher;

        public BubbleBuffSpellbookController SpellbookController;
        private GameObject host;

        public static void Install() {
            Instance = new();
            RoundLimitWatcher = new();
            Watcher = new();
            EventBus.Subscribe(RoundLimitWatcher);
            EventBus.Subscribe(Watcher);
        }

        public static void Execute(BuffGroup group) {
            Instance.SpellbookController?.Execute(group);
        }

        // One host object for the whole session; the buff state is rebuilt when another game is loaded.
        internal void EnsureController() {
            var player = Game.Instance?.Player;
            if (player == null || string.IsNullOrEmpty(player.GameId) || !Game.Instance.Player.Party.Any())
                return;
            if (host == null) {
                host = new GameObject("PadBuffs_Host");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<BubbleBuffGlobalController>();
            }
            if (SpellbookController == null) {
                SpellbookController = host.AddComponent<BubbleBuffSpellbookController>();
                SpellbookController.CreateBuffstate();
            } else if (!SpellbookController.IsFor(player.GameId)) {
                SpellbookController.CreateBuffstate();
            }
        }

        internal void TryInstallUI() { }

        internal void OpenBuffMenu() => PadQuickMenu.Toggle();
    }

    internal class KmEventWatcher : ISceneHandler, IPartyChangedUIHandler, IPartyCombatHandler, ILevelUpCompleteUIHandler, ISpellBookUIHandler {
        private static void Revalidate() {
            Main.Safely(() => GlobalBubbleBuffer.Instance?.SpellbookController?.RevalidateSpells());
        }

        public void OnAreaDidLoad() {
            Revalidate();
            Main.Safely(AbilityCache.Revalidate);
        }

        public void OnAreaBeginUnloading() { }

        public void HandlePartyChanged() => Revalidate();

        public void HandleMemorizedSpell(Kingmaker.UnitLogic.Abilities.AbilityData data, Kingmaker.UnitLogic.UnitDescriptor owner) => Revalidate();

        public void HandleForgetSpell(Kingmaker.UnitLogic.Abilities.AbilityData data, Kingmaker.UnitLogic.UnitDescriptor owner) => Revalidate();

        public void HandleLevelUpComplete(Kingmaker.EntitySystem.Entities.UnitEntityData unit, bool isChargen) => Revalidate();

        public void HandlePartyCombatStateChanged(bool inCombat) {
            if (!inCombat)
                return;
            Main.Safely(() => GlobalBubbleBuffer.Instance?.SpellbookController?.ExecuteCombatStart());
        }
    }
}
