// The orchestrator. Owns the saved diary data and drives the whole flow: submit/capture an event
// signal -> build a DiaryEvent with context -> queue an LLM
// request -> apply the result each tick -> hand views to the UI. Context/prompt building live in
// DiaryContextBuilder / DiaryPromptBuilder; the saved data models in DiaryEvent / PawnDiaryRecord.
// This is a RimWorld GameComponent (lifecycle hooks: GameComponentTick, GameComponentUpdate,
// ExposeData, etc.).
// New to C#/RimWorld? See AGENTS.md.
//
// ── This class is large, so it is split across several files using C# `partial class` ──
// A `partial` class is one class whose members are spread over multiple files; the compiler
// stitches them back together, so every file shares the same fields and private methods exactly
// as if they were one file. The split is purely organizational (no behavior change). Map:
//   DiaryGameComponent.cs                — this file: state, lifecycle hooks (tick/save/load)
//   DiaryGameComponent.PublicApi.cs      — read/write entry points the UI calls
//   ── event capture ──
//   Most hook-driven events (mental states, tales, mood conditions, thoughts, inspirations,
//   rituals, abilities, romance, ...) enter through DiaryEvents.Submit as XxxSignal objects
//   (Source/Ingestion/Sources) and land in DiaryGameComponent.Dispatch.cs; the pure per-event
//   decisions live under Source/Capture. The scan-driven and batching sources keep partials here:
//   DiaryGameComponent.Interactions.cs   — social interactions (PlayLog.Add)
//   DiaryGameComponent.ThoughtProgression.cs — staged situational need thoughts (hunger, rest, etc.)
//   DiaryGameComponent.Hediffs.cs        — XML-driven health-condition signals (AddHediff + severity scan)
//   DiaryGameComponent.Work.cs           — occasional solo notes about current pawn work
//   DiaryGameComponent.Arrivals.cs       — the neutral "how this pawn joined" first entry
//   DiaryGameComponent.InteractionBatching.cs — XML-configured batching for quick social logs
//   DiaryGameComponent.TaleBatching.cs   — delayed solo batches for bursty Tale events
//   DiaryGameComponent.AmbientThoughts.cs — day-note batching for low-impact temporary thoughts
//   DiaryGameComponent.Odyssey.cs         — guarded Odyssey journey/history save state + baselines
//   DiaryGameComponent.Royalty.cs         — guarded Royalty state, baselines, persona reconciliation/pages
//   ──
//   DiaryGameComponent.EventFactory.cs   — AddPairwiseEvent/AddSoloEvent: build + register DiaryEvents
//   DiaryGameComponent.Memory.cs         — associative-memory repository, recall/deposit appliers, eviction
//   DiaryGameComponent.Generation.cs     — deciding what to (re)queue (scan + per-pawn re-enable)
//   DiaryGameComponent.GenerationDispatch.cs — QueuePrompt choke point, LLM dispatch, apply results, titles
//   DiaryGameComponent.ApiLanes.cs       — choose which configured API lane handles each request
//   DiaryGameComponent.GenerationEligibility.cs — generation gates + persona/enchantment/humor rules + pawn lookup
//   DiaryGameComponent.Lookup.cs         — finding diary records/events + eligibility helpers
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Central coordinator for the diary system: records events, queues LLM generation, applies
    /// finished results on ticks/updates, persists everything on save/load, and serves entry views to
    /// the UI. Reach the live instance via <see cref="Instance"/>.
    /// </summary>
    public partial class DiaryGameComponent : GameComponent
    {
        // Dedup windows live in DiaryTuningDef (editable XML); see DiaryTuning.Current.
        // The transient dedup dictionaries keep only recent keys. Once any dictionary crosses this
        // size, the shared gate sweeps entries outside that source's configured dedup window.
        private const int RecentEventPruneThreshold = 512;

        // Per-pawn saved state (event references, persona, enabled flag). Persisted via ExposeData.
        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        // O(1) pawnId->record index mirroring `diaries`. NOT saved: rebuilt from the loaded list in
        // PostLoadInit (RebuildDiaryIndex) and kept in sync on create. `diaries` is append-only (never
        // removed/cleared), so the index only ever grows. Mirrors DiaryEventRepository.eventsById so the
        // per-pawn lookups (FindDiary / FindDiaryByPawnId), called per captured event, stay constant-time
        // instead of linear-scanning every record (including dead colonists) as the colony ages.
        private readonly Dictionary<string, PawnDiaryRecord> diariesById = new Dictionary<string, PawnDiaryRecord>();
        // The saved event store: every DiaryEvent across all pawns plus the O(1) id->event lookup
        // index that mirrors it. Owns FindEvent/Register/RebuildIndex and the "diaryEvents" Scribe
        // key. Extracted out of this class so the event store has one clear owner (Run Card 10); this
        // class remains the only RimWorld lifecycle/save owner and drives the repository from
        // ExposeData/GameComponentTick. See DiaryEventRepository.cs.
        private readonly DiaryEventRepository events = new DiaryEventRepository();
        // Compact archive of old display-only diary pages. Retention converts completed/stale old POVs
        // here before dropping their heavy hot DiaryEvent refs, so long histories remain visible without
        // staying in generation/title/orphan scans. Saved under "diaryArchiveEntries".
        private readonly DiaryArchiveRepository archive = new DiaryArchiveRepository();
        // Saved XML event-window state. Each row is a long-running narrative window such as "gray
        // flesh was found, but the metalhorror has not emerged yet." The Def remains XML-owned; this
        // list only remembers active runtime instances across save/load.
        private List<ActiveEventWindowState> activeEventWindows = new List<ActiveEventWindowState>();
        // Interaction batches still accumulating lines; keyed by group/pair/optional def. Not saved
        // because ExposeData flushes first.
        private readonly Dictionary<string, PendingInteractionBatch> pendingInteractionBatches = new Dictionary<string, PendingInteractionBatch>();
        // Ambient interaction notes still accumulating low-stakes social texture; keyed by
        // group+pawn+day. Also transient and flushed before saving.
        private readonly Dictionary<string, PendingAmbientInteractionNote> pendingAmbientInteractionNotes = new Dictionary<string, PendingAmbientInteractionNote>();
        // Bursty Tale events (notably combat wounds/kills) that are waiting to become one solo entry
        // per pawn after the configured quiet window. Transient and flushed before saving.
        private readonly Dictionary<string, PendingTaleBatch> pendingTaleBatches = new Dictionary<string, PendingTaleBatch>();
        // Prevents an ambient group from writing twice for the same pawn/day after an early save or
        // max-count flush in the same play session.
        private readonly HashSet<string> writtenAmbientInteractionNotes = new HashSet<string>();
        // Ambient temporary thoughts still accumulating into one per-pawn day memory. Not saved;
        // flushed before saving just like interaction batches.
        private readonly Dictionary<string, PendingAmbientThoughtNote> pendingAmbientThoughtNotes = new Dictionary<string, PendingAmbientThoughtNote>();
        private readonly HashSet<string> writtenAmbientThoughtNotes = new HashSet<string>();

        // Submit-bus sources share one consolidated transient store (see
        // DiaryGameComponent.Dispatch.cs: recentEvents). Every hook-driven source has now been
        // migrated onto that bus; the transient maps kept here are the remaining scan-loop bookkeeping
        // (ThoughtProgression / Hediff / Work / DayReflection progression state, the generic
        // event-window guard, and the raid generation-delay map) — none of them is a legacy recorder.
        // Transient (not saved) generation delay for ordinary raids. The event is recorded as soon as
        // RimWorld spawns the threat, but the LLM waits a short XML-tuned window so walk-in raids read
        // more like anticipation/contact than instant combat aftermath.
        private readonly Dictionary<string, int> delayedRaidGenerationReadyTicks = new Dictionary<string, int>();
        // Transient (not saved) guard against generic event-window start/end signals double-firing.
        // Each entry remembers its own Def-configured dedup window so a prune driven by one window
        // never evicts a still-live key with a different window (see RecentEventExpiry).
        private readonly Dictionary<string, RecentEventEntry> recentEventWindowEvents = new Dictionary<string, RecentEventEntry>();
        // Transient (not saved) list of quests already seen in the accepted state. Quest.Accept can
        // be reached through more than one RimWorld UI path, so the tick scanner uses this to catch
        // missed acceptance transitions without duplicating hook-driven entries.
        private readonly HashSet<int> knownAcceptedQuestIds = new HashSet<int>();
        // Transient (not saved): event-role keys ("eventId|role") seen pending-but-not-in-flight on the
        // previous generation scan. An entry must look orphaned on two consecutive scans before the
        // orphan recovery re-queues it, so a request that merely finished between scans (its result
        // still queued for the main thread) is never mistaken for an orphan. See
        // RecoverOrphanedPendingGenerations.
        private HashSet<string> orphanCandidatesLastScan = new HashSet<string>();
        // Loaded saves already contain accepted quests. If the quest manager is not ready during
        // LoadedGame, the first scanner pass baselines them instead of backfilling old acceptances.
        private bool baselineQuestAcceptancesOnNextScan;
        // New games build maps after StartedNewGame. This flag lets the first tick with maps create
        // founding-colonist arrival entries using scenario context.
        private bool initialArrivalScanPending;
        // Generated direct-speech PlayLog rows need their displayed text after save/load. The actual
        // PlayLogEntry_Interaction stays in RimWorld's PlayLog; this map keys its LogID to the LLM
        // speech line our ToGameStringFromPOV patch should show.
        private Dictionary<int, string> generatedSpeechPlayLogTexts = new Dictionary<int, string>();
        private List<int> generatedSpeechPlayLogTextKeys;
        private List<string> generatedSpeechPlayLogTextValues;
        // Transient closed-window badge cache keyed by pawn id. Inspect-tab/gizmo drawing reads only
        // this dictionary, never the saved diary lists, so selecting a pawn with thousands of pages
        // cannot start a diary-record lookup or history scan.
        private readonly Dictionary<string, DiaryCommandStatus> commandStatusByPawnId = new Dictionary<string, DiaryCommandStatus>();

        // How often (in ticks) GameComponentTick rescans saved events to (re)queue any pending
        // generations once something has explicitly requested a catch-up pass.
        private const int GenerationScanIntervalTicks = 200;
        private int nextGenerationScanTick;
        // Most new events queue their LLM request immediately, so a full active-event scan is only
        // needed for load catch-up, delayed raid entries, and orphan recovery.
        private bool generationScanRequested;
        // Orphan recovery needs a full active-event pass, but only handles rare entries stranded on
        // "writing..." after a dropped background request. Run it less often than normal queue scans.
        private const int OrphanRecoveryScanIntervalTicks = 600;
        private int nextOrphanRecoveryScanTick;

        // Quest acceptance has a direct Harmony hook, but a light state scan covers UI or modded
        // accept paths that do not pass through the expected method.
        private const int QuestAcceptanceScanIntervalTicks = 120;
        private int nextQuestAcceptanceScanTick;

        // Active event windows are few, but scanning timeouts on a short interval avoids work every
        // tick and still closes expired prompt context promptly.
        private const int EventWindowTimeoutScanIntervalTicks = 250;
        private int nextEventWindowTimeoutScanTick;

        // Ambient notes read like end-of-day memories, so when a pawn goes to sleep or rests we try
        // to write any sufficiently full low-stakes note for that pawn. This is only a periodic scan
        // rather than a Harmony patch because the pending note dictionaries already decide what is
        // worth writing.
        private const int AmbientSleepFlushScanIntervalTicks = 250;
        private int nextAmbientSleepFlushScanTick;

        // Work entries are sampled rather than hook-driven because vanilla work jobs can run for a
        // long time and there is no single "meaningful work moment happened" callback. The scanner
        // interval itself is XML-tunable; this field only stores the next allowed scan tick.
        private int nextWorkScanTick;
        // Pawn progression entries are sampled: skills, psylink, titles, and xenotypes are slow-moving
        // state where baseline suppression is more important than catching every internal setter.
        private int nextProgressionScanTick;
        // Persona bonds use their own XML cadence so disabling progression pages never freezes
        // separation/recovery truth. This unscribed long deadline prevents a large compatibility
        // override near Int32.MaxValue from wrapping negative and turning the scan into per-tick work.
        private long nextRoyaltyPersonaReconciliationTick;

        // Current absolute in-game day. Uses TicksAbs so day-note batching follows the world calendar.
        private static int CurrentDayIndex
        {
            get
            {
                return Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            }
        }

        // True only when a game is actually in play. A few of our Harmony hooks also fire during pawn
        // generation and map init — AddHediff while rolling starting-pawn age injuries, SetFaction
        // during scenario setup — before the world clock exists. Recording then is meaningless and
        // trips RimWorld's "TicksAbs accessed before gameStartAbsTick is set" error via CurrentDayIndex
        // and event date stamping, so those hooks gate on this. (Verse.Current is qualified because
        // this type also defines a static Current.)
        internal static bool GamePlaying
        {
            get { return Verse.Current.ProgramState == ProgramState.Playing; }
        }

        public DiaryGameComponent(Game game)
        {
            // One LLM session spans the lifetime of one loaded Game, and this constructor is the
            // single place that starts it. Constructing a Game (new game OR loading a save) runs this
            // ctor first, so BeginSession here cancels any requests still running from a previous Game
            // before this one queues anything. StartedNewGame/LoadedGame deliberately do NOT call
            // BeginSession again: by the time they run, this Game has already queued its own startup
            // entries (e.g. founding-colonist starting thoughts queued during InitNewGame), and a
            // second BeginSession would cancel those mid-flight and strand them forever on "Generating".
            LlmClient.BeginSession();
            Integration.ExternalLlmCompletionService.ResetSession();
            // Reset the error reporter's per-session dedupe/caps alongside the LLM session. Statics
            // leak across exit-to-menu + load, so clearing here keeps each loaded game's reporting fresh.
            DiaryErrorReporter.ResetSession();
            DeathContextCache.Clear();
            ArrivalContextCache.Clear();
            ResetBiotechGrowthTransientState();
            ResetBiotechFamilyTransientState();
            BiotechGeneMutationCorrelation.Clear();
            BiotechPsychicBondCorrelation.Clear();
            BiotechDeathrestCorrelation.Clear();
            ResetAnomalyTransientState();
            BeliefHistoryCorrelationCache.Reset();
            BeliefMutationCache.Reset();
            DlcContext.ResetBeliefProjectionCaches();
            // TicksGame can repeat across different games, so drop the per-tick free-colonist snapshot
            // here (every Game construction) rather than risk reusing the previous game's list.
            ResetFreeColonistSnapshot();
        }

        internal static DiaryGameComponent Instance
        {
            get
            {
                return Verse.Current.Game?.GetComponent<DiaryGameComponent>();
            }
        }

        [Obsolete("Use Instance. Current is kept only as a binary-compatibility alias.", true)]
        internal static DiaryGameComponent Current
        {
            get { return Instance; }
        }

        public override void StartedNewGame()
        {
            pendingInteractionBatches.Clear();
            pendingAmbientInteractionNotes.Clear();
            pendingTaleBatches.Clear();
            writtenAmbientInteractionNotes.Clear();
            pendingAmbientThoughtNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            delayedRaidGenerationReadyTicks.Clear();
            recentEventWindowEvents.Clear();
            recentEvents.Clear();
            ResetExternalApiBudgetState();
            activeEventWindows.Clear();
            activeObservedConditions.Clear();
            recentObservedConditionEvents.Clear();
            nextObservedConditionPollTick.Clear();
            knownAcceptedQuestIds.Clear();
            orphanCandidatesLastScan.Clear();
            generatedSpeechPlayLogTexts.Clear();
            commandStatusByPawnId.Clear();
            archive.Clear();
            ResetBiotechGrowthForNewGame();
            ResetBiotechFamilyForNewGame();
            pollutionObservationVersion = CurrentPollutionObservationVersion;
            ResetOdysseyForNewGame();
            ResetAnomalyForNewGame();
            ResetMemoryForNewGame();
            // Do NOT BeginSession here: the constructor already started this Game's session, and the
            // starting-colonist thoughts (GiveAllStartingPlayerPawnsThought) were queued in it during
            // InitNewGame. Restarting the session now would cancel those in-flight requests and leave
            // their diary entries stuck on "Generating" with no way to re-queue them this session.
            nextGenerationScanTick = 0;
            generationScanRequested = true;
            nextOrphanRecoveryScanTick = 0;
            nextQuestAcceptanceScanTick = 0;
            nextEventWindowTimeoutScanTick = 0;
            nextObservedConditionScanTick = 0;
            nextAmbientSleepFlushScanTick = 0;
            nextWorkScanTick = 0;
            nextHediffProgressionScanTick = 0;
            nextProgressionScanTick = 0;
            nextBeliefScanTick = 0;
            beliefScanCursor = 0;
            nextRoyaltyPersonaReconciliationTick = 0;
            baselineQuestAcceptancesOnNextScan = false;
            initialArrivalScanPending = true;
            // Day-summary state is transient; clear it and let the first tick re-snapshot opinions.
            ResetDaySummaryState();
            ResetThoughtProgressionState(false);
            ResetHediffProgressionState(true);
            MaybeShowErrorReportingNotice();
        }

        public override void LoadedGame()
        {
            pendingInteractionBatches.Clear();
            pendingAmbientInteractionNotes.Clear();
            pendingTaleBatches.Clear();
            writtenAmbientInteractionNotes.Clear();
            pendingAmbientThoughtNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            delayedRaidGenerationReadyTicks.Clear();
            recentEventWindowEvents.Clear();
            recentEvents.Clear();
            ResetExternalApiBudgetState();
            // Transient observed-condition bookkeeping is rebuilt from scratch; the SAVED
            // activeObservedConditions list is deliberately NOT cleared here so loaded states survive.
            recentObservedConditionEvents.Clear();
            nextObservedConditionPollTick.Clear();
            knownAcceptedQuestIds.Clear();
            orphanCandidatesLastScan.Clear();
            ResetBiotechGrowthTransientState();
            ResetBiotechFamilyTransientState();
            nextBiotechBirthNamingPollTick = 0;
            BootstrapBiotechFamilyArcsForLoadedSave();
            BaselineBiotechPsychicBondsOnLoad();
            // Pollution uses the ordinary activeObservedConditions rows. Old saves migrate current tile
            // state once; later reloads leave saved debounce/decay progress untouched.
            BaselineMapPollutionConditionsOnLoad();
            InvalidateBiotechGeneObservationsWithoutDlc();
            NormalizeBeliefStatesForLoadedSave();
            // Do this synchronously at load, not only on the periodic scanner: a paused game may be
            // saved again before any tick runs, and must not persist stale "available" DLC state.
            MarkRoyaltyObservationUnavailable();
            BootstrapOdysseyForLoadedSave();
            BootstrapAnomalyForLoadedSave();
            // Do NOT BeginSession here: the constructor already started this Game's session and
            // cancelled any requests left over from a previous Game. Loaded events have had their
            // "pending" status normalized back to "not generated" (DiaryGenerationStatus, via
            // DiarySaveNormalization on the slot), so the scan below re-queues them in this session.
            nextGenerationScanTick = 0;
            generationScanRequested = true;
            nextOrphanRecoveryScanTick = 0;
            nextQuestAcceptanceScanTick = 0;
            nextEventWindowTimeoutScanTick = 0;
            nextObservedConditionScanTick = 0;
            nextAmbientSleepFlushScanTick = 0;
            nextWorkScanTick = 0;
            nextHediffProgressionScanTick = 0;
            nextProgressionScanTick = 0;
            nextBeliefScanTick = 0;
            beliefScanCursor = 0;
            nextRoyaltyPersonaReconciliationTick = 0;
            baselineQuestAcceptancesOnNextScan = !BaselineAcceptedQuests();
            // Loaded saves normally have their arrival pages already, so the founding-arrival
            // bootstrap stays off. But when a free colonist is missing one — a save from a session
            // where the founding scan was wedged by a broken BackstoryDef patch (pre-2026-07-08), the
            // mod added to an existing colony, or a join that happened while recording was off — the
            // bootstrap is re-armed so the first tick/signal writes the missing pages. Safe on healthy
            // saves: the scan submits per colonist and the ArrivalSignal capture drops every pawn that
            // already has a page, and late pages are forced to the front of the pawn's diary anyway.
            initialArrivalScanPending = AnyFreeColonistMissingArrivalPage();
            // Day-summary state is transient; clear it and let the first tick re-snapshot opinions.
            ResetDaySummaryState();
            RebuildWrittenDayReflectionsFromEvents();
            ResetThoughtProgressionState(true);
            ResetHediffProgressionState(true);
            RunRequestedGenerationScan();
            QueueMissingTitles();
            MaybeShowErrorReportingNotice();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Pre-save flushing/pruning is our own bookkeeping; never let it abort the actual
                // save (the Scribe.Look calls below) — a partial flush is far better than a lost save.
                try
                {
                    // This must remain before events.ExposeEvents below: reconciliation and any
                    // unmatched Thought release are synchronous and belong in this same save.
                    FlushRoyalTitleThoughtsBeforeSave();
                    FlushRoyalPermitRaidsBeforeSave();
                    BiotechPsychicBondCorrelation.FlushPending();
                    BiotechDeathrestCorrelation.FlushPending();
                    FlushAllInteractionBatches();
                    FlushAllTaleBatches();
                    FlushAllAmbientThoughtNotes();
                    ApplyDiaryEventLimits();
                    ApplyMemoryEviction();
                    PruneDiaryEventRefs();
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[Pawn Diary] Pre-save diary flush failed: " + e,
                        "DiaryGameComponent.ExposeData.Save".GetHashCode());
                }
            }

            Scribe_Collections.Look(ref diaries, "diaries", LookMode.Deep);
            events.ExposeEvents("diaryEvents");
            archive.ExposeArchive("diaryArchiveEntries");
            ExposeMemoryData();
            Scribe_Collections.Look(ref activeEventWindows, "activeEventWindows", LookMode.Deep);
            // Plan 12: saved observed-condition runtime state. Additive key; old saves load an empty
            // list. See DiaryGameComponent.ObservedConditions.cs.
            Scribe_Collections.Look(ref activeObservedConditions, "activeObservedConditions", LookMode.Deep);
            Scribe_Collections.Look(ref observedConditionCooldownUntilTick,
                "observedConditionCooldownUntilTick", LookMode.Value, LookMode.Value,
                ref observedConditionCooldownKeys, ref observedConditionCooldownValues);
            Scribe_Values.Look(ref pollutionObservationVersion,
                "pollutionObservationVersion", 0);
            ExposeBiotechGrowthData();
            ExposeBiotechFamilyData();
            ExposeOdysseyData();
            ExposeRoyaltyData();
            ExposeAnomalyData();

            // Before writing generated-speech PlayLog state, drop rows RimWorld's PlayLog has already
            // pruned so stale LogIDs cannot accumulate or block future injection.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                try
                {
                    PruneStaleGeneratedSpeechPlayLogState();
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[Pawn Diary] Pre-save speech-log prune failed: " + e,
                        "DiaryGameComponent.ExposeData.SpeechPrune".GetHashCode());
                }
            }

            Scribe_Collections.Look(ref generatedSpeechPlayLogTexts, "generatedSpeechPlayLogTexts", LookMode.Value, LookMode.Value,
                ref generatedSpeechPlayLogTextKeys, ref generatedSpeechPlayLogTextValues);
            ExposeDevPanelStateForDev();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (diaries == null)
                {
                    diaries = new List<PawnDiaryRecord>();
                }

                if (generatedSpeechPlayLogTexts == null)
                {
                    generatedSpeechPlayLogTexts = new Dictionary<int, string>();
                }

                if (activeEventWindows == null)
                {
                    activeEventWindows = new List<ActiveEventWindowState>();
                }

                if (activeObservedConditions == null)
                {
                    activeObservedConditions = new List<ActiveObservedConditionState>();
                }

                if (observedConditionCooldownUntilTick == null)
                {
                    observedConditionCooldownUntilTick = new Dictionary<string, int>();
                }

                // Post-load rebuilds derive transient indexes from loaded data; a throw here must not
                // abort the whole game load, so degrade to whatever loaded and log once. The null
                // guards above stay outside the try because the rest of the session depends on them.
                try
                {
                    // The pawnId->record index is not serialized; rebuild it from the loaded diaries
                    // first so the per-pawn lookups below resolve in O(1).
                    RebuildDiaryIndex();
                    // The lookup index is not serialized; rebuild it from the loaded events so FindEvent
                    // works immediately (the first generation scan and any UI draw run before any new
                    // event is recorded this session).
                    events.RebuildIndex();
                    PostLoadInitMemory();
                    NormalizeActiveEventWindows();
                    NormalizeActiveObservedConditions();
                    NormalizeObservedConditionCooldowns();
                    ApplyDiaryEventLimits();
                    RebuildWrittenDayReflectionsFromEvents();
                    PruneDiaryEventRefs();
                    PruneStaleGeneratedSpeechPlayLogState();
                    RebuildCommandStatusCache();
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[Pawn Diary] Post-load diary rebuild failed: " + e,
                        "DiaryGameComponent.ExposeData.PostLoad".GetHashCode());
                }
            }
        }

        /// <summary>
        /// Reconciles live title changes, then releases memories whose short ownership window overlaps
        /// a save. Kept as a narrow component seam so loaded tests can exercise the exact pre-save
        /// behavior without invoking unrelated developer-save transient queues.
        /// </summary>
        internal void FlushRoyalTitleThoughtsBeforeSave()
        {
            ReconcileRoyaltyOwnersBeforeSave();
            RoyalTitleThoughtCorrelation.FlushPending();
        }

        /// <summary>Returns transient unclaimed quick-aid raids to normal capture before saving.</summary>
        internal void FlushRoyalPermitRaidsBeforeSave()
        {
            PawnDiary.Ingestion.QuickMilitaryAidRaidCorrelation.FlushAll();
        }

        /// <summary>Clears plain static correlation state after every new-game or load boundary.</summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            try
            {
                RoyaltyTransientState.Reset();
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Royalty transient-state reset failed: " + exception,
                    "PawnDiary.Royalty.Reset".GetHashCode());
            }
            try
            {
                AnomalyTransientState.Reset();
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Anomaly transient-state reset failed: " + exception,
                    "PawnDiary.Anomaly.Reset".GetHashCode());
            }
            try
            {
                BeliefHistoryCorrelationCache.Reset();
                BeliefMutationCache.Reset();
                DlcContext.ResetBeliefProjectionCaches();
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Belief transient-state reset failed: " + exception,
                    "PawnDiary.Belief.Reset".GetHashCode());
            }
        }

        public override void GameComponentTick()
        {
            // This runs every tick. An exception escaping here would surface inside RimWorld's tick
            // loop, so wrap the whole body: a failed tick skips that tick's diary bookkeeping and is
            // logged once, while the game keeps ticking normally.
            try
            {
                GameComponentTickInner();
                MaybeRefreshErrorRedactionNames();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] GameComponentTick failed and was skipped: " + e,
                    "DiaryGameComponent.GameComponentTick".GetHashCode());
            }
        }

        public override void GameComponentUpdate()
        {
            // RimWorld stops GameComponentTick while paused, but our LLM requests finish on
            // background .NET workers. Keep draining completed work on the real-time update hook so
            // already queued pages, recipient follow-ups, and title follow-ups can settle while the
            // player is paused.
            if (!GamePlaying)
            {
                return;
            }

            try
            {
                DrainCompletedLlmWork();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] GameComponentUpdate LLM drain failed and was skipped: " + e,
                    "DiaryGameComponent.GameComponentUpdate".GetHashCode());
            }
        }

        // ── Error-reporter support (see DiaryErrorReporter) ──────────────────────────────────────────
        // Coarse cadence for republishing colony/colonist names to the error scrubber. ~1 in-game hour;
        // names change slowly (recruits, renames), so this need not be frequent.
        private const int ErrorRedactionNamesRefreshInterval = 2500;
        // Safety cap so a very large colony cannot build an unbounded redaction list each refresh.
        private const int ErrorRedactionNamesMax = 200;
        // Instance field (one component per Game) so each loaded game starts refreshing from its own tick.
        private int nextErrorRedactionNamesRefreshTick;

        /// <summary>
        /// Periodically publishes the colony name and colonist names to <see cref="DiaryErrorReporter"/>
        /// so the scrubber can redact any that surface in an outgoing error message. Runs on the main
        /// thread (reads live pawn/faction state); the reporter consumes the published copy off-thread.
        /// </summary>
        private void MaybeRefreshErrorRedactionNames()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now < nextErrorRedactionNamesRefreshTick)
            {
                return;
            }

            nextErrorRedactionNamesRefreshTick = now + ErrorRedactionNamesRefreshInterval;

            List<string> names = new List<string>();
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && !string.IsNullOrWhiteSpace(player.Name))
            {
                names.Add(player.Name);
            }

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (names.Count >= ErrorRedactionNamesMax)
                {
                    break;
                }

                if (pawn == null)
                {
                    continue;
                }

                if (pawn.Name != null)
                {
                    names.Add(pawn.Name.ToStringFull);
                }

                string shortName = pawn.LabelShort;
                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    names.Add(shortName);
                }
            }

            DiaryErrorReporter.UpdateRedactionNames(names);
        }

        /// <summary>
        /// Shows a one-time informational notice that opt-out error reporting is on by default, with a
        /// button to turn it off. Persisted flag makes it appear once per install; the dialog is deferred
        /// until the load long-event finishes so the window stack is ready.
        /// </summary>
        private void MaybeShowErrorReportingNotice()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null || settings.errorReportingNoticeShown)
            {
                return;
            }

            // Mark shown + persist up front so the notice never reappears even if the dialog is dismissed
            // without pressing a button, or fails to open at all.
            settings.errorReportingNoticeShown = true;
            settings.Write();

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Dialog_MessageBox dialog = new Dialog_MessageBox(
                        "PawnDiary.ErrorReporting.NoticeText".Translate(),
                        "PawnDiary.ErrorReporting.NoticeKeepOn".Translate(), null,
                        "PawnDiary.ErrorReporting.NoticeTurnOff".Translate(),
                        () =>
                        {
                            settings.enableErrorReporting = false;
                            settings.Write();
                        },
                        "PawnDiary.ErrorReporting.NoticeTitle".Translate());
                    Find.WindowStack.Add(dialog);
                }
                catch (Exception e)
                {
                    Log.Warning("[Pawn Diary] Failed to show error-reporting notice: " + e);
                }
            });
        }

        private void GameComponentTickInner()
        {
            int now = Find.TickManager.TicksGame;
            if (ModsConfig.RoyaltyActive
                && PawnDiary.Ingestion.QuickMilitaryAidRaidCorrelation.HasState)
                PawnDiary.Ingestion.QuickMilitaryAidRaidCorrelation.FlushExpired(
                    now, DiaryRoyaltyPolicy.Snapshot());
            if (initialArrivalScanPending && TryRecordStartingColonistArrivals())
            {
                initialArrivalScanPending = false;
            }

            if (initialArrivalScanPending)
            {
                DrainCompletedLlmWork();
                return;
            }

            FlushReadyInteractionBatches();
            FlushReadyTaleBatches();
            FlushReadyAmbientThoughtNotes();
            TickPendingBiotechBirths(now);

            // Re-baseline each colonist's opinions at the start of every new day, so the reflection
            // can measure how feelings shifted over the day. Cheap: a no-op comparison most ticks.
            if (CurrentDayIndex != opinionSnapshotDay)
            {
                SnapshotDayStartOpinions();
            }

            if (now >= nextAmbientSleepFlushScanTick)
            {
                nextAmbientSleepFlushScanTick = now + AmbientSleepFlushScanIntervalTicks;
                FlushAmbientNotesForSleepingPawns();
            }

            if (!initialArrivalScanPending && now >= nextWorkScanTick)
            {
                nextWorkScanTick = now + Math.Max(250, DiarySignalPolicies.WorkScanIntervalTicks);
                ScanPawnWorkForDiaryEvents();
            }

            if (!initialArrivalScanPending && now >= nextQuestAcceptanceScanTick)
            {
                nextQuestAcceptanceScanTick = now + QuestAcceptanceScanIntervalTicks;
                ScanAcceptedQuestsForDiaryEvents();
            }

            if (now >= nextEventWindowTimeoutScanTick)
            {
                nextEventWindowTimeoutScanTick = now + EventWindowTimeoutScanIntervalTicks;
                ScanEventWindowTimeouts();
            }

            if (!initialArrivalScanPending && now >= nextObservedConditionScanTick)
            {
                nextObservedConditionScanTick = now + ObservedConditionScanIntervalTicks;
                ScanObservedConditions();
            }

            if (!initialArrivalScanPending && now >= nextThoughtProgressionScanTick)
            {
                nextThoughtProgressionScanTick = now + Math.Max(250, DiarySignalPolicies.ThoughtProgressionScanIntervalTicks);
                ScanThoughtProgressionsForDiaryEvents(baselineThoughtProgressionsOnNextScan);
                baselineThoughtProgressionsOnNextScan = false;
            }

            if (!initialArrivalScanPending && now >= nextHediffProgressionScanTick)
            {
                nextHediffProgressionScanTick = now + Math.Max(250, DiaryTuning.Current.hediffProgressionScanIntervalTicks);
                ScanHediffProgressionsForDiaryEvents(baselineHediffProgressionsOnNextScan);
                baselineHediffProgressionsOnNextScan = false;
            }

            if (!initialArrivalScanPending && now >= nextProgressionScanTick)
            {
                nextProgressionScanTick = now + Math.Max(250, DiarySignalPolicies.ProgressionScanIntervalTicks);
                ScanPawnProgressionsForDiaryEvents();
            }

            if (!initialArrivalScanPending && ModsConfig.IdeologyActive && now >= nextBeliefScanTick)
            {
                BeliefPolicySnapshot beliefPolicy = DiaryBeliefPolicy.Snapshot();
                nextBeliefScanTick = now + beliefPolicy.beliefScanIntervalTicks;
                ScanPawnBeliefs(now);
            }

            RunRoyaltyPersonaReconciliationIfDue(now);

            MaybeRunMemoryEvictionScan(now);

            if (now >= nextOrphanRecoveryScanTick)
            {
                nextOrphanRecoveryScanTick = now + OrphanRecoveryScanIntervalTicks;
                RecoverOrphanedPendingGenerations();
            }

            if (generationScanRequested && now >= nextGenerationScanTick)
            {
                nextGenerationScanTick = now + GenerationScanIntervalTicks;
                RunRequestedGenerationScan();
            }

            DrainCompletedLlmWork();
        }

        /// <summary>
        /// Runs at most one Royalty live-state reconciliation when its elapsed-time deadline is due.
        /// </summary>
        private void RunRoyaltyPersonaReconciliationIfDue(int now)
        {
            if (initialArrivalScanPending || !ModsConfig.RoyaltyActive
                || !RoyaltyReconciliationSchedule.IsDue(
                    now, nextRoyaltyPersonaReconciliationTick)) return;

            // A time skip can pass many nominal cadence boundaries. Reconciliation reads only the
            // current live state, so replaying every missed interval could invent transitions from
            // repeated observations of the same moment. One pass is the bounded catch-up; rebasing
            // from 'now' prevents a hot loop while the saved pending timestamp still preserves real
            // elapsed separation time.
            nextRoyaltyPersonaReconciliationTick = RoyaltyReconciliationSchedule.NextDeadline(
                now, DiaryRoyaltyPolicy.Snapshot().reconciliationCadenceTicks);
            ReconcileRoyaltyPersonaBonds();
        }

        private void RequestGenerationScan()
        {
            generationScanRequested = true;
        }

        private void RunRequestedGenerationScan()
        {
            if (!generationScanRequested)
            {
                return;
            }

            generationScanRequested = false;
            QueueAllPendingGenerations();
        }

        private void DrainCompletedLlmWork()
        {
            LlmGenerationResult result;
            while (LlmClient.TryDequeueCompleted(out result))
            {
                ApplyLlmResult(result);
            }

            // Background send workers can't call Log.Message safely (it's main-thread only), so they
            // queue debug lines; flush them here, on the main thread. See LlmClient.LogDebug.
            LlmClient.FlushPendingLogs();
        }

        /// <summary>
        /// Writes ambient day notes at a natural diary moment: when the pawn has settled down to
        /// sleep or rest. Thin notes stay pending so one stray chat does not become a forced entry.
        /// </summary>
        private void FlushAmbientNotesForSleepingPawns()
        {
            bool daySummary = DiaryTuning.Current.daySummaryEnabled;
            bool arcReflection = DiaryTuning.Current.arcReflectionEnabled;

            // When both reflection paths are off we only have work if filler notes are pending. When a
            // reflection is on, it can be due with no pending filler, so scan resting pawns idempotently.
            if (!daySummary
                && !arcReflection
                && pendingAmbientInteractionNotes.Count == 0
                && pendingAmbientThoughtNotes.Count == 0)
            {
                return;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (!IsRestingForAmbientFlush(pawn))
                {
                    continue;
                }

                if (daySummary)
                {
                    // The reflection folds the filler in and writes one richer end-of-day entry.
                    FlushDaySummaryForPawn(pawn);
                }
                else
                {
                    if (arcReflection && TryFlushArcReflectionForPawn(
                        pawn, pawn.GetUniqueLoadID(), CurrentDayIndex, majorEventTrigger: false))
                    {
                        continue;
                    }

                    FlushAmbientInteractionNotesForPawn(pawn);
                    FlushAmbientThoughtNotesForPawn(pawn);
                }
            }
        }

        /// <summary>
        /// RimWorld uses LayDown for normal sleep and LayDownResting for medical/bed rest. LayDownAwake
        /// is deliberately ignored because it is often ritual/idling behavior rather than "end my day".
        /// </summary>
        private static bool IsRestingForAmbientFlush(Pawn pawn)
        {
            string jobDefName = pawn?.CurJobDef?.defName;
            return string.Equals(jobDefName, "LayDown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(jobDefName, "LayDownResting", StringComparison.OrdinalIgnoreCase);
        }
    }
}
