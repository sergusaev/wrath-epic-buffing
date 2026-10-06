using BuffIt2TheLimit.Config;
using BuffIt2TheLimit.Extensions;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
#if KINGMAKER
using Kingmaker.Assets.Console.GamepadInput;
using Kingmaker.Blueprints.Console;
#else
using Owlcat.Runtime.UI.ConsoleTools.GamepadInput;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // Gamepad-mode buff menu.
    // Main: per-group status, apply one group or all groups in sequence, combat toggle, cast report.
    // Groups: list of groups, create / rename / hide / delete.
    // Members: buffs of one group, add and remove, on/off tick, auto-fill by duration.
    // Name: on-screen keyboard for a group name and its duration.
    // Editor: targets per party member for each buff.
    // Buttons go through the game's console input layer (the world and HUD are blocked while
    // the menu is open); directions are polled from the Rewired player for hold-to-repeat.
    internal class PadQuickMenu : MonoBehaviour {

        private enum Page { Main, Groups, Members, Name, Editor, Help }

        private static PadQuickMenu instance;

        private static readonly Color PanelColor = new(0.06f, 0.05f, 0.04f, 0.94f);
        private static readonly Color RowColor = new(1f, 1f, 1f, 0.04f);
        private static readonly Color RowOutsideColor = new(1f, 1f, 1f, 0.015f);
        private static readonly Color RowSelectedColor = new(0.62f, 0.47f, 0.2f, 0.55f);
        private static readonly Color ChipColor = new(1f, 1f, 1f, 0.06f);
        private static readonly Color ChipWantedColor = new(0.3f, 0.55f, 0.28f, 0.75f);
        private static readonly Color ChipCursorColor = new(0.85f, 0.66f, 0.28f, 0.9f);
        private static readonly Color ChipCursorWantedColor = new(0.55f, 0.75f, 0.3f, 0.95f);
        private static readonly Color BoxColor = new(0.85f, 0.8f, 0.7f, 0.85f);
        private static readonly Color BoxInnerColor = new(0.08f, 0.07f, 0.06f, 1f);
        private static readonly Color TickColor = new(0.5f, 0.82f, 0.4f, 1f);
        private static readonly Color KeyColor = new(1f, 1f, 1f, 0.07f);
        private static readonly Color TitleColor = new(0.93f, 0.8f, 0.5f);
        private static readonly Color TextColor = new(0.92f, 0.9f, 0.85f);
        private static readonly Color DimColor = new(0.65f, 0.62f, 0.56f);

        private const float MainWidth = 1000f;
        private const float WideWidth = 1180f;
        private const int VisibleRows = 10;
        private const int MemberRows = 12;
        private const float StickThreshold = 0.5f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.08f;
        private const float RoutineTimeout = 30f;
        private const float DeleteConfirmTime = 3f;

        private GameObject overlay;
        private RectTransform panel;
        private TextMeshProUGUI titleText;
        private TMP_FontAsset font;
        private IDisposable layerHandle;
        private Page page;
        private float nextRefresh;
        private bool recalcTried;

        // main
        private GameObject mainRoot;
        private Transform mainRowHolder;
        private readonly List<ListRow> rows = new();
        private List<BuffGroup> mainGroups = new();
        private TextMeshProUGUI reportText;
        private int selected;

        // groups
        private GameObject groupsRoot;
        private Transform groupsRowHolder;
        private readonly List<ListRow> groupRows = new();
        private List<BuffGroup> groupList = new();
        private TextMeshProUGUI groupsNote;
        private int groupsCursor;
        private BuffGroup? deleteArmed;
        private GameObject groupsDeleteHint;
        private float deleteArmedUntil;

        // members
        private GameObject membersRoot;
        private TextMeshProUGUI membersTabText;
        private readonly List<ListRow> memberRows = new();
        private TextMeshProUGUI membersDetail;
        private TextMeshProUGUI membersNote;
        private BuffGroup memberGroup;
        private List<BubbleBuff> memberList = new();
        private int memberCount;
        private int memberCursor;
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
        private TextMeshProUGUI tabText;
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
        private RectTransform helpThumb;
        private float helpOffset;
        private readonly List<int> helpSections = new();
        private List<float> helpSectionOffsets;
        private const float HelpHeight = 720f;
        private const float HelpStep = 46f;

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
        private int GroupsRow => mainGroups.Count + 1;
        private int EditorRow => mainGroups.Count + 2;
        private int HelpRow => mainGroups.Count + 3;
        private int MainRowCount => mainGroups.Count + 4;

        private static BufferState State => GlobalBubbleBuffer.Instance?.SpellbookController?.state;

        public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

        private class ListRow {
            public Image Bg;
            public GameObject Box;
            public Image Tick;
            public TextMeshProUGUI Text;
        }

        public static void Toggle() {
            Main.Log($"[PAD] toggle, open={IsOpen}");
            try {
                if (IsOpen)
                    instance.Close();
                else
                    Open();
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.Toggle");
            }
        }

        private static void Open() {
            if (instance == null)
                instance = Build();
            instance.recalcTried = false;
            instance.overlay.SetActive(true);
            instance.gameObject.SetActive(true);
            instance.PushInput();
            instance.ShowPage(Page.Main);
            instance.ShowReport();
            Main.Log($"[PAD] opened, input layer={(instance.layerHandle != null)}");
        }

        private void Close() {
            PopInput();
            gameObject.SetActive(false);
            overlay.SetActive(false);
        }

        private void Awake() {
            BuffExecutor.RoutineFinished += OnRoutineFinished;
        }

        private void OnDisable() {
            PopInput();
        }

        private void OnDestroy() {
            PopInput();
            BuffExecutor.RoutineFinished -= OnRoutineFinished;
            if (instance == this)
                instance = null;
            if (overlay != null)
                Destroy(overlay);
        }

        private void Update() {
            try {
                PollButtons();
                PollDirections();
                if (page == Page.Name) {
                    PollTyping();
                } else {
                    if (Input.GetKeyDown(KeyCode.Return)) OnConfirm();
                    if (Input.GetKeyDown(KeyCode.Backspace)) OnDecline();
                }

                if (waiting && Time.unscaledTime > waitDeadline) {
                    Main.Log($"[PAD] no result for {current} after {RoutineTimeout}s");
                    waiting = false;
                    ReplaceLast(string.Format("pad.timeout".i8(), PadGroups.Name(current)));
                    RunNext();
                }

                if (deleteArmed != null && Time.unscaledTime > deleteArmedUntil) {
                    deleteArmed = null;
                    if (page == Page.Groups)
                        RenderGroups();
                }

                if (page == Page.Main && Time.unscaledTime >= nextRefresh)
                    RefreshMain();
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.Update");
            }
        }

        // ---------- input ----------

        private void PushInput() {
            if (layerHandle != null)
                return;
#if KINGMAKER
            // In mouse mode the game has no console input layers; buttons are polled instead.
            if (!Game.Instance.IsControllerGamepad)
                return;
#endif
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
            layer.AddAxis2D((_, v) => MarkStick(v), (int)RewiredActionType.LeftStickX, (int)RewiredActionType.LeftStickY, false);
            layerHandle = pad.PushLayer(layer);
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
            int frame = Time.frameCount;
            if (firedFrame.TryGetValue(action, out var last) && frame - last <= 2)
                return;
            firedFrame[action] = frame;
            if (padLogLines < 200) {
                padLogLines++;
                Main.Log($"[PAD] {action} via {source}, page={page}");
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
                Main.Log($"[PAD] input source: {what}");
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
            if (v == 0) v = Input.GetKey(KeyCode.UpArrow) ? -1 : Input.GetKey(KeyCode.DownArrow) ? 1 : 0;
            if (h == 0) h = Input.GetKey(KeyCode.LeftArrow) ? -1 : Input.GetKey(KeyCode.RightArrow) ? 1 : 0;

            // A direction still held from the previous page does nothing until it is released.
            if (holdDirections) {
                if (v != 0 || h != 0)
                    return;
                holdDirections = false;
            }

            int dv = vertical.Step(v);
            int dh = horizontal.Step(h);
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
                    else if (selected == GroupsRow)
                        ShowPage(Page.Groups);
                    else if (selected == EditorRow)
                        OpenEditor(Page.Main, null);
                    else if (selected == HelpRow)
                        ShowPage(Page.Help);
                    break;
                case Page.Groups:
                    if (groupsCursor < groupList.Count)
                        OpenMembers(groupList[groupsCursor], Page.Groups);
                    else
                        OpenName(null);
                    break;
                case Page.Members: ToggleMembership(); break;
                case Page.Name: PressKey(); break;
                case Page.Editor: ToggleTarget(); break;
            }
        }

        private void OnDecline() {
            switch (page) {
                case Page.Main: Close(); break;
                case Page.Members: ShowPage(membersBack); break;
                case Page.Name: ShowPage(Page.Groups); break;
                case Page.Editor:
                    if (editorBack == Page.Members)
                        OpenMembers(memberGroup, membersBack, SelectedBuff);
                    else
                        ShowPage(editorBack);
                    break;
                default: ShowPage(Page.Main); break;
            }
        }

        private void OnFuncX() {
            switch (page) {
                case Page.Main: Enqueue(mainGroups); break;
                case Page.Groups: ToggleHidden(); break;
                case Page.Members: ToggleTick(); break;
                case Page.Name: Backspace(); break;
                case Page.Editor: AutoTargets(); break;
            }
        }

        private void OnFuncY() {
            switch (page) {
                case Page.Main:
                    if (selected < mainGroups.Count)
                        OpenMembers(mainGroups[selected], Page.Main);
                    else
                        ShowPage(Page.Groups);
                    break;
                case Page.Groups:
                    if (groupsCursor < groupList.Count)
                        OpenName(groupList[groupsCursor]);
                    break;
                case Page.Members: AutoFill(); break;
                case Page.Name: FinishName(); break;
                case Page.Editor: ToggleWholeParty(); break;
            }
        }

        private void OnShoulder(int delta) {
            switch (page) {
                case Page.Groups:
                    if (delta > 0) DeleteGroup();
                    break;
                case Page.Members: SwitchMemberGroup(delta); break;
                case Page.Name: CycleDuration(delta); break;
                case Page.Editor: SwitchTab(delta); break;
                case Page.Help: JumpSection(delta); break;
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
                    if (filtered.Count == 0)
                        return;
                    cursor = Mathf.Clamp(cursor + delta, 0, filtered.Count - 1);
                    RenderEditor();
                    break;
                case Page.Help: ScrollHelp(delta * HelpStep); break;
            }
        }

        private void MoveHorizontal(int delta) {
            switch (page) {
                case Page.Members:
                    if (delta > 0 && SelectedMember != null)
                        OpenEditor(Page.Members, SelectedMember);
                    break;
                case Page.Name:
                    keyCol = Mathf.Clamp(keyCol + delta, 0, keys[keyRow].Count - 1);
                    RenderKeys();
                    break;
                case Page.Editor: MovePartyCursor(delta); break;
            }
        }

        // ---------- pages ----------

        private void ShowPage(Page next) {
            page = next;
            holdDirections = true;
            mainRoot.SetActive(page == Page.Main);
            groupsRoot.SetActive(page == Page.Groups);
            membersRoot.SetActive(page == Page.Members);
            nameRoot.SetActive(page == Page.Name);
            editorRoot.SetActive(page == Page.Editor);
            helpRoot.SetActive(page == Page.Help);
            panel.sizeDelta = new Vector2(page == Page.Main || page == Page.Groups ? MainWidth : WideWidth, 0);
            titleText.text = page switch {
                Page.Main => "pad.title".i8(),
                Page.Groups => "pad.groups.title".i8(),
                Page.Members => "pad.members.title".i8(),
                Page.Name => nameTarget == null ? "pad.name.title.new".i8() : "pad.name.title.rename".i8(),
                Page.Editor => "pad.editor.title".i8(),
                _ => "pad.help.title".i8()
            };
            EnsureBuffList();
            switch (page) {
                case Page.Main: RefreshMain(); break;
                case Page.Groups:
                    groupsNote.text = "";
                    deleteArmed = null;
                    RenderGroups();
                    break;
                case Page.Members: RebuildMembers(null); break;
                case Page.Name: RenderName(); break;
                case Page.Editor: RebuildFilter(keepBuff: null); break;
                case Page.Help:
                    OpenHelp();
                    break;
            }
        }

        private void EnsureBuffList() {
            var state = State;
            // In gamepad mode the spellbook is never opened, so the buff list is built here.
            if (state != null && state.BuffList == null && !recalcTried) {
                recalcTried = true;
                state.Recalculate(false);
                Main.Log($"[PAD] buff list built: {state.BuffList?.Count() ?? -1} buffs, party {Bubble.Group.Count}");
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
#if !KINGMAKER
            // Same refresh the mod runs on combat state changes: re-enables/disables HUD buttons.
            new HideBubbleButtonsWatcher().HandlePartyCombatStateChanged(Game.Instance.Player.IsInCombat);
#endif
            RefreshMain();
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
                report.Add($"   <color=#E08A7A>{bad.buff.Name}</color> {reasons}");
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
        }

        private void RefreshMain() {
            nextRefresh = Time.unscaledTime + 1f;
            try {
                mainGroups = PadGroups.Visible();
                EnsureRows(rows, mainRowHolder, MainRowCount, 22, withBox: false);
                selected = Mathf.Clamp(selected, 0, MainRowCount - 1);
                UpdateMainSelection();

                var state = State;
                rows[CombatRow].Text.text = CombatRowText(state);
                rows[GroupsRow].Text.text = Line("pad.groups.row".i8(), "pad.groups.row.desc".i8());
                rows[EditorRow].Text.text = Line("pad.editor.row".i8(), "pad.editor.row.desc".i8());
                rows[HelpRow].Text.text = Line("pad.help.row".i8(), "pad.help.row.desc".i8());
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
                        var color = active < wanted ? "#E8C46A" : "#9AD08A";
                        status = $"<color={color}>{string.Format("pad.status".i8(), buffs, active, wanted)}</color>";
                        if (soonest.HasValue)
                            status += " · " + string.Format("pad.left".i8(), FormatTime(soonest.Value));
                    }
                    if (off > 0)
                        status += $" <color=#8A847A>· {string.Format("pad.status.off".i8(), off)}</color>";
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
                    ? $"<color=#9AD08A>{"pad.combat.on".i8()}</color>"
                    : $"<color=#B0A898>{"pad.combat.off".i8()}</color>";
            return Line("pad.combat".i8(), value);
        }

        // ---------- groups page ----------

        private int GroupsRowCount => groupList.Count + (PadGroups.CanCreate ? 1 : 0);

        private void RenderGroups() {
            groupList = PadGroups.All();
            EnsureRows(groupRows, groupsRowHolder, GroupsRowCount, 21, withBox: false);
            groupsCursor = Mathf.Clamp(groupsCursor, 0, GroupsRowCount - 1);
            // Only custom groups can be deleted, so RB is offered only on them.
            if (groupsDeleteHint != null)
                groupsDeleteHint.SetActive(groupsCursor < groupList.Count && PadGroups.IsCustom(groupList[groupsCursor]));
            var state = State;
            for (int i = 0; i < groupRows.Count; i++) {
                var row = groupRows[i];
                row.Bg.color = i == groupsCursor ? RowSelectedColor : RowColor;
                if (i >= groupList.Count) {
                    row.Text.text = $"<color=#9AD08A><b>+ {"pad.groups.new".i8()}</b></color>";
                    continue;
                }
                var group = groupList[i];
                int members = 0, on = 0;
                if (state?.BuffList != null) {
                    foreach (var buff in state.BuffList) {
                        if (!PadGroups.IsMember(buff, group))
                            continue;
                        members++;
                        if (buff.ActiveIn(group))
                            on++;
                    }
                }
                string visibility = PadGroups.IsHidden(group)
                    ? $"<color=#8A847A>{"pad.groups.hidden".i8()}</color>"
                    : "pad.groups.shown".i8();
                string kind = PadGroups.IsCustom(group) ? "" : $" <color=#8A847A>{"pad.groups.builtin".i8()}</color>";
                string name = deleteArmed == group
                    ? $"<color=#E08A7A>{T("pad.groups.confirm")}</color>"
                    : $"<b>{Trim(PadGroups.Name(group), 30)}</b>{kind}";
                row.Text.text = $"{name}<pos=42%><size=85%>{PadGroups.DurationName(PadGroups.DurationOf(group))}</size>"
                    + $"<pos=68%><size=85%>{string.Format("pad.groups.count".i8(), members, on)}</size><pos=87%><size=85%>{visibility}</size>";
            }
        }

        private void ToggleHidden() {
            if (groupsCursor >= groupList.Count)
                return;
            var group = groupList[groupsCursor];
            PadGroups.SetHidden(group, !PadGroups.IsHidden(group));
            RenderGroups();
        }

        // RB once arms the deletion, a second RB within a few seconds deletes.
        private void DeleteGroup() {
            if (groupsCursor >= groupList.Count)
                return;
            var group = groupList[groupsCursor];
            if (!PadGroups.IsCustom(group)) {
                groupsNote.text = T("pad.groups.nodelete");
                return;
            }
            if (deleteArmed != group) {
                deleteArmed = group;
                deleteArmedUntil = Time.unscaledTime + DeleteConfirmTime;
                RenderGroups();
                return;
            }
            string name = PadGroups.Name(group);
            deleteArmed = null;
            PadGroups.Delete(group);
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

        private void SwitchMemberGroup(int delta) {
            var all = PadGroups.All();
            int idx = all.IndexOf(memberGroup);
            memberGroup = all[(idx + delta + all.Count) % all.Count];
            membersNote.text = "";
            RebuildMembers(null);
        }

        // Members first, then the rest: buffs of the group's duration before the others.
        private void RebuildMembers(BubbleBuff keep) {
            Bubble.RefreshGroup();
            var state = State;
            var duration = PadGroups.DurationOf(memberGroup);
            var buffs = state?.BuffList == null
                ? new List<BubbleBuff>()
                : state.BuffList.Where(b => !b.HideBecause(HideReason.Blacklisted) && b.CasterQueue.Count > 0).ToList();
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
            var all = PadGroups.All();
            int idx = all.IndexOf(memberGroup);
            string prev = all.Count > 1 ? PadGroups.Name(all[(idx - 1 + all.Count) % all.Count]) : "";
            string next = all.Count > 1 ? PadGroups.Name(all[(idx + 1) % all.Count]) : "";
            membersTabText.text = $"<color=#8A847A>{Trim(prev, 22)}</color>    <color=#E8C46A><b>{PadGroups.Name(memberGroup)}</b></color>    <color=#8A847A>{Trim(next, 22)}</color>";

            if (memberCursor < memberTop)
                memberTop = memberCursor;
            if (memberCursor >= memberTop + MemberRows)
                memberTop = memberCursor - MemberRows + 1;
            memberTop = Mathf.Clamp(memberTop, 0, Mathf.Max(0, memberList.Count - MemberRows));

            for (int i = 0; i < memberRows.Count; i++) {
                int at = memberTop + i;
                var row = memberRows[i];
                if (at >= memberList.Count) {
                    row.Bg.color = Color.clear;
                    row.Box.SetActive(false);
                    row.Text.text = at == 0 ? $"<color=#8A847A>{"pad.editor.empty".i8()}</color>" : "";
                    continue;
                }
                var buff = memberList[at];
                bool member = at < memberCount;
                row.Bg.color = at == memberCursor ? RowSelectedColor : member ? RowColor : RowOutsideColor;
                row.Box.SetActive(member);
                row.Tick.enabled = member && buff.ActiveIn(memberGroup);

                var kind = PadGroups.KindOf(buff, out var targets);
                string span = PadGroups.SpanName(PadGroups.Span(buff));
                string kindText = kind switch {
                    TargetKind.Self => "pad.kind.self".i8(),
                    TargetKind.Party => "pad.kind.party".i8(),
                    TargetKind.Song => "pad.kind.song".i8(),
                    _ => $"<color=#E08A7A>{"pad.kind.none".i8()}</color>"
                };
                string name = member ? Trim(buff.Name, 42) : $"<color=#A8A296>{Trim(buff.Name, 42)}</color>";
                string count = "";
                if (member) {
                    int wanted = Bubble.Group.Count(buff.UnitWants);
                    int reach = Bubble.Group.Count(buff.CanTarget);
                    count = $"<color=#9AD08A>{wanted}/{reach}</color>";
                }
                row.Text.text = $"{name}<pos=52%><color=#B8B0A0>{span}</color><pos=70%>{kindText}<pos=91%>{count}";
            }

            int on = memberList.Take(memberCount).Count(b => b.ActiveIn(memberGroup));
            string position = memberList.Count > MemberRows ? $"  <color=#8A847A>{memberCursor + 1}/{memberList.Count}</color>" : "";
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
                    row.Add(MakeKey(rowGo.transform, c.ToString(), c.ToString(), 58));
                keys.Add(row);
            }
            var special = MakeHorizontal(keyHolder, "Keys", 6, TextAnchor.MiddleCenter);
            keys.Add(new List<(Image, TextMeshProUGUI, string)> {
                MakeKey(special.transform, KeySpace, "pad.key.space".i8(), 200),
                MakeKey(special.transform, KeyShift, "pad.key.shift".i8(), 110),
                MakeKey(special.transform, KeyLang, latin ? "RU" : "EN", 90),
                MakeKey(special.transform, KeyErase, "pad.key.erase".i8(), 150),
                MakeKey(special.transform, KeyDone, "pad.key.done".i8(), 150),
            });
            keyRow = Mathf.Clamp(keyRow, 0, keys.Count - 1);
            keyCol = Mathf.Clamp(keyCol, 0, keys[keyRow].Count - 1);
        }

        private (Image, TextMeshProUGUI, string) MakeKey(Transform parent, string key, string label, float width) {
            var go = new GameObject("Key", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = KeyColor;
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
                        text.text = shift ? $"<color=#E8C46A>{"pad.key.shift".i8()}</color>" : "pad.key.shift".i8();
                }
            }
        }

        private void RenderName() {
            var duration = PadGroups.Presets[nameDuration];
            string shown = nameBuffer.Length > 0
                ? $"{nameBuffer}<color=#E8C46A>_</color>"
                : $"<color=#E8C46A>_</color><color=#6A655C>{PadGroups.DurationName(duration)}</color>";
            nameText.text = $"<size=80%><color=#B8B0A0>{"pad.name.label".i8()}</color></size>\n<size=130%>{shown}</size>";
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
                Main.Log($"[PAD] group created: {created} '{PadGroups.Name(created.Value)}' {duration}");
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
            i == 0 ? "pad.tab.assigned".i8() : i == 1 ? "pad.tab.all".i8() : PadGroups.Name(tabs[i].Value);

        private void OpenEditor(Page back, BubbleBuff buff) {
            editorBack = back;
            if (buff != null)
                tab = 1;
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
            if (buff.HideBecause(HideReason.Blacklisted))
                return false;
            if (tab == 0)
                return buff.Requested > 0;
            if (tab == 1)
                return true;
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

            var tabs = EditorTabs;
            string prev = EditorTabName(tabs, (tab - 1 + tabs.Count) % tabs.Count);
            string next = EditorTabName(tabs, (tab + 1) % tabs.Count);
            tabText.text = $"<color=#8A847A>{Trim(prev, 22)}</color>    <color=#E8C46A><b>{EditorTabName(tabs, tab)}</b></color>    <color=#8A847A>{Trim(next, 22)}</color>";

            if (cursor < top)
                top = cursor;
            if (cursor >= top + VisibleRows)
                top = cursor - VisibleRows + 1;
            top = Mathf.Clamp(top, 0, Mathf.Max(0, filtered.Count - VisibleRows));

            for (int i = 0; i < listRows.Count; i++) {
                int idx = top + i;
                var row = listRows[i];
                if (idx >= filtered.Count) {
                    row.Bg.color = Color.clear;
                    row.Text.text = idx == 0 ? $"<color=#8A847A>{"pad.editor.empty".i8()}</color>" : "";
                    continue;
                }
                var buff = filtered[idx];
                row.Bg.color = idx == cursor ? RowSelectedColor : RowColor;
                var targetable = party.Where(buff.CanTarget).ToList();
                int wanted = targetable.Count(buff.UnitWants);
                string span = $"<color=#B8B0A0>{PadGroups.SpanName(PadGroups.Span(buff))}</color>";
                string count = wanted > 0 ? $"<color=#9AD08A>{wanted}/{targetable.Count}</color>" : $"<color=#55504A>0/{targetable.Count}</color>";
                int groups = buff.Requested > 0 ? buff.InGroups.Count : 0;
                string groupMark = groups > 0 ? $"<color=#E8C46A>{string.Format("pad.editor.groups".i8(), groups)}</color>" : "";
                row.Text.text = $"{Trim(buff.Name, 46)}<pos=56%>{span}<pos=72%>{groupMark}<pos=91%>{count}";
            }

            string more = filtered.Count > VisibleRows ? $"  <color=#8A847A>{cursor + 1}/{filtered.Count}</color>" : "";
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
                bool atCursor = i == partyCol;
                bg.color = atCursor ? (wants ? ChipCursorWantedColor : ChipCursorColor) : (wants ? ChipWantedColor : ChipColor);
                string name = Trim(unit.CharacterName, 11);
                text.text = can ? name : $"<color=#7A5A55><s>{name}</s></color>";
            }
        }

        private void EnsureChips(int count) {
            while (chips.Count < count) {
                var chip = new GameObject("Chip", typeof(RectTransform));
                chip.transform.SetParent(chipHolder, false);
                var bg = chip.AddComponent<Image>();
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

        // ---------- help page ----------

        private void OpenHelp() {
            helpSections.Clear();
            helpSectionOffsets = null;
            helpText.spriteAsset = PadHelp.IconAsset;
            helpText.text = PadHelp.Build(helpSections);
            helpOffset = 0;
            ScrollHelp(0);
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
            ((RectTransform)helpText.transform).anchoredPosition = new Vector2(0, helpOffset);
            bool scrolls = max > 1f;
            helpThumb.parent.gameObject.SetActive(scrolls);
            if (scrolls) {
                float visible = HelpHeight / (HelpHeight + max);
                float start = helpOffset / (HelpHeight + max);
                helpThumb.anchorMax = new Vector2(1, 1 - start);
                helpThumb.anchorMin = new Vector2(0, 1 - start - visible);
            }
        }

        // LB/RB jump to the previous or next heading. Offsets are measured on first use,
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

        private void EnsureRows(List<ListRow> list, Transform holder, int count, float size, bool withBox) {
            while (list.Count < count)
                list.Add(MakeListRow(holder, font, size, withBox));
            while (list.Count > count) {
                Destroy(list[list.Count - 1].Bg.gameObject);
                list.RemoveAt(list.Count - 1);
            }
        }

        // ---------- construction ----------

        // Own screen-space overlay: the game's static PC canvas is hidden in gamepad mode.
        private static PadQuickMenu Build() {
            var font = FindObjectsOfType<TextMeshProUGUI>()
                .Select(t => t.font)
                .FirstOrDefault(f => f != null);

            var overlay = new GameObject("BI2TL_PadOverlay", typeof(RectTransform));
            DontDestroyOnLoad(overlay);
            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var root = new GameObject("BI2TL_PadQuickMenu", typeof(RectTransform));
            root.SetActive(false);
            root.transform.SetParent(overlay.transform, false);

            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(MainWidth, 0);

            root.AddComponent<Image>().color = PanelColor;
            ConfigureVertical(root.AddComponent<VerticalLayoutGroup>(), new RectOffset(28, 28, 22, 22), 10);
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var menu = root.AddComponent<PadQuickMenu>();
            menu.overlay = overlay;
            menu.panel = rect;
            menu.font = font;
            menu.titleText = MakeText(root.transform, font, "pad.title".i8(), 30, FontStyles.Bold, TitleColor);

            // main
            menu.mainRoot = MakeContainer(root.transform, "Main", 10);
            menu.mainRowHolder = MakeContainer(menu.mainRoot.transform, "Rows", 6).transform;
            menu.reportText = MakeText(menu.mainRoot.transform, font, "", 20, FontStyles.Normal, TextColor);
            menu.reportText.gameObject.SetActive(false);
            MakeHintBar(menu.mainRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.select"),
                (new[] { RewiredActionType.Confirm }, "pad.h.apply"),
                (new[] { RewiredActionType.Func01 }, "pad.h.all"),
                (new[] { RewiredActionType.Func02 }, "pad.h.members"),
                (new[] { RewiredActionType.Decline }, "pad.h.close"));

            // groups
            menu.groupsRoot = MakeContainer(root.transform, "Groups", 8);
            menu.groupsRowHolder = MakeContainer(menu.groupsRoot.transform, "Rows", 5).transform;
            menu.groupsNote = MakeText(menu.groupsRoot.transform, font, "", 19, FontStyles.Normal, TitleColor);
            var groupHints = MakeHintBar(menu.groupsRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.select"),
                (new[] { RewiredActionType.Confirm }, "pad.h.open"),
                (new[] { RewiredActionType.Func02 }, "pad.h.rename"),
                (new[] { RewiredActionType.Func01 }, "pad.h.hide"),
                (new[] { RewiredActionType.RightUp }, "pad.h.delete"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.groupsDeleteHint = groupHints[4];
            menu.groupsRoot.SetActive(false);

            // members
            menu.membersRoot = MakeContainer(root.transform, "Members", 5);
            var memberTabs = MakeHorizontal(menu.membersRoot.transform, "Tabs", 14, TextAnchor.MiddleLeft);
            MakeIcon(memberTabs.transform, font, RewiredActionType.LeftUp, 32);
            menu.membersTabText = MakeText(memberTabs.transform, font, "", 21, FontStyles.Normal, TextColor);
            menu.membersTabText.enableWordWrapping = false;
            MakeIcon(memberTabs.transform, font, RewiredActionType.RightUp, 32);
            for (int r = 0; r < MemberRows; r++)
                menu.memberRows.Add(MakeListRow(menu.membersRoot.transform, font, 20, withBox: true));
            menu.membersDetail = MakeText(menu.membersRoot.transform, font, "", 19, FontStyles.Normal, DimColor);
            menu.membersNote = MakeText(menu.membersRoot.transform, font, "", 19, FontStyles.Normal, TitleColor);
            MakeHintBar(menu.membersRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.buff"),
                (new[] { RewiredActionType.Confirm }, "pad.h.addremove"),
                (new[] { RewiredActionType.Func01 }, "pad.h.tick"),
                (new[] { RewiredActionType.Func02 }, "pad.h.fill"),
                (new[] { RewiredActionType.DPadRight }, "pad.h.targets"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.group"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.membersRoot.SetActive(false);

            // name
            menu.nameRoot = MakeContainer(root.transform, "Name", 10);
            menu.nameText = MakeText(menu.nameRoot.transform, font, "", 24, FontStyles.Normal, TextColor);
            var durationRow = MakeHorizontal(menu.nameRoot.transform, "Duration", 14, TextAnchor.MiddleLeft);
            MakeIcon(durationRow.transform, font, RewiredActionType.LeftUp, 32);
            menu.nameDurationText = MakeText(durationRow.transform, font, "", 21, FontStyles.Normal, TextColor);
            menu.nameDurationText.enableWordWrapping = false;
            MakeIcon(durationRow.transform, font, RewiredActionType.RightUp, 32);
            menu.keyHolder = MakeContainer(menu.nameRoot.transform, "Keyboard", 6).transform;
            MakeText(menu.nameRoot.transform, font, T("pad.name.note"), 18, FontStyles.Normal, DimColor);
            MakeHintBar(menu.nameRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical, RewiredActionType.DPadHorizontal }, "pad.h.key"),
                (new[] { RewiredActionType.Confirm }, "pad.h.type"),
                (new[] { RewiredActionType.Func01 }, "pad.h.erase"),
                (new[] { RewiredActionType.Func02 }, "pad.h.done"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.duration"),
                (new[] { RewiredActionType.Decline }, "pad.h.cancel"));
            menu.nameRoot.SetActive(false);

            // editor
            menu.editorRoot = MakeContainer(root.transform, "Editor", 6);
            var tabRow = MakeHorizontal(menu.editorRoot.transform, "Tabs", 14, TextAnchor.MiddleLeft);
            MakeIcon(tabRow.transform, font, RewiredActionType.LeftUp, 32);
            menu.tabText = MakeText(tabRow.transform, font, "", 21, FontStyles.Normal, TextColor);
            menu.tabText.enableWordWrapping = false;
            MakeIcon(tabRow.transform, font, RewiredActionType.RightUp, 32);
            for (int r = 0; r < VisibleRows; r++)
                menu.listRows.Add(MakeListRow(menu.editorRoot.transform, font, 21, withBox: false));
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
            MakeHintBar(menu.editorRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.buff"),
                (new[] { RewiredActionType.DPadHorizontal }, "pad.h.char"),
                (new[] { RewiredActionType.Confirm }, "pad.h.target"),
                (new[] { RewiredActionType.Func02 }, "pad.h.party"),
                (new[] { RewiredActionType.Func01 }, "pad.h.auto"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
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
            helpRect.offsetMax = new Vector2(-24, 0);
            menu.helpText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var track = new GameObject("Track", typeof(RectTransform));
            track.transform.SetParent(viewport.transform, false);
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(1, 0);
            trackRect.anchorMax = new Vector2(1, 1);
            trackRect.pivot = new Vector2(1, 0.5f);
            trackRect.sizeDelta = new Vector2(6, 0);
            track.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            var thumb = new GameObject("Thumb", typeof(RectTransform));
            thumb.transform.SetParent(track.transform, false);
            menu.helpThumb = (RectTransform)thumb.transform;
            menu.helpThumb.offsetMin = menu.helpThumb.offsetMax = Vector2.zero;
            thumb.AddComponent<Image>().color = new Color(0.93f, 0.8f, 0.5f, 0.7f);
            MakeHintBar(menu.helpRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.scroll"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.section"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.helpRoot.SetActive(false);

            return menu;
        }

        // Button hints the way the game draws them: the console icon of each button, then a label.
        private static List<GameObject> MakeHintBar(Transform parent, TMP_FontAsset font, params (RewiredActionType[] buttons, string labelKey)[] items) {
            var made = new List<GameObject>();
            var separator = new GameObject("HintSeparator", typeof(RectTransform));
            separator.transform.SetParent(parent, false);
            separator.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            separator.AddComponent<LayoutElement>().preferredHeight = 1;

            var bar = MakeHorizontal(parent, "Hints", 24, TextAnchor.MiddleCenter);
            foreach (var (buttons, labelKey) in items) {
                var item = MakeHorizontal(bar.transform, "Hint", 6, TextAnchor.MiddleLeft);
                foreach (var button in buttons)
                    MakeIcon(item.transform, font, button, 30);
                var label = MakeText(item.transform, font, labelKey.i8(), 19, FontStyles.Normal, DimColor);
                label.enableWordWrapping = false;
                made.Add(item);
            }
            return made;
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
        private static void MakeIcon(Transform parent, TMP_FontAsset font, RewiredActionType button, float size) {
            Sprite sprite = null;
            try {
                sprite = GamePadIcons.Instance?.GetIcon(button);
            } catch (Exception) { }
            if (sprite == null) {
                var text = MakeText(parent, font, $"[{IconFallback(button)}]", 19, FontStyles.Bold, TitleColor);
                text.enableWordWrapping = false;
                return;
            }
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = size;
            element.preferredHeight = size;
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

        private static GameObject MakeContainer(Transform parent, string name, float spacing) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ConfigureVertical(go.AddComponent<VerticalLayoutGroup>(), new RectOffset(0, 0, 0, 0), spacing);
            return go;
        }

        // A row: background, optional tick box (outline, dark inside, green tick), one line of text.
        private static ListRow MakeListRow(Transform parent, TMP_FontAsset font, float size, bool withBox) {
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

            var row = new ListRow { Bg = bg };
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

            row.Text = MakeText(go.transform, font, "", size, FontStyles.Normal, TextColor);
            row.Text.enableWordWrapping = false;
            row.Text.overflowMode = TextOverflowModes.Ellipsis;
            var textElement = row.Text.gameObject.AddComponent<LayoutElement>();
            textElement.flexibleWidth = 1;
            return row;
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
}
