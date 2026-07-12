// Psychic-ritual ingestion signal — the impure capture+emit half of the "Anomaly psychic ritual
// finished" source (LordToil_PsychicRitual.RitualCompleted). Replaces the old
// DiaryGameComponent.RecordPsychicRitualFinished. A perspective FAN-OUT like Ideology rituals, but it
// deliberately sends no role/title fields — only perspective, outcome, and quality. Maps to the same
// DiaryEventType.Ritual spec. Pure decision + psychic game-context live in
// Source/Capture/Events/RitualEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony perspective fan-out for a finished Anomaly psychic ritual. Built by
    /// <see cref="PsychicRitualCompletedPatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>.
    /// </summary>
    internal sealed class PsychicRitualFanoutSignal : DiaryFanoutSignal
    {
        private const string PsychicRitualClassifierPrefix = "PsychicRitual";

        private static readonly FieldInfo PsychicRitualDefField = AccessTools.Field(typeof(PsychicRitual), "def");
        private static readonly FieldInfo PsychicRitualAssignmentsField = AccessTools.Field(typeof(PsychicRitual), "assignments");
        private static readonly FieldInfo PsychicRitualCanceledField = AccessTools.Field(typeof(PsychicRitual), "canceled");

        private readonly bool valid;
        private readonly string colonyDedupKey;
        private readonly PsychicRitualRoleAssignments assignments;
        private readonly Pawn invoker;
        private readonly Pawn targetPawn;

        internal string DefName { get; }
        internal string Label { get; }
        internal string Quality { get; }
        internal string GroupInstruction { get; }

        public PsychicRitualFanoutSignal(PsychicRitual psychicRitual, bool success)
        {
            if (!DiaryGameComponent.GamePlaying || psychicRitual == null || !success
                || !ModsConfig.AnomalyActive || PawnDiaryMod.Settings == null || PsychicRitualCanceled(psychicRitual))
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

            // Keep the XML ritual theme reachable without consuming the simulation RNG. The per-pawn
            // child appends its localized invoker/target/participant/spectator guidance at emit time.
            int instructionTick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            int instructionSeed = PromptVariants.HashSeed(defName + "|" + instructionTick);
            GroupInstruction = PromptVariants.Pick(group.instructions, group.instruction, instructionSeed);

            assignments = PsychicRitualAssignmentsField?.GetValue(psychicRitual) as PsychicRitualRoleAssignments;
            if (assignments == null)
            {
                return;
            }

            DefName = defName;
            string ritualId = psychicRitual.GetUniqueLoadID();
            colonyDedupKey = "psychic_ritual|" + defName + "|" + ritualId + "|" + Find.TickManager.TicksGame;

            string label = DiaryLineCleaner.CleanLine(def.label);
            Label = string.IsNullOrWhiteSpace(label) ? defName : label;
            Quality = PsychicRitualQuality(psychicRitual);
            invoker = PsychicRitualInvoker(assignments);
            targetPawn = PsychicRitualTargetPawn(assignments);
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiaryTuning.Current.ritualDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            HashSet<string> seen = new HashSet<string>();

            foreach (DiarySignal s in PerPawn(invoker, targetPawn, RitualEventData.PerspectiveInvoker, seen))
            {
                yield return s;
            }
            foreach (DiarySignal s in PerPawn(targetPawn, invoker, RitualEventData.PerspectiveTarget, seen))
            {
                yield return s;
            }

            foreach (Pawn pawn in assignments.AllAssignedPawns)
            {
                foreach (DiarySignal s in PerPawn(pawn, invoker, RitualEventData.PerspectiveParticipant, seen))
                {
                    yield return s;
                }
            }

            List<Pawn> spectators = assignments.SpectatorsForReading;
            if (spectators != null)
            {
                for (int i = 0; i < spectators.Count; i++)
                {
                    foreach (DiarySignal s in PerPawn(spectators[i], invoker, RitualEventData.PerspectiveSpectator, seen))
                    {
                        yield return s;
                    }
                }
            }
        }

        private IEnumerable<DiarySignal> PerPawn(Pawn pawn, Pawn otherPawn, string perspective, HashSet<string> seen)
        {
            if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
            {
                yield break;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId) || !seen.Add(pawnId))
            {
                yield break;
            }

            yield return new PsychicRitualPawnSignal(this, pawn, otherPawn, perspective, pawnId);
        }

        // ── Helpers moved verbatim from the old DiaryGameComponent.PsychicRituals.cs ──

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

        internal static string PsychicRitualPerspectiveLabel(string perspective)
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

        internal static string PsychicRitualInstructionFor(string perspective)
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

    /// <summary>One pawn's perspective on a finished Anomaly psychic ritual.</summary>
    internal sealed class PsychicRitualPawnSignal : DiarySignal
    {
        private readonly PsychicRitualFanoutSignal source;
        private readonly Pawn pawn;
        private readonly Pawn otherPawn;
        private readonly string perspective;
        private readonly RitualEventData payload;

        public PsychicRitualPawnSignal(PsychicRitualFanoutSignal source, Pawn pawn, Pawn otherPawn, string perspective, string pawnId)
        {
            this.source = source;
            this.pawn = pawn;
            this.otherPawn = otherPawn;
            this.perspective = perspective;
            payload = new RitualEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = source.DefName,
                Perspective = perspective,
                Cancelled = false,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true, userEnabled: true, signalEnabled: true, ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string context = RitualEventData.BuildPsychicGameContext(
                source.DefName, perspective, RitualFanoutSignal.RitualOutcomeFinished, source.Quality);
            string text = "PawnDiary.Event.PsychicRitualFinished"
                .Translate(pawn.LabelShortCap, source.Label, PsychicRitualFanoutSignal.PsychicRitualPerspectiveLabel(perspective))
                .Resolve();
            string instruction = RitualEventData.CombineInstructions(
                source.GroupInstruction,
                PsychicRitualFanoutSignal.PsychicRitualInstructionFor(perspective));

            DiaryEvent ritualEvent = sink.AddSoloEvent(pawn, otherPawn, source.DefName, source.Label, text, instruction, context);
            if (ritualEvent == null)
            {
                return;
            }

            sink.QueueSolo(ritualEvent, DiaryEvent.InitiatorRole);
        }
    }
}
