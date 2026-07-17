// Ritual ingestion signal — the impure capture+emit half of the "Ideology ritual finished" source
// (LordJob_Ritual.ApplyOutcome). Replaces the old DiaryGameComponent.RecordRitualFinished. A
// perspective FAN-OUT: one solo entry per organizer / target / participant / spectator, with a single
// ritual-level dedup window. Each pawn's role label needs the live ritual job, so it is computed in
// the per-pawn child constructor.
//
// Pure decision + game-context + quality-band math live in Source/Capture/Events/RitualEventData.cs.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony perspective fan-out for a finished Ideology ritual. Built by <see cref="RitualOutcomePatch"/>
    /// and submitted via <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>. The ritual-level gates
    /// (group classification + user toggle, non-cancelled) run in the constructor.
    /// </summary>
    internal sealed class RitualFanoutSignal : DiaryFanoutSignal
    {
        internal const string RitualOutcomeFinished = "finished";
        private const string RitualTargetRoleLabel = "target";

        private static readonly FieldInfo RitualAssignmentsField =
            AccessTools.Field(typeof(LordJob_Ritual), "assignments");
        private static readonly FieldInfo RitualSelectedTargetField =
            AccessTools.Field(typeof(LordJob_Ritual), "selectedTarget");
        private static readonly FieldInfo RitualBehaviorField =
            AccessTools.Field(typeof(Precept_Ritual), "behavior");

        private readonly bool valid;
        private readonly string colonyDedupKey;
        private readonly Pawn organizer;
        private readonly Pawn targetPawn;
        private readonly List<Pawn> fixtureParticipants;
        private readonly List<Pawn> fixtureSpectators;
        private readonly bool odysseyLaunchAuthorized;
        private readonly int odysseyLaunchTick = -1;
        // Ordinary rituals keep their unlimited existing fanout. Only the exact Odyssey launch
        // group replaces this with the XML-owned cap after passing its Odyssey cooldown.
        private readonly int maximumWriters = int.MaxValue;

        internal LordJob_Ritual RitualJob { get; }
        internal RitualRoleAssignments Assignments { get; }
        internal string DefName { get; }
        internal string Title { get; }
        internal string Label { get; }
        internal string BehaviorClass { get; }
        internal string Quality { get; }
        internal string GroupInstruction { get; }

        public RitualFanoutSignal(LordJob_Ritual ritualJob, float progress, bool cancelled)
        {
            if (!DiaryGameComponent.GamePlaying || ritualJob == null || ritualJob.Ritual == null
                || PawnDiaryMod.Settings == null || cancelled)
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

            BehaviorClass = RitualBehaviorClass(ritual);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(RitualClassifierKey(defName, BehaviorClass));
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            int eventTick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            bool odysseyLaunch = ModsConfig.OdysseyActive
                && string.Equals(group.defName, OdysseyGroupDefNames.Launch, StringComparison.Ordinal);
            if (odysseyLaunch)
            {
                OdysseyPolicySnapshot odysseyPolicy = DiaryOdysseyPolicy.Snapshot();
                if (string.Equals(group.defName, odysseyPolicy.launchGroupKey, StringComparison.Ordinal))
                {
                    DiaryGameComponent component = DiaryGameComponent.Instance;
                    if (component == null
                        || !component.AllowsOdysseyLaunchRitualAt(eventTick, ritualJob.Organizer))
                    {
                        return;
                    }

                    maximumWriters = Math.Max(1, Math.Min(2, odysseyPolicy.maximumLaunchWriters));
                    odysseyLaunchAuthorized = true;
                    odysseyLaunchTick = eventTick;
                }
            }

            // Snapshot one XML-owned thematic variant once for the whole ritual. Every pawn keeps
            // the same ceremony framing, while the child signal appends its own localized role guide.
            // A deterministic local seed avoids consuming RimWorld's global simulation RNG merely
            // to choose cosmetic prompt prose.
            int instructionTick = eventTick;
            int instructionSeed = PromptVariants.HashSeed(defName + "|" + instructionTick);
            GroupInstruction = PromptVariants.Pick(group.instructions, group.instruction, instructionSeed);

            RitualJob = ritualJob;
            DefName = defName;
            organizer = ritualJob.Organizer;
            targetPawn = RitualTargetPawn(ritualJob);
            colonyDedupKey = "ritual|" + defName + "|" + PawnKey(organizer) + "|" + PawnKey(targetPawn)
                + "|" + Find.TickManager.TicksGame;
            Assignments = RitualAssignments(ritualJob);
            Title = RitualTitle(ritualJob, ritual);
            Label = Title;
            Quality = RitualEventData.QualityLabel(progress, DiaryTuning.Current.ritualQualityBands);
            valid = true;
        }

        /// <summary>
        /// Builds a bounded loaded-game fixture from already-copied ritual facts. RimTest cannot safely
        /// construct a live <see cref="LordJob_Ritual"/> without starting a real map ritual, so this
        /// internal seam exercises the production fan-out, child capture, dedup, persistence, and prompt
        /// context without changing the live Harmony constructor above.
        /// </summary>
        internal static RitualFanoutSignal CreateTestFixture(
            Pawn organizer,
            Pawn targetPawn,
            List<Pawn> participants,
            List<Pawn> spectators,
            string defName,
            string title,
            string behaviorClass,
            float progress,
            string groupInstruction)
        {
            return new RitualFanoutSignal(
                organizer,
                targetPawn,
                participants,
                spectators,
                defName,
                title,
                behaviorClass,
                progress,
                groupInstruction);
        }

        private RitualFanoutSignal(
            Pawn organizer,
            Pawn targetPawn,
            List<Pawn> participants,
            List<Pawn> spectators,
            string defName,
            string title,
            string behaviorClass,
            float progress,
            string groupInstruction)
        {
            if (!DiaryGameComponent.GamePlaying || string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            this.organizer = organizer;
            this.targetPawn = targetPawn;
            fixtureParticipants = participants == null ? new List<Pawn>() : new List<Pawn>(participants);
            fixtureSpectators = spectators == null ? new List<Pawn>() : new List<Pawn>(spectators);
            DefName = defName;
            Title = string.IsNullOrWhiteSpace(title) ? RitualEventData.FallbackTitle : title;
            Label = Title;
            BehaviorClass = behaviorClass ?? string.Empty;
            Quality = RitualEventData.QualityLabel(progress, DiaryTuning.Current.ritualQualityBands);
            GroupInstruction = groupInstruction ?? string.Empty;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            colonyDedupKey = "ritual_fixture|" + defName + "|" + PawnKey(organizer) + "|"
                + PawnKey(targetPawn) + "|" + tick;
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
            int yielded = 0;

            // Order matches the old RecordRitualFinished: organizer, target, participants, spectators.
            foreach (DiarySignal s in PerPawn(organizer, targetPawn, RitualEventData.PerspectiveOrganizer, seen))
            {
                yield return s;
                if (++yielded >= maximumWriters) yield break;
            }
            foreach (DiarySignal s in PerPawn(targetPawn, organizer, RitualEventData.PerspectiveTarget, seen))
            {
                yield return s;
                if (++yielded >= maximumWriters) yield break;
            }

            List<Pawn> participants = Assignments?.Participants ?? fixtureParticipants;
            if (participants != null)
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    foreach (DiarySignal s in PerPawn(participants[i], organizer, RitualEventData.PerspectiveParticipant, seen))
                    {
                        yield return s;
                        if (++yielded >= maximumWriters) yield break;
                    }
                }
            }

            List<Pawn> spectators = Assignments?.SpectatorsForReading ?? fixtureSpectators;
            if (spectators != null)
            {
                for (int i = 0; i < spectators.Count; i++)
                {
                    foreach (DiarySignal s in PerPawn(spectators[i], organizer, RitualEventData.PerspectiveSpectator, seen))
                    {
                        yield return s;
                        if (++yielded >= maximumWriters) yield break;
                    }
                }
            }
        }

        // Yields one child for an eligible, not-yet-seen pawn (mirrors TryRecordRitualPawn's gate).
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

            yield return new RitualPawnSignal(this, pawn, otherPawn, perspective, pawnId);
        }

        internal string RoleLabelFor(Pawn pawn, string perspective)
        {
            return RitualRoleLabel(RitualJob, Assignments, pawn, perspective);
        }

        /// <summary>Marks cooldown state only after one child successfully creates its saved page.</summary>
        internal void NotifyPageCreated(DiaryGameComponent sink)
        {
            if (odysseyLaunchAuthorized)
            {
                sink?.MarkOdysseyLaunchPageAt(odysseyLaunchTick);
            }
        }

        // ── Helpers moved verbatim from the old DiaryGameComponent.Rituals.cs ──

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
            string title = DiaryLineCleaner.CleanLine(ritualJob?.RitualLabel);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = DiaryLineCleaner.CleanLine(ritual?.LabelCap);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = DiaryLineCleaner.CleanLine(ritual?.def?.label);
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
            LordJob_Ritual ritualJob, RitualRoleAssignments assignments, Pawn pawn, string perspective)
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

            string assignedRole = role == null ? string.Empty : DiaryLineCleaner.CleanLine(role.LabelCap.Resolve());
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

        internal static string RitualPerspectiveLabel(string perspective)
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

        internal static string RitualInstructionFor(string perspective)
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

    /// <summary>One pawn's perspective on a finished Ideology ritual.</summary>
    internal sealed class RitualPawnSignal : DiarySignal
    {
        private readonly RitualFanoutSignal source;
        private readonly Pawn pawn;
        private readonly Pawn otherPawn;
        private readonly string perspective;
        private readonly string ritualRole;
        private readonly RitualEventData payload;

        public RitualPawnSignal(RitualFanoutSignal source, Pawn pawn, Pawn otherPawn, string perspective, string pawnId)
        {
            this.source = source;
            this.pawn = pawn;
            this.otherPawn = otherPawn;
            this.perspective = perspective;
            ritualRole = source.RoleLabelFor(pawn, perspective);
            payload = new RitualEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = source.DefName,
                Title = source.Title,
                BehaviorClass = source.BehaviorClass,
                Perspective = perspective,
                RitualRole = ritualRole,
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

            string context = RitualEventData.BuildGameContext(
                source.DefName, source.Title, source.BehaviorClass, perspective, ritualRole,
                DlcContext.RoyalTitle(pawn), DlcContext.IdeologicalRole(pawn),
                RitualFanoutSignal.RitualOutcomeFinished, source.Quality);
            string text = "PawnDiary.Event.RitualFinished"
                .Translate(pawn.LabelShortCap, source.Title, RitualFanoutSignal.RitualPerspectiveLabel(perspective), ritualRole)
                .Resolve();
            string instruction = RitualEventData.CombineInstructions(
                source.GroupInstruction,
                RitualFanoutSignal.RitualInstructionFor(perspective));

            DiaryEvent ritualEvent = sink.AddSoloEvent(pawn, otherPawn, source.DefName, source.Label, text, instruction, context);
            if (ritualEvent == null)
            {
                return;
            }

            source.NotifyPageCreated(sink);
            sink.QueueSolo(ritualEvent, DiaryEvent.InitiatorRole);
        }
    }
}
