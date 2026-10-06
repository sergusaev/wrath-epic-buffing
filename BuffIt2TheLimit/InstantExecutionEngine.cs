using BuffIt2TheLimit.Extensions;
using BuffIt2TheLimit.Handlers;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuffIt2TheLimit {
    public class InstantExecutionEngine : IBuffExecutionEngine {
        
        public const int BATCH_SIZE = 8;
        public const float DELAY = 0.05f;

        private RuleCastSpell Cast(CastTask task, List<EngineCastingHandler> handlers) {
            EngineCastingHandler handler = null;
            try {
                // Subscribe to the RuleCastSpell event that will be executed by the trigger
                handler = new EngineCastingHandler(task, true);
                handlers.Add(handler);
                EventBus.Subscribe(handler);

                // Trigger the RuleCastSpell
                return Rulebook.Trigger<RuleCastSpell>(new(task.SpellToCast, task.Target));
            }
            catch (Exception ex) {
                Main.Error(ex, "Instant Engine Casting");
                handler?.EnsureFinalized();
                return null;
            }
        }

        public IEnumerator CreateSpellCastRoutine(List<CastTask> tasks) {
            var handlers = new List<EngineCastingHandler>();
            try {
                var tasks_WithRetentions = tasks.Where(x => x.Retentions.Any);
                var batches_WithoutRetentions = tasks.Where(x => !x.Retentions.Any).Chunk(BATCH_SIZE);

                // Batches without retentions
                foreach (var batch in batches_WithoutRetentions) {
                    batch.ForEach(task => {
                        Cast(task, handlers);
                    });

                    yield return new WaitForSecondsRealtime(DELAY);
                }

                // Batches with retentions
                foreach (var task in tasks_WithRetentions) {
                    Cast(task, handlers);

                    yield return new WaitForSecondsRealtime(DELAY);
                }

                // Grace period: give in-flight AbilityExecutionProcesses a moment to end
                // and self-finalize via HandleExecutionProcessEnd before the sweep below
                // rolls back shared state.
                for (int i = 0; i < 30 && handlers.Any(h => !h.IsFinalized); i++) {
                    yield return null;
                }
            } finally {
                // Sweep handlers the game never finalized — instant Rulebook.Trigger
                // casts don't always get an AbilityExecutionProcess, and cancelled
                // casts never reach HandleExecutionProcessEnd. EnsureFinalized is
                // idempotent, so double-finalizing the normal path is harmless.
                foreach (var handler in handlers) {
                    handler.EnsureFinalized();
                }
            }
        }
    }
}