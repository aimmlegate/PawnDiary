// Work diary events. RimWorld work is long-running and repetitive, so there is no clean one-shot
// hook like PlayLog.Add or TaleRecorder.RecordTale. This file samples colonists' current jobs every
// few in-game hours, applies persistent cooldowns from existing diary history, then makes a small
// weighted random roll. New to C#/RimWorld? See AGENTS.md ("WorkTypeDef", "WorkGiverDef").
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;
using Verse.AI;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Periodically samples eligible colonists' current work and sometimes records a solo work
        /// diary event. Cooldowns are read from saved DiaryEvents, so loading a save does not reset
        /// "recently wrote about this work" memory.
        /// </summary>
        private void ScanPawnWorkForDiaryEvents()
        {
            if (PawnDiaryMod.Settings == null || !DiarySignalPolicies.Enabled(DiarySignalPolicies.Work))
            {
                return;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                TryRecordCurrentWork(colonists[i]);
            }
        }

        /// <summary>
        /// Records one current-work moment if the pawn is doing a real work job, the work group is
        /// enabled, cooldowns allow it, and the weighted random roll succeeds.
        /// </summary>
        private void TryRecordCurrentWork(Pawn pawn)
        {
            WorkGiverDef workGiverDef;
            WorkTypeDef workTypeDef;
            if (!TryGetCurrentWork(pawn, out workGiverDef, out workTypeDef))
            {
                return;
            }

            bool ignoredWorkType = ShouldIgnoreWorkType(workTypeDef);
            WorkMood mood = ignoredWorkType ? default(WorkMood) : ClassifyWorkMood(pawn, workTypeDef);
            string eventDefName = ignoredWorkType
                ? WorkEventData.RoutineDefName
                : WorkEventDefName(workTypeDef, mood);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyWork(eventDefName);
            bool eligible = IsDiaryEligible(pawn);
            bool userEnabled = group != null && PawnDiaryMod.Settings.IsWorkEnabled(group);
            bool signalEnabled = DiarySignalPolicies.Enabled(DiarySignalPolicies.Work);
            bool canRollWorkEvent = eligible && userEnabled && signalEnabled && !ignoredWorkType;

            int cooldownTicks = Math.Max(0, DiarySignalPolicies.WorkSameTypeCooldownTicks);
            bool sameWorkCooldownClear = false;
            if (canRollWorkEvent)
            {
                sameWorkCooldownClear = !HasRecentWorkEvent(pawn, workTypeDef.defName, cooldownTicks, true);
            }

            bool passedChanceRoll = false;
            if (canRollWorkEvent && sameWorkCooldownClear)
            {
                float chance = WorkDiaryChance(pawn, workTypeDef, mood);
                if (HasRecentWorkEvent(pawn, workTypeDef.defName, cooldownTicks, false))
                {
                    chance *= Math.Max(0f, DiarySignalPolicies.WorkRecentDifferentTypeMultiplier);
                }
                passedChanceRoll = Rand.Value <= Math.Min(1f, Math.Max(0f, chance));
            }

            string moodImpact = mood.IsPositive ? MoodImpact.Positive : mood.IsNegative ? MoodImpact.Negative : MoodImpact.Neutral;
            WorkEventData data = new WorkEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = eventDefName,
                WorkTypeDefName = workTypeDef.defName,
                WorkGiverDefName = workGiverDef?.defName,
                MoodImpact = moodImpact,
                HasCurrentWork = true,
                IgnoredWorkType = ignoredWorkType,
                SameWorkCooldownClear = sameWorkCooldownClear,
                PassedChanceRoll = passedChanceRoll,
                HasPassion = mood.HasPassion,
                HasLowSkill = mood.HasLowSkill,
                IsNegativeChore = mood.IsNegativeChore,
                IsDarkStudy = mood.IsDarkStudy,
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: eligible,
                userEnabled: userEnabled,
                signalEnabled: signalEnabled,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Work);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string label = WorkLabel(workTypeDef, workGiverDef);
            string text = WorkEventText(pawn, label, moodImpact, IsDarkStudy(workTypeDef));
            string instruction = InteractionGroups.InstructionForWork(group);
            string gameContext = WorkEventData.BuildGameContext(
                data.WorkTypeDefName,
                data.WorkGiverDefName,
                data.MoodImpact,
                data.HasPassion,
                data.HasLowSkill,
                data.IsNegativeChore,
                data.IsDarkStudy);

            DiaryEvent diaryEvent = AddSoloEvent(pawn, null, eventDefName, label, text, instruction, gameContext);
            diaryEvent.moodImpact = moodImpact;
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Reads the current job's WorkGiverDef/WorkTypeDef. Jobs without a WorkGiverDef are
        /// usually recreation, rest, drafted combat, pathing, or forced utility jobs rather than
        /// normal Work-tab labor, so they are ignored.
        /// </summary>
        private static bool TryGetCurrentWork(Pawn pawn, out WorkGiverDef workGiverDef, out WorkTypeDef workTypeDef)
        {
            workGiverDef = null;
            workTypeDef = null;

            Job job = pawn?.CurJob;
            workGiverDef = job?.workGiverDef;
            workTypeDef = workGiverDef?.workType;
            return workTypeDef != null;
        }

        /// <summary>
        /// Social and violent work already has stronger dedicated diary sources (social logs,
        /// mental states, tales/combat). Skip those work types so this feature stays focused on
        /// labor, not conversations or fighting.
        /// </summary>
        private static bool ShouldIgnoreWorkType(WorkTypeDef workTypeDef)
        {
            if (workTypeDef == null)
            {
                return true;
            }

            WorkTags tags = workTypeDef.workTags;
            return (tags & WorkTags.Social) != 0 || (tags & WorkTags.Violent) != 0;
        }

        /// <summary>
        /// Passionate work is framed positively. Cleaning and dumb labor are always negative.
        /// Other non-passion work is neutral unless the pawn's relevant skill is low.
        /// </summary>
        private static WorkMood ClassifyWorkMood(Pawn pawn, WorkTypeDef workTypeDef)
        {
            bool darkStudy = IsDarkStudy(workTypeDef);
            bool negativeChore = IsCleaningOrDumbWork(workTypeDef);
            bool passion = HasPassionForWork(pawn, workTypeDef);
            bool lowSkill = !passion && HasLowRelevantSkill(pawn, workTypeDef, DiarySignalPolicies.WorkLowSkillThreshold);

            return new WorkMood(passion && !negativeChore && !darkStudy, negativeChore || lowSkill || darkStudy, passion, lowSkill, negativeChore, darkStudy);
        }

        private static string WorkEventDefName(WorkTypeDef workTypeDef, WorkMood mood)
        {
            return WorkEventData.EventDefName(mood.IsDarkStudy, mood.IsPositive, mood.IsNegative);
        }

        private static float WorkDiaryChance(Pawn pawn, WorkTypeDef workTypeDef, WorkMood mood)
        {
            float chance = Math.Max(0f, DiarySignalPolicies.WorkBaseChance);
            if (mood.HasPassion)
            {
                chance *= Math.Max(0f, DiarySignalPolicies.WorkPassionChanceMultiplier);
            }

            if (mood.IsNegative)
            {
                chance *= Math.Max(0f, DiarySignalPolicies.WorkNegativeChanceMultiplier);
            }

            if (mood.IsDarkStudy)
            {
                chance *= Math.Max(0f, DiarySignalPolicies.WorkDarkStudyChanceMultiplier);
            }

            float weight = PawnDiaryMod.Settings?.workGenerationWeight ?? 1f;
            return chance * weight;
        }

        private bool HasRecentWorkEvent(Pawn pawn, string currentWorkTypeDefName, int windowTicks, bool sameWorkOnly)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(currentWorkTypeDefName) || windowTicks <= 0 || diaryEvents == null)
            {
                return false;
            }

            int minTick = Find.TickManager.TicksGame - windowTicks;
            string pawnId = pawn.GetUniqueLoadID();
            for (int i = diaryEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.tick < minTick)
                {
                    break;
                }

                if (diaryEvent.initiatorPawnId != pawnId || !IsWorkContext(diaryEvent.gameContext))
                {
                    continue;
                }

                string recordedWork = WorkTypeFromContext(diaryEvent.gameContext);
                bool sameWork = string.Equals(recordedWork, currentWorkTypeDefName, StringComparison.OrdinalIgnoreCase);
                if ((sameWorkOnly && sameWork) || (!sameWorkOnly && !sameWork))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWorkContext(string gameContext)
        {
            return DiaryContextFields.HasField(gameContext, "work");
        }

        private static string WorkTypeFromContext(string gameContext)
        {
            return DiaryContextFields.Value(gameContext, "work");
        }

        private static bool HasPassionForWork(Pawn pawn, WorkTypeDef workTypeDef)
        {
            if (pawn?.skills == null || workTypeDef?.relevantSkills == null)
            {
                return false;
            }

            for (int i = 0; i < workTypeDef.relevantSkills.Count; i++)
            {
                SkillRecord skill = pawn.skills.GetSkill(workTypeDef.relevantSkills[i]);
                if (skill != null && skill.passion != Passion.None)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasLowRelevantSkill(Pawn pawn, WorkTypeDef workTypeDef, int threshold)
        {
            if (pawn?.skills == null || workTypeDef?.relevantSkills == null || workTypeDef.relevantSkills.Count == 0)
            {
                return false;
            }

            int bestLevel = -1;
            for (int i = 0; i < workTypeDef.relevantSkills.Count; i++)
            {
                SkillRecord skill = pawn.skills.GetSkill(workTypeDef.relevantSkills[i]);
                if (skill != null && skill.Level > bestLevel)
                {
                    bestLevel = skill.Level;
                }
            }

            return bestLevel >= 0 && bestLevel < threshold;
        }

        private static bool IsCleaningOrDumbWork(WorkTypeDef workTypeDef)
        {
            if (workTypeDef == null)
            {
                return false;
            }

            return string.Equals(workTypeDef.defName, "Cleaning", StringComparison.OrdinalIgnoreCase)
                || (workTypeDef.workTags & WorkTags.ManualDumb) != 0;
        }

        private static bool IsDarkStudy(WorkTypeDef workTypeDef)
        {
            return string.Equals(workTypeDef?.defName, "DarkStudy", StringComparison.OrdinalIgnoreCase);
        }

        private static string WorkLabel(WorkTypeDef workTypeDef, WorkGiverDef workGiverDef)
        {
            string label = DiaryContextBuilder.CleanLine(workGiverDef?.label);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            label = DiaryContextBuilder.CleanLine(workTypeDef?.gerundLabel);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            label = DiaryContextBuilder.CleanLine(workTypeDef?.labelShort);
            return string.IsNullOrWhiteSpace(label) ? workTypeDef?.defName ?? string.Empty : label;
        }

        private static string WorkEventText(Pawn pawn, string label, string moodImpact, bool darkStudy)
        {
            if (darkStudy)
            {
                return "PawnDiary.Event.WorkDarkStudy".Translate(pawn.LabelShortCap, label);
            }

            return MoodImpact.PickText(moodImpact,
                "PawnDiary.Event.WorkPositive",
                "PawnDiary.Event.WorkNegative",
                "PawnDiary.Event.WorkNeutral",
                pawn.LabelShortCap.Named("PAWN"),
                label.Named("WORK"));
        }

        private struct WorkMood
        {
            public readonly bool IsPositive;
            public readonly bool IsNegative;
            public readonly bool HasPassion;
            public readonly bool HasLowSkill;
            public readonly bool IsNegativeChore;
            public readonly bool IsDarkStudy;

            public WorkMood(bool isPositive, bool isNegative, bool hasPassion, bool hasLowSkill, bool isNegativeChore, bool isDarkStudy)
            {
                IsPositive = isPositive;
                IsNegative = isNegative;
                HasPassion = hasPassion;
                HasLowSkill = hasLowSkill;
                IsNegativeChore = isNegativeChore;
                IsDarkStudy = isDarkStudy;
            }
        }
    }
}
