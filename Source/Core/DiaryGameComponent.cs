// The orchestrator. Owns the saved diary data and drives the whole flow: record an event
// (RecordInteraction / RecordMentalState / RecordTale / RecordMoodEvent / RecordWork)
// -> build a DiaryEvent with context -> queue an LLM
// request -> apply the result each tick -> hand views to the UI. Context/prompt building live in
// DiaryContextBuilder / DiaryPromptBuilder; the saved data models in DiaryEvent / PawnDiaryRecord.
// This is a RimWorld GameComponent (lifecycle hooks: GameComponentTick, ExposeData, etc.).
// New to C#/RimWorld? See AGENTS.md.
//
// ── This class is large, so it is split across several files using C# `partial class` ──
// A `partial` class is one class whose members are spread over multiple files; the compiler
// stitches them back together, so every file shares the same fields and private methods exactly
// as if they were one file. The split is purely organizational (no behavior change). Map:
//   DiaryGameComponent.cs                — this file: state, lifecycle hooks (tick/save/load)
//   DiaryGameComponent.PublicApi.cs      — read/write entry points the UI calls
//   ── one file per event we listen for (Record* hook + that event's text/context helpers) ──
//   DiaryGameComponent.Interactions.cs   — social interactions (PlayLog.Add)
//   DiaryGameComponent.MentalStates.cs   — social fights + mental breaks (TryStartMentalState)
//   DiaryGameComponent.Tales.cs          — notable-history tales (TaleRecorder.RecordTale)
//   DiaryGameComponent.MoodEvents.cs     — mood-affecting game conditions (RegisterCondition)
//   DiaryGameComponent.Thoughts.cs       — temporary memory thoughts (TryGainMemory)
//   DiaryGameComponent.Inspirations.cs   — pawn inspirations (TryStartInspiration)
//   DiaryGameComponent.ThoughtProgression.cs — staged situational need thoughts (hunger, rest, etc.)
//   DiaryGameComponent.Hediffs.cs        — XML-driven health-condition signals (AddHediff + severity scan)
//   DiaryGameComponent.Work.cs           — occasional solo notes about current pawn work
//   DiaryGameComponent.Arrivals.cs       — the neutral "how this pawn joined" first entry
//   DiaryGameComponent.InteractionBatching.cs — XML-configured batching for quick social logs
//   DiaryGameComponent.TaleBatching.cs   — delayed solo batches for bursty Tale events
//   DiaryGameComponent.AmbientThoughts.cs — day-note batching for low-impact temporary thoughts
//   ──
//   DiaryGameComponent.EventFactory.cs   — AddPairwiseEvent/AddSoloEvent: build + register DiaryEvents
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
    /// finished results each tick, persists everything on save/load, and serves entry views to
    /// the UI. Reach the live instance via <see cref="Current"/>.
    /// </summary>
    public partial class DiaryGameComponent : GameComponent
    {
        // Dedup windows live in DiaryTuningDef (editable XML); see DiaryTuning.Current.
        // The transient dedup dictionaries keep only recent keys. Once any dictionary crosses this
        // size, the shared gate sweeps entries outside that source's configured dedup window.
        private const int RecentEventPruneThreshold = 512;
        // Synthetic event used for the neutral first entry that explains how a pawn joined.
        private const string ArrivalGroupKey = "arrival";
        private const string ArrivalDefName = "PawnDiary_Arrival";

        // Per-pawn saved state (event references, persona, enabled flag). Persisted via ExposeData.
        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        // The saved event store: every DiaryEvent across all pawns plus the O(1) id->event lookup
        // index that mirrors it. Owns FindEvent/Register/RebuildIndex and the "diaryEvents" Scribe
        // key. Extracted out of this class so the event store has one clear owner (Run Card 10); this
        // class remains the only RimWorld lifecycle/save owner and drives the repository from
        // ExposeData/GameComponentTick. See DiaryEventRepository.cs.
        private readonly DiaryEventRepository events = new DiaryEventRepository();
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

        // Transient (not saved) guard against duplicate mental-state events, e.g. the two
        // mirrored SocialFighting starts, or a break that re-triggers quickly.
        private readonly Dictionary<string, int> recentMentalEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against TaleRecorder firing the same notable event repeatedly.
        private readonly Dictionary<string, int> recentTaleEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against the same mood event firing for the same
        // GameCondition on multiple maps within a short window.
        private readonly Dictionary<string, int> recentMoodEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against the same pawn+thought being recorded repeatedly.
        private readonly Dictionary<string, int> recentThoughtEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against repeated hediff appearance/progression signals.
        private readonly Dictionary<string, int> recentHediffEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against the mirrored AddDirectRelation call from the other
        // participant when a romance relation is added symmetrically. Keys by canonical pair id +
        // relation defName.
        private readonly Dictionary<string, int> recentRomanceEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against a raid incident double-firing or re-firing within the
        // dedup window (e.g. mirrored multi-map transitions). Keys by incident/map/faction/points.
        private readonly Dictionary<string, int> recentRaidEvents = new Dictionary<string, int>();
        // Transient (not saved) generation delay for ordinary raids. The event is recorded as soon as
        // RimWorld spawns the threat, but the LLM waits a short XML-tuned window so walk-in raids read
        // more like anticipation/contact than instant combat aftermath.
        private readonly Dictionary<string, int> delayedRaidGenerationReadyTicks = new Dictionary<string, int>();
        // Transient (not saved) guard against a quest lifecycle signal double-firing (e.g. a
        // multi-map transition or a fluke double-call). Keys by quest id + signal.
        private readonly Dictionary<string, int> recentQuestEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against a ritual outcome double-firing. Keys by ritual,
        // organizer, target, and finish tick.
        private readonly Dictionary<string, int> recentRitualEvents = new Dictionary<string, int>();
        // Transient (not saved) guard against an ability activation double-firing. Keys by caster,
        // ability, target, and activation tick.
        private readonly Dictionary<string, int> recentAbilityEvents = new Dictionary<string, int>();
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

        // How often (in ticks) GameComponentTick rescans saved events to (re)queue any pending
        // generations, and the next tick that scan is allowed to run.
        private const int GenerationScanIntervalTicks = 120;
        private int nextGenerationScanTick;

        // Quest acceptance has a direct Harmony hook, but a light state scan covers UI or modded
        // accept paths that do not pass through the expected method.
        private const int QuestAcceptanceScanIntervalTicks = 120;
        private int nextQuestAcceptanceScanTick;

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
            DeathContextCache.Clear();
            ArrivalContextCache.Clear();
        }

        public static DiaryGameComponent Current
        {
            get
            {
                return Verse.Current.Game?.GetComponent<DiaryGameComponent>();
            }
        }

        public override void StartedNewGame()
        {
            pendingInteractionBatches.Clear();
            pendingAmbientInteractionNotes.Clear();
            pendingTaleBatches.Clear();
            writtenAmbientInteractionNotes.Clear();
            pendingAmbientThoughtNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            recentMentalEvents.Clear();
            recentTaleEvents.Clear();
            recentMoodEvents.Clear();
            recentThoughtEvents.Clear();
            recentHediffEvents.Clear();
            recentRomanceEvents.Clear();
            recentRaidEvents.Clear();
            delayedRaidGenerationReadyTicks.Clear();
            recentQuestEvents.Clear();
            knownAcceptedQuestIds.Clear();
            orphanCandidatesLastScan.Clear();
            generatedSpeechPlayLogTexts.Clear();
            // Do NOT BeginSession here: the constructor already started this Game's session, and the
            // starting-colonist thoughts (GiveAllStartingPlayerPawnsThought) were queued in it during
            // InitNewGame. Restarting the session now would cancel those in-flight requests and leave
            // their diary entries stuck on "Generating" with no way to re-queue them this session.
            nextGenerationScanTick = 0;
            nextQuestAcceptanceScanTick = 0;
            nextAmbientSleepFlushScanTick = 0;
            nextWorkScanTick = 0;
            nextHediffProgressionScanTick = 0;
            baselineQuestAcceptancesOnNextScan = false;
            initialArrivalScanPending = true;
            // Day-summary state is transient; clear it and let the first tick re-snapshot opinions.
            ResetDaySummaryState();
            ResetThoughtProgressionState(false);
            ResetHediffProgressionState(true);
        }

        public override void LoadedGame()
        {
            pendingInteractionBatches.Clear();
            pendingAmbientInteractionNotes.Clear();
            pendingTaleBatches.Clear();
            writtenAmbientInteractionNotes.Clear();
            pendingAmbientThoughtNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            recentMentalEvents.Clear();
            recentTaleEvents.Clear();
            recentMoodEvents.Clear();
            recentThoughtEvents.Clear();
            recentHediffEvents.Clear();
            recentRomanceEvents.Clear();
            recentRaidEvents.Clear();
            delayedRaidGenerationReadyTicks.Clear();
            recentQuestEvents.Clear();
            knownAcceptedQuestIds.Clear();
            orphanCandidatesLastScan.Clear();
            // Do NOT BeginSession here: the constructor already started this Game's session and
            // cancelled any requests left over from a previous Game. Loaded events have had their
            // "pending" status normalized back to "not generated" (DiaryEvent.NormalizeLoadedStatus),
            // so the scan below re-queues them in the current session.
            nextGenerationScanTick = 0;
            nextQuestAcceptanceScanTick = 0;
            nextAmbientSleepFlushScanTick = 0;
            nextWorkScanTick = 0;
            nextHediffProgressionScanTick = 0;
            baselineQuestAcceptancesOnNextScan = !BaselineAcceptedQuests();
            initialArrivalScanPending = false;
            // Day-summary state is transient; clear it and let the first tick re-snapshot opinions.
            ResetDaySummaryState();
            RebuildWrittenDayReflectionsFromEvents();
            ResetThoughtProgressionState(true);
            ResetHediffProgressionState(true);
            QueueAllPendingGenerations();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Pre-save flushing/pruning is our own bookkeeping; never let it abort the actual
                // save (the Scribe.Look calls below) — a partial flush is far better than a lost save.
                try
                {
                    FlushAllInteractionBatches();
                    FlushAllTaleBatches();
                    FlushAllAmbientThoughtNotes();
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

                // Post-load rebuilds derive transient indexes from loaded data; a throw here must not
                // abort the whole game load, so degrade to whatever loaded and log once. The null
                // guards above stay outside the try because the rest of the session depends on them.
                try
                {
                    // The lookup index is not serialized; rebuild it from the loaded events so FindEvent
                    // works immediately (the first generation scan and any UI draw run before any new
                    // event is recorded this session).
                    events.RebuildIndex();
                    RebuildWrittenDayReflectionsFromEvents();
                    PruneDiaryEventRefs();
                    PruneStaleGeneratedSpeechPlayLogState();
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[Pawn Diary] Post-load diary rebuild failed: " + e,
                        "DiaryGameComponent.ExposeData.PostLoad".GetHashCode());
                }
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
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] GameComponentTick failed and was skipped: " + e,
                    "DiaryGameComponent.GameComponentTick".GetHashCode());
            }
        }

        private void GameComponentTickInner()
        {
            FlushReadyInteractionBatches();
            FlushReadyTaleBatches();
            FlushReadyAmbientThoughtNotes();

            // Re-baseline each colonist's opinions at the start of every new day, so the reflection
            // can measure how feelings shifted over the day. Cheap: a no-op comparison most ticks.
            if (CurrentDayIndex != opinionSnapshotDay)
            {
                SnapshotDayStartOpinions();
            }

            int now = Find.TickManager.TicksGame;
            if (now >= nextAmbientSleepFlushScanTick)
            {
                nextAmbientSleepFlushScanTick = now + AmbientSleepFlushScanIntervalTicks;
                FlushAmbientNotesForSleepingPawns();
            }

            if (initialArrivalScanPending && TryRecordStartingColonistArrivals())
            {
                initialArrivalScanPending = false;
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

            if (now >= nextGenerationScanTick)
            {
                nextGenerationScanTick = now + GenerationScanIntervalTicks;
                RecoverOrphanedPendingGenerations();
                QueueAllPendingGenerations();
            }

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

            // When the reflection is off we only have work if filler notes are pending. When it is on,
            // a reflection can also be driven by major events / opinion shifts / new afflictions even
            // with no pending filler, so we always scan resting pawns (each is cheap and idempotent).
            if (!daySummary && pendingAmbientInteractionNotes.Count == 0 && pendingAmbientThoughtNotes.Count == 0)
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
