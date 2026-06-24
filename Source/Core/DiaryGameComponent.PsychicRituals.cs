// Anomaly psychic ritual completion events. These are separate from Ideology/Royalty rituals:
// RimWorld drives them through LordJob_PsychicRitual and PsychicRitualGraph, and the diary records
// them without sending role/title prompt fields.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string PsychicRitualClassifierPrefix = "PsychicRitual";

        private static readonly FieldInfo PsychicRitualDefField =
            AccessTools.Field(typeof(PsychicRitual), "def");
        private static readonly FieldInfo PsychicRitualAssignmentsField =
            AccessTools.Field(typeof(PsychicRitual), "assignments");
        private static readonly FieldInfo PsychicRitualCanceledField =
            AccessTools.Field(typeof(PsychicRitual), "canceled");

        /// <summary>
        /// Records a successful Anomaly psychic ritual as per-pawn solo diary events. Called from
        /// LordToil_PsychicRitual.RitualCompleted after vanilla reaches the completion callback.
        /// </summary>
        public void RecordPsychicRitualFinished(PsychicRitual psychicRitual, bool success)
        {
            if (!CanRecordGameplayEventNow()
                || psychicRitual == null
                || !success
                || !ModsConfig.AnomalyActive
                || PawnDiaryMod.Settings == null
                || PsychicRitualCanceled(psychicRitual))
            {
                return;
            }

            PsychicRitualDef def = PsychicRitualDefField?.GetValue(psychicRitual) as PsychicRitualDef;
            string defName = def?.defName;
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(PsychicRitualClassifierKey(defName));
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            PsychicRitualRoleAssignments assignments =
                PsychicRitualAssignmentsField?.GetValue(psychicRitual) as PsychicRitualRoleAssignments;
            if (assignments == null)
            {
                return;
            }

            string ritualId = psychicRitual.GetUniqueLoadID();
            string dedupKey = "psychic_ritual|" + defName + "|" + ritualId + "|" + Find.TickManager.TicksGame;
            if (IsRecentlyRecorded(recentRitualEvents, dedupKey, DiaryTuning.Current.ritualDedupTicks))
            {
                return;
            }

            string label = DiaryContextBuilder.CleanLine(def.label);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = defName;
            }

            string quality = PsychicRitualQuality(psychicRitual);
            HashSet<string> recordedPawnIds = new HashSet<string>();
            bool recordedAny = false;

            Pawn invoker = PsychicRitualInvoker(assignments);
            Pawn targetPawn = PsychicRitualTargetPawn(assignments);
            recordedAny |= TryRecordPsychicRitualPawn(invoker, targetPawn, defName, label,
                RitualEventData.PerspectiveInvoker, quality, recordedPawnIds);
            recordedAny |= TryRecordPsychicRitualPawn(targetPawn, invoker, defName, label,
                RitualEventData.PerspectiveTarget, quality, recordedPawnIds);

            foreach (Pawn pawn in assignments.AllAssignedPawns)
            {
                recordedAny |= TryRecordPsychicRitualPawn(pawn, invoker, defName, label,
                    RitualEventData.PerspectiveParticipant, quality, recordedPawnIds);
            }

            List<Pawn> spectators = assignments.SpectatorsForReading;
            if (spectators != null)
            {
                for (int i = 0; i < spectators.Count; i++)
                {
                    recordedAny |= TryRecordPsychicRitualPawn(spectators[i], invoker, defName, label,
                        RitualEventData.PerspectiveSpectator, quality, recordedPawnIds);
                }
            }

            if (recordedAny)
            {
                MarkRecentlyRecorded(recentRitualEvents, dedupKey, DiaryTuning.Current.ritualDedupTicks);
            }
        }

        private bool TryRecordPsychicRitualPawn(
            Pawn pawn,
            Pawn otherPawn,
            string defName,
            string label,
            string perspective,
            string quality,
            HashSet<string> recordedPawnIds)
        {
            if (pawn == null || !IsDiaryEligible(pawn))
            {
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId) || recordedPawnIds.Contains(pawnId))
            {
                return false;
            }

            recordedPawnIds.Add(pawnId);
            RitualEventData data = new RitualEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = defName,
                Perspective = perspective,
                Cancelled = false,
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: true,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Ritual);
            CaptureDecision decision = spec != null ? spec.Decide(data, ctx) : CaptureDecision.Drop;
            if (decision != CaptureDecision.GenerateSolo)
            {
                return false;
            }

            string context = RitualEventData.BuildPsychicGameContext(
                defName,
                perspective,
                RitualOutcomeFinished,
                quality);
            string text = "PawnDiary.Event.PsychicRitualFinished"
                .Translate(pawn.LabelShortCap, label, PsychicRitualPerspectiveLabel(perspective))
                .Resolve();
            string instruction = PsychicRitualInstructionFor(perspective);

            DiaryEvent ritualEvent = AddSoloEvent(pawn, otherPawn, defName, label, text, instruction, context);
            if (ritualEvent == null)
            {
                return false;
            }

            QueueLlmRewrite(ritualEvent, DiaryEvent.InitiatorRole);
            return true;
        }

        private static bool PsychicRitualCanceled(PsychicRitual psychicRitual)
        {
            object value = PsychicRitualCanceledField?.GetValue(psychicRitual);
            return value is bool && (bool)value;
        }

        private static Pawn PsychicRitualInvoker(PsychicRitualRoleAssignments assignments)
        {
            PsychicRitualRoleDef invokerRole = DefDatabase<PsychicRitualRoleDef>.GetNamedSilentFail("Invoker");
            return invokerRole == null ? null : assignments.FirstAssignedPawn(invokerRole);
        }

        private static Pawn PsychicRitualTargetPawn(PsychicRitualRoleAssignments assignments)
        {
            TargetInfo target = assignments.Target;
            return target.Thing as Pawn;
        }

        private static string PsychicRitualQuality(PsychicRitual psychicRitual)
        {
            return psychicRitual == null
                ? string.Empty
                : RitualEventData.QualityLabel(psychicRitual.PowerPercent, DiaryTuning.Current.ritualQualityBands);
        }

        private static string PsychicRitualClassifierKey(string defName)
        {
            return PsychicRitualClassifierPrefix + ";" + defName;
        }

        private static string PsychicRitualPerspectiveLabel(string perspective)
        {
            if (string.Equals(perspective, RitualEventData.PerspectiveInvoker, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.PsychicRitual.Role.Invoker".Translate().Resolve();
            }

            if (string.Equals(perspective, RitualEventData.PerspectiveTarget, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Ritual.Role.Target".Translate().Resolve();
            }

            if (string.Equals(perspective, RitualEventData.PerspectiveSpectator, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Ritual.Role.Spectator".Translate().Resolve();
            }

            return "PawnDiary.Ritual.Role.Participant".Translate().Resolve();
        }

        private static string PsychicRitualInstructionFor(string perspective)
        {
            if (string.Equals(perspective, RitualEventData.PerspectiveInvoker, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Event.PsychicRitualInstruction.Invoker".Translate().Resolve();
            }

            if (string.Equals(perspective, RitualEventData.PerspectiveTarget, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Event.PsychicRitualInstruction.Target".Translate().Resolve();
            }

            if (string.Equals(perspective, RitualEventData.PerspectiveSpectator, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Event.PsychicRitualInstruction.Spectator".Translate().Resolve();
            }

            return "PawnDiary.Event.PsychicRitualInstruction.Participant".Translate().Resolve();
        }
    }
}
