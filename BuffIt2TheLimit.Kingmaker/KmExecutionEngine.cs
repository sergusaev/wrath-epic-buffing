using Kingmaker;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using System;
using System.Collections;
using System.Collections.Generic;
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
                var spell = task.SpellToCast;
                if (!spell.CanTarget(task.Target)) {
                    Main.Log($"Cast skipped (invalid target): {spell.Name} → {task.Target?.Unit?.CharacterName ?? "<point>"}");
                    return;
                }
                var rule = new RuleCastSpell(spell, task.Target);
                rule.Context.DisableLog = true;
                Rulebook.Trigger(rule);
                // The game spends the slot on a failed cast as well (spell failure, armor).
                Consume(task);
                task.ActuallyFired = rule.Success;
                if (!rule.Success)
                    Main.Log($"Cast failed: {spell.Name} by {task.Caster?.CharacterName}");
            } catch (Exception ex) {
                Main.Error(ex, "Kingmaker casting");
            }
        }

        private static void Consume(CastTask task) {
            switch (task.SourceType) {
                case BuffSourceType.Spell:
                    task.SpellToCast.Spend();
                    break;
                case BuffSourceType.Scroll:
                case BuffSourceType.Potion:
                    if (task.SourceItem != null)
                        Game.Instance.Player.Inventory.Remove(task.SourceItem, 1);
                    break;
                case BuffSourceType.Equipment:
                    if (task.SourceItem != null && task.SourceItem.IsSpendCharges)
                        task.SourceItem.Charges--;
                    break;
            }
        }
    }
}
