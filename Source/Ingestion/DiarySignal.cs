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

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// One captured game event on its way through the diary pipeline. A Harmony patch builds the
    /// matching subclass and hands it to <see cref="DiaryEvents.Submit(DiarySignal)"/>; the shared
    /// dispatcher (DiaryGameComponent.Dispatch) runs the universal steps and calls <see cref="Emit"/>.
    /// </summary>
    public abstract class DiarySignal
    {
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
    public abstract class DiaryFanoutSignal
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
