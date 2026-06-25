// Ability-use events. RimWorld routes psycasts, permits, powers, and many DLC/modded abilities
// through Ability.Activate. This adapter records only successful activations, then samples them by
// cooldown so fast repeatable powers rarely become diary pages while rare powers have more weight.
using System;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private static readonly FieldInfo AbilityPawnField = AccessTools.Field(typeof(Ability), "pawn");

        /// <summary>
        /// Records a successful local-map ability activation, if the cooldown-weighted sample passes.
        /// </summary>
        public void RecordAbilityUsed(Ability ability, LocalTargetInfo target, LocalTargetInfo destination)
        {
            Pawn targetPawn = target.Thing as Pawn;
            string targetLabel = TargetLabel(target.Thing);
            if (string.IsNullOrWhiteSpace(targetLabel))
            {
                targetLabel = TargetLabel(destination.Thing);
            }

            RecordAbilityUsed(ability, targetPawn, targetLabel);
        }

        /// <summary>
        /// Records a successful world-targeted ability activation, if eligible and sampled in.
        /// </summary>
        public void RecordAbilityUsed(Ability ability, GlobalTargetInfo target)
        {
            Pawn targetPawn = target.Thing as Pawn;
            RecordAbilityUsed(ability, targetPawn, TargetLabel(target.Thing));
        }

        private void RecordAbilityUsed(Ability ability, Pawn targetPawn, string targetLabel)
        {
            if (!CanRecordGameplayEventNow() || ability == null || ability.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            Pawn pawn = AbilityPawnField?.GetValue(ability) as Pawn;
            if (pawn == null || !IsDiaryEligible(pawn))
            {
                return;
            }

            AbilityDef def = ability.def;
            string defName = def.defName;
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            string category = AbilityCategory(def);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyAbility(AbilityClassifierKey(def, category));
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            string dedupKey = "ability|" + pawn.GetUniqueLoadID() + "|" + defName + "|"
                + DiaryContextBuilder.CleanLine(targetLabel) + "|" + Find.TickManager.TicksGame;
            if (IsRecentlyRecorded(recentAbilityEvents, dedupKey, DiaryTuning.Current.abilityDedupTicks))
            {
                return;
            }

            int cooldownTicks = AbilityCooldownTicks(ability, def);
            float recordChance = AbilityEventData.CooldownWeightedChance(
                cooldownTicks,
                DiaryTuning.Current.abilityUseMinChance,
                DiaryTuning.Current.abilityUseMaxChance,
                DiaryTuning.Current.abilityUseReferenceCooldownTicks);

            AbilityEventData data = new AbilityEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = defName,
                Label = AbilityLabel(def),
                Category = category,
                TargetLabel = targetLabel,
                CooldownTicks = cooldownTicks,
                RecordChance = recordChance,
                Roll = Rand.Value,
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: true,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Ability);
            CaptureDecision decision = spec != null ? spec.Decide(data, ctx) : CaptureDecision.Drop;
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string label = AbilityLabel(def);
            string context = AbilityEventData.BuildGameContext(
                defName,
                label,
                category,
                targetLabel,
                cooldownTicks,
                recordChance);
            string text = string.IsNullOrWhiteSpace(targetLabel)
                ? "PawnDiary.Event.AbilityUsed".Translate(pawn.LabelShortCap, label).Resolve()
                : "PawnDiary.Event.AbilityUsedOn".Translate(pawn.LabelShortCap, label, targetLabel).Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);

            DiaryEvent abilityEvent = AddSoloEvent(pawn, targetPawn, defName, label, text, instruction, context);
            if (abilityEvent == null)
            {
                return;
            }

            QueueLlmRewrite(abilityEvent, DiaryEvent.InitiatorRole);
            MarkRecentlyRecorded(recentAbilityEvents, dedupKey, DiaryTuning.Current.abilityDedupTicks);
        }

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

            return string.IsNullOrWhiteSpace(category) ? "unknown" : DiaryContextBuilder.CleanLine(category);
        }

        private static string AbilityLabel(AbilityDef def)
        {
            string label = DiaryContextBuilder.CleanLine(def == null ? null : def.LabelCap.Resolve());
            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryContextBuilder.CleanLine(def?.label);
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

            return DiaryContextBuilder.CleanLine(thing.LabelShortCap);
        }
    }
}
