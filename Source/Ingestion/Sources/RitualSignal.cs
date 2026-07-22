// Ritual ingestion signal — the impure capture+emit half of the "Ideology ritual finished" source
// (LordJob_Ritual.ApplyOutcome). Replaces the old DiaryGameComponent.RecordRitualFinished. A
// perspective FAN-OUT: one solo entry per organizer / target / participant / spectator, with a single
// ritual-level dedup window. The exact completed conversion family also freezes organizer-role and
// target-mutation DTOs here, after vanilla's outcome worker returned and before a page is persisted.
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
        private readonly ConversionRitualPolicySnapshot conversionPolicy;
        private readonly BeliefSourcePreceptFact conversionOrganizerRolePrecept;
        private readonly BeliefMutationSnapshot conversionTargetMutation;
        private RoyalMutationBatchSnapshot royaltyMutationBatch;
        private string royaltyMutationContext = string.Empty;
        // Ordinary rituals keep their unlimited existing fanout. Only the exact Odyssey launch
        // group replaces this with the XML-owned cap after passing its Odyssey cooldown.
        private readonly int maximumWriters = int.MaxValue;

        internal LordJob_Ritual RitualJob { get; }
        internal RitualRoleAssignments Assignments { get; }
        internal string DefName { get; }
        internal string Title { get; }
        internal string Label { get; }
        internal string BehaviorClass { get; }
        internal string OutcomeWorkerClass { get; }
        internal string Quality { get; }
        internal string GroupInstruction { get; }
        internal int EventTick { get; }

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

            // The Royalty policy master owns the canonical DLC ceremony pages as well as their
            // attached mutation context. Leaving the ordinary ritual page alive while the master is
            // disabled would make the switch only half-effective and transfer the same action to an
            // un-enriched route.
            if (!RoyaltyPolicyAllowsRitual(defName, DiaryRoyaltyPolicy.Snapshot())) return;

            BehaviorClass = RitualBehaviorClass(ritual);
            OutcomeWorkerClass = ritual.outcomeEffect?.GetType().Name ?? string.Empty;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(RitualClassifierKey(defName, BehaviorClass));
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            int eventTick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            EventTick = eventTick;
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
            Assignments = RitualAssignments(ritualJob);
            Pawn selectedOrganizer = ritualJob.Organizer;
            Pawn selectedTargetPawn = RitualTargetPawn(ritualJob);
            ConversionRitualPolicySnapshot candidatePolicy = DiaryConversionRitualPolicy.Snapshot();
            bool exactConversion = ConversionRitualPolicy.Matches(
                DefName, BehaviorClass, OutcomeWorkerClass, group.defName,
                ModsConfig.IdeologyActive, candidatePolicy);
            if (exactConversion)
            {
                // Conversion's selectedTarget is normally the ritual focus, not the convertee. The
                // installed role ids therefore own the exact organizer and target identities.
                selectedOrganizer = Assignments?.FirstAssignedPawn(candidatePolicy.organizerRoleId)
                    ?? selectedOrganizer;
                selectedTargetPawn = Assignments?.FirstAssignedPawn(candidatePolicy.targetRoleId)
                    ?? selectedTargetPawn;
                BeliefSourcePreceptFact rolePrecept;
                BeliefMutationSnapshot targetMutation;
                if (ConversionRitualEvidenceAdapter.TryCapture(
                    selectedOrganizer, selectedTargetPawn, eventTick, candidatePolicy,
                    out rolePrecept, out targetMutation))
                {
                    conversionPolicy = candidatePolicy;
                    conversionOrganizerRolePrecept = rolePrecept;
                    conversionTargetMutation = targetMutation;
                }
            }
            organizer = selectedOrganizer;
            targetPawn = selectedTargetPawn;
            colonyDedupKey = "ritual|" + defName + "|" + PawnKey(organizer) + "|" + PawnKey(targetPawn)
                + "|" + eventTick;
            Title = RitualTitle(ritualJob, ritual);
            Label = Title;
            Quality = RitualEventData.QualityLabel(progress, DiaryTuning.Current.ritualQualityBands);
            AttachRoyalMutationOwner(
                RoyalMutationRoutePolicy.RitualCause(DefName, DiaryRoyaltyPolicy.Snapshot()),
                CandidatePawnIds(organizer, targetPawn, Assignments?.Participants));
            valid = true;
        }

        /// <summary>
        /// Builds the canonical bestowing ritual fanout. Vanilla's bestowing LordJob invokes its
        /// outcome worker directly rather than LordJob_Ritual.ApplyOutcome, so the exact worker hook
        /// forwards the completed ceremony here after its title/psylink mutations finish.
        /// </summary>
        internal static RitualFanoutSignal CreateRoyalBestowing(
            Pawn bestower,
            Pawn target,
            List<Pawn> participants,
            float progress)
        {
            if (!DiaryGameComponent.GamePlaying || !ModsConfig.RoyaltyActive
                || bestower == null || target == null || PawnDiaryMod.Settings == null) return null;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            if (!policy.enabled) return null;
            string defName = FirstOrFallback(
                policy.bestowingRitualDefNames, "BestowingCeremony");
            string behavior = "RitualOutcomeEffectWorker_Bestowing";
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(
                RitualClassifierKey(defName, behavior));
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName)) return null;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string instruction = PromptVariants.Pick(
                group.instructions, group.instruction,
                PromptVariants.HashSeed(defName + "|" + tick));
            string title = DiaryLineCleaner.CleanLine("BestowingCeremonyLabel".Translate().Resolve());
            RitualFanoutSignal signal = new RitualFanoutSignal(
                bestower, target, participants, new List<Pawn>(), defName, title,
                behavior, string.Empty, progress, instruction);
            signal.AttachRoyalMutationOwner(
                RoyalMutationCauseTokens.ImperialBestowing,
                CandidatePawnIds(bestower, target, participants));
            return signal.valid ? signal : null;
        }

        /// <summary>
        /// Reports whether the canonical ritual group can own an exact bestowing/anima mutation.
        /// The mutation adapter calls this before staging a delayed owner so disabling Royal rituals
        /// consumes the action silently instead of leaking it into a later Progression fallback.
        /// </summary>
        internal static bool RoyalMutationOwnerEnabled(
            string causeToken,
            RoyaltyPolicySnapshot policy)
        {
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (!effective.enabled || PawnDiaryMod.Settings == null) return false;

            string defName;
            string behavior;
            if (causeToken == RoyalMutationCauseTokens.ImperialBestowing)
            {
                defName = FirstOrFallback(effective.bestowingRitualDefNames, "BestowingCeremony");
                behavior = "RitualOutcomeEffectWorker_Bestowing";
            }
            else if (causeToken == RoyalMutationCauseTokens.AnimaLinking)
            {
                defName = FirstOrFallback(effective.animaRitualDefNames, "AnimaTreeLinking");
                behavior = "RitualBehaviorWorker_AnimaLinking";
            }
            else
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(
                RitualClassifierKey(defName, behavior));
            return group != null && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
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
            string groupInstruction,
            string outcomeWorkerClass = "")
        {
            RitualFanoutSignal signal = new RitualFanoutSignal(
                organizer,
                targetPawn,
                participants,
                spectators,
                defName,
                title,
                behaviorClass,
                outcomeWorkerClass,
                progress,
                groupInstruction);
            // Keep this loaded-game seam on the production ownership path. A matching bestowing or
            // anima fixture must claim the same completed mutation batch that a live ritual would;
            // unrelated ritual fixtures simply resolve to the "unknown" route and attach nothing.
            signal.AttachRoyalMutationOwner(
                RoyalMutationRoutePolicy.RitualCause(defName, DiaryRoyaltyPolicy.Snapshot()),
                CandidatePawnIds(organizer, targetPawn, participants));
            return signal;
        }

        private RitualFanoutSignal(
            Pawn organizer,
            Pawn targetPawn,
            List<Pawn> participants,
            List<Pawn> spectators,
            string defName,
            string title,
            string behaviorClass,
            string outcomeWorkerClass,
            float progress,
            string groupInstruction)
        {
            if (!DiaryGameComponent.GamePlaying || string.IsNullOrWhiteSpace(defName))
            {
                return;
            }
            if (!RoyaltyPolicyAllowsRitual(defName, DiaryRoyaltyPolicy.Snapshot())) return;

            this.organizer = organizer;
            this.targetPawn = targetPawn;
            fixtureParticipants = participants == null ? new List<Pawn>() : new List<Pawn>(participants);
            fixtureSpectators = spectators == null ? new List<Pawn>() : new List<Pawn>(spectators);
            DefName = defName;
            Title = string.IsNullOrWhiteSpace(title) ? RitualEventData.FallbackTitle : title;
            Label = Title;
            BehaviorClass = behaviorClass ?? string.Empty;
            OutcomeWorkerClass = outcomeWorkerClass ?? string.Empty;
            Quality = RitualEventData.QualityLabel(progress, DiaryTuning.Current.ritualQualityBands);
            GroupInstruction = groupInstruction ?? string.Empty;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            EventTick = tick;
            colonyDedupKey = "ritual_fixture|" + defName + "|" + PawnKey(organizer) + "|"
                + PawnKey(targetPawn) + "|" + tick;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRitual(
                RitualClassifierKey(DefName, BehaviorClass));
            ConversionRitualPolicySnapshot candidatePolicy = DiaryConversionRitualPolicy.Snapshot();
            if (ConversionRitualPolicy.Matches(
                DefName, BehaviorClass, OutcomeWorkerClass, group?.defName,
                ModsConfig.IdeologyActive, candidatePolicy))
            {
                BeliefSourcePreceptFact rolePrecept;
                BeliefMutationSnapshot targetMutation;
                if (ConversionRitualEvidenceAdapter.TryCapture(
                    organizer, targetPawn, tick, candidatePolicy, out rolePrecept, out targetMutation))
                {
                    conversionPolicy = candidatePolicy;
                    conversionOrganizerRolePrecept = rolePrecept;
                    conversionTargetMutation = targetMutation;
                }
            }
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
                    // Vanilla's Participants list includes spectators. Preserve generic ritual behavior,
                    // but keep the exact conversion family's smaller spectator context on its real role.
                    if (conversionPolicy != null && Assignments?.SpectatorsForReading != null
                        && Assignments.SpectatorsForReading.Contains(participants[i])) continue;
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

        /// <summary>Builds a detached page-specific row; target mutation never crosses to other POVs.</summary>
        internal BeliefEventEvidence ConversionEvidenceFor(
            string pawnId, string pawnLabel, string perspective)
        {
            return conversionPolicy == null ? null : ConversionRitualPolicy.EvidenceFor(
                pawnId, EventTick, DefName, pawnLabel, perspective,
                conversionOrganizerRolePrecept,
                string.Equals(perspective, RitualEventData.PerspectiveTarget, StringComparison.Ordinal)
                    ? conversionTargetMutation
                    : null,
                conversionPolicy);
        }

        /// <summary>Adds exact role/result markers without copying target mechanics to another page.</summary>
        internal string ConversionContextFor(
            string gameContext, string perspective, BeliefEventEvidence evidence)
        {
            if (conversionPolicy == null) return gameContext ?? string.Empty;
            BeliefMutationSnapshot mutation = string.Equals(
                perspective, RitualEventData.PerspectiveTarget, StringComparison.Ordinal)
                    ? conversionTargetMutation
                    : null;
            string context = ConversionRitualPolicy.AppendGameContext(
                gameContext, perspective, mutation, conversionPolicy);
            return BeliefMutationEventSelector.AppendGameContextMarker(context, evidence);
        }

        /// <summary>
        /// Marks cooldown state after any saved child page, but claims a personal Royalty mutation
        /// only when the saved page belongs to the pawn whose title/psylink facts it carries.
        /// </summary>
        internal void NotifyPageCreated(DiaryGameComponent sink, string pagePawnId)
        {
            if (odysseyLaunchAuthorized)
            {
                sink?.MarkOdysseyLaunchPageAt(odysseyLaunchTick);
            }
            if (royaltyMutationBatch != null
                && string.Equals(royaltyMutationBatch.pawnId, pagePawnId, StringComparison.Ordinal)
                && RoyalMutationCorrelation.ClaimRitual(royaltyMutationBatch))
            {
                RoyalTitleMutationSnapshot title = royaltyMutationBatch.titleMutation;
                if (title != null)
                {
                    RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                    RoyalTitleThoughtCorrelation.Claim(
                        royaltyMutationBatch.pawnId,
                        title.previousTitle?.titleDefName,
                        title.newTitle?.titleDefName,
                        Find.TickManager?.TicksGame ?? 0,
                        policy.titleThoughtCorrelationTicks);
                }
            }
        }

        /// <summary>
        /// Returns enriched title/psylink facts only for the pawn whose mutation was captured. Ritual
        /// fanout remains one normal perspective page per eligible attendee, but their pages must not
        /// all claim the target's personal title/psylink transition.
        /// </summary>
        internal string RoyaltyMutationContextFor(string pawnId)
        {
            return royaltyMutationBatch != null
                && string.Equals(royaltyMutationBatch.pawnId, pawnId, StringComparison.Ordinal)
                ? royaltyMutationContext
                : string.Empty;
        }

        private void AttachRoyalMutationOwner(string causeToken, IList<string> candidatePawnIds)
        {
            if (!ModsConfig.RoyaltyActive || causeToken == RoyalMutationCauseTokens.Unknown) return;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            RoyalMutationBatchSnapshot batch = RoyalMutationCorrelation.PrepareRitualOwner(
                causeToken, candidatePawnIds, Find.TickManager?.TicksGame ?? 0, policy);
            if (batch == null) return;
            royaltyMutationBatch = batch;
            RoyalTitleMutationSnapshot title = batch.titleMutation;
            string transition = title == null
                ? RoyalTitleTransitionTokens.Invalid
                : RoyalTitleTransitionPolicy.Classify(
                    title.previousTitle, title.newTitle, true, true, policy).transitionToken;
            royaltyMutationContext = RoyalMutationContextFormatter.Format(
                batch, transition, policy.maximumRoyaltyContextCharacters,
                policy.maximumDutyCategoryTokens, includeOptionalDuties: true);
        }

        private static List<string> CandidatePawnIds(
            Pawn first,
            Pawn second,
            IEnumerable<Pawn> additional)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            AddCandidate(result, seen, first);
            AddCandidate(result, seen, second);
            if (additional != null)
                foreach (Pawn pawn in additional) AddCandidate(result, seen, pawn);
            return result;
        }

        private static void AddCandidate(List<string> result, HashSet<string> seen, Pawn pawn)
        {
            string id = pawn?.GetUniqueLoadID() ?? string.Empty;
            if (id.Length > 0 && seen.Add(id)) result.Add(id);
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

        private static string FirstOrFallback(IList<string> values, string fallback)
        {
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                    if (!string.IsNullOrWhiteSpace(values[i])) return values[i].Trim();
            }
            return fallback;
        }

        private static bool RoyaltyPolicyAllowsRitual(
            string ritualDefName,
            RoyaltyPolicySnapshot policy)
        {
            if (!ModsConfig.RoyaltyActive) return true;
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            return effective.enabled
                || RoyalMutationRoutePolicy.RitualCause(ritualDefName, effective)
                    == RoyalMutationCauseTokens.Unknown;
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
        private readonly BeliefEventEvidence conversionEvidence;

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
                Tick = source.EventTick,
                DefName = source.DefName,
                Title = source.Title,
                BehaviorClass = source.BehaviorClass,
                Perspective = perspective,
                RitualRole = ritualRole,
                Cancelled = false,
            };
            conversionEvidence = source.ConversionEvidenceFor(
                pawnId, pawn.LabelShortCap, perspective);
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
            string royaltyMutationContext = source.RoyaltyMutationContextFor(payload.PawnId);
            if (!string.IsNullOrWhiteSpace(royaltyMutationContext))
                context += "; " + royaltyMutationContext;
            context = source.ConversionContextFor(context, perspective, conversionEvidence);
            string text = "PawnDiary.Event.RitualFinished"
                .Translate(pawn.LabelShortCap, source.Title, RitualFanoutSignal.RitualPerspectiveLabel(perspective), ritualRole)
                .Resolve();
            string instruction = RitualEventData.CombineInstructions(
                source.GroupInstruction,
                RitualFanoutSignal.RitualInstructionFor(perspective));

            DiaryEvent ritualEvent = sink.AddSoloEvent(
                pawn, otherPawn, source.DefName, source.Label, text, instruction, context,
                conversionEvidence);
            if (ritualEvent == null)
            {
                return;
            }

            source.NotifyPageCreated(sink, payload.PawnId);
            sink.QueueSolo(ritualEvent, DiaryEvent.InitiatorRole);
        }
    }
}
