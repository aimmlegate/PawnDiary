// Ideology ritual completion events. RimWorld drives rituals through LordJob_Ritual; when the job
// applies its outcome, we fan the finished ritual out into separate solo diary entries for the
// ritual author/organizer, target pawn if any, participants, and spectators.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string RitualOutcomeFinished = "finished";
        private const string RitualTargetRoleLabel = "target";
        private const string RitualSpectatorRoleLabel = "spectator";

        private static readonly FieldInfo RitualAssignmentsField =
            AccessTools.Field(typeof(LordJob_Ritual), "assignments");
        private static readonly FieldInfo RitualSelectedTargetField =
            AccessTools.Field(typeof(LordJob_Ritual), "selectedTarget");
        private static readonly FieldInfo RitualBehaviorField =
            AccessTools.Field(typeof(Precept_Ritual), "behavior");

        /// <summary>
        /// Records a finished Ideology ritual as per-pawn solo diary events. Called after
        /// LordJob_Ritual.ApplyOutcome so only completed outcomes are captured; canceled rituals drop.
        /// </summary>
        public void RecordRitualFinished(LordJob_Ritual ritualJob, float progress, bool cancelled)
        {
            if (!CanRecordGameplayEventNow()
                || ritualJob == null
                || ritualJob.Ritual == null
                || PawnDiaryMod.Settings == null
                || cancelled)
            {
                return;
            }

            Precept_Ritual ritual = ritualJob.Ritual;
            string defName = ritual.def?.defName;
            if (string.IsNullOrWhiteSpace(defName))
            {
                defName = ritual.def?.label;
            }

            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            string behaviorClass = RitualBehaviorClass(ritual);
            string classifierKey = RitualClassifierKey(defName, behaviorClass);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(classifierKey);
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            Pawn organizer = ritualJob.Organizer;
            Pawn targetPawn = RitualTargetPawn(ritualJob);
            string dedupKey = "ritual|" + defName
                + "|" + PawnKey(organizer)
                + "|" + PawnKey(targetPawn)
                + "|" + Find.TickManager.TicksGame;
            if (IsRecentlyRecorded(recentRitualEvents, dedupKey, DiaryTuning.Current.ritualDedupTicks))
            {
                return;
            }

            RitualRoleAssignments assignments = RitualAssignments(ritualJob);
            string title = RitualTitle(ritualJob, ritual);
            string label = title;
            string quality = progress.ToString("0.##", CultureInfo.InvariantCulture);
            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Ritual);
            HashSet<string> recordedPawnIds = new HashSet<string>();
            bool recordedAny = false;

            recordedAny |= TryRecordRitualPawn(ritualJob, assignments, spec, organizer, targetPawn,
                defName, label, title, behaviorClass, RitualEventData.PerspectiveOrganizer, quality, recordedPawnIds);
            recordedAny |= TryRecordRitualPawn(ritualJob, assignments, spec, targetPawn, organizer,
                defName, label, title, behaviorClass, RitualEventData.PerspectiveTarget, quality, recordedPawnIds);

            List<Pawn> participants = assignments?.Participants;
            if (participants != null)
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    recordedAny |= TryRecordRitualPawn(ritualJob, assignments, spec, participants[i], organizer,
                        defName, label, title, behaviorClass, RitualEventData.PerspectiveParticipant, quality, recordedPawnIds);
                }
            }

            List<Pawn> spectators = assignments?.SpectatorsForReading;
            if (spectators != null)
            {
                for (int i = 0; i < spectators.Count; i++)
                {
                    recordedAny |= TryRecordRitualPawn(ritualJob, assignments, spec, spectators[i], organizer,
                        defName, label, title, behaviorClass, RitualEventData.PerspectiveSpectator, quality, recordedPawnIds);
                }
            }

            if (recordedAny)
            {
                MarkRecentlyRecorded(recentRitualEvents, dedupKey, DiaryTuning.Current.ritualDedupTicks);
            }
        }

        private bool TryRecordRitualPawn(
            LordJob_Ritual ritualJob,
            RitualRoleAssignments assignments,
            DiaryEventSpec spec,
            Pawn pawn,
            Pawn otherPawn,
            string defName,
            string label,
            string title,
            string behaviorClass,
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
            string ritualRole = RitualRoleLabel(ritualJob, assignments, pawn, perspective);
            RitualEventData data = new RitualEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = defName,
                Title = title,
                BehaviorClass = behaviorClass,
                Perspective = perspective,
                RitualRole = ritualRole,
                Cancelled = false,
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: true,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);

            CaptureDecision decision = spec != null ? spec.Decide(data, ctx) : CaptureDecision.Drop;
            if (decision != CaptureDecision.GenerateSolo)
            {
                return false;
            }

            string context = RitualEventData.BuildGameContext(
                defName,
                title,
                behaviorClass,
                perspective,
                ritualRole,
                DlcContext.RoyalTitle(pawn),
                DlcContext.IdeologicalRole(pawn),
                RitualOutcomeFinished,
                quality);
            string text = "PawnDiary.Event.RitualFinished"
                .Translate(pawn.LabelShortCap, title, RitualPerspectiveLabel(perspective), ritualRole)
                .Resolve();
            string instruction = RitualInstructionFor(perspective);

            DiaryEvent ritualEvent = AddSoloEvent(pawn, otherPawn, defName, label, text, instruction, context);
            if (ritualEvent == null)
            {
                return false;
            }

            QueueLlmRewrite(ritualEvent, DiaryEvent.InitiatorRole);
            return true;
        }

        private static RitualRoleAssignments RitualAssignments(LordJob_Ritual ritualJob)
        {
            return RitualAssignmentsField?.GetValue(ritualJob) as RitualRoleAssignments;
        }

        private static Pawn RitualTargetPawn(LordJob_Ritual ritualJob)
        {
            if (RitualSelectedTargetField == null || ritualJob == null)
            {
                return null;
            }

            object value = RitualSelectedTargetField.GetValue(ritualJob);
            if (!(value is TargetInfo))
            {
                return null;
            }

            TargetInfo target = (TargetInfo)value;
            return target.Thing as Pawn;
        }

        private static string RitualTitle(LordJob_Ritual ritualJob, Precept_Ritual ritual)
        {
            string title = DiaryContextBuilder.CleanLine(ritualJob?.RitualLabel);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = DiaryContextBuilder.CleanLine(ritual?.LabelCap);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = DiaryContextBuilder.CleanLine(ritual?.def?.label);
            }

            return string.IsNullOrWhiteSpace(title) ? RitualEventData.FallbackTitle : title;
        }

        private static string RitualBehaviorClass(Precept_Ritual ritual)
        {
            object behavior = RitualBehaviorField?.GetValue(ritual);
            return behavior == null ? string.Empty : behavior.GetType().Name;
        }

        private static string RitualClassifierKey(string defName, string behaviorClass)
        {
            if (string.IsNullOrWhiteSpace(behaviorClass))
            {
                return defName;
            }

            return defName + ";" + behaviorClass;
        }

        private static string RitualRoleLabel(
            LordJob_Ritual ritualJob,
            RitualRoleAssignments assignments,
            Pawn pawn,
            string perspective)
        {
            if (string.Equals(perspective, RitualEventData.PerspectiveTarget, StringComparison.OrdinalIgnoreCase))
            {
                return RitualTargetRoleLabel;
            }

            RitualRole role = null;
            try
            {
                role = ritualJob?.RoleFor(pawn, true);
            }
            catch
            {
                role = assignments?.RoleForPawn(pawn, true);
            }

            string assignedRole = role == null ? string.Empty : DiaryContextBuilder.CleanLine(role.LabelCap.Resolve());
            string perspectiveLabel = RitualPerspectiveLabel(perspective);
            if (string.IsNullOrWhiteSpace(assignedRole))
            {
                return perspectiveLabel;
            }

            if (string.Equals(assignedRole, perspectiveLabel, StringComparison.OrdinalIgnoreCase))
            {
                return assignedRole;
            }

            return perspectiveLabel + " (" + assignedRole + ")";
        }

        private static string RitualPerspectiveLabel(string perspective)
        {
            if (string.Equals(perspective, RitualEventData.PerspectiveOrganizer, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Ritual.Role.Author".Translate().Resolve();
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

        private static string RitualInstructionFor(string perspective)
        {
            if (string.Equals(perspective, RitualEventData.PerspectiveOrganizer, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Event.RitualInstruction.Author".Translate().Resolve();
            }

            if (string.Equals(perspective, RitualEventData.PerspectiveTarget, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Event.RitualInstruction.Target".Translate().Resolve();
            }

            if (string.Equals(perspective, RitualEventData.PerspectiveSpectator, StringComparison.OrdinalIgnoreCase))
            {
                return "PawnDiary.Event.RitualInstruction.Spectator".Translate().Resolve();
            }

            return "PawnDiary.Event.RitualInstruction.Participant".Translate().Resolve();
        }

        private static string PawnKey(Pawn pawn)
        {
            return pawn == null ? "none" : pawn.GetUniqueLoadID();
        }
    }
}
