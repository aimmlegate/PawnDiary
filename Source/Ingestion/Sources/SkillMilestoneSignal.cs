// Skill-milestone ingestion signal — the impure capture+emit half of SkillRecord.Learn. The hook
// snapshots the old/new level; XML decides which levels count as milestones. This source is solo:
// the pawn whose skill advanced writes the page.
using System;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures a colonist crossing a configured skill level milestone.
    /// </summary>
    public sealed class SkillMilestoneSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly DiaryInteractionGroupDef group;
        private readonly SkillMilestoneEventData payload;

        public SkillMilestoneSignal(Pawn pawn, SkillDef skillDef, int oldLevel, int newLevel, string passion)
        {
            if (!DiaryGameComponent.GamePlaying || pawn == null || skillDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!DiaryGameComponent.IsDiaryEligible(pawn))
            {
                return;
            }

            string defName = skillDef.defName;
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            int milestone = SkillMilestoneEventData.CrossedMilestone(
                oldLevel, newLevel, DiarySignalPolicies.SkillMilestoneLevels);
            if (milestone <= 0)
            {
                return;
            }

            DiaryInteractionGroupDef classified = InteractionGroups.ClassifySkillMilestone(defName);
            if (classified == null || !PawnDiaryMod.Settings.IsGroupEnabled(classified.defName))
            {
                return;
            }

            this.pawn = pawn;
            group = classified;
            payload = new SkillMilestoneEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = defName,
                Label = SkillLabel(skillDef),
                Passion = string.IsNullOrWhiteSpace(passion) ? "none" : passion.Trim().ToLowerInvariant(),
                OldLevel = oldLevel,
                NewLevel = newLevel,
                MilestoneLevel = milestone,
                MilestoneLevels = DiarySignalPolicies.SkillMilestoneLevels
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true,
                userEnabled: true,
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.SkillMilestone),
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload == null ? string.Empty : payload.DedupKey();

        public override int DedupWindowTicks => DiarySignalPolicies.SkillMilestoneDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo || pawn == null || payload == null)
            {
                return;
            }

            string context = SkillMilestoneEventData.BuildGameContext(
                payload.DefName, payload.Label, payload.OldLevel, payload.NewLevel,
                payload.MilestoneLevel, payload.Passion);
            string text = "PawnDiary.Event.SkillMilestone".Translate(
                pawn.LabelShortCap, payload.Label, payload.MilestoneLevel).Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, payload.DefName, payload.Label, text, instruction, context);
            if (diaryEvent == null)
            {
                return;
            }

            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }

        private static string SkillLabel(SkillDef skillDef)
        {
            string label = DiaryLineCleaner.CleanLine(skillDef == null ? null : skillDef.LabelCap.Resolve());
            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryLineCleaner.CleanLine(skillDef?.label);
            }

            return string.IsNullOrWhiteSpace(label) ? skillDef?.defName ?? "skill" : label;
        }
    }
}
