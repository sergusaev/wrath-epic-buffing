using Kingmaker;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuffIt2TheLimit {

    // Kingmaker casting: RuleCastSpell has no pre-trigger hook here, so the target is checked
    // before the rule, the spell resolves at once and the slot or item is spent right after.
    public class KmExecutionEngine : IBuffExecutionEngine {

        private const int BatchSize = 8;
        private const float Delay = 0.05f;

        public IEnumerator CreateSpellCastRoutine(List<CastTask> tasks) {
            for (int i = 0; i < tasks.Count; i++) {
                Cast(tasks[i]);
                if ((i + 1) % BatchSize == 0)
                    yield return new WaitForSecondsRealtime(Delay);
            }
        }

        private static void Cast(CastTask task) {
            try {
                if (task.SourceType != BuffSourceType.Spell && task.SourceItem != null) {
                    CastFromItem(task);
                    return;
                }
                var spell = task.SpellToCast;
                if (!spell.CanTarget(task.Target)) {
                    Main.Log($"Cast skipped (invalid target): {spell.Name} → {task.Target?.Unit?.CharacterName ?? "<point>"}");
                    return;
                }
                var rule = new RuleCastSpell(spell, task.Target);
                rule.Context.DisableLog = true;
                var rod = ExtendWithRod(task, rule);
                Rulebook.Trigger(rule);
                // The game spends the slot on a failed cast as well (spell failure, armor),
                // and the rod's charge too (MetamagicRodMechanics).
                spell.Spend();
                rod?.Invoke();
                task.ActuallyFired = rule.Success;
                if (!rule.Success)
                    Main.Log($"Cast failed: {spell.Name} by {task.Caster?.CharacterName}");
            } catch (Exception ex) {
                Main.Error(ex, "Kingmaker casting");
            }
        }

        // A scroll, potion, wand or other item, used the way ItemEntity.TryUseFromInventory does
        // (which itself refuses in combat): the spell becomes a fact of the user tied to the item,
        // so the game takes caster level, DC, Use Magic Device and the spending from the item.
        // A failed Use Magic Device spends nothing and is rolled again up to the set attempts.
        private static void CastFromItem(CastTask task) {
            var user = task.Caster;
            var item = ItemFor(task.SourceItem);
            if (item == null) {
                Main.Log($"Cast skipped (item used up): {task.SpellToCast.Name} by {user?.CharacterName}");
                return;
            }
            // An item that already grants the ability (a staff, a quick slot item) casts through
            // that fact; scrolls, potions and wands from the inventory get one for the cast.
            var fact = task.SpellToCast.Fact as Ability;
            bool temporary = fact == null || fact.SourceItem != item;
            if (temporary) {
                fact = user.Descriptor.AddFact<Ability>(task.SpellToCast.Blueprint);
                if (fact == null) {
                    Main.Log($"Cast skipped (no ability): {task.SpellToCast.Name} by {user.CharacterName}");
                    return;
                }
                fact.SourceItem = item;
            }
            try {
                var spell = fact.Data;
                if (!spell.IsAvailable) {
                    Main.Log($"Cast skipped (item not usable): {spell.Name} from {item.Name} by {user.CharacterName}");
                    return;
                }
                if (!spell.CanTarget(task.Target)) {
                    Main.Log($"Cast skipped (invalid target): {spell.Name} → {task.Target?.Unit?.CharacterName ?? "<point>"}");
                    return;
                }
                int attempts = spell.ShouldUseMagicDevice ? Math.Max(1, task.UmdRetries) : 1;
                for (int attempt = 1; attempt <= attempts; attempt++) {
                    var rule = new RuleCastSpell(spell, task.Target);
                    rule.Context.DisableLog = true;
                    Rulebook.Trigger(rule);
                    if (rule.Success)
                        Deliver(rule, spell);
                    if (rule.IsUMDFailed) {
                        Main.Log($"Use Magic Device failed ({attempt}/{attempts}): {item.Name} by {user.CharacterName}");
                        continue;
                    }
                    spell.Spend();
                    task.ActuallyFired = rule.Success;
                    if (!rule.Success)
                        Main.Log($"Cast failed: {spell.Name} from {item.Name} by {user.CharacterName}");
                    return;
                }
            } finally {
                if (temporary)
                    user.Descriptor.RemoveFact(fact);
            }
        }

        // The fact goes away right after the cast, so the spell has to finish before that.
        private static void Deliver(RuleCastSpell rule, AbilityData spell) {
            var process = rule.ExecutionProcess;
            if (process == null) return;
            process.InstantDeliver();
            process.Detach();
            int ticks = 0;
            while (!process.IsEnded && ticks++ < 1000)
                process.Tick();
            if (ticks >= 1000)
                Main.Log($"Item cast did not finish: {spell.Name}");
        }

        // The planned stack may be gone by now (the last scroll of it read by an earlier cast):
        // any other stack of the same item does.
        private static ItemEntity ItemFor(ItemEntity planned) {
            if (Usable(planned)) return planned;
            return Game.Instance.Player.Inventory.FirstOrDefault(i => i.Blueprint == planned.Blueprint && Usable(i));
        }

        private static bool Usable(ItemEntity item) =>
            item != null && item.Collection != null && item.Count > 0 && (!item.IsSpendCharges || item.Charges > 0);

        // Extend from a metamagic rod the caster holds, as the rod itself would add it while
        // switched on; returns what spends its charge. Nothing when the rod is on already:
        // the game has added Extend and spends the charge itself.
        private static Action ExtendWithRod(CastTask task, RuleCastSpell rule) {
            if (task.MetamagicRodItem == null) return null;
            var parameters = rule.Context.m_Params;
            if (parameters == null || parameters.HasMetamagic(Metamagic.Extend)) return null;
            var (item, rod, resource) = BufferState.ExtendRods(task.Caster).FirstOrDefault(r => r.item == task.MetamagicRodItem);
            if (rod == null || resource == null || task.Caster.Descriptor.Resources.GetResourceAmount(resource) < 1) {
                Main.Log($"Extend Rod unavailable at cast: {task.SpellToCast.Name} by {task.Caster.CharacterName}");
                return null;
            }
            parameters.Metamagic |= Metamagic.Extend;
            return () => task.Caster.Descriptor.Resources.Spend(resource, 1);
        }
    }
}
