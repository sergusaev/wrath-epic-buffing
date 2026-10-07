using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Parts;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuffIt2TheLimit {

#if !KINGMAKER
    // Blueprint id as a Guid; Kingmaker stores it as a string (see the Kingmaker host).
    static class BlueprintIds {
        public static Guid Gid(this SimpleBlueprint blueprint) => blueprint.AssetGuid.m_Guid;
    }
#endif

    public class CasterCacheEntry {
        public Ability PowerfulChange;
        public Ability ShareTransmutation;
        public Ability ReservoirCLBuff;
    }

    public class AbilityCache {

        public static Dictionary<string, CasterCacheEntry> CasterCache = new();

        public static void Revalidate() {
            Main.Verbose("Revalidating Caster Cache");
            CasterCache.Clear();
            foreach (var u in Bubble.ConfigGroup) {
#if KINGMAKER
                // The arcanist abilities are WotR-only.
                CasterCache[u.UniqueId] = new CasterCacheEntry();
                continue;
#endif
                var entry = new CasterCacheEntry {
                    PowerfulChange = u.Abilities.GetAbility(BubbleBlueprints.PowerfulChange),
                    ShareTransmutation = u.Abilities.GetAbility(BubbleBlueprints.ShareTransmutation),
                    ReservoirCLBuff = u.Abilities.GetAbility(BubbleBlueprints.ReservoirBaseAbility)
                };
                CasterCache[u.UniqueId] = entry;
            }
        }
    }

    [Flags]
    public enum HideReason {
        Short = 1,
        Blacklisted = 2,
    };


    public class CasterKey {
        [JsonProperty]
        public string Name;
        [JsonProperty]
        public Guid Spellbook;
        [JsonProperty]
        public BuffSourceType SourceType;

        public override bool Equals(object obj) {
            return obj is CasterKey key &&
                   Name == key.Name &&
                   Spellbook.Equals(key.Spellbook) &&
                   SourceType == key.SourceType;
        }

        public override int GetHashCode() {
            int hashCode = 1282151259;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + Spellbook.GetHashCode();
            hashCode = hashCode * -1521134295 + SourceType.GetHashCode();
            return hashCode;
        }
    }
     public enum Category {
        Buff,
        Ability,
        Equipment,
        Song,
        Toggle
    }

    public enum BuffGroup {
        Long,
        Quick,
        Important,
        // Slots for groups created in the buff menu (PadGroups); unused slots never appear.
        Custom1, Custom2, Custom3, Custom4, Custom5, Custom6,
        Custom7, Custom8, Custom9, Custom10, Custom11, Custom12,
    }

    public enum BuffSourceType {
        Spell,
        Scroll,
        Potion,
        Equipment,
        Song,
        Activatable
    }

    public enum UmdMode {
        SafeOnly,
        AllowIfPossible,
        AlwaysTry
    }

    public enum SourcePriority {
        SpellsScrollsPotions = 0,
        SpellsPotionsScrolls = 1,
        ScrollsSpellsPotions = 2,
        ScrollsPotionsSpells = 3,
        PotionsSpellsScrolls = 4,
        PotionsScrollsSpells = 5,
    }

    static class Bubble {
        public static List<UnitEntityData> Group = new();
        public static List<UnitEntityData> ConfigGroup = new();
        public static Dictionary<string, UnitEntityData> GroupById = new();
        public static bool ShowReserve = false;

        public static void RefreshGroup() {
#if KINGMAKER
            // Kingmaker: the party plus each member's animal companion; no reserve list.
            var party = new List<UnitEntityData>(Game.Instance.Player.Party);
            foreach (var unit in Game.Instance.Player.Party) {
                var pet = unit.Descriptor.Pet;
                if (pet != null && pet.IsInGame && !party.Contains(pet))
                    party.Add(pet);
            }
            Group = party;
            ConfigGroup = party;
#else
            var baseGroup = Game.Instance.SelectionCharacter.ActualGroup;
            var result = new List<UnitEntityData>(baseGroup);

            foreach (var unit in baseGroup) {
                var petMaster = unit.Get<UnitPartPetMaster>();
                if (petMaster == null) continue;

                var pets = new List<UnitEntityData>();
                foreach (var petRef in petMaster.Pets) {
                    var pet = petRef.Entity;
                    if (pet != null && pet.IsInGame && !result.Contains(pet)) {
                        pets.Add(pet);
                    }
                }
                pets.Sort((a, b) => string.Compare(a.UniqueId, b.UniqueId, StringComparison.Ordinal));
                result.AddRange(pets);
            }

            Group = result;

            if (ShowReserve) {
                var config = new List<UnitEntityData>(result);
                var activeIds = new HashSet<string>(result.Select(u => u.UniqueId));

                foreach (var unit in Game.Instance.Player.RemoteCompanions) {
                    if (activeIds.Contains(unit.UniqueId)) continue;
                    if (unit.Get<UnitPartPet>() != null) continue; // Pets added via master

                    config.Add(unit);

                    var petMaster = unit.Get<UnitPartPetMaster>();
                    if (petMaster == null) continue;

                    var pets = new List<UnitEntityData>();
                    foreach (var petRef in petMaster.Pets) {
                        var pet = petRef.Entity;
                        if (pet != null && !activeIds.Contains(pet.UniqueId) && !config.Contains(pet)) {
                            pets.Add(pet);
                        }
                    }
                    pets.Sort((a, b) => string.Compare(a.UniqueId, b.UniqueId, StringComparison.Ordinal));
                    config.AddRange(pets);
                }
                ConfigGroup = config;
            } else {
                ConfigGroup = result;
            }

#endif

            GroupById.Clear();
            foreach (var u in ConfigGroup) {
                GroupById[u.UniqueId] = u;
            }
        }
    }

    // Countdowns run on game time, in and out of combat: a toggle without
    // DeactivateIfCombatEnded (Blazing Rondo) keeps running — and spending performance rounds,
    // ActivatableAbilityResourceLogic.OnNewRound has no combat check — after a fight that ended
    // before the limit, so the limit has to be able to fire outside combat too.
    internal class RoundLimitHandler : IAreaActivationHandler {
        private const double SecondsPerRound = 6.0;
        // Keyed per performer, not per blueprint: two party members running the same toggle
        // each get their own countdown instead of the later activation resetting (and the
        // expiry switching off) both. Double, not float: Player.GameTime counts the whole
        // campaign, and a float of 10^7+ seconds only resolves whole seconds or worse.
        private readonly Dictionary<(string UnitId, Guid Guid), double> activationTimes = new();

        public void TrackActivation(UnitEntityData caster, Guid guid) {
            if (caster == null) return;
            double gameTime = Game.Instance.Player.GameTime.TotalSeconds;
            Main.Verbose($"[RoundLimit] TrackActivation: {caster.CharacterName} guid={guid}, gameTime={gameTime:F1}s");
            activationTimes[(caster.UniqueId, guid)] = gameTime;
        }

        // For a toggle found already running: keeps a countdown that is still going (a short
        // combat gap must not restart it) and only starts one when there is none.
        public bool TrackIfUntracked(UnitEntityData caster, Guid guid) {
            if (caster == null || activationTimes.ContainsKey((caster.UniqueId, guid))) return false;
            TrackActivation(caster, guid);
            return true;
        }

        /// <summary>
        /// Called every frame from BubbleBuffGlobalController.Update().
        /// Uses game time to track elapsed rounds.
        /// </summary>
        public void Tick() {
            if (activationTimes.Count == 0) return;

            double gameTime = Game.Instance.Player.GameTime.TotalSeconds;

            var controller = GlobalBubbleBuffer.Instance?.SpellbookController;
            if (controller?.state?.BuffList == null) return;

            var toRemove = new List<(string, Guid)>();
            foreach (var kvp in activationTimes) {
                var (unitId, guid) = kvp.Key;
                double timePassed = gameTime - kvp.Value;

                var buff = controller.state.BuffList.FirstOrDefault(b =>
                    b.IsActivatable && b.ActivatableSource?.Blueprint.Gid() == guid);
                var provider = buff?.CasterQueue.FirstOrDefault(p => p.who?.UniqueId == unitId);

                if (buff == null || provider == null || buff.DeactivateAfterRounds <= 0 || timePassed < 0) {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Via the resolver: an ActivationDisable-locked parent (Shifter's Fury)
                // is never itself IsOn, so a direct src.IsOn = false would be a no-op
                // and the toggle would outlive its round limit.
                var src = provider.ActivatableSource ?? buff.ActivatableSource;
                var running = BuffExecutor.ResolveDeactivationTargets(provider.who, src).ToList();
                // Already off (engine combat-end stop, rest, manual toggle): drop the countdown,
                // otherwise it would later switch off a run the player started by hand.
                if (running.Count == 0) {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                double limitSeconds = buff.DeactivateAfterRounds * SecondsPerRound;
                if (timePassed >= limitSeconds) {
                    Main.Log($"Round limit reached for {buff.Name} on {provider.who.CharacterName}: {timePassed:F1}s elapsed (limit={buff.DeactivateAfterRounds} rounds = {limitSeconds:F0}s), deactivating");
                    foreach (var target in running)
                        target.IsOn = false;
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove) {
                activationTimes.Remove(key);
            }
        }

        // Game time belongs to the loaded save: after a load or area change the stored
        // timestamps are meaningless. A song still running is picked up again by the next
        // combat start (Phase 0a in ExecuteCombatStart).
        public void OnAreaActivated() {
            activationTimes.Clear();
        }
    }

    static class BubbleBlueprints {
        public static BlueprintAbility ShareTransmutation => Resources.GetBlueprint<BlueprintAbility>("749567e4f652852469316f787921e156");
        public static BlueprintAbility PowerfulChange => Resources.GetBlueprint<BlueprintAbility>("a45f3dae9c64ec848b35f85568f4b220");
        public static BlueprintAbility ReservoirBaseAbility => Resources.GetBlueprint<BlueprintAbility>("91295893ae9fdfb4b8936a93eff019df");
        public static BlueprintArchetype PhantasmalMageArchetype => Resources.GetBlueprint<BlueprintArchetype>("e9d0ee69305049fe8400a066010dbcd1");
    }
}
