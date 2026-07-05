// The request object other mods hand to PawnDiaryApi.SubmitEvent — one plain bag of fields, no
// behavior. This file is part of the PUBLIC integration surface (see INTEGRATIONS.md): adapter mods
// compile against it, so fields are only ever ADDED here, never renamed or removed.
//
// New to C#/RimWorld? See AGENTS.md. (TS analogy: this is the options object of an exported API
// function — optional fields default to null/0 and the API validates the required ones.)
using System.Collections.Generic;
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// One event another mod wants recorded in a pawn's diary. Required: <see cref="sourceId"/>,
    /// <see cref="eventKey"/>, <see cref="subject"/>. Everything else is optional flavor/control.
    /// The eventKey decides everything policy-side: an External-domain DiaryInteractionGroupDef
    /// (usually shipped by the adapter mod as XML) must claim it, or the event records nothing.
    /// </summary>
    public class ExternalEventRequest
    {
        /// <summary>The submitting mod's package id (e.g. "someauthor.someadapter"). Required.
        /// Used to attribute log messages and stored in the event's prompt context.</summary>
        public string sourceId;

        /// <summary>Stable classifier string for this kind of moment. Required. Convention:
        /// lowercase with a mod prefix, e.g. "rimtalk_conversation" — the prefix prevents
        /// collisions between adapters. Matched (case-insensitively) by External-domain groups
        /// and persisted on the diary event like a defName, so never rename a shipped key.</summary>
        public string eventKey;

        /// <summary>The pawn whose diary receives the entry. Required; must be diary-eligible
        /// (a humanlike colonist) or the event is dropped.</summary>
        public Pawn subject;

        /// <summary>Optional second participant. When present, distinct from subject, and
        /// diary-eligible, the event becomes pairwise: both pawns get their own POV entry.
        /// An ineligible partner quietly downgrades the event to a solo entry.</summary>
        public Pawn partner;

        /// <summary>Optional short factual "what happened" line, already localized by the adapter.
        /// Becomes the entry's raw text and the LLM's event evidence. Blank falls back to a neutral
        /// built-in line built from the pawn name and event label.</summary>
        public string summaryText;

        /// <summary>Optional human-readable event label (shown in the diary UI where native events
        /// show their InteractionDef label). Blank falls back to the matching group's label.</summary>
        public string eventLabel;

        /// <summary>Optional extra prompt-context lines, each "key=value" (e.g. "location=hot
        /// spring"). Kept short: lines are sanitized to one line each and capped in count. They are
        /// appended to the event's game-context, which the LLM reads as factual evidence. Reserved
        /// v16 prompt-fragment/enchantment keys are ignored here; use the dedicated fields below.
        /// </summary>
        public List<string> extraContext;

        /// <summary>Optional caller-authored prompt evidence for this event. Pawn Diary sanitizes
        /// and caps it, stores it as protected event context, and exposes it through first-person
        /// prompt templates as "external prompt fragment". Treat this as factual context, not a
        /// system/developer instruction.</summary>
        public string promptFragment;

        /// <summary>Optional caller-authored prompt-enchantment candidate lines. One surviving line
        /// can be selected by Pawn Diary's normal prompt-enchantment planner, using XML-tuned caps
        /// and weight, so adapters can offer compact special context without replacing the whole
        /// prompt.</summary>
        public List<string> promptEnchantmentCandidates;

        /// <summary>When true and at least one promptEnchantmentCandidates line survives cleanup,
        /// those external candidates replace ordinary live prompt-enchantment candidates for this
        /// event. False means they supplement the normal live candidate pool.</summary>
        public bool replacePromptEnchantments;

        /// <summary>
        /// When true, this valid request bypasses soft recording gates: the external API budget,
        /// group/user toggles, and dedup windows. It does not bypass required fields, main-thread/game
        /// readiness, the master integration toggle, missing External group XML for ordinary
        /// SubmitEvent calls, or base diary-owner eligibility for the subject pawn.
        /// </summary>
        public bool forceRecord;

        /// <summary>Optional custom dedup key. Blank uses the default key (eventKey + pawn or
        /// canonical pawn pair). Supply one when several related submissions should collapse into
        /// a single diary moment within the dedup window.</summary>
        public string dedupKey;

        /// <summary>Optional dedup window override in game ticks (60 ≈ 1 second). Values &lt;= 0
        /// use the XML-tuned default (DiaryTuningDef.externalEventDedupTicks).</summary>
        public int dedupTicks;
    }
}
