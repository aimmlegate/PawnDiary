// Main-thread adapter for Narrative Continuity N3-A. Canonical Anomaly sources call this only after
// their existing pure plans have verified a visible result and created the one authoritative page.
// The adapter formats DefInjected factual prose, freezes plain evidence/provider facts, and invokes
// the shared selector; it never reads hidden creepjoiner, infection, entity, or terminal-void state.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers", "DLC-safety", and localization).
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Freezes source-authorized Anomaly evidence and optional provider context per exact POV.</summary>
    internal static class AnomalyNarrativeContextAdapter
    {
        /// <summary>Attaches exact breached-entity/map/call-scope evidence to one containment POV.</summary>
        public static void ApplyContainment(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            ContainmentEscapeFacts facts,
            ContainmentBreachPlan plan,
            AnomalyWriterSelection writer)
        {
            if (facts == null || plan == null || writer == null || !plan.valid || plan.escapedCount < 1
                || !WriterMatches(povPawn, writer) || string.IsNullOrWhiteSpace(facts.escapeId)
                || string.IsNullOrWhiteSpace(plan.dedupKey)) return;

            try
            {
                string visibleSubject = VisibleContainmentSubject(facts, plan);
                Apply(
                    sink,
                    diaryEvent,
                    povPawn,
                    povRole,
                    Fact(
                        AnomalyNarrativeContinuityTokens.ContainmentBreach,
                        NarrativeFacetTokens.AmbientPressure,
                        AnomalyNarrativeContinuityTokens.Breached,
                        NarrativeSubjectKindTokens.Entity,
                        facts.escapeId,
                        plan.dedupKey,
                        FormatNarrative(
                            AnomalyNarrativeContinuityTokens.ContainmentBreach,
                            AnomalyNarrativeContinuityTokens.Breached,
                            visibleSubject),
                        diaryEvent?.tick ?? facts.tick),
                    visibleSubject,
                    AnomalyNarrativeContinuityTokens.ContainmentSourceDomain,
                    AnomalyNarrativeContinuityTokens.ContainmentSourceDefName);
            }
            catch (Exception exception)
            {
                LogFailure(AnomalyNarrativeContinuityTokens.ContainmentBreach, exception);
            }
        }

        /// <summary>Attaches one verified visible rejection/aggression/departure fact to its exact POV.</summary>
        public static void ApplyCreepJoinerOutcome(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            CreepJoinerOutcomeFacts facts,
            CreepJoinerOutcomePlan plan)
        {
            if (facts == null || plan == null || plan.selectedWriter == null || !plan.valid
                || !plan.advanceArc || !facts.playerVisible || !facts.transitionVerified
                || !WriterMatches(povPawn, plan.selectedWriter)) return;
            try
            {
                ApplyCreepJoiner(
                    sink, diaryEvent, povPawn, povRole, facts.pawnId, facts.subjectLabel, plan.phase);
            }
            catch (Exception exception)
            {
                LogFailure(AnomalyNarrativeContinuityTokens.CreepJoinerOutcome, exception);
            }
        }

        /// <summary>Attaches one completed and visibly disclosed surgical fact to each exact selected POV.</summary>
        public static void ApplyCreepJoinerDisclosure(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            CreepJoinerSurgicalDisclosureFacts facts,
            CreepJoinerSurgicalDisclosurePlan plan,
            AnomalyWriterSelection writer)
        {
            if (facts == null || plan == null || writer == null || !plan.valid || !plan.advanceArc
                || !facts.surgeryCompleted || !facts.trackerDisclosureAppended || !facts.playerVisible
                || !WriterMatches(povPawn, writer)) return;
            try
            {
                ApplyCreepJoiner(
                    sink, diaryEvent, povPawn, povRole,
                    facts.subjectPawnId, facts.subjectLabel, plan.phase);
            }
            catch (Exception exception)
            {
                LogFailure(AnomalyNarrativeContinuityTokens.CreepJoinerOutcome, exception);
            }
        }

        /// <summary>Attaches one verified non-ghoul-to-ghoul identity fact to each exact selected POV.</summary>
        public static void ApplyGhoulTransformation(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            GhoulTransformationFacts facts,
            GhoulTransformationPlan plan,
            AnomalyWriterSelection writer)
        {
            if (facts == null || plan == null || writer == null || !plan.valid
                || !plan.transitionVerified || !facts.methodReturnedNormally || facts.wasGhoul
                || !facts.isGhoul || !facts.playerVisible || !WriterMatches(povPawn, writer)) return;
            try
            {
                string subjectLabel = PromptTextSanitizer.LocalizedPromptText(facts.subjectLabel);
                Apply(
                    sink,
                    diaryEvent,
                    povPawn,
                    povRole,
                    Fact(
                        AnomalyNarrativeContinuityTokens.GhoulTransformation,
                        NarrativeFacetTokens.IdentityTransition,
                        AnomalyNarrativeContinuityTokens.Transformed,
                        NarrativeSubjectKindTokens.Pawn,
                        facts.subjectPawnId,
                        string.Empty,
                        subjectLabel.Length == 0
                            ? string.Empty
                            : FormatNarrative(
                                AnomalyNarrativeContinuityTokens.GhoulTransformation,
                                AnomalyNarrativeContinuityTokens.Transformed,
                                subjectLabel),
                        diaryEvent?.tick ?? facts.tick),
                    subjectLabel,
                    AnomalyNarrativeContinuityTokens.GhoulSourceDomain,
                    AnomalyNarrativeContinuityTokens.GhoulSourceDefName);
            }
            catch (Exception exception)
            {
                LogFailure(AnomalyNarrativeContinuityTokens.GhoulTransformation, exception);
            }
        }

        /// <summary>
        /// Builds the optional provider snapshot for one exact XML-owned monolith chapter. The caller
        /// still owns and persists the event-window evidence independently when the localized format is blank.
        /// </summary>
        public static AnomalyNarrativeSnapshot MonolithSnapshot(
            Pawn povPawn,
            NarrativeEvidence evidence)
        {
            if (!ModsConfig.AnomalyActive || povPawn == null || evidence == null
                || evidence.sourceDomain != AnomalyNarrativeContinuityTokens.MonolithSourceDomain)
                return null;
            string povPawnId = povPawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(povPawnId)) return null;
            AnomalyNarrativeSnapshot snapshot = Snapshot(povPawnId);
            snapshot.facts.Add(Fact(
                AnomalyNarrativeContinuityTokens.MonolithChapter,
                evidence.facet,
                evidence.phase,
                evidence.subjectKind,
                evidence.subjectId,
                evidence.arcKey,
                FormatNarrative(
                    AnomalyNarrativeContinuityTokens.MonolithChapter,
                    evidence.phase),
                evidence.tick));
            return snapshot;
        }

        private static void ApplyCreepJoiner(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            string subjectPawnId,
            string subjectLabel,
            string phase)
        {
            string cleanedLabel = PromptTextSanitizer.LocalizedPromptText(subjectLabel);
            Apply(
                sink,
                diaryEvent,
                povPawn,
                povRole,
                Fact(
                    AnomalyNarrativeContinuityTokens.CreepJoinerOutcome,
                    NarrativeFacetTokens.IdentityTransition,
                    phase,
                    NarrativeSubjectKindTokens.Pawn,
                    subjectPawnId,
                    string.Empty,
                    cleanedLabel.Length == 0
                        ? string.Empty
                        : FormatNarrative(
                            AnomalyNarrativeContinuityTokens.CreepJoinerOutcome,
                            phase,
                            cleanedLabel),
                    diaryEvent?.tick ?? 0),
                cleanedLabel,
                AnomalyNarrativeContinuityTokens.CreepJoinerSourceDomain,
                AnomalyNarrativeContinuityTokens.CreepJoinerSourceDefName);
        }

        private static void Apply(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            AnomalyNarrativeFact fact,
            string subjectLabel,
            string sourceDomain,
            string sourceDefName)
        {
            if (!ModsConfig.AnomalyActive || sink == null || diaryEvent == null || povPawn == null
                || fact == null || string.IsNullOrWhiteSpace(povRole)) return;
            try
            {
                string povPawnId = povPawn.GetUniqueLoadID();
                NarrativeEvidence evidence = new NarrativeEvidence
                {
                    eventId = diaryEvent.eventId,
                    tick = diaryEvent.tick,
                    povPawnId = povPawnId,
                    povRole = povRole,
                    facet = fact.facet,
                    phase = fact.phase,
                    subjectKind = fact.subjectKind,
                    subjectId = fact.subjectId,
                    subjectLabel = subjectLabel ?? string.Empty,
                    arcKey = fact.arcKey,
                    beliefTopics = Topics(fact.sourceKind, fact.phase),
                    salience = Salience(fact.sourceKind, fact.phase),
                    pawnCanKnow = true,
                    sourceDomain = sourceDomain,
                    sourceDefName = sourceDefName
                };
                AnomalyNarrativeSnapshot snapshot = Snapshot(povPawnId);
                snapshot.facts.Add(fact);
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = povPawnId,
                        povRole = povRole,
                        evidence = new List<NarrativeEvidence> { evidence },
                        royalty = sink.RoyaltyNarrativeSnapshotFor(povPawn, diaryEvent.tick),
                        anomaly = snapshot,
                        odyssey = sink.OdysseyNarrativeSnapshotFor(povPawn, diaryEvent.tick),
                        recentSelectedCandidateKeys =
                            sink.RecentNarrativeSelectedCandidateKeys(povPawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full)
                    });
                if (result.evidence.Count > 0) diaryEvent.ApplyNarrativeContext(povRole, result);
            }
            catch (Exception exception)
            {
                // Optional enrichment cannot revoke the already-created canonical page.
                LogFailure(fact.sourceKind, exception);
            }
        }

        private static void LogFailure(string sourceKind, Exception exception)
        {
            Log.ErrorOnce(
                "[Pawn Diary] Anomaly Narrative Continuity enrichment failed; the canonical page remains: "
                + exception,
                ("PawnDiary.Anomaly.Narrative." + (sourceKind ?? string.Empty)).GetHashCode());
        }

        private static AnomalyNarrativeSnapshot Snapshot(string povPawnId)
        {
            return new AnomalyNarrativeSnapshot
            {
                providerAvailable = ModsConfig.AnomalyActive,
                povPawnId = povPawnId ?? string.Empty,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
        }

        private static AnomalyNarrativeFact Fact(
            string sourceKind,
            string facet,
            string phase,
            string subjectKind,
            string subjectId,
            string arcKey,
            string text,
            int sourceTick)
        {
            return new AnomalyNarrativeFact
            {
                sourceKind = sourceKind ?? string.Empty,
                facet = facet ?? string.Empty,
                phase = phase ?? string.Empty,
                subjectKind = subjectKind ?? string.Empty,
                subjectId = subjectId ?? string.Empty,
                arcKey = arcKey ?? string.Empty,
                text = text ?? string.Empty,
                sourceTick = Math.Max(0, sourceTick)
            };
        }

        private static string FormatNarrative(string sourceKind, string phase, params object[] values)
        {
            string format = DiaryAnomalyPolicy.NarrativeFormat(sourceKind, phase);
            if (string.IsNullOrWhiteSpace(format)) return string.Empty;
            try
            {
                return PromptTextSanitizer.LocalizedPromptText(string.Format(format, values));
            }
            catch (FormatException)
            {
                // A malformed custom translation disables only this optional lens.
                return string.Empty;
            }
        }

        private static bool WriterMatches(Pawn pawn, AnomalyWriterSelection writer)
        {
            return pawn != null && writer != null && string.Equals(
                pawn.GetUniqueLoadID(), writer.pawnId, StringComparison.Ordinal);
        }

        private static string VisibleContainmentSubject(
            ContainmentEscapeFacts facts,
            ContainmentBreachPlan plan)
        {
            for (int i = 0; i < facts.entities.Count; i++)
            {
                ContainedEntityFact entity = facts.entities[i];
                if (entity != null && entity.escaped && string.Equals(
                    entity.entityId, facts.escapeId, StringComparison.Ordinal))
                {
                    string label = PromptTextSanitizer.LocalizedPromptText(entity.visibleLabel);
                    if (label.Length > 0) return label;
                    break;
                }
            }
            string summary = PromptTextSanitizer.LocalizedPromptText(
                ContainmentBreachContextFormatter.VisibleEntitySummary(plan));
            return summary.Length > 0
                ? summary
                : "PawnDiary.Event.Anomaly.Containment.UnknownSubject".Translate().Resolve();
        }

        private static List<string> Topics(string sourceKind, string phase)
        {
            if (sourceKind == AnomalyNarrativeContinuityTokens.MonolithChapter)
                return new List<string> { "monolith", "void", "escalation" };
            if (sourceKind == AnomalyNarrativeContinuityTokens.ContainmentBreach)
                return new List<string> { "containment", "breach", "danger" };
            if (sourceKind == AnomalyNarrativeContinuityTokens.GhoulTransformation)
                return new List<string> { "identity", "body", "transformation" };
            List<string> result = new List<string> { "identity", "creepjoiner" };
            if (phase == AnomalyNarrativeContinuityTokens.SurgicalReveal) result.Add("disclosure");
            else if (phase == AnomalyNarrativeContinuityTokens.Rejected) result.Add("rejection");
            else if (phase == AnomalyNarrativeContinuityTokens.Aggressive) result.Add("hostility");
            else if (phase == AnomalyNarrativeContinuityTokens.Departed) result.Add("departure");
            return result;
        }

        private static string Salience(string sourceKind, string phase)
        {
            return sourceKind == AnomalyNarrativeContinuityTokens.CreepJoinerOutcome
                    && phase == AnomalyNarrativeContinuityTokens.SurgicalReveal
                ? NarrativeSalienceTokens.Meaningful
                : NarrativeSalienceTokens.Major;
        }
    }
}
