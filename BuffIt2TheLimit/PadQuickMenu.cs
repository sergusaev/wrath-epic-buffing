using BuffIt2TheLimit.Config;
using BuffIt2TheLimit.Extensions;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
#if KINGMAKER
using Kingmaker.Assets.Console.GamepadInput;
using Kingmaker.Blueprints.Console;
#else
using Kingmaker.UnitLogic;
using Owlcat.Runtime.UI.ConsoleTools.GamepadInput;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // The buff menu, the only buff screen of the mod in both control modes. Tabs as in the other mod windows, switched
    // with LB/RB (X/C on the keyboard) or a click on a name:
    // Cast (Main): per-group status, apply one group or all groups in sequence, combat toggle, cast report.
    // Buffs (Editor): targets per party member for each buff; the first row chooses which buffs are listed.
    // Groups: list of groups, create / rename / hide.
    // Settings (Options): mod settings.
    // Help.
    // Opened from a tab and closed with B back to it: Members (buffs of one group, add and remove, on/off tick,
    // auto-fill by duration, delete the group), Name (on-screen keyboard for a group name and its duration), the
    // targets of a buff from Members (Editor) and the settings of one buff and its casters (Options).
    // Gamepad buttons go through the game's console input layer (the world and HUD are blocked while
    // the menu is open); directions are polled from the Rewired player for hold-to-repeat.
    // Start and Esc bring the game's menu over the buff menu, as over the game's own windows; the buff menu hides
    // under it and comes back. It never pauses the game: three groups in a row should not need three unpauses.
    // Keyboard keys (PadKeys) are polled; the game's own hotkeys are off while the menu is open.
    // Mouse: the pointer lights up what it is over. A click selects a row, a second click on the selected row acts as A;
    // rows that only open a page, keys, party members, tick boxes and values act at once. The wheel scrolls, the right
    // button goes back, a click on a button hint at the bottom does what the button does.
    internal class PadQuickMenu : MonoBehaviour {

        private enum Page { Main, Groups, Members, Name, Editor, Help, Options }

        private static PadQuickMenu instance;

        private static Color RowColor => PadTheme.Row;
        private static Color RowOutsideColor => PadTheme.RowOutside;
        private static Color RowSelectedColor => PadTheme.RowSelected;
        private static Color ChipColor => PadTheme.Chip;
        private static Color ChipWantedColor => PadTheme.ChipWanted;
        private static Color ChipCursorColor => PadTheme.ChipCursor;
        private static Color ChipCursorWantedColor => PadTheme.ChipCursorWanted;
        private static Color BoxColor => PadTheme.Box;
        private static Color BoxInnerColor => PadTheme.BoxInner;
        private static Color TickColor => PadTheme.Tick;
        private static Color KeyColor => PadTheme.Key;
        private static Color TitleColor => PadTheme.Title;
        private static Color TextColor => PadTheme.Text;
        private static Color DimColor => PadTheme.Dim;

        private const float WideWidth = 1320f;
        private const int VisibleRows = 10;
        private const int MemberRows = 12;
        private const float StickThreshold = 0.5f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.08f;
        private const float RoutineTimeout = 30f;
        // Holding the right stick this long asks to delete the selected custom group.
        private const float DeleteHoldTime = 0.6f;
        private const int OptionRows = 13;
        private const float IconSize = 30f;
        private const int WheelRows = 3;
        // Characters of a custom group name that fit the name column of the groups page; longer names end in "…".
        private const int CustomNameColumn = 34;

        private GameObject overlay;
        private Canvas overlayCanvas;
        private RectTransform panel;
        private TextMeshProUGUI titleText;
        // The tab row under the title, on the tabs only.
        private GameObject tabRow;
        private TextMeshProUGUI tabsText;
        private int topTab;
        private TMP_FontAsset font;
        private IDisposable layerHandle;
        private Kingmaker.UI.KeyboardAccess blockedKeyboard;
        private Page page;
        private float nextRefresh;
        private bool recalcTried;
        private bool builtForKeyboard;
        private int ignoreKeysUntil;

        // main
        private GameObject mainRoot;
        private Transform mainRowHolder;
        private readonly List<ListRow> rows = new();
        private List<BuffGroup> mainGroups = new();
        private TextMeshProUGUI reportText;
        // The label of the A hint on the main page: what A does on the selected row.
        private TextMeshProUGUI mainConfirmLabel;
        private int selected;

        // groups
        private GameObject groupsRoot;
        private Transform groupsRowHolder;
        private readonly List<ListRow> groupRows = new();
        private List<BuffGroup> groupList = new();
        private TextMeshProUGUI groupsNote;
        private int groupsCursor;
        // A custom group is deleted after a long press of the right stick (Del or a click on the hint) and a yes in
        // the confirmation over the window.
        private GameObject groupsDeleteHint;
        // The ring around the R3 icon fills while the stick is held.
        private Image deleteHoldFill;
        private float deleteHeldSince = -1f;
        private bool deleteHoldFired;
        private GameObject confirmRoot;
        private TextMeshProUGUI confirmText;
        private BuffGroup? confirmGroup;

        // members
        private GameObject membersRoot;
        private readonly List<ListRow> memberRows = new();
        private TextMeshProUGUI membersDetail;
        private TextMeshProUGUI membersNote;
        private BuffGroup memberGroup;
        private List<BubbleBuff> memberList = new();
        private int memberCount;
        private int memberCursor;
        private bool iconsLogged;
        private int memberTop;
        private Page membersBack;

        // name
        private GameObject nameRoot;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI nameDurationText;
        private Transform keyHolder;
        private readonly List<List<(Image bg, TextMeshProUGUI text, string key)>> keys = new();
        private BuffGroup? nameTarget;
        private string nameBuffer = "";
        private int nameDuration;
        private bool latin;
        private bool shift;
        private int keyRow;
        private int keyCol;

        // editor
        private GameObject editorRoot;
        // "Show": which buffs the list holds, a choice in the look of the settings above it.
        private ListRow filterRow;
        private bool onFilter;
        private GameObject editorTabHint;
        private TextMeshProUGUI editorBackLabel;
        private readonly List<ListRow> listRows = new();
        private TextMeshProUGUI detailText;
        private Transform chipHolder;
        private readonly List<(Image bg, TextMeshProUGUI text)> chips = new();
        private int tab;
        private int cursor;
        private int top;
        private int partyCol;
        private List<BubbleBuff> filtered = new();
        private Page editorBack;

        // help
        private GameObject helpRoot;
        private TextMeshProUGUI helpText;
        private ModBar.ModScrollbar helpBar, memberBar, editorBar, optionBar;
        private float helpOffset;
        private readonly List<int> helpSections = new();
        private readonly List<GameObject> helpRules = new();
        private bool helpRulesPending;
        private List<float> helpSectionOffsets;
        private const float HelpHeight = 720f;
        private const float HelpStep = 46f;

        // options
        private class Opt {
            public string Id;
            public string Label;
            public Func<string> Value;
            public Action<int> Change;
            public string Note;
            // Shown as a switch (On) or a choice between arrows (Choice) at the right of the row.
            public Func<bool> On;
            public Func<string> Choice;
            // Choices of one plate group are as wide as the longest word any of them may show (Words), so the
            // group lines up and keeps its width while stepping; the other controls fit their own word.
            public string Plate;
            public Func<IEnumerable<string>> Words;
            public bool Header => Change == null;
        }
        private const float OptionControlWidth = 240f, OptionControlHeight = 30f;
        private GameObject optionsRoot;
        private readonly List<ListRow> optRows = new();
        private TextMeshProUGUI optDetail;
        private List<Opt> opts = new();
        private readonly Dictionary<string, float> optPlates = new();
        private Func<List<Opt>> optSource;
        private string optTitle;
        private Page optBack;
        private GameObject optionsTabHint;
        private TextMeshProUGUI optionsBackLabel;
        private int optCursor;
        private int optTop;
        private bool rebuildRequested;
        // The wheel moved the list: the cursor is already inside it, RenderOptions keeps the scroll as it is.
        private bool optScrolled;

        private RepeatInput vertical;
        private RepeatInput horizontal;
        private bool holdDirections;

        private readonly Queue<BuffGroup> pending = new();
        private BuffGroup current;
        private bool waiting;
        private bool inExecute;
        private float waitDeadline;
        private readonly List<string> report = new();

        private int CombatRow => mainGroups.Count;
        private int MainRowCount => mainGroups.Count + 1;

        private const int Tabs = 5;
        private const int CastTab = 0, BuffsTab = 1, GroupsTab = 2, SettingsTab = 3, HelpTab = 4;

        private static BufferState State => GlobalBubbleBuffer.Instance?.SpellbookController?.state;

        public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

        private static string ModId => ModSettings.ModEntry.Info.Id;

        // A window of another mod of the collection is open: the buff menu does not open over it.
        public static bool OtherModWindowOpen => ModWindows.ModWindow.OtherOpen(ModId);

        private class ListRow {
            public Image Bg;
            public GameObject Box;
            public Image Tick;
            // The buff's picture, on buff lists only.
            public Image Icon;
            public TextMeshProUGUI Text;
            public PadPointer Pointer;
            // The switch or the choice of an option, in the look of the game's settings.
            public ModBar.ModControl Control;

            public void SetIcon(Sprite sprite) {
                if (Icon == null)
                    return;
                Icon.sprite = sprite;
                Icon.enabled = sprite != null;
            }
        }

        public static void Toggle() {
            Main.Verbose($"[PAD] toggle, open={IsOpen}");
            try {
                if (IsOpen)
                    instance.Close();
                else if (!OtherModWindowOpen)
                    Open();
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.Toggle");
            }
        }

        // From the quick cast bar: the buffs of one group.
        public static void OpenGroup(BuffGroup group) {
            try {
                if (!IsOpen)
                    Open();
                instance.OpenMembers(group, Page.Main);
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.OpenGroup");
            }
        }

        private static void Open() {
            if (instance != null && instance.builtForKeyboard != PadSettings.KeyboardMode)
                Discard();
            if (instance == null)
                instance = Build();
            instance.recalcTried = false;
            instance.overlay.SetActive(true);
            instance.gameObject.SetActive(true);
            PadPointer.ResetMouse();
            instance.PushInput();
            instance.BlockGameKeys();
            instance.ShowPage(Page.Main);
            instance.ShowReport();
            ModWindows.ModWindow.Opened(ModId, instance.overlay);
            Main.Verbose($"[PAD] opened, input layer={(instance.layerHandle != null)}");
        }

        private void Close() {
            Uncover();
            ModWindows.ModWindow.Closed(overlay);
            PopInput();
            UnblockGameKeys();
            gameObject.SetActive(false);
            overlay.SetActive(false);
#if KINGMAKER
            KmVirtualCursor.Restore();
#endif
            PadBar.Refresh();
        }

        // Hints and key labels are made when the menu is built, so a new display mode builds it anew.
        private static void Discard() {
            var old = instance;
            instance = null;
            old.Close();
            Destroy(old.overlay);
        }

        private static void Rebuild() {
            var back = instance.optBack;
            Discard();
            Open();
            instance.OpenGlobalOptions(back);
        }

        private void Awake() {
            BuffExecutor.RoutineFinished += OnRoutineFinished;
        }

        private void OnDisable() {
            PopInput();
            UnblockGameKeys();
        }

        private void OnDestroy() {
            ModWindows.ModWindow.Closed(overlay);
            PopInput();
            UnblockGameKeys();
            BuffExecutor.RoutineFinished -= OnRoutineFinished;
            if (instance == this)
                instance = null;
            if (overlay != null)
                Destroy(overlay);
        }

        private void Update() {
            try {
                bool over = GameMenuOver();
                if (over != covered) {
                    covered = over;
                    overlayCanvas.enabled = !over;
                    // The press that closes the game's menu is not also a press in the buff menu.
                    if (!over)
                        ignoreInputUntil = Time.frameCount + 2;
                }
                // The game's menu loaded a save or went to the main menu.
                if (covered && Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance.IsLoadingInProcess) {
                    Close();
                    return;
                }
                if (!covered && Time.frameCount > ignoreInputUntil) {
                    PadPointer.WatchMouse();
                    PollButtons();
                    PollMouse();
                    PollDirections();
                    if (page == Page.Name)
                        PollNameKeys();
                    else
                        PollKeys();
                }

                if (waiting && Time.unscaledTime > waitDeadline) {
                    Main.Log($"[PAD] no result for {current} after {RoutineTimeout}s");
                    waiting = false;
                    ReplaceLast(string.Format("pad.timeout".i8(), PadGroups.Name(current)));
                    RunNext();
                }

                if (!covered && Time.frameCount > ignoreInputUntil)
                    PollDeleteHold();

                if (page == Page.Main && Time.unscaledTime >= nextRefresh)
                    RefreshMain();
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.Update");
            }
        }

        // ---------- the game's menu over the buff menu ----------

        // Start opens it through the game's base layer, which stays bound under the menu's layer (gamepad mode); Esc
        // opens it here (keyboard mode). While it or a window opened from it (options, saves) is up, the buff menu is
        // hidden and takes no input; with the keyboard the game's keys are back meanwhile, the menu needs them.
        private bool covered;
        private bool menuRequested;
        private int menuFrame = -10;
        private int ignoreInputUntil;
#if KINGMAKER
        private HashSet<Kingmaker.UI.ServiceWindow.UIWindow> shownBefore;
        private static readonly HarmonyLib.AccessTools.FieldRef<Kingmaker.UI.SaveLoadWindow.SaveLoadWindow, bool> SaveLoadShown =
            HarmonyLib.AccessTools.FieldRefAccess<Kingmaker.UI.SaveLoadWindow.SaveLoadWindow, bool>("m_IsShown");
#endif

        private void OpenGameMenu() {
#if KINGMAKER
            shownBefore = new(FindObjectsOfType<Kingmaker.UI.ServiceWindow.UIWindow>().Where(w => w.IsShow));
#endif
            menuRequested = true;
            menuFrame = Time.frameCount;
            UnblockGameKeys();
            Main.Verbose("[PAD] game menu over the buff menu");
#if KINGMAKER
            Kingmaker.PubSubSystem.EventBus.RaiseEvent<Kingmaker.PubSubSystem.IEscMenuWindowUIHandler>(h => h.HandleOpen());
#else
            Kingmaker.PubSubSystem.EventBus.RaiseEvent<Kingmaker.PubSubSystem.IEscMenuHandler>(h => h.HandleOpen());
#endif
        }

        private bool GameMenuOver() {
            if (Game.Instance.IsControllerGamepad) {
                if (layerHandle == null)
                    return false;
                var layer = GamePad.Instance?.CurrentInputLayer;
                return layer != null && layer.ContextName != "BI2TL_PadQuickMenu";
            }
            if (!menuRequested)
                return false;
            // The menu takes a frame or two to show.
            if (Time.frameCount <= menuFrame + 2)
                return true;
#if KINGMAKER
            // Options and the like are windows of the game's kind; the list of saves and the confirmations are not.
            bool shown = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance.IsLoadingInProcess
                || FindObjectsOfType<Kingmaker.UI.ServiceWindow.UIWindow>().Any(w => w.IsShow && !shownBefore.Contains(w))
                || FindObjectsOfType<Kingmaker.UI.SaveLoadWindow.SaveLoadWindow>().Any(w => SaveLoadShown(w))
                || FindObjectsOfType<Kingmaker.UI.DialogMessageBox>().Any(w => w.IsShown);
#else
            var common = Kingmaker.UI.MVVM.RootUIContext.Instance?.CommonVM;
            bool shown = common != null && (common.EscMenuContextVM?.EscMenu.Value != null || common.SaveLoadVM.Value != null
                || common.SettingsVM.Value != null || common.MessageModalVM.Value != null);
#endif
            if (!shown) {
                menuRequested = false;
                BlockGameKeys();
            }
            return shown;
        }

        // Closing under the game's menu: the canvas comes back for the next opening.
        private void Uncover() {
            covered = false;
            menuRequested = false;
            if (overlayCanvas != null)
                overlayCanvas.enabled = true;
        }

        // ---------- input ----------

        private void PushInput() {
            if (layerHandle != null)
                return;
            // In mouse mode the game has no console input layers; buttons are polled instead.
            if (!Game.Instance.IsControllerGamepad)
                return;
            var pad = GamePad.Instance;
            if (pad == null)
                return;
            var layer = new InputLayer { ContextName = "BI2TL_PadQuickMenu" };
            foreach (var action in MenuButtons) {
                var a = action;
                layer.AddButton(_ => Fire(a, "layer"), (int)a);
            }
            foreach (var action in DPadButtons) {
                var a = action;
                layer.AddButton(_ => MarkDPad(a), (int)a, Rewired.InputActionEventType.ButtonPressed);
            }
            // Held, the right stick deletes a group (PollDeleteHold); bound here so the layers under the menu skip it.
            layer.AddButton(_ => { }, (int)RewiredActionType.RightStickButton);
            layer.AddAxis2D((_, v) => MarkStick(v), (int)RewiredActionType.LeftStickX, (int)RewiredActionType.LeftStickY, false);
            layerHandle = pad.PushLayer(layer);
#if KINGMAKER
            KmVirtualCursor.Hide();
#endif
        }

        private static readonly RewiredActionType[] MenuButtons = {
            RewiredActionType.Confirm, RewiredActionType.Decline, RewiredActionType.Func01,
            RewiredActionType.Func02, RewiredActionType.LeftUp, RewiredActionType.RightUp
        };
        private static readonly RewiredActionType[] DPadButtons = {
            RewiredActionType.DPadUp, RewiredActionType.DPadDown, RewiredActionType.DPadLeft, RewiredActionType.DPadRight
        };

        private readonly Dictionary<RewiredActionType, int> firedFrame = new();
        private readonly HashSet<string> loggedSources = new();
        private int padLogLines;
        private RewiredActionType heldDPad;
        private int heldDPadFrame = -10;
        private Vector2 stick;
        private int stickFrame = -10;

        // Buttons arrive from the input layer and from polling the Rewired player;
        // whichever comes first in a frame wins, the other is ignored.
        private void Fire(RewiredActionType action, string source) {
            if (covered || Time.frameCount <= ignoreInputUntil)
                return;
            int frame = Time.frameCount;
            if (firedFrame.TryGetValue(action, out var last) && frame - last <= 2)
                return;
            firedFrame[action] = frame;
            if (confirmGroup != null) {
                if (action == RewiredActionType.Confirm)
                    AnswerDelete(true);
                else if (action == RewiredActionType.Decline)
                    AnswerDelete(false);
                return;
            }
            if (padLogLines < 200) {
                padLogLines++;
                Main.Verbose($"[PAD] {action} via {source}, page={page}");
            }
            switch (action) {
                case RewiredActionType.Confirm: OnConfirm(); break;
                case RewiredActionType.Decline: OnDecline(); break;
                case RewiredActionType.Func01: OnFuncX(); break;
                case RewiredActionType.Func02: OnFuncY(); break;
                case RewiredActionType.LeftUp: OnShoulder(-1); break;
                case RewiredActionType.RightUp: OnShoulder(1); break;
            }
        }

        private void LogSourceOnce(string what) {
            if (loggedSources.Add(what))
                Main.Verbose($"[PAD] input source: {what}");
        }

        private void MarkDPad(RewiredActionType action) {
            heldDPad = action;
            heldDPadFrame = Time.frameCount;
            LogSourceOnce("dpad via layer");
        }

        private void MarkStick(Vector2 value) {
            stick = value;
            stickFrame = Time.frameCount;
            if (value.sqrMagnitude > StickThreshold * StickThreshold)
                LogSourceOnce("stick via layer");
        }

        private void PollButtons() {
            var player = GamePad.Instance?.Player;
            if (player == null)
                return;
            foreach (var action in MenuButtons) {
                bool down = false;
                try {
                    down = player.GetButtonDown((int)action);
                } catch (Exception) { }
                if (down)
                    Fire(action, "poll");
            }
        }

        private void PopInput() {
            layerHandle?.Dispose();
            layerHandle = null;
        }

        // The game's own hotkeys (Esc, pause, windows on letters) stay off while the menu is open.
        private void BlockGameKeys() {
            if (blockedKeyboard != null)
                return;
            var keyboard = Game.Instance?.Keyboard;
            if (keyboard == null)
                return;
            keyboard.Disabled.Value = true;
            blockedKeyboard = keyboard;
        }

        private void UnblockGameKeys() {
            if (blockedKeyboard == null)
                return;
            blockedKeyboard.Disabled.Value = false;
            blockedKeyboard = null;
        }

        // Keyboard: every gamepad action has a key next to WASD plus the usual duplicates (PadKeys).
        private void PollKeys() {
            if (Time.frameCount <= ignoreKeysUntil)
                return;
            foreach (var action in MenuButtons) {
                if (PadKeys.Pressed(PadKeys.For(action)))
                    Fire(action, "key");
            }
            if (Input.GetKeyDown(KeyCode.Escape) && !Game.Instance.IsControllerGamepad)
                OpenGameMenu();
            if (Input.GetKeyDown(KeyCode.Delete) && page == Page.Groups && confirmGroup == null)
                AskDelete();
            if (Input.GetKeyDown(KeyCode.Home))
                JumpTo(first: true);
            if (Input.GetKeyDown(KeyCode.End))
                JumpTo(first: false);
            if (page == Page.Main) {
                for (int i = 0; i < 9 && i < mainGroups.Count; i++) {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) {
                        selected = i;
                        UpdateMainSelection();
                        Enqueue(new[] { mainGroups[i] });
                    }
                }
            }
        }

        // On the name page letters are text; Enter and Backspace come through PollTyping. Backspace on an empty name
        // leaves the page (Q is a letter here, Esc opens the game's menu as everywhere).
        private void PollNameKeys() {
            if (Input.GetKeyDown(KeyCode.Backspace) && nameBuffer.Length == 0) {
                OnDecline();
                return;
            }
            PollTyping();
            if (Input.GetKeyDown(KeyCode.Escape) && !Game.Instance.IsControllerGamepad)
                OpenGameMenu();
            if (Input.GetKeyDown(KeyCode.PageUp))
                CycleDuration(-1);
            if (Input.GetKeyDown(KeyCode.PageDown))
                CycleDuration(1);
        }

        private void JumpTo(bool first) {
            int edge = first ? 0 : int.MaxValue;
            switch (page) {
                case Page.Main:
                    selected = Mathf.Clamp(edge, 0, MainRowCount - 1);
                    UpdateMainSelection();
                    break;
                case Page.Groups:
                    groupsCursor = Mathf.Clamp(edge, 0, GroupsRowCount - 1);
                    RenderGroups();
                    break;
                case Page.Members:
                    memberCursor = Mathf.Clamp(edge, 0, Mathf.Max(0, memberList.Count - 1));
                    RenderMembers();
                    break;
                case Page.Editor:
                    onFilter = first && filtered.Count == 0;
                    cursor = Mathf.Clamp(edge, 0, Mathf.Max(0, filtered.Count - 1));
                    RenderEditor();
                    break;
                case Page.Options:
                    optCursor = first ? -1 : opts.Count;
                    MoveOption(first ? 1 : -1);
                    break;
                case Page.Help:
                    ScrollHelp(first ? -HelpMaxOffset - helpOffset : HelpMaxOffset);
                    break;
            }
        }

        // ---------- mouse ----------

        // On release: the press and the release both land on the menu, so the game never sees half a click.
        private void PollMouse() {
            if (Input.GetMouseButtonUp(1))
                Fire(RewiredActionType.Decline, "mouse");
        }

        // A click on a button hint or icon does what the button does.
        private void Press(RewiredActionType button) {
            if (button == RewiredActionType.RightStickButton) {
                if (confirmGroup == null && page == Page.Groups)
                    AskDelete();
                return;
            }
            if (button == RewiredActionType.DPadRight) {
                MoveHorizontal(1);
                return;
            }
            Fire(button, "mouse");
        }

        // The wheel moves the visible part of a list by a few rows and takes the cursor along, so it stays in view.
        private void OnWheel(float y) {
            if (Mathf.Abs(y) < 0.01f || confirmGroup != null)
                return;
            int delta = y > 0 ? -WheelRows : WheelRows;
            switch (page) {
                case Page.Members: ScrollMembers(delta); break;
                case Page.Editor: ScrollEditor(delta); break;
                case Page.Options: ScrollOptions(delta); break;
                case Page.Help: ScrollHelp(delta * HelpStep); break;
            }
            PadPointer.Over?.Refresh();
        }

        private void ScrollMembers(int delta) {
            int next = Mathf.Clamp(memberTop + delta, 0, Mathf.Max(0, memberList.Count - MemberRows));
            if (next == memberTop)
                return;
            memberTop = next;
            memberCursor = Mathf.Clamp(memberCursor, memberTop, Mathf.Min(memberList.Count, memberTop + MemberRows) - 1);
            RenderMembers();
        }

        private void ScrollEditor(int delta) {
            int next = Mathf.Clamp(top + delta, 0, Mathf.Max(0, filtered.Count - VisibleRows));
            if (next == top)
                return;
            top = next;
            cursor = Mathf.Clamp(cursor, top, Mathf.Min(filtered.Count, top + VisibleRows) - 1);
            RenderEditor();
        }

        private void ScrollOptions(int delta) {
            int next = Mathf.Clamp(optTop + delta, 0, Mathf.Max(0, opts.Count - OptionRows));
            if (next == optTop)
                return;
            optTop = next;
            int last = Mathf.Min(opts.Count, optTop + OptionRows) - 1;
            if (optCursor < optTop || optCursor > last) {
                int step = optCursor < optTop ? 1 : -1;
                for (int at = step > 0 ? optTop : last; at >= optTop && at <= last; at += step) {
                    if (!opts[at].Header) {
                        optCursor = at;
                        break;
                    }
                }
            }
            optScrolled = true;
            RenderOptions();
        }

        // Group rows cast, so the first click only selects them; "cast in combat" flips at once.
        private void ClickMain(int i, PointerEventData e) {
            if (i >= MainRowCount)
                return;
            bool act = i == selected || i >= mainGroups.Count;
            selected = i;
            UpdateMainSelection();
            if (act)
                OnConfirm();
        }

        // The first click selects a group for the hints below (rename, hide, delete), the second opens it.
        private void ClickGroup(int i, PointerEventData e) {
            if (i >= GroupsRowCount)
                return;
            bool act = i == groupsCursor || i >= groupList.Count;
            groupsCursor = i;
            RenderGroups();
            if (act)
                OnConfirm();
        }

        // The tick box switches the buff on or off at once; the second click on a row adds or removes the buff.
        private void ClickMember(int r, PointerEventData e) {
            int at = memberTop + r;
            if (at >= memberList.Count)
                return;
            bool act = at == memberCursor;
            memberCursor = at;
            var box = memberRows[r].Box;
            var hit = e.pointerPressRaycast.gameObject;
            if (box != null && box.activeSelf && hit != null && hit.transform.IsChildOf(box.transform.parent))
                ToggleTick();
            else if (act)
                ToggleMembership();
            else
                RenderMembers();
        }

        // A row only selects the buff; its targets are switched on the party members below it.
        private void ClickEditorRow(int r) {
            int at = top + r;
            if (at >= filtered.Count || at == cursor && !onFilter)
                return;
            cursor = at;
            onFilter = false;
            RenderEditor();
        }

        // A click on the "show" row selects it, a click on its value steps it forth.
        private void ClickFilter(PointerEventData e) {
            bool act = onFilter || OnValue(filterRow.Text, e);
            onFilter = true;
            if (act)
                SwitchTab(1);
            else
                RenderEditor();
        }

        private void StepFilter(int direction) {
            onFilter = true;
            SwitchTab(direction);
        }

        private void ClickChip(int i) {
            if (i >= Party.Count)
                return;
            partyCol = i;
            onFilter = false;
            RenderEditor();
            ToggleTarget();
        }

        private void ClickTabs(PointerEventData e) {
            int link = TMP_TextUtilities.FindIntersectingLink(tabsText, e.position, ModBar.BarLayout.Cam(tabsText));
            if (link >= 0)
                OpenTab(int.Parse(tabsText.textInfo.linkInfo[link].GetLinkID()));
        }

        // The arrows of a value step it back or forth; a click on the value, or a second click on the row, steps it forth.
        private void ClickOption(int r, PointerEventData e) {
            int at = optTop + r;
            if (at >= opts.Count || opts[at].Header)
                return;
            var text = optRows[r].Text;
            int delta = 0;
            int link = TMP_TextUtilities.FindIntersectingLink(text, e.position, ModBar.BarLayout.Cam(text));
            if (link >= 0)
                delta = text.textInfo.linkInfo[link].GetLinkID() == "dec" ? -1 : 1;
            else if (at == optCursor || optRows[r].Control.Shown == ModBar.ModControl.Kind.None && OnValue(text, e))
                delta = 1;
            optCursor = at;
            if (delta != 0)
                ChangeOption(delta);
            else
                RenderOptions();
        }

        // The value column starts at 62% of the row (RenderOptions).
        private static bool OnValue(TextMeshProUGUI text, PointerEventData e) {
            var rect = text.rectTransform;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, ModBar.BarLayout.Cam(rect), out var local)
                && local.x >= rect.rect.xMin + rect.rect.width * 0.6f;
        }

        // An arrow of a choice steps it; a side of a switch turns it when the side is not its state.
        private void StepOption(int r, int direction) {
            int at = optTop + r;
            if (at >= opts.Count || opts[at].Header)
                return;
            optCursor = at;
            var opt = opts[at];
            if (opt.On == null || opt.On() != direction > 0)
                ChangeOption(opt.On == null ? direction : 1);
            else
                RenderOptions();
        }

        private void ClickKey(int r, int c) {
            if (r >= keys.Count || c >= keys[r].Count)
                return;
            keyRow = r;
            keyCol = c;
            RenderKeys();
            PressKey();
        }

        // ---------- key capture ----------

        private struct RepeatInput {
            private int last;
            private float next;

            public int Step(int dir) {
                float now = Time.unscaledTime;
                if (dir == 0) {
                    last = 0;
                    return 0;
                }
                if (dir != last) {
                    last = dir;
                    next = now + RepeatDelay;
                    return dir;
                }
                if (now >= next) {
                    next = now + RepeatInterval;
                    return dir;
                }
                return 0;
            }
        }

        private void PollDirections() {
            int v = 0, h = 0;
            int frame = Time.frameCount;
            if (frame - heldDPadFrame <= 1) {
                if (heldDPad == RewiredActionType.DPadUp) v = -1;
                else if (heldDPad == RewiredActionType.DPadDown) v = 1;
                else if (heldDPad == RewiredActionType.DPadLeft) h = -1;
                else if (heldDPad == RewiredActionType.DPadRight) h = 1;
            }
            if (v == 0 && h == 0 && frame - stickFrame <= 2) {
                if (stick.y > StickThreshold) v = -1;
                else if (stick.y < -StickThreshold) v = 1;
                if (stick.x > StickThreshold) h = 1;
                else if (stick.x < -StickThreshold) h = -1;
            }
            var player = GamePad.Instance?.Player;
            if (v == 0 && h == 0 && player != null) {
                if (player.GetButton((int)RewiredActionType.DPadUp) || player.GetButton((int)RewiredActionType.DPadDown)
                    || player.GetButton((int)RewiredActionType.DPadLeft) || player.GetButton((int)RewiredActionType.DPadRight))
                    LogSourceOnce("dpad via poll");
                if (player.GetButton((int)RewiredActionType.DPadUp)) v = -1;
                else if (player.GetButton((int)RewiredActionType.DPadDown)) v = 1;
                else {
                    float y = player.GetAxis((int)RewiredActionType.LeftStickY);
                    if (y > StickThreshold) v = -1;
                    else if (y < -StickThreshold) v = 1;
                }
                if (player.GetButton((int)RewiredActionType.DPadLeft)) h = -1;
                else if (player.GetButton((int)RewiredActionType.DPadRight)) h = 1;
                else {
                    float x = player.GetAxis((int)RewiredActionType.LeftStickX);
                    if (x > StickThreshold) h = 1;
                    else if (x < -StickThreshold) h = -1;
                }
            }
            // Letters are text on the name page, so WASD moves only elsewhere.
            bool letters = page != Page.Name;
            if (v == 0) v = PadKeys.Held(PadKeys.Up, letters) ? -1 : PadKeys.Held(PadKeys.Down, letters) ? 1 : 0;
            if (h == 0) h = PadKeys.Held(PadKeys.Left, letters) ? -1 : PadKeys.Held(PadKeys.Right, letters) ? 1 : 0;

            // A direction still held from the previous page does nothing until it is released.
            if (holdDirections) {
                if (v != 0 || h != 0)
                    return;
                holdDirections = false;
            }

            int dv = vertical.Step(v);
            int dh = horizontal.Step(h);
            if (confirmGroup != null)
                return;
            if (dv != 0) MoveVertical(dv);
            if (dh != 0) MoveHorizontal(dh);
        }

        private void OnConfirm() {
            switch (page) {
                case Page.Main:
                    if (selected < mainGroups.Count)
                        Enqueue(new[] { mainGroups[selected] });
                    else if (selected == CombatRow)
                        ToggleAllowInCombat();
                    break;
                case Page.Groups:
                    if (groupsCursor < groupList.Count)
                        OpenMembers(groupList[groupsCursor], Page.Groups);
                    else
                        OpenName(null);
                    break;
                case Page.Members: ToggleMembership(); break;
                case Page.Name: PressKey(); break;
                case Page.Editor:
                    if (onFilter)
                        SwitchTab(1);
                    else
                        ToggleTarget();
                    break;
                case Page.Options: ChangeOption(1); break;
            }
        }

        private void OnDecline() {
            if (OnTab) {
                Close();
                return;
            }
            switch (page) {
                case Page.Members: ShowPage(membersBack); break;
                case Page.Name: ShowPage(Page.Groups); break;
                case Page.Editor: OpenMembers(memberGroup, membersBack, SelectedBuff); break;
                case Page.Options: OpenMembers(memberGroup, membersBack, SelectedMember); break;
            }
        }

        private void OnFuncX() {
            switch (page) {
                case Page.Main: Enqueue(mainGroups); break;
                case Page.Groups: ToggleHidden(); break;
                case Page.Members: ToggleTick(); break;
                case Page.Name: Backspace(); break;
                case Page.Editor:
                    if (!onFilter)
                        AutoTargets();
                    break;
            }
        }

        private void OnFuncY() {
            switch (page) {
                case Page.Groups:
                    if (groupsCursor < groupList.Count)
                        OpenName(groupList[groupsCursor]);
                    break;
                case Page.Members: AutoFill(); break;
                case Page.Name: FinishName(); break;
                case Page.Editor:
                    if (!onFilter)
                        ToggleWholeParty();
                    break;
            }
        }

        // On a tab LB/RB switch tabs; the pages opened from a tab keep them for their own use.
        private void OnShoulder(int delta) {
            if (OnTab) {
                OpenTab(topTab + delta);
                return;
            }
            switch (page) {
                case Page.Members:
                    if (delta > 0 && SelectedMember != null)
                        OpenBuffOptions(SelectedMember);
                    break;
                case Page.Name: CycleDuration(delta); break;
            }
        }

        private void MoveVertical(int delta) {
            switch (page) {
                case Page.Main:
                    selected = Mathf.Clamp(selected + delta, 0, MainRowCount - 1);
                    UpdateMainSelection();
                    break;
                case Page.Groups:
                    groupsCursor = Mathf.Clamp(groupsCursor + delta, 0, GroupsRowCount - 1);
                    RenderGroups();
                    break;
                case Page.Members:
                    if (memberList.Count == 0)
                        return;
                    memberCursor = Mathf.Clamp(memberCursor + delta, 0, memberList.Count - 1);
                    RenderMembers();
                    break;
                case Page.Name:
                    keyRow = Mathf.Clamp(keyRow + delta, 0, keys.Count - 1);
                    keyCol = Mathf.Min(keyCol, keys[keyRow].Count - 1);
                    RenderKeys();
                    break;
                case Page.Editor:
                    // The "show" row is above the first buff.
                    if (onFilter) {
                        if (delta < 0 || filtered.Count == 0)
                            return;
                        onFilter = false;
                        cursor = 0;
                    } else if (delta < 0 && cursor == 0 || filtered.Count == 0) {
                        onFilter = true;
                    } else {
                        cursor = Mathf.Clamp(cursor + delta, 0, filtered.Count - 1);
                    }
                    RenderEditor();
                    break;
                case Page.Help: ScrollHelp(delta * HelpStep); break;
                case Page.Options: MoveOption(delta); break;
            }
        }

        private void MoveHorizontal(int delta) {
            switch (page) {
                case Page.Options: ChangeOption(delta); break;
                case Page.Members:
                    if (delta > 0 && SelectedMember != null)
                        OpenEditor(Page.Members, SelectedMember);
                    break;
                case Page.Name:
                    keyCol = Mathf.Clamp(keyCol + delta, 0, keys[keyRow].Count - 1);
                    RenderKeys();
                    break;
                case Page.Editor:
                    if (onFilter)
                        SwitchTab(delta);
                    else
                        MovePartyCursor(delta);
                    break;
                case Page.Help: JumpSection(delta); break;
            }
        }

        // ---------- pages ----------

        // The page is a tab itself, not one opened from a tab.
        private bool OnTab => page switch {
            Page.Main or Page.Groups or Page.Help => true,
            Page.Editor => editorBack != Page.Members,
            Page.Options => optBack != Page.Members,
            _ => false
        };

        private static string TabName(int i) => (i switch {
            CastTab => "pad.tab.cast",
            BuffsTab => "pad.tab.buffs",
            GroupsTab => "pad.groups.row",
            SettingsTab => "pad.settings.row",
            _ => "pad.help.row"
        }).i8();

        private void OpenTab(int i) {
            switch ((i % Tabs + Tabs) % Tabs) {
                case CastTab: ShowPage(Page.Main); break;
                case BuffsTab: OpenEditor(Page.Main, null); break;
                case GroupsTab: ShowPage(Page.Groups); break;
                case SettingsTab: OpenGlobalOptions(Page.Main); break;
                default: ShowPage(Page.Help); break;
            }
        }

        // The current tab is bold. A page opened from a tab has no tab row.
        private void RenderTabs() {
            if (OnTab)
                topTab = page switch {
                    Page.Main => CastTab,
                    Page.Editor => BuffsTab,
                    Page.Groups => GroupsTab,
                    Page.Options => SettingsTab,
                    _ => HelpTab
                };
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < Tabs; i++) {
                if (i > 0)
                    text.Append($"<color={PadTheme.Faint}>   ·   </color>");
                string name = i == topTab ? $"<b><color={PadTheme.Accent}>{TabName(i)}</color></b>" : TabName(i);
                text.Append($"<link=\"{i}\">{name}</link>");
            }
            tabsText.text = text.ToString();
            tabRow.SetActive(OnTab);
        }

        private void ShowPage(Page next) {
            page = next;
            holdDirections = true;
            mainRoot.SetActive(page == Page.Main);
            groupsRoot.SetActive(page == Page.Groups);
            membersRoot.SetActive(page == Page.Members);
            nameRoot.SetActive(page == Page.Name);
            editorRoot.SetActive(page == Page.Editor);
            helpRoot.SetActive(page == Page.Help);
            optionsRoot.SetActive(page == Page.Options);
            titleText.text = PadTheme.Initial(OnTab ? "pad.title".i8() : page switch {
                Page.Members => string.Format("pad.members.title".i8(), PadGroups.Name(memberGroup)),
                Page.Name => nameTarget == null ? "pad.name.title.new".i8() : "pad.name.title.rename".i8(),
                Page.Editor => "pad.editor.title".i8(),
                _ => optTitle
            });
            // Made while the menu was hidden, so Kingmaker gave it the default alignment on waking.
            titleText.alignment = TextAlignmentOptions.Center;
            RenderTabs();
            // B closes the menu on a tab and goes back to the tab from a page opened from it.
            string back = (OnTab ? "pad.h.close" : "pad.h.back").i8();
            editorTabHint.SetActive(OnTab);
            editorBackLabel.text = back;
            optionsTabHint.SetActive(OnTab);
            optionsBackLabel.text = back;
            EnsureBuffList();
            switch (page) {
                case Page.Main: RefreshMain(); break;
                case Page.Groups:
                    groupsNote.text = "";
                    RenderGroups();
                    break;
                case Page.Members: RebuildMembers(null); break;
                case Page.Name: RenderName(); break;
                case Page.Editor:
                    onFilter = false;
                    RebuildFilter(keepBuff: null);
                    break;
                case Page.Help:
                    OpenHelp();
                    break;
                case Page.Options: RebuildOptions(); break;
            }
        }

        private void EnsureBuffList() {
            var state = State;
            // In gamepad mode the spellbook is never opened, so the buff list is built here.
            if (state != null && state.BuffList == null && !recalcTried) {
                recalcTried = true;
                state.Recalculate(false);
                Main.Verbose($"[PAD] buff list built: {state.BuffList?.Count() ?? -1} buffs, party {Bubble.Group.Count}");
            }
        }

        private static void Commit() {
            PadGroups.SaveAll();
        }

        // ---------- main page ----------

        private void ToggleAllowInCombat() {
            var state = State;
            if (state == null)
                return;
            state.AllowInCombat = !state.AllowInCombat;
            RefreshCombatButtons();
            RefreshMain();
        }

        // The quick cast buttons follow "cast in combat"; the combat-start routine is not touched.
        private static void RefreshCombatButtons() {
            PadBar.UpdateInteractable();
        }

        // Groups run one after another: the next group is recalculated only after the
        // previous routine has spent its slots, otherwise both plan on the same slots.
        private void Enqueue(IEnumerable<BuffGroup> groups) {
            if (waiting || pending.Count > 0)
                return;
            report.Clear();
            foreach (var g in groups.ToList())
                pending.Enqueue(g);
            RunNext();
        }

        // A routine with nothing to cast finishes inside Execute, so "waiting" is set before
        // the call and the result handler resumes the queue only for asynchronous finishes.
        private void RunNext() {
            while (pending.Count > 0) {
                current = pending.Dequeue();
                waiting = true;
                waitDeadline = Time.unscaledTime + RoutineTimeout;
                report.Add(string.Format("pad.running".i8(), PadGroups.Name(current)));
                int before = BuffExecutor.ScheduledRoutines;
                inExecute = true;
                try {
                    GlobalBubbleBuffer.Execute(current);
                } catch (Exception ex) {
                    Main.Error(ex, "PadQuickMenu.Execute");
                } finally {
                    inExecute = false;
                }
                if (BuffExecutor.ScheduledRoutines == before) {
                    waiting = false;
                    ReplaceLast(string.Format("pad.blocked".i8(), PadGroups.Name(current)));
                    continue;
                }
                if (waiting) {
                    ShowReport();
                    return;
                }
            }
            ShowReport();
            RefreshMain();
        }

        private void OnRoutineFinished(string title, int applied, int attempted, int skipped, TooltipTemplateBuffer tooltip) {
            if (!waiting) {
                report.Clear();
                report.Add(string.Format("pad.result".i8(), title, applied, attempted, skipped));
                ShowReport();
                return;
            }
            waiting = false;
            ReplaceLast(string.Format("pad.result".i8(), PadGroups.Name(current), applied, attempted, skipped));
            foreach (var bad in tooltip.Bad.Take(6)) {
                var reasons = string.Join("; ", bad.messages.Select(m => m.Trim()).Take(2));
                report.Add($"   <color={PadTheme.Bad}>{bad.buff.Name}</color> {reasons}");
            }
            if (tooltip.Bad.Count > 6)
                report.Add($"   … +{tooltip.Bad.Count - 6}");
            if (!inExecute)
                RunNext();
        }

        private void ReplaceLast(string line) {
            if (report.Count > 0)
                report[report.Count - 1] = line;
            else
                report.Add(line);
        }

        private void ShowReport() {
            if (reportText == null)
                return;
            reportText.text = string.Join("\n", report);
            reportText.gameObject.SetActive(report.Count > 0);
        }

        private void UpdateMainSelection() {
            for (int i = 0; i < rows.Count; i++)
                rows[i].Bg.color = i == selected ? RowSelectedColor : RowColor;
            if (mainConfirmLabel != null && mainGroups != null)
                mainConfirmLabel.text = (selected < mainGroups.Count ? "pad.h.apply"
                    : State?.AllowInCombat == true ? "pad.h.disable" : "pad.h.enable").i8();
        }

        private void RefreshMain() {
            nextRefresh = Time.unscaledTime + 1f;
            try {
                mainGroups = PadGroups.Visible();
                EnsureRows(rows, mainRowHolder, MainRowCount, 22, withBox: false, ClickMain);
                selected = Mathf.Clamp(selected, 0, MainRowCount - 1);
                UpdateMainSelection();

                var state = State;
                rows[CombatRow].Text.text = CombatRowText(state);
                if (state?.BuffList == null) {
                    for (int i = 0; i < mainGroups.Count; i++)
                        rows[i].Text.text = Line(PadGroups.Name(mainGroups[i]), "pad.nostate".i8());
                    return;
                }

                Bubble.RefreshGroup();
                var unitData = Bubble.Group.ToDictionary(u => u.UniqueId, u => new UnitBuffData(u));

                for (int i = 0; i < mainGroups.Count; i++) {
                    var group = mainGroups[i];
                    int buffs = 0, off = 0, wanted = 0, active = 0;
                    TimeSpan? soonest = null;

                    foreach (var buff in state.BuffList) {
                        if (!PadGroups.IsMember(buff, group))
                            continue;
                        if (!buff.ActiveIn(group)) {
                            off++;
                            continue;
                        }
                        buffs++;
                        var guids = new HashSet<Guid>(buff.BuffsApplied?.OwnBuffGuids ?? Enumerable.Empty<Guid>());

                        foreach (var unit in Bubble.Group) {
                            if (!buff.UnitWants(unit))
                                continue;
                            wanted++;
                            bool present = false;
                            try {
                                present = buff.BuffsApplied != null
                                    && buff.BuffsApplied.IsPresent(unitData[unit.UniqueId], buff.IgnoreForOverwriteCheck);
                            } catch (Exception) { }
                            if (!present)
                                continue;
                            active++;
                            foreach (var fact in unit.Buffs.RawFacts.OfType<Kingmaker.UnitLogic.Buffs.Buff>()) {
                                if (fact.IsPermanent || !guids.Contains(fact.BGuid()))
                                    continue;
                                var left = fact.TimeLeft;
                                if (soonest == null || left < soonest)
                                    soonest = left;
                            }
                        }
                    }

                    string status;
                    if (wanted == 0) {
                        status = "pad.notset".i8();
                    } else {
                        var color = active < wanted ? PadTheme.Partial : PadTheme.Good;
                        status = $"<color={color}>{string.Format("pad.status".i8(), buffs, active, wanted)}</color>";
                        if (soonest.HasValue)
                            status += " · " + string.Format("pad.left".i8(), FormatTime(soonest.Value));
                    }
                    if (off > 0)
                        status += $" <color={PadTheme.Muted}>· {string.Format("pad.status.off".i8(), off)}</color>";
                    rows[i].Text.text = Line(PadGroups.Name(group), status);
                }
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.RefreshMain");
            }
        }

        private static string Line(string name, string desc) =>
            $"<b>{Trim(name, 30)}</b><pos=36%><size=85%>{desc}</size>";

        private static string CombatRowText(BufferState state) {
            string value = state == null
                ? "pad.nostate".i8()
                : state.AllowInCombat
                    ? $"<color={PadTheme.Good}>{"pad.combat.on".i8()}</color>"
                    : $"<color={PadTheme.Off}>{"pad.combat.off".i8()}</color>";
            return Line("pad.combat".i8(), value);
        }

        // ---------- groups page ----------

        private int GroupsRowCount => groupList.Count + (PadGroups.CanCreate ? 1 : 0);

        private void RenderGroups() {
            groupList = PadGroups.All();
            EnsureRows(groupRows, groupsRowHolder, GroupsRowCount, 21, withBox: false, ClickGroup);
            groupsCursor = Mathf.Clamp(groupsCursor, 0, GroupsRowCount - 1);
            // Only custom groups can be deleted, so the hint is offered only on them.
            groupsDeleteHint.SetActive(CursorOnCustom);
            var state = State;
            for (int i = 0; i < groupRows.Count; i++) {
                var row = groupRows[i];
                row.Bg.color = i == groupsCursor ? RowSelectedColor : RowColor;
                var group = i < groupList.Count ? groupList[i] : (BuffGroup?)null;
                bool custom = group != null && PadGroups.IsCustom(group.Value);
                if (group == null) {
                    row.Text.text = $"<color={PadTheme.Good}><b>+ {"pad.groups.new".i8()}</b></color>";
                    continue;
                }
                int members = 0, on = 0;
                if (state?.BuffList != null) {
                    foreach (var buff in state.BuffList) {
                        if (!PadGroups.IsMember(buff, group.Value))
                            continue;
                        members++;
                        if (buff.ActiveIn(group.Value))
                            on++;
                    }
                }
                string visibility = PadGroups.IsHidden(group.Value)
                    ? $"<color={PadTheme.Muted}>{"pad.groups.hidden".i8()}</color>"
                    : "pad.groups.shown".i8();
                string kind = custom ? "" : $" <color={PadTheme.Muted}>{"pad.groups.builtin".i8()}</color>";
                string name = $"<b>{Trim(PadGroups.Name(group.Value), custom ? CustomNameColumn : 40)}</b>{kind}";
                row.Text.text = $"{name}<pos=47%><size=85%>{PadGroups.DurationName(PadGroups.DurationOf(group.Value))}</size>"
                    + $"<pos=70%><size=85%>{string.Format("pad.groups.count".i8(), members, on)}</size><pos=88%><size=85%>{visibility}</size>";
            }
        }

        private void ToggleHidden() {
            if (groupsCursor >= groupList.Count)
                return;
            var group = groupList[groupsCursor];
            PadGroups.SetHidden(group, !PadGroups.IsHidden(group));
            RenderGroups();
        }

        private bool CursorOnCustom => groupsCursor < groupList.Count && PadGroups.IsCustom(groupList[groupsCursor]);

        // The right stick held on a custom group asks once; it asks again only after a release.
        private void PollDeleteHold() {
            bool held = false;
            if (page == Page.Groups && Game.Instance.IsControllerGamepad) {
                try {
                    held = GamePad.Instance?.Player?.GetButton((int)RewiredActionType.RightStickButton) == true;
                } catch (Exception) { }
            }
            if (!held) {
                deleteHeldSince = -1f;
                deleteHoldFired = false;
                if (deleteHoldFill != null)
                    deleteHoldFill.fillAmount = 0f;
                return;
            }
            if (deleteHeldSince < 0)
                deleteHeldSince = Time.unscaledTime;
            if (deleteHoldFill != null)
                deleteHoldFill.fillAmount = !CursorOnCustom || deleteHoldFired ? 0f : Mathf.Clamp01((Time.unscaledTime - deleteHeldSince) / DeleteHoldTime);
            if (!deleteHoldFired && confirmGroup == null && Time.unscaledTime - deleteHeldSince >= DeleteHoldTime) {
                deleteHoldFired = true;
                AskDelete();
            }
        }

        private void AskDelete() {
            if (!CursorOnCustom)
                return;
            confirmGroup = groupList[groupsCursor];
            confirmText.text = string.Format("pad.groups.ask".i8(), PadGroups.Name(confirmGroup.Value));
            // Kingmaker gives a text made on an inactive object its default settings when it wakes.
            confirmText.alignment = TextAlignmentOptions.Center;
            confirmRoot.SetActive(true);
            confirmRoot.transform.SetAsLastSibling();
        }

        private void AnswerDelete(bool yes) {
            var group = confirmGroup;
            confirmGroup = null;
            confirmRoot.SetActive(false);
            holdDirections = true;
            if (!yes || group == null)
                return;
            string name = PadGroups.Name(group.Value);
            PadGroups.Delete(group.Value);
            groupsNote.text = string.Format("pad.groups.deleted".i8(), name);
            RenderGroups();
        }

        // ---------- members page ----------

        private BubbleBuff SelectedMember => memberCursor >= 0 && memberCursor < memberList.Count ? memberList[memberCursor] : null;

        private void OpenMembers(BuffGroup group, Page back, BubbleBuff keep = null) {
            memberGroup = group;
            membersBack = back;
            membersNote.text = "";
            ShowPage(Page.Members);
            if (keep != null)
                RebuildMembers(keep);
        }

        // Members first, then the rest: buffs of the group's duration before the others.
        private void RebuildMembers(BubbleBuff keep) {
            Bubble.RefreshGroup();
            var state = State;
            var duration = PadGroups.DurationOf(memberGroup);
            var buffs = state?.BuffList == null
                ? new List<BubbleBuff>()
                : state.BuffList.Where(b => (PadSettings.Current.ShowHidden || !b.HideBecause(HideReason.Blacklisted)) && b.CasterQueue.Count > 0).ToList();
            var members = buffs.Where(b => PadGroups.IsMember(b, memberGroup))
                .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var others = buffs.Where(b => !PadGroups.IsMember(b, memberGroup))
                .OrderByDescending(b => PadGroups.Fits(b, duration))
                .ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase);
            memberCount = members.Count;
            memberList = members.Concat(others).ToList();
            if (keep != null) {
                int idx = memberList.IndexOf(keep);
                memberCursor = idx >= 0 ? idx : Mathf.Min(memberCursor, memberList.Count - 1);
            } else {
                memberCursor = 0;
                memberTop = 0;
            }
            memberCursor = Mathf.Max(0, memberCursor);
            RenderMembers();
        }

        private void RenderMembers() {
            if (memberCursor < memberTop)
                memberTop = memberCursor;
            if (memberCursor >= memberTop + MemberRows)
                memberTop = memberCursor - MemberRows + 1;
            memberTop = Mathf.Clamp(memberTop, 0, Mathf.Max(0, memberList.Count - MemberRows));
            memberBar.Set(memberTop, MemberRows, memberList.Count);

            for (int i = 0; i < memberRows.Count; i++) {
                int at = memberTop + i;
                var row = memberRows[i];
                if (at >= memberList.Count) {
                    row.Bg.color = Color.clear;
                    row.Box.SetActive(false);
                    row.SetIcon(null);
                    row.Text.text = at == 0 ? $"<color={PadTheme.Muted}>{"pad.editor.empty".i8()}</color>" : "";
                    continue;
                }
                var buff = memberList[at];
                bool member = at < memberCount;
                row.SetIcon(buff.Icon);
                row.Bg.color = at == memberCursor ? RowSelectedColor : member ? RowColor : RowOutsideColor;
                row.Box.SetActive(member);
                row.Tick.enabled = member && buff.ActiveIn(memberGroup);

                var kind = PadGroups.KindOf(buff, out var targets);
                string span = PadGroups.SpanName(PadGroups.Span(buff));
                string kindText = kind switch {
                    TargetKind.Self => "pad.kind.self".i8(),
                    TargetKind.Party => "pad.kind.party".i8(),
                    TargetKind.Song => "pad.kind.song".i8(),
                    _ => $"<color={PadTheme.Bad}>{"pad.kind.none".i8()}</color>"
                };
                string name = member ? Trim(buff.Name, 42) : $"<color={PadTheme.Outside}>{Trim(buff.Name, 42)}</color>";
                if (buff.HideBecause(HideReason.Blacklisted))
                    name = $"<color={PadTheme.Struck}><s>{Trim(buff.Name, 42)}</s></color>";
                string count = "";
                if (member) {
                    int wanted = Bubble.Group.Count(buff.UnitWants);
                    int reach = Bubble.Group.Count(buff.CanTarget);
                    count = $"<color={PadTheme.Good}>{wanted}/{reach}</color>";
                }
                row.Text.text = $"{name}<pos=52%><color={PadTheme.Soft}>{span}</color><pos=70%>{kindText}<pos=91%>{count}";
            }

            if (!iconsLogged && memberList.Count > 0) {
                iconsLogged = true;
                Main.Verbose($"[PAD] buff pictures: {memberList.Count(b => b.Icon != null)} of {memberList.Count}, row picture={(memberRows.Count > 0 && memberRows[0].Icon != null)}");
            }
            int on = memberList.Take(memberCount).Count(b => b.ActiveIn(memberGroup));
            string position = memberList.Count > MemberRows ? $"  <color={PadTheme.Muted}>{memberCursor + 1}/{memberList.Count}</color>" : "";
            string where = SelectedMember == null ? ""
                : memberCursor < memberCount ? "pad.members.inside".i8() : "pad.members.outside".i8();
            membersDetail.text = string.Format("pad.members.detail".i8(), memberCount, on,
                PadGroups.DurationName(PadGroups.DurationOf(memberGroup))) + (where.Length > 0 ? " · " + where : "") + position;
        }

        private void ToggleMembership() {
            var buff = SelectedMember;
            if (buff == null)
                return;
            if (PadGroups.IsMember(buff, memberGroup)) {
                bool lastGroup = buff.InGroups.Count == 1;
                PadGroups.Remove(buff, memberGroup);
                membersNote.text = string.Format(lastGroup ? "pad.members.removed.last".i8() : "pad.members.removed".i8(), buff.Name);
            } else if (PadGroups.Add(buff, memberGroup, on: true)) {
                var kind = PadGroups.KindOf(buff, out _);
                membersNote.text = string.Format("pad.members.added".i8(), buff.Name,
                    kind == TargetKind.Self ? "pad.kind.self".i8() : kind == TargetKind.Song ? "pad.kind.song".i8() : "pad.kind.party".i8());
            } else {
                membersNote.text = string.Format("pad.members.notargets".i8(), buff.Name);
                return;
            }
            Commit();
            RebuildMembers(buff);
        }

        private void ToggleTick() {
            var buff = SelectedMember;
            if (buff == null || !PadGroups.IsMember(buff, memberGroup)) {
                membersNote.text = T("pad.members.tick.outside");
                return;
            }
            PadGroups.SetOn(buff, memberGroup, !buff.ActiveIn(memberGroup));
            Commit();
            RenderMembers();
        }

        private void AutoFill() {
            int added = PadGroups.AutoFill(memberGroup);
            if (added < 0) {
                membersNote.text = T("pad.members.fill.any");
                return;
            }
            membersNote.text = string.Format(T("pad.members.fill"), added);
            Commit();
            RebuildMembers(SelectedMember);
        }

        // ---------- name page ----------

        private static readonly string[] RowsRu = { "ЙЦУКЕНГШЩЗХЪ", "ФЫВАПРОЛДЖЭ", "ЯЧСМИТЬБЮЁ", "1234567890-+/." };
        private static readonly string[] RowsEn = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM", "1234567890-+/." };
        private const string KeySpace = "\u0001space";
        private const string KeyShift = "\u0001shift";
        private const string KeyLang = "\u0001lang";
        private const string KeyErase = "\u0001erase";
        private const string KeyDone = "\u0001done";

        private void OpenName(BuffGroup? target) {
            if (target == null && !PadGroups.CanCreate) {
                groupsNote.text = "pad.groups.full".i8();
                return;
            }
            nameTarget = target;
            var duration = target == null ? GroupDuration.Any : PadGroups.DurationOf(target.Value);
            nameDuration = Mathf.Max(0, Array.IndexOf(PadGroups.Presets, duration));
            nameBuffer = "";
            if (target != null) {
                string current = PadGroups.Name(target.Value);
                if (current != PadGroups.DurationName(duration))
                    nameBuffer = current;
            }
            shift = false;
            keyRow = 0;
            keyCol = 0;
            BuildKeys();
            ShowPage(Page.Name);
        }

        private void BuildKeys() {
            foreach (Transform child in keyHolder)
                Destroy(child.gameObject);
            keys.Clear();
            var letters = latin ? RowsEn : RowsRu;
            foreach (var line in letters) {
                var rowGo = MakeHorizontal(keyHolder, "Keys", 6, TextAnchor.MiddleCenter);
                var row = new List<(Image, TextMeshProUGUI, string)>();
                foreach (char c in line)
                    row.Add(MakeKey(rowGo.transform, c.ToString(), c.ToString(), 58, keys.Count, row.Count));
                keys.Add(row);
            }
            var special = MakeHorizontal(keyHolder, "Keys", 6, TextAnchor.MiddleCenter);
            int last = keys.Count;
            keys.Add(new List<(Image, TextMeshProUGUI, string)> {
                MakeKey(special.transform, KeySpace, "pad.key.space".i8(), 200, last, 0),
                MakeKey(special.transform, KeyShift, "pad.key.shift".i8(), 110, last, 1),
                MakeKey(special.transform, KeyLang, latin ? "RU" : "EN", 90, last, 2),
                MakeKey(special.transform, KeyErase, "pad.key.erase".i8(), 150, last, 3),
                MakeKey(special.transform, KeyDone, "pad.key.done".i8(), 150, last, 4),
            });
            keyRow = Mathf.Clamp(keyRow, 0, keys.Count - 1);
            keyCol = Mathf.Clamp(keyCol, 0, keys[keyRow].Count - 1);
        }

        private (Image, TextMeshProUGUI, string) MakeKey(Transform parent, string key, string label, float width, int keyRowAt, int keyColAt) {
            var go = new GameObject("Key", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = KeyColor;
            AddPointer(go, tint: true).Click = _ => ClickKey(keyRowAt, keyColAt);
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 50;
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var text = MakeText(go.transform, font, label, 24, FontStyles.Normal, TextColor);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            return (bg, text, key);
        }

        private bool UpperNext => shift || nameBuffer.Length == 0;

        private void RenderKeys() {
            for (int r = 0; r < keys.Count; r++) {
                for (int c = 0; c < keys[r].Count; c++) {
                    var (bg, text, key) = keys[r][c];
                    bg.color = r == keyRow && c == keyCol ? RowSelectedColor : KeyColor;
                    if (key.Length == 1 && char.IsLetter(key[0]))
                        text.text = UpperNext ? key : key.ToLowerInvariant();
                    else if (key == KeyShift)
                        text.text = shift ? $"<color={PadTheme.Accent}>{"pad.key.shift".i8()}</color>" : "pad.key.shift".i8();
                }
            }
        }

        private void RenderName() {
            var duration = PadGroups.Presets[nameDuration];
            string shown = nameBuffer.Length > 0
                ? $"{nameBuffer}<color={PadTheme.Accent}>_</color>"
                : $"<color={PadTheme.Accent}>_</color><color={PadTheme.Faint}>{PadGroups.DurationName(duration)}</color>";
            nameText.text = $"<size=80%><color={PadTheme.Soft}>{"pad.name.label".i8()}</color></size>\n<size=130%>{shown}</size>";
            nameDurationText.text = string.Format("pad.name.duration".i8(), PadGroups.DurationName(duration));
            RenderKeys();
        }

        private void PressKey() {
            var key = keys[keyRow][keyCol].key;
            switch (key) {
                case KeySpace: Type(' '); break;
                case KeyShift: shift = !shift; RenderName(); break;
                case KeyLang:
                    latin = !latin;
                    BuildKeys();
                    RenderName();
                    break;
                case KeyErase: Backspace(); break;
                case KeyDone: FinishName(); break;
                default: Type(key[0]); break;
            }
        }

        private void Type(char c) {
            if (nameBuffer.Length >= PadGroups.NameLimit)
                return;
            if (c == ' ' && (nameBuffer.Length == 0 || nameBuffer.EndsWith(" ")))
                return;
            if (char.IsLetter(c))
                c = UpperNext ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
            nameBuffer += c;
            shift = false;
            RenderName();
        }

        private void Backspace() {
            if (nameBuffer.Length > 0)
                nameBuffer = nameBuffer.Substring(0, nameBuffer.Length - 1);
            RenderName();
        }

        // Keyboard text (Steam on-screen keyboard or a real one) is taken as typed.
        private void PollTyping() {
            foreach (char c in Input.inputString) {
                if (c == '\b') {
                    Backspace();
                } else if (c == '\n' || c == '\r') {
                    FinishName();
                    return;
                } else if (!char.IsControl(c) && nameBuffer.Length < PadGroups.NameLimit) {
                    nameBuffer += c;
                    RenderName();
                }
            }
        }

        private void CycleDuration(int delta) {
            nameDuration = (nameDuration + delta + PadGroups.Presets.Length) % PadGroups.Presets.Length;
            RenderName();
        }

        private void FinishName() {
            var duration = PadGroups.Presets[nameDuration];
            if (nameTarget == null) {
                var created = PadGroups.Create(nameBuffer, duration);
                if (created == null) {
                    groupsNote.text = "pad.groups.full".i8();
                    ShowPage(Page.Groups);
                    return;
                }
                Main.Verbose($"[PAD] group created: {created} '{PadGroups.Name(created.Value)}' {duration}");
                groupList = PadGroups.All();
                groupsCursor = groupList.IndexOf(created.Value);
                OpenMembers(created.Value, Page.Groups);
                membersNote.text = duration == GroupDuration.Any ? T("pad.members.new.any") : T("pad.members.new");
            } else {
                PadGroups.Rename(nameTarget.Value, nameBuffer, duration);
                ShowPage(Page.Groups);
            }
        }

        // ---------- editor page ----------

        private List<BuffGroup?> EditorTabs {
            get {
                var tabs = new List<BuffGroup?> { null, null };
                tabs.AddRange(PadGroups.All().Select(g => (BuffGroup?)g));
                return tabs;
            }
        }

        private static string EditorTabName(List<BuffGroup?> tabs, int i) =>
            i == 0 ? "pad.tab.all".i8() : i == 1 ? "pad.tab.assigned".i8() : PadGroups.Name(tabs[i].Value);

        private void OpenEditor(Page back, BubbleBuff buff) {
            editorBack = back;
            if (buff != null)
                tab = 0;
            ShowPage(Page.Editor);
            if (buff != null)
                RebuildFilter(buff);
        }

        private void SwitchTab(int delta) {
            int count = EditorTabs.Count;
            tab = (tab + delta + count) % count;
            RebuildFilter(keepBuff: null);
        }

        private bool PassesTab(BubbleBuff buff, List<BuffGroup?> tabs) {
            if (!PadSettings.Current.ShowHidden && buff.HideBecause(HideReason.Blacklisted))
                return false;
            if (tab == 0)
                return true;
            if (tab == 1)
                return buff.Requested > 0;
            return PadGroups.IsMember(buff, tabs[tab].Value);
        }

        private void RebuildFilter(BubbleBuff keepBuff) {
            var state = State;
            var tabs = EditorTabs;
            tab = Mathf.Clamp(tab, 0, tabs.Count - 1);
            filtered = state?.BuffList == null
                ? new List<BubbleBuff>()
                : state.BuffList.Where(b => PassesTab(b, tabs)).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (keepBuff != null) {
                int idx = filtered.IndexOf(keepBuff);
                cursor = idx >= 0 ? idx : Mathf.Min(cursor, filtered.Count - 1);
            } else {
                cursor = 0;
                top = 0;
            }
            cursor = Mathf.Max(0, cursor);
            RenderEditor();
        }

        private BubbleBuff SelectedBuff => cursor >= 0 && cursor < filtered.Count ? filtered[cursor] : null;

        private List<UnitEntityData> Party {
            get {
                Bubble.RefreshGroup();
                return Bubble.Group;
            }
        }

        private void MovePartyCursor(int delta) {
            int count = Party.Count;
            if (count == 0)
                return;
            partyCol = Mathf.Clamp(partyCol + delta, 0, count - 1);
            RenderEditor();
        }

        private void ToggleTarget() {
            var buff = SelectedBuff;
            var party = Party;
            if (buff == null || partyCol >= party.Count)
                return;
            var unit = party[partyCol];
            if (!buff.CanTarget(unit))
                return;
            bool removing = buff.UnitWants(unit);
            buff.SetUnitWants(unit, !removing);
            // Dropping the last target takes the buff out of every group.
            if (removing && buff.Requested == 0) {
                foreach (var g in buff.InGroups.ToList())
                    PadGroups.Remove(buff, g);
            }
            ApplyEdit(buff);
        }

        private void ToggleWholeParty() {
            var buff = SelectedBuff;
            if (buff == null)
                return;
            var targetable = Party.Where(buff.CanTarget).ToList();
            if (targetable.Count == 0)
                return;
            bool allWanted = targetable.All(buff.UnitWants);
            foreach (var unit in targetable)
                buff.SetUnitWants(unit, !allWanted);
            if (allWanted && buff.Requested == 0) {
                foreach (var g in buff.InGroups.ToList())
                    PadGroups.Remove(buff, g);
            }
            ApplyEdit(buff);
        }

        private void AutoTargets() {
            var buff = SelectedBuff;
            if (buff == null)
                return;
            PadGroups.AutoTarget(buff);
            ApplyEdit(buff);
        }

        private void ApplyEdit(BubbleBuff buff) {
            Commit();
            RebuildFilter(keepBuff: buff);
        }

        private void RenderEditor() {
            var party = Party;
            if (partyCol >= party.Count)
                partyCol = Mathf.Max(0, party.Count - 1);

            // The choice keeps the width of its longest word while stepping, as the plates of the settings.
            var tabs = EditorTabs;
            filterRow.Bg.color = onFilter ? RowSelectedColor : RowColor;
            filterRow.Text.text = "pad.show".i8();
            // Measured on the shown control: TMP measures a text of an inactive object with the wrong font size.
            filterRow.Control.Least = 0f;
            filterRow.Control.SetChoice(Trim(EditorTabName(tabs, tab), 30));
            float least = 0f;
            for (int i = 0; i < tabs.Count; i++)
                least = Mathf.Max(least, filterRow.Control.Need(Trim(EditorTabName(tabs, i), 30)));
            filterRow.Control.Least = least;
            filterRow.Control.SetChoice(Trim(EditorTabName(tabs, tab), 30));

            if (cursor < top)
                top = cursor;
            if (cursor >= top + VisibleRows)
                top = cursor - VisibleRows + 1;
            top = Mathf.Clamp(top, 0, Mathf.Max(0, filtered.Count - VisibleRows));
            editorBar.Set(top, VisibleRows, filtered.Count);

            for (int i = 0; i < listRows.Count; i++) {
                int idx = top + i;
                var row = listRows[i];
                if (idx >= filtered.Count) {
                    row.Bg.color = Color.clear;
                    row.SetIcon(null);
                    row.Text.text = idx == 0 ? $"<color={PadTheme.Muted}>{"pad.editor.empty".i8()}</color>" : "";
                    continue;
                }
                var buff = filtered[idx];
                row.Bg.color = idx == cursor && !onFilter ? RowSelectedColor : RowColor;
                row.SetIcon(buff.Icon);
                var targetable = party.Where(buff.CanTarget).ToList();
                int wanted = targetable.Count(buff.UnitWants);
                string span = $"<color={PadTheme.Soft}>{PadGroups.SpanName(PadGroups.Span(buff))}</color>";
                string count = wanted > 0 ? $"<color={PadTheme.Good}>{wanted}/{targetable.Count}</color>" : $"<color={PadTheme.Faint}>0/{targetable.Count}</color>";
                int groups = buff.Requested > 0 ? buff.InGroups.Count : 0;
                string groupMark = groups > 0 ? $"<color={PadTheme.Soft}>{string.Format("pad.editor.groups".i8(), groups)}</color>" : "";
                row.Text.text = $"{Trim(buff.Name, 46)}<pos=56%>{span}<pos=72%>{groupMark}<pos=91%>{count}";
            }

            string more = filtered.Count > VisibleRows ? $"  <color={PadTheme.Muted}>{cursor + 1}/{filtered.Count}</color>" : "";
            var selectedBuff = SelectedBuff;
            if (selectedBuff == null) {
                detailText.text = more;
            } else {
                var names = selectedBuff.Requested > 0
                    ? string.Join(", ", PadGroups.All().Where(g => PadGroups.IsMember(selectedBuff, g))
                        .Select(g => selectedBuff.ActiveIn(g) ? PadGroups.Name(g) : $"{PadGroups.Name(g)} ({"pad.off".i8()})"))
                    : "pad.editor.nogroup".i8();
                detailText.text = string.Format("pad.editor.detail".i8(), selectedBuff.Name, names, selectedBuff.CasterQueue.Count) + more;
            }

            EnsureChips(party.Count);
            for (int i = 0; i < chips.Count; i++) {
                var (bg, text) = chips[i];
                if (i >= party.Count) {
                    bg.gameObject.SetActive(false);
                    continue;
                }
                bg.gameObject.SetActive(true);
                var unit = party[i];
                bool can = selectedBuff != null && selectedBuff.CanTarget(unit);
                bool wants = selectedBuff != null && selectedBuff.UnitWants(unit);
                bool atCursor = i == partyCol && !onFilter;
                bg.color = atCursor ? (wants ? ChipCursorWantedColor : ChipCursorColor) : (wants ? ChipWantedColor : ChipColor);
                string name = Trim(unit.CharacterName, 11);
                text.text = can ? name : $"<color={PadTheme.Blocked}><s>{name}</s></color>";
            }
        }

        private void EnsureChips(int count) {
            while (chips.Count < count) {
                var chip = new GameObject("Chip", typeof(RectTransform));
                chip.transform.SetParent(chipHolder, false);
                var bg = chip.AddComponent<Image>();
                int index = chips.Count;
                AddPointer(chip, tint: true).Click = _ => ClickChip(index);
                var layout = chip.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 6, 6);
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                var text = MakeText(chip.transform, font, "", 19, FontStyles.Normal, TextColor);
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                chips.Add((bg, text));
            }
        }

        // ---------- options page ----------

        private void OpenGlobalOptions(Page back) {
            optBack = back;
            optTitle = "pad.settings.title".i8();
            optSource = GlobalOptions;
            optCursor = -1;
            optTop = 0;
            ShowPage(Page.Options);
        }

        private void OpenBuffOptions(BubbleBuff buff) {
            var key = buff.Key;
            optBack = Page.Members;
            optTitle = string.Format("pad.buffset.title".i8(), Trim(buff.Name, 40));
            optSource = () => BuffOptions(key);
            optCursor = -1;
            optTop = 0;
            ShowPage(Page.Options);
        }

        // The list is rebuilt after every change: a change can rescan the buffs, reorder casters
        // or show other rows. The cursor stays on the row with the same id.
        private void RebuildOptions() {
            if (optSource == null)
                return;
            string keep = optCursor >= 0 && optCursor < opts.Count ? opts[optCursor].Id : null;
            try {
                opts = optSource();
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.RebuildOptions");
                opts = new List<Opt>();
            }
            optPlates.Clear();
            int found = keep == null ? -1 : opts.FindIndex(o => o.Id == keep);
            if (found >= 0) {
                optCursor = found;
                RenderOptions();
            } else {
                optCursor = Mathf.Clamp(optCursor, -1, opts.Count - 1);
                if (optCursor < 0 || opts[optCursor].Header)
                    MoveOption(1);
                else
                    RenderOptions();
            }
        }

        private void MoveOption(int delta) {
            int at = optCursor;
            for (int i = 0; i < opts.Count; i++) {
                at += delta;
                if (at < 0 || at >= opts.Count)
                    break;
                if (!opts[at].Header) {
                    optCursor = at;
                    break;
                }
            }
            RenderOptions();
        }

        private void ChangeOption(int delta) {
            if (optCursor < 0 || optCursor >= opts.Count || opts[optCursor].Header)
                return;
            try {
                opts[optCursor].Change(delta);
                Commit();
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.ChangeOption");
            }
            if (rebuildRequested) {
                rebuildRequested = false;
                Rebuild();
                return;
            }
            RebuildOptions();
        }

        private void RenderOptions() {
            if (!optScrolled) {
                if (optCursor >= 0 && optCursor < optTop)
                    optTop = optCursor;
                if (optCursor >= optTop + OptionRows)
                    optTop = optCursor - OptionRows + 1;
                // Keep the heading above the first row in view.
                if (optTop > 0 && optCursor == optTop && opts[optTop - 1].Header)
                    optTop--;
            }
            optScrolled = false;
            optTop = Mathf.Clamp(optTop, 0, Mathf.Max(0, opts.Count - OptionRows));
            optionBar.Set(optTop, OptionRows, opts.Count);
            for (int i = 0; i < optRows.Count; i++) {
                int at = optTop + i;
                var row = optRows[i];
                if (at >= opts.Count) {
                    row.Bg.color = Color.clear;
                    row.Text.text = "";
                    row.Control.Hide();
                    continue;
                }
                var opt = opts[at];
                if (opt.Header) {
                    row.Bg.color = Color.clear;
                    row.Text.text = $"<color={PadTheme.Accent}><b>{opt.Label}</b></color>";
                    row.Control.Hide();
                    continue;
                }
                bool atCursor = at == optCursor;
                row.Control.Least = 0f;
                row.Bg.color = atCursor ? RowSelectedColor : RowColor;
                if (opt.On != null) {
                    row.Text.text = Trim(opt.Label, 48);
                    row.Control.SetSwitch(opt.On(), Initial("pad.opt.on".i8()), Initial("pad.opt.off".i8()));
                } else if (opt.Choice != null) {
                    row.Text.text = Trim(opt.Label, 48);
                    row.Control.SetChoice(opt.Choice());
                    if (opt.Plate != null) {
                        row.Control.Least = PlateWidth(opt, row.Control);
                        row.Control.SetChoice(opt.Choice());
                    }
                } else {
                    row.Control.Hide();
                    string value = opt.Value?.Invoke() ?? "";
                    row.Text.text = $"{Trim(opt.Label, 48)}<pos=62%>{value}";
                }
            }
            var current = optCursor >= 0 && optCursor < opts.Count ? opts[optCursor] : null;
            string more = opts.Count > OptionRows ? $"  <color={PadTheme.Muted}>{optCursor + 1}/{opts.Count}</color>" : "";
            optDetail.text = (current?.Note ?? "") + more;
        }

        // A plate group as wide as the longest word any of its choices may show. Measured on a shown control:
        // TMP measures a text of an inactive object with the wrong font size.
        private float PlateWidth(Opt shown, ModBar.ModControl control) {
            if (optPlates.TryGetValue(shown.Plate, out var width))
                return width;
            foreach (var opt in opts.Where(o => o.Plate == shown.Plate && o.Choice != null))
                foreach (var word in opt.Words?.Invoke() ?? new[] { opt.Choice() })
                    width = Mathf.Max(width, control.Need(word));
            return optPlates[shown.Plate] = width;
        }

        private static string OnOff(bool on) => on
            ? $"<color={PadTheme.Good}>{"pad.opt.on".i8()}</color>"
            : $"<color={PadTheme.Off}>{"pad.opt.off".i8()}</color>";

        // Labels of the original settings end with a colon.
        private static string Label(string key) => key.i8().Trim().TrimEnd(':').Trim();

        private static Opt Head(string text) => new() { Id = "head:" + text, Label = text };

        private static string Initial(string word) => word.Length > 0 ? char.ToUpper(word[0]) + word.Substring(1).TrimEnd('.') : word;

        private static Opt Flag(string id, string label, Func<bool> get, Action<bool> set, string note = null) => new() {
            Id = id,
            Label = label,
            On = get,
            Value = () => OnOff(get()),
            Change = _ => set(!get()),
            Note = note
        };

        private static Opt Cycle(string id, string label, Func<string> value, Action<int> change, string note = null,
            Func<IEnumerable<string>> words = null, string plate = null) => new() {
            Id = id,
            Label = label,
            Choice = value,
            Words = words,
            Plate = plate,
            Value = () => $"<link=\"dec\"><color={PadTheme.Heading}>‹</color> </link>{value()}<link=\"inc\"> <color={PadTheme.Heading}>›</color></link>",
            Change = change,
            Note = note
        };

        private static int Wrap(int value, int delta, int count) => ((value + delta) % count + count) % count;

        private List<Opt> GlobalOptions() {
            var list = new List<Opt>();
            var s = State;
            if (s == null) {
                list.Add(Head("pad.nostate".i8()));
                return list;
            }
            var saved = s.SavedState;
            var pad = PadSettings.Current;

            list.Add(Head("pad.set.cast".i8()));
            list.Add(Flag("combat", Label("setting-in-combat"), () => s.AllowInCombat, v => {
                s.AllowInCombat = v;
                RefreshCombatButtons();
            }));
            list.Add(Flag("castall", Label("setting-cast-all-combat-start"), () => s.CastAllOnCombatStart, v => s.CastAllOnCombatStart = v,
                "setting-cast-all-combat-start-tooltip".i8()));
            list.Add(Flag("skipanim", Label("setting-skip-combat-anim"), () => s.SkipAnimationsOnCombatStart, v => s.SkipAnimationsOnCombatStart = v,
                "pad.set.skipanim.note".i8()));
            list.Add(Flag("overwrite", Label("setting-overwritebuff"), () => s.OverwriteBuff, v => s.OverwriteBuff = v,
                "setting-overwritebuff-tooltip".i8()));
            list.Add(Flag("verbose", Label("setting-verbose"), () => s.VerboseCasting, v => s.VerboseCasting = v,
                "setting-verbose-tooltip".i8()));
#if !KINGMAKER
            list.Add(Flag("asf", Label("setting-bypass-asf"), () => s.BypassArcaneSpellFailure, v => s.BypassArcaneSpellFailure = v,
                "setting-bypass-asf-tooltip".i8()));
#endif

            list.Add(Head("pad.set.sources".i8()));
            void Source(string id, string key, Func<bool> get, Action<bool> set) =>
                list.Add(Flag(id, Label(key), get, v => {
                    set(v);
                    s.InputDirty = true;
                    s.Save(true);
                }, "pad.set.source.note".i8()));
            Source("songs", "setting-songs-enabled", () => saved.SongsEnabled, v => saved.SongsEnabled = v);
            Source("toggles", "setting-activatables-enabled", () => saved.ActivatablesEnabled, v => saved.ActivatablesEnabled = v);
            Source("scrolls", "setting-scrolls-enabled", () => saved.ScrollsEnabled, v => saved.ScrollsEnabled = v);
            Source("potions", "setting-potions-enabled", () => saved.PotionsEnabled, v => saved.PotionsEnabled = v);
            Source("equipment", "setting-equipment-enabled", () => saved.EquipmentEnabled, v => saved.EquipmentEnabled = v);
            list.Add(Cycle("priority", Label("setting-source-priority"), () => PriorityName((int)saved.GlobalSourcePriority), d => {
                saved.GlobalSourcePriority = (SourcePriority)Wrap((int)saved.GlobalSourcePriority, d, 6);
                s.Save(true);
            }, "setting-source-priority-tooltip".i8(), () => Enumerable.Range(0, 6).Select(PriorityName), "sources"));
            list.Add(Cycle("umdmode", Label("setting-umd-mode"), () => UmdName(saved.UmdMode), d => {
                saved.UmdMode = (UmdMode)Wrap((int)saved.UmdMode, d, 3);
                s.Save(true);
            }, "setting-umd-mode-tooltip".i8(), () => Enumerable.Range(0, 3).Select(i => UmdName((UmdMode)i)), "sources"));
            list.Add(Cycle("umdretries", Label("setting-umd-retries"), () => saved.UmdRetries.ToString(), d => {
                saved.UmdRetries = Mathf.Clamp(saved.UmdRetries + d, 1, 20);
                s.Save(true);
            }, "setting-umd-retries-tooltip".i8()));

            list.Add(Head("pad.set.view".i8()));
            list.Add(Cycle("display", "pad.set.display".i8(), () => DisplayName(pad.Display), d => {
                pad.Display = (PadDisplay)Wrap((int)pad.Display, d, 3);
                PadSettings.Save();
                rebuildRequested = true;
            }, "pad.set.display.note".i8()));
            list.Add(Flag("hidden", Label("showhidden"), () => pad.ShowHidden, v => {
                pad.ShowHidden = v;
                PadSettings.Save();
            }, "pad.set.showhidden.note".i8()));
            return list;
        }

        private List<Opt> BuffOptions(BuffKey key) {
            var list = new List<Opt>();
            var s = State;
            var buff = s?.BuffList?.FirstOrDefault(b => b.Key.Equals(key));
            if (buff == null) {
                list.Add(Head("pad.buffset.gone".i8()));
                return list;
            }

            list.Add(Head(buff.Name));
            list.Add(Flag("combatstart", Label("use.combatstart"), () => buff.CastOnCombatStart, v => buff.CastOnCombatStart = v,
                "use.combatstart.tooltip".i8()));
            if (buff.IsActivatable) {
                list.Add(Cycle("rounds", Label("deactivate.after.rounds"),
                    () => buff.DeactivateAfterRounds == 0 ? "nolimit".i8() : buff.DeactivateAfterRounds.ToString(),
                    d => buff.DeactivateAfterRounds = Mathf.Clamp(buff.DeactivateAfterRounds + d, 0, 20),
                    "deactivate.after.rounds.tooltip".i8()));
            }
            list.Add(Flag("hide", Label("hideability"), () => buff.HideBecause(HideReason.Blacklisted),
                v => buff.SetHidden(HideReason.Blacklisted, v), "pad.buffset.hide.note".i8()));
            list.Add(Head("pad.buffset.sources".i8()));
            list.Add(Flag("spells", Label("use.spells"), () => buff.UseSpells, v => buff.UseSpells = v));
            list.Add(Flag("scrolls", Label("use.scrolls"), () => buff.UseScrolls, v => buff.UseScrolls = v));
            list.Add(Flag("potions", Label("use.potions"), () => buff.UsePotions, v => buff.UsePotions = v));
            list.Add(Flag("equipment", Label("use.equipment"), () => buff.UseEquipment, v => buff.UseEquipment = v));
            list.Add(Flag("extendrod", Label("use.extendrod"), () => buff.UseExtendRod, v => buff.UseExtendRod = v));
            list.Add(Cycle("priority", Label("setting-source-priority"),
                () => buff.SourcePriorityOverride < 0 ? "priority.useglobal".i8() : PriorityName(buff.SourcePriorityOverride),
                d => buff.SourcePriorityOverride = Wrap(buff.SourcePriorityOverride + 1, d, 7) - 1, null,
                () => Enumerable.Range(0, 6).Select(PriorityName).Append("priority.useglobal".i8()), "priority"));

            var ranks = s.SavedState.CasterRanks ??= new Dictionary<string, int>();
            for (int i = 0; i < buff.CasterQueue.Count; i++) {
                var caster = buff.CasterQueue[i];
                string id = $"caster:{caster.Key.Name}:{caster.Key.Spellbook}:{caster.SourceType}:{caster.SourceItem?.Name}";
                list.Add(Head($"{caster.who.CharacterName} · {SourceName(caster)}"));
                list.Add(Flag(id + ":ban", Label("bancasts"), () => caster.Banned, v => caster.Banned = v));
                if (caster.MaxCap < 100) {
                    list.Add(Cycle(id + ":cap", Label("limitcasts"),
                        () => caster.CustomCap < 0 ? $"{"nolimit".i8()} ({caster.MaxCap})" : $"{caster.CustomCap}/{caster.MaxCap}",
                        d => caster.CustomCap = NextCap(caster, d)));
                }
                if (!buff.IsActivatable) {
                    string unitId = caster.who.UniqueId;
                    int Global() => ranks.TryGetValue(unitId, out var r) ? r : 0;
                    list.Add(Cycle(id + ":rank", Label("caster.rank.global"), () => Global().ToString("+#;-#;0"), d => {
                        int next = Global() + d;
                        if (next == 0)
                            ranks.Remove(unitId);
                        else
                            ranks[unitId] = next;
                        foreach (var b in s.BuffList)
                            b.SortProviders();
                    }, "caster.rank-tooltip".i8()));
                    list.Add(Cycle(id + ":buffrank", Label("caster.rank.buff"),
                        () => caster.PriorityOverride?.ToString("+#;-#;0") ?? $"<color={PadTheme.Muted}>{Global():+#;-#;0}</color>",
                        d => {
                            int next = (caster.PriorityOverride ?? Global()) + d;
                            caster.PriorityOverride = next == Global() ? (int?)null : next;
                            buff.SortProviders();
                        }, "caster.rank-tooltip".i8()));
                }
#if !KINGMAKER
                AddArcanistOptions(list, id, buff, caster);
#endif
            }
            return list;
        }

        // As in the original caster popout: the full count means no limit.
        private static int NextCap(BuffProvider caster, int delta) {
            int value = caster.CustomCap < 0 ? caster.MaxCap : caster.CustomCap;
            value = Mathf.Clamp(value + delta, 0, caster.MaxCap);
            return value >= caster.MaxCap ? -1 : value;
        }

