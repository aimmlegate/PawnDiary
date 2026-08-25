// KnowledgeContracts.cs — the closed DTO/vocabulary layer for the deterministic pawn-knowledge
// system (design/MEMORY_SYSTEM_REDESIGN_PLAN.md): lifelong important-event memory records plus
// per-pawn cultural interpretation. This file replaces the old associative MemoryContracts.cs.
//
// Everything here is a plain data object or a stable string token. The impure adapters
// (DiaryGameComponent.Knowledge.cs, DiaryPipelineAdapters) copy live game/Def/settings state INTO
// these snapshots and hand them to the pure classifiers/selectors/planners next to this file.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). This file must stay free of
// Verse/Unity/settings/Def references so the pure test projects can link it directly.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable schema tokens shared by the knowledge capture/retrieval/culture layers.
    /// These are save/XML contract values — never localize or rename them.</summary>
    internal static class KnowledgeTokens
    {
        // Culture provenance (§4.1): how the origin culture was determined.
        public const string CultureSourceCaptured = "captured";
        public const string CultureSourceInferred = "inferred";

        // Capture channels (which listener produced a signal). XML DiaryImportantEventDef rows
        // declare which channel they match so external mods can add rows per channel.
        public const string SignalEvent = "event";
        public const string SignalHediffQuiet = "hediffQuiet";
        public const string SignalHediffRemoved = "hediffRemoved";
        public const string SignalRoleAssigned = "roleAssigned";
        public const string SignalRoleUnassigned = "roleUnassigned";
        public const string SignalIdeoConversion = "ideoConversion";
        public const string SignalDeathInstigator = "deathInstigator";
        public const string SignalDeathFamily = "deathFamily";
        // M7 detached observation channels. These never request or create a diary page.
        public const string SignalMemoryOpinionEpisode = "memoryOpinionEpisode";
        public const string SignalMemoryFormalRelation = "memoryFormalRelation";
        public const string SignalMemoryRelativeState = "memoryRelativeState";
        public const string SignalMemoryFactionDiplomacy = "memoryFactionDiplomacy";
        public const string SignalMemoryFactionLifecycle = "memoryFactionLifecycle";

        // Event-kind tokens that runtime lifecycle code must recognize. Most event kinds remain
        // XML-only; arrival is special because the load bootstrap must treat its durable knowledge
        // record as satisfying the boundary when the player deliberately disabled arrival pages, and
        // normal profile removal/automatic eviction must preserve that lifecycle-owned marker.
        public const string EventKindFactionJoined = "status.faction.joined";

        // Additive record provenance and recall-scope tokens. Old saves omit both fields, so the
        // normalizers deliberately resolve missing or unknown values to captured/contextual.
        public const string SourceKindCaptured = "captured";
        public const string SourceKindPlayer = "player";
        public const string RecallScopeContextual = "contextual";
        public const string RecallScopeBackground = "background";

        // The one player-authored memory kind in v1 of the normal profile editor. This is a saved
        // schema token, not player-facing prose, and must never be localized or renamed.
        public const string EventKindPlayerBackstory = "player.backstory";

        // Owner tokens for signal=event rows: which POV of the diary event owns the record.
        public const string OwnersInitiator = "initiator";
        public const string OwnersRecipient = "recipient";
        public const string OwnersBoth = "both";
        // Non-event channels always pass one explicit owner per signal; rows use this token.
        public const string OwnersProvided = "provided";

        // Built-in line-template placeholders (besides "{<factKey>}" rows).
        public const string PlaceholderOther = "{other}";

        // Sentinel words the prompt schema uses for "no value" — never treated as real values.
        public static bool IsSentinelValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string trimmed = value.Trim();
            return string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "n/a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "unknown", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>One other pawn referenced by a record: stable ID for matching plus a saved
    /// display-name fallback so removed pawns still render (§2.2).</summary>
    internal sealed class KnowledgeParticipant
    {
        public string pawnId = string.Empty;
        public string name = string.Empty;
    }

    /// <summary>One structured fact row: a stable key plus a localized display value captured at
    /// capture time. Values feed line templates; keys never reach the LLM.</summary>
    internal sealed class KnowledgeFact
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    /// <summary>
    /// Pure mirror of one saved ImportantMemoryRecord (§2.2). Gameplay facts plus an optional
    /// editor-authored prose override — never a generated diary entry or LLM summary.
    /// </summary>
    internal sealed class ImportantMemoryRecordSnapshot
    {
        public string recordId = string.Empty;
        public string dedupKey = string.Empty;
        public string ownerPawnId = string.Empty;
        public string sourceEventId = string.Empty;
        /// <summary>KnowledgeTokens.SourceKind*; old/unknown rows resolve to captured.</summary>
        public string sourceKind = KnowledgeTokens.SourceKindCaptured;
        /// <summary>KnowledgeTokens.RecallScope*; old/unknown rows resolve to contextual.</summary>
        public string recallScope = KnowledgeTokens.RecallScopeContextual;
        /// <summary>Stable event-kind token from the matched DiaryImportantEventDef.</summary>
        public string eventKind = string.Empty;
        /// <summary>Ranking family (§3.1 tier 3), e.g. "relationship"/"body"/"status".</summary>
        public string topicKey = string.Empty;
        public int tick;
        /// <summary>The game date at capture, rendered the same way diary pages render theirs.</summary>
        public string dateLabel = string.Empty;
        public List<KnowledgeParticipant> participants = new List<KnowledgeParticipant>();
        /// <summary>Exact subject/entity keys, "prefix:token" (part:Heart, title:Baron…).</summary>
        public List<string> subjectKeys = new List<string>();
        public List<KnowledgeFact> facts = new List<KnowledgeFact>();
        /// <summary>Bounded, capture-time-localized one-line summary used when the event Def is
        /// missing (mod removed). Stable IDs/tokens above remain authoritative (§5).</summary>
        public string fallbackSummary = string.Empty;
        /// <summary>
        /// Optional player/editor-authored replacement for the rendered line. Retrieval identity and
        /// structured facts stay authoritative; this changes only the prose sent to the writer.
        /// </summary>
        public string manualTextOverride = string.Empty;
    }

    /// <summary>Rule for extracting a stable subject key from a gameContext value:
    /// key present and non-sentinel → subjectKeys gets "prefix:value".</summary>
    internal sealed class KnowledgeSubjectKeyRule
    {
        public string contextKey = string.Empty;
        public string prefix = string.Empty;
    }

    /// <summary>Rule for extracting an additional pawn participant id/name from gameContext.</summary>
    internal sealed class KnowledgeParticipantKeyRule
    {
        public string contextKey = string.Empty;
        public string nameContextKey = string.Empty;
    }

    /// <summary>One XML-projected canonical memory-fact declaration.</summary>
    internal sealed class MemoryFactDescriptor
    {
        public string factKind = string.Empty;
        public string contextKey = string.Empty;
        public string aggregationToken = string.Empty;
        public string canonicalValueKind = string.Empty;
        public List<string> allowedStates = new List<string>();
    }

    /// <summary>One exact extractor field declared by a memory capture rule.</summary>
    internal sealed class MemoryRouteExtractor
    {
        public string extractorToken = string.Empty;
    }

    /// <summary>The optional, rule-owned exact route declaration for one promptable capture rule.</summary>
    internal sealed class MemoryThreadRouteRule
    {
        public string subjectKind = string.Empty;
        public List<MemoryRouteExtractor> equivalentExtractors = new List<MemoryRouteExtractor>();
        public string chapterPhasePolicy = string.Empty;
        /// <summary>XML-owned placement instruction; see MemoryChapterDirectiveTokens.</summary>
        public string chapterDirective = "continue_current";
        /// <summary>Stable closure reason used only by a directive that closes a chapter.</summary>
        public string chapterClosureReasonToken = string.Empty;
        public string fallbackLabelSource = string.Empty;
    }

    /// <summary>
    /// Pure copy of one DiaryImportantEventDef row: the XML-owned allowlist entry describing one
    /// important event kind (§2.1) — its capture channel, matchers, owners, and rendering.
    /// </summary>
    internal sealed class ImportantEventRule
    {
        public string defName = string.Empty;
        public bool enabled = true;
        public string eventKind = string.Empty;
        public string topicKey = string.Empty;
        /// <summary>Capture channel (KnowledgeTokens.Signal*).</summary>
        public string signal = KnowledgeTokens.SignalEvent;
        /// <summary>Ascending evaluation order; first matching rule wins within a channel.</summary>
        public int order = 100;
        /// <summary>Exact defName matches (case-insensitive): diary interactionDefName for the
        /// event channel, hediff defName for the quiet-hediff channel.</summary>
        public List<string> matchDefNames = new List<string>();
        /// <summary>Suffix matches against the lowercased defName (e.g. "_missingpart").</summary>
        public List<string> matchSuffixes = new List<string>();
        /// <summary>Extra gameContext gates: "key=" (present, non-sentinel) or "key=value".</summary>
        public List<string> requireContext = new List<string>();
        /// <summary>KnowledgeTokens.Owners* — who owns the record for the event channel.</summary>
        public string owners = KnowledgeTokens.OwnersBoth;
        public List<KnowledgeSubjectKeyRule> subjectKeyRules = new List<KnowledgeSubjectKeyRule>();
        public List<KnowledgeParticipantKeyRule> participantKeyRules =
            new List<KnowledgeParticipantKeyRule>();
        /// <summary>Fixed subject keys every record of this kind carries — the "title/status
        /// family" entity keys (§3.1), e.g. "title" on every royal-title row so a demotion can
        /// recall the original investiture.</summary>
        public List<string> constantSubjectKeys = new List<string>();
        /// <summary>gameContext keys copied into the record's fact rows (display values).</summary>
        public List<string> factKeys = new List<string>();
        /// <summary>Localized one-line template, e.g. "married {other}" / "lost {part_label}".</summary>
        public string lineTemplate = string.Empty;

        // Unified-memory metadata. M7 consumes this XML-owned contract while the legacy record remains
        // active until M11 changes the public activation gate.
        public string captureSourceToken = string.Empty;
        public string memoryKind = string.Empty;
        public string memoryCategory = string.Empty;
        public string baseImportance = string.Empty;
        public List<MemoryFactDescriptor> memoryFacts = new List<MemoryFactDescriptor>();
        public MemoryThreadRouteRule threadRoute;
        public bool consolidationEligible;
        public List<string> promptConsumerIds = new List<string>();
        public bool authoritativePageOwned;
        /// <summary>Exact relation Def names whose observation transition this page route owns.</summary>
        public List<string> authoritativeRelationDefNames = new List<string>();
    }

    /// <summary>
    /// One capture signal handed to the pure classifier. The impure listener fills exactly one of
    /// these per owner-candidate group; for the diary-event channel initiator/recipient stand in
    /// for the owner slots and the classifier resolves owners from the rule.
    /// </summary>
    internal sealed class KnowledgeCaptureSignal
    {
        /// <summary>Capture channel (KnowledgeTokens.Signal*).</summary>
        public string signal = KnowledgeTokens.SignalEvent;
        /// <summary>Diary interactionDefName / hediff defName / channel-specific token.</summary>
        public string defName = string.Empty;
        public string sourceEventId = string.Empty;
        /// <summary>
        /// Canonical occurrence identity. Authoritative diary routes copy DiaryEvent.eventId here;
        /// detached no-page adapters either supply their own durable identity or the bounded fallback
        /// evidence below.
        /// </summary>
        public string sourceOccurrenceId = string.Empty;
        public long sourceLocalSequenceInvariant;
        public bool sourceProvesUniqueness;
        public int tick;
        /// <summary>Localized game-date label captured alongside the signal.</summary>
        public string dateLabel = string.Empty;
        /// <summary>Raw "key=value; key=value" context (diary gameContext or channel-built).</summary>
        public string gameContext = string.Empty;
        public string initiatorPawnId = string.Empty;
        public string initiatorName = string.Empty;
        public string recipientPawnId = string.Empty;
        public string recipientName = string.Empty;
        /// <summary>Explicit owner for non-event channels (owners = "provided").</summary>
        public string providedOwnerPawnId = string.Empty;
        /// <summary>Other pawns the record should reference besides the POV slots.</summary>
        public List<KnowledgeParticipant> extraParticipants = new List<KnowledgeParticipant>();
    }

    /// <summary>Classifier output: one record draft for one owner (before persistence).</summary>
    internal sealed class ImportantMemoryDraft
    {
        public string ownerPawnId = string.Empty;
        public string matchedRuleDefName = string.Empty;
        public ImportantMemoryRecordSnapshot record = new ImportantMemoryRecordSnapshot();
        /// <summary>Optional current-schema factual draft. Null means M7 policy refused safely.</summary>
        public FactualMemoryDraft factual;
    }

    /// <summary>One detached exact subject ready for current-schema factual persistence.</summary>
    internal sealed class FactualMemorySubjectDraft
    {
        public string subjectRefId = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string frozenLabel = string.Empty;
        public string roleToken = string.Empty;
        public string knownnessToken = string.Empty;
    }

    /// <summary>One detached canonical fact ready for current-schema factual persistence.</summary>
    internal sealed class FactualMemoryFactDraft
    {
        public string factId = string.Empty;
        public string factKind = string.Empty;
        public string canonicalSubjectKind = string.Empty;
        public string canonicalSubjectId = string.Empty;
        public string aggregationToken = string.Empty;
        public string canonicalValueKind = string.Empty;
        public string canonicalValue = string.Empty;
        public bool majorTurningPoint;
        public bool reversal;
    }

    /// <summary>
    /// Pure M7 classifier output. Verse/Scribe rows and settings remain outside this DTO; the main-thread
    /// adapter may admit it only after resolving the owner's current autobiographical epoch.
    /// </summary>
    internal sealed class FactualMemoryDraft
    {
        public string ownerPawnId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string sourceEventId = string.Empty;
        public string sourceKindToken = string.Empty;
        public string captureRuleId = string.Empty;
        public string factDiscriminator = string.Empty;
        public string kind = string.Empty;
        public string category = string.Empty;
        public string importance = string.Empty;
        public long originalEventTick;
        public bool consolidationEligible;
        public bool authoritativePageOwned;
        public bool routeReliable;
        public string routeReasonToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string frozenSubjectLabel = string.Empty;
        public string chapterPhaseToken = string.Empty;
        public string chapterDirective = string.Empty;
        public string chapterClosureReasonToken = string.Empty;
        public string automaticWording = string.Empty;
        public FactualMemorySubjectDraft primarySubject;
        public List<FactualMemorySubjectDraft> secondarySubjects =
            new List<FactualMemorySubjectDraft>();
        public List<FactualMemoryFactDraft> facts = new List<FactualMemoryFactDraft>();
        public string provenanceRefId = string.Empty;
    }

    /// <summary>Retrieval query built from the CURRENT event (§3.1).</summary>
    internal sealed class KnowledgeQuery
    {
        public string eventId = string.Empty;
        public string ownerPawnId = string.Empty;
        public int currentTick;
        /// <summary>
        /// When true, an exact subject-key match is not enough: a record must name one of the
        /// concrete pawns in <see cref="participantIds"/>. Delayed social reflections use this so a
        /// broad entity key cannot pull in a memory that was not actually about their subject.
        /// </summary>
        public bool requireParticipantOverlap;
        /// <summary>
        /// Source event IDs that are canonical evidence for the current page and therefore must not
        /// be echoed back as "earlier" memory. Kept separate from <see cref="eventId"/> because a
        /// delayed derivative page has its own ID as well as the original source page's ID.
        /// </summary>
        public List<string> excludedSourceEventIds = new List<string>();
        /// <summary>Concrete other pawns of the current event.</summary>
        public List<string> participantIds = new List<string>();
        /// <summary>Exact subject keys extracted from the current event's context.</summary>
        public List<string> subjectKeys = new List<string>();
        /// <summary>Topic families the current event classified into (ranking tier 3).</summary>
        public List<string> topicKeys = new List<string>();
    }

    /// <summary>Why a candidate was rejected — dev-report vocabulary (§7).</summary>
    internal static class KnowledgeRejectReasons
    {
        public const string SelfEcho = "self_echo";
        public const string ExcludedSource = "excluded_source_event";
        public const string NoOverlap = "no_shared_participant_or_subject";
        public const string OverCap = "ranked_below_line_cap";
        public const string Blank = "blank_record";
    }

    /// <summary>One row of the retrieval report: candidate, verdict, and why (§7).</summary>
    internal sealed class KnowledgeCandidateReport
    {
        public string recordId = string.Empty;
        public string eventKind = string.Empty;
        public bool selected;
        public bool sharedParticipant;
        public bool sharedSubject;
        public bool sharedTopic;
        public string rejectReason = string.Empty;
    }

    /// <summary>Deterministic retrieval result: at most the line-cap records, plus the full
    /// candidate report for the dev tab.</summary>
    internal sealed class KnowledgeSelectionResult
    {
        public List<ImportantMemoryRecordSnapshot> selected = new List<ImportantMemoryRecordSnapshot>();
        public List<KnowledgeCandidateReport> report = new List<KnowledgeCandidateReport>();
    }

    /// <summary>Pure copy of one DiaryCultureTopicDef: a cultural interpretation topic (§4.2),
    /// its structured triggers, and its localized natural-language terms (§4.3).</summary>
    internal sealed class CultureTopicRule
    {
        public string topicKey = string.Empty;
        public bool enabled = true;
        public int order = 100;
        /// <summary>GameContext keys: a selected GameContext-source field with this contextKey
        /// (non-blank value) triggers the topic.</summary>
        public List<string> triggerContextKeys = new List<string>();
        /// <summary>"key=value" rows: a selected GameContext-source field with that contextKey
        /// whose rendered value equals the given stable token triggers the topic.</summary>
        public List<string> triggerContextPairs = new List<string>();
        /// <summary>Stable schema markers ("xenotype=") searched inside scannable field values.</summary>
        public List<string> triggerValueMarkers = new List<string>();
        /// <summary>
        /// Localized words or phrases searched with Unicode word boundaries. A trailing '*' on an
        /// individual word matches inflected suffixes, for example "mechanoid*" or "механоид*".
        /// </summary>
        public List<string> triggerTextTerms = new List<string>();
        /// <summary>Exact event defNames (interactionDefName) that trigger the topic.</summary>
        public List<string> triggerDefNames = new List<string>();
    }

    /// <summary>One authored clause: the cultural stance for one topic (≤80 chars, localized).</summary>
    internal sealed class CultureClause
    {
        public string topicKey = string.Empty;
        public string clause = string.Empty;
    }

    /// <summary>Pure copy of one DiaryCultureProfileDef: a CultureDef's writing lens (§4.2).</summary>
    internal sealed class CultureProfile
    {
        public string cultureDefName = string.Empty;
        public List<CultureClause> clauses = new List<CultureClause>();

        public string ClauseFor(string topicKey)
        {
            if (string.IsNullOrWhiteSpace(topicKey) || clauses == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < clauses.Count; i++)
            {
                CultureClause row = clauses[i];
                if (row != null
                    && string.Equals(row.topicKey, topicKey, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(row.clause))
                {
                    return row.clause.Trim();
                }
            }

            return string.Empty;
        }
    }

    /// <summary>Pure mirror of the pawn's persisted culture state (§4.1).</summary>
    internal sealed class CultureStateSnapshot
    {
        public string originCultureDefName = string.Empty;
        /// <summary>KnowledgeTokens.CultureSource* or empty when unresolved.</summary>
        public string originSource = string.Empty;
        public string adoptedCultureDefName = string.Empty;
    }

    /// <summary>Inputs the impure side gathers for one origin-culture resolution (§4.1).</summary>
    internal sealed class CultureResolutionInput
    {
        /// <summary>
        /// Culture captured at the pawn's origin boundary before a mutable faction/ideology change.
        /// When present this outranks all current-state fallbacks.
        /// </summary>
        public string capturedOriginCultureDefName = string.Empty;
        public bool ideologyActive;
        /// <summary>pawn.Ideo?.culture defName (blank when absent).</summary>
        public string ideoCultureDefName = string.Empty;
        /// <summary>Origin faction's allowedCultures defNames in XML order.</summary>
        public List<string> factionCultureDefNames = new List<string>();
        /// <summary>True when initializing state for a pre-existing pawn on a legacy save —
        /// the result is marked "inferred" and never silently rewritten later.</summary>
        public bool legacyInference;
    }

    /// <summary>One planned inline culture annotation: append <see cref="text"/> to the end of the
    /// rendered field at <see cref="fieldIndex"/> (§4.3).</summary>
    internal sealed class CultureAnnotationPlanEntry
    {
        public int fieldIndex = -1;
        public string topicKey = string.Empty;
        public string text = string.Empty;
    }

    /// <summary>Annotation planning result plus the dev-report of matched topics (§7).</summary>
    internal sealed class CultureAnnotationPlan
    {
        public List<CultureAnnotationPlanEntry> entries = new List<CultureAnnotationPlanEntry>();
        public List<string> matchedTopics = new List<string>();
    }

    /// <summary>One prompt field as the annotation planner sees it after detail selection.</summary>
    internal sealed class AnnotationFieldView
    {
        public int index = -1;
        public string source = string.Empty;
        public string contextKey = string.Empty;
        public string resolvedValue = string.Empty;
        /// <summary>
        /// Full structured gameContext for a selected GameContext field. This lets XML topic triggers
        /// inspect stable keys that are present in the event even when the template displays a
        /// different individual context key.
        /// </summary>
        public string structuredContext = string.Empty;
    }

    /// <summary>Per-owner record totals for global-cap eviction planning (§2.3).</summary>
    internal sealed class KnowledgeOwnerLoad
    {
        public string ownerPawnId = string.Empty;
        /// <summary>True when the owner pawn no longer exists in the game world at all (no live
        /// pawn, no corpse, no world pawn). Dead-but-present owners are NOT absent — their records
        /// are retained for resurrection.</summary>
        public bool ownerAbsent;
        /// <summary>(recordId, tick) pairs for this owner, any order.</summary>
        public List<KnowledgeRecordStub> records = new List<KnowledgeRecordStub>();
    }

    /// <summary>Minimal record identity for eviction planning.</summary>
    internal sealed class KnowledgeRecordStub
    {
        public string recordId = string.Empty;
        public int tick;
        /// <summary>Original owner-list ordinal; final tie-break and exact runtime deletion handle.</summary>
        public int sourceIndex = -1;
        /// <summary>
        /// True for the owning pawn's exact canonical player/background singleton or a captured,
        /// contextual faction-joined lifecycle marker. Planners count protected rows toward caps but
        /// may never choose them for automatic eviction.
        /// </summary>
        public bool protectedFromAutomaticEviction;
    }

    /// <summary>Eviction plan: record ids to drop plus whether the one bounded global-cap warning
    /// should be emitted (§2.3).</summary>
    internal sealed class KnowledgeEvictionPlan
    {
        public List<string> dropRecordIds = new List<string>();
        public bool globalCapHit;
    }

    /// <summary>
    /// The full XML-owned policy snapshot (caps, prompt shape, annotation policy) copied from
    /// DiaryKnowledgeTuningDef plus the player's single injection switch. CreateDefault mirrors
    /// the shipped XML exactly; the parity test in the pure suite enforces it.
    /// </summary>
    internal sealed class KnowledgePolicySnapshot
    {
        /// <summary>The one player-facing switch (§3.2): prompt injection only. Capture and
        /// culture tracking continue while this is off.</summary>
        public bool injectionEnabled = true;

        // Defensive limits (§2.3).
        public int maxRecordsPerPawn = 512;
        public int maxRecordsGlobal = 20000;
        public int fallbackSummaryMaxChars = 240;
        public int playerAuthoredMemoryMaxChars = 450;

        // Relevant-past prompt block (§3.2).
        public int relevantPastMaxLines = 2;
        public int relevantPastMaxChars = 500;
        /// <summary>"- ({0}) {1}" — {0} game date, {1} localized fact line.</summary>
        public string relevantPastLineFormat = "- ({0}) {1}";
        /// <summary>
        /// XML/DefInjected factual framing for player background prose. The code fallback contains
        /// no English prompt prose so a missing Def safely emits only the authored text.
        /// </summary>
        public string backgroundMemoryLineFormat = "{0}";
        public string relevantPastInstruction = string.Empty;

        // Inline culture annotation (§4.3).
        public int maxCultureTopicsPerPrompt = 2;
        /// <summary>"(culture: {0})"</summary>
        public string annotationSingleFormat = "(culture: {0})";
        /// <summary>"(origin: {0}; adopted: {1})"</summary>
        public string annotationDualFormat = "(origin: {0}; adopted: {1})";
        /// <summary>Field sources the topic detector MAY scan. System instructions, past-memory
        /// text, and generated text are excluded by never being listed here.</summary>
        public List<string> scannableSources = new List<string>();
        /// <summary>Subject-key extraction applied to the CURRENT event when building the
        /// retrieval query (record-side extraction lives on the event rules).</summary>
        public List<KnowledgeSubjectKeyRule> querySubjectKeyRules = new List<KnowledgeSubjectKeyRule>();

        public static KnowledgePolicySnapshot CreateDefault()
        {
            KnowledgePolicySnapshot policy = new KnowledgePolicySnapshot();
            policy.scannableSources.Add("EventNoun");
            policy.scannableSources.Add("PovText");
            policy.scannableSources.Add("NeutralText");
            policy.scannableSources.Add("PawnSummary");
            policy.scannableSources.Add("Setting");
            policy.scannableSources.Add("GameContext");
            policy.scannableSources.Add("DeathFacts");
            policy.scannableSources.Add("ArrivalFacts");
            policy.querySubjectKeyRules.Add(Rule("romance", "relation"));
            policy.querySubjectKeyRules.Add(Rule("part_def", "part"));
            policy.querySubjectKeyRules.Add(Rule("hediff", "hediff"));
            policy.querySubjectKeyRules.Add(Rule("royal_title", "title"));
            policy.querySubjectKeyRules.Add(Rule("ideological_role", "role"));
            policy.querySubjectKeyRules.Add(Rule("xenotype", "xenotype"));
            policy.querySubjectKeyRules.Add(Rule("faction", "faction"));
            policy.querySubjectKeyRules.Add(Rule("weapon", "weapon"));
            return policy;
        }

        private static KnowledgeSubjectKeyRule Rule(string contextKey, string prefix)
        {
            return new KnowledgeSubjectKeyRule { contextKey = contextKey, prefix = prefix };
        }
    }

    /// <summary>
    /// Normalizes malformed XML-owned numeric policy before it reaches eviction or prompt loops.
    /// Positive authored values stay tunable; zero/negative values fall back to the shipped defaults.
    /// </summary>
    internal static class KnowledgePolicyNormalization
    {
        public const int DefaultEvictionScanIntervalTicks = 150000;

        /// <summary>Repairs nonpositive caps on a detached policy snapshot in place.</summary>
        public static KnowledgePolicySnapshot Normalize(KnowledgePolicySnapshot policy)
        {
            KnowledgePolicySnapshot effective = policy ?? KnowledgePolicySnapshot.CreateDefault();
            KnowledgePolicySnapshot defaults = KnowledgePolicySnapshot.CreateDefault();
            effective.maxRecordsPerPawn = PositiveOrDefault(
                effective.maxRecordsPerPawn, defaults.maxRecordsPerPawn);
            effective.maxRecordsGlobal = PositiveOrDefault(
                effective.maxRecordsGlobal, defaults.maxRecordsGlobal);
            effective.fallbackSummaryMaxChars = PositiveOrDefault(
                effective.fallbackSummaryMaxChars, defaults.fallbackSummaryMaxChars);
            effective.playerAuthoredMemoryMaxChars = PositiveOrDefault(
                effective.playerAuthoredMemoryMaxChars, defaults.playerAuthoredMemoryMaxChars);
            effective.relevantPastMaxLines = PositiveOrDefault(
                effective.relevantPastMaxLines, defaults.relevantPastMaxLines);
            effective.relevantPastMaxChars = PositiveOrDefault(
                effective.relevantPastMaxChars, defaults.relevantPastMaxChars);
            effective.maxCultureTopicsPerPrompt = PositiveOrDefault(
                effective.maxCultureTopicsPerPrompt, defaults.maxCultureTopicsPerPrompt);
            return effective;
        }

        /// <summary>Returns a safe elapsed-time cadence for the impure knowledge scan adapter.</summary>
        public static int EvictionScanIntervalTicks(int configured)
        {
            return PositiveOrDefault(configured, DefaultEvictionScanIntervalTicks);
        }

        private static int PositiveOrDefault(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }
    }
}
