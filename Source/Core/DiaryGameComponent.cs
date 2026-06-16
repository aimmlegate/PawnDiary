// The orchestrator. Owns the saved diary data and drives the whole flow: record an event
// (RecordInteraction / RecordMentalState / RecordTale / RecordMoodEvent) -> build a DiaryEvent with context -> queue an LLM
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
//   DiaryGameComponent.CraftedAndRelics.cs — masterwork/legendary crafts + relic installs
//   DiaryGameComponent.MoodEvents.cs     — mood-affecting game conditions (RegisterCondition)
//   DiaryGameComponent.Thoughts.cs       — temporary memory thoughts (TryGainMemory)
//   DiaryGameComponent.Arrivals.cs       — the neutral "how this pawn joined" first entry
//   DiaryGameComponent.InteractionBatching.cs — XML-configured batching for quick social logs
//   DiaryGameComponent.AmbientThoughts.cs — day-note batching for low-impact temporary thoughts
//   ──
//   DiaryGameComponent.EventFactory.cs   — AddPairwiseEvent/AddSoloEvent: build + register DiaryEvents
//   DiaryGameComponent.Generation.cs     — prompt building, API lane selection, LLM dispatch/apply
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
        // Synthetic Tale-domain groups for notable events vanilla does not expose as TaleDefs.
        private const string TaleQualityGroupKey = "talequality";
        private const string TaleRelicGroupKey = "talerelic";
        // Synthetic event used for the neutral first entry that explains how a pawn joined.
        private const string ArrivalDefName = "PawnDiary_Arrival";

        // Per-pawn saved state (event references, persona, enabled flag). Persisted via ExposeData.
        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        // All diary events across every pawn. Persisted via ExposeData.
        private List<DiaryEvent> diaryEvents = new List<DiaryEvent>();
        // Interaction batches still accumulating lines; keyed by group/pair/optional def. Not saved
        // because ExposeData flushes first.
        private readonly Dictionary<string, PendingInteractionBatch> pendingInteractionBatches = new Dictionary<string, PendingInteractionBatch>();
        // Ambient interaction notes still accumulating low-stakes social texture; keyed by
        // group+pawn+day. Also transient and flushed before saving.
        private readonly Dictionary<string, PendingAmbientInteractionNote> pendingAmbientInteractionNotes = new Dictionary<string, PendingAmbientInteractionNote>();
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
        // Transient (not saved): event-role keys ("eventId|role") seen pending-but-not-in-flight on the
        // previous generation scan. An entry must look orphaned on two consecutive scans before the
        // orphan recovery re-queues it, so a request that merely finished between scans (its result
        // still queued for the main thread) is never mistaken for an orphan. See
        // RecoverOrphanedPendingGenerations.
        private HashSet<string> orphanCandidatesLastScan = new HashSet<string>();
        // New games build maps after StartedNewGame. This flag lets the first tick with maps create
        // founding-colonist arrival entries using scenario context.
        private bool initialArrivalScanPending;

        // How often (in ticks) GameComponentTick rescans saved events to (re)queue any pending
        // generations, and the next tick that scan is allowed to run.
        private const int GenerationScanIntervalTicks = 120;
        private int nextGenerationScanTick;

        // Ambient notes read like end-of-day memories, so when a pawn goes to sleep or rests we try
        // to write any sufficiently full low-stakes note for that pawn. This is only a periodic scan
        // rather than a Harmony patch because the pending note dictionaries already decide what is
        // worth writing.
        private const int AmbientSleepFlushScanIntervalTicks = 250;
        private int nextAmbientSleepFlushScanTick;

        // Current absolute in-game day. Uses TicksAbs so day-note batching follows the world calendar.
        private static int CurrentDayIndex
        {
            get
            {
                return Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            }
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
            writtenAmbientInteractionNotes.Clear();
            pendingAmbientThoughtNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            recentMentalEvents.Clear();
            recentTaleEvents.Clear();
            recentMoodEvents.Clear();
            recentThoughtEvents.Clear();
            orphanCandidatesLastScan.Clear();
            // Do NOT BeginSession here: the constructor already started this Game's session, and the
            // starting-colonist thoughts (GiveAllStartingPlayerPawnsThought) were queued in it during
            // InitNewGame. Restarting the session now would cancel those in-flight requests and leave
            // their diary entries stuck on "Generating" with no way to re-queue them this session.
            nextGenerationScanTick = 0;
            nextAmbientSleepFlushScanTick = 0;
            initialArrivalScanPending = true;
            // Day-summary state is transient; clear it and let the first tick re-snapshot opinions.
            ResetDaySummaryState();
        }

        public override void LoadedGame()
        {
            pendingInteractionBatches.Clear();
            pendingAmbientInteractionNotes.Clear();
            writtenAmbientInteractionNotes.Clear();
            pendingAmbientThoughtNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            recentMentalEvents.Clear();
            recentTaleEvents.Clear();
            recentMoodEvents.Clear();
            recentThoughtEvents.Clear();
            orphanCandidatesLastScan.Clear();
            // Do NOT BeginSession here: the constructor already started this Game's session and
            // cancelled any requests left over from a previous Game. Loaded events have had their
            // "pending" status normalized back to "not generated" (DiaryEvent.NormalizeLoadedStatus),
            // so the scan below re-queues them in the current session.
            nextGenerationScanTick = 0;
            nextAmbientSleepFlushScanTick = 0;
            initialArrivalScanPending = false;
            // Day-summary state is transient; clear it and let the first tick re-snapshot opinions.
            ResetDaySummaryState();
            QueueAllPendingGenerations();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                FlushAllInteractionBatches();
                FlushAllAmbientThoughtNotes();
            }

            Scribe_Collections.Look(ref diaries, "diaries", LookMode.Deep);
            Scribe_Collections.Look(ref diaryEvents, "diaryEvents", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (diaries == null)
                {
                    diaries = new List<PawnDiaryRecord>();
                }

                if (diaryEvents == null)
                {
                    diaryEvents = new List<DiaryEvent>();
                }
            }
        }

        public override void GameComponentTick()
        {
            FlushReadyInteractionBatches();
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

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
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
