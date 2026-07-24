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
    /// Pure mirror of one saved ImportantMemoryRecord (§2.2). Gameplay facts only — never a
    /// generated diary entry or LLM summary.
    /// </summary>
    internal sealed class ImportantMemoryRecordSnapshot
    {
        public string recordId = string.Empty;
        public string dedupKey = string.Empty;
        public string ownerPawnId = string.Empty;
        public string sourceEventId = string.Empty;
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
    }

    /// <summary>Rule for extracting a stable subject key from a gameContext value:
    /// key present and non-sentinel → subjectKeys gets "prefix:value".</summary>
    internal sealed class KnowledgeSubjectKeyRule
    {
        public string contextKey = string.Empty;
        public string prefix = string.Empty;
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
        /// <summary>Fixed subject keys every record of this kind carries — the "title/status
        /// family" entity keys (§3.1), e.g. "title" on every royal-title row so a demotion can
        /// recall the original investiture.</summary>
        public List<string> constantSubjectKeys = new List<string>();
        /// <summary>gameContext keys copied into the record's fact rows (display values).</summary>
        public List<string> factKeys = new List<string>();
        /// <summary>Localized one-line template, e.g. "married {other}" / "lost {part_label}".</summary>
        public string lineTemplate = string.Empty;
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
    }

    /// <summary>Retrieval query built from the CURRENT event (§3.1).</summary>
    internal sealed class KnowledgeQuery
    {
        public string eventId = string.Empty;
        public string ownerPawnId = string.Empty;
        public int currentTick;
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

    /// <summary>Pure copy of one DiaryCultureTopicDef: a cultural interpretation topic (§4.2) and
    /// its structured triggers (§4.3). Detection never matches localized word forms.</summary>
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

        // Relevant-past prompt block (§3.2).
        public int relevantPastMaxLines = 2;
        public int relevantPastMaxChars = 500;
        /// <summary>"- ({0}) {1}" — {0} game date, {1} localized fact line.</summary>
        public string relevantPastLineFormat = "- ({0}) {1}";
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
}
