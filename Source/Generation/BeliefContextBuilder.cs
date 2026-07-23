// Impure Ideology Phase 1 orchestration. This is the one event-time bridge from a live Pawn to the
// pure resolver: it snapshots guarded doctrine through DlcContext, merges only plain recent history
// evidence, resolves exactly once, and returns a frozen full context block.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>One evaluated POV result, also carrying valence diagnostics needed by body-mod parity.</summary>
    internal sealed class BeliefContextBuildResult
    {
        public bool evaluated;
        public string fullContext = string.Empty;
        public BeliefStanceResolution resolution = new BeliefStanceResolution();
        public IdeologyNarrativeSnapshot ideologyNarrative;
        // The same bounded N1 history snapshot feeds both resolver-level precept diversity and the
        // shared candidate selector. Carrying it with a prepared body result avoids a second store scan.
        public List<string> recentSelectedCandidateKeys = new List<string>();

        public static BeliefContextBuildResult Empty(bool evaluated = false)
        {
            return new BeliefContextBuildResult { evaluated = evaluated };
        }
    }

    /// <summary>Builds one saved event-time belief block for one already-eligible POV.</summary>
    internal static class BeliefContextBuilder
    {
        public static BeliefContextBuildResult Build(
            Pawn pawn,
            BeliefEventEvidence sourceEvidence,
            string eventId,
            int eventTick,
            string povRole,
            IList<string> recentSelectedCandidateKeys = null)
        {
            try
            {
                if (!ModsConfig.IdeologyActive || pawn == null || sourceEvidence == null
                    || !DiaryGameComponent.IsDiaryEligible(pawn))
                    return BeliefContextBuildResult.Empty();

                BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
                if (!policy.enabled) return BeliefContextBuildResult.Empty();

                string pawnId = pawn.GetUniqueLoadID();
                BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForPov(
                    sourceEvidence, eventId, eventTick, pawnId, povRole);
                BeliefEventEvidenceFactory.AddHistoryDefNames(evidence,
                    BeliefHistoryCorrelationCache.NearbyDefNames(pawnId, eventTick, policy));
                if (!BeliefEventEvidenceFactory.HasUsefulVisibleEvidence(evidence))
                    return BeliefContextBuildResult.Empty();

                BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(pawn, policy);
                if (!snapshot.ideologyActive) return BeliefContextBuildResult.Empty();

                return Resolve(snapshot, evidence, eventId, pawnId, policy,
                    recentSelectedCandidateKeys, BeliefResolutionModeTokens.EventEnrichment);
            }
            catch (Exception exception)
            {
                // Belief context is optional enrichment. A broken modded getter or malformed Def must
                // not unwind through the source factory before its ordinary diary event is registered.
                Type type = exception.GetType();
                Log.WarningOnce(
                    "[Pawn Diary] Ideology belief-context enrichment failed; this page keeps ordinary context: "
                    + type.FullName + ": " + exception.Message,
                    ("PawnDiary.BeliefContextBuilder." + type.FullName).GetHashCode());
                return BeliefContextBuildResult.Empty();
            }
        }

        /// <summary>
        /// Resolves a developer preview from an already-detached snapshot. This exists so the suite
        /// can derive its synthetic evidence and final context from one live capture without retaining
        /// an Ideo/Precept or evaluating the resolver twice.
        /// </summary>
        public static BeliefContextBuildResult BuildSyntheticPreview(
            BeliefSnapshot snapshot,
            BeliefEventEvidence evidence,
            string eventId,
            string pawnId,
            BeliefPolicySnapshot policy)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            if (!effective.enabled || snapshot?.ideologyActive != true
                || !BeliefEventEvidenceFactory.HasUsefulVisibleEvidence(evidence))
                return BeliefContextBuildResult.Empty();
            return Resolve(snapshot, evidence, eventId, pawnId, effective, null,
                BeliefResolutionModeTokens.EventEnrichment);
        }

        /// <summary>
        /// Builds the primary saved context for one standalone belief reflection. The live doctrine is
        /// read once at rest; pending before/after facts come only from the saved Phase-3 state.
        /// </summary>
        public static BeliefContextBuildResult BuildReflection(
            Pawn pawn,
            PawnBeliefState saved,
            string eventId,
            int eventTick,
            string trigger,
            BeliefPolicySnapshot policy)
        {
            try
            {
                BeliefPolicySnapshot effective = policy ?? DiaryBeliefPolicy.Snapshot();
                if (!ModsConfig.IdeologyActive || pawn == null || saved == null || !effective.enabled)
                    return BeliefContextBuildResult.Empty();

                BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(pawn, effective);
                if (!snapshot.ideologyActive) return BeliefContextBuildResult.Empty();
                string pawnId = pawn.GetUniqueLoadID();
                BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                    pawnId,
                    eventTick,
                    "ideology",
                    "PawnBeliefReflection",
                    DiaryEvent.InitiatorRole,
                    saved.lastIdeologyName,
                    "belief_reflection");
                evidence.currentBeliefFactsRelevant = true;
                evidence.narrative.eventId = eventId ?? string.Empty;
                evidence.narrative.facet = NarrativeFacetTokens.IdentityTransition;
                evidence.narrative.phase = trigger ?? BeliefReflectionTriggerTokens.Quiet;
                evidence.narrative.subjectKind = NarrativeSubjectKindTokens.Pawn;
                evidence.narrative.subjectId = pawnId;
                evidence.narrative.salience = saved.pendingIdeologyChange
                    ? NarrativeSalienceTokens.Major
                    : NarrativeSalienceTokens.Meaningful;
                evidence.mutation = ReflectionMutation(saved, pawnId, eventTick);

                BeliefStanceResolution resolution = EventRelativeStanceResolver.Resolve(
                    new BeliefResolutionRequest
                    {
                        snapshot = snapshot,
                        evidence = evidence,
                        policy = effective,
                        mode = BeliefResolutionModeTokens.QuietReflection,
                        deterministicSeed = HumorChancePolicy.StableSeed(eventId, pawnId),
                        recentSelectionDefNames = new List<string>(
                            saved.recentSelectedPreceptDefNames ?? new List<string>())
                    });
                return new BeliefContextBuildResult
                {
                    evaluated = true,
                    resolution = resolution,
                    fullContext = BeliefContextFormatter.Format(
                        resolution, NarrativeDetailLevelTokens.Full, effective)
                };
            }
            catch (Exception exception)
            {
                Type type = exception.GetType();
                Log.WarningOnce(
                    "[Pawn Diary] Ideology belief-reflection context failed; no page was created: "
                    + type.FullName + ": " + exception.Message,
                    ("PawnDiary.BeliefContextBuilder.Reflection." + type.FullName).GetHashCode());
                return BeliefContextBuildResult.Empty();
            }
        }

        private static BeliefContextBuildResult Resolve(
            BeliefSnapshot snapshot,
            BeliefEventEvidence evidence,
            string eventId,
            string pawnId,
            BeliefPolicySnapshot policy,
            IList<string> recentSelectedCandidateKeys,
            string mode)
        {
            List<string> boundedRecentKeys = NarrativePersistencePolicy.NormalizeSelectedCandidateKeys(
                recentSelectedCandidateKeys);
            BeliefStanceResolution resolution = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = policy,
                    mode = mode,
                    deterministicSeed = HumorChancePolicy.StableSeed(eventId, pawnId),
                    recentSelectionDefNames = IdeologyNarrativeSelectionHistory.PreceptDefNames(
                        boundedRecentKeys, snapshot, policy.maximumRecentSelections)
                });
            return new BeliefContextBuildResult
            {
                evaluated = true,
                resolution = resolution,
                recentSelectedCandidateKeys = boundedRecentKeys,
                fullContext = BeliefContextFormatter.Format(
                    resolution, NarrativeDetailLevelTokens.Full, policy),
                ideologyNarrative = resolution.includeNarrativeInterpretation
                    ? IdeologyNarrativeSnapshotFactory.Create(
                        resolution,
                        evidence.narrative,
                        policy,
                        FormatInterpretationFact(resolution, policy))
                    : null
            };
        }

        private static BeliefMutationSnapshot ReflectionMutation(
            PawnBeliefState saved,
            string pawnId,
            int eventTick)
        {
            if (!saved.pendingIdeologyChange && !saved.hasPendingCertainty) return null;
            return new BeliefMutationSnapshot
            {
                pawnId = pawnId,
                capturedTick = Math.Max(0, eventTick),
                beforeIdeologyId = saved.pendingIdeologyChange
                    ? saved.pendingPreviousIdeologyId
                    : saved.lastIdeologyId,
                beforeIdeologyName = saved.pendingIdeologyChange
                    ? saved.pendingPreviousIdeologyName
                    : saved.lastIdeologyName,
                afterIdeologyId = saved.pendingIdeologyChange
                    ? saved.pendingCurrentIdeologyId
                    : saved.lastIdeologyId,
                afterIdeologyName = saved.pendingIdeologyChange
                    ? saved.pendingCurrentIdeologyName
                    : saved.lastIdeologyName,
                hasBeforeCertainty = saved.hasPendingCertainty,
                beforeCertainty = saved.pendingCertaintyBefore,
                hasAfterCertainty = saved.hasPendingCertainty,
                afterCertainty = saved.pendingCertaintyAfter,
                ideologyChanged = saved.pendingIdeologyChange,
                certaintyChanged = saved.hasPendingCertainty
            };
        }

        private static string FormatInterpretationFact(
            BeliefStanceResolution resolution,
            BeliefPolicySnapshot policy)
        {
            if (resolution?.stances == null || resolution.stances.Count == 0) return string.Empty;
            BeliefPreceptFact precept = resolution.stances[0]?.precept;
            string ideology = PromptTextSanitizer.LocalizedPromptText(resolution.ideologyName);
            string label = PromptTextSanitizer.LocalizedPromptText(precept?.displayLabel);
            string description = PromptTextSanitizer.LocalizedPromptText(precept?.description);
            if (ideology.Length == 0 || label.Length == 0) return string.Empty;

            // Components and DefInjected formats are already sanitized separately. Format only after
            // that step so periods in player-authored ideology/precept names cannot consume the shared
            // two-sentence sanitizer budget and cut the assembled fact in the middle.
            return IdeologyInterpretationFactFormatter.Format(
                DiaryBeliefPolicy.InterpretationFactFormat,
                DiaryBeliefPolicy.InterpretationFactWithoutDescriptionFormat,
                ideology,
                label,
                description,
                Math.Min(
                    IdeologyNarrativeSnapshotFactory.MaximumNarrativeTextCharacters,
                    policy.maximumDescriptionCharacters));
        }
    }
}
