// Pawn progression scanner. This watches slow-changing pawn state that does not always have a clean
// one-shot vanilla hook: passion skill milestones, psylink levels, xenotype changes, and royal-title
// changes. It stores only scanner baselines/highest values on PawnDiaryRecord; existing diary pages
// remain the history layer used by reflections.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Periodically scans eligible colonists for future progression changes. First scan for a pawn
        /// baselines current state and emits nothing, preventing old-save catch-up bursts.
        /// </summary>
        private void ScanPawnProgressionsForDiaryEvents()
        {
            if (PawnDiaryMod.Settings == null || !DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression))
            {
                return;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (!IsDiaryEligible(pawn))
                {
                    continue;
                }

                PawnDiaryRecord diary = FindDiary(pawn, true);
                if (diary == null)
                {
                    continue;
                }

                PawnProgressionState state = diary.EnsureProgressionState();
                bool baseline = state.baselineProgressionOnNextScan;
                ScanPassionSkillMilestones(pawn, state, baseline);
                ScanPsylinkLevel(pawn, state, baseline);
                ScanXenotypeChange(pawn, state, baseline);
                ScanRoyalTitleChange(pawn, state, baseline);
                if (baseline)
                {
                    state.baselineProgressionOnNextScan = false;
                }
            }
        }

        private void ScanPassionSkillMilestones(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            if (pawn?.skills == null)
            {
                return;
            }

            List<SkillDef> skillDefs = DefDatabase<SkillDef>.AllDefsListForReading;
            for (int i = 0; i < skillDefs.Count; i++)
            {
                SkillDef skillDef = skillDefs[i];
                SkillRecord skill = pawn.skills.GetSkill(skillDef);
                if (skill == null)
                {
                    continue;
                }

                bool hasPassion = skill.passion != Passion.None;
                string skillDefName = skillDef?.defName ?? string.Empty;
                int previous = state.HighestSkillMilestone(skillDefName);
                ProgressionMilestoneDecision decision = ProgressionMilestonePolicy.EvaluateSkillMilestone(
                    skill.Level,
                    hasPassion,
                    DiaryTuning.Current.progressionSkillMilestones,
                    previous,
                    baseline);

                if (decision.newHighestMilestone != previous)
                {
                    state.SetSkillMilestone(skillDefName, decision.newHighestMilestone);
                }

                if (!decision.shouldEmit)
                {
                    continue;
                }

                string skillLabel = CleanLabel(skillDef?.label, skillDefName);
                string passionToken = skill.passion == Passion.Major ? "major" : "minor";
                string passionLabel = (skill.passion == Passion.Major
                    ? "PawnDiary.Event.ProgressionPassionMajor"
                    : "PawnDiary.Event.ProgressionPassionMinor").Translate().Resolve();
                string extraContext = "skill=" + skillLabel
                    + "; skill_level=" + decision.milestoneToEmit
                    + "; previous_skill_milestone=" + previous
                    + "; passion=" + passionToken;
                ProgressionEventData data = ProgressionData(
                    pawn,
                    ProgressionEventData.SkillMilestoneDefName,
                    "skill",
                    skillLabel,
                    previous.ToString(),
                    decision.milestoneToEmit.ToString(),
                    extraContext);
                string label = "PawnDiary.Event.ProgressionSkillLabel"
                    .Translate(skillLabel, decision.milestoneToEmit).Resolve();
                string text = "PawnDiary.Event.ProgressionSkillText"
                    .Translate(pawn.LabelShortCap, skillLabel, decision.milestoneToEmit, passionLabel).Resolve();
                DispatchProgression(pawn, data, label, text, majorArcCandidate: false);
            }
        }

        private void ScanPsylinkLevel(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            int currentLevel = CurrentPsylinkLevel(pawn);
            int previousLevel = state.highestPsylinkLevelRecorded;
            ProgressionLevelDecision decision = ProgressionMilestonePolicy.EvaluateLevelIncrease(
                currentLevel,
                previousLevel,
                baseline,
                1,
                6);
            state.highestPsylinkLevelRecorded = decision.newHighestLevel;
            if (!decision.shouldEmit)
            {
                return;
            }

            string extraContext = "psylink_level=" + decision.levelToEmit
                + "; previous_psylink_level=" + previousLevel;
            string psylinkLabel = "PawnDiary.Event.ProgressionPsylinkLabel"
                .Translate(decision.levelToEmit).Resolve();
            ProgressionEventData data = ProgressionData(
                pawn,
                ProgressionEventData.PsylinkLevelDefName,
                "psylink",
                psylinkLabel,
                previousLevel.ToString(),
                decision.levelToEmit.ToString(),
                extraContext);
            string label = psylinkLabel;
            string text = "PawnDiary.Event.ProgressionPsylinkText"
                .Translate(pawn.LabelShortCap, decision.levelToEmit).Resolve();
            DispatchProgression(pawn, data, label, text, majorArcCandidate: IsMajorArcPsylinkLevel(decision.levelToEmit));
        }

        private void ScanXenotypeChange(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            string currentDef = DlcContext.XenotypeDefName(pawn);
            string currentLabel = DlcContext.XenotypeLabel(pawn);
            if (baseline)
            {
                state.lastObservedXenotypeDefName = currentDef;
                state.lastObservedXenotypeLabel = currentLabel;
                return;
            }

            if (string.IsNullOrWhiteSpace(currentDef)
                && string.IsNullOrWhiteSpace(state.lastObservedXenotypeDefName))
            {
                state.lastObservedXenotypeLabel = currentLabel;
                return;
            }

            if (!Changed(state.lastObservedXenotypeDefName, state.lastObservedXenotypeLabel, currentDef, currentLabel))
            {
                return;
            }

            string previousLabel = string.IsNullOrWhiteSpace(state.lastObservedXenotypeLabel)
                ? "none"
                : state.lastObservedXenotypeLabel;
            state.lastObservedXenotypeDefName = currentDef;
            state.lastObservedXenotypeLabel = currentLabel;

            bool majorArcXenotype = IsMajorArcXenotype(currentDef);
            string extraContext = "previous_xenotype=" + previousLabel
                + "; xenotype=" + currentLabel
                + "; xenotype_def=" + currentDef
                + "; major_xenotype=" + (majorArcXenotype ? "true" : "false");
            ProgressionEventData data = ProgressionData(
                pawn,
                ProgressionEventData.XenotypeChangedDefName,
                "xenotype",
                currentLabel,
                previousLabel,
                currentLabel,
                extraContext);
            string label = "PawnDiary.Event.ProgressionXenotypeLabel"
                .Translate(currentLabel).Resolve();
            string text = "PawnDiary.Event.ProgressionXenotypeText"
                .Translate(pawn.LabelShortCap, previousLabel, currentLabel).Resolve();
            DispatchProgression(pawn, data, label, text, majorArcCandidate: majorArcXenotype);
        }

        private void ScanRoyalTitleChange(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            string currentDef = DlcContext.RoyalTitleDefName(pawn);
            string currentLabel = DlcContext.RoyalTitleLabel(pawn);
            if (baseline)
            {
                state.lastObservedRoyalTitleDefName = currentDef;
                state.lastObservedRoyalTitleLabel = currentLabel;
                return;
            }

            if (!Changed(state.lastObservedRoyalTitleDefName, state.lastObservedRoyalTitleLabel, currentDef, currentLabel))
            {
                return;
            }

            string previousLabel = string.IsNullOrWhiteSpace(state.lastObservedRoyalTitleLabel)
                ? "none"
                : state.lastObservedRoyalTitleLabel;
            state.lastObservedRoyalTitleDefName = currentDef;
            state.lastObservedRoyalTitleLabel = currentLabel;

            if (string.IsNullOrWhiteSpace(currentLabel))
            {
                return;
            }

            string extraContext = "previous_title=" + previousLabel
                + "; title=" + currentLabel
                + "; title_def=" + currentDef;
            ProgressionEventData data = ProgressionData(
                pawn,
                ProgressionEventData.RoyalTitleChangedDefName,
                "royal_title",
                currentLabel,
                previousLabel,
                currentLabel,
                extraContext);
            string label = "PawnDiary.Event.ProgressionRoyalTitleLabel"
                .Translate(currentLabel).Resolve();
            string text = "PawnDiary.Event.ProgressionRoyalTitleText"
                .Translate(pawn.LabelShortCap, previousLabel, currentLabel).Resolve();
            DispatchProgression(pawn, data, label, text, majorArcCandidate: false);
        }

        private bool DispatchProgression(Pawn pawn, ProgressionEventData data, string label, string text,
            bool majorArcCandidate)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyProgression(data.DefName);
            bool userEnabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            bool signalEnabled = DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression);
            string instruction = InteractionGroups.InstructionForProgression(group);
            string gameContext = ProgressionEventData.BuildGameContext(
                data.DefName,
                data.Kind,
                data.Label,
                data.PreviousValue,
                data.NewValue,
                data.Context);
            bool emitted = Dispatch(new ProgressionSignal(
                data,
                pawn,
                label,
                text,
                instruction,
                gameContext,
                IsDiaryEligible(pawn),
                userEnabled,
                signalEnabled));
            if (emitted && majorArcCandidate)
            {
                ConsiderArcReflectionAfterMajorEvent(pawn);
            }

            return emitted;
        }

        private static ProgressionEventData ProgressionData(Pawn pawn, string defName, string kind,
            string label, string previousValue, string newValue, string context)
        {
            return new ProgressionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = defName,
                Kind = kind,
                Label = label,
                PreviousValue = previousValue,
                NewValue = newValue,
                Context = context,
                AlreadyRecorded = false
            };
        }

        private static int CurrentPsylinkLevel(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return 0;
            }

            int highest = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!MatchesDefName(DiaryTuning.Current.psylinkHediffDefNames, hediff?.def?.defName))
                {
                    continue;
                }

                int level;
                if (TryGetPsylinkLevel(hediff, out level) && level > highest)
                {
                    highest = level;
                }
            }

            return highest;
        }

        private static bool TryGetPsylinkLevel(Hediff hediff, out int level)
        {
            level = 0;
            if (hediff == null)
            {
                return false;
            }

            if (TryReadIntMember(hediff, "level", out level)
                || TryReadIntMember(hediff, "Level", out level))
            {
                level = ClampPsylinkLevel(level);
                return level > 0;
            }

            try
            {
                if (hediff.def?.stages != null && hediff.def.stages.Count > 0)
                {
                    level = ClampPsylinkLevel(hediff.CurStageIndex + 1);
                    return level > 0;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryReadIntMember(object instance, string memberName, out int value)
        {
            value = 0;
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, flags);
            if (field != null && TryCoerceInt(field.GetValue(instance), out value))
            {
                return true;
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            return property != null && property.GetIndexParameters().Length == 0
                && TryCoerceInt(property.GetValue(instance, null), out value);
        }

        private static bool TryCoerceInt(object raw, out int value)
        {
            value = 0;
            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is float)
            {
                value = (int)(float)raw;
                return true;
            }

            if (raw is double)
            {
                value = (int)(double)raw;
                return true;
            }

            return raw != null && int.TryParse(raw.ToString(), out value);
        }

        private static int ClampPsylinkLevel(int level)
        {
            if (level < 1)
            {
                return 0;
            }

            return level > 6 ? 6 : level;
        }

        private static bool Changed(string previousDef, string previousLabel, string currentDef, string currentLabel)
        {
            if (!string.IsNullOrWhiteSpace(previousDef) || !string.IsNullOrWhiteSpace(currentDef))
            {
                return !string.Equals(previousDef ?? string.Empty, currentDef ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

            return !string.Equals(previousLabel ?? string.Empty, currentLabel ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesDefName(List<string> defNames, string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || defNames == null)
            {
                return false;
            }

            for (int i = 0; i < defNames.Count; i++)
            {
                if (string.Equals(defNames[i], defName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMajorArcPsylinkLevel(int level)
        {
            int clamped = ClampPsylinkLevel(level);
            return MeetsArcMajorSeverity((clamped * 100) / 6);
        }

        private static bool IsMajorArcXenotype(string defName)
        {
            return MatchesDefName(DiaryTuning.Current.arcReflectionMajorXenotypeDefNames, defName)
                && MeetsArcMajorSeverity(100);
        }

        private static bool MeetsArcMajorSeverity(int severity)
        {
            return severity >= Math.Max(0, DiaryTuning.Current.arcReflectionMajorSeverityThreshold);
        }

        private static string CleanLabel(string label, string fallback)
        {
            string cleaned = DiaryLineCleaner.CleanLine(label);
            return string.IsNullOrWhiteSpace(cleaned) ? (fallback ?? string.Empty) : cleaned;
        }
    }
}
