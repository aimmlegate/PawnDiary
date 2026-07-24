// Plain, assembly-free contracts for Ideology belief interpretation and transient mutation facts.
// Live RimWorld objects are projected into these detached rows only by DlcContext. NarrativeEvidence
// remains the owner of facet, salience, POV knowledge, source, and topic vocabulary shared with the
// Narrative Continuity layer.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PawnDiary
{
    /// <summary>Stable resolver modes. Only quiet reflection may select general doctrine.</summary>
    internal static class BeliefResolutionModeTokens
    {
        public const string EventEnrichment = "event_enrichment";
        public const string QuietReflection = "quiet_reflection";

        public static bool IsKnown(string value)
        {
            return value == EventEnrichment || value == QuietReflection;
        }
    }

    /// <summary>Stable projected correlation kinds; these are schema values, not display prose.</summary>
    internal static class BeliefCorrelationKindTokens
    {
        public const string Thought = "thought";
        public const string HistoryEvent = "history_event";
        public const string Issue = "issue";

        public static bool IsKnown(string value)
        {
            return value == Thought || value == HistoryEvent || value == Issue;
        }
    }

    /// <summary>Mechanical valence from thought offsets. Text is never parsed to create these values.</summary>
    internal static class BeliefValenceTokens
    {
        public const string Positive = "positive";
        public const string Negative = "negative";
        public const string Mixed = "mixed";
        public const string Neutral = "neutral";
        public const string Unknown = "unknown";

        public static string Normalize(string value)
        {
            return value == Positive || value == Negative || value == Mixed || value == Neutral
                ? value
                : Unknown;
        }
    }

    /// <summary>Stable relevance-source diagnostics in strict structural-to-lexical order.</summary>
    internal static class BeliefRelevanceSourceTokens
    {
        public const string SourcePrecept = "source_precept";
        public const string ThoughtCorrelation = "thought_correlation";
        public const string HistoryCorrelation = "history_correlation";
        public const string IssueIdentity = "issue_identity";
        public const string MemeAssociation = "meme_association";
        public const string LexicalPhrase = "lexical_phrase";
        public const string LexicalTokens = "lexical_tokens";
        public const string LexicalFuzzy = "lexical_fuzzy";
        public const string Correction = "correction";
        public const string QuietFallback = "quiet_fallback";
    }

    /// <summary>Categorical relevance tiers. Bonuses may reorder within a tier but never cross one.</summary>
    internal static class BeliefRelevanceTierTokens
    {
        public const string SourcePrecept = "source_precept";
        public const string ExactCorrelation = "exact_correlation";
        public const string DirectIdentity = "direct_identity";
        public const string CorrelationText = "correlation_text";
        public const string IssueText = "issue_text";
        public const string GeneralText = "general_text";
        public const string Association = "association";
        public const string QuietFallback = "quiet_fallback";
    }

    /// <summary>
    /// Stable automatic-coverage outcomes. These tokens describe resolver mechanics only; they never
    /// carry precept labels, ideology names, descriptions, or prompt text.
    /// </summary>
    internal static class BeliefAutomaticCoverageOutcomeTokens
    {
        public const string ExactCorrelation = "exact_correlation";
        public const string StructuralCorrelation = "structural_correlation";
        public const string SemanticAlias = "semantic_alias";
        public const string GuardedLexical = "guarded_lexical";
        public const string BelowConfidence = "below_confidence";
        public const string Ambiguous = "ambiguous";
        public const string NoMatch = "no_match";

        public static bool IsKnown(string value)
        {
            return value == ExactCorrelation || value == StructuralCorrelation
                || value == SemanticAlias || value == GuardedLexical
                || value == BelowConfidence || value == Ambiguous || value == NoMatch;
        }
    }

    /// <summary>Stable rejection details for the combined no-candidate/no-evidence outcome.</summary>
    internal static class BeliefAutomaticCoverageReasonTokens
    {
        public const string None = "none";
        public const string InvalidInput = "invalid_input";
        public const string UnavailableSnapshot = "unavailable_snapshot";
        public const string UnverifiedEvidence = "unverified_evidence";
        public const string MismatchedPov = "mismatched_pov";
        public const string FutureEvidence = "future_evidence";
        public const string NoEvidence = "no_evidence";
        public const string BelowConfidence = "below_confidence";
        public const string RunnerUpAmbiguity = "runner_up_ambiguity";
        public const string NoCandidate = "no_candidate";

        public static bool IsKnown(string value)
        {
            return value == None || value == InvalidInput || value == UnavailableSnapshot
                || value == UnverifiedEvidence || value == MismatchedPov
                || value == FutureEvidence || value == NoEvidence || value == BelowConfidence
                || value == RunnerUpAmbiguity || value == NoCandidate;
        }
    }

    /// <summary>
    /// XML-relative confidence bands. Structural winners do not use lexical confidence; all other
    /// bands are computed from the configured minimum confidence and runner-up margin.
    /// </summary>
    internal static class BeliefAutomaticCoverageConfidenceBandTokens
    {
        public const string None = "none";
        public const string Structural = "structural";
        public const string BelowMinimum = "below_minimum";
        public const string Qualified = "qualified";
        public const string Strong = "strong";

        public static bool IsKnown(string value)
        {
            return value == None || value == Structural || value == BelowMinimum
                || value == Qualified || value == Strong;
        }
    }

    /// <summary>Optional explicit compatibility-correction actions. The shipped list is empty.</summary>
    internal static class BeliefCorrectionActionTokens
    {
        public const string Force = "force";
        public const string Exclude = "exclude";

        public static bool IsKnown(string value)
        {
            return value == Force || value == Exclude;
        }
    }

    /// <summary>Stable certainty movement tokens used by formatter and future saved state.</summary>
    internal static class BeliefCertaintyTrendTokens
    {
        public const string Rising = "rising";
        public const string Falling = "falling";
        public const string Stable = "stable";
        public const string Unknown = "unknown";
    }

    /// <summary>Stable certainty-change magnitude tokens.</summary>
    internal static class BeliefCertaintyMagnitudeTokens
    {
        public const string Minor = "minor";
        public const string Meaningful = "meaningful";
        public const string Major = "major";
        public const string Unknown = "unknown";
    }

    /// <summary>One detached issue associated with a live precept.</summary>
    internal sealed class BeliefIssueFact
    {
        public string defName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>One thought/history/issue relationship projected from a live precept component.</summary>
    internal sealed class BeliefCorrelationFact
    {
        public string kind = string.Empty;
        public string defName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public string sourceComponentKind = string.Empty;
        public string sourceFieldToken = string.Empty;
        public float minimumMoodOffset;
        public float maximumMoodOffset;
        public float minimumOpinionOffset;
        public float maximumOpinionOffset;
        public string valence = BeliefValenceTokens.Unknown;
    }

    /// <summary>One detached meme from the pawn's current live ideoligion.</summary>
    internal sealed class BeliefMemeFact
    {
        public string defName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public int impactRank;
        public bool isStructure;
    }

    /// <summary>One detached deity fact. Type/gender are stable tokens and never imply invented lore.</summary>
    internal sealed class BeliefDeityFact
    {
        public string name = string.Empty;
        public string typeToken = string.Empty;
        public string genderToken = string.Empty;
        public string relatedMemeDefName = string.Empty;
        public bool isKeyDeity;
    }

    /// <summary>One detached precept plus only the correlations and meme links found on live doctrine.</summary>
    internal sealed class BeliefPreceptFact
    {
        public string instanceId = string.Empty;
        public string defName = string.Empty;
        public BeliefIssueFact issue;
        public string displayLabel = string.Empty;
        public string description = string.Empty;
        public int impactRank;
        public bool visible = true;
        public bool proselytizes;
        public bool requiredByCurrentMeme;
        public List<string> associatedMemeDefNames = new List<string>();
        public List<string> requiredMemeDefNames = new List<string>();
        public List<BeliefCorrelationFact> correlations = new List<BeliefCorrelationFact>();
    }

    /// <summary>Current certainty plus optional event-time before/after facts.</summary>
    internal sealed class BeliefCertaintyFact
    {
        public bool hasCurrent;
        public float current;
        public bool hasBefore;
        public float before;
        public bool hasAfter;
        public float after;
    }

    /// <summary>Detached live doctrine snapshot. Empty/inactive snapshots are normal and fail closed.</summary>
    internal sealed class BeliefSnapshot
    {
        public bool ideologyActive;
        public string pawnId = string.Empty;
        public int capturedTick;
        public string ideologyId = string.Empty;
        public string ideologyName = string.Empty;
        public string roleName = string.Empty;
        public BeliefCertaintyFact certainty = new BeliefCertaintyFact();
        public BeliefMemeFact structure;
        public List<BeliefMemeFact> memes = new List<BeliefMemeFact>();
        public List<BeliefPreceptFact> precepts = new List<BeliefPreceptFact>();
        public List<BeliefDeityFact> deities = new List<BeliefDeityFact>();
    }

    /// <summary>
    /// Exact source-precept identity copied from a live thought. The adapter returns this detached row
    /// so ingestion never needs to retain or inspect a RimWorld <c>Precept</c> object.
    /// </summary>
    internal sealed class BeliefSourcePreceptFact
    {
        public string instanceId = string.Empty;
        public string defName = string.Empty;
    }

    /// <summary>
    /// Exact detached food identity captured while vanilla still owns the ingested Thing. The kind is
    /// a stable mechanical token; the Def identity and label are copied facts, never inferred from a
    /// generic meal name or quality.
    /// </summary>
    internal sealed class FoodIngestionEvidenceFact
    {
        public string ingredientKind = string.Empty;
        public string ingredientDefName = string.Empty;
        public string ingredientLabel = string.Empty;
    }

    /// <summary>Stable mechanical food categories understood by XML food-evidence rules.</summary>
    internal static class FoodIngestionEvidenceKindTokens
    {
        public const string HumanlikeMeat = "humanlike_meat";
        public const string InsectMeat = "insect_meat";
        public const string AnimalMeat = "animal_meat";
        public const string Fungus = "fungus";
        public const string NutrientPaste = "nutrient_paste";
    }

    /// <summary>
    /// One non-emitting history observation. Only stable identifiers leave the guarded runtime
    /// adapter; the bounded correlation buffer never owns a HistoryEvent, Pawn, or Def.
    /// </summary>
    internal sealed class BeliefHistoryObservation
    {
        public int tick;
        public string historyEventDefName = string.Empty;
        public List<string> visiblePawnIds = new List<string>();
    }

    /// <summary>
    /// One detached point-in-time Pawn_IdeoTracker state. Runtime adapters create it at mutation
    /// boundaries; pure coalescing code never retains a Pawn, Ideo, tracker, or Def.
    /// </summary>
    internal sealed class BeliefMutationState
    {
        public string pawnId = string.Empty;
        public int capturedTick;
        public string ideologyId = string.Empty;
        public string ideologyName = string.Empty;
        public bool hasCertainty;
        public float certainty;
    }

    /// <summary>Stable mechanical cause tokens. They identify hooked methods, never doctrine policy.</summary>
    internal static class BeliefMutationCauseTokens
    {
        public const string ConversionAttempt = "conversion_attempt";
        public const string CertaintyOffset = "certainty_offset";
        public const string SetIdeology = "set_ideology";

        /// <summary>Returns true only for a stable method-boundary token owned by Phase 2.</summary>
        public static bool IsKnown(string value)
        {
            return value == ConversionAttempt || value == CertaintyOffset || value == SetIdeology;
        }
    }

    /// <summary>Detached before/after facts for a conversion, reassurance, or certainty mutation.</summary>
    internal sealed class BeliefMutationSnapshot
    {
        public string pawnId = string.Empty;
        public int capturedTick;
        public string beforeIdeologyId = string.Empty;
        public string beforeIdeologyName = string.Empty;
        public string afterIdeologyId = string.Empty;
        public string afterIdeologyName = string.Empty;
        public string attemptedIdeologyId = string.Empty;
        public string attemptedIdeologyName = string.Empty;
        public bool hasBeforeCertainty;
        public float beforeCertainty;
        public bool hasAfterCertainty;
        public float afterCertainty;
        public bool ideologyChanged;
        public bool certaintyChanged;
        public bool? conversionSucceeded;
        public List<string> causeTokens = new List<string>();

        // Transient ordering metadata makes nested vanilla calls deterministic: the outer call owns
        // the earliest before state even though its postfix completes last. It is never persisted or
        // formatted into a prompt.
        internal long startedSequence;
        internal long completedSequence;
        internal bool observedMutation;

        /// <summary>True only when the adapter supplied an observed mutation fact.</summary>
        public bool HasUsefulFact
        {
            get
            {
                return ideologyChanged || certaintyChanged || conversionSucceeded.HasValue
                    || causeTokens != null && causeTokens.Count > 0;
            }
        }
    }

    /// <summary>One bounded, guarded event-side text field used only for relevance matching.</summary>
    internal sealed class BeliefEvidenceTextFact
    {
        public string field = string.Empty;
        public string value = string.Empty;
    }

    /// <summary>
    /// Optional event-owned output limits. Null preserves the established global belief policy; exact
    /// speech adapters use a detached copy so a witness can never inherit speaker-only presentation.
    /// </summary>
    internal sealed class BeliefContextProjection
    {
        public int maximumSelectedStances = 2;
        public int maximumSupportingMemes = 2;
        public int maximumContextCharacters;
        public bool includeRole = true;
        public bool includeCertainty = true;
        public bool includeStructure = true;
        public bool includeDeity = true;
        public bool includeNarrativeInterpretation = true;
        public string promptInstruction = string.Empty;

        /// <summary>Returns a deep detached copy; null remains null.</summary>
        public static BeliefContextProjection Copy(BeliefContextProjection source)
        {
            if (source == null) return null;
            return new BeliefContextProjection
            {
                maximumSelectedStances = source.maximumSelectedStances,
                maximumSupportingMemes = source.maximumSupportingMemes,
                maximumContextCharacters = source.maximumContextCharacters,
                includeRole = source.includeRole,
                includeCertainty = source.includeCertainty,
                includeStructure = source.includeStructure,
                includeDeity = source.includeDeity,
                includeNarrativeInterpretation = source.includeNarrativeInterpretation,
                promptInstruction = source.promptInstruction ?? string.Empty
            };
        }
    }

    /// <summary>
    /// Belief-specific evidence around the canonical NarrativeEvidence row. The shared row remains the
    /// one owner of POV knowledge, facets, salience, source identity, role, and belief-topic tokens.
    /// </summary>
    internal sealed class BeliefEventEvidence
    {
        public NarrativeEvidence narrative = new NarrativeEvidence();
        public string groupKey = string.Empty;
        // Exact belief-crisis pages may request the pawn's visible current ideoligion and certainty
        // even when the transient mutation row is unavailable. The guarded runtime builder still emits
        // nothing when no live snapshot exists. This flag never authorizes a page and never permits
        // reconstruction of an earlier ideoligion.
        public bool currentBeliefFactsRelevant;
        public string sourcePreceptInstanceId = string.Empty;
        public string sourcePreceptDefName = string.Empty;
        public List<string> thoughtDefNames = new List<string>();
        public List<string> historyEventDefNames = new List<string>();
        public List<string> issueDefNames = new List<string>();
        public List<string> memeDefNames = new List<string>();
        public List<string> semanticAliasTokens = new List<string>();
        public List<BeliefEvidenceTextFact> matchFields = new List<BeliefEvidenceTextFact>();
        public BeliefMutationSnapshot mutation;
        public BeliefContextProjection projection;
    }

    /// <summary>Input to the pure resolver. No field may contain a live game object.</summary>
    internal sealed class BeliefResolutionRequest
    {
        public BeliefSnapshot snapshot;
        public BeliefEventEvidence evidence;
        public BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
        public string mode = BeliefResolutionModeTokens.EventEnrichment;
        public int deterministicSeed = 1;
        public List<string> recentSelectionDefNames = new List<string>();
    }

    /// <summary>
    /// One bounded, dev-safe resolver diagnostic. Every string is a stable schema token, while numeric
    /// values are aggregate-safe scores/counts. Candidate identities and authored text stay outside it.
    /// </summary>
    internal sealed class BeliefAutomaticCoverageDiagnostic
    {
        public string outcome = BeliefAutomaticCoverageOutcomeTokens.NoMatch;
        public string reason = BeliefAutomaticCoverageReasonTokens.NoCandidate;
        public string winnerSource = string.Empty;
        public string winnerTier = string.Empty;
        public string confidenceBand = BeliefAutomaticCoverageConfidenceBandTokens.None;
        public float confidence;
        public float runnerUpGap;
        public bool hasRunnerUpGap;
        public int candidateCount;
    }

    /// <summary>One selected live stance with pure diagnostics describing why it was selected.</summary>
    internal sealed class ResolvedBeliefStance
    {
        public BeliefPreceptFact precept;
        public BeliefMemeFact supportingMeme;
        public string matchedIdentity = string.Empty;
        public string correctionKey = string.Empty;
        public string relevanceSource = string.Empty;
        public string relevanceTier = string.Empty;
        public float score;
        public float confidenceScore;
        public float runnerUpGap;
        public string correlationValence = BeliefValenceTokens.Unknown;
        public string independentEvidenceKey = string.Empty;
    }

    /// <summary>Ordered, bounded result. It can represent useful mutation or direct-meme context without a stance.</summary>
    internal sealed class BeliefStanceResolution
    {
        public BeliefAutomaticCoverageDiagnostic automaticCoverage =
            new BeliefAutomaticCoverageDiagnostic();
        public List<ResolvedBeliefStance> stances = new List<ResolvedBeliefStance>();
        public string ideologyId = string.Empty;
        public string ideologyName = string.Empty;
        public string roleName = string.Empty;
        public bool hasCertainty;
        public float certainty;
        public BeliefMemeFact structure;
        public List<BeliefMemeFact> supportingMemes = new List<BeliefMemeFact>();
        public BeliefDeityFact deity;
        public string certaintyBand = string.Empty;
        public string certaintyPhrase = string.Empty;
        public string certaintyTrend = BeliefCertaintyTrendTokens.Unknown;
        public string certaintyMagnitude = BeliefCertaintyMagnitudeTokens.Unknown;
        public BeliefMutationSnapshot mutation;
        public string mutationSubjectLabel = string.Empty;
        public bool mutationSubjectIsPov;
        public bool currentBeliefFactsRelevant;
        public List<string> expandedTopicTokens = new List<string>();
        public List<string> selectionReasonTokens = new List<string>();
        // N3-I is the only current consumer, but this remains an explicit discriminator because later
        // quiet-reflection/mutation modes may return belief context that must not consume the singular
        // interpretation slot. Providers fail closed when a future mode supplies another category.
        public string narrativeCategory = NarrativeCategoryTokens.Interpretation;
        public int maximumContextCharacters;
        public bool includeNarrativeInterpretation = true;

        public bool HasUsefulContext
        {
            get
            {
                return currentBeliefFactsRelevant || stances.Count > 0 || supportingMemes.Count > 0
                    || mutation != null && mutation.HasUsefulFact;
            }
        }
    }

    /// <summary>One token-to-score row copied from XML into an immutable policy snapshot.</summary>
    internal sealed class BeliefTokenScore
    {
        public readonly string token;
        public readonly float score;

        public BeliefTokenScore(string token, float score)
        {
            this.token = token ?? string.Empty;
            this.score = score;
        }
    }

    /// <summary>Stable source-domain tokens used by XML canonical-event ownership rules.</summary>
    internal static class BeliefCanonicalEventSourceTokens
    {
        public const string Ability = "ability";
        public const string Thought = "thought";
    }

    /// <summary>
    /// One exact source Def whose generic page is redundant while a named downstream group is active.
    /// The downstream group stays in the plain contract so the game-edge adapter can honor the
    /// player's effective group setting before suppressing anything.
    /// </summary>
    internal sealed class BeliefCanonicalEventOwnershipRule
    {
        public readonly string sourceDomain;
        public readonly string sourceDefName;
        public readonly string downstreamGroupDefName;

        public BeliefCanonicalEventOwnershipRule(
            string sourceDomain,
            string sourceDefName,
            string downstreamGroupDefName)
        {
            this.sourceDomain = sourceDomain ?? string.Empty;
            this.sourceDefName = sourceDefName ?? string.Empty;
            this.downstreamGroupDefName = downstreamGroupDefName ?? string.Empty;
        }
    }

    /// <summary>Stable source domains understood by mutation-to-event correlation rules.</summary>
    internal static class BeliefMutationEventSourceTokens
    {
        public const string Interaction = "interaction";
        public const string MentalState = "mental_state";
    }

    /// <summary>Stable event participant roles used to find the pawn whose tracker mutated.</summary>
    internal static class BeliefMutationSubjectRoleTokens
    {
        public const string Initiator = "initiator";
        public const string Recipient = "recipient";

        public static bool IsKnown(string value)
        {
            return value == Initiator || value == Recipient;
        }
    }

    /// <summary>XML tokens describing the conversion result an exact event row must corroborate.</summary>
    internal static class BeliefMutationConversionResultTokens
    {
        public const string Known = "known";
        public const string Success = "success";
        public const string Failure = "failure";
        public const string None = "none";

        public static bool IsKnown(string value)
        {
            return value == Known || value == Success || value == Failure || value == None;
        }
    }

    /// <summary>XML tokens describing the certainty direction required by an exact event row.</summary>
    internal static class BeliefMutationCertaintyDirectionTokens
    {
        public const string Any = "any";
        public const string Increase = "increase";
        public const string Decrease = "decrease";

        public static bool IsKnown(string value)
        {
            return value == Any || value == Increase || value == Decrease;
        }
    }

    /// <summary>XML tokens describing whether an exact event requires an ideology transition.</summary>
    internal static class BeliefMutationIdeologyChangeTokens
    {
        public const string Any = "any";
        public const string Changed = "changed";
        public const string Unchanged = "unchanged";

        public static bool IsKnown(string value)
        {
            return value == Any || value == Changed || value == Unchanged;
        }
    }

    /// <summary>
    /// One exact already-authorized event route and the mechanical mutation shape it may enrich.
    /// It names event facts and participant roles only; it never identifies doctrine.
    /// </summary>
    internal sealed class BeliefMutationEventRule
    {
        public readonly string sourceDomain;
        public readonly string sourceDefName;
        public readonly string downstreamGroupDefName;
        public readonly string subjectRole;
        public readonly string evidenceGroupKey;
        public readonly string requiredCauseToken;
        public readonly string conversionResult;
        public readonly string certaintyDirection;
        public readonly string ideologyChange;
        public readonly bool requireAttemptedIdeology;

        public BeliefMutationEventRule(
            string sourceDomain,
            string sourceDefName,
            string downstreamGroupDefName,
            string subjectRole,
            string evidenceGroupKey,
            string requiredCauseToken,
            string conversionResult,
            string certaintyDirection,
            string ideologyChange,
            bool requireAttemptedIdeology)
        {
            this.sourceDomain = sourceDomain ?? string.Empty;
            this.sourceDefName = sourceDefName ?? string.Empty;
            this.downstreamGroupDefName = downstreamGroupDefName ?? string.Empty;
            this.subjectRole = subjectRole ?? string.Empty;
            this.evidenceGroupKey = evidenceGroupKey ?? string.Empty;
            this.requiredCauseToken = requiredCauseToken ?? string.Empty;
            this.conversionResult = conversionResult ?? string.Empty;
            this.certaintyDirection = certaintyDirection ?? string.Empty;
            this.ideologyChange = ideologyChange ?? string.Empty;
            this.requireAttemptedIdeology = requireAttemptedIdeology;
        }
    }

    /// <summary>
    /// One exact already-authorized Counsel interaction and the stable mood-result tokens it may add
    /// to game context. This is event policy only: it carries no doctrine, Pawn, Thought, or Def.
    /// </summary>
    internal sealed class CounselEventRule
    {
        public readonly string sourceDefName;
        public readonly string downstreamGroupDefName;
        public readonly string resultToken;
        public readonly string moodEffectToken;

        public CounselEventRule(
            string sourceDefName,
            string downstreamGroupDefName,
            string resultToken,
            string moodEffectToken)
        {
            this.sourceDefName = sourceDefName ?? string.Empty;
            this.downstreamGroupDefName = downstreamGroupDefName ?? string.Empty;
            this.resultToken = resultToken ?? string.Empty;
            this.moodEffectToken = moodEffectToken ?? string.Empty;
        }
    }

    /// <summary>One localized semantic concept and its equivalent guarded matching phrases.</summary>
    internal sealed class BeliefSemanticAlias
    {
        public readonly string topicToken;
        public readonly IReadOnlyList<string> aliases;

        public BeliefSemanticAlias(string topicToken, IList<string> aliases)
        {
            this.topicToken = topicToken ?? string.Empty;
            this.aliases = CopyStrings(aliases);
        }

        private static IReadOnlyList<string> CopyStrings(IList<string> values)
        {
            List<string> copy = new List<string>();
            if (values != null)
                for (int i = 0; i < values.Count; i++) copy.Add(values[i] ?? string.Empty);
            return new ReadOnlyCollection<string>(copy);
        }
    }

    /// <summary>Exact source-fact expansion rule; it adds evidence vocabulary, never candidate IDs.</summary>
    internal sealed class BeliefEventEvidenceRule
    {
        public readonly string key;
        public readonly string sourceDomain;
        public readonly string sourceDefName;
        public readonly string groupKey;
        public readonly string facet;
        public readonly string phase;
        public readonly string povRole;
        public readonly string mutationCauseToken;
        public readonly IReadOnlyList<string> addTopics;
        public readonly IReadOnlyList<string> addSemanticAliases;

        public BeliefEventEvidenceRule(
            string key,
            string sourceDomain,
            string sourceDefName,
            string groupKey,
            string facet,
            string phase,
            string povRole,
            string mutationCauseToken,
            IList<string> addTopics,
            IList<string> addSemanticAliases)
        {
            this.key = key ?? string.Empty;
            this.sourceDomain = sourceDomain ?? string.Empty;
            this.sourceDefName = sourceDefName ?? string.Empty;
            this.groupKey = groupKey ?? string.Empty;
            this.facet = facet ?? string.Empty;
            this.phase = phase ?? string.Empty;
            this.povRole = povRole ?? string.Empty;
            this.mutationCauseToken = mutationCauseToken ?? string.Empty;
            this.addTopics = CopyStrings(addTopics);
            this.addSemanticAliases = CopyStrings(addSemanticAliases);
        }

        /// <summary>
        /// True when the rule constrains at least one event fact. A key plus output vocabulary is not
        /// enough: selectorless rules would otherwise enrich every event in the game.
        /// </summary>
        public bool HasSelector
        {
            get
            {
                return !string.IsNullOrWhiteSpace(sourceDomain)
                    || !string.IsNullOrWhiteSpace(sourceDefName)
                    || !string.IsNullOrWhiteSpace(groupKey)
                    || !string.IsNullOrWhiteSpace(facet)
                    || !string.IsNullOrWhiteSpace(phase)
                    || !string.IsNullOrWhiteSpace(povRole)
                    || !string.IsNullOrWhiteSpace(mutationCauseToken);
            }
        }

        private static IReadOnlyList<string> CopyStrings(IList<string> values)
        {
            List<string> copy = new List<string>();
            if (values != null)
                for (int i = 0; i < values.Count; i++) copy.Add(values[i] ?? string.Empty);
            return new ReadOnlyCollection<string>(copy);
        }
    }

    /// <summary>
    /// XML-owned mapping from one exact captured ingredient kind to resolver group/field vocabulary.
    /// It names no precept, issue, meme, thought, meal, or ingredient Def.
    /// </summary>
    internal sealed class BeliefFoodEvidenceRule
    {
        public readonly string key;
        public readonly string ingredientKind;
        public readonly string groupKey;
        public readonly string matchField;

        public BeliefFoodEvidenceRule(
            string key,
            string ingredientKind,
            string groupKey,
            string matchField)
        {
            this.key = key ?? string.Empty;
            this.ingredientKind = ingredientKind ?? string.Empty;
            this.groupKey = groupKey ?? string.Empty;
            this.matchField = matchField ?? string.Empty;
        }
    }

    /// <summary>Explicit compatibility correction for a proven metadata-poor Def. Defaults contain none.</summary>
    internal sealed class BeliefCorrelationCorrection
    {
        public readonly string key;
        public readonly string action;
        public readonly string preceptDefName;
        public readonly string issueDefName;
        public readonly string memeDefName;
        public readonly string sourceDomain;
        public readonly string sourceDefName;
        public readonly string groupKey;
        public readonly string topicToken;

        public BeliefCorrelationCorrection(
            string key,
            string action,
            string preceptDefName,
            string issueDefName,
            string memeDefName,
            string sourceDomain,
            string sourceDefName,
            string groupKey,
            string topicToken)
        {
            this.key = key ?? string.Empty;
            this.action = action ?? string.Empty;
            this.preceptDefName = preceptDefName ?? string.Empty;
            this.issueDefName = issueDefName ?? string.Empty;
            this.memeDefName = memeDefName ?? string.Empty;
            this.sourceDomain = sourceDomain ?? string.Empty;
            this.sourceDefName = sourceDefName ?? string.Empty;
            this.groupKey = groupKey ?? string.Empty;
            this.topicToken = topicToken ?? string.Empty;
        }
    }

    /// <summary>One certainty band and its XML/DefInjected model-facing phrase.</summary>
    internal sealed class BeliefCertaintyBand
    {
        public readonly string token;
        public readonly float minimum;
        public readonly string phrase;

        public BeliefCertaintyBand(string token, float minimum, string phrase)
        {
            this.token = token ?? string.Empty;
            this.minimum = minimum;
            this.phrase = phrase ?? string.Empty;
        }
    }

    /// <summary>One Full/Balanced/Compact formatter budget.</summary>
    internal sealed class BeliefDetailBudget
    {
        public readonly string detailLevel;
        public readonly int maximumLines;
        public readonly int maximumCharacters;
        public readonly bool includeDescriptions;
        public readonly bool includeStructure;
        public readonly bool includeMemes;
        public readonly bool includeDeity;

        public BeliefDetailBudget(
            string detailLevel,
            int maximumLines,
            int maximumCharacters,
            bool includeDescriptions,
            bool includeStructure,
            bool includeMemes,
            bool includeDeity)
        {
            this.detailLevel = NarrativeDetailLevelTokens.Normalize(detailLevel);
            this.maximumLines = maximumLines;
            this.maximumCharacters = maximumCharacters;
            this.includeDescriptions = includeDescriptions;
            this.includeStructure = includeStructure;
            this.includeMemes = includeMemes;
            this.includeDeity = includeDeity;
        }
    }

    /// <summary>
    /// Mutable construction DTO used only at the XML/test boundary. Build() freezes deep copies into
    /// BeliefPolicySnapshot, so pure resolver calls cannot observe later Def/list mutation.
    /// </summary>
    internal sealed class BeliefPolicyBuilder
    {
        public bool enabled = true;
        public int maximumPreceptCandidates = 128;
        public int maximumMemeCandidates = 32;
        public int maximumDeityCandidates = 16;
        public int maximumSelectedStances = 2;
        public int defaultSelectedStances = 1;
        public int maximumSupportingMemes = 2;
        public int maximumRecentSelections = 16;
        public int maximumReflectedBeliefSourceIds = 16;
        public int beliefScanIntervalTicks = 250;
        public int maximumBeliefPawnsPerScan = 4;
        public int pendingBeliefEvidenceMaxAgeTicks = 3600000;
        public int maximumHistoryCorrelationEntries = 256;
        public int historyCorrelationWindowTicks = 120;
        public int maximumMutationCorrelationEntries = 256;
        public int mutationCorrelationWindowTicks = 120;
        public int maximumAutomaticDiagnosticSamples = 4096;
        public int maximumFieldCharacters = 320;
        public int maximumNormalizedTokensPerField = 48;
        public int maximumLexicalFieldsPerDocument = 96;
        public int maximumLexicalTokensPerDocument = 256;
        public int maximumDescriptionCharacters = 240;
        public int maximumIdentifierCharacters = 160;
        public int maximumTotalCharacters = 1800;
        public int maximumTotalLines = 16;
        public float minimumLexicalConfidence = 65f;
        public float lexicalRunnerUpMargin = 18f;
        public float fuzzyRunnerUpMargin = 26f;
        public int minimumDistinctiveTokenMatches = 2;
        public int uniqueTokenMinimumCharacters = 8;
        public int fuzzyTokenMinimumCharacters = 7;
        public int fuzzyMinimumDistinctiveMatches = 2;
        public float fuzzySimilarityMinimum = 0.84f;
        public int commonTokenMinimumDocuments = 2;
        public float commonTokenDocumentFraction = 0.55f;
        public float phraseMatchScore = 42f;
        public float tokenMatchScore = 18f;
        public float uniqueTokenBonus = 18f;
        public float fuzzyMatchScore = 16f;
        public float selectionWeightBase = 100f;
        public float secondSlotMinimumScore = 900f;
        public float recentSelectionPenalty = 450f;
        public float requiredByMemeBonus = 120f;
        public float proselytizingRoleBonus = 100f;
        public float meaningfulSalienceBonus = 45f;
        public float majorSalienceBonus = 90f;
        public float terminalSalienceBonus = 120f;
        public float highImpactBonus = 220f;
        public float mediumImpactBonus = 120f;
        public float lowImpactBonus = 40f;
        public float certaintyMeaningfulDelta = 0.05f;
        public float certaintyMajorDelta = 0.15f;
        public bool includeStructure = true;
        public bool includeRelatedDeity = true;
        public bool includeKeyDeity = true;
        public bool allowDeterministicAlternativeDeity;
        public float quietReflectionChance = 0.08f;
        public int recentBeliefEventWindowTicks = 180000;
        public int beliefReflectionCooldownTicks = 900000;
        public int maximumBeliefReflectionsPerQuadrum = 2;
        public int beliefReflectionMaxTokens = 360;
        public List<BeliefTokenScore> tierScores = new List<BeliefTokenScore>();
        public List<BeliefTokenScore> eventFieldWeights = new List<BeliefTokenScore>();
        public List<BeliefTokenScore> beliefFieldWeights = new List<BeliefTokenScore>();
        public List<BeliefCertaintyBand> certaintyBands = new List<BeliefCertaintyBand>();
        public List<BeliefSemanticAlias> semanticAliases = new List<BeliefSemanticAlias>();
        public List<BeliefEventEvidenceRule> eventEvidenceRules = new List<BeliefEventEvidenceRule>();
        public List<BeliefFoodEvidenceRule> foodEvidenceRules = new List<BeliefFoodEvidenceRule>();
        public List<string> lexicalExclusions = new List<string>();
        public List<string> proselytizingPovRoles = new List<string>();
        public List<BeliefCanonicalEventOwnershipRule> canonicalEventOwnershipRules =
            new List<BeliefCanonicalEventOwnershipRule>();
        public List<BeliefMutationEventRule> mutationEventRules =
            new List<BeliefMutationEventRule>();
        public List<CounselEventRule> counselEventRules = new List<CounselEventRule>();
        public List<BeliefCorrelationCorrection> correlationOverrides = new List<BeliefCorrelationCorrection>();
        public List<BeliefDetailBudget> detailBudgets = new List<BeliefDetailBudget>();

        /// <summary>Creates the safe code fallback used when XML is missing or malformed.</summary>
        public static BeliefPolicyBuilder CreateDefault()
        {
            BeliefPolicyBuilder value = new BeliefPolicyBuilder();
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.SourcePrecept, 1200f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.ExactCorrelation, 1000f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.DirectIdentity, 900f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.CorrelationText, 750f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.IssueText, 650f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.GeneralText, 450f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.Association, 300f));
            value.tierScores.Add(new BeliefTokenScore(BeliefRelevanceTierTokens.QuietFallback, 200f));

            AddScore(value.eventFieldWeights, "correlation", 3f);
            AddScore(value.eventFieldWeights, "event_label", 2.5f);
            AddScore(value.eventFieldWeights, "subject_label", 2.3f);
            AddScore(value.eventFieldWeights, "object_label", 2.3f);
            AddScore(value.eventFieldWeights, "ingredient_label", 2.8f);
            AddScore(value.eventFieldWeights, "body_part_label", 2.8f);
            AddScore(value.eventFieldWeights, "hediff_label", 2.8f);
            AddScore(value.eventFieldWeights, "weapon_label", 2.5f);
            AddScore(value.eventFieldWeights, "ritual_label", 2.5f);
            AddScore(value.eventFieldWeights, "condition_label", 2.3f);
            AddScore(value.eventFieldWeights, "semantic_alias", 2.6f);
            AddScore(value.eventFieldWeights, "def_name", 1.4f);
            AddScore(value.eventFieldWeights, "group", 0.5f);
            AddScore(value.eventFieldWeights, "domain", 0.25f);

            AddScore(value.beliefFieldWeights, "correlation", 3f);
            AddScore(value.beliefFieldWeights, "issue", 2.7f);
            AddScore(value.beliefFieldWeights, "precept", 1.8f);
            AddScore(value.beliefFieldWeights, "meme", 1.2f);

            value.certaintyBands.Add(new BeliefCertaintyBand("fervent", 0.85f, string.Empty));
            value.certaintyBands.Add(new BeliefCertaintyBand("confident", 0.60f, string.Empty));
            value.certaintyBands.Add(new BeliefCertaintyBand("uneasy", 0.35f, string.Empty));
            value.certaintyBands.Add(new BeliefCertaintyBand("conflicted", 0.15f, string.Empty));
            value.certaintyBands.Add(new BeliefCertaintyBand("doubtful", 0f, string.Empty));

            // Schema words and final prompt labels are deliberately excluded. Language-specific common
            // words are suppressed dynamically instead of being hardcoded here.
            value.lexicalExclusions.Add("event");
            value.lexicalExclusions.Add("pawn");
            value.lexicalExclusions.Add("ideology");
            value.lexicalExclusions.Add("ideoligion");
            value.lexicalExclusions.Add("precept");
            value.lexicalExclusions.Add("belief");
            value.lexicalExclusions.Add("context");
            value.proselytizingPovRoles.Add("converter");
            value.proselytizingPovRoles.Add("organizer");
            value.proselytizingPovRoles.Add("moral_guide");

            value.detailBudgets.Add(new BeliefDetailBudget(
                NarrativeDetailLevelTokens.Full, 16, 1800, true, true, true, true));
            value.detailBudgets.Add(new BeliefDetailBudget(
                NarrativeDetailLevelTokens.Balanced, 9, 1000, false, true, true, true));
            value.detailBudgets.Add(new BeliefDetailBudget(
                NarrativeDetailLevelTokens.Compact, 5, 520, false, false, false, true));

            // Intentionally no semantic aliases, evidence rules, or corrections in the fallback. XML
            // owns editable event vocabulary, and missing XML therefore fails conservatively.
            return value;
        }

        /// <summary>Builds an immutable, deeply copied policy snapshot.</summary>
        public BeliefPolicySnapshot Build()
        {
            return new BeliefPolicySnapshot(this);
        }

        private static void AddScore(List<BeliefTokenScore> target, string token, float score)
        {
            target.Add(new BeliefTokenScore(token, score));
        }
    }

    /// <summary>Immutable pure snapshot copied from XML policy at the future main-thread adapter boundary.</summary>
    internal sealed class BeliefPolicySnapshot
    {
        public readonly bool enabled;
        public readonly int maximumPreceptCandidates;
        public readonly int maximumMemeCandidates;
        public readonly int maximumDeityCandidates;
        public readonly int maximumSelectedStances;
        public readonly int defaultSelectedStances;
        public readonly int maximumSupportingMemes;
        public readonly int maximumRecentSelections;
        public readonly int maximumReflectedBeliefSourceIds;
        public readonly int beliefScanIntervalTicks;
        public readonly int maximumBeliefPawnsPerScan;
        public readonly int pendingBeliefEvidenceMaxAgeTicks;
        public readonly int maximumHistoryCorrelationEntries;
        public readonly int historyCorrelationWindowTicks;
        public readonly int maximumMutationCorrelationEntries;
        public readonly int mutationCorrelationWindowTicks;
        public readonly int maximumAutomaticDiagnosticSamples;
        public readonly int maximumFieldCharacters;
        public readonly int maximumNormalizedTokensPerField;
        public readonly int maximumLexicalFieldsPerDocument;
        public readonly int maximumLexicalTokensPerDocument;
        public readonly int maximumDescriptionCharacters;
        public readonly int maximumIdentifierCharacters;
        public readonly int maximumTotalCharacters;
        public readonly int maximumTotalLines;
        public readonly float minimumLexicalConfidence;
        public readonly float lexicalRunnerUpMargin;
        public readonly float fuzzyRunnerUpMargin;
        public readonly int minimumDistinctiveTokenMatches;
        public readonly int uniqueTokenMinimumCharacters;
        public readonly int fuzzyTokenMinimumCharacters;
        public readonly int fuzzyMinimumDistinctiveMatches;
        public readonly float fuzzySimilarityMinimum;
        public readonly int commonTokenMinimumDocuments;
        public readonly float commonTokenDocumentFraction;
        public readonly float phraseMatchScore;
        public readonly float tokenMatchScore;
        public readonly float uniqueTokenBonus;
        public readonly float fuzzyMatchScore;
        public readonly float selectionWeightBase;
        public readonly float secondSlotMinimumScore;
        public readonly float recentSelectionPenalty;
        public readonly float requiredByMemeBonus;
        public readonly float proselytizingRoleBonus;
        public readonly float meaningfulSalienceBonus;
        public readonly float majorSalienceBonus;
        public readonly float terminalSalienceBonus;
        public readonly float highImpactBonus;
        public readonly float mediumImpactBonus;
        public readonly float lowImpactBonus;
        public readonly float certaintyMeaningfulDelta;
        public readonly float certaintyMajorDelta;
        public readonly bool includeStructure;
        public readonly bool includeRelatedDeity;
        public readonly bool includeKeyDeity;
        public readonly bool allowDeterministicAlternativeDeity;
        public readonly float quietReflectionChance;
        public readonly int recentBeliefEventWindowTicks;
        public readonly int beliefReflectionCooldownTicks;
        public readonly int maximumBeliefReflectionsPerQuadrum;
        public readonly int beliefReflectionMaxTokens;
        public readonly IReadOnlyList<BeliefTokenScore> tierScores;
        public readonly IReadOnlyList<BeliefTokenScore> eventFieldWeights;
        public readonly IReadOnlyList<BeliefTokenScore> beliefFieldWeights;
        public readonly IReadOnlyList<BeliefCertaintyBand> certaintyBands;
        public readonly IReadOnlyList<BeliefSemanticAlias> semanticAliases;
        public readonly IReadOnlyList<BeliefEventEvidenceRule> eventEvidenceRules;
        public readonly IReadOnlyList<BeliefFoodEvidenceRule> foodEvidenceRules;
        public readonly IReadOnlyList<string> lexicalExclusions;
        public readonly IReadOnlyList<string> proselytizingPovRoles;
        public readonly IReadOnlyList<BeliefCanonicalEventOwnershipRule> canonicalEventOwnershipRules;
        public readonly IReadOnlyList<BeliefMutationEventRule> mutationEventRules;
        public readonly IReadOnlyList<CounselEventRule> counselEventRules;
        public readonly IReadOnlyList<BeliefCorrelationCorrection> correlationOverrides;
        public readonly IReadOnlyList<BeliefDetailBudget> detailBudgets;

        internal BeliefPolicySnapshot(BeliefPolicyBuilder source)
        {
            BeliefPolicyBuilder value = source ?? BeliefPolicyBuilder.CreateDefault();
            enabled = value.enabled;
            maximumPreceptCandidates = Clamp(value.maximumPreceptCandidates, 1, 512, 128);
            maximumMemeCandidates = Clamp(value.maximumMemeCandidates, 1, 128, 32);
            maximumDeityCandidates = Clamp(value.maximumDeityCandidates, 1, 64, 16);
            maximumSelectedStances = Clamp(value.maximumSelectedStances, 1, 2, 2);
            defaultSelectedStances = Clamp(value.defaultSelectedStances, 1, maximumSelectedStances, 1);
            maximumSupportingMemes = Clamp(value.maximumSupportingMemes, 0, 4, 2);
            maximumRecentSelections = Clamp(value.maximumRecentSelections, 0, 64, 16);
            maximumReflectedBeliefSourceIds = Clamp(value.maximumReflectedBeliefSourceIds, 0, 64, 16);
            beliefScanIntervalTicks = Clamp(value.beliefScanIntervalTicks, 60, 60000, 250);
            maximumBeliefPawnsPerScan = Clamp(value.maximumBeliefPawnsPerScan, 1, 64, 4);
            pendingBeliefEvidenceMaxAgeTicks = Clamp(
                value.pendingBeliefEvidenceMaxAgeTicks, 60000, 60000000, 3600000);
            maximumHistoryCorrelationEntries = Clamp(value.maximumHistoryCorrelationEntries, 1, 2048, 256);
            historyCorrelationWindowTicks = Clamp(value.historyCorrelationWindowTicks, 0, 600, 120);
            maximumMutationCorrelationEntries = Clamp(value.maximumMutationCorrelationEntries, 1, 2048, 256);
            mutationCorrelationWindowTicks = Clamp(value.mutationCorrelationWindowTicks, 0, 600, 120);
            maximumAutomaticDiagnosticSamples = Clamp(
                value.maximumAutomaticDiagnosticSamples, 1, 100000, 4096);
            maximumFieldCharacters = Clamp(value.maximumFieldCharacters, 32, 2048, 320);
            maximumNormalizedTokensPerField = Clamp(value.maximumNormalizedTokensPerField, 4, 128, 48);
            maximumLexicalFieldsPerDocument = Clamp(value.maximumLexicalFieldsPerDocument, 8, 256, 96);
            maximumLexicalTokensPerDocument = Clamp(value.maximumLexicalTokensPerDocument, 32, 1024, 256);
            maximumDescriptionCharacters = Clamp(value.maximumDescriptionCharacters, 32, 1024, 240);
            maximumIdentifierCharacters = Clamp(value.maximumIdentifierCharacters, 16, 512, 160);
            maximumTotalCharacters = Clamp(value.maximumTotalCharacters, 128, 4096, 1800);
            maximumTotalLines = Clamp(value.maximumTotalLines, 3, 32, 16);
            minimumLexicalConfidence = NonNegative(value.minimumLexicalConfidence, 65f);
            lexicalRunnerUpMargin = NonNegative(value.lexicalRunnerUpMargin, 18f);
            fuzzyRunnerUpMargin = NonNegative(value.fuzzyRunnerUpMargin, 26f);
            minimumDistinctiveTokenMatches = Clamp(value.minimumDistinctiveTokenMatches, 1, 8, 2);
            uniqueTokenMinimumCharacters = Clamp(value.uniqueTokenMinimumCharacters, 4, 32, 8);
            fuzzyTokenMinimumCharacters = Clamp(value.fuzzyTokenMinimumCharacters, 4, 32, 7);
            fuzzyMinimumDistinctiveMatches = Clamp(value.fuzzyMinimumDistinctiveMatches, 1, 8, 2);
            fuzzySimilarityMinimum = Clamp01(value.fuzzySimilarityMinimum, 0.84f);
            commonTokenMinimumDocuments = Clamp(value.commonTokenMinimumDocuments, 2, 32, 2);
            commonTokenDocumentFraction = Clamp01(value.commonTokenDocumentFraction, 0.55f);
            phraseMatchScore = NonNegative(value.phraseMatchScore, 42f);
            tokenMatchScore = NonNegative(value.tokenMatchScore, 18f);
            uniqueTokenBonus = NonNegative(value.uniqueTokenBonus, 18f);
            fuzzyMatchScore = NonNegative(value.fuzzyMatchScore, 16f);
            selectionWeightBase = NonNegative(value.selectionWeightBase, 100f);
            secondSlotMinimumScore = NonNegative(value.secondSlotMinimumScore, 900f);
            recentSelectionPenalty = NonNegative(value.recentSelectionPenalty, 450f);
            requiredByMemeBonus = NonNegative(value.requiredByMemeBonus, 120f);
            proselytizingRoleBonus = NonNegative(value.proselytizingRoleBonus, 100f);
            meaningfulSalienceBonus = NonNegative(value.meaningfulSalienceBonus, 45f);
            majorSalienceBonus = NonNegative(value.majorSalienceBonus, 90f);
            terminalSalienceBonus = NonNegative(value.terminalSalienceBonus, 120f);
            highImpactBonus = NonNegative(value.highImpactBonus, 220f);
            mediumImpactBonus = NonNegative(value.mediumImpactBonus, 120f);
            lowImpactBonus = NonNegative(value.lowImpactBonus, 40f);
            certaintyMeaningfulDelta = Clamp01(value.certaintyMeaningfulDelta, 0.05f);
            certaintyMajorDelta = Math.Max(certaintyMeaningfulDelta, Clamp01(value.certaintyMajorDelta, 0.15f));
            includeStructure = value.includeStructure;
            includeRelatedDeity = value.includeRelatedDeity;
            includeKeyDeity = value.includeKeyDeity;
            allowDeterministicAlternativeDeity = value.allowDeterministicAlternativeDeity;
            quietReflectionChance = Clamp01(value.quietReflectionChance, 0.08f);
            recentBeliefEventWindowTicks = Clamp(
                value.recentBeliefEventWindowTicks, 0, 3600000, 180000);
            beliefReflectionCooldownTicks = Math.Max(0, value.beliefReflectionCooldownTicks);
            maximumBeliefReflectionsPerQuadrum = Clamp(value.maximumBeliefReflectionsPerQuadrum, 0, 16, 2);
            beliefReflectionMaxTokens = Clamp(value.beliefReflectionMaxTokens, 1, 4000, 360);
            tierScores = CopyScores(value.tierScores);
            eventFieldWeights = CopyScores(value.eventFieldWeights);
            beliefFieldWeights = CopyScores(value.beliefFieldWeights);
            certaintyBands = CopyBands(value.certaintyBands);
            semanticAliases = CopyAliases(value.semanticAliases);
            eventEvidenceRules = CopyRules(value.eventEvidenceRules);
            foodEvidenceRules = CopyFoodRules(value.foodEvidenceRules);
            lexicalExclusions = CopyStrings(value.lexicalExclusions);
            proselytizingPovRoles = CopyStrings(value.proselytizingPovRoles);
            canonicalEventOwnershipRules = CopyOwnershipRules(value.canonicalEventOwnershipRules);
            mutationEventRules = CopyMutationEventRules(value.mutationEventRules);
            counselEventRules = CopyCounselEventRules(value.counselEventRules);
            correlationOverrides = CopyCorrections(value.correlationOverrides);
            detailBudgets = CopyBudgets(value.detailBudgets);
        }

        /// <summary>Returns an immutable safe fallback policy.</summary>
        public static BeliefPolicySnapshot CreateDefault()
        {
            return BeliefPolicyBuilder.CreateDefault().Build();
        }

        public float TierScore(string token)
        {
            return ScoreFor(tierScores, token, 0f);
        }

        public float EventFieldWeight(string token)
        {
            return ScoreFor(eventFieldWeights, token, 1f);
        }

        public float BeliefFieldWeight(string token)
        {
            return ScoreFor(beliefFieldWeights, token, 1f);
        }

        public BeliefDetailBudget DetailBudget(string detailLevel)
        {
            string normalized = NarrativeDetailLevelTokens.Normalize(detailLevel);
            for (int i = 0; i < detailBudgets.Count; i++)
                if (detailBudgets[i].detailLevel == normalized) return detailBudgets[i];
            bool compact = normalized == NarrativeDetailLevelTokens.Compact;
            return new BeliefDetailBudget(normalized, maximumTotalLines, maximumTotalCharacters,
                normalized == NarrativeDetailLevelTokens.Full, !compact, !compact, true);
        }

        private static float ScoreFor(IReadOnlyList<BeliefTokenScore> values, string token, float fallback)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i].token, token, StringComparison.OrdinalIgnoreCase)) return values[i].score;
            return fallback;
        }

        private static IReadOnlyList<BeliefTokenScore> CopyScores(IList<BeliefTokenScore> source)
        {
            List<BeliefTokenScore> copy = new List<BeliefTokenScore>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                    if (source[i] != null) copy.Add(new BeliefTokenScore(source[i].token, source[i].score));
            return new ReadOnlyCollection<BeliefTokenScore>(copy);
        }

        private static IReadOnlyList<BeliefCertaintyBand> CopyBands(IList<BeliefCertaintyBand> source)
        {
            List<BeliefCertaintyBand> copy = new List<BeliefCertaintyBand>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                    if (source[i] != null) copy.Add(new BeliefCertaintyBand(source[i].token, Clamp01(source[i].minimum, 0f), source[i].phrase));
            copy.Sort((left, right) => right.minimum.CompareTo(left.minimum));
            return new ReadOnlyCollection<BeliefCertaintyBand>(copy);
        }

        private static IReadOnlyList<BeliefSemanticAlias> CopyAliases(IList<BeliefSemanticAlias> source)
        {
            List<BeliefSemanticAlias> copy = new List<BeliefSemanticAlias>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                    if (source[i] != null) copy.Add(new BeliefSemanticAlias(source[i].topicToken, ToList(source[i].aliases)));
            return new ReadOnlyCollection<BeliefSemanticAlias>(copy);
        }

        private static IReadOnlyList<BeliefEventEvidenceRule> CopyRules(IList<BeliefEventEvidenceRule> source)
        {
            List<BeliefEventEvidenceRule> copy = new List<BeliefEventEvidenceRule>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    BeliefEventEvidenceRule row = source[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.key) && row.HasSelector)
                        copy.Add(new BeliefEventEvidenceRule(row.key, row.sourceDomain,
                        row.sourceDefName, row.groupKey, row.facet, row.phase, row.povRole,
                        row.mutationCauseToken, ToList(row.addTopics), ToList(row.addSemanticAliases)));
                }
            return new ReadOnlyCollection<BeliefEventEvidenceRule>(copy);
        }

        private static IReadOnlyList<BeliefFoodEvidenceRule> CopyFoodRules(
            IList<BeliefFoodEvidenceRule> source)
        {
            List<BeliefFoodEvidenceRule> copy = new List<BeliefFoodEvidenceRule>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    BeliefFoodEvidenceRule row = source[i];
                    // Preserve malformed and duplicate rows in the detached snapshot. The pure food
                    // policy must see ambiguity and fail closed instead of silently blessing one row.
                    if (row != null)
                        copy.Add(new BeliefFoodEvidenceRule(
                            row.key, row.ingredientKind, row.groupKey, row.matchField));
                }
            return new ReadOnlyCollection<BeliefFoodEvidenceRule>(copy);
        }

        private static IReadOnlyList<BeliefCanonicalEventOwnershipRule> CopyOwnershipRules(
            IList<BeliefCanonicalEventOwnershipRule> source)
        {
            List<BeliefCanonicalEventOwnershipRule> copy =
                new List<BeliefCanonicalEventOwnershipRule>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    BeliefCanonicalEventOwnershipRule row = source[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.sourceDomain)
                        && !string.IsNullOrWhiteSpace(row.sourceDefName)
                        && !string.IsNullOrWhiteSpace(row.downstreamGroupDefName))
                        copy.Add(new BeliefCanonicalEventOwnershipRule(
                            row.sourceDomain, row.sourceDefName, row.downstreamGroupDefName));
                }
            return new ReadOnlyCollection<BeliefCanonicalEventOwnershipRule>(copy);
        }

        private static IReadOnlyList<BeliefMutationEventRule> CopyMutationEventRules(
            IList<BeliefMutationEventRule> source)
        {
            List<BeliefMutationEventRule> copy = new List<BeliefMutationEventRule>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    BeliefMutationEventRule row = source[i];
                    if (row == null || string.IsNullOrWhiteSpace(row.sourceDomain)
                        || string.IsNullOrWhiteSpace(row.sourceDefName)
                        || string.IsNullOrWhiteSpace(row.downstreamGroupDefName)
                        || !BeliefMutationSubjectRoleTokens.IsKnown(row.subjectRole)
                        || string.IsNullOrWhiteSpace(row.evidenceGroupKey)
                        || !BeliefMutationCauseTokens.IsKnown(row.requiredCauseToken)
                        || !BeliefMutationConversionResultTokens.IsKnown(row.conversionResult)
                        || !BeliefMutationCertaintyDirectionTokens.IsKnown(row.certaintyDirection)
                        || !BeliefMutationIdeologyChangeTokens.IsKnown(row.ideologyChange))
                        continue;
                    copy.Add(new BeliefMutationEventRule(
                        row.sourceDomain, row.sourceDefName, row.downstreamGroupDefName,
                        row.subjectRole, row.evidenceGroupKey, row.requiredCauseToken,
                        row.conversionResult, row.certaintyDirection, row.ideologyChange,
                        row.requireAttemptedIdeology));
                }
            return new ReadOnlyCollection<BeliefMutationEventRule>(copy);
        }

        private static IReadOnlyList<CounselEventRule> CopyCounselEventRules(
            IList<CounselEventRule> source)
        {
            List<CounselEventRule> copy = new List<CounselEventRule>();
            if (source != null)
                for (int i = 0; i < source.Count && i < 32; i++)
                {
                    CounselEventRule row = source[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.sourceDefName)
                        && !string.IsNullOrWhiteSpace(row.downstreamGroupDefName)
                        && !string.IsNullOrWhiteSpace(row.resultToken)
                        && !string.IsNullOrWhiteSpace(row.moodEffectToken))
                        copy.Add(new CounselEventRule(row.sourceDefName, row.downstreamGroupDefName,
                            row.resultToken, row.moodEffectToken));
                }
            return new ReadOnlyCollection<CounselEventRule>(copy);
        }

        private static IReadOnlyList<BeliefCorrelationCorrection> CopyCorrections(IList<BeliefCorrelationCorrection> source)
        {
            List<BeliefCorrelationCorrection> copy = new List<BeliefCorrelationCorrection>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    BeliefCorrelationCorrection row = source[i];
                    if (row != null && BeliefCorrectionActionTokens.IsKnown(row.action))
                        copy.Add(new BeliefCorrelationCorrection(row.key, row.action, row.preceptDefName,
                            row.issueDefName, row.memeDefName, row.sourceDomain, row.sourceDefName,
                            row.groupKey, row.topicToken));
                }
            return new ReadOnlyCollection<BeliefCorrelationCorrection>(copy);
        }

        private static IReadOnlyList<BeliefDetailBudget> CopyBudgets(IList<BeliefDetailBudget> source)
        {
            List<BeliefDetailBudget> copy = new List<BeliefDetailBudget>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    BeliefDetailBudget row = source[i];
                    if (row != null) copy.Add(new BeliefDetailBudget(row.detailLevel,
                        Clamp(row.maximumLines, 1, 32, 8), Clamp(row.maximumCharacters, 64, 4096, 900),
                        row.includeDescriptions, row.includeStructure, row.includeMemes, row.includeDeity));
                }
            return new ReadOnlyCollection<BeliefDetailBudget>(copy);
        }

        private static IReadOnlyList<string> CopyStrings(IList<string> source)
        {
            return new ReadOnlyCollection<string>(source == null ? new List<string>() : new List<string>(source));
        }

        private static List<string> ToList(IReadOnlyList<string> source)
        {
            List<string> result = new List<string>();
            if (source != null) for (int i = 0; i < source.Count; i++) result.Add(source[i]);
            return result;
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value >= minimum && value <= maximum ? value : fallback;
        }

        private static float NonNegative(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? fallback : value;
        }

        private static float Clamp01(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