#if !KINGMAKER
        // Shown only where the original popout enabled them.
        private static void AddArcanistOptions(List<Opt> list, string id, BubbleBuff buff, BuffProvider caster) {
            if (caster.spell == null)
                return;
            bool transmutation = caster.spell.IsArcanistSpell
                && caster.spell.Blueprint.School == Kingmaker.Blueprints.Classes.Spells.SpellSchool.Transmutation;
            if (transmutation && caster.who.HasFact(BubbleBuffSpellbookController.ShareTransmutationFeature))
                list.Add(Flag(id + ":share", Label("use.sharetransmutation"), () => caster.ShareTransmutation, v => caster.ShareTransmutation = v));
            if (transmutation && caster.who.HasFact(BubbleBuffSpellbookController.PowerfulChangeFeature))
                list.Add(Flag(id + ":powerful", Label("use.powerfulchange"), () => caster.PowerfulChange, v => caster.PowerfulChange = v));
            if ((caster.spell.IsArcanistSpell || caster.spell.Spellbook.Blueprint == Utilities.SpellTools.Spellbook.ExploiterWizardSpellbook)
                && caster.who.HasFact(BubbleBlueprints.ReservoirBaseAbility)
                && !caster.who.Progression.IsArchetype(BubbleBlueprints.PhantasmalMageArchetype))
                list.Add(Flag(id + ":reservoir", Label("use.reservoirclbuff"), () => caster.ReservoirCLBuff, v => caster.ReservoirCLBuff = v));
            if (caster.who.HasFact(BubbleBuffSpellbookController.AzataZippyMagicFeature) && !buff.IsMass
                && (caster.ShareTransmutation || !caster.SelfCastOnly))
                list.Add(Flag(id + ":zippy", Label("use.azatazippymagic"), () => caster.AzataZippyMagic, v => caster.AzataZippyMagic = v));
        }
