using BuffIt2TheLimit.Config;
using BuffIt2TheLimit.Extensions;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Owlcat.Runtime.UI.ConsoleTools.GamepadInput;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    // Gamepad-mode buff menu with two pages.
    // Main: per-group status, apply one group or all groups in sequence, combat toggle, cast report.
    // Editor: buff list by tab, targets per party member, group assignment.
    // Buttons go through the game's console input layer (the world and HUD are blocked while
    // the menu is open); directions are polled from the Rewired player for hold-to-repeat.
    internal class PadQuickMenu : MonoBehaviour {

        private enum Page { Main, Editor, Help }

        private enum Tab { Assigned, All, Long, Quick, Important }

        private static PadQuickMenu instance;
        private static readonly BuffGroup[] Groups = { BuffGroup.Long, BuffGroup.Quick, BuffGroup.Important };
        private static readonly Tab[] Tabs = { Tab.Assigned, Tab.All, Tab.Long, Tab.Quick, Tab.Important };

        private static readonly Color PanelColor = new(0.06f, 0.05f, 0.04f, 0.94f);
        private static readonly Color RowColor = new(1f, 1f, 1f, 0.04f);
        private static readonly Color RowSelectedColor = new(0.62f, 0.47f, 0.2f, 0.55f);
        private static readonly Color ChipColor = new(1f, 1f, 1f, 0.06f);
        private static readonly Color ChipWantedColor = new(0.3f, 0.55f, 0.28f, 0.75f);
        private static readonly Color ChipCursorColor = new(0.85f, 0.66f, 0.28f, 0.9f);
        private static readonly Color ChipCursorWantedColor = new(0.55f, 0.75f, 0.3f, 0.95f);
        private static readonly Color TitleColor = new(0.93f, 0.8f, 0.5f);
        private static readonly Color TextColor = new(0.92f, 0.9f, 0.85f);
        private static readonly Color DimColor = new(0.65f, 0.62f, 0.56f);

        private const float MainWidth = 820f;
        private const float EditorWidth = 1180f;
        private const int VisibleRows = 10;
        private const float StickThreshold = 0.5f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.08f;
        private const float RoutineTimeout = 30f;

        private GameObject overlay;
        private RectTransform panel;
        private TextMeshProUGUI titleText;
        private IDisposable layerHandle;
        private Page page;
        private float nextRefresh;
        private bool recalcTried;

        private GameObject mainRoot;
        private readonly List<(Image bg, TextMeshProUGUI text)> rows = new();
        private TextMeshProUGUI reportText;
        private int selected;

        private GameObject editorRoot;
        private GameObject helpRoot;
        private TextMeshProUGUI helpText;
        private List<string> helpLines = new();
        private int helpTop;
        private const int HelpVisibleLines = 20;
        private TextMeshProUGUI tabText;
        private readonly List<(Image bg, TextMeshProUGUI text)> listRows = new();
        private TextMeshProUGUI detailText;
        private Transform chipHolder;
        private readonly List<(Image bg, TextMeshProUGUI text)> chips = new();
        private TMP_FontAsset font;
        private int tab;
        private int cursor;
        private int top;
        private int partyCol;
        private List<BubbleBuff> filtered = new();

        private RepeatInput vertical;
        private RepeatInput horizontal;

        private readonly Queue<BuffGroup> pending = new();
        private BuffGroup current;
        private bool waiting;
        private bool inExecute;
        private float waitDeadline;
        private readonly List<string> report = new();

        private int MainRowCount => Groups.Length + 3;
        private int HelpRow => Groups.Length + 2;
        private int CombatRow => Groups.Length;
        private int EditorRow => Groups.Length + 1;

        private static BufferState State => GlobalBubbleBuffer.Instance?.SpellbookController?.state;

        public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

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
                if (Input.GetKeyDown(KeyCode.Return)) OnConfirm();
                if (Input.GetKeyDown(KeyCode.Backspace)) OnDecline();

                if (waiting && Time.unscaledTime > waitDeadline) {
                    Main.Log($"[PAD] no result for {current} after {RoutineTimeout}s");
                    waiting = false;
                    ReplaceLast(string.Format("pad.timeout".i8(), GroupName(current)));
                    RunNext();
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
                case RewiredActionType.LeftUp: SwitchTab(-1); break;
                case RewiredActionType.RightUp: SwitchTab(1); break;
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

            int dv = vertical.Step(v);
            int dh = horizontal.Step(h);
            if (dv != 0) MoveVertical(dv);
            if (dh != 0 && page == Page.Editor) MovePartyCursor(dh);
        }

        private void OnConfirm() {
            if (page == Page.Editor) {
                ToggleTarget();
                return;
            }
            if (selected == CombatRow)
                ToggleAllowInCombat();
            else if (selected == EditorRow)
                ShowPage(Page.Editor);
            else if (selected == HelpRow)
                ShowPage(Page.Help);
            else
                Enqueue(new[] { Groups[selected] });
        }

        private void OnDecline() {
            if (page != Page.Main)
                ShowPage(Page.Main);
            else
                Close();
        }

        private void OnFuncX() {
            if (page == Page.Editor)
                CycleGroup();
            else
                Enqueue(Groups);
        }

        private void OnFuncY() {
            if (page == Page.Editor)
                ToggleWholeParty();
            else
                ShowPage(Page.Editor);
        }

        private void MoveVertical(int delta) {
            if (page == Page.Main) {
                selected = Mathf.Clamp(selected + delta, 0, MainRowCount - 1);
                UpdateMainSelection();
            } else if (page == Page.Help) {
                helpTop = Mathf.Clamp(helpTop + delta, 0, Mathf.Max(0, helpLines.Count - HelpVisibleLines));
                RenderHelp();
            } else {
                if (filtered.Count == 0)
                    return;
                cursor = Mathf.Clamp(cursor + delta, 0, filtered.Count - 1);
                RenderEditor();
            }
        }

        // ---------- pages ----------

        private void ShowPage(Page next) {
            page = next;
            mainRoot.SetActive(page == Page.Main);
            editorRoot.SetActive(page == Page.Editor);
            helpRoot.SetActive(page == Page.Help);
            panel.sizeDelta = new Vector2(page == Page.Main ? MainWidth : EditorWidth, 0);
            titleText.text = page switch {
                Page.Main => "pad.title".i8(),
                Page.Editor => "pad.editor.title".i8(),
                _ => "pad.help.title".i8()
            };
            if (page == Page.Main) {
                RefreshMain();
            } else if (page == Page.Help) {
                EnsureBuffList();
                helpLines = BuildHelp();
                helpTop = 0;
                RenderHelp();
            } else {
                EnsureBuffList();
                RebuildFilter(keepBuff: null);
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

        // ---------- main page ----------

        private void ToggleAllowInCombat() {
            var state = State;
            if (state == null)
                return;
            state.AllowInCombat = !state.AllowInCombat;
            // Same refresh the mod runs on combat state changes: re-enables/disables HUD buttons.
            new HideBubbleButtonsWatcher().HandlePartyCombatStateChanged(Game.Instance.Player.IsInCombat);
            RefreshMain();
        }

        // Groups run one after another: the next group is recalculated only after the
        // previous routine has spent its slots, otherwise both plan on the same slots.
        private void Enqueue(IEnumerable<BuffGroup> groups) {
            if (waiting || pending.Count > 0)
                return;
            report.Clear();
            foreach (var g in groups)
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
                report.Add(string.Format("pad.running".i8(), GroupName(current)));
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
                    ReplaceLast(string.Format("pad.blocked".i8(), GroupName(current)));
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
            ReplaceLast(string.Format("pad.result".i8(), GroupName(current), applied, attempted, skipped));
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
                rows[i].bg.color = i == selected ? RowSelectedColor : RowColor;
        }

        private void RefreshMain() {
            nextRefresh = Time.unscaledTime + 1f;
            UpdateMainSelection();
            try {
                EnsureBuffList();
                var state = State;
                rows[CombatRow].text.text = CombatRowText(state);
                rows[EditorRow].text.text = $"<b>{"pad.editor.row".i8()}</b>\n<size=80%>{"pad.editor.row.desc".i8()}</size>";
                rows[HelpRow].text.text = $"<b>{"pad.help.row".i8()}</b>\n<size=80%>{"pad.help.row.desc".i8()}</size>";
                if (state?.BuffList == null) {
                    for (int i = 0; i < Groups.Length; i++)
                        rows[i].text.text = $"<b>{GroupName(Groups[i])}</b>\n<size=80%>{"pad.nostate".i8()}</size>";
                    return;
                }

                Bubble.RefreshGroup();
                var unitData = Bubble.Group.ToDictionary(u => u.UniqueId, u => new UnitBuffData(u));

                for (int i = 0; i < Groups.Length; i++) {
                    var group = Groups[i];
                    int buffs = 0, wanted = 0, active = 0;
                    TimeSpan? soonest = null;

                    foreach (var buff in state.BuffList) {
                        if (!buff.InGroups.Contains(group) || buff.Requested == 0)
                            continue;
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
                            foreach (var fact in unit.Buffs.RawFacts) {
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
                    rows[i].text.text = $"<b>{GroupName(group)}</b>\n<size=80%>{status}</size>";
                }
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.RefreshMain");
            }
        }

        private static string CombatRowText(BufferState state) {
            string value = state == null
                ? "pad.nostate".i8()
                : state.AllowInCombat
                    ? $"<color=#9AD08A>{"pad.combat.on".i8()}</color>"
                    : $"<color=#B0A898>{"pad.combat.off".i8()}</color>";
            return $"<b>{"pad.combat".i8()}</b>\n<size=80%>{value}</size>";
        }

        // ---------- editor page ----------

        private void SwitchTab(int delta) {
            if (page != Page.Editor)
                return;
            tab = (tab + delta + Tabs.Length) % Tabs.Length;
            RebuildFilter(keepBuff: null);
        }

        private bool PassesTab(BubbleBuff buff) {
            if (buff.HideBecause(HideReason.Blacklisted))
                return false;
            return Tabs[tab] switch {
                Tab.Assigned => buff.Requested > 0,
                Tab.All => true,
                Tab.Long => buff.Requested > 0 && buff.InGroups.Contains(BuffGroup.Long),
                Tab.Quick => buff.Requested > 0 && buff.InGroups.Contains(BuffGroup.Quick),
                Tab.Important => buff.Requested > 0 && buff.InGroups.Contains(BuffGroup.Important),
                _ => true
            };
        }

        private void RebuildFilter(BubbleBuff keepBuff) {
            var state = State;
            filtered = state?.BuffList == null
                ? new List<BubbleBuff>()
                : state.BuffList.Where(PassesTab).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
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
            buff.SetUnitWants(unit, !buff.UnitWants(unit));
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
            ApplyEdit(buff);
        }

        // Long → Quick → Important → Long. A buff without targets is shown without a group,
        // the group only matters once at least one target is set.
        private void CycleGroup() {
            var buff = SelectedBuff;
            if (buff == null)
                return;
            BuffGroup next = BuffGroup.Long;
            if (buff.InGroups.Count == 1) {
                next = buff.InGroups.First() switch {
                    BuffGroup.Long => BuffGroup.Quick,
                    BuffGroup.Quick => BuffGroup.Important,
                    _ => BuffGroup.Long
                };
            }
            buff.InGroups.Clear();
            buff.InGroups.Add(next);
            State?.Save();
            RebuildFilter(keepBuff: buff);
        }

        private void ApplyEdit(BubbleBuff buff) {
            try {
                State?.Recalculate(false);
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.ApplyEdit");
            }
            RebuildFilter(keepBuff: buff);
        }

        private string GroupBadges(BubbleBuff buff) {
            var parts = new List<string>();
            foreach (var g in Groups) {
                bool on = buff.Requested > 0 && buff.InGroups.Contains(g);
                string letter = GroupLetter(g);
                parts.Add(on ? $"<color=#E8C46A>{letter}</color>" : $"<color=#55504A>{letter}</color>");
            }
            return string.Join(" ", parts);
        }

        private void RenderEditor() {
            var party = Party;
            if (partyCol >= party.Count)
                partyCol = Mathf.Max(0, party.Count - 1);

            var tabNames = Tabs.Select((t, i) => i == tab ? $"<color=#E8C46A><b>{TabName(t)}</b></color>" : $"<color=#8A847A>{TabName(t)}</color>");
            tabText.text = string.Join("    ", tabNames);

            if (cursor < top)
                top = cursor;
            if (cursor >= top + VisibleRows)
                top = cursor - VisibleRows + 1;
            top = Mathf.Clamp(top, 0, Mathf.Max(0, filtered.Count - VisibleRows));

            for (int i = 0; i < listRows.Count; i++) {
                int idx = top + i;
                var (bg, text) = listRows[i];
                if (idx >= filtered.Count) {
                    bg.color = Color.clear;
                    text.text = idx == 0 ? $"<color=#8A847A>{"pad.editor.empty".i8()}</color>" : "";
                    continue;
                }
                var buff = filtered[idx];
                bg.color = idx == cursor ? RowSelectedColor : RowColor;
                var targetable = party.Where(buff.CanTarget).ToList();
                int wanted = targetable.Count(buff.UnitWants);
                string rounds = buff.HideBecause(HideReason.Short) ? $" <color=#8A847A>{"pad.editor.rounds".i8()}</color>" : "";
                string count = wanted > 0 ? $"<color=#9AD08A>{wanted}/{targetable.Count}</color>" : $"<color=#55504A>0/{targetable.Count}</color>";
                text.text = $"{Trim(buff.Name, 46)}{rounds}<pos=78%>{GroupBadges(buff)}<pos=91%>{count}";
            }

            string more = filtered.Count > VisibleRows ? $"  <color=#8A847A>{cursor + 1}/{filtered.Count}</color>" : "";
            var selectedBuff = SelectedBuff;
            if (selectedBuff == null) {
                detailText.text = more;
            } else {
                var groups = selectedBuff.Requested > 0
                    ? string.Join(", ", Groups.Where(selectedBuff.InGroups.Contains).Select(GroupName))
                    : "pad.editor.nogroup".i8();
                detailText.text = string.Format("pad.editor.detail".i8(), selectedBuff.Name, groups, selectedBuff.CasterQueue.Count) + more;
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

        private void RenderHelp() {
            var visible = helpLines.Skip(helpTop).Take(HelpVisibleLines);
            string position = helpLines.Count > HelpVisibleLines
                ? $"\n<color=#8A847A>{helpTop + 1}–{Mathf.Min(helpTop + HelpVisibleLines, helpLines.Count)} / {helpLines.Count}</color>"
                : "";
            helpText.text = string.Join("\n", visible) + position;
        }

        private List<string> BuildHelp() {
            var lines = new List<string>();
            foreach (var line in "pad.help.body".i8().Split('\n'))
                lines.Add(line.StartsWith("#") ? $"<color=#E8C46A><b>{line.TrimStart('#', ' ')}</b></color>" : line);
            lines.Add("");
            lines.Add($"<color=#E8C46A><b>{"pad.help.example.title".i8()}</b></color>");
            try {
                lines.AddRange(BuildExample());
            } catch (Exception ex) {
                Main.Error(ex, "PadQuickMenu.BuildExample");
                lines.Add("pad.help.example.none".i8());
            }
            return lines;
        }

        // Example built from the current party and buff setup.
        private List<string> BuildExample() {
            var lines = new List<string>();
            var state = State;
            var party = Party;
            if (state?.BuffList == null || party.Count == 0) {
                lines.Add("pad.help.example.none".i8());
                return lines;
            }
            var buffs = state.BuffList.Where(b => !b.HideBecause(HideReason.Blacklisted) && b.CasterQueue.Count > 0).ToList();
            string Names(IEnumerable<UnitEntityData> units) => string.Join(", ", units.Select(u => u.CharacterName));

            var multi = buffs
                .Where(b => b.Requested > 0 && b.InGroups.Contains(BuffGroup.Long) && party.Count(b.CanTarget) > 1)
                .FirstOrDefault(b => party.Any(u => b.CanTarget(u) && !b.UnitWants(u)))
                ?? buffs.FirstOrDefault(b => b.Requested > 0 && party.Count(b.CanTarget) > 1);
            if (multi != null) {
                var caster = multi.CasterQueue[0].who;
                var wanted = party.Where(multi.UnitWants).ToList();
                var missing = party.FirstOrDefault(u => multi.CanTarget(u) && !multi.UnitWants(u));
                var groupName = string.Join(", ", Groups.Where(multi.InGroups.Contains).Select(GroupName));
                lines.Add(string.Format("pad.help.example.1".i8(), multi.Name, caster?.CharacterName ?? "?", wanted.Count > 0 ? Names(wanted) : "—", groupName));
                if (missing != null)
                    lines.Add(string.Format("pad.help.example.1add".i8(), missing.CharacterName, multi.Name));
                else
                    lines.Add(string.Format("pad.help.example.1remove".i8(), wanted.LastOrDefault()?.CharacterName ?? "?", multi.Name));
            }

            var rounds = buffs.FirstOrDefault(b => b.HideBecause(HideReason.Short) && b.Requested > 0)
                ?? buffs.FirstOrDefault(b => b.HideBecause(HideReason.Short));
            if (rounds != null) {
                bool inImportant = rounds.Requested > 0 && rounds.InGroups.Contains(BuffGroup.Important);
                lines.Add(string.Format(inImportant ? "pad.help.example.2ok".i8() : "pad.help.example.2move".i8(), rounds.Name));
            }

            var self = buffs.FirstOrDefault(b => party.Count(b.CanTarget) == 1);
            if (self != null) {
                var only = party.First(self.CanTarget);
                lines.Add(string.Format("pad.help.example.3".i8(), self.Name, only.CharacterName));
            }

            if (lines.Count == 0)
                lines.Add("pad.help.example.none".i8());
            return lines;
        }

        // ---------- helpers ----------

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max - 1) + "…";

        internal static string GroupName(BuffGroup group) => group switch {
            BuffGroup.Long => "group.normal".i8(),
            BuffGroup.Quick => "group.short".i8(),
            BuffGroup.Important => "group.important".i8(),
            _ => group.ToString()
        };

        private static string GroupLetter(BuffGroup group) => group switch {
            BuffGroup.Long => "pad.letter.long".i8(),
            BuffGroup.Quick => "pad.letter.quick".i8(),
            BuffGroup.Important => "pad.letter.important".i8(),
            _ => "?"
        };

        private static string TabName(Tab t) => t switch {
            Tab.Assigned => "pad.tab.assigned".i8(),
            Tab.All => "pad.tab.all".i8(),
            Tab.Long => GroupName(BuffGroup.Long),
            Tab.Quick => GroupName(BuffGroup.Quick),
            Tab.Important => GroupName(BuffGroup.Important),
            _ => t.ToString()
        };

        private static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";

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

            menu.mainRoot = MakeContainer(root.transform, "Main", 10);
            for (int r = 0; r < menu.MainRowCount; r++)
                menu.rows.Add(MakeRow(menu.mainRoot.transform, font, 24));
            menu.reportText = MakeText(menu.mainRoot.transform, font, "", 20, FontStyles.Normal, TextColor);
            menu.reportText.gameObject.SetActive(false);
            MakeHintBar(menu.mainRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.select"),
                (new[] { RewiredActionType.Confirm }, "pad.h.apply"),
                (new[] { RewiredActionType.Func01 }, "pad.h.all"),
                (new[] { RewiredActionType.Func02 }, "pad.h.setup"),
                (new[] { RewiredActionType.Decline }, "pad.h.close"));

            menu.editorRoot = MakeContainer(root.transform, "Editor", 6);
            var tabRow = MakeHorizontal(menu.editorRoot.transform, "Tabs", 14, TextAnchor.MiddleLeft);
            MakeIcon(tabRow.transform, font, RewiredActionType.LeftUp, 32);
            menu.tabText = MakeText(tabRow.transform, font, "", 21, FontStyles.Normal, TextColor);
            menu.tabText.enableWordWrapping = false;
            MakeIcon(tabRow.transform, font, RewiredActionType.RightUp, 32);
            for (int r = 0; r < VisibleRows; r++) {
                var row = MakeRow(menu.editorRoot.transform, font, 21);
                row.text.enableWordWrapping = false;
                row.text.overflowMode = TextOverflowModes.Ellipsis;
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
            MakeHintBar(menu.editorRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.buff"),
                (new[] { RewiredActionType.DPadHorizontal }, "pad.h.char"),
                (new[] { RewiredActionType.Confirm }, "pad.h.target"),
                (new[] { RewiredActionType.Func02 }, "pad.h.party"),
                (new[] { RewiredActionType.Func01 }, "pad.h.group"),
                (new[] { RewiredActionType.LeftUp, RewiredActionType.RightUp }, "pad.h.tab"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.editorRoot.SetActive(false);

            menu.helpRoot = MakeContainer(root.transform, "Help", 8);
            menu.helpText = MakeText(menu.helpRoot.transform, font, "", 20, FontStyles.Normal, TextColor);
            MakeHintBar(menu.helpRoot.transform, font,
                (new[] { RewiredActionType.DPadVertical }, "pad.h.scroll"),
                (new[] { RewiredActionType.Decline }, "pad.h.back"));
            menu.helpRoot.SetActive(false);

            return menu;
        }

        // Button hints the way the game draws them: the console icon of each button, then a label.
        private static void MakeHintBar(Transform parent, TMP_FontAsset font, params (RewiredActionType[] buttons, string labelKey)[] items) {
            var separator = new GameObject("HintSeparator", typeof(RectTransform));
            separator.transform.SetParent(parent, false);
            separator.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            separator.AddComponent<LayoutElement>().preferredHeight = 1;

            var bar = MakeHorizontal(parent, "Hints", 26, TextAnchor.MiddleCenter);
            foreach (var (buttons, labelKey) in items) {
                var item = MakeHorizontal(bar.transform, "Hint", 6, TextAnchor.MiddleLeft);
                foreach (var button in buttons)
                    MakeIcon(item.transform, font, button, 30);
                var label = MakeText(item.transform, font, labelKey.i8(), 19, FontStyles.Normal, DimColor);
                label.enableWordWrapping = false;
            }
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

        private static (Image bg, TextMeshProUGUI text) MakeRow(Transform parent, TMP_FontAsset font, float size) {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var bg = row.AddComponent<Image>();
            bg.color = RowColor;
            ConfigureVertical(row.AddComponent<VerticalLayoutGroup>(), new RectOffset(16, 16, 6, 6), 0);
            var text = MakeText(row.transform, font, "", size, FontStyles.Normal, TextColor);
            return (bg, text);
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
            text.text = value;
            return text;
        }
    }
}
