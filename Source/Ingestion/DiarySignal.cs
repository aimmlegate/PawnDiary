// The ingestion envelope — the single "bus message" every diary event becomes on its way in.
//
// ── Why this exists ──
// Before this, each game event had its own bespoke RecordXxx method on DiaryGameComponent that
// hand-rolled the same skeleton: guard, snapshot a payload, build a CaptureContext, ask the catalog
// to Decide, branch on the decision, dedup against a per-source dictionary, then build text and queue
// the LLM. ~17 sources copy-pasted that glue. A DiarySignal moves the glue into ONE place
// (DiaryGameComponent.Dispatch) and leaves each source with only the two things that genuinely differ:
//   • the impure CAPTURE (read live Pawn/Def state into a plain payload) — the subclass constructor;
//   • the impure EMIT (build localized text + game-context, create the DiaryEvent, queue generation).
//
// ── Layering ──
// A DiarySignal is IMPURE by design: it holds live RimWorld references (Pawn, Def) so its Emit can
// translate text and read live state on the main thread. That is why it lives in PawnDiary.Ingestion,
// NOT in PawnDiary.Capture (the pure, RimWorld-free decision layer that the standalone tests compile).
// The pure half of every source stays where it was: XxxEventData (payload + Decide + BuildGameContext)
// under Source/Capture, unit-tested without the game.
//
// In Redux terms: the catalog Spec is the reducer, the XxxEventData is the action payload, and a
// DiarySignal is the "dispatch(action)" call site — it carries the action plus the side effect to run
// once the reducer has decided.
//
// New to C#/RimWorld? See AGENTS.md. (TS analogy: this is an abstract base "event" class; each source
// extends it like a discriminated-union member, and Dispatch is the one switch that routes them all.)
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// One captured game event on its way through the diary pipeline. A Harmony patch builds the
    /// matching subclass and hands it to <see cref="DiaryEvents.Submit(DiarySignal)"/>; the shared
    /// dispatcher (DiaryGameComponent.Dispatch) runs the universal steps and calls <see cref="Emit"/>.
    /// </summary>
    internal abstract class DiarySignal
    {
        private int historicalEventTick = -1;

        /// <summary>
        /// The plain, RimWorld-free payload the catalog decides on. Carries the event type plus the
        /// facts the pure <c>Decide</c> needs. Built by the subclass from live state at capture time.
        /// </summary>
        public abstract DiaryEventData Payload { get; }

        /// <summary>
        /// Snapshots the impure "should we even consider this?" gates (eligibility, per-def user
        /// toggle, signal-policy enabled) into a CaptureContext for the pure Decider. Runs on the main
        /// thread, so it may read Settings / DefDatabase / the tick manager.
        /// </summary>
        public abstract CaptureContext BuildContext();

        /// <summary>
        /// True only for explicit integration-API requests that ask the dispatcher to bypass soft
        /// drop gates (dedup and source policy drops). Native gameplay signals
        /// keep the default false value so their existing capture policy is unchanged.
        /// </summary>
        public virtual bool ForceRecord => false;

        /// <summary>
        /// Captures durable important-event knowledge when the catalog drops only the diary page
        /// (for example because the player disabled that page group). The default is a no-op because
        /// most signals are deliberately outside the closed important-event allowlist. Overrides must
        /// only project already-captured facts; they must never create a DiaryEvent or queue generation.
        /// </summary>
        public virtual void CaptureKnowledgeWithoutPage(DiaryGameComponent sink)
        {
        }

        /// <summary>
        /// Optional side effect that runs after the catalog and both dedup gates accepted this source,
        /// but before B6 may fold a low-salience page into a digest. H7 uses it to reserve a delayed
        /// reflection from the canonical interaction even when that source page is intentionally folded.
        /// Implementations must not create the current source event; <see cref="Emit"/> still owns it.
        /// </summary>
        public virtual void OnAccepted(DiaryGameComponent sink, CaptureDecision decision)
        {
        }

        /// <summary>
        /// Optional accepted-source callback after B6 pacing and synchronous emission complete. The
        /// boolean proves whether this source registered a canonical page whose prose may later finish.
        /// H7 uses a false value to freeze facts-only fallback for ambient or folded interactions.
        /// </summary>
        public virtual void OnAcceptedEmissionCompleted(
            DiaryGameComponent sink,
            CaptureDecision decision,
            bool sourceEventRegistered)
        {
        }

        /// <summary>
        /// Quality Wave B6. True for everyday, low-stakes moments — the ones whose classified group is
        /// neither important nor combat. Only these are paced by the daily soft cap; anything the
        /// player would call a real event (a raid, a death, a mental break) always writes its page.
        /// Each source computes this itself because each source owns its own group classification.
        /// </summary>
        public virtual bool IsLowSalience => false;

        /// <summary>
        /// Stable saved token naming which source a folded-away moment came from (see
        /// <see cref="DigestPacingPolicy"/>). Empty for sources that never produce a digest line.
        /// </summary>
        public virtual string DigestSourceKind => string.Empty;

        /// <summary>
        /// A compact, already-localized one-liner describing this moment, recorded instead of a page
        /// when the daily soft cap suppressed it. Null/empty simply records nothing. Impure (it
        /// translates), so it is called only on the main thread, from the dispatcher.
        /// </summary>
        public virtual string BuildDigestLine()
        {
            return string.Empty;
        }

        /// <summary>
        /// POV-specific digest line. A shared pair page suppressed for BOTH pawns must give each diary
        /// its own side of the moment; sources with a single perspective inherit the shared line.
        /// </summary>
        public virtual string BuildDigestLineForPawn(string pawnId)
        {
            return BuildDigestLine();
        }

        /// <summary>
        /// The diarists this signal is about to write for, so the soft cap can inspect their daily
        /// counts before the page exists. The default is the payload's single POV pawn; pair-shaped
        /// sources override it to report both. An empty result opts the signal out of pacing.
        /// The dispatcher passes the payload it already read, so no source re-runs a lazy capture.
        /// </summary>
        public virtual void CollectPacedWriters(
            DiaryEventData payload, CaptureDecision decision, List<string> writers)
        {
            if (writers == null || payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.PawnId))
            {
                writers.Add(payload.PawnId);
            }
        }

        /// <summary>
        /// Dedup key for this event (raw, source-prefixed, e.g. <c>"thought|pawnId|defName"</c>).
        /// Return <see cref="string.Empty"/> for sources that do not dedup. The dispatcher uses this
        /// against the shared recent-events store. Usually delegates to a pure <c>Payload.DedupKey()</c>.
        /// </summary>
        public virtual string DedupKey => string.Empty;

        /// <summary>
        /// How long (in ticks) the dedup key blocks a repeat. Impure: reads the XML-tuned window
        /// (DiaryTuning / DiarySignalPolicies). Ignored when <see cref="DedupKey"/> is empty.
        /// </summary>
        public virtual int DedupWindowTicks => 0;

        /// <summary>
        /// Optional generic event-type dedup key, checked after the catalog decision. Most sources
        /// return empty here and either use their detailed <see cref="DedupKey"/> or, if that is also
        /// empty, let Dispatch apply the default short type+subject fallback. Cross-source shapes can
        /// override this to share one key; death descriptions do this so Tale deaths and the fallback
        /// collapse to one final page.
        /// </summary>
        public virtual string EventTypeDedupKey(DiaryEventData payload, CaptureDecision decision)
        {
            return string.Empty;
        }

        /// <summary>
        /// How long the generic event-type key blocks a repeat. Impure XML read; ignored when the
        /// chosen generic key is empty.
        /// </summary>
        public virtual int EventTypeDedupWindowTicks => DiaryTuning.Current.genericEventTypeDedupTicks;

        /// <summary>
        /// Marks a captured signal for chronological insertion when an ownership scope must release it
        /// later. The tick is transient; the signal itself never enters a save.
        /// </summary>
        internal void PreserveHistoricalOrdering(int eventTick)
        {
            historicalEventTick = System.Math.Max(0, eventTick);
        }

        /// <summary>
        /// Binary-compatible pre-Phase-1 solo factory. Keep this exact signature because the separate
        /// RimTest assembly may still be the prior build while the core DLL has already been rebuilt.
        /// </summary>
        protected DiaryEvent CreateSoloEvent(
            DiaryGameComponent sink,
            Pawn pawn,
            Pawn otherPawn,
            string defName,
            string label,
            string text,
            string instruction,
            string gameContext)
        {
            return CreateSoloEvent(
                sink, pawn, otherPawn, defName, label, text, instruction, gameContext, null);
        }

        /// <summary>Creates a solo event, preserving chronology and optional plain belief evidence.</summary>
        protected DiaryEvent CreateSoloEvent(
            DiaryGameComponent sink,
            Pawn pawn,
            Pawn otherPawn,
            string defName,
            string label,
            string text,
            string instruction,
            string gameContext,
            BeliefEventEvidence beliefEvidence)
        {
            return historicalEventTick >= 0
                ? sink.AddSoloEvent(
                    pawn, otherPawn, defName, label, text, instruction, gameContext,
                    historicalEventTick, beliefEvidence)
                : sink.AddSoloEvent(pawn, otherPawn, defName, label, text, instruction,
                    gameContext, beliefEvidence);
        }

        /// <summary>
        /// Binary-compatible pre-Phase-1 pair factory. It forwards to the evidence-aware overload
        /// without re-evaluating or changing capture policy.
        /// </summary>
        protected DiaryEvent CreatePairwiseEvent(
            DiaryGameComponent sink,
            Pawn initiator,
            Pawn recipient,
            string defName,
            string label,
            string initiatorText,
            string recipientText,
            string instruction,
            string gameContext)
        {
            return CreatePairwiseEvent(
                sink, initiator, recipient, defName, label, initiatorText, recipientText,
                instruction, gameContext, null);
        }

        /// <summary>Creates a pair event, preserving chronology and optional plain belief evidence.</summary>
        protected DiaryEvent CreatePairwiseEvent(
            DiaryGameComponent sink,
            Pawn initiator,
            Pawn recipient,
            string defName,
            string label,
            string initiatorText,
            string recipientText,
            string instruction,
            string gameContext,
            BeliefEventEvidence beliefEvidence)
        {
            return historicalEventTick >= 0
                ? sink.AddPairwiseEvent(
                    initiator,
                    recipient,
                    defName,
                    label,
                    initiatorText,
                    recipientText,
                    instruction,
                    gameContext,
                    historicalEventTick,
                    beliefEvidence)
                : sink.AddPairwiseEvent(
                    initiator,
                    recipient,
                    defName,
                    label,
                    initiatorText,
                    recipientText,
                    instruction,
                    gameContext,
                    beliefEvidence);
        }

        /// <summary>
        /// Performs the impure side effect the decision asks for: build the localized event text and
        /// game-context, create the DiaryEvent via the sink's factory, and queue LLM generation (or
        /// route to the ambient/batch sinks). Called only after the event passed Decide + dedup.
        /// </summary>
        /// <param name="sink">The live component that owns the event store and generation queue.</param>
        /// <param name="decision">The catalog's decision (GenerateSolo / GeneratePair / RouteAmbient / …).</param>
        public abstract void Emit(DiaryGameComponent sink, CaptureDecision decision);
    }

    /// <summary>
    /// A colony-wide event that fans out to one entry per eligible colonist (raids, accepted quests,
    /// finished rituals, mood-affecting conditions). The shared dispatcher owns the colony-level dedup
    /// window — peek before the loop, mark only after at least one entry is emitted — so an empty
    /// colonist list can never consume the whole window. The per-pawn set stays source-specific and
    /// lives in <see cref="PerPawnSignals"/>.
    /// </summary>
    internal abstract class DiaryFanoutSignal
    {
        /// <summary>
        /// One dedup key for the whole fan-out (raw, source-prefixed, e.g.
        /// <c>"raid|incidentDef|mapIndex|faction|points"</c>). Empty disables colony dedup.
        /// </summary>
        public abstract string ColonyDedupKey { get; }

        /// <summary>How long (ticks) the colony dedup key blocks a repeat. Impure XML read.</summary>
        public abstract int ColonyDedupTicks { get; }

        /// <summary>
        /// The per-pawn signals to run, already filtered to eligible colonists (and de-duplicated by
        /// pawn id). Fan-out children use their own dispatcher path: payload/context snapshot → Decide
        /// → optional per-pawn dedup → Emit. The solo dispatcher checks dedup before payload capture so
        /// side-effecting solo payloads (currently Ability's RNG roll) can be skipped on duplicates.
        /// </summary>
        public abstract IEnumerable<DiarySignal> PerPawnSignals();
    }
}