#endif

        private static string PriorityName(int priority) => priority switch {
            0 => "priority.spells-scrolls-potions".i8(),
            1 => "priority.spells-potions-scrolls".i8(),
            2 => "priority.scrolls-spells-potions".i8(),
            3 => "priority.scrolls-potions-spells".i8(),
            4 => "priority.potions-spells-scrolls".i8(),
            _ => "priority.potions-scrolls-spells".i8()
        };

        private static string UmdName(UmdMode mode) => mode switch {
            UmdMode.SafeOnly => "umd.safeonly".i8(),
            UmdMode.AlwaysTry => "umd.alwaystry".i8(),
            _ => "umd.allowifpossible".i8()
        };

        private static string SourceName(BuffProvider caster) => caster.SourceType switch {
            BuffSourceType.Scroll => "source.scroll".i8(),
            BuffSourceType.Potion => "source.potion".i8(),
            BuffSourceType.Equipment => "source.equipment".i8(),
            BuffSourceType.Song => "source.song".i8(),
            BuffSourceType.Activatable => "source.activatable".i8(),
            _ => caster.book?.Blueprint?.Name ?? "source.spell".i8()
        };

        private static string DisplayName(PadDisplay display) => display switch {
            PadDisplay.Gamepad => "pad.set.display.pad".i8(),
            PadDisplay.Keyboard => "pad.set.display.kb".i8(),
            _ => "pad.set.display.auto".i8()
        };

        // ---------- help page ----------

        private void OpenHelp() {
            helpSections.Clear();
            helpSectionOffsets = null;
            helpText.spriteAsset = PadHelp.IconAsset;
            helpText.text = PadHelp.Build(helpSections);
            if (PadHelp.FitIcons(helpText)) {
                helpSections.Clear();
                helpText.text = PadHelp.Build(helpSections);
            }
            helpOffset = 0;
            ScrollHelp(0);
            foreach (var rule in helpRules)
                Destroy(rule);
            helpRules.Clear();
            helpRulesPending = PadTheme.HasSeparator;
        }

        // The rule under each heading goes into the room left after the heading line, once the text has its final width;
        // as a child of the text it scrolls with it.
        private void LateUpdate() {
            if (!helpRulesPending || page != Page.Help || helpText.rectTransform.rect.width <= 0)
                return;
            helpRulesPending = false;
            try {
                helpText.ForceMeshUpdate();
                var info = helpText.textInfo;
                foreach (int source in helpSections) {
                    for (int i = 0; i < info.characterCount; i++) {
                        if (info.characterInfo[i].index < source || !info.characterInfo[i].isVisible)
                            continue;
                        var rule = PadTheme.Separator(helpText.transform);
                        if (rule == null)
                            return;
                        rule.anchorMin = new Vector2(0, 1);
                        rule.anchorMax = new Vector2(1, 1);
                        rule.pivot = new Vector2(0.5f, 1);
                        rule.sizeDelta = new Vector2(0, PadTheme.SeparatorHeight);
                        rule.anchoredPosition = new Vector2(0, info.lineInfo[info.characterInfo[i].lineNumber].descender - 2);
                        helpRules.Add(rule.gameObject);
                        break;
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.HelpRules");
            }
        }

        private float HelpMaxOffset {
            get {
                Canvas.ForceUpdateCanvases();
                return Mathf.Max(0, helpText.preferredHeight - HelpHeight);
            }
        }

        private void ScrollHelp(float by) {
            float max = HelpMaxOffset;
            helpOffset = Mathf.Clamp(helpOffset + by, 0, max);
            var rect = (RectTransform)helpText.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, helpOffset);
            helpBar.Set(helpOffset, HelpHeight, HelpHeight + max);
        }

        // Left/right jump to the previous or next heading. Offsets are measured on first use,
        // when the text already has its final width.
        private void JumpSection(int delta) {
            if (helpSectionOffsets == null) {
                helpSectionOffsets = new List<float>();
                try {
                    helpText.ForceMeshUpdate();
                    var info = helpText.textInfo;
                    foreach (int source in helpSections) {
                        for (int i = 0; i < info.characterCount; i++) {
                            if (info.characterInfo[i].index < source || !info.characterInfo[i].isVisible)
                                continue;
                            helpSectionOffsets.Add(Mathf.Max(0, -info.lineInfo[info.characterInfo[i].lineNumber].ascender - 8));
                            break;
                        }
                    }
                } catch (Exception ex) {
                    Main.Error(ex, "PadQuickMenu.JumpSection");
                }
            }
            float target;
            if (delta > 0) {
                target = helpSectionOffsets.Where(o => o > helpOffset + 2).DefaultIfEmpty(helpOffset + HelpHeight - HelpStep).First();
            } else {
                target = helpSectionOffsets.Where(o => o < helpOffset - 2).DefaultIfEmpty(0).Last();
            }
            ScrollHelp(target - helpOffset);
        }

        // ---------- helpers ----------

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max - 1) + "…";

        internal static string GroupName(BuffGroup group) => PadGroups.Name(group);

        // Localized text with button tokens turned into icons.
        private static string T(string key) => PadHelp.Tokens(key.i8());

        private static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";

        private void EnsureRows(List<ListRow> list, Transform holder, int count, float size, bool withBox, Action<int, PointerEventData> click) {
            while (list.Count < count) {
                int index = list.Count;
                var row = MakeListRow(holder, font, size, withBox);
                row.Pointer.Click = e => click(index, e);
                list.Add(row);
            }
            while (list.Count > count) {
                Destroy(list[list.Count - 1].Bg.gameObject);
                list.RemoveAt(list.Count - 1);
            }
        }

        // ---------- construction ----------

        // Own screen-space overlay: the game's static PC canvas is hidden in gamepad mode.
        private static PadQuickMenu Build() {
            var font = PadTheme.Font;

            var overlay = new GameObject("BI2TL_PadOverlay", typeof(RectTransform));
            DontDestroyOnLoad(overlay);
            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            ModBar.BarLayout.ThroughGameCamera(canvas);
            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            // Clicks land on the menu instead of the world: the game skips the click when the pointer is over UI.
            overlay.AddComponent<GraphicRaycaster>();
            // The wheel over the menu scrolls it; Kingmaker does not zoom while the pointer is over something that scrolls.
            overlay.AddComponent<PadWheel>().Scroll = y => instance?.OnWheel(y);
            var blocker = new GameObject("BI2TL_PadClickBlocker", typeof(RectTransform));
            blocker.transform.SetParent(overlay.transform, false);
            var blockerRect = (RectTransform)blocker.transform;
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.sizeDelta = Vector2.zero;
            blocker.AddComponent<Image>().color = Color.clear;

            var root = new GameObject("BI2TL_PadQuickMenu", typeof(RectTransform));
            root.SetActive(false);
            root.transform.SetParent(overlay.transform, false);

            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0, (PadTheme.HintGap + PadTheme.HintRowHeight) / 2);
            rect.sizeDelta = new Vector2(WideWidth, 0);

            PadTheme.Panel(root.AddComponent<Image>());
            ConfigureVertical(root.AddComponent<VerticalLayoutGroup>(), PadTheme.Paper ? PadTheme.PanelPadding : new RectOffset(28, 28, 22, 22), 10);
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var menu = root.AddComponent<PadQuickMenu>();
            ModBar.KeyCapRoot.Attach(root);
            menu.builtForKeyboard = PadSettings.KeyboardMode;
            menu.overlay = overlay;
            menu.overlayCanvas = canvas;
            menu.panel = rect;
            menu.font = font;
            menu.titleText = MakeText(root.transform, font, PadTheme.Initial("pad.title".i8()), 30, FontStyles.Bold, TitleColor);
            menu.titleText.alignment = TextAlignmentOptions.Center;
            PadTheme.Separator(root.transform);

            // tabs, as in the other mod windows
            menu.tabRow = MakeHorizontal(root.transform, "Tabs", 16, TextAnchor.MiddleCenter);
            Clickable(MakeIcon(menu.tabRow.transform, font, RewiredActionType.LeftUp, 32), RewiredActionType.LeftUp);
            menu.tabsText = MakeText(menu.tabRow.transform, font, "", 22, FontStyles.Normal, TextColor);
            menu.tabsText.enableWordWrapping = false;
            AddPointer(menu.tabsText.gameObject, tint: false).Click = e => menu.ClickTabs(e);
            Clickable(MakeIcon(menu.tabRow.transform, font, RewiredActionType.RightUp, 32), RewiredActionType.RightUp);

            // main
            menu.mainRoot = MakeContainer(root.transform, "Main", 10);
            menu.mainRowHolder = MakeContainer(menu.mainRoot.transform, "Rows", 6).transform;
            menu.reportText = MakeText(menu.mainRoot.transform, font, "", 20, FontStyles.Normal, TextColor);
            menu.reportText.gameObject.SetActive(false);
            var mainHints = MakeHintBar(menu.mainRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.select"),
                (new[] { RewiredActionType.Confirm }, "pad.h.apply"),
                (new[] { RewiredActionType.Func01 }, "pad.h.all"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.close"));
            menu.mainConfirmLabel = mainHints[1].GetComponentsInChildren<TextMeshProUGUI>().Last();

            // groups
            menu.groupsRoot = MakeContainer(root.transform, "Groups", 8);
            menu.groupsRowHolder = MakeContainer(menu.groupsRoot.transform, "Rows", 5).transform;
            menu.groupsNote = MakeText(menu.groupsRoot.transform, font, "", 19, FontStyles.Normal, TitleColor);
            var groupHints = MakeHintBar(menu.groupsRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.select"),
                (new[] { RewiredActionType.Confirm }, "pad.h.open"),
                (new[] { RewiredActionType.Func02 }, "pad.h.rename"),
                (new[] { RewiredActionType.Func01 }, "pad.h.hide"),
                (new[] { RewiredActionType.RightStickButton }, "pad.h.delgroup"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.close"));
            menu.groupsDeleteHint = groupHints[4];
            if (!PadSettings.KeyboardMode)
                menu.deleteHoldFill = AddHoldRing(groupHints[4].transform.Find("Icon"));
            menu.groupsRoot.SetActive(false);

            // members
            menu.membersRoot = MakeContainer(root.transform, "Members", 5);
            var memberBox = MakeListBox(menu.membersRoot.transform, 5, out menu.memberBar);
            menu.memberBar.Moved = first => menu.ScrollMembers(Mathf.RoundToInt(first) - menu.memberTop);
            for (int r = 0; r < MemberRows; r++) {
                int at = r;
                var row = MakeListRow(memberBox, font, 20, withBox: true, withIcon: true);
                row.Pointer.Click = e => menu.ClickMember(at, e);
                row.Pointer.Live = () => menu.memberTop + at < menu.memberList.Count;
                menu.memberRows.Add(row);
            }
            menu.membersDetail = MakeText(menu.membersRoot.transform, font, "", 19, FontStyles.Normal, DimColor);
            menu.membersNote = MakeText(menu.membersRoot.transform, font, "", 19, FontStyles.Normal, TitleColor);
            MakeHintBar(menu.membersRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.buff"),
                (new[] { RewiredActionType.Confirm }, "pad.h.addremove"),
                (new[] { RewiredActionType.Func01 }, "pad.h.tick"),
                (new[] { RewiredActionType.Func02 }, "pad.h.fill"),
                (new[] { RewiredActionType.DPadRight }, "pad.h.targets"),
                (new[] { RewiredActionType.RightUp }, "pad.h.buffset"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.membersRoot.SetActive(false);

            // name
            menu.nameRoot = MakeContainer(root.transform, "Name", 10);
            menu.nameText = MakeText(menu.nameRoot.transform, font, "", 24, FontStyles.Normal, TextColor);
            var durationRow = MakeHorizontal(menu.nameRoot.transform, "Duration", 14, TextAnchor.MiddleLeft);
            bool keyboard = menu.builtForKeyboard;
            // Letters are typed on the name page, so the keyboard changes the duration with PageUp/PageDown.
            Clickable(keyboard
                ? MakeKeyLabel(durationRow.transform, font, "PgUp")
                : MakeIcon(durationRow.transform, font, RewiredActionType.LeftUp, 32), RewiredActionType.LeftUp);
            menu.nameDurationText = MakeText(durationRow.transform, font, "", 21, FontStyles.Normal, TextColor);
            menu.nameDurationText.enableWordWrapping = false;
            Clickable(keyboard
                ? MakeKeyLabel(durationRow.transform, font, "PgDn")
                : MakeIcon(durationRow.transform, font, RewiredActionType.RightUp, 32), RewiredActionType.RightUp);
            menu.keyHolder = MakeContainer(menu.nameRoot.transform, "Keyboard", 6).transform;
            menu.keyHolder.gameObject.SetActive(!keyboard);
            MakeText(menu.nameRoot.transform, font, T(keyboard ? "pad.name.note.kb" : "pad.name.note"), 18, FontStyles.Normal, DimColor);
            if (keyboard) {
                MakeKeyHintBar(menu.nameRoot.transform, font,
                    ("Enter", "pad.h.done", m => m.FinishName()),
                    ("Backspace", "pad.h.erase", m => m.Backspace()),
                    ("PgUp/PgDn", "pad.h.duration", m => m.CycleDuration(1)),
                    ("RMB", "pad.h.cancel", m => m.OnDecline()));
            } else {
                MakeHintBar(menu.nameRoot.transform, font,
                    (new[] { RewiredActionType.DPadVertical, RewiredActionType.DPadHorizontal }, "pad.h.key"),
                    (new[] { RewiredActionType.Confirm }, "pad.h.type"),
                    (new[] { RewiredActionType.Func01 }, "pad.h.erase"),
                    (new[] { RewiredActionType.Func02 }, "pad.h.done"),
                    (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.duration"),
                    (new[] { RewiredActionType.Decline }, "pad.h.cancel"));
            }
            menu.nameRoot.SetActive(false);

            // editor
            menu.editorRoot = MakeContainer(root.transform, "Editor", 6);
            var filterBox = new GameObject("Show", typeof(RectTransform));
            filterBox.transform.SetParent(menu.editorRoot.transform, false);
            ConfigureVertical(filterBox.AddComponent<VerticalLayoutGroup>(), new RectOffset(0, (int)ModBar.ModScrollbar.Width + 4, 0, 4), 0);
            menu.filterRow = MakeListRow(filterBox.transform, font, 21, withBox: false);
            menu.filterRow.Pointer.Click = e => menu.ClickFilter(e);
            menu.filterRow.Control = ModBar.ModControl.Create((RectTransform)menu.filterRow.Bg.transform, menu.filterRow.Text, OptionControlWidth, OptionControlHeight);
            menu.filterRow.Control.Step = d => menu.StepFilter(d);
            var editorBox = MakeListBox(menu.editorRoot.transform, 6, out menu.editorBar);
            menu.editorBar.Moved = first => menu.ScrollEditor(Mathf.RoundToInt(first) - menu.top);
            for (int r = 0; r < VisibleRows; r++) {
                int at = r;
                var row = MakeListRow(editorBox, font, 21, withBox: false, withIcon: true);
                row.Pointer.Click = _ => menu.ClickEditorRow(at);
                row.Pointer.Live = () => menu.top + at < menu.filtered.Count;
                menu.listRows.Add(row);
            }
            menu.detailText = MakeText(menu.editorRoot.transform, font, "", 19, FontStyles.Normal, DimColor);
            var chipHolder = new GameObject("Party", typeof(RectTransform));
            chipHolder.transform.SetParent(menu.editorRoot.transform, false);
            var chipLayout = chipHolder.AddComponent<HorizontalLayoutGroup>();
            chipLayout.spacing = 6;
            chipLayout.childControlWidth = true;
            chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = true;
            chipLayout.childForceExpandHeight = false;
            menu.chipHolder = chipHolder.transform;
            var editorHints = MakeHintBar(menu.editorRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.buff"),
                (new[] { RewiredActionType.DPadHorizontal }, "pad.h.char"),
                (new[] { RewiredActionType.Confirm }, "pad.h.target"),
                (new[] { RewiredActionType.Func02 }, "pad.h.party"),
                (new[] { RewiredActionType.Func01 }, "pad.h.auto"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.editorTabHint = editorHints[5];
            menu.editorBackLabel = editorHints[6].GetComponentsInChildren<TextMeshProUGUI>().Last();
            menu.editorRoot.SetActive(false);

            // help
            menu.helpRoot = MakeContainer(root.transform, "Help", 8);
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(menu.helpRoot.transform, false);
            viewport.AddComponent<RectMask2D>();
            var viewportSize = viewport.AddComponent<LayoutElement>();
            viewportSize.preferredHeight = HelpHeight;
            viewportSize.flexibleWidth = 1;
            menu.helpText = MakeText(viewport.transform, font, "", 20, FontStyles.Normal, TextColor);
            menu.helpText.lineSpacing = 6;
            var helpRect = (RectTransform)menu.helpText.transform;
            helpRect.anchorMin = new Vector2(0, 1);
            helpRect.anchorMax = new Vector2(1, 1);
            helpRect.pivot = new Vector2(0.5f, 1);
            helpRect.offsetMin = new Vector2(4, 0);
            helpRect.offsetMax = new Vector2(-ModBar.ModScrollbar.Width - 6, 0);
            menu.helpText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            menu.helpBar = ModBar.ModScrollbar.Create((RectTransform)viewport.transform, PadTheme.FromBundles);
            menu.helpBar.Moved = first => menu.ScrollHelp(first - menu.helpOffset);
            MakeHintBar(menu.helpRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.scroll"),
                (new[] { RewiredActionType.DPadHorizontal }, "pad.h.section"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.close"));
            menu.helpRoot.SetActive(false);

            // options
            menu.optionsRoot = MakeContainer(root.transform, "Options", 5);
            var optionBox = MakeListBox(menu.optionsRoot.transform, 5, out menu.optionBar);
            menu.optionBar.Moved = first => menu.ScrollOptions(Mathf.RoundToInt(first) - menu.optTop);
            for (int r = 0; r < OptionRows; r++) {
                int at = r;
                var row = MakeListRow(optionBox, font, 20, withBox: false);
                row.Pointer.Click = e => menu.ClickOption(at, e);
                row.Pointer.Live = () => menu.optTop + at < menu.opts.Count && !menu.opts[menu.optTop + at].Header;
                row.Control = ModBar.ModControl.Create((RectTransform)row.Bg.transform, row.Text, OptionControlWidth, OptionControlHeight);
                row.Control.Step = d => menu.StepOption(at, d);
                menu.optRows.Add(row);
            }
            menu.optDetail = MakeText(menu.optionsRoot.transform, font, "", 18, FontStyles.Normal, DimColor);
            var optionLayout = menu.optDetail.gameObject.AddComponent<LayoutElement>();
            optionLayout.minHeight = 48;
            var optionHints = MakeHintBar(menu.optionsRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.select"),
                (new[] { RewiredActionType.Confirm }, "pad.h.change"),
                (new[] { RewiredActionType.DPadHorizontal }, "pad.h.value"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.optionsTabHint = optionHints[3];
            menu.optionsBackLabel = optionHints[4].GetComponentsInChildren<TextMeshProUGUI>().Last();
            menu.optionsRoot.SetActive(false);

            menu.BuildConfirm(root.transform);
            return menu;
        }

        // The confirmation over the window: a shade over the whole sheet that takes the clicks, a small sheet in the
        // middle with the question and the A and B buttons.
        private void BuildConfirm(Transform panelRoot) {
            confirmRoot = new GameObject("Confirm", typeof(RectTransform));
            confirmRoot.transform.SetParent(panelRoot, false);
            confirmRoot.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch((RectTransform)confirmRoot.transform, 0);
            var shade = confirmRoot.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.55f);
            // A right click anywhere is B, as everywhere in the menu; clicks elsewhere do nothing.
            AddPointer(confirmRoot, tint: false);

            var sheet = new GameObject("Sheet", typeof(RectTransform));
            sheet.transform.SetParent(confirmRoot.transform, false);
            var rect = (RectTransform)sheet.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(640, 0);
            PadTheme.Panel(sheet.AddComponent<Image>());
            ConfigureVertical(sheet.AddComponent<VerticalLayoutGroup>(), PadTheme.Paper ? PadTheme.PanelPadding : new RectOffset(28, 28, 22, 22), 18);
            sheet.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            confirmText = MakeText(sheet.transform, font, "", 24, FontStyles.Normal, TextColor);
            confirmText.alignment = TextAlignmentOptions.Center;
            var buttons = MakeHorizontal(sheet.transform, "Buttons", 40, TextAnchor.MiddleCenter);
            foreach (var (button, key) in new[] { (RewiredActionType.Confirm, "pad.h.delete"), (RewiredActionType.Decline, "pad.h.cancel") }) {
                var item = MakeHorizontal(buttons.transform, "Button", 8, TextAnchor.MiddleCenter);
                MakeIcon(item.transform, font, button, PadTheme.HintIconSize);
                var label = MakeText(item.transform, font, key.i8(), 22, FontStyles.Bold, TitleColor);
                label.enableWordWrapping = false;
                Clickable(item, button, label);
            }
            confirmRoot.SetActive(false);
        }

        // A press to hold: a faint ring around the button's icon and a bright one that fills clockwise while it is held,
        // as the game marks its hold hints.
        private static Image AddHoldRing(Transform icon) {
            if (icon == null)
                return null;
            Image Ring(string name, Color color) {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(icon, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                Stretch((RectTransform)go.transform, -4);
                var image = go.AddComponent<Image>();
                image.sprite = RingSprite;
                image.color = color;
                image.raycastTarget = false;
                return image;
            }
            var hint = PadTheme.HintLabel;
            Ring("HoldRing", new Color(hint.r, hint.g, hint.b, 0.45f));
            var fill = Ring("HoldFill", PadTheme.HintHover);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            return fill;
        }

        private static Sprite ringSprite;

        // A white ring with soft edges, tinted by its image.
        private static Sprite RingSprite {
            get {
                if (ringSprite != null)
                    return ringSprite;
                const int n = 64;
                const float outer = 31f, width = 4f;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[n * n];
                for (int y = 0; y < n; y++) {
                    for (int x = 0; x < n; x++) {
                        float d = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                        float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - (outer - width) + 0.5f);
                        pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return ringSprite;
            }
        }

        // Button hints the way the game draws them under its Formation window: a row under the panel, each hint the
        // console icon of its button and a label (PadTheme.Hint*). The row belongs to its page and shows with it.
        private static List<GameObject> MakeHintBar(Transform parent, TMP_FontAsset font, params (RewiredActionType[] buttons, string labelKey)[] items) {
            var made = new List<GameObject>();
            var bar = MakeHintRow(parent);
            foreach (var (buttons, labelKey) in items) {
                var item = MakeHint(bar);
                var icons = buttons.Select(button => MakeIcon(item.transform, font, button, PadTheme.HintIconSize, PadTheme.HintLabel)).ToList();
                var label = MakeHintLabel(item.transform, font, labelKey);
                // One button: the whole hint is pressed; a pair (LB/RB): each icon on its own.
                if (buttons.Length == 1)
                    Clickable(item, buttons[0], label, PadTheme.HintHover);
                else
                    for (int i = 0; i < buttons.Length; i++)
                        Clickable(icons[i], buttons[i], hover: PadTheme.HintHover);
                made.Add(item);
            }
            return made;
        }

        // The row sits under the panel, outside its layout, and is switched with the page it was made for.
        private static Transform MakeHintRow(Transform page) {
            var panel = page.parent;
            var row = new GameObject("Hints", typeof(RectTransform));
            row.transform.SetParent(panel, false);
            row.AddComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -PadTheme.HintGap);
            rect.sizeDelta = new Vector2(0, PadTheme.HintRowHeight);
            ModBar.AbovePortraits.Attach((RectTransform)panel, rect, PadTheme.HintGap);
            if (PadTheme.HintPlate)
                PadTheme.HintBackground(row.AddComponent<Image>());
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = PadTheme.HintPadding;
            layout.spacing = PadTheme.HintSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            row.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            page.gameObject.AddComponent<PadPageHints>().Row = row;
            // Off until its page is enabled: the pages are built before the menu is first shown.
            row.SetActive(false);
            return row.transform;
        }

        private static GameObject MakeHint(Transform row) {
            var item = MakeHorizontal(row, "Hint", 6, TextAnchor.MiddleLeft);
            if (!PadTheme.HintPlate) {
                // WotR: a dark strip behind every hint, longer than the hint on both sides, as the game draws it.
                ((HorizontalLayoutGroup)item.GetComponent<LayoutGroup>()).padding = new RectOffset(4, 14, 0, 0);
                var strip = new GameObject("Background", typeof(RectTransform));
                strip.transform.SetParent(item.transform, false);
                strip.AddComponent<LayoutElement>().ignoreLayout = true;
                var rect = (RectTransform)strip.transform;
                rect.anchorMin = new Vector2(0, 0.5f);
                rect.anchorMax = new Vector2(1, 0.5f);
                rect.sizeDelta = new Vector2(24, PadTheme.HintSpriteHeight);
                rect.anchoredPosition = new Vector2(2, 0);
                PadTheme.HintBackground(strip.AddComponent<Image>());
            }
            return item;
        }

        private static TextMeshProUGUI MakeHintLabel(Transform item, TMP_FontAsset font, string labelKey) {
            var label = MakeText(item, font, labelKey.i8(), PadTheme.HintLabelSize, PadTheme.HintLabelStyle, PadTheme.HintLabel);
            label.enableWordWrapping = false;
            return label;
        }

        private static GameObject MakeHorizontal(Transform parent, string name, float spacing, TextAnchor alignment) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return go;
        }

        // Falls back to a text label when the game has no icon for the button.
        private static GameObject MakeIcon(Transform parent, TMP_FontAsset font, RewiredActionType button, float size, Color? color = null) {
            if (PadSettings.KeyboardMode)
                return MakeKeyLabel(parent, font, PadKeys.Label(button), color);
            Sprite sprite = null;
            try {
                sprite = GamePadIcons.Instance?.GetIcon(button);
            } catch (Exception) { }
            if (sprite == null) {
                var text = MakeText(parent, font, $"[{IconFallback(button)}]", 19, FontStyles.Bold, color ?? TitleColor);
                text.enableWordWrapping = false;
                return text.gameObject;
            }
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = size;
            element.preferredHeight = size;
            return go;
        }

        private static GameObject MakeKeyLabel(Transform parent, TMP_FontAsset font, string key, Color? color = null) =>
            ModBar.KeyCaps.Create(parent, key, KeyCapHeight, font);

        private const float KeyCapHeight = 32f;

        private static void MakeKeyHintBar(Transform parent, TMP_FontAsset font, params (string key, string labelKey, Action<PadQuickMenu> act)[] items) {
            var bar = MakeHintRow(parent);
            foreach (var (key, labelKey, act) in items) {
                var item = MakeHint(bar);
                MakeKeyLabel(item.transform, font, key, PadTheme.HintLabel);
                var label = MakeHintLabel(item.transform, font, labelKey);
                var pointer = AddPointer(item, tint: false);
                pointer.Mark(label, PadTheme.HintHover);
                pointer.Click = _ => {
                    if (instance != null)
                        act(instance);
                };
            }
        }

        private static string IconFallback(RewiredActionType button) => button switch {
            RewiredActionType.Confirm => "A",
            RewiredActionType.Decline => "B",
            RewiredActionType.Func01 => "X",
            RewiredActionType.Func02 => "Y",
            RewiredActionType.LeftUp => "LB",
            RewiredActionType.RightUp => "RB",
            RewiredActionType.DPadVertical => "D-pad",
            RewiredActionType.DPadHorizontal => "D-pad",
            RewiredActionType.DPadRight => "D-pad",
            RewiredActionType.RightStickButton => "R3",
            _ => button.ToString()
        };

        private static void ConfigureVertical(VerticalLayoutGroup layout, RectOffset padding, float spacing) {
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        // The rows of a scrolled list in their own column, the game's scrollbar at its right.
        private static Transform MakeListBox(Transform parent, float spacing, out ModBar.ModScrollbar bar) {
            var go = new GameObject("Rows", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ConfigureVertical(go.AddComponent<VerticalLayoutGroup>(), new RectOffset(0, (int)ModBar.ModScrollbar.Width + 4, 0, 0), spacing);
            bar = ModBar.ModScrollbar.Create((RectTransform)go.transform, PadTheme.FromBundles);
            return go.transform;
        }

        private static GameObject MakeContainer(Transform parent, string name, float spacing) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ConfigureVertical(go.AddComponent<VerticalLayoutGroup>(), new RectOffset(0, 0, 0, 0), spacing);
            return go;
        }

        // A row: background, optional tick box (outline, dark inside, green tick), one line of text.
        private static ListRow MakeListRow(Transform parent, TMP_FontAsset font, float size, bool withBox, bool withIcon = false) {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = RowColor;
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 6, 6);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var row = new ListRow { Bg = bg, Pointer = AddPointer(go, tint: true) };
            if (withBox) {
                var holder = new GameObject("BoxSlot", typeof(RectTransform));
                holder.transform.SetParent(go.transform, false);
                var slot = holder.AddComponent<LayoutElement>();
                slot.preferredWidth = 24;
                slot.preferredHeight = 24;

                var box = new GameObject("Box", typeof(RectTransform));
                box.transform.SetParent(holder.transform, false);
                Stretch((RectTransform)box.transform, 0);
                box.AddComponent<Image>().color = BoxColor;
                var inner = new GameObject("Inner", typeof(RectTransform));
                inner.transform.SetParent(box.transform, false);
                Stretch((RectTransform)inner.transform, 2);
                inner.AddComponent<Image>().color = BoxInnerColor;
                var tick = new GameObject("Tick", typeof(RectTransform));
                tick.transform.SetParent(box.transform, false);
                Stretch((RectTransform)tick.transform, 5);
                row.Tick = tick.AddComponent<Image>();
                row.Tick.color = TickColor;
                row.Box = box;
            }

            if (withIcon) {
                var slot = new GameObject("IconSlot", typeof(RectTransform));
                slot.transform.SetParent(go.transform, false);
                var element = slot.AddComponent<LayoutElement>();
                element.preferredWidth = element.minWidth = IconSize;
                element.preferredHeight = element.minHeight = IconSize;
                var icon = new GameObject("Icon", typeof(RectTransform));
                icon.transform.SetParent(slot.transform, false);
                Stretch((RectTransform)icon.transform, 0);
                row.Icon = icon.AddComponent<Image>();
                row.Icon.preserveAspect = true;
                row.Icon.raycastTarget = false;
                row.Icon.enabled = false;
            }

            row.Text = MakeText(go.transform, font, "", size, FontStyles.Normal, TextColor);
            row.Text.enableWordWrapping = false;
            row.Text.overflowMode = TextOverflowModes.Ellipsis;
            var textElement = row.Text.gameObject.AddComponent<LayoutElement>();
            textElement.flexibleWidth = 1;
            return row;
        }

        private static PadPointer AddPointer(GameObject go, bool tint) {
            var pointer = go.AddComponent<PadPointer>();
            if (tint) {
                var hover = new GameObject("Hover", typeof(RectTransform));
                hover.transform.SetParent(go.transform, false);
                hover.transform.SetAsFirstSibling();
                hover.AddComponent<LayoutElement>().ignoreLayout = true;
                Stretch((RectTransform)hover.transform, 0);
                var image = hover.AddComponent<Image>();
                image.color = PadTheme.RowHover;
                image.raycastTarget = false;
                image.enabled = false;
                pointer.Tint = image;
            }
            return pointer;
        }

        private static readonly Color IconHover = new Color(1f, 0.82f, 0.5f);

        // Hints of buttons that only point a direction (select, scroll) are not pressed.
        private static void Clickable(GameObject go, RewiredActionType button, Graphic label = null, Color? hover = null) {
            if (button != RewiredActionType.Confirm && button != RewiredActionType.Decline && button != RewiredActionType.Func01
                && button != RewiredActionType.Func02 && button != RewiredActionType.LeftUp && button != RewiredActionType.RightUp
                && button != RewiredActionType.DPadRight && button != RewiredActionType.RightStickButton)
                return;
            var pointer = AddPointer(go, tint: false);
            var mark = label != null ? label : go.GetComponent<Graphic>();
            if (mark != null)
                pointer.Mark(mark, mark is Image ? IconHover : hover ?? (mark.color == TitleColor ? TextColor : TitleColor));
            pointer.Click = _ => instance?.Press(button);
        }

        private static void Stretch(RectTransform rect, float inset) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static TextMeshProUGUI MakeText(Transform parent, TMP_FontAsset font, string value, float size, FontStyles style, Color color) {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.richText = true;
            text.enableWordWrapping = true;
            if (PadHelp.IconAsset != null)
                text.spriteAsset = PadHelp.IconAsset;
            text.text = value;
            return text;
        }
    }

    // A part of the menu the mouse works on. The hover tint is an image of its own over the part's colour, so the menu
    // redraws its rows without knowing where the pointer is.
    internal class PadPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
        public Action<PointerEventData> Click;
        // False on an empty row: no tint, and clicks do nothing.
        public Func<bool> Live;
        public Image Tint;
        private Graphic mark;
        private Color markColor;
        private Color markHover;
        private bool over;

        public static PadPointer Over { get; private set; }

        // Rows light up under the pointer only once the real mouse moved while the menu is open: a cursor resting
        // over the menu (the Steam Deck's trackpad mouse) would otherwise show a second selection apart from the menu's own.
        public static bool MouseUsed { get; private set; }
        private static Vector3 mouseAt;

        public static void ResetMouse() {
            MouseUsed = false;
            mouseAt = Input.mousePosition;
        }

        public static void WatchMouse() {
            if (MouseUsed || (Input.mousePosition - mouseAt).sqrMagnitude <= 4f)
                return;
            MouseUsed = true;
            Over?.Refresh();
        }

        public void Mark(Graphic graphic, Color hover) {
            mark = graphic;
            markColor = graphic.color;
            markHover = hover;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            over = true;
            Over = this;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData) {
            over = false;
            if (Over == this)
                Over = null;
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left || (Live != null && !Live()))
                return;
            try {
                Click?.Invoke(eventData);
            } catch (Exception ex) {
                Main.Error(ex, "PadPointer.Click");
            }
            // The row under the pointer may now show something else, or nothing.
            if (Over != null)
                Over.Refresh();
        }

        // Rows show other items after a scroll, so the tint is checked again.
        public void Refresh() {
            bool lit = over && MouseUsed && (Live == null || Live());
            if (Tint != null)
                Tint.enabled = lit;
            if (mark != null)
                mark.color = lit ? markHover : markColor;
        }

        private void OnDisable() {
            over = false;
            if (Over == this)
                Over = null;
            Refresh();
        }
    }

    // The wheel anywhere over the menu's screen.
    // Shows the hint row of a page while the page is shown; the row lives outside the page, under the panel.
    internal class PadPageHints : MonoBehaviour {
        public GameObject Row;
        private void OnEnable() {
            if (Row != null)
                Row.SetActive(true);
        }
        private void OnDisable() {
            if (Row != null)
                Row.SetActive(false);
        }
    }

    internal class PadWheel : MonoBehaviour, IScrollHandler {
        public Action<float> Scroll;

        public void OnScroll(PointerEventData eventData) => Scroll?.Invoke(eventData.scrollDelta.y);
    }
}
