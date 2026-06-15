// The orchestrator. Owns the saved diary data and drives the whole flow: record an event
// (RecordInteraction / RecordMentalState / RecordTale / RecordMoodEvent) -> build a DiaryEvent with context -> queue an LLM
// request -> apply the result each tick -> hand views to the UI. Context/prompt building live in
// DiaryContextBuilder / DiaryPromptBuilder; the saved data models in DiaryEvent / PawnDiaryRecord.
// This is a RimWorld GameComponent (lifecycle hooks: GameComponentTick, ExposeData, etc.).
// New to C#/RimWorld? See AGENTS.md.
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
    public class DiaryGameComponent : GameComponent
    {
        // Dedup windows live in DiaryTuningDef (editable XML); see DiaryTuning.Current.
        // InteractionGroups key that marks an interaction as low-stakes small talk eligible for batching.
        private const string SmallTalkGroupKey = "smalltalk";
        // Synthetic defName/label used when multiple small-talk interactions are merged into one diary event.
        private const string SmallTalkBatchDefName = "SmallTalkBatch";
        // Synthetic Tale-domain groups for notable events vanilla does not expose as TaleDefs.
        private const string TaleQualityGroupKey = "talequality";
        private const string TaleRelicGroupKey = "talerelic";
        // Synthetic event used for the neutral first entry that explains how a pawn joined.
        private const string ArrivalDefName = "PawnDiary_Arrival";

        // Per-pawn saved state (event references, persona, enabled flag). Persisted via ExposeData.
        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        // All diary events across every pawn. Persisted via ExposeData.
        private List<DiaryEvent> diaryEvents = new List<DiaryEvent>();
        // Small-talk batches still accumulating lines; keyed by PairKey. Not saved (flushed first).
        private readonly Dictionary<string, PendingSmallTalkBatch> pendingSmallTalkBatches = new Dictionary<string, PendingSmallTalkBatch>();

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
        // New games build maps after StartedNewGame. This flag lets the first tick with maps create
        // founding-colonist arrival entries using scenario context.
        private bool initialArrivalScanPending;

        // These TaleDefs mirror events we already capture through more specific hooks. Skipping
        // them avoids two diary entries for one social fight or mental break.
        private static readonly HashSet<string> TaleDefsCoveredElsewhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SocialFight",
            "MentalStateBerserk",
            "MentalStateGaveUp"
        };

        // TaleDefs where one involved pawn died. RimWorld uses different first/second pawn roles
        // depending on the tale, so DeathVictimForTale below resolves the victim explicitly.
        private static readonly HashSet<string> DeathTaleDefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "KilledBy",
            "KilledCapacity",
            "KilledLongRange",
            "KilledMajorThreat",
            "KilledMelee",
            "KilledMortar",
            "KilledChild",
            "KilledColonist",
            "KilledColonyAnimal"
        };

        public DiaryGameComponent(Game game)
        {
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
            pendingSmallTalkBatches.Clear();
            recentMentalEvents.Clear();
            recentTaleEvents.Clear();
            recentMoodEvents.Clear();
            recentThoughtEvents.Clear();
            LlmClient.BeginSession();
            nextGenerationScanTick = 0;
            initialArrivalScanPending = true;
        }

        public override void LoadedGame()
        {
            pendingSmallTalkBatches.Clear();
            recentMentalEvents.Clear();
            recentTaleEvents.Clear();
            recentMoodEvents.Clear();
            recentThoughtEvents.Clear();
            LlmClient.BeginSession();
            nextGenerationScanTick = 0;
            initialArrivalScanPending = false;
            QueueAllPendingGenerations();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                FlushAllSmallTalkBatches();
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
            FlushReadySmallTalkBatches();

            if (initialArrivalScanPending && TryRecordStartingColonistArrivals())
            {
                initialArrivalScanPending = false;
            }

            int now = Find.TickManager.TicksGame;
            if (now >= nextGenerationScanTick)
            {
                nextGenerationScanTick = now + GenerationScanIntervalTicks;
                QueueAllPendingGenerations();
            }

            LlmGenerationResult result;
            while (LlmClient.TryDequeueCompleted(out result))
            {
                ApplyLlmResult(result);
            }
        }

        /// <summary>
        /// Returns the diary entries to render for a pawn (newest data first as stored).
        /// Pure read — no side effects. Generation is driven entirely by the background tick scan.
        /// </summary>
        public IReadOnlyList<DiaryEntryView> EntriesFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return EmptyEntries.List;
            }

            string pawnId = pawn.GetUniqueLoadID();

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return EmptyEntries.List;
            }

            List<DiaryEntryView> views = new List<DiaryEntryView>();
            int? finalDeathTick = FinalDeathTickFor(pawnId, diary);

            if (diary.eventIds != null)
            {
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                    if (EventFallsAfterFinalDeath(diaryEvent, finalDeathTick))
                    {
                        continue;
                    }

                    DiaryEntryView view = diaryEvent?.ToViewFor(pawnId);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            if (diary.entries != null)
            {
                for (int i = 0; i < diary.entries.Count; i++)
                {
                    if (finalDeathTick.HasValue && diary.entries[i] != null && diary.entries[i].tick > finalDeathTick.Value)
                    {
                        continue;
                    }

                    DiaryEntryView view = DiaryEntryView.FromLegacy(diary.entries[i]);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            return views;
        }

        /// <summary>
        /// Finds the generated diary entry that belongs to a clicked RimWorld social-log row.
        /// Returns null when the event was not recorded, belongs to another pawn, or has not
        /// produced visible LLM text yet; callers should keep vanilla click behavior in that case.
        /// </summary>
        public DiaryEntryView GeneratedEntryForPlayLogEntry(Pawn pawn, int playLogEntryId)
        {
            if (!IsDiaryEligible(pawn) || playLogEntryId < 0)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null || diary.eventIds == null)
            {
                return null;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null || !diaryEvent.MatchesPlayLogEntry(playLogEntryId))
                {
                    continue;
                }

                DiaryEntryView view = diaryEvent.ToViewFor(pawnId);
                if (view != null && !string.IsNullOrWhiteSpace(view.GeneratedText))
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns whether a pawn has diary generation enabled (defaults to true if no record exists yet).
        /// </summary>
        public bool DiaryGenerationEnabledFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            return FindDiary(pawn, true)?.diaryGenerationEnabled ?? true;
        }

        public void SetDiaryGenerationEnabled(Pawn pawn, bool enabled)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.diaryGenerationEnabled = enabled;
                if (enabled)
                {
                    QueuePendingGenerationsForPawn(pawn.GetUniqueLoadID());
                }
            }
        }

        public DiaryPersonaDef PersonaFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return DiaryPersonas.Default;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            return DiaryPersonas.Resolve(diary?.personaDefName);
        }

        public void SetPersona(Pawn pawn, string personaDefName)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            DiaryPersonaDef persona = DiaryPersonas.Resolve(personaDefName);
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.personaDefName = persona.defName;
            }
        }

        private void EnsureGenerationQueued(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole) && diaryEvent.HasDeathDescription())
            {
                QueueDeathDescription(diaryEvent);
                return;
            }

            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole) && diaryEvent.HasArrivalDescription())
            {
                QueueArrivalDescription(diaryEvent);
                return;
            }

            if (DualPovEnabled && DiaryEvent.RoleIsInitiatorOrRecipient(povRole) && !diaryEvent.solo)
            {
                QueueSequentialPairwiseRewrite(diaryEvent);
                return;
            }

            if (diaryEvent.CanQueueGeneration(povRole))
            {
                QueueLlmRewrite(diaryEvent, povRole);
            }
        }

        private const int GenerationScanIntervalTicks = 120;
        private int nextGenerationScanTick;

        private void QueueAllPendingGenerations()
        {
            if (diaryEvents == null)
            {
                return;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription())
                {
                    if (diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                if (diaryEvent.HasArrivalDescription())
                {
                    if (diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                if (diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole)
                    && !EventFallsAfterFinalDeathForPawn(diaryEvent, diaryEvent.initiatorPawnId))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole);
                }

                if (!diaryEvent.solo
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole)
                    && !EventFallsAfterFinalDeathForPawn(diaryEvent, diaryEvent.recipientPawnId))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole);
                }

            }
        }

        private void QueuePendingGenerationsForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null || diary.eventIds == null)
            {
                return;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null)
                {
                    continue;
                }

                if (EventFallsAfterFinalDeathForPawn(diaryEvent, pawnId))
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription())
                {
                    if (diaryEvent.IsDeathDescriptionFor(pawnId))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                if (diaryEvent.HasArrivalDescription())
                {
                    if (diaryEvent.IsArrivalDescriptionFor(pawnId))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                string povRole = diaryEvent.RoleForPawn(pawnId);
                EnsureGenerationQueued(diaryEvent, povRole);
            }
        }

        /// <summary>
        /// New-game bootstrap: records one neutral arrival entry for each starting colonist once
        /// RimWorld has finished creating maps and free-colonist lists.
        /// </summary>
        private bool TryRecordStartingColonistArrivals()
        {
            if (Find.Maps == null || Find.Maps.Count == 0)
            {
                return false;
            }

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                if (map?.mapPawns?.FreeColonists == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    RecordColonistArrival(colonists[i], BuildStartingArrivalContext());
                }
            }

            return true;
        }

        /// <summary>
        /// Records the first neutral entry for a pawn: how they became part of the colony. This is
        /// public because the Pawn.SetFaction Harmony patch calls it after vanilla confirms the join.
        /// </summary>
        public void RecordColonistArrival(Pawn pawn, string arrivalContext)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (HasArrivalEventFor(pawnId))
            {
                return;
            }

            bool startingPawn = !string.IsNullOrWhiteSpace(arrivalContext)
                && arrivalContext.IndexOf("arrival_source=game_start", StringComparison.OrdinalIgnoreCase) >= 0;
            string label = "PawnDiary.Event.ArrivalLabel".Translate().Resolve();
            string text = startingPawn
                ? "PawnDiary.Event.StartingArrival".Translate(pawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.JoinedArrival".Translate(pawn.LabelShortCap).Resolve();

            DiaryEvent arrivalEvent = AddSoloEvent(pawn, null, ArrivalDefName, label, text, string.Empty,
                BuildArrivalGameContext(pawn, arrivalContext));
            QueueArrivalDescription(arrivalEvent);
        }

        private bool HasArrivalEventFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaryEvents == null)
            {
                return false;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i] != null && diaryEvents[i].IsArrivalDescriptionFor(pawnId))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildArrivalGameContext(Pawn pawn, string arrivalContext)
        {
            List<string> parts = new List<string>
            {
                "arrival_description=true",
                "arrival_pawn=" + DiaryContextBuilder.CleanLine(pawn.LabelShortCap),
                "arrival_pawn_id=" + pawn.GetUniqueLoadID()
            };

            if (!string.IsNullOrWhiteSpace(arrivalContext))
            {
                parts.Add(arrivalContext);
            }
            else
            {
                parts.Add("arrival_source=unknown");
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string BuildStartingArrivalContext()
        {
            List<string> parts = new List<string>
            {
                "arrival_source=game_start"
            };

            Scenario scenario = Verse.Current.Game?.Scenario;
            if (scenario != null)
            {
                string scenarioName = DiaryContextBuilder.CleanLine(scenario.name);
                if (!string.IsNullOrWhiteSpace(scenarioName))
                {
                    parts.Add("scenario_name=" + scenarioName);
                }

                string scenarioDescription = DiaryContextBuilder.CleanLine(scenario.description);
                if (!string.IsNullOrWhiteSpace(scenarioDescription))
                {
                    parts.Add("scenario_description=" + scenarioDescription);
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private static bool DualPovEnabled
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.dualPovGeneration;
            }
        }

        /// <summary>
        /// Convenience overload that auto-generates a fallback description from the pawns and interaction label.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            string fallbackText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionDef.LabelCap.Resolve(), recipient.LabelShortCap);
            RecordInteraction(initiator, recipient, interactionDef, fallbackText);
        }

        /// <summary>
        /// Convenience overload that uses the same game text for both initiator and recipient.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string gameText)
        {
            RecordInteraction(initiator, recipient, interactionDef, gameText, gameText);
        }

        /// <summary>
        /// Records one social interaction (the full entry point used by the Harmony patch). Skips
        /// it unless its group is enabled, then builds a pairwise <see cref="DiaryEvent"/> and
        /// queues generation. The shorter overloads above just fill in default text.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string initiatorGameText, string recipientGameText)
        {
            RecordInteraction(initiator, recipient, interactionDef, initiatorGameText, recipientGameText, -1);
        }

        /// <summary>
        /// Records one social interaction and remembers the originating PlayLog id, so a later
        /// click in RimWorld's Social tab can jump back to the generated diary entry.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string initiatorGameText, string recipientGameText, int playLogEntryId)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!IsInteractionSignificant(interactionDef))
            {
                return;
            }

            bool initiatorEligible = IsDiaryEligible(initiator);
            bool recipientEligible = IsDiaryEligible(recipient);

            if (!initiatorEligible && !recipientEligible)
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryContextBuilder.CleanLine(initiatorGameText);
            string recipientText = DiaryContextBuilder.CleanLine(recipientGameText);

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? initiator : recipient;
                Pawn otherPawn = initiatorEligible ? recipient : initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.Interaction".Translate(eligiblePawn.LabelShortCap, interactionLabel, otherPawn.LabelShortCap);
                }

                string gameContext = DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel);
                DiaryEvent soloEvent = AddSoloEvent(eligiblePawn, otherPawn, interactionDef.defName, interactionLabel,
                    eligibleText, InteractionInstruction(interactionDef), gameContext);
                soloEvent.AddPlayLogEntryId(playLogEntryId);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionLabel, recipient.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            if (IsSmallTalkInteraction(interactionDef))
            {
                RecordSmallTalkInteraction(initiator, recipient, interactionDef, interactionLabel, initiatorText, recipientText, playLogEntryId);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionInstruction(interactionDef),
                DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel));
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            QueuePairwiseGeneration(diaryEvent);
        }

        private void RecordSmallTalkInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string interactionLabel, string initiatorText, string recipientText, int playLogEntryId)
        {
            string key = PairKey(initiator, recipient);
            PendingSmallTalkBatch batch;
            if (!pendingSmallTalkBatches.TryGetValue(key, out batch))
            {
                int now = Find.TickManager.TicksGame;
                batch = new PendingSmallTalkBatch
                {
                    initiator = initiator,
                    recipient = recipient,
                    initiatorPawnId = initiator.GetUniqueLoadID(),
                    recipientPawnId = recipient.GetUniqueLoadID(),
                    firstTick = now,
                    lastTick = now,
                    firstDefName = interactionDef.defName,
                    firstLabel = interactionLabel,
                    instruction = InteractionInstruction(interactionDef)
                };
                pendingSmallTalkBatches[key] = batch;
            }

            AppendSmallTalkBatchLine(batch, initiator, interactionLabel, initiatorText, recipientText);
            AddPlayLogEntryId(batch.playLogEntryIds, playLogEntryId);
            batch.lastTick = Find.TickManager.TicksGame;

            if (batch.Count >= SmallTalkBatchMaxEvents)
            {
                FlushSmallTalkBatch(key, batch);
            }
        }

        // Social fights and mental breaks. Social fights are pairwise (both pawns fight);
        // everything else is recorded as a solo break from the breaking pawn's point of view.
        public void RecordMentalState(Pawn pawn, MentalStateDef stateDef, Pawn otherPawn, string reason)
        {
            if (!IsDiaryEligible(pawn) || stateDef == null)
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.IsMentalStateEnabled(stateDef))
            {
                return;
            }

            string label = stateDef.LabelCap.Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForMentalState(stateDef);
            bool isSocialFight = string.Equals(stateDef.defName, "SocialFighting", StringComparison.OrdinalIgnoreCase)
                && IsDiaryEligible(otherPawn) && otherPawn != pawn;

            if (isSocialFight)
            {
                // SocialFighting fires once per participant; collapse the mirrored call.
                if (RecentlyRecorded(recentMentalEvents, "fight|" + PairKey(pawn, otherPawn), DiaryTuning.Current.socialFightDedupTicks))
                {
                    return;
                }

                string text = "PawnDiary.Event.SocialFight".Translate(pawn.LabelShortCap, otherPawn.LabelShortCap);
                string gameContext = "mental_state=" + stateDef.defName + "; label=" + DiaryContextBuilder.CleanLine(label) + ReasonSuffix(reason);
                DiaryEvent fightEvent = AddPairwiseEvent(pawn, otherPawn, stateDef.defName, label,
                    text, text, instruction, gameContext);
                QueuePairwiseGeneration(fightEvent);
                return;
            }

            if (RecentlyRecorded(recentMentalEvents, "break|" + pawn.GetUniqueLoadID() + "|" + stateDef.defName, DiaryTuning.Current.mentalBreakDedupTicks))
            {
                return;
            }

            string breakText = BuildMentalBreakText(pawn, label, otherPawn, reason);
            string breakContext = "mental_state=" + stateDef.defName + "; label=" + DiaryContextBuilder.CleanLine(label)
                + (otherPawn != null ? "; target=" + DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap) : string.Empty)
                + ReasonSuffix(reason);
            DiaryEvent breakEvent = AddSoloEvent(pawn, otherPawn, stateDef.defName, label, breakText, instruction, breakContext);
            QueueLlmRewrite(breakEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records one RimWorld TaleRecorder event. Tales are vanilla's broader notable-history
        /// events: deaths, wounds, surgeries, births, recruitment, research, disasters, and more.
        /// Some tales involve one pawn, others two; this method records a solo or pairwise diary
        /// event depending on how many eligible colonists are involved.
        /// </summary>
        public void RecordTale(Tale tale, TaleDef taleDef)
        {
            taleDef = taleDef ?? tale?.def;
            if (tale == null || taleDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (TaleDefsCoveredElsewhere.Contains(taleDef.defName) || !PawnDiaryMod.Settings.IsTaleEnabled(taleDef))
            {
                return;
            }

            // Several incidents (Eclipse, Aurora, ToxicFallout, VolcanicWinter, Flashstorm, ...) are
            // recorded BOTH as a Tale and as a GameCondition that shares the same defName. The
            // MoodEvent domain already captures the GameCondition with proper positive/negative mood
            // handling, so skip the Tale here to avoid logging the same event in the diary twice.
            if (DefDatabase<GameConditionDef>.GetNamedSilentFail(taleDef.defName) != null)
            {
                return;
            }

            Pawn firstPawn;
            Pawn secondPawn;
            ExtractTalePawns(tale, out firstPawn, out secondPawn);

            Pawn deathVictim = DeathVictimForTale(taleDef, firstPawn, secondPawn);
            bool deathDescription = IsDeathDescriptionEligible(deathVictim);

            bool firstEligible = IsDiaryEligible(firstPawn) || (deathDescription && firstPawn == deathVictim);
            bool secondEligible = IsDiaryEligible(secondPawn) || (deathDescription && secondPawn == deathVictim);
            if (!firstEligible && !secondEligible)
            {
                return;
            }

            string key = "tale|" + taleDef.defName + "|" + (firstPawn?.GetUniqueLoadID() ?? string.Empty)
                + "|" + (secondPawn?.GetUniqueLoadID() ?? string.Empty);
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string label = CleanTaleLabel(taleDef);
            Def attachedDef = AttachedDefFor(tale);
            string instruction = PawnDiaryMod.Settings.InstructionForTale(taleDef);
            string gameContext = BuildTaleGameContext(tale, taleDef, label, attachedDef);
            if (deathDescription)
            {
                gameContext = AppendDeathDescriptionContext(gameContext, deathVictim, firstPawn, secondPawn);
            }

            if (firstEligible && secondEligible && firstPawn != secondPawn)
            {
                string text = BuildTalePairText(firstPawn, secondPawn, label, attachedDef);
                DiaryEvent pairEvent = AddPairwiseEvent(firstPawn, secondPawn, taleDef.defName, label,
                    text, text, instruction, gameContext);
                if (deathDescription)
                {
                    AddDeathEventRef(deathVictim, pairEvent.eventId);
                    QueueDeathDescription(pairEvent);
                    return;
                }

                QueuePairwiseGeneration(pairEvent);
                return;
            }

            Pawn povPawn = firstEligible ? firstPawn : secondPawn;
            Pawn otherPawn = firstEligible ? secondPawn : firstPawn;
            string soloText = BuildTaleSoloText(povPawn, label, otherPawn, attachedDef);
            DiaryEvent soloEvent = AddSoloEvent(povPawn, otherPawn, taleDef.defName, label, soloText, instruction, gameContext);
            if (deathDescription)
            {
                AddDeathEventRef(deathVictim, soloEvent.eventId);
                QueueDeathDescription(soloEvent);
                return;
            }

            QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records a masterwork or legendary item created by a colonist. Vanilla sends letters for
        /// these through QualityUtility rather than TaleRecorder, so this bridges that notification
        /// into the same solo diary-event flow used by Tale events.
        /// </summary>
        public void RecordCraftedQuality(Thing craftedThing, Pawn worker)
        {
            if (!IsDiaryEligible(worker) || craftedThing == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            QualityCategory quality;
            if (!QualityUtility.TryGetQuality(craftedThing, out quality)
                || (quality != QualityCategory.Masterwork && quality != QualityCategory.Legendary))
            {
                return;
            }

            // Art already flows through vanilla's CraftedArt tale (RecordTale), so recording the
            // quality letter for it too would produce two diary entries for one sculpture. Let the
            // tale own art; this hook covers only non-art masterwork/legendary items.
            if (craftedThing.TryGetComp<CompArt>() != null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGroupEnabled(TaleQualityGroupKey))
            {
                return;
            }

            string defName = quality == QualityCategory.Legendary ? "CraftedLegendary" : "CraftedMasterwork";
            string key = "crafted_quality|" + worker.GetUniqueLoadID() + "|" + defName + "|" + craftedThing.ThingID;
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string qualityLabel = ("QualityCategory_" + quality).Translate().Resolve();
            string itemLabel = DiaryContextBuilder.CleanLine(craftedThing.LabelShortCap);
            string label = "PawnDiary.Event.CraftedQualityLabel".Translate(qualityLabel).Resolve();
            string text = "PawnDiary.Event.CraftedQuality".Translate(worker.LabelShortCap, qualityLabel, itemLabel).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(InteractionGroups.ByKey(TaleQualityGroupKey));
            string gameContext = "tale=" + defName
                + "; source=QualityUtility.SendCraftNotification"
                + "; quality=" + quality
                + "; item=" + itemLabel
                + "; itemDef=" + (craftedThing.def?.defName ?? string.Empty);

            DiaryEvent qualityEvent = AddSoloEvent(worker, null, defName, label, text, instruction, gameContext);
            QueueLlmRewrite(qualityEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records the pawn who installs a venerated ideology relic in a reliquary. The relic
        /// container knows the item but not the worker, so the Harmony patch calls this from the
        /// install job's completion action where both are available.
        /// </summary>
        public void RecordRelicInstalled(Pawn pawn, Thing relic)
        {
            if (!IsDiaryEligible(pawn) || relic == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGroupEnabled(TaleRelicGroupKey))
            {
                return;
            }

            string key = "relic_installed|" + pawn.GetUniqueLoadID() + "|" + relic.ThingID;
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string relicLabel = DiaryContextBuilder.CleanLine(relic.LabelShortCap);
            string label = "PawnDiary.Event.RelicInstalledLabel".Translate().Resolve();
            string text = "PawnDiary.Event.RelicInstalled".Translate(pawn.LabelShortCap, relicLabel).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(InteractionGroups.ByKey(TaleRelicGroupKey));
            string gameContext = "tale=RelicInstalled"
                + "; source=JobDriver_InstallRelic"
                + "; relic=" + relicLabel
                + "; relicDef=" + (relic.def?.defName ?? string.Empty);

            DiaryEvent relicEvent = AddSoloEvent(pawn, null, "RelicInstalled", label, text, instruction, gameContext);
            QueueLlmRewrite(relicEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records a mood-affecting GameCondition (aurora, eclipse, psychic drone, toxic fallout,
        /// etc.) as solo diary events for each eligible colonist on affected maps. Called by the
        /// <see cref="GameConditionStartPatch"/> Harmony postfix on
        /// <c>GameConditionManager.RegisterCondition</c>.
        /// </summary>
        public void RecordMoodEvent(GameCondition condition)
        {
            if (condition == null || condition.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsMoodEventEnabled(condition.def))
            {
                return;
            }

            GameConditionDef conditionDef = condition.def;
            string conditionDefName = conditionDef.defName;
            string conditionLabel = DiaryContextBuilder.CleanLine(conditionDef.LabelCap.Resolve());

            // Dedup: the same GameCondition type within a short window avoids duplicate diary events
            // if a condition fires across multiple maps or re-triggers quickly.
            string dedupKey = "moodevent|" + conditionDefName;
            if (RecentlyRecorded(recentMoodEvents, dedupKey, DiaryTuning.Current.moodEventDedupTicks))
            {
                return;
            }

            string instruction = PawnDiaryMod.Settings.InstructionForMoodEvent(conditionDef);

            // The condition's own thought offset is identical for every colonist, so compute it once
            // here (it scans the whole ThoughtDef database) instead of inside the per-pawn loop.
            float conditionThoughtOffset = DiaryContextBuilder.GetMoodOffsetFromConditionThoughts(conditionDef);

            // Base gameContext shared by all pawns experiencing this condition.
            string baseContext = "mood_event=" + conditionDefName
                + "; label=" + conditionLabel;

            // Collect all eligible colonists on maps affected by this condition.
            // AffectedMaps can be empty for planet-wide conditions, in which case we
            // check all maps in play.
            List<Map> affectedMaps = condition.AffectedMaps;
            if (affectedMaps == null || affectedMaps.Count == 0)
            {
                affectedMaps = Find.Maps;
            }

            HashSet<string> recordedPawnIds = new HashSet<string>();
            for (int i = 0; i < affectedMaps.Count; i++)
            {
                Map map = affectedMaps[i];
                if (map == null || map.mapPawns == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn pawn = colonists[j];
                    if (pawn == null || !IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    // A pawn could be on multiple maps during transitions; skip duplicates.
                    string pawnId = pawn.GetUniqueLoadID();
                    if (recordedPawnIds.Contains(pawnId))
                    {
                        continue;
                    }

                    recordedPawnIds.Add(pawnId);

                    // Each affected colonist gets their own solo diary event, since each
                    // pawn experiences the mood event independently. The mood impact direction
                    // (positive/negative/neutral) is determined per-pawn because some conditions
                    // (e.g. PsychicSuppressorMale) affect different sexes differently.
                    string moodImpact = DiaryContextBuilder.DetermineMoodImpact(condition, pawn, conditionThoughtOffset);
                    string perPawnContext = baseContext + "; mood_impact=" + moodImpact;

                    string text;
                    if (string.Equals(moodImpact, "positive", StringComparison.OrdinalIgnoreCase))
                    {
                        text = "PawnDiary.Event.MoodEventPositive".Translate(pawn.LabelShortCap, conditionLabel).Resolve();
                    }
                    else if (string.Equals(moodImpact, "negative", StringComparison.OrdinalIgnoreCase))
                    {
                        text = "PawnDiary.Event.MoodEventNegative".Translate(pawn.LabelShortCap, conditionLabel).Resolve();
                    }
                    else
                    {
                        text = "PawnDiary.Event.MoodEvent".Translate(pawn.LabelShortCap, conditionLabel).Resolve();
                    }

                    DiaryEvent moodEvent = AddSoloEvent(pawn, null, conditionDefName, conditionLabel,
                        text, instruction, perPawnContext);
                    moodEvent.moodImpact = moodImpact;
                    QueueLlmRewrite(moodEvent, DiaryEvent.InitiatorRole);
                }
            }
        }

        /// <summary>
        /// Records a diary event when a pawn gains a temporary thought with expiration.
        /// Filters out thoughts below magnitude thresholds (±5 general, ±15 eating),
        /// room stat thoughts, and deduplicates within the configured window.
        /// </summary>
        public void RecordThought(Pawn pawn, Thought_Memory thought)
        {
            if (pawn == null || thought == null || thought.def == null)
            {
                return;
            }

            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.IsThoughtEnabled(thought.def))
            {
                return;
            }

            if (thought.def.durationDays <= 0f)
            {
                return;
            }

            if (MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtIgnoreTokens))
            {
                return;
            }

            float moodOffset = GetThoughtMoodOffset(thought);
            bool isEatingThought = MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtEatingTokens);
            bool bypassThreshold = MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtBypassThresholdTokens);

            // Bypass thoughts (death, banishment, etc.) are always recorded regardless of magnitude.
            // Everything else must clear its threshold; eating thoughts use the higher bar.
            if (!bypassThreshold)
            {
                float minMoodOffset = isEatingThought
                    ? DiaryTuning.Current.thoughtEatingMinMoodOffset
                    : DiaryTuning.Current.thoughtMinMoodOffset;
                if (Mathf.Abs(moodOffset) < minMoodOffset)
                {
                    return;
                }
            }

            string dedupKey = "thought|" + pawn.GetUniqueLoadID() + "|" + thought.def.defName;
            if (RecentlyRecorded(recentThoughtEvents, dedupKey, DiaryTuning.Current.thoughtDedupTicks))
            {
                return;
            }

            string thoughtDefName = thought.def.defName;
            string label = DiaryContextBuilder.CleanLine(thought.def.LabelCap.Resolve());
            string instruction = PawnDiaryMod.Settings.InstructionForThought(thought.def);

            string moodImpact = moodOffset > 0.5f ? "positive" : moodOffset < -0.5f ? "negative" : "neutral";

            string gameContext = "thought=" + thoughtDefName
                + "; label=" + label
                + "; mood_impact=" + moodImpact
                + "; mood_offset=" + moodOffset.ToString("F1")
                + "; duration_days=" + thought.def.durationDays.ToString("F1");

            string text;
            if (string.Equals(moodImpact, "positive", StringComparison.OrdinalIgnoreCase))
            {
                text = "PawnDiary.Event.ThoughtPositive".Translate(pawn.LabelShortCap, label).Resolve();
            }
            else if (string.Equals(moodImpact, "negative", StringComparison.OrdinalIgnoreCase))
            {
                text = "PawnDiary.Event.ThoughtNegative".Translate(pawn.LabelShortCap, label).Resolve();
            }
            else
            {
                text = "PawnDiary.Event.Thought".Translate(pawn.LabelShortCap, label).Resolve();
            }

            DiaryEvent thoughtEvent = AddSoloEvent(pawn, null, thoughtDefName, label, text, instruction, gameContext);
            thoughtEvent.moodImpact = moodImpact;
            QueueLlmRewrite(thoughtEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Returns true if the thought's defName contains any of the tokens in the given list (case-insensitive).
        /// Used for configurable ignore/bypass/classification token lists from DiaryTuningDef.
        /// </summary>
        private static bool MatchesAnyToken(ThoughtDef thoughtDef, List<string> tokens)
        {
            if (thoughtDef == null || string.IsNullOrEmpty(thoughtDef.defName) || tokens == null)
            {
                return false;
            }

            string defName = thoughtDef.defName;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!string.IsNullOrEmpty(tokens[i])
                    && defName.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the mood offset for a thought's current stage. Uses Thought.MoodOffset()
        /// which returns the active stage's baseMoodEffect (not a sum of all stages).
        /// </summary>
        private static float GetThoughtMoodOffset(Thought_Memory thought)
        {
            if (thought == null)
            {
                return 0f;
            }

            return thought.MoodOffset();
        }

        /// <summary>
        /// Creates a DiaryEvent involving two pawns (initiator + recipient), enriches it with context
        /// summaries, registers it, and cross-references both pawns' diary records.
        /// </summary>
        private DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext)
        {
            string neutralText = string.Equals(initiatorText, recipientText, StringComparison.OrdinalIgnoreCase)
                ? initiatorText
                : initiator.LabelShortCap + ": " + initiatorText + " / " + recipient.LabelShortCap + ": " + recipientText;

            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = Find.TickManager.TicksGame,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = initiator.GetUniqueLoadID(),
                recipientPawnId = recipient.GetUniqueLoadID(),
                initiatorName = initiator.LabelShortCap,
                recipientName = recipient.LabelShortCap,
                initiatorText = initiatorText,
                recipientText = recipientText,
                neutralText = neutralText,
                sequenceText = neutralText,
                gameContext = gameContext,
                instruction = instruction,
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(initiator),
                recipientPawnSummary = DiaryContextBuilder.BuildPawnSummary(recipient),
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(initiator),
                recipientSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(recipient),
                opinionsSummary = DiaryContextBuilder.BuildOpinionsSummary(initiator, recipient),
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(initiator, recipient, diaryEvents),
                recipientContinuity = DiaryContextBuilder.BuildContinuitySummary(recipient, initiator, diaryEvents),
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(initiator.GetUniqueLoadID(), diaryEvents),
                recipientLastOpener = DiaryContextBuilder.LatestDiaryOpener(recipient.GetUniqueLoadID(), diaryEvents),
                initiatorBurningPassion = DiaryContextBuilder.RandomBurningPassion(initiator),
                recipientBurningPassion = DiaryContextBuilder.RandomBurningPassion(recipient),
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(initiator),
                recipientWeapon = DiaryContextBuilder.EquippedWeapon(recipient),
                initiatorAtmosphere = DiaryContextBuilder.BuildAtmosphere(initiator, recipient, instruction),
                recipientAtmosphere = DiaryContextBuilder.BuildAtmosphere(recipient, initiator, instruction),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId);
            AddEventRef(recipient, diaryEvent.eventId);
            return diaryEvent;
        }

        /// <summary>
        /// Creates a solo DiaryEvent (single-POV, e.g. a mental break) with no recipient role.
        /// </summary>
        private DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext)
        {
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = Find.TickManager.TicksGame,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = pawn.GetUniqueLoadID(),
                recipientPawnId = string.Empty,
                initiatorName = pawn.LabelShortCap,
                recipientName = string.Empty,
                initiatorText = text,
                recipientText = string.Empty,
                neutralText = text,
                sequenceText = text,
                gameContext = gameContext,
                instruction = instruction,
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(pawn),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn),
                recipientSurroundings = "n/a",
                opinionsSummary = otherPawn != null ? DiaryContextBuilder.BuildOpinionsSummary(pawn, otherPawn) : "n/a",
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(pawn, otherPawn, diaryEvents),
                recipientContinuity = "none",
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(pawn.GetUniqueLoadID(), diaryEvents),
                recipientLastOpener = string.Empty,
                initiatorBurningPassion = DiaryContextBuilder.RandomBurningPassion(pawn),
                recipientBurningPassion = string.Empty,
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(pawn),
                recipientWeapon = string.Empty,
                initiatorAtmosphere = DiaryContextBuilder.BuildAtmosphere(pawn, otherPawn, instruction),
                recipientAtmosphere = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId);
            return diaryEvent;
        }

        /// <summary>
        /// Dispatches a pairwise event for LLM generation: sequential if dual-POV is on, else initiator-only.
        /// </summary>
        private void QueuePairwiseGeneration(DiaryEvent diaryEvent)
        {
            if (DualPovEnabled)
            {
                QueueSequentialPairwiseRewrite(diaryEvent);
            }
            else
            {
                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
                QueueLlmRewrite(diaryEvent, DiaryEvent.RecipientRole);
            }
        }

        /// <summary>
        /// Assembles a human-readable fallback description for a mental break, including target and reason if available.
        /// </summary>
        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = "PawnDiary.Event.MentalBreak".Translate(pawn.LabelShortCap, DiaryContextBuilder.CleanLine(label));
            if (otherPawn != null)
            {
                text += "PawnDiary.Event.DirectedAt".Translate(DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap)).Resolve();
            }

            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }

        /// <summary>
        /// Returns a cleaned "reason=…" suffix for appending to gameContext strings, or empty if no reason.
        /// </summary>
        private static string ReasonSuffix(string reason)
        {
            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            return string.IsNullOrWhiteSpace(cleanReason) ? string.Empty : "; reason=" + cleanReason;
        }

        /// <summary>
        /// Pulls the live pawn references out of vanilla Tale subclasses. TaleData also keeps
        /// historical snapshots, but the live Pawn is what we need for diary ownership/context.
        /// </summary>
        private static void ExtractTalePawns(Tale tale, out Pawn firstPawn, out Pawn secondPawn)
        {
            firstPawn = null;
            secondPawn = null;

            Tale_DoublePawn doublePawnTale = tale as Tale_DoublePawn;
            if (doublePawnTale != null)
            {
                firstPawn = doublePawnTale.firstPawnData?.pawn;
                secondPawn = doublePawnTale.secondPawnData?.pawn;
                return;
            }

            Tale_SinglePawn singlePawnTale = tale as Tale_SinglePawn;
            if (singlePawnTale != null)
            {
                firstPawn = singlePawnTale.pawnData?.pawn;
            }
        }

        /// <summary>
        /// Returns the extra Def attached to some tales (research project, skill, damage type,
        /// crafted object kind, etc.), or null for plain pawn-only tales.
        /// </summary>
        private static Def AttachedDefFor(Tale tale)
        {
            Tale_DoublePawnAndDef doublePawnAndDef = tale as Tale_DoublePawnAndDef;
            if (doublePawnAndDef != null)
            {
                return doublePawnAndDef.defData?.def;
            }

            Tale_SinglePawnAndDef singlePawnAndDef = tale as Tale_SinglePawnAndDef;
            return singlePawnAndDef?.defData?.def;
        }

        /// <summary>
        /// Resolves a user-facing TaleDef label, falling back to defName if the label is blank.
        /// </summary>
        private static string CleanTaleLabel(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return "unknown";
            }

            string label = DiaryContextBuilder.CleanLine(taleDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? taleDef.defName : label;
        }

        /// <summary>
        /// Builds the raw game text for a single-pawn tale, localized because it is shown in
        /// the Diary tab and also passed to the model as "what happened".
        /// </summary>
        private static string BuildTaleSoloText(Pawn povPawn, string label, Pawn otherPawn, Def attachedDef)
        {
            string text = otherPawn != null
                ? "PawnDiary.Event.TaleSoloWithOther".Translate(povPawn.LabelShortCap, label, otherPawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.TaleSolo".Translate(povPawn.LabelShortCap, label).Resolve();

            return AppendAttachedDefText(text, attachedDef);
        }

        /// <summary>
        /// Builds the raw game text for a two-pawn tale. Both pawns receive the same factual
        /// description; the LLM prompt adds separate pawn summaries and continuity for each POV.
        /// </summary>
        private static string BuildTalePairText(Pawn firstPawn, Pawn secondPawn, string label, Def attachedDef)
        {
            string text = "PawnDiary.Event.TalePair".Translate(firstPawn.LabelShortCap, secondPawn.LabelShortCap, label).Resolve();
            return AppendAttachedDefText(text, attachedDef);
        }

        /// <summary>
        /// Appends the TaleData_Def label when vanilla supplied one, such as a research project,
        /// skill, damage type, or crafted object kind.
        /// </summary>
        private static string AppendAttachedDefText(string text, Def attachedDef)
        {
            if (attachedDef == null)
            {
                return text;
            }

            string label = DiaryContextBuilder.CleanLine(attachedDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label)
                ? text
                : text + "PawnDiary.Event.TaleAttachedDef".Translate(label).Resolve();
        }

        /// <summary>
        /// Creates a compact metadata string for Tale-sourced diary events. The leading tale=
        /// marker is also how saved events are later classified back into the Tale domain.
        /// </summary>
        private static string BuildTaleGameContext(Tale tale, TaleDef taleDef, string label, Def attachedDef)
        {
            List<string> parts = new List<string>
            {
                "tale=" + taleDef.defName,
                "label=" + DiaryContextBuilder.CleanLine(label),
                "taleClass=" + tale.GetType().Name
            };

            if (attachedDef != null)
            {
                parts.Add("attachedDef=" + attachedDef.defName);
                parts.Add("attachedLabel=" + DiaryContextBuilder.CleanLine(attachedDef.LabelCap.Resolve()));
            }

            return string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Appends colonist-death metadata to the Tale context. The neutral death prompt reads this
        /// instead of using a pawn persona, so it can describe cause, damaged body part, illness, and
        /// nearby context without pretending the dead pawn wrote a diary entry.
        /// </summary>
        private static string AppendDeathDescriptionContext(string gameContext, Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            List<string> parts = new List<string>
            {
                gameContext,
                "death_description=true",
                "death_victim=" + DiaryContextBuilder.CleanLine(deathVictim.LabelShortCap),
                "death_victim_id=" + deathVictim.GetUniqueLoadID(),
                "death_victim_role=" + DeathVictimRole(deathVictim, firstPawn, secondPawn)
            };

            Pawn otherPawn = deathVictim == firstPawn ? secondPawn : firstPawn;
            if (otherPawn != null)
            {
                parts.Add("other_pawn=" + DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap));
            }

            string deathFacts = DeathContextCache.ConsumeOrBuild(deathVictim);
            if (!string.IsNullOrWhiteSpace(deathFacts))
            {
                parts.Add(deathFacts);
            }

            return string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Returns the pawn who died for TaleDefs that represent deaths. Vanilla's TaleDefs do not
        /// use one consistent pawn order, so this method keeps that mapping in one place.
        /// </summary>
        private static Pawn DeathVictimForTale(TaleDef taleDef, Pawn firstPawn, Pawn secondPawn)
        {
            if (taleDef == null || string.IsNullOrWhiteSpace(taleDef.defName) || !DeathTaleDefs.Contains(taleDef.defName))
            {
                return null;
            }

            // KilledBy is firstPawn=VICTIM, secondPawn=KILLER. Most other kill tales are
            // firstPawn=KILLER, secondPawn=VICTIM/COLONIST/CHILD.
            if (string.Equals(taleDef.defName, "KilledBy", StringComparison.OrdinalIgnoreCase))
            {
                return firstPawn;
            }

            return secondPawn;
        }

        private static string DeathVictimRole(Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            if (deathVictim == firstPawn)
            {
                return DiaryEvent.InitiatorRole;
            }

            if (deathVictim == secondPawn)
            {
                return DiaryEvent.RecipientRole;
            }

            return DiaryEvent.NeutralRole;
        }

        private static bool IsDeathDescriptionEligible(Pawn pawn)
        {
            return pawn != null
                && IsHumanlike(pawn)
                && (pawn.IsColonist || pawn.Faction == Faction.OfPlayer);
        }

        /// <summary>
        /// Dedup gate: returns true if the same key was recorded within the given tick window, preventing
        /// duplicate events (e.g. mirrored SocialFighting calls).
        /// </summary>
        private bool RecentlyRecorded(Dictionary<string, int> recentEvents, string key, int windowTicks)
        {
            int now = Find.TickManager.TicksGame;
            int last;
            if (recentEvents.TryGetValue(key, out last) && now - last < windowTicks)
            {
                return true;
            }

            recentEvents[key] = now;
            return false;
        }

        /// <summary>
        /// Checks whether a pawn is a humanlike (as opposed to an animal or mechanoid).
        /// </summary>
        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }

        /// <summary>
        /// A pawn qualifies for diary tracking if it is a humanlike colonist.
        /// </summary>
        private static bool IsDiaryEligible(Pawn pawn)
        {
            return IsHumanlike(pawn) && pawn.IsColonist;
        }

        /// <summary>
        /// Called each tick: flushes any batch that has exceeded the time window or hit the max-events cap.
        /// </summary>
        private void FlushReadySmallTalkBatches()
        {
            if (pendingSmallTalkBatches.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int windowTicks = SmallTalkBatchWindowTicks;
            List<string> keysToFlush = new List<string>();

            foreach (KeyValuePair<string, PendingSmallTalkBatch> pair in pendingSmallTalkBatches)
            {
                PendingSmallTalkBatch batch = pair.Value;
                if (batch == null || batch.Count >= SmallTalkBatchMaxEvents || now - batch.lastTick >= windowTicks)
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushSmallTalkBatches(keysToFlush);
        }

        /// <summary>
        /// Flushes every pending small-talk batch immediately (used on save to avoid losing transient data).
        /// </summary>
        private void FlushAllSmallTalkBatches()
        {
            if (pendingSmallTalkBatches.Count == 0)
            {
                return;
            }

            FlushSmallTalkBatches(new List<string>(pendingSmallTalkBatches.Keys));
        }

        /// <summary>
        /// Flushes each batch identified by its key in the provided list.
        /// </summary>
        private void FlushSmallTalkBatches(List<string> keysToFlush)
        {
            if (keysToFlush == null)
            {
                return;
            }

            for (int i = 0; i < keysToFlush.Count; i++)
            {
                PendingSmallTalkBatch batch;
                if (pendingSmallTalkBatches.TryGetValue(keysToFlush[i], out batch))
                {
                    FlushSmallTalkBatch(keysToFlush[i], batch);
                }
            }
        }

        /// <summary>
        /// Converts a finished batch into a DiaryEvent (solo or pairwise depending on eligibility) and
        /// queues it for LLM generation.
        /// </summary>
        private void FlushSmallTalkBatch(string key, PendingSmallTalkBatch batch)
        {
            pendingSmallTalkBatches.Remove(key);

            if (batch == null || batch.initiator == null || batch.recipient == null || batch.Count == 0)
            {
                return;
            }

            bool initiatorEligible = IsDiaryEligible(batch.initiator);
            bool recipientEligible = IsDiaryEligible(batch.recipient);

            if (!initiatorEligible && !recipientEligible)
            {
                return;
            }

            bool combined = batch.Count > 1;
            string label = combined ? "PawnDiary.Event.SmallTalkLabel".Translate().Resolve() : batch.firstLabel;
            string defName = combined ? SmallTalkBatchDefName : batch.firstDefName;
            string initiatorText = BuildSmallTalkBatchText(batch.initiatorLines);
            string recipientText = BuildSmallTalkBatchText(batch.recipientLines);
            string instruction = SmallTalkBatchInstruction(batch.instruction);
            string gameContext = "group=smalltalk; events=" + batch.Count
                + "; first_tick=" + batch.firstTick
                + "; last_tick=" + batch.lastTick;

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? batch.initiator : batch.recipient;
                Pawn otherPawn = initiatorEligible ? batch.recipient : batch.initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.SmallTalk".Translate(eligiblePawn.LabelShortCap, otherPawn.LabelShortCap);
                }

                DiaryEvent soloEvent = AddSoloEvent(eligiblePawn, otherPawn, defName, label,
                    eligibleText, instruction, gameContext);
                AddPlayLogEntryIds(soloEvent, batch.playLogEntryIds);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(batch.initiator, batch.recipient, defName, label,
                initiatorText, recipientText, instruction, gameContext);
            AddPlayLogEntryIds(diaryEvent, batch.playLogEntryIds);
            QueuePairwiseGeneration(diaryEvent);
        }

        /// <summary>
        /// Appends one interaction line to the batch, assigning it to the correct initiator/recipient line lists
        /// regardless of which pawn was the initiator in the original interaction.
        /// </summary>
        private static void AppendSmallTalkBatchLine(PendingSmallTalkBatch batch, Pawn initiator,
            string interactionLabel, string initiatorText, string recipientText)
        {
            string initiatorLine = SmallTalkBatchLine(interactionLabel, initiatorText);
            string recipientLine = SmallTalkBatchLine(interactionLabel, recipientText);

            if (initiator.GetUniqueLoadID() == batch.initiatorPawnId)
            {
                batch.initiatorLines.Add(initiatorLine);
                batch.recipientLines.Add(recipientLine);
            }
            else
            {
                batch.initiatorLines.Add(recipientLine);
                batch.recipientLines.Add(initiatorLine);
            }
        }

        /// <summary>
        /// Adds one PlayLog id to an in-progress small-talk batch, ignoring the sentinel used
        /// by older call paths that do not know the originating log row.
        /// </summary>
        private static void AddPlayLogEntryId(List<int> playLogEntryIds, int playLogEntryId)
        {
            if (playLogEntryIds == null || playLogEntryId < 0 || playLogEntryIds.Contains(playLogEntryId))
            {
                return;
            }

            playLogEntryIds.Add(playLogEntryId);
        }

        /// <summary>
        /// Copies all PlayLog ids from a small-talk batch onto its merged diary event.
        /// </summary>
        private static void AddPlayLogEntryIds(DiaryEvent diaryEvent, List<int> playLogEntryIds)
        {
            if (diaryEvent == null || playLogEntryIds == null)
            {
                return;
            }

            for (int i = 0; i < playLogEntryIds.Count; i++)
            {
                diaryEvent.AddPlayLogEntryId(playLogEntryIds[i]);
            }
        }

        /// <summary>
        /// Formats accumulated lines into a single description string; uses a numbered list when multiple.
        /// </summary>
        private static string BuildSmallTalkBatchText(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return "PawnDiary.Event.SmallTalkBrief".Translate();
            }

            if (lines.Count == 1)
            {
                return lines[0];
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("PawnDiary.Event.SmallTalkBatchHeader".Translate().Resolve());
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append("\n").Append(i + 1).Append(". ").Append(lines[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats one small-talk moment as "label: text" (or just text if the label is empty).
        /// </summary>
        private static string SmallTalkBatchLine(string interactionLabel, string text)
        {
            string cleanText = DiaryContextBuilder.CleanLine(text);
            string cleanLabel = DiaryContextBuilder.CleanLine(interactionLabel);
            if (string.IsNullOrWhiteSpace(cleanLabel))
            {
                return cleanText;
            }

            return cleanLabel + ": " + cleanText;
        }

        /// <summary>
        /// Appends a batching-specific instruction to the base instruction so the LLM writes one combined entry.
        /// </summary>
        private static string SmallTalkBatchInstruction(string baseInstruction)
        {
            string instruction = DiaryContextBuilder.CleanLine(baseInstruction);
            string batchingInstruction = "PawnDiary.Event.SmallTalkBatchInstruction".Translate();
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return batchingInstruction;
            }

            return instruction + "; " + batchingInstruction;
        }

        /// <summary>
        /// How many ticks a small-talk batch stays open before being flushed automatically.
        /// </summary>
        private static int SmallTalkBatchWindowTicks
        {
            get
            {
                return Math.Max(0, DiaryTuning.Current.smallTalkBatchWindowTicks);
            }
        }

        /// <summary>
        /// Maximum number of small-talk interactions to accumulate per batch before forcing a flush.
        /// </summary>
        private static int SmallTalkBatchMaxEvents
        {
            get
            {
                return Math.Max(1, DiaryTuning.Current.smallTalkBatchMaxEvents);
            }
        }

        /// <summary>
        /// Produces a deterministic, order-independent key for a pawn pair (for small-talk batch grouping).
        /// </summary>
        private static string PairKey(Pawn first, Pawn second)
        {
            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }

        /// <summary>
        /// Adds an event ID to a pawn's diary, creating the diary record if necessary.
        /// </summary>
        private void AddEventRef(Pawn pawn, string eventId)
        {
            if (!IsDiaryEligible(pawn) || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return;
            }

            DiaryEvent diaryEvent = FindEvent(eventId);
            if (EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawn.GetUniqueLoadID(), diary)))
            {
                return;
            }

            diary.pawnName = pawn.LabelShortCap;
            if (diary.eventIds == null)
            {
                diary.eventIds = new List<string>();
            }

            if (!diary.eventIds.Contains(eventId))
            {
                diary.eventIds.Add(eventId);
            }
        }

        /// <summary>
        /// Adds the death-description event to the deceased colonist's diary even after death state
        /// makes the usual live-colonist eligibility checks unreliable.
        /// </summary>
        private void AddDeathEventRef(Pawn pawn, string eventId)
        {
            if (!IsDeathDescriptionEligible(pawn) || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return;
            }

            DiaryEvent diaryEvent = FindEvent(eventId);
            if (EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawn.GetUniqueLoadID(), diary)))
            {
                return;
            }

            diary.pawnName = pawn.LabelShortCap;
            if (diary.eventIds == null)
            {
                diary.eventIds = new List<string>();
            }

            if (!diary.eventIds.Contains(eventId))
            {
                diary.eventIds.Add(eventId);
            }
        }

        /// <summary>
        /// Returns the tick of the latest neutral death-description event for a pawn. That event is
        /// terminal: anything later is suppressed from display and generation for that pawn.
        /// </summary>
        private int? FinalDeathTickFor(string pawnId, PawnDiaryRecord diary)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diary?.eventIds == null)
            {
                return null;
            }

            int? finalDeathTick = null;
            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null || !diaryEvent.IsDeathDescriptionFor(pawnId))
                {
                    continue;
                }

                if (!finalDeathTick.HasValue || diaryEvent.tick > finalDeathTick.Value)
                {
                    finalDeathTick = diaryEvent.tick;
                }
            }

            return finalDeathTick;
        }

        private bool EventFallsAfterFinalDeathForPawn(DiaryEvent diaryEvent, string pawnId)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawnId, diary));
        }

        private static bool EventFallsAfterFinalDeath(DiaryEvent diaryEvent, int? finalDeathTick)
        {
            return diaryEvent != null && finalDeathTick.HasValue && diaryEvent.tick > finalDeathTick.Value;
        }

        /// <summary>
        /// Builds the prompt for a single POV role and enqueues the LLM request if generation is still allowed.
        /// </summary>
        private void QueueLlmRewrite(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            // Persona is resolved at queue time so changing a pawn affects future generations
            // without rewriting prompts that were already sent or saved for debugging.
            string rawText = DiaryPromptBuilder.BuildInteractionPrompt(diaryEvent, povRole, PersonaRuleFor(diaryEvent, povRole));
            QueuePrompt(diaryEvent, povRole, rawText);
        }

        /// <summary>
        /// Queues the persona-independent neutral description used for colonist deaths. This is not
        /// a first-person diary entry; it is a concise record of how the pawn died.
        /// </summary>
        private void QueueDeathDescription(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || !diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
            {
                return;
            }

            string rawText = DiaryPromptBuilder.BuildDeathDescriptionPrompt(diaryEvent);
            QueuePrompt(diaryEvent, DiaryEvent.NeutralRole, rawText);
        }

        /// <summary>
        /// Queues the persona-independent neutral description used for colony arrivals. This is a
        /// factual first page for the pawn's diary, not a first-person entry.
        /// </summary>
        private void QueueArrivalDescription(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || !diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
            {
                return;
            }

            string rawText = DiaryPromptBuilder.BuildArrivalDescriptionPrompt(diaryEvent);
            QueuePrompt(diaryEvent, DiaryEvent.NeutralRole, rawText);
        }

        /// <summary>
        /// Dual-POV flow: queues the initiator first, then the recipient once the initiator result arrives
        /// (so the recipient prompt can include the initiator's generated text as hidden continuity context).
        /// </summary>
        private void QueueSequentialPairwiseRewrite(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || diaryEvent.solo)
            {
                return;
            }

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole);

            // Normal paired flow: initiator writes first, then recipient can receive that entry
            // as hidden continuity context.
            if (initiatorEnabled && diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
            {
                string rawText = DiaryPromptBuilder.BuildSequentialInteractionPrompt(diaryEvent, DiaryEvent.InitiatorRole, PersonaRuleFor(diaryEvent, DiaryEvent.InitiatorRole));
                QueuePrompt(diaryEvent, DiaryEvent.InitiatorRole, rawText);
                return;
            }

            // If the recipient is disabled, stop here even if the initiator completed.
            if (!recipientEnabled)
            {
                return;
            }

            // Keep the old paired behavior when the initiator was supposed to generate but failed.
            if (initiatorEnabled && string.Equals(diaryEvent.initiatorStatus, DiaryEvent.FailedStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                }

                return;
            }

            // Wait for initiator context only when the initiator is enabled. If the initiator is
            // disabled, the recipient can still generate from the base event prompt.
            if (initiatorEnabled
                && (!string.Equals(diaryEvent.initiatorStatus, DiaryEvent.CompleteStatus, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(diaryEvent.initiatorGeneratedText)))
            {
                return;
            }

            if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
            {
                // Recipient prompt includes hidden initiator context only when that context exists.
                string rawText = initiatorEnabled
                    ? DiaryPromptBuilder.BuildSequentialInteractionPrompt(diaryEvent, DiaryEvent.RecipientRole, PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole))
                    : DiaryPromptBuilder.BuildInteractionPrompt(diaryEvent, DiaryEvent.RecipientRole, PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole));
                QueuePrompt(diaryEvent, DiaryEvent.RecipientRole, rawText);
            }
        }

        /// <summary>
        /// Final step before LLM dispatch: stamps the prompt on the event, records endpoint metadata, marks
        /// the event queued, and enqueues the request to <see cref="LlmClient"/>.
        /// </summary>
        private void QueuePrompt(DiaryEvent diaryEvent, string povRole, string rawText)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            diaryEvent.SetPrompt(povRole, rawText);

            if (PawnDiaryMod.Settings == null)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoLlmSettings".Translate());
                return;
            }

            // Pick which configured API lane handles this request. New events spread across all
            // lanes (parallelism); the recipient half of a paired event reuses the initiator's lane
            // so a sequential pair stays on one model.
            List<ApiEndpointConfig> targets = PawnDiaryMod.Settings.ActiveEndpoints();
            LogApiLaneConfiguration(PawnDiaryMod.Settings, targets);
            if (targets.Count == 0)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoApiConfigured".Translate());
                return;
            }

            string selectionReason;
            ApiEndpointConfig target = SelectApiTarget(diaryEvent, povRole, targets, out selectionReason);
            List<ApiEndpointConfig> failoverTargets = BuildFailoverTargets(targets, target);
            LogApiDebug(
                "Queue event=" + diaryEvent.eventId
                + " role=" + povRole
                + " primary=" + LaneLabel(target)
                + " reason=" + selectionReason
                + " failovers=[" + LaneList(failoverTargets) + "]");

            diaryEvent.SetLlmMeta(povRole, EndpointUtility.BuildChatCompletionsUrl(target.url), target.model);
            diaryEvent.MarkQueued(povRole);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                systemPrompt = PawnDiaryMod.Settings.systemPrompt,
                rawText = rawText,
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                // The other configured lanes, tried in order if this one errors ("use next model").
                failoverTargets = failoverTargets,
                timeoutSeconds = PawnDiaryMod.Settings.timeoutSeconds,
                maxTokens = PawnDiaryMod.Settings.maxTokens,
                temperature = PawnDiaryMod.Settings.temperature
            });
        }

        /// <summary>
        /// Chooses the primary API lane for a request from the given active lanes (non-empty).
        /// The recipient of a paired (dual-POV) event reuses the lane the initiator used — matched
        /// by the endpoint+model recorded on the event — so the sequential pair shares one model.
        /// Everything else takes the next lane in round-robin order to spread load across APIs.
        /// </summary>
        private static ApiEndpointConfig SelectApiTarget(DiaryEvent diaryEvent, string povRole, List<ApiEndpointConfig> targets, out string reason)
        {
            // Sequential pinning: keep the recipient on the initiator's lane when paired mode ran
            // the initiator first and recorded which endpoint+model it used (after failover, the
            // recorded meta is the lane that actually produced the initiator's entry).
            if (DualPovEnabled && DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole))
            {
                string initiatorEndpoint = diaryEvent.initiatorLlmEndpoint;
                string initiatorModel = diaryEvent.initiatorLlmModel;
                if (!string.IsNullOrWhiteSpace(initiatorEndpoint) && !string.IsNullOrWhiteSpace(initiatorModel))
                {
                    foreach (ApiEndpointConfig candidate in targets)
                    {
                        if (string.Equals(EndpointUtility.BuildChatCompletionsUrl(candidate.url), initiatorEndpoint, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(candidate.model, initiatorModel, StringComparison.Ordinal))
                        {
                            reason = "recipient pinned to initiator lane";
                            return candidate;
                        }
                    }

                    LogApiDebug(
                        "Recipient pin missed for event=" + diaryEvent.eventId
                        + " initiatorLane=" + initiatorModel + " @ " + initiatorEndpoint
                        + "; falling back to round-robin");
                }
            }

            int index = LlmClient.NextRoundRobinIndex() % targets.Count;
            reason = "round-robin index " + index + " of " + targets.Count;
            return targets[index];
        }

        /// <summary>
        /// Returns the lanes to fall back to (every active lane except the chosen primary), in order,
        /// so a request that errors on its primary lane can retry on the others.
        /// </summary>
        private static List<ApiEndpointConfig> BuildFailoverTargets(List<ApiEndpointConfig> targets, ApiEndpointConfig primary)
        {
            List<ApiEndpointConfig> failovers = new List<ApiEndpointConfig>();
            foreach (ApiEndpointConfig candidate in targets)
            {
                if (!ReferenceEquals(candidate, primary))
                {
                    failovers.Add(candidate);
                }
            }

            return failovers;
        }

        /// <summary>
        /// Writes one-line API lane diagnostics to the RimWorld log. These are intentionally
        /// English debug logs, not player-facing UI strings; never include API keys here.
        /// </summary>
        private static void LogApiLaneConfiguration(PawnDiarySettings settings, List<ApiEndpointConfig> active)
        {
            int configuredCount = settings?.apiEndpoints?.Count ?? 0;
            int activeCount = active?.Count ?? 0;
            LogApiDebug(
                "Configured API lanes=" + configuredCount
                + ", active lanes=" + activeCount
                + (configuredCount == activeCount ? string.Empty : " (disabled rows or rows with blank url/model are skipped)")
                + "; active=[" + LaneList(active) + "]");
        }

        private static void LogApiDebug(string message)
        {
            Log.Message("[PawnDiary debug] " + message);
        }

        private static string LaneList(List<ApiEndpointConfig> lanes)
        {
            if (lanes == null || lanes.Count == 0)
            {
                return "none";
            }

            List<string> labels = new List<string>();
            foreach (ApiEndpointConfig lane in lanes)
            {
                labels.Add(LaneLabel(lane));
            }

            return string.Join(" | ", labels.ToArray());
        }

        private static string LaneLabel(ApiEndpointConfig lane)
        {
            if (lane == null)
            {
                return "<null>";
            }

            return (string.IsNullOrWhiteSpace(lane.model) ? "<blank-model>" : lane.model)
                + " @ "
                + (string.IsNullOrWhiteSpace(lane.url) ? "<blank-url>" : EndpointUtility.BuildChatCompletionsUrl(lane.url));
        }


        /// <summary>
        /// Returns the user-defined instruction for a specific interaction def, or empty string if none.
        /// </summary>
        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return PawnDiaryMod.Settings?.InstructionFor(interactionDef) ?? string.Empty;
        }

        /// <summary>
        /// An interaction is recorded only if its group (see InteractionGroups) is enabled in settings.
        /// </summary>
        private static bool IsInteractionSignificant(InteractionDef interactionDef)
        {
            return interactionDef != null
                && !string.IsNullOrWhiteSpace(interactionDef.defName)
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsInteractionEnabled(interactionDef);
        }

        /// <summary>
        /// Returns true when the interaction belongs to the "smalltalk" group and should be batched rather
        /// than recorded individually.
        /// </summary>
        private static bool IsSmallTalkInteraction(InteractionDef interactionDef)
        {
            // Only the low-stakes Small talk group batches. Heartfelt events such as DeepTalk
            // stay immediate because their group key is "heartfelt", not "smalltalk".
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && string.Equals(group.defName, SmallTalkGroupKey, StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Dequeues a completed LLM result and applies it to the corresponding DiaryEvent, then kicks
        /// off the recipient side if dual-POV is active.
        /// </summary>
        private void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.eventId))
            {
                return;
            }

            DiaryEvent diaryEvent = FindEvent(result.eventId);
            if (diaryEvent == null)
            {
                return;
            }

            diaryEvent.ApplyLlmResult(result);

            // Record the lane that actually produced the text. After failover this may differ from
            // the primary lane chosen at queue time, so updating it here keeps the debug block
            // accurate and lets a paired recipient pin to the model the initiator really used.
            if (result.success && !string.IsNullOrWhiteSpace(result.endpointUrl) && !string.IsNullOrWhiteSpace(result.modelName))
            {
                diaryEvent.SetLlmMeta(result.povRole, EndpointUtility.BuildChatCompletionsUrl(result.endpointUrl), result.modelName);
            }

            QueueRecipientAfterInitiatorResult(diaryEvent, result);
        }

        /// <summary>
        /// After the initiator entry completes, either marks the recipient as failed (if the initiator failed)
        /// or re-evaluates the sequential queue so the recipient can generate with the initiator's text as context.
        /// </summary>
        private void QueueRecipientAfterInitiatorResult(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (!DualPovEnabled
                || diaryEvent == null
                || diaryEvent.solo
                || result == null
                || !string.Equals(result.povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.success)
            {
                // Do not mark a disabled recipient as failed; they intentionally have no LLM state.
                if (DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole)
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                }

                return;
            }

            QueueSequentialPairwiseRewrite(diaryEvent);
        }

        /// <summary>
        /// Checks whether the pawn for a given POV role in an event has diary generation enabled,
        /// resolving the pawn via its saved diary record.
        /// </summary>
        private bool DiaryGenerationEnabledFor(DiaryEvent diaryEvent, string povRole)
        {
            // DiaryEvents store pawn IDs, not Pawn instances, so queue-time checks resolve through
            // the saved diary record for that POV.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return true;
            }

            if (EventFallsAfterFinalDeathForPawn(diaryEvent, pawnId))
            {
                return false;
            }

            return FindDiaryByPawnId(pawnId)?.diaryGenerationEnabled ?? true;
        }

        /// <summary>
        /// Resolves the LLM persona rule string for a given POV in an event, falling back to the XML default.
        /// </summary>
        private string PersonaRuleFor(DiaryEvent diaryEvent, string povRole)
        {
            // Missing records fall back to the XML default persona, which also covers old saves.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return DiaryPersonas.RuleFor(diary?.personaDefName);
        }

        /// <summary>
        /// Returns the pawn ID for a given POV role in a DiaryEvent (initiator or recipient).
        /// </summary>
        private static string PawnIdForRole(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            if (string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnId;
            }

            if (string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.initiatorPawnId;
            }

            return null;
        }

        /// <summary>
        /// Locates a DiaryEvent by its unique ID.
        /// </summary>
        private DiaryEvent FindEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || diaryEvents == null)
            {
                return null;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i].eventId == eventId)
                {
                    return diaryEvents[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Public accessor for finding a DiaryEvent by ID (used by the diary tab to build
        /// linked-entry previews and navigate to the other pawn's entry).
        /// </summary>
        public DiaryEvent FindEventById(string eventId)
        {
            return FindEvent(eventId);
        }

        /// <summary>
        /// Looks up a pawn's diary record by Pawn instance; optionally creates one if missing.
        /// </summary>
        private PawnDiaryRecord FindDiary(Pawn pawn, bool createIfMissing)
        {
            if (pawn == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < diaries.Count; i++)
            {
                if (diaries[i].pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diaries[i]);
                    return diaries[i];
                }
            }

            if (!createIfMissing)
            {
                return null;
            }

            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawn.LabelShortCap,
                // Initial persona is rolled once, biased toward the pawn's traits/backstory and
                // softly steered away from personas other colonists already use. The player can
                // still override it from the diary tab; that saved choice is never re-rolled.
                personaDefName = DiaryPersonas.WeightedStartingPersona(pawn, BuildUsedPersonaCounts(pawnId)).defName,
                diaryGenerationEnabled = true
            };
            diaries.Add(diary);
            return diary;
        }

        /// <summary>
        /// Counts how many current, living colonists already use each persona, so a new pawn's
        /// initial roll can softly avoid duplicates (see <see cref="DiaryPersonas.WeightedStartingPersona"/>).
        /// Keyed by persona defName. The pawn being created is excluded via <paramref name="excludePawnId"/>;
        /// dead/lost pawns and non-colonists are ignored so retired voices free up again.
        /// </summary>
        private Dictionary<string, int> BuildUsedPersonaCounts(string excludePawnId)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (diaries == null)
            {
                return counts;
            }

            // The set of pawn IDs that are colonists right now, so persona "use" reflects the
            // living colony rather than every record ever created.
            HashSet<string> colonistIds = new HashSet<string>();
            foreach (Pawn colonist in PawnsFinder.AllMaps_FreeColonists)
            {
                if (colonist != null)
                {
                    colonistIds.Add(colonist.GetUniqueLoadID());
                }
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || diary.pawnId == excludePawnId || string.IsNullOrWhiteSpace(diary.personaDefName))
                {
                    continue;
                }

                if (!colonistIds.Contains(diary.pawnId))
                {
                    continue;
                }

                counts.TryGetValue(diary.personaDefName, out int current);
                counts[diary.personaDefName] = current + 1;
            }

            return counts;
        }

        /// <summary>
        /// Looks up a diary record by pawn ID string (no creation).
        /// </summary>
        private PawnDiaryRecord FindDiaryByPawnId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaries == null)
            {
                return null;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                if (diaries[i].pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diaries[i]);
                    return diaries[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Migrates old saves: ensures a persona is assigned even if the Def was removed or never set.
        /// </summary>
        private static void EnsurePawnDiaryDefaults(PawnDiaryRecord diary)
        {
            if (diary == null)
            {
                return;
            }

            // Existing saves may have no persona, and XML mods may remove an old persona Def.
            if (string.IsNullOrWhiteSpace(diary.personaDefName) || DiaryPersonas.ForDefName(diary.personaDefName) == null)
            {
                diary.personaDefName = DiaryPersonas.Default.defName;
            }
        }

        /// <summary>
        /// Singleton empty list returned when a pawn has no diary entries, avoiding per-call allocations.
        /// </summary>
        private static class EmptyEntries
        {
            public static readonly IReadOnlyList<DiaryEntryView> List = new List<DiaryEntryView>();
        }

        /// <summary>
        /// Accumulates small-talk moments between a specific pair of pawns until the batch window
        /// expires or the max event count is reached, then produces a single merged DiaryEvent.
        /// </summary>
        private class PendingSmallTalkBatch
        {
            // Live Pawn references — only valid during the current game session (not saved).
            public Pawn initiator;
            public Pawn recipient;
            // Saved-safe IDs used when flushing after a Pawn reference may have become invalid.
            public string initiatorPawnId;
            public string recipientPawnId;
            // Tick range over which the batch accumulated lines.
            public int firstTick;
            public int lastTick;
            // The defName/label of the first interaction in the batch, used as the event label
            // when the batch contains only one entry.
            public string firstDefName;
            public string firstLabel;
            // The LLM instruction carried over from the first interaction in the batch.
            public string instruction;
            // Per-POV line accumulators — each small-talk moment appends one line per POV.
            public readonly List<string> initiatorLines = new List<string>();
            public readonly List<string> recipientLines = new List<string>();
            // RimWorld social-log ids represented by the eventual merged diary event.
            public readonly List<int> playLogEntryIds = new List<int>();

            /// <summary>Number of small-talk moments accumulated so far.</summary>
            public int Count
            {
                get
                {
                    return initiatorLines.Count;
                }
            }
        }
    }
}
