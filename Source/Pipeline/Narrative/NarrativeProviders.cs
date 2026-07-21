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
        // N3-B may describe one XML-selected salient gene theme instead of the broad xenotype. This
        // stable, prose-free key is what repetition policy persists; N2 callers may leave it empty and
        // retain the xenotype defName fallback.
        public string identityStableKey = string.Empty;
        public string identityText = string.Empty;
        public List<string> identityTopicTokens = new List<string>();
        public int sourceTick;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>One current persona-bond fact detached from its saved Royalty state.</summary>
    internal sealed class RoyaltyPersonaNarrativeFact
    {
        public string weaponThingId = string.Empty;
        public string weaponName = string.Empty;
        public int bondEpoch;
        public string arcKey = string.Empty;
        public string text = string.Empty;
        public int sourceTick;
    }

    /// <summary>One current faction-specific title fact detached from all live title objects.</summary>
    internal sealed class RoyaltyTitleNarrativeFact
    {
        public string factionId = string.Empty;
        public string titleDefName = string.Empty;
        public string text = string.Empty;
        public List<string> dutyCategoryTokens = new List<string>();
        public int sourceTick;
    }

    /// <summary>One bounded, exact active Royal Ascent pressure fact detached from saved window state.</summary>
    internal sealed class RoyaltyCourtPressureNarrativeFact
    {
        public string arcPrefix = string.Empty;
        public string arcKey = string.Empty;
        public string text = string.Empty;
        public int sourceTick;
    }

    /// <summary>
    /// Plain event-time Royalty facts. The main-thread adapter proves that every row belongs to the
    /// exact POV; pure policy still requires source-owned evidence before proposing any lens.
    /// </summary>
    internal sealed class RoyaltyNarrativeSnapshot
    {
        public bool providerAvailable;
        public string povPawnId = string.Empty;
        public List<RoyaltyPersonaNarrativeFact> personaBonds =
            new List<RoyaltyPersonaNarrativeFact>();
        public List<RoyaltyTitleNarrativeFact> titles =
            new List<RoyaltyTitleNarrativeFact>();
        public RoyaltyCourtPressureNarrativeFact courtPressure;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>Pure Royalty provider for exact persona-bond and title identity continuity.</summary>
    internal static class RoyaltyNarrativeProvider
    {
        // These defensive caps limit malformed or mod-expanded snapshots before the shared selector's
        // XML maxCandidates cap. They are schema safety limits, not tunable narrative policy.
        private const int MaximumPersonaCandidates = 4;
        private const int MaximumTitleCandidates = 4;
        private const int MaximumDutyTopics = 4;

        /// <summary>
        /// Returns only facts that match the exact evidence arc/subject grammar. Input order never
        /// affects output order, so save list order cannot change deterministic selection.
        /// </summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            RoyaltyNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> persona = new List<NarrativeLensCandidate>();
            List<NarrativeLensCandidate> titles = new List<NarrativeLensCandidate>();
            List<NarrativeLensCandidate> pressure = new List<NarrativeLensCandidate>();
            if (!Usable(snapshot) || evidence == null) return persona;

            List<RoyaltyPersonaNarrativeFact> personaFacts = snapshot.personaBonds
                ?? new List<RoyaltyPersonaNarrativeFact>();
            for (int i = 0; i < personaFacts.Count; i++)
            {
                RoyaltyPersonaNarrativeFact fact = personaFacts[i];
                if (!ValidPersona(fact) || !PersonaApplies(evidence, snapshot.povPawnId, fact)) continue;
                string candidateKey = "royalty|persona|" + fact.weaponThingId.Trim() + "|" + fact.bondEpoch;
                if (ContainsCandidateKey(persona, candidateKey)) continue;
                persona.Add(new NarrativeLensCandidate
                {
                    candidateKey = candidateKey,
                    provider = NarrativeProviderTokens.Royalty,
                    category = NarrativeCategoryTokens.Bond,
                    text = fact.text.Trim(),
                    facet = NarrativeFacetTokens.BondLifecycle,
                    subjectKind = NarrativeSubjectKindTokens.Weapon,
                    subjectId = fact.weaponThingId.Trim(),
                    arcKey = fact.arcKey.Trim(),
                    topicTokens = new List<string> { "weapons", "bonding", "loyalty" },
                    sourceTick = fact.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = NarrativeRelationshipTokens.ExactArc,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            RoyaltyCourtPressureNarrativeFact court = snapshot.courtPressure;
            bool exactPressureArc;
            if (ValidPressure(court)
                && PressureApplies(evidence, snapshot.povPawnId, court.arcKey, out exactPressureArc))
            {
                pressure.Add(new NarrativeLensCandidate
                {
                    candidateKey = "royalty|ascent-pressure|" + court.arcKey.Trim(),
                    provider = NarrativeProviderTokens.Royalty,
                    category = NarrativeCategoryTokens.Pressure,
                    text = court.text.Trim(),
                    facet = NarrativeFacetTokens.AmbientPressure,
                    subjectKind = NarrativeSubjectKindTokens.Colony,
                    subjectId = "royal_ascent",
                    arcKey = court.arcKey.Trim(),
                    topicTokens = new List<string> { "authority", "status", "duty", "hospitality" },
                    sourceTick = court.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = exactPressureArc
                        ? NarrativeRelationshipTokens.ExactArc
                        : NarrativeRelationshipTokens.DirectTopic,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            List<RoyaltyTitleNarrativeFact> titleFacts = snapshot.titles
                ?? new List<RoyaltyTitleNarrativeFact>();
            for (int i = 0; i < titleFacts.Count; i++)
            {
                RoyaltyTitleNarrativeFact fact = titleFacts[i];
                if (!ValidTitle(fact) || !TitleApplies(evidence, snapshot.povPawnId)) continue;
                string candidateKey = "royalty|title|" + snapshot.povPawnId.Trim() + "|"
                    + fact.factionId.Trim() + "|" + fact.titleDefName.Trim();
                if (ContainsCandidateKey(titles, candidateKey)) continue;
                titles.Add(new NarrativeLensCandidate
                {
                    candidateKey = candidateKey,
                    provider = NarrativeProviderTokens.Royalty,
                    category = NarrativeCategoryTokens.Identity,
                    text = fact.text.Trim(),
                    facet = NarrativeFacetTokens.IdentityTransition,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = snapshot.povPawnId.Trim(),
                    topicTokens = TitleTopics(fact.dutyCategoryTokens),
                    sourceTick = fact.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = NarrativeRelationshipTokens.ExactSubject,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            persona.Sort(CompareCandidateKeys);
            titles.Sort(CompareCandidateKeys);
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            AddBounded(result, persona, MaximumPersonaCandidates);
            AddBounded(result, titles, MaximumTitleCandidates);
            AddBounded(result, pressure, 1);
            return result;
        }

        private static bool Usable(RoyaltyNarrativeSnapshot snapshot)
        {
            return snapshot != null && snapshot.providerAvailable && snapshot.pawnCanKnow
                && snapshot.hasVerifiedPovConnection
                && SafeKeyPart(snapshot.povPawnId);
        }

        private static bool ValidPersona(RoyaltyPersonaNarrativeFact fact)
        {
            if (fact == null || fact.bondEpoch < 1 || string.IsNullOrWhiteSpace(fact.weaponThingId)
                || string.IsNullOrWhiteSpace(fact.text)) return false;
            string weaponId = fact.weaponThingId.Trim();
            return SafeKeyPart(weaponId) && string.Equals(
                fact.arcKey,
                "royalty-persona|" + weaponId + "|" + fact.bondEpoch,
                StringComparison.Ordinal);
        }

        private static bool ValidTitle(RoyaltyTitleNarrativeFact fact)
        {
            return fact != null && SafeKeyPart(fact.factionId)
                && SafeKeyPart(fact.titleDefName)
                && !string.IsNullOrWhiteSpace(fact.text);
        }

        private static bool ValidPressure(RoyaltyCourtPressureNarrativeFact fact)
        {
            if (fact == null || string.IsNullOrWhiteSpace(fact.text)) return false;

            string prefix = (fact.arcPrefix ?? string.Empty).Trim();
            string arcKey = (fact.arcKey ?? string.Empty).Trim();
            if (!SafePressureToken(prefix) || !arcKey.StartsWith(prefix + "|", StringComparison.Ordinal))
                return false;
            return SafePressureToken(arcKey.Substring(prefix.Length + 1));
        }

        private static bool SafePressureToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string cleaned = value.Trim();
            return cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0
                && cleaned.IndexOf('\r') < 0 && cleaned.IndexOf('\n') < 0;
        }

        private static bool SafeKeyPart(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Trim().IndexOf('|') < 0;
        }

        private static bool PersonaApplies(
            List<NarrativeEvidence> evidence,
            string povPawnId,
            RoyaltyPersonaNarrativeFact fact)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (!SameKnownPov(row, povPawnId) || row.facet != NarrativeFacetTokens.BondLifecycle)
                    continue;
                if (row.subjectKind == NarrativeSubjectKindTokens.Weapon
                    && string.Equals(row.subjectId, fact.weaponThingId, StringComparison.Ordinal)
                    && string.Equals(row.arcKey, fact.arcKey, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool TitleApplies(List<NarrativeEvidence> evidence, string povPawnId)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (!SameKnownPov(row, povPawnId)
                    || row.facet != NarrativeFacetTokens.IdentityTransition
                    || row.subjectKind != NarrativeSubjectKindTokens.Pawn
                    || !string.Equals(row.subjectId, povPawnId, StringComparison.Ordinal)) continue;
                if (row.sourceDomain == "royalty_title" || HasRoyaltyTitleTopic(row.beliefTopics))
                    return true;
            }
            return false;
        }

        private static bool PressureApplies(
            List<NarrativeEvidence> evidence,
            string povPawnId,
            string arcKey,
            out bool exactArc)
        {
            exactArc = false;
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (!SameKnownPov(row, povPawnId)) continue;
                if (string.Equals(row.arcKey, arcKey, StringComparison.Ordinal))
                {
                    exactArc = true;
                    return true;
                }
                if (HasRoyalAuthorityTopic(row.beliefTopics)) return true;
            }
            return false;
        }

        private static bool SameKnownPov(NarrativeEvidence row, string povPawnId)
        {
            return row != null && row.pawnCanKnow == true
                && string.Equals(row.povPawnId, povPawnId, StringComparison.Ordinal);
        }

        private static bool HasRoyaltyTitleTopic(List<string> topics)
        {
            if (topics == null) return false;
            for (int i = 0; i < topics.Count; i++)
                if (topics[i] == "authority" || topics[i] == "status" || topics[i] == "duty") return true;
            return false;
        }

        private static bool HasRoyalAuthorityTopic(List<string> topics)
        {
            if (topics == null) return false;
            for (int i = 0; i < topics.Count; i++)
            {
                string topic = topics[i];
                if (topic == "authority" || topic == "status" || topic == "duty"
                    || topic == "hospitality") return true;
            }
            return false;
        }

        private static List<string> TitleTopics(List<string> dutyTopics)
        {
            List<string> result = new List<string> { "authority", "status", "duty" };
            if (dutyTopics == null) return result;
            List<string> sorted = new List<string>();
            for (int i = 0; i < dutyTopics.Count; i++)
            {
                string token = (dutyTopics[i] ?? string.Empty).Trim();
                if (token.Length > 0 && token.IndexOf('|') < 0 && !sorted.Contains(token)) sorted.Add(token);
            }
            sorted.Sort(StringComparer.Ordinal);
            for (int i = 0; i < sorted.Count && i < MaximumDutyTopics; i++) result.Add(sorted[i]);
            return result;
        }

        private static int CompareCandidateKeys(NarrativeLensCandidate left, NarrativeLensCandidate right)
        {
            return string.CompareOrdinal(left?.candidateKey, right?.candidateKey);
        }

        private static bool ContainsCandidateKey(List<NarrativeLensCandidate> source, string key)
        {
            for (int i = 0; i < source.Count; i++)
                if (string.Equals(source[i]?.candidateKey, key, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void AddBounded(
            List<NarrativeLensCandidate> destination,
            List<NarrativeLensCandidate> source,
            int maximum)
        {
            for (int i = 0; i < source.Count && i < maximum; i++) destination.Add(source[i]);
        }
    }

    /// <summary>Stable N3-A source and phase tokens; none can encode hidden Anomaly configuration.</summary>
    internal static class AnomalyNarrativeContinuityTokens
    {
        public const string GhoulTransformation = "ghoul_transformation";
        public const string ContainmentBreach = "containment_breach";
        public const string MonolithChapter = "monolith_chapter";
        public const string CreepJoinerOutcome = "creepjoiner_outcome";

        public const string Transformed = "transformed";
        public const string Breached = "breached";
        public const string Stirring = "stirring";
        public const string Waking = "waking";
        public const string VoidAwakened = "void_awakened";
        public const string SurgicalReveal = "surgical_reveal";
        public const string Rejected = "rejected";
        public const string Aggressive = "aggressive";
        public const string Departed = "departed";

        public const string GhoulSourceDomain = "anomaly_ghoul";
        public const string ContainmentSourceDomain = "anomaly_containment";
        public const string MonolithSourceDomain = "anomaly_monolith";
        public const string CreepJoinerSourceDomain = "anomaly_creepjoiner";

        public const string GhoulSourceDefName = "PawnDiary_GhoulTransformation";
        public const string ContainmentSourceDefName = "PawnDiary_ContainmentBreach";
        public const string CreepJoinerSourceDefName = "PawnDiary_CreepJoinerOutcome";
        public const string MonolithStirringSourceDefName = "Stirring";
        public const string MonolithWakingSourceDefName = "Waking";
        public const string MonolithVoidAwakenedSourceDefName = "VoidAwakened";
        public const string MonolithStirringWindowDefName = "VoidMonolithActivation";
        public const string MonolithWakingWindowDefName = "VoidMonolithWaking";
        public const string MonolithVoidAwakenedWindowDefName = "VoidMonolithVoidAwakened";

        /// <summary>Accepts only the three shipped window/phase/reached-level identities.</summary>
        public static bool MatchesVisibleMonolithSource(
            string windowDefName,
            string phase,
            string sourceDefName)
        {
            if (!MatchesVisibleMonolithPhaseSource(phase, sourceDefName)) return false;
            if (phase == Stirring) return windowDefName == MonolithStirringWindowDefName;
            if (phase == Waking) return windowDefName == MonolithWakingWindowDefName;
            return windowDefName == MonolithVoidAwakenedWindowDefName;
        }

        /// <summary>
        /// Accepts only the reached-level Def that belongs to one visible monolith phase. The event-window
        /// adapter additionally checks the owning window identity before it creates source evidence.
        /// </summary>
        public static bool MatchesVisibleMonolithPhaseSource(string phase, string sourceDefName)
        {
            if (phase == Stirring) return sourceDefName == MonolithStirringSourceDefName;
            if (phase == Waking) return sourceDefName == MonolithWakingSourceDefName;
            return phase == VoidAwakened && sourceDefName == MonolithVoidAwakenedSourceDefName;
        }
    }

    /// <summary>
    /// One already-visible Anomaly fact detached from its source page. It has no field for benefit,
    /// downside, infection, infiltrator identity, tracker configuration, or an unresolved outcome.
    /// </summary>
    internal sealed class AnomalyNarrativeFact
    {
        public string sourceKind = string.Empty;
        public string facet = string.Empty;
        public string phase = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string arcKey = string.Empty;
        public string text = string.Empty;
        public int sourceTick;
    }

    /// <summary>
    /// Plain, event-time input for the N3-A Anomaly provider. Main-thread source adapters add only facts
    /// already authorized for this exact POV; pure policy rechecks every identity and source mapping.
    /// </summary>
    internal sealed class AnomalyNarrativeSnapshot
    {
        public bool providerAvailable;
        public string povPawnId = string.Empty;
        public List<AnomalyNarrativeFact> facts = new List<AnomalyNarrativeFact>();
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>Pure spoiler-safe provider for exact visible Anomaly identity, chapter, and pressure facts.</summary>
    internal static class AnomalyNarrativeProvider
    {
        // These are defensive memory/output ceilings, not narrative tuning. The shared XML policy owns
        // the final cross-provider candidate and selected-lens caps.
        private const int MaximumIdentityCandidates = 4;
        private const int MaximumChapterCandidates = 3;
        private const int MaximumPressureCandidates = 1;
        private const int MaximumTextCharacters = 512;
        private const int MaximumStableSegmentCharacters = 160;

        /// <summary>
        /// Returns only candidates whose exact POV, source, facet, phase, subject, and/or arc are all
        /// repeated by source-owned evidence. Output is bounded and sorted independently of input order.
        /// </summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            AnomalyNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> identity = new List<NarrativeLensCandidate>();
            List<NarrativeLensCandidate> chapters = new List<NarrativeLensCandidate>();
            List<NarrativeLensCandidate> pressure = new List<NarrativeLensCandidate>();
            if (!Usable(snapshot) || evidence == null) return identity;

            List<AnomalyNarrativeFact> facts = snapshot.facts ?? new List<AnomalyNarrativeFact>();
            for (int i = 0; i < facts.Count; i++)
            {
                AnomalyNarrativeFact fact = facts[i];
                NarrativeEvidence matched;
                NarrativeLensCandidate candidate = CandidateFor(
                    fact, evidence, snapshot.povPawnId, out matched);
                if (candidate == null) continue;

                if (candidate.category == NarrativeCategoryTokens.Identity)
                    InsertBounded(identity, candidate, MaximumIdentityCandidates);
                else if (candidate.category == NarrativeCategoryTokens.Chapter)
                    InsertBounded(chapters, candidate, MaximumChapterCandidates);
                else if (candidate.category == NarrativeCategoryTokens.Pressure)
                    InsertBounded(pressure, candidate, MaximumPressureCandidates);
            }

            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            result.AddRange(identity);
            result.AddRange(chapters);
            result.AddRange(pressure);
            result.Sort(CompareCandidates);
            return result;
        }

        private static bool Usable(AnomalyNarrativeSnapshot snapshot)
        {
            return snapshot != null && snapshot.providerAvailable && snapshot.pawnCanKnow
                && snapshot.hasVerifiedPovConnection && SafeSegment(snapshot.povPawnId);
        }

        private static NarrativeLensCandidate CandidateFor(
            AnomalyNarrativeFact fact,
            List<NarrativeEvidence> evidence,
            string povPawnId,
            out NarrativeEvidence matched)
        {
            matched = null;
            if (!ValidBaseFact(fact)) return null;

            string candidateKey;
            string category;
            string relationship;
            string salience;
            List<string> topics;
            if (IsGhoulFact(fact))
            {
                candidateKey = "anomaly|identity|ghoul|" + fact.subjectId.Trim() + "|transformed";
                category = NarrativeCategoryTokens.Identity;
                relationship = NarrativeRelationshipTokens.ExactSubject;
                salience = NarrativeSalienceTokens.Major;
                topics = new List<string> { "identity", "body", "transformation" };
            }
            else if (IsCreepJoinerFact(fact))
            {
                string phase = fact.phase.Trim();
                candidateKey = "anomaly|identity|creepjoiner|" + fact.subjectId.Trim() + "|" + phase;
                category = NarrativeCategoryTokens.Identity;
                relationship = NarrativeRelationshipTokens.ExactSubject;
                salience = phase == AnomalyNarrativeContinuityTokens.SurgicalReveal
                    ? NarrativeSalienceTokens.Meaningful
                    : NarrativeSalienceTokens.Major;
                topics = CreepJoinerTopics(phase);
            }
            else if (IsMonolithFact(fact, out string campaignEpoch))
            {
                candidateKey = "anomaly|chapter|monolith|" + campaignEpoch + "|" + fact.phase.Trim();
                category = NarrativeCategoryTokens.Chapter;
                relationship = NarrativeRelationshipTokens.ExactArc;
                salience = NarrativeSalienceTokens.Major;
                topics = new List<string> { "monolith", "void", "escalation" };
            }
            else if (IsContainmentFact(fact, out string mapId, out string startTick, out string entityId))
            {
                candidateKey = "anomaly|pressure|breach|" + mapId + "|" + startTick + "|" + entityId;
                category = NarrativeCategoryTokens.Pressure;
                relationship = NarrativeRelationshipTokens.ExactArc;
                salience = NarrativeSalienceTokens.Major;
                topics = new List<string> { "containment", "breach", "danger" };
            }
            else
            {
                return null;
            }

            matched = MatchingEvidence(fact, evidence, povPawnId);
            if (matched == null) return null;
            return new NarrativeLensCandidate
            {
                candidateKey = candidateKey,
                provider = NarrativeProviderTokens.Anomaly,
                category = category,
                text = fact.text.Trim(),
                facet = fact.facet.Trim(),
                subjectKind = fact.subjectKind.Trim(),
                subjectId = fact.subjectId.Trim(),
                arcKey = fact.arcKey.Trim(),
                topicTokens = topics,
                sourceEventId = matched.eventId ?? string.Empty,
                sourceTick = fact.sourceTick,
                salience = salience,
                relationship = relationship,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true
            };
        }

        private static bool ValidBaseFact(AnomalyNarrativeFact fact)
        {
            if (fact == null || fact.sourceTick < 0 || string.IsNullOrWhiteSpace(fact.text)) return false;
            string text = fact.text.Trim();
            if (text.Length > MaximumTextCharacters) return false;
            for (int i = 0; i < text.Length; i++)
                if (char.IsControl(text[i])) return false;
            return true;
        }

        private static bool IsGhoulFact(AnomalyNarrativeFact fact)
        {
            return fact.sourceKind == AnomalyNarrativeContinuityTokens.GhoulTransformation
                && fact.facet == NarrativeFacetTokens.IdentityTransition
                && fact.phase == AnomalyNarrativeContinuityTokens.Transformed
                && fact.subjectKind == NarrativeSubjectKindTokens.Pawn
                && SafeSegment(fact.subjectId)
                && string.IsNullOrWhiteSpace(fact.arcKey);
        }

        private static bool IsCreepJoinerFact(AnomalyNarrativeFact fact)
        {
            return fact.sourceKind == AnomalyNarrativeContinuityTokens.CreepJoinerOutcome
                && fact.facet == NarrativeFacetTokens.IdentityTransition
                && IsVisibleCreepJoinerPhase(fact.phase)
                && fact.subjectKind == NarrativeSubjectKindTokens.Pawn
                && SafeSegment(fact.subjectId)
                && string.IsNullOrWhiteSpace(fact.arcKey);
        }

        private static bool IsMonolithFact(AnomalyNarrativeFact fact, out string campaignEpoch)
        {
            campaignEpoch = string.Empty;
            if (fact.sourceKind != AnomalyNarrativeContinuityTokens.MonolithChapter
                || fact.facet != NarrativeFacetTokens.JourneyChapter
                || !IsVisibleMonolithPhase(fact.phase)
                || !string.IsNullOrWhiteSpace(fact.subjectKind)
                || !string.IsNullOrWhiteSpace(fact.subjectId)) return false;
            return TryMonolithArc(fact.arcKey, out campaignEpoch);
        }

        private static bool IsContainmentFact(
            AnomalyNarrativeFact fact,
            out string mapId,
            out string startTick,
            out string entityId)
        {
            mapId = string.Empty;
            startTick = string.Empty;
            entityId = string.Empty;
            if (fact.sourceKind != AnomalyNarrativeContinuityTokens.ContainmentBreach
                || fact.facet != NarrativeFacetTokens.AmbientPressure
                || fact.phase != AnomalyNarrativeContinuityTokens.Breached
                || fact.subjectKind != NarrativeSubjectKindTokens.Entity
                || !SafeSegment(fact.subjectId)) return false;

            string[] parts = (fact.arcKey ?? string.Empty).Trim().Split('|');
            if (parts.Length != 4 || parts[0] != "anomaly-breach"
                || !CanonicalNonNegativeInteger(parts[1])
                || !CanonicalNonNegativeInteger(parts[2])
                || !SafeSegment(parts[3])
                || !string.Equals(parts[3], fact.subjectId.Trim(), StringComparison.Ordinal)) return false;
            mapId = parts[1];
            startTick = parts[2];
            entityId = parts[3];
            return true;
        }

        private static NarrativeEvidence MatchingEvidence(
            AnomalyNarrativeFact fact,
            List<NarrativeEvidence> evidence,
            string povPawnId)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (row == null || row.pawnCanKnow != true
                    || !string.Equals(row.povPawnId, povPawnId, StringComparison.Ordinal)
                    || !string.Equals(row.facet, fact.facet, StringComparison.Ordinal)
                    || !string.Equals(row.phase, fact.phase, StringComparison.Ordinal)
                    || !SourceMatches(fact, row)) continue;

                if (fact.sourceKind == AnomalyNarrativeContinuityTokens.MonolithChapter)
                {
                    if (string.Equals(row.arcKey, fact.arcKey, StringComparison.Ordinal)) return row;
                    continue;
                }
                if (fact.sourceKind == AnomalyNarrativeContinuityTokens.ContainmentBreach)
                {
                    if (string.Equals(row.subjectKind, fact.subjectKind, StringComparison.Ordinal)
                        && string.Equals(row.subjectId, fact.subjectId, StringComparison.Ordinal)
                        && string.Equals(row.arcKey, fact.arcKey, StringComparison.Ordinal)) return row;
                    continue;
                }
                if (string.Equals(row.subjectKind, fact.subjectKind, StringComparison.Ordinal)
                    && string.Equals(row.subjectId, fact.subjectId, StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(row.arcKey)) return row;
            }
            return null;
        }

        private static bool SourceMatches(AnomalyNarrativeFact fact, NarrativeEvidence evidence)
        {
            if (fact.sourceKind == AnomalyNarrativeContinuityTokens.GhoulTransformation)
                return evidence.sourceDomain == AnomalyNarrativeContinuityTokens.GhoulSourceDomain
                    && evidence.sourceDefName == AnomalyNarrativeContinuityTokens.GhoulSourceDefName;
            if (fact.sourceKind == AnomalyNarrativeContinuityTokens.ContainmentBreach)
                return evidence.sourceDomain == AnomalyNarrativeContinuityTokens.ContainmentSourceDomain
                    && evidence.sourceDefName == AnomalyNarrativeContinuityTokens.ContainmentSourceDefName;
            if (fact.sourceKind == AnomalyNarrativeContinuityTokens.CreepJoinerOutcome)
                return evidence.sourceDomain == AnomalyNarrativeContinuityTokens.CreepJoinerSourceDomain
                    && evidence.sourceDefName == AnomalyNarrativeContinuityTokens.CreepJoinerSourceDefName;
            return fact.sourceKind == AnomalyNarrativeContinuityTokens.MonolithChapter
                && evidence.sourceDomain == AnomalyNarrativeContinuityTokens.MonolithSourceDomain
                && AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithPhaseSource(
                    fact.phase, evidence.sourceDefName);
        }

        private static bool TryMonolithArc(string arcKey, out string campaignEpoch)
        {
            campaignEpoch = string.Empty;
            string[] parts = (arcKey ?? string.Empty).Trim().Split('|');
            if (parts.Length != 2 || parts[0] != "anomaly-monolith"
                || !CanonicalNonNegativeInteger(parts[1])) return false;
            campaignEpoch = parts[1];
            return true;
        }

        private static bool CanonicalNonNegativeInteger(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 10
                || (value.Length > 1 && value[0] == '0')) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            return true;
        }

        private static bool SafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string cleaned = value.Trim();
            if (cleaned.Length > MaximumStableSegmentCharacters) return false;
            for (int i = 0; i < cleaned.Length; i++)
            {
                char current = cleaned[i];
                if (char.IsControl(current) || char.IsWhiteSpace(current)
                    || current == '|' || current == ';') return false;
            }
            return true;
        }

        private static bool IsVisibleMonolithPhase(string phase)
        {
            return phase == AnomalyNarrativeContinuityTokens.Stirring
                || phase == AnomalyNarrativeContinuityTokens.Waking
                || phase == AnomalyNarrativeContinuityTokens.VoidAwakened;
        }

        private static bool IsVisibleCreepJoinerPhase(string phase)
        {
            return phase == AnomalyNarrativeContinuityTokens.SurgicalReveal
                || phase == AnomalyNarrativeContinuityTokens.Rejected
                || phase == AnomalyNarrativeContinuityTokens.Aggressive
                || phase == AnomalyNarrativeContinuityTokens.Departed;
        }

        private static List<string> CreepJoinerTopics(string phase)
        {
            List<string> result = new List<string> { "identity", "creepjoiner" };
            if (phase == AnomalyNarrativeContinuityTokens.SurgicalReveal) result.Add("disclosure");
            else if (phase == AnomalyNarrativeContinuityTokens.Rejected) result.Add("rejection");
            else if (phase == AnomalyNarrativeContinuityTokens.Aggressive) result.Add("hostility");
            else if (phase == AnomalyNarrativeContinuityTokens.Departed) result.Add("departure");
            return result;
        }

        private static void InsertBounded(
            List<NarrativeLensCandidate> destination,
            NarrativeLensCandidate candidate,
            int maximum)
        {
            for (int i = 0; i < destination.Count; i++)
            {
                if (!string.Equals(
                    destination[i].candidateKey, candidate.candidateKey, StringComparison.Ordinal)) continue;
                if (CompareCandidates(candidate, destination[i]) < 0) destination[i] = candidate;
                destination.Sort(CompareCandidates);
                return;
            }
            destination.Add(candidate);
            destination.Sort(CompareCandidates);
            if (destination.Count > maximum) destination.RemoveAt(destination.Count - 1);
        }

        private static int CompareCandidates(NarrativeLensCandidate left, NarrativeLensCandidate right)
        {
            int key = string.CompareOrdinal(left?.candidateKey, right?.candidateKey);
            if (key != 0) return key;
            int text = string.CompareOrdinal(left?.text, right?.text);
            if (text != 0) return text;
            int source = string.CompareOrdinal(left?.sourceEventId, right?.sourceEventId);
            return source != 0 ? source : (left?.sourceTick ?? 0).CompareTo(right?.sourceTick ?? 0);
        }
    }

    /// <summary>Stable Odyssey journey phases saved into Narrative Continuity references.</summary>
    internal static class OdysseyNarrativePhaseTokens
    {
        public const string Departed = "departed";
        public const string Arrived = "arrived";
        public const string Returned = "returned";
    }

    /// <summary>
    /// Stable N3-O identities copied from the already-shipped O2 seasonal-flood observer. They are
    /// plain schema strings, not Def references, so the pure provider stays no-DLC safe.
    /// </summary>
    internal static class OdysseyEnvironmentalNarrativeTokens
    {
        public const string SeasonalFlood = "seasonal_flood";
        public const string SeasonalFloodConditionDefName = "SeasonalFloodActive";
        public const string SeasonalFloodEvidenceDefName = "SeasonalFlood";
    }

    /// <summary>
    /// One source-owned, event-time Odyssey pressure fact detached from the observed-condition save
    /// row and every live Pawn, map, Thing, condition Def, and DLC object.
    /// </summary>
    internal sealed class OdysseyEnvironmentalNarrativeFact
    {
        public string sourceKind = string.Empty;
        public string sourceDefName = string.Empty;
        public string evidenceDefName = string.Empty;
        public string povPawnId = string.Empty;
        public string shipStableId = string.Empty;
        public string locationKey = string.Empty;
        public string text = string.Empty;
        public int sourceTick;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
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
        public List<OdysseyEnvironmentalNarrativeFact> environmentalPressures =
            new List<OdysseyEnvironmentalNarrativeFact>();
        public int sourceTick;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>
    /// Pure candidate factory for one exact occupied Odyssey mobile home and bounded, source-owned
    /// environmental pressure on that same map.
    /// </summary>
    internal static class OdysseyNarrativeProvider
    {
        // These are defensive schema limits. The shared XML policy still owns global candidate/lens
        // caps, score weights, detail budgets, and the explicit home+pressure coexistence rule.
        private const int MaximumPressureCandidates = 1;
        private const int MaximumKeyPartCharacters = 160;
        private const int MaximumArcCharacters = 360;
        private const int MaximumTextCharacters = 480;

        /// <summary>
        /// Returns at most one home and one pressure lens. A matching journey arc promotes the home
        /// lens to exact-arc relevance; seasonal flooding remains an exact-place or verified ambient
        /// fact and never claims that the journey caused it.
        /// </summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            OdysseyNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            if (!Usable(evidence, snapshot))
            {
                return result;
            }

            NarrativeLensCandidate home = BuildHome(snapshot);
            if (home != null) result.Add(home);

            List<NarrativeLensCandidate> pressure = new List<NarrativeLensCandidate>();
            List<OdysseyEnvironmentalNarrativeFact> facts = snapshot.environmentalPressures
                ?? new List<OdysseyEnvironmentalNarrativeFact>();
            for (int i = 0; i < facts.Count; i++)
            {
                NarrativeLensCandidate candidate = BuildPressure(snapshot, facts[i]);
                if (candidate != null) pressure.Add(candidate);
            }

            // A corrupt/mod-expanded snapshot can repeat the same source row. Sort newest first within
            // one stable key, then de-duplicate, so input/save list order never changes the result.
            pressure.Sort(ComparePressure);
            string lastKey = string.Empty;
            int pressureAdded = 0;
            for (int i = 0; i < pressure.Count && pressureAdded < MaximumPressureCandidates; i++)
            {
                NarrativeLensCandidate candidate = pressure[i];
                if (string.Equals(candidate.candidateKey, lastKey, StringComparison.Ordinal)) continue;
                result.Add(candidate);
                pressureAdded++;
                lastKey = candidate.candidateKey;
            }
            return result;
        }

        private static bool Usable(List<NarrativeEvidence> evidence, OdysseyNarrativeSnapshot snapshot)
        {
            return snapshot != null && snapshot.providerAvailable && snapshot.pawnCanKnow
                && snapshot.hasVerifiedPovConnection && snapshot.sourceTick >= 0
                && SafeKeyPart(snapshot.povPawnId) && SafeKeyPart(snapshot.shipStableId)
                && SafeKeyPart(snapshot.locationKey) && CouldApply(evidence, snapshot.povPawnId)
                && ValidJourneyArcOrEmpty(snapshot.journeyId, snapshot.shipStableId);
        }

        private static NarrativeLensCandidate BuildHome(OdysseyNarrativeSnapshot snapshot)
        {
            if (!SafeText(snapshot.homeText)
                || string.IsNullOrWhiteSpace(snapshot.locationLabel)) return null;

            return new NarrativeLensCandidate
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
            };
        }

        private static NarrativeLensCandidate BuildPressure(
            OdysseyNarrativeSnapshot snapshot,
            OdysseyEnvironmentalNarrativeFact fact)
        {
            if (fact == null || !fact.pawnCanKnow || !fact.hasVerifiedPovConnection
                || fact.sourceTick < 0 || !SafeText(fact.text)
                || !string.Equals(fact.sourceKind,
                    OdysseyEnvironmentalNarrativeTokens.SeasonalFlood, StringComparison.Ordinal)
                || !string.Equals(fact.sourceDefName,
                    OdysseyEnvironmentalNarrativeTokens.SeasonalFloodConditionDefName,
                    StringComparison.Ordinal)
                || !string.Equals(fact.evidenceDefName,
                    OdysseyEnvironmentalNarrativeTokens.SeasonalFloodEvidenceDefName,
                    StringComparison.Ordinal)
                || !string.Equals(fact.povPawnId, snapshot.povPawnId, StringComparison.Ordinal)
                || !string.Equals(fact.shipStableId, snapshot.shipStableId, StringComparison.Ordinal)
                || !string.Equals(fact.locationKey, snapshot.locationKey, StringComparison.Ordinal))
            {
                return null;
            }

            return new NarrativeLensCandidate
            {
                candidateKey = "odyssey|pressure|" + fact.sourceKind + "|" + fact.locationKey.Trim(),
                provider = NarrativeProviderTokens.Odyssey,
                category = NarrativeCategoryTokens.Pressure,
                text = fact.text.Trim(),
                facet = NarrativeFacetTokens.AmbientPressure,
                subjectKind = NarrativeSubjectKindTokens.Place,
                subjectId = fact.locationKey.Trim(),
                // The flood belongs to the exact current place, not to the journey's causal arc.
                arcKey = string.Empty,
                topicTokens = new List<string> { "environment", "flood", "water" },
                sourceTick = fact.sourceTick,
                salience = NarrativeSalienceTokens.Meaningful,
                relationship = NarrativeRelationshipTokens.Ambient,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true
            };
        }

        private static bool SafeKeyPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string clean = value.Trim();
            return clean.Length <= MaximumKeyPartCharacters
                && clean.IndexOfAny(new[] { '|', ';', '=', '\r', '\n' }) < 0;
        }

        private static bool SafeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string clean = value.Trim();
            return clean.Length <= MaximumTextCharacters
                && clean.IndexOfAny(new[] { '\r', '\n' }) < 0;
        }

        private static bool ValidJourneyArcOrEmpty(string value, string shipStableId)
        {
            string arc = (value ?? string.Empty).Trim();
            if (arc.Length == 0) return true;
            if (arc.Length > MaximumArcCharacters || arc.IndexOfAny(new[] { ';', '=', '\r', '\n' }) >= 0)
                return false;

            const string prefix = "odyssey-journey|";
            if (!arc.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string suffix = arc.Substring(prefix.Length);
            int separator = suffix.LastIndexOf('|');
            int tick;
            return separator > 0 && separator < suffix.Length - 1
                && string.Equals(suffix.Substring(0, separator), shipStableId.Trim(), StringComparison.Ordinal)
                && int.TryParse(suffix.Substring(separator + 1), out tick) && tick >= 0;
        }

        private static int ComparePressure(
            NarrativeLensCandidate left,
            NarrativeLensCandidate right)
        {
            int byKey = string.CompareOrdinal(left?.candidateKey, right?.candidateKey);
            if (byKey != 0) return byKey;
            int byTick = (right?.sourceTick ?? -1).CompareTo(left?.sourceTick ?? -1);
            return byTick != 0 ? byTick : string.CompareOrdinal(left?.text, right?.text);
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

            string identityKey = string.IsNullOrWhiteSpace(snapshot.identityStableKey)
                ? (snapshot.xenotypeDefName ?? string.Empty).Trim()
                : snapshot.identityStableKey.Trim();
            if (!string.IsNullOrWhiteSpace(snapshot.childId)
                && identityKey.Length > 0
                && !string.IsNullOrWhiteSpace(snapshot.identityText))
            {
                result.Add(new NarrativeLensCandidate
                {
                    candidateKey = "biotech|identity|" + snapshot.childId + "|" + identityKey,
                    provider = NarrativeProviderTokens.Biotech,
                    category = NarrativeCategoryTokens.Identity,
                    text = snapshot.identityText.Trim(),
                    facet = NarrativeFacetTokens.IdentityTransition,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = snapshot.childId,
                    topicTokens = IdentityTopics(snapshot.identityTopicTokens),
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

        private static List<string> IdentityTopics(List<string> source)
        {
            List<string> result = new List<string> { "identity" };
            if (source == null) return result;
            for (int i = 0; i < source.Count && result.Count < 4; i++)
            {
                string token = (source[i] ?? string.Empty).Trim();
                if (token.Length == 0 || result.Contains(token)) continue;
                result.Add(token);
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
    /// Fixed provider list. Royalty, Biotech, Odyssey, and visible-only Anomaly facts have guarded
    /// implementations; remaining empty calls are intentional stubs. Any absent DLC/provider is the
    /// ordinary zero-candidate path.
    /// </summary>
    internal static class NarrativeProviderOrchestrator
    {
        public static List<NarrativeLensCandidate> Collect(
            List<NarrativeEvidence> evidence,
            List<NarrativeLensCandidate> coreCandidates,
            RoyaltyNarrativeSnapshot royalty,
            BiotechNarrativeSnapshot biotech,
            AnomalyNarrativeSnapshot anomaly,
            OdysseyNarrativeSnapshot odyssey)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            Add(result, coreCandidates); // Core/synthetic fixtures remain first and deterministic.
            Add(result, RoyaltyNarrativeProvider.Build(evidence, royalty));
            Add(result, IdeologyCandidates());
            Add(result, BiotechNarrativeProvider.Build(evidence, biotech));
            Add(result, AnomalyNarrativeProvider.Build(evidence, anomaly));
            Add(result, OdysseyNarrativeProvider.Build(evidence, odyssey));
            return result;
        }

        private static List<NarrativeLensCandidate> IdeologyCandidates() => new List<NarrativeLensCandidate>();
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
