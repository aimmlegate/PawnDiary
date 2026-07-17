// Pure provider orchestration for Narrative Continuity. Runtime adapters copy guarded DLC state into
// these plain snapshots; this file turns those facts into optional candidates without touching Pawn,
// DefDatabase, settings, localization, or any paid-DLC type.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable Biotech family-continuity states understood by the first real provider.</summary>
    internal static class BiotechNarrativeContinuityTokens
    {
        public const string SinceBirth = "since_birth";
        public const string ObservedChildhood = "observed_childhood";
        public const string BaselineFamily = "baseline_family";
    }

    /// <summary>
    /// Plain, event-time Biotech facts. Text fields are already localized and sanitized by the main-thread
    /// adapter; stable IDs and tokens remain prose-free for matching and deterministic candidate keys.
    /// </summary>
    internal class BiotechNarrativeSnapshot
    {
        public bool providerAvailable;
        public string povPawnId = string.Empty;
        public string childId = string.Empty;
        public string familyArcId = string.Empty;
        public string familyContinuity = string.Empty;
        public string familyText = string.Empty;
        public string xenotypeDefName = string.Empty;
        public string identityText = string.Empty;
        public int sourceTick;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>Stable Odyssey journey phases saved into Narrative Continuity references.</summary>
    internal static class OdysseyNarrativePhaseTokens
    {
        public const string Departed = "departed";
        public const string Arrived = "arrived";
        public const string Returned = "returned";
    }

    /// <summary>
    /// Plain event-time Odyssey home facts. The runtime adapter authorizes the exact POV/gravship
    /// connection and freezes localized text before this contract reaches pure provider policy.
    /// </summary>
    internal class OdysseyNarrativeSnapshot
    {
        public bool providerAvailable;
        public string povPawnId = string.Empty;
        public string shipStableId = string.Empty;
        public string shipName = string.Empty;
        public string journeyId = string.Empty;
        public string locationKey = string.Empty;
        public string locationLabel = string.Empty;
        public string homeText = string.Empty;
        public int sourceTick;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>Pure candidate factory for one exact, currently occupied Odyssey mobile home.</summary>
    internal static class OdysseyNarrativeProvider
    {
        /// <summary>
        /// Returns at most one home lens. A matching journey arc promotes it to exact-arc relevance;
        /// otherwise the selector can use it only as a verified ambient fact for this exact POV.
        /// </summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            OdysseyNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            if (snapshot == null || !snapshot.providerAvailable || !snapshot.pawnCanKnow
                || !snapshot.hasVerifiedPovConnection || !CouldApply(evidence, snapshot.povPawnId)
                || string.IsNullOrWhiteSpace(snapshot.povPawnId)
                || string.IsNullOrWhiteSpace(snapshot.shipStableId)
                || string.IsNullOrWhiteSpace(snapshot.locationKey)
                || string.IsNullOrWhiteSpace(snapshot.locationLabel)
                || string.IsNullOrWhiteSpace(snapshot.homeText))
            {
                return result;
            }

            result.Add(new NarrativeLensCandidate
            {
                candidateKey = "odyssey|home|" + snapshot.shipStableId.Trim()
                    + "|" + snapshot.locationKey.Trim(),
                provider = NarrativeProviderTokens.Odyssey,
                category = NarrativeCategoryTokens.Home,
                text = snapshot.homeText.Trim(),
                facet = NarrativeFacetTokens.JourneyChapter,
                subjectKind = NarrativeSubjectKindTokens.Ship,
                subjectId = snapshot.shipStableId.Trim(),
                arcKey = (snapshot.journeyId ?? string.Empty).Trim(),
                topicTokens = new List<string> { "home", "journey", "location" },
                sourceTick = snapshot.sourceTick,
                salience = NarrativeSalienceTokens.Meaningful,
                relationship = NarrativeRelationshipTokens.Ambient,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true
            });
            return result;
        }

        private static bool CouldApply(List<NarrativeEvidence> evidence, string povPawnId)
        {
            if (evidence == null || string.IsNullOrWhiteSpace(povPawnId)) return false;
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (row != null && row.pawnCanKnow == true
                    && string.Equals(row.povPawnId, povPawnId, StringComparison.Ordinal)
                    && NarrativeFacetTokens.IsKnown(row.facet))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Pure source-emission helpers for Odyssey departure and landing references. They authorize no
    /// page; O1.4 supplies them only after its source-specific ownership transaction succeeds.
    /// </summary>
    internal static class OdysseyNarrativeEvidenceFactory
    {
        public const string SourceDomain = "odyssey_journey";

        /// <summary>Builds the one ship reference for an exactly committed departure.</summary>
        public static List<NarrativeEvidence> Departure(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            string journeyId,
            string shipId,
            string shipLabel,
            string sourceDefName,
            bool pawnCanKnow)
        {
            List<NarrativeEvidence> result = new List<NarrativeEvidence>();
            NarrativeEvidence ship = Create(
                eventId, tick, povPawnId, povRole, OdysseyNarrativePhaseTokens.Departed,
                NarrativeSubjectKindTokens.Ship, shipId, shipLabel, journeyId,
                string.Empty, sourceDefName, pawnCanKnow);
            if (ship != null) result.Add(ship);
            return result;
        }

        /// <summary>
        /// Builds the exact ship reference plus an optional visible-place reference for one successful
        /// arrival. Homecoming is expressed as returned; every other successful finish is arrived.
        /// </summary>
        public static List<NarrativeEvidence> Landing(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            string journeyId,
            string shipId,
            string shipLabel,
            string placeId,
            string placeLabel,
            string relatedDepartureEventId,
            string sourceDefName,
            bool returned,
            bool pawnCanKnow)
        {
            List<NarrativeEvidence> result = new List<NarrativeEvidence>();
            string phase = returned ? OdysseyNarrativePhaseTokens.Returned : OdysseyNarrativePhaseTokens.Arrived;
            NarrativeEvidence ship = Create(
                eventId, tick, povPawnId, povRole, phase,
                NarrativeSubjectKindTokens.Ship, shipId, shipLabel, journeyId,
                relatedDepartureEventId, sourceDefName, pawnCanKnow);
            if (ship == null) return result;
            result.Add(ship);

            NarrativeEvidence place = Create(
                eventId, tick, povPawnId, povRole, phase,
                NarrativeSubjectKindTokens.Place, placeId, placeLabel, journeyId,
                relatedDepartureEventId, sourceDefName, pawnCanKnow);
            if (place != null) result.Add(place);
            return result;
        }

        private static NarrativeEvidence Create(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            string phase,
            string subjectKind,
            string subjectId,
            string subjectLabel,
            string journeyId,
            string relatedEventId,
            string sourceDefName,
            bool pawnCanKnow)
        {
            if (string.IsNullOrWhiteSpace(eventId) || tick < 0
                || string.IsNullOrWhiteSpace(povPawnId)
                || string.IsNullOrWhiteSpace(journeyId)
                || string.IsNullOrWhiteSpace(subjectId))
            {
                return null;
            }

            return new NarrativeEvidence
            {
                eventId = eventId.Trim(),
                tick = tick,
                povPawnId = povPawnId.Trim(),
                povRole = (povRole ?? string.Empty).Trim(),
                facet = NarrativeFacetTokens.JourneyChapter,
                phase = phase,
                subjectKind = subjectKind,
                subjectId = subjectId.Trim(),
                subjectLabel = (subjectLabel ?? string.Empty).Trim(),
                arcKey = journeyId.Trim(),
                relatedEventId = (relatedEventId ?? string.Empty).Trim(),
                beliefTopics = new List<string> { "journey", "home", "location" },
                salience = NarrativeSalienceTokens.Major,
                pawnCanKnow = pawnCanKnow,
                sourceDomain = SourceDomain,
                sourceDefName = (sourceDefName ?? string.Empty).Trim()
            };
        }
    }

    /// <summary>Pure candidate factory for visible Biotech family and current identity facts.</summary>
    internal static class BiotechNarrativeProvider
    {
        /// <summary>
        /// Classifies a saved family arc without inferring emotion or kinship. An exact birth is strongest;
        /// observed upbringing follows; a current exact parent relation is the quiet baseline.
        /// </summary>
        public static string FamilyContinuity(
            bool birthKnown,
            bool hasObservedUpbringing,
            bool hasExactFamilyConnection)
        {
            if (birthKnown) return BiotechNarrativeContinuityTokens.SinceBirth;
            if (hasObservedUpbringing) return BiotechNarrativeContinuityTokens.ObservedChildhood;
            return hasExactFamilyConnection ? BiotechNarrativeContinuityTokens.BaselineFamily : string.Empty;
        }

        /// <summary>Returns candidates only when authorized evidence exactly reaches this snapshot.</summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            BiotechNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            if (snapshot == null || !snapshot.providerAvailable || !snapshot.pawnCanKnow
                || !snapshot.hasVerifiedPovConnection || !CouldApply(evidence, snapshot))
            {
                return result;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.familyArcId)
                && !string.IsNullOrWhiteSpace(snapshot.familyContinuity)
                && !string.IsNullOrWhiteSpace(snapshot.familyText))
            {
                result.Add(new NarrativeLensCandidate
                {
                    candidateKey = "biotech|family|" + snapshot.familyArcId,
                    provider = NarrativeProviderTokens.Biotech,
                    category = NarrativeCategoryTokens.Bond,
                    text = snapshot.familyText.Trim(),
                    facet = NarrativeFacetTokens.IdentityTransition,
                    subjectKind = NarrativeSubjectKindTokens.Family,
                    subjectId = snapshot.familyArcId,
                    arcKey = snapshot.familyArcId,
                    topicTokens = new List<string> { "family" },
                    sourceTick = snapshot.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = NarrativeRelationshipTokens.ExactArc,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            if (!string.IsNullOrWhiteSpace(snapshot.childId)
                && !string.IsNullOrWhiteSpace(snapshot.xenotypeDefName)
                && !string.IsNullOrWhiteSpace(snapshot.identityText))
            {
                result.Add(new NarrativeLensCandidate
                {
                    candidateKey = "biotech|identity|" + snapshot.childId + "|" + snapshot.xenotypeDefName.Trim(),
                    provider = NarrativeProviderTokens.Biotech,
                    category = NarrativeCategoryTokens.Identity,
                    text = snapshot.identityText.Trim(),
                    facet = NarrativeFacetTokens.IdentityTransition,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = snapshot.childId,
                    topicTokens = new List<string> { "identity" },
                    sourceTick = snapshot.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = NarrativeRelationshipTokens.ExactSubject,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            return result;
        }

        private static bool CouldApply(List<NarrativeEvidence> evidence, BiotechNarrativeSnapshot snapshot)
        {
            if (evidence == null) return false;
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (row == null || row.pawnCanKnow != true
                    || row.facet != NarrativeFacetTokens.IdentityTransition)
                {
                    continue;
                }

                if ((!string.IsNullOrWhiteSpace(snapshot.familyArcId)
                        && row.arcKey == snapshot.familyArcId)
                    || (!string.IsNullOrWhiteSpace(snapshot.childId)
                        && row.subjectKind == NarrativeSubjectKindTokens.Pawn
                        && row.subjectId == snapshot.childId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Fixed provider list. Empty calls are intentional N2 stubs: each later source wave replaces only
    /// its own row, and an absent DLC/provider remains the ordinary zero-candidate path.
    /// </summary>
    internal static class NarrativeProviderOrchestrator
    {
        public static List<NarrativeLensCandidate> Collect(
            List<NarrativeEvidence> evidence,
            List<NarrativeLensCandidate> coreCandidates,
            BiotechNarrativeSnapshot biotech,
            OdysseyNarrativeSnapshot odyssey)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            Add(result, coreCandidates); // Core/synthetic fixtures remain first and deterministic.
            Add(result, RoyaltyCandidates());
            Add(result, IdeologyCandidates());
            Add(result, BiotechNarrativeProvider.Build(evidence, biotech));
            Add(result, AnomalyCandidates());
            Add(result, OdysseyNarrativeProvider.Build(evidence, odyssey));
            return result;
        }

        private static List<NarrativeLensCandidate> RoyaltyCandidates() => new List<NarrativeLensCandidate>();
        private static List<NarrativeLensCandidate> IdeologyCandidates() => new List<NarrativeLensCandidate>();
        private static List<NarrativeLensCandidate> AnomalyCandidates() => new List<NarrativeLensCandidate>();
        private static void Add(List<NarrativeLensCandidate> destination, List<NarrativeLensCandidate> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null) destination.Add(source[i]);
            }
        }
    }
}
