using BuffIt2TheLimit.Config;
using Kingmaker.EntitySystem.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuffIt2TheLimit {

    // Longest duration of a buff's effects; Toggle marks activatables and songs.
    public enum BuffDuration { Unknown, Rounds, Minutes, TenMinutes, Hours, Toggle }

    // Duration a group is meant for: names it and drives auto-fill.
    public enum GroupDuration { Any, Rounds, Minutes, TenMinutesPlus, Hours, Toggles }

    // How the targets of a buff are chosen automatically.
    internal enum TargetKind { None, Self, Party, Song }

    // Groups of the buff menu: the three built-in groups plus custom ones stored in
    // SavedBufferState.Groups, and buff membership with a per-group on/off switch.
    // Targets belong to the buff, not to the group: a buff in two groups has the same targets in both.
    internal static class PadGroups {

        public const int NameLimit = 32;

        public static readonly BuffGroup[] BuiltIn = { BuffGroup.Long, BuffGroup.Quick, BuffGroup.Important };

        public static readonly GroupDuration[] Presets = {
            GroupDuration.Hours, GroupDuration.TenMinutesPlus, GroupDuration.Minutes,
            GroupDuration.Rounds, GroupDuration.Toggles, GroupDuration.Any
        };

        private static BufferState State => GlobalBubbleBuffer.Instance?.SpellbookController?.state;

        private static List<SavedGroup> Saved {
            get {
                var saved = State?.SavedState;
                if (saved == null)
                    return null;
                saved.Groups ??= new List<SavedGroup>();
                return saved.Groups;
            }
        }

        // ---------- groups ----------

        public static bool IsCustom(BuffGroup group) => group >= BuffGroup.Custom1;

        public static List<BuffGroup> All() {
            var list = new List<BuffGroup>(BuiltIn);
            var saved = Saved;
            if (saved != null)
                list.AddRange(saved.Where(g => IsCustom(g.Id)).Select(g => g.Id).Distinct());
            return list;
        }

        public static List<BuffGroup> Visible() => All().Where(g => !IsHidden(g)).ToList();

        private static SavedGroup Find(BuffGroup group) => Saved?.FirstOrDefault(g => g.Id == group);

        private static SavedGroup Entry(BuffGroup group) {
            var entry = Find(group);
            if (entry == null) {
                entry = new SavedGroup { Id = group, Duration = DefaultDuration(group) };
                Saved?.Add(entry);
            }
            return entry;
        }

        // Without a name of its own a group is called by its duration.
        public static string Name(BuffGroup group) {
            var name = Find(group)?.Name;
            return string.IsNullOrWhiteSpace(name) ? DurationName(DurationOf(group)) : name;
        }

        public static GroupDuration DurationOf(BuffGroup group) => Find(group)?.Duration ?? DefaultDuration(group);

        private static GroupDuration DefaultDuration(BuffGroup group) => group switch {
            BuffGroup.Long => GroupDuration.TenMinutesPlus,
            BuffGroup.Quick => GroupDuration.Minutes,
            BuffGroup.Important => GroupDuration.Rounds,
            _ => GroupDuration.Any
        };

        public static bool IsHidden(BuffGroup group) => Find(group)?.Hidden ?? false;

        public static bool CanCreate => FreeSlot() != null;

        private static BuffGroup? FreeSlot() {
            var used = new HashSet<BuffGroup>(All());
            for (var g = BuffGroup.Custom1; g <= BuffGroup.Custom12; g++)
                if (!used.Contains(g))
                    return g;
            return null;
        }

        public static BuffGroup? Create(string name, GroupDuration duration) {
            var slot = FreeSlot();
            if (slot == null || Saved == null)
                return null;
            // A slot left over in buff data from a deleted group must start empty.
            StripGroup(slot.Value);
            Saved.Add(new SavedGroup { Id = slot.Value, Name = CleanName(name, duration), Duration = duration });
            SaveAll();
            return slot;
        }

        public static void Rename(BuffGroup group, string name, GroupDuration duration) {
            if (Saved == null)
                return;
            var entry = Entry(group);
            entry.Duration = duration;
            entry.Name = CleanName(name, duration);
            State?.Save(true);
        }

        private static string CleanName(string name, GroupDuration duration) {
            name = (name ?? "").Trim();
            if (name.Length > NameLimit)
                name = name.Substring(0, NameLimit);
            return name.Length == 0 || name == DurationName(duration) ? null : name;
        }

        public static void SetHidden(BuffGroup group, bool hidden) {
            if (Saved == null)
                return;
            Entry(group).Hidden = hidden;
            State?.Save(true);
        }

        public static void Delete(BuffGroup group) {
            if (!IsCustom(group) || Saved == null)
                return;
            StripGroup(group);
            Saved.RemoveAll(g => g.Id == group);
            State.SavedState.ShortcutKeys?.Remove(group);
            SaveAll();
        }

        // Removes the group from every buff, including saved buffs nobody in the party can cast now.
        private static void StripGroup(BuffGroup group) {
            var state = State;
            if (state == null)
                return;
            var live = new HashSet<BuffKey>();
            if (state.BuffList != null) {
                foreach (var buff in state.BuffList) {
                    live.Add(buff.Key);
                    if (buff.InGroups.Contains(group) || buff.DisabledIn.Contains(group))
                        RemoveFromGroup(buff, group);
                }
            }
            foreach (var pair in state.SavedState.Buffs) {
                if (live.Contains(pair.Key))
                    continue;
                var save = pair.Value;
                save.DisabledIn?.Remove(group);
                if (save.InGroups == null || !save.InGroups.Remove(group))
                    continue;
                if (save.InGroups.Count == 0) {
                    save.InGroups.Add(BuffGroup.Long);
                    save.InGroup = BuffGroup.Long;
                    save.Wanted?.Clear();
                    save.DisabledIn = null;
                }
            }
        }

        public static string DurationName(GroupDuration duration) => duration switch {
            GroupDuration.Rounds => "pad.dur.rounds".i8(),
            GroupDuration.Minutes => "pad.dur.minutes".i8(),
            GroupDuration.TenMinutesPlus => "pad.dur.tenplus".i8(),
            GroupDuration.Hours => "pad.dur.hours".i8(),
            GroupDuration.Toggles => "pad.dur.toggles".i8(),
            _ => "pad.dur.any".i8()
        };

        // ---------- buffs ----------

        public static BuffDuration Span(BubbleBuff buff) {
            if (buff.IsActivatable)
                return BuffDuration.Toggle;
            var effects = buff.BuffsApplied;
            if (effects == null)
                return BuffDuration.Unknown;
            if (effects.Duration != BuffDuration.Unknown)
                return effects.Duration;
            return effects.IsLong ? BuffDuration.Hours : BuffDuration.Rounds;
        }

        public static string SpanName(BuffDuration span) => span switch {
            BuffDuration.Rounds => "pad.span.rounds".i8(),
            BuffDuration.Minutes => "pad.span.minutes".i8(),
            BuffDuration.TenMinutes => "pad.span.ten".i8(),
            BuffDuration.Hours => "pad.span.hours".i8(),
            BuffDuration.Toggle => "pad.span.toggle".i8(),
            _ => "?"
        };

        public static bool Fits(BubbleBuff buff, GroupDuration duration) {
            var span = Span(buff);
            return duration switch {
                GroupDuration.Rounds => span == BuffDuration.Rounds,
                GroupDuration.Minutes => span == BuffDuration.Minutes,
                GroupDuration.TenMinutesPlus => span == BuffDuration.TenMinutes || span == BuffDuration.Hours,
                GroupDuration.Hours => span == BuffDuration.Hours,
                GroupDuration.Toggles => span == BuffDuration.Toggle,
                _ => false
            };
        }

        public static bool IsMember(BubbleBuff buff, BuffGroup group) => buff.Requested > 0 && buff.InGroups.Contains(group);

        public static bool IsOn(BubbleBuff buff, BuffGroup group) => buff.ActiveIn(group);

        // Self buffs (only the caster can be a target) go on every party member who can cast
        // them on himself; party buffs go on everyone they can reach; a song needs one performer.
        public static TargetKind KindOf(BubbleBuff buff, out List<UnitEntityData> targets) {
            targets = Bubble.Group.Where(buff.CanTarget).ToList();
            if (targets.Count == 0)
                return TargetKind.None;
            if (buff.IsSong) {
                targets = targets.Take(1).ToList();
                return TargetKind.Song;
            }
            var casters = new HashSet<string>(buff.CasterQueue.Where(c => c.who != null).Select(c => c.who.UniqueId));
            return targets.All(u => casters.Contains(u.UniqueId)) ? TargetKind.Self : TargetKind.Party;
        }

        public static void AutoTarget(BubbleBuff buff) {
            Bubble.RefreshGroup();
            KindOf(buff, out var targets);
            ClearTargets(buff);
            foreach (var unit in targets)
                buff.SetUnitWants(unit, true);
        }

        private static void ClearTargets(BubbleBuff buff) {
            foreach (var unit in Bubble.ConfigGroup.Concat(Bubble.Group).Distinct()) {
                if (buff.UnitWants(unit))
                    buff.SetUnitWants(unit, false);
            }
            if (State?.SavedState?.Buffs != null && State.SavedState.Buffs.TryGetValue(buff.Key, out var save))
                save.Wanted?.Clear();
        }

        // Adds the buff to the group; a buff without targets gets them automatically.
        // Returns false when nobody in the party can receive it (the buff stays out).
        public static bool Add(BubbleBuff buff, BuffGroup group, bool on) {
            if (buff.Requested == 0) {
                buff.InGroups.Clear();
                buff.DisabledIn.Clear();
                AutoTarget(buff);
                if (buff.Requested == 0) {
                    buff.InGroups.Add(BuffGroup.Long);
                    return false;
                }
            }
            buff.InGroups.Add(group);
            if (on)
                buff.DisabledIn.Remove(group);
            else
                buff.DisabledIn.Add(group);
            return true;
        }

        // Leaving the last group also drops the targets: a buff outside every group is not set up.
        public static void Remove(BubbleBuff buff, BuffGroup group) {
            RemoveFromGroup(buff, group);
        }

        private static void RemoveFromGroup(BubbleBuff buff, BuffGroup group) {
            buff.InGroups.Remove(group);
            buff.DisabledIn.Remove(group);
            if (buff.InGroups.Count == 0 || buff.Requested == 0) {
                ClearTargets(buff);
                buff.InGroups.Clear();
                buff.InGroups.Add(BuffGroup.Long);
                buff.DisabledIn.Clear();
            }
        }

        public static void SetOn(BubbleBuff buff, BuffGroup group, bool on) {
            if (!IsMember(buff, group))
                return;
            if (on)
                buff.DisabledIn.Remove(group);
            else
                buff.DisabledIn.Add(group);
        }

        // Adds every castable buff of the group's duration, switched off, so nothing is cast
        // until it is ticked. Returns the number added, -1 for a group without a duration.
        public static int AutoFill(BuffGroup group) {
            var duration = DurationOf(group);
            var state = State;
            if (duration == GroupDuration.Any)
                return -1;
            if (state?.BuffList == null)
                return 0;
            int added = 0;
            Bubble.RefreshGroup();
            foreach (var buff in state.BuffList.ToList()) {
                if (buff.HideBecause(HideReason.Blacklisted) || buff.CasterQueue.Count == 0)
                    continue;
                if (IsMember(buff, group) || !Fits(buff, duration))
                    continue;
                if (Add(buff, group, on: false))
                    added++;
            }
            return added;
        }

        public static void SaveAll() {
            var state = State;
            if (state == null)
                return;
            try {
                if (state.BuffList != null)
                    state.Recalculate(false);
                else
                    state.Save(true);
            } catch (Exception ex) {
                Main.Error(ex, "PadGroups.SaveAll");
            }
        }
    }
}
