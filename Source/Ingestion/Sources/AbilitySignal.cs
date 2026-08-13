// Ability ingestion signal — the impure capture+emit half of the "ability activated" source
// (Ability.Activate, both the local-map and world-map overloads). Replaces the old
// DiaryGameComponent.RecordAbilityUsed. A solo source: one entry for the caster, sampled by a
// cooldown-weighted chance so fast repeatable powers rarely become diary pages while rare powers
// carry more weight. This class supplies that native chance to the shared frequency gate; the pure
// AbilityEventData reducer owns only semantic validity. Exact XML-owned Ideology routes with a later
// canonical interaction/ritual are therefore dropped before the shared gate draws.
//
// The dedup key is tick-stamped and includes the cleaned target label, so it lives here (it needs
// DiaryLineCleaner) rather than on the pure payload. Pure decision + game-context + the
// cooldown-weighted chance math live in Source/Capture/Events/AbilityEventData.cs. A generic route is
// suppressed only while its XML-named downstream group is effectively enabled for this player.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one successful ability activation and emits it as a solo diary event for the caster.
    /// Built by <see cref="AbilityActivateLocalPatch"/> / <see cref="AbilityActivateGlobalPatch"/> and
    /// submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class AbilitySignal : DiarySignal
    {
        private static readonly FieldInfo AbilityPawnField = AccessTools.Field(typeof(Ability), "pawn");

        private Pawn pawn;
        private Pawn targetPawn;
        private DiaryInteractionGroupDef group;
        private string defName;
        private string label;
        private string category;
        private string targetLabel;
        private int cooldownTicks;
        private float recordChance;
        private string dedupKey;
        private AbilityEventData payload;

        /// <summary>Local-map activation (Ability.Activate(LocalTargetInfo, LocalTargetInfo)).</summary>
        public AbilitySignal(Ability ability, LocalTargetInfo target, LocalTargetInfo destination)
        {
            Pawn tp = target.Thing as Pawn;
            string tl = TargetLabel(target.Thing);
            if (string.IsNullOrWhiteSpace(tl))
            {
                tl = TargetLabel(destination.Thing);
            }

            Init(ability, tp, tl);
        }

        /// <summary>World-map activation (Ability.Activate(GlobalTargetInfo)).</summary>
        public AbilitySignal(Ability ability, GlobalTargetInfo target)
        {
            Init(ability, target.Thing as Pawn, TargetLabel(target.Thing));
        }

        private void Init(Ability ability, Pawn targetPawn, string targetLabel)
        {
            if (!DiaryGameComponent.GamePlaying || ability == null || ability.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            Pawn caster = AbilityPawnField?.GetValue(ability) as Pawn;
            if (caster == null || !DiaryGameComponent.IsDiaryEligible(caster))
            {
                return;
            }

            AbilityDef def = ability.def;
            string name = def.defName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string cat = AbilityCategory(def);
            DiaryInteractionGroupDef classified = InteractionGroups.ClassifyAbility(AbilityClassifierKey(def, cat));
            if (classified == null || !PawnDiaryMod.Settings.IsGroupEnabled(classified.defName))
            {
                return;
            }

            int cooldown = AbilityCooldownTicks(ability, def);
            float chance = AbilityEventData.CooldownWeightedChance(
                cooldown,
                DiaryTuning.Current.abilityUseMinChance,
                DiaryTuning.Current.abilityUseMaxChance,
                DiaryTuning.Current.abilityUseReferenceCooldownTicks);

            this.pawn = caster;
            this.targetPawn = targetPawn;
            this.group = classified;
            this.defName = name;
            this.label = AbilityLabel(def);
            this.category = cat;
            this.targetLabel = targetLabel;
            this.cooldownTicks = cooldown;
            this.recordChance = chance;
            BeliefPolicySnapshot beliefPolicy = DiaryBeliefPolicy.Snapshot();
            string downstreamGroup = BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                BeliefCanonicalEventSourceTokens.Ability,
                name,
                ModsConfig.IdeologyActive,
                beliefPolicy.enabled,
                beliefPolicy.canonicalEventOwnershipRules);
            bool downstreamCovered = downstreamGroup.Length > 0
                && PawnDiaryMod.Settings.IsGroupEnabled(downstreamGroup);

            payload = new AbilityEventData
            {
                PawnId = caster.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = name,
                Label = label,
                Category = cat,
                TargetLabel = targetLabel,
                CooldownTicks = cooldown,
                RecordChance = chance,
                DownstreamCovered = downstreamCovered,
            };

            dedupKey = "ability|" + caster.GetUniqueLoadID() + "|" + name + "|"
                + DiaryLineCleaner.CleanLine(targetLabel) + "|" + Find.TickManager.TicksGame;
        }

        public override DiaryEventData Payload => payload;

        // Caster eligibility + the user's group toggle already passed in Init. The exact classified
        // group and native cooldown chance let the shared dispatcher own the only probability draw.
        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true,
                frequencyGroup: group,
                nativeCaptureChance: recordChance);
        }

        /// <summary>
        /// A frequency miss is a sampling miss, so another activation may retry after rejection.
        /// </summary>
        public override bool FrequencyRejectionConsumesDedup => false;

        public override string DedupKey => dedupKey ?? string.Empty;

        public override int DedupWindowTicks => DiaryTuning.Current.abilityDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string context = AbilityEventData.BuildGameContext(
                defName, label, category, targetLabel, cooldownTicks, recordChance);
            string text = string.IsNullOrWhiteSpace(targetLabel)
                ? "PawnDiary.Event.AbilityUsed".Translate(pawn.LabelShortCap, label).Resolve()
                : "PawnDiary.Event.AbilityUsedOn".Translate(pawn.LabelShortCap, label, targetLabel).Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);

            DiaryEvent abilityEvent = sink.AddSoloEvent(pawn, targetPawn, defName, label, text, instruction, context);
            if (abilityEvent == null)
            {
                return;
            }

            sink.QueueSolo(abilityEvent, DiaryEvent.InitiatorRole);
        }

        // ── Helpers moved verbatim from the old DiaryGameComponent.Abilities.cs ──

        private static string AbilityClassifierKey(AbilityDef def, string category)
        {
            string key = def.defName;
            if (!string.IsNullOrWhiteSpace(category))
            {
                key += ";" + category;
            }

            if (def.IsPsycast)
            {
                key += ";Psycast";
            }

            key += def.hostile ? ";Hostile" : ";Utility";
            return key;
        }

        private static string AbilityCategory(AbilityDef def)
        {
            string category = def.category?.defName;
            if (string.IsNullOrWhiteSpace(category))
            {
                category = def.groupDef?.defName;
            }

            if (string.IsNullOrWhiteSpace(category) && def.IsPsycast)
            {
                category = "Psycast";
            }

            return string.IsNullOrWhiteSpace(category) ? "unknown" : DiaryLineCleaner.CleanLine(category);
        }

        private static string AbilityLabel(AbilityDef def)
        {
            string label = DiaryLineCleaner.CleanLine(def == null ? null : def.LabelCap.Resolve());
            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryLineCleaner.CleanLine(def?.label);
            }

            return string.IsNullOrWhiteSpace(label) ? def?.defName ?? "ability" : label;
        }

        private static int AbilityCooldownTicks(Ability ability, AbilityDef def)
        {
            int cooldown = Math.Max(0, ability.CooldownTicksTotal);
            if (cooldown > 0 || def == null)
            {
                return cooldown;
            }

            return Math.Max(0, (def.cooldownTicksRange.min + def.cooldownTicksRange.max) / 2);
        }

        private static string TargetLabel(Thing thing)
        {
            if (thing == null)
            {
                return string.Empty;
            }

            return DiaryLineCleaner.CleanLine(thing.LabelShortCap);
        }
    }
}
