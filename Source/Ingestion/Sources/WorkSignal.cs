// Work ingestion signal — the impure capture+emit half of the "current work sampled" source. There
// is no one-shot Harmony hook for work (jobs are long-running), so the component's periodic scan
// (ScanPawnWorkForDiaryEvents) samples each colonist and submits a WorkSignal. This replaces the old
// DiaryGameComponent.TryRecordCurrentWork: the scan still picks WHO to sample, but the per-pawn
// snapshot (work type, mood classification, persistent cooldowns, weighted roll) and emit now live
// here, and the Decide/route runs through the shared dispatcher like every other source.
//
// Cooldowns read saved diary history through DiaryGameComponent.Current.HasRecentWorkEvent. Pure
// decision + game-context + defName selection live in Source/Capture/Events/WorkEventData.cs.
// New to C#/RimWorld? See AGENTS.md ("WorkTypeDef", "WorkGiverDef").
using System;
using PawnDiary.Capture;
using RimWorld;
using Verse;
using Verse.AI;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one colonist's current work moment and, if the weighted roll passes, emits a solo work
    /// entry. Built by the component's work scan and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class WorkSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly WorkGiverDef workGiverDef;
        private readonly WorkTypeDef workTypeDef;
        private readonly DiaryInteractionGroupDef group;
        private readonly string eventDefName;
        private readonly string moodImpact;
        private readonly bool darkStudy;
        private readonly bool eligible;
        private readonly bool userEnabled;
        private readonly bool signalEnabled;
        private readonly WorkEventData payload;

        public WorkSignal(Pawn pawn)
        {
            this.pawn = pawn;

            if (!DiaryGameComponent.GamePlaying || pawn == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!TryGetCurrentWork(pawn, out workGiverDef, out workTypeDef))
            {
                return;
            }

            bool ignoredWorkType = ShouldIgnoreWorkType(workTypeDef);
            WorkMood mood = ignoredWorkType ? default(WorkMood) : ClassifyWorkMood(pawn, workTypeDef);
            eventDefName = ignoredWorkType ? WorkEventData.RoutineDefName : WorkEventDefName(workTypeDef, mood);
            group = InteractionGroups.ClassifyWork(eventDefName);
            darkStudy = IsDarkStudy(workTypeDef);
            eligible = DiaryGameComponent.IsDiaryEligible(pawn);
            userEnabled = group != null && PawnDiaryMod.Settings.IsWorkEnabled(group);
            signalEnabled = DiarySignalPolicies.Enabled(DiarySignalPolicies.Work);
            bool canRollWorkEvent = eligible && userEnabled && signalEnabled && !ignoredWorkType;

            int cooldownTicks = Math.Max(0, DiarySignalPolicies.WorkSameTypeCooldownTicks);
            bool sameWorkCooldownClear = false;
            if (canRollWorkEvent)
            {
                sameWorkCooldownClear = !(DiaryGameComponent.Current?.HasRecentWorkEvent(pawn, workTypeDef.defName, cooldownTicks, true) ?? false);
            }

            bool passedChanceRoll = false;
            if (canRollWorkEvent && sameWorkCooldownClear)
            {
                float chance = WorkDiaryChance(pawn, workTypeDef, mood);
                if (DiaryGameComponent.Current?.HasRecentWorkEvent(pawn, workTypeDef.defName, cooldownTicks, false) ?? false)
                {
                    chance *= Math.Max(0f, DiarySignalPolicies.WorkRecentDifferentTypeMultiplier);
                }

                passedChanceRoll = Rand.Value <= Math.Min(1f, Math.Max(0f, chance));
            }

            moodImpact = mood.IsPositive ? MoodImpact.Positive : mood.IsNegative ? MoodImpact.Negative : MoodImpact.Neutral;
            payload = new WorkEventData
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
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: eligible,
                userEnabled: userEnabled,
                signalEnabled: signalEnabled,
                ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string label = WorkLabel(workTypeDef, workGiverDef);
            string text = WorkEventText(pawn, label, moodImpact, darkStudy);
            string instruction = InteractionGroups.InstructionForWork(group);
            string gameContext = WorkEventData.BuildGameContext(
                payload.WorkTypeDefName, payload.WorkGiverDefName, payload.MoodImpact,
                payload.HasPassion, payload.HasLowSkill, payload.IsNegativeChore, payload.IsDarkStudy);

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, eventDefName, label, text, instruction, gameContext);
            diaryEvent.moodImpact = moodImpact;
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }

        // ── Helpers moved verbatim from the old DiaryGameComponent.Work.cs ──

        private static bool TryGetCurrentWork(Pawn pawn, out WorkGiverDef workGiverDef, out WorkTypeDef workTypeDef)
        {
            workGiverDef = null;
            workTypeDef = null;

            Job job = pawn?.CurJob;
            workGiverDef = job?.workGiverDef;
            workTypeDef = workGiverDef?.workType;
            return workTypeDef != null;
        }

        private static bool ShouldIgnoreWorkType(WorkTypeDef workTypeDef)
        {
            if (workTypeDef == null)
            {
                return true;
            }

            WorkTags tags = workTypeDef.workTags;
            return (tags & WorkTags.Social) != 0 || (tags & WorkTags.Violent) != 0;
        }

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
            string label = DiaryLineCleaner.CleanLine(workGiverDef?.label);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            label = DiaryLineCleaner.CleanLine(workTypeDef?.gerundLabel);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            label = DiaryLineCleaner.CleanLine(workTypeDef?.labelShort);
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
