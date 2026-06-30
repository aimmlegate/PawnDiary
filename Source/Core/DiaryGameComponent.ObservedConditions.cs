// Observed conditions (Plan 12): the impure edge of the lasting-game-state system. Where event windows
// (DiaryGameComponent.EventWindows.cs) react to one-shot signals and guess a duration, this scans LIVE
// game state on an XML-owned interval, snapshots what it currently sees into plain
// ObservedConditionObservation DTOs, hands them to the pure ObservedConditionPolicy to diff against
// saved state, and then applies the returned plan (persist/forget rows, optionally record diary pages)
// and exposes prompt-bias candidates. Live reads happen ONLY here; all lifecycle decisions live in the
// pure policy. This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs.
//
// New to C#/RimWorld? See AGENTS.md. DLC-safety: every observer matches by plain string / vanilla API
// that simply finds nothing when the content is absent, so a no-DLC game runs cleanly.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Saved active observed conditions (the "diaryArchive"-style runtime list). Serialized in
        // DiaryGameComponent.cs ExposeData under "activeObservedConditions". Each row is one lasting
        // state currently believed active, so a reload mid-state does not lose or re-announce it.
        private List<ActiveObservedConditionState> activeObservedConditions = new List<ActiveObservedConditionState>();

        // How often (ticks) the component CHECKS which condition defs are due. Each def's own
        // pollIntervalTicks is the real cadence; this short gate just decides due-ness cheaply.
        private const int ObservedConditionScanIntervalTicks = 250;
        private int nextObservedConditionScanTick;

        // Transient (not saved) per-condition next-poll tick, keyed by conditionKey. Rebuilt each
        // session so a def's poll interval is honored without persisting scheduling state.
        private readonly Dictionary<string, int> nextObservedConditionPollTick = new Dictionary<string, int>();

        // Transient (not saved) dedup guard against a start/end page double-writing within a def's
        // dedup window. Reuses the shared RecentEventEntry store shape (see DiaryGameComponent.Lookup.cs).
        private readonly Dictionary<string, RecentEventEntry> recentObservedConditionEvents = new Dictionary<string, RecentEventEntry>();

        private const string ObservedConditionPhaseStart = "start";
        private const string ObservedConditionPhaseEnd = "end";

        // ---------------------------------------------------------------------------------------------
        // Scan entry point (Pass 3)
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// One scan pass: gather observations from the defs that are due now, diff them against saved
        /// state with the pure policy, and apply the plan. Only the due defs (and any orphaned saved
        /// rows whose def is gone) take part, so a def that is not due this pass leaves its conditions
        /// untouched — a key correctness point, since a non-polled def must not look "missing".
        /// </summary>
        private void ScanObservedConditions()
        {
            if (!CanRecordGameplayEventNow())
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<DiaryObservedConditionDef> allDefs = DefDatabase<DiaryObservedConditionDef>.AllDefsListForReading;

            // Index enabled defs by key, and pick the subset whose poll is due this pass.
            Dictionary<string, DiaryObservedConditionDef> enabledDefByKey =
                new Dictionary<string, DiaryObservedConditionDef>();
            Dictionary<string, DiaryObservedConditionDef> dueDefByKey =
                new Dictionary<string, DiaryObservedConditionDef>();
            List<ObservedConditionDefSnapshot> dueDefSnapshots = new List<ObservedConditionDefSnapshot>();
            List<ObservedConditionObservation> observations = new List<ObservedConditionObservation>();

            if (allDefs != null)
            {
                for (int i = 0; i < allDefs.Count; i++)
                {
                    DiaryObservedConditionDef def = allDefs[i];
                    if (def == null || !def.enabled)
                    {
                        continue;
                    }

                    string key = def.EffectiveConditionKey();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    enabledDefByKey[key] = def;

                    int nextPoll;
                    if (nextObservedConditionPollTick.TryGetValue(key, out nextPoll) && now < nextPoll)
                    {
                        continue; // not due yet — leave its saved conditions alone.
                    }

                    nextObservedConditionPollTick[key] = now + def.EffectivePollIntervalTicks();
                    dueDefByKey[key] = def;
                    dueDefSnapshots.Add(def.ToDefSnapshot());
                    CollectObservations(def, observations);
                }
            }

            // Working saved set: rows whose def is due (so they diff against this pass's observations),
            // plus rows whose def is gone/disabled (so the policy drops them with no end page).
            List<ObservedConditionStateSnapshot> workingStates = null;
            if (activeObservedConditions != null)
            {
                for (int i = 0; i < activeObservedConditions.Count; i++)
                {
                    ActiveObservedConditionState saved = activeObservedConditions[i];
                    if (saved == null || string.IsNullOrWhiteSpace(saved.conditionKey))
                    {
                        continue;
                    }

                    bool defDue = dueDefByKey.ContainsKey(saved.conditionKey);
                    bool defGone = !enabledDefByKey.ContainsKey(saved.conditionKey);
                    if (!defDue && !defGone)
                    {
                        continue; // def exists but is not due this pass — untouched.
                    }

                    if (workingStates == null)
                    {
                        workingStates = new List<ObservedConditionStateSnapshot>();
                    }

                    workingStates.Add(saved.ToSnapshot());
                }
            }

            if (dueDefSnapshots.Count == 0 && (workingStates == null || workingStates.Count == 0))
            {
                return; // nothing due and nothing to retire.
            }

            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(now, workingStates, observations, dueDefSnapshots);
            ApplyObservedConditionPlan(plan, dueDefByKey);
        }

        /// <summary>Dispatches one def to its observer, appending any current observations.</summary>
        private void CollectObservations(DiaryObservedConditionDef def, List<ObservedConditionObservation> observations)
        {
            switch (def.observerType)
            {
                case ObservedConditionObserverType.MapDanger:
                    CollectMapDangerObservations(def, observations);
                    break;
                case ObservedConditionObserverType.GameCondition:
                    CollectGameConditionObservations(def, observations);
                    break;
                case ObservedConditionObserverType.ThingPresent:
                    CollectThingPresentObservations(def, observations);
                    break;
                case ObservedConditionObserverType.PawnHediff:
                    CollectPawnHediffObservations(def, observations);
                    break;
                case ObservedConditionObserverType.RecentEvidence:
                    // No live feed yet (see DOCUMENTATION.md §5.1). Intentionally a no-op.
                    break;
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Observers
        // ---------------------------------------------------------------------------------------------

        // Pass 4: map danger. Active while a (home) map is materially dangerous — a broad, visible state
        // that hides nothing. Danger rating alone is enough by default; minHostileCount > 0 adds an
        // explicit spawned-hostile threshold.
        private void CollectMapDangerObservations(DiaryObservedConditionDef def,
            List<ObservedConditionObservation> observations)
        {
            StoryDanger minDanger = ParseDanger(def.minDangerRating);
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (!MapEligible(def, map))
                {
                    continue;
                }

                StoryDanger danger = map.dangerWatcher != null ? map.dangerWatcher.DangerRating : StoryDanger.None;
                bool dangerActive = danger != StoryDanger.None && (int)danger >= (int)minDanger;
                bool hostileActive = false;
                if (def.minHostileCount > 0)
                {
                    int hostiles = CountSpawnedHostiles(map, def.minHostileCount);
                    hostileActive = hostiles >= def.minHostileCount;
                }

                if (dangerActive || hostileActive)
                {
                    observations.Add(NewObservation(def, map.uniqueID, null, string.Empty, string.Empty, 0));
                }
            }
        }

        // Pass 5: game conditions. The game owns start/end truth; we just read ActiveConditions and
        // match the exact condition defName (with optional substring/label fallbacks).
        private void CollectGameConditionObservations(DiaryObservedConditionDef def,
            List<ObservedConditionObservation> observations)
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (!MapEligible(def, map) || map.gameConditionManager == null)
                {
                    continue;
                }

                List<GameCondition> conditions = map.gameConditionManager.ActiveConditions;
                if (conditions == null)
                {
                    continue;
                }

                for (int c = 0; c < conditions.Count; c++)
                {
                    GameCondition condition = conditions[c];
                    if (condition == null || condition.def == null)
                    {
                        continue;
                    }

                    string label = DiaryLineCleaner.CleanLine(condition.LabelCap);
                    if (!MatchesObservedConditionDef(def, condition.def.defName, label))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = DiaryLineCleaner.CleanLine(condition.def.LabelCap.Resolve());
                    }

                    observations.Add(NewObservation(def, map.uniqueID, null, condition.def.defName, label, 1));
                    break; // one observation per map: the condition is either present or not.
                }
            }
        }

        // Pass 6: observable evidence things/filth. Uses the indexed ThingsOfDef lookup (never a
        // full-map scan) so it is cheap even on a busy map. Describes only what is physically present.
        private void CollectThingPresentObservations(DiaryObservedConditionDef def,
            List<ObservedConditionObservation> observations)
        {
            if (def.matchDefNames == null || def.matchDefNames.Count == 0)
            {
                return;
            }

            int maxLabels = Math.Max(0, def.maxEvidenceLabels);
            int maxChars = Math.Max(0, def.maxEvidenceChars);
            int maxCount = Math.Max(1, def.maxEvidenceCount);

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (!MapEligible(def, map) || map.listerThings == null)
                {
                    continue;
                }

                int total = 0;
                string firstDefName = string.Empty;
                List<string> labels = new List<string>();
                for (int n = 0; n < def.matchDefNames.Count; n++)
                {
                    string name = def.matchDefNames[n];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    // GetNamedSilentFail keeps absent DLC/mod content safe: it returns null, we skip.
                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(name.Trim());
                    if (thingDef == null)
                    {
                        continue;
                    }

                    List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
                    if (things == null || things.Count == 0)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(firstDefName))
                    {
                        firstDefName = thingDef.defName;
                    }

                    total += things.Count;
                    if (total > maxCount)
                    {
                        total = maxCount;
                    }

                    if (labels.Count < maxLabels)
                    {
                        string singular = DiaryLineCleaner.CleanLine(thingDef.LabelCap.Resolve());
                        if (!string.IsNullOrWhiteSpace(singular))
                        {
                            labels.Add(things.Count > 1 ? singular + " x" + things.Count : singular);
                        }
                    }
                }

                if (total <= 0)
                {
                    continue;
                }

                string evidenceLabel = Truncate(string.Join(", ", labels.ToArray()), maxChars);
                observations.Add(NewObservation(def, map.uniqueID, null, firstDefName, evidenceLabel, total));
            }
        }

        // Pass 7: pawn-scoped visible hediffs. Only VISIBLE hediffs are matched, so a hidden/undiscovered
        // condition is never surfaced — this is how the observer respects "do not reveal hidden mechanics".
        private void CollectPawnHediffObservations(DiaryObservedConditionDef def,
            List<ObservedConditionObservation> observations)
        {
            // Skip the (relatively expensive) pawn/hediff scan when no matcher could ever fire. The Def
            // initializes these as non-null empty lists, so check Count as well as null.
            if (IsMatcherListEmpty(def.matchDefNames)
                && IsMatcherListEmpty(def.matchDefNameContains)
                && IsMatcherListEmpty(def.matchLabels))
            {
                return;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (!MapEligible(def, map) || map.mapPawns == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                if (colonists == null)
                {
                    continue;
                }

                for (int p = 0; p < colonists.Count; p++)
                {
                    Pawn pawn = colonists[p];
                    if (!IsHumanlike(pawn) || pawn.health == null || pawn.health.hediffSet == null)
                    {
                        continue;
                    }

                    List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                    if (hediffs == null)
                    {
                        continue;
                    }

                    for (int h = 0; h < hediffs.Count; h++)
                    {
                        Hediff hediff = hediffs[h];
                        if (hediff == null || hediff.def == null || !hediff.Visible)
                        {
                            continue;
                        }

                        string label = DiaryLineCleaner.CleanLine(hediff.LabelCap);
                        if (!MatchesObservedConditionDef(def, hediff.def.defName, label))
                        {
                            continue;
                        }

                        observations.Add(NewObservation(def, -1, pawn.GetUniqueLoadID(),
                            hediff.def.defName, label, 1));
                        break; // one observation per pawn.
                    }
                }
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Apply the plan
        // ---------------------------------------------------------------------------------------------

        private void ApplyObservedConditionPlan(ObservedConditionPlan plan,
            Dictionary<string, DiaryObservedConditionDef> dueDefByKey)
        {
            if (plan == null || plan.decisions == null)
            {
                return;
            }

            if (activeObservedConditions == null)
            {
                activeObservedConditions = new List<ActiveObservedConditionState>();
            }

            for (int i = 0; i < plan.decisions.Count; i++)
            {
                ObservedConditionDecision decision = plan.decisions[i];
                if (decision == null || decision.state == null)
                {
                    continue;
                }

                // Record the page BEFORE mutating saved state, and let the recorder tell us whether the
                // transition was satisfied. Only a satisfied recording justifies flipping the start/end
                // "recorded" flags or dropping the row: if no eligible pawn was available, we keep the
                // state retryable (startRecorded/endRecorded left false, row retained) so the next scan
                // re-enters the same transition instead of permanently losing the page.
                bool pageSatisfied = true;
                if (decision.recordPage)
                {
                    DiaryObservedConditionDef def;
                    if (dueDefByKey.TryGetValue(decision.state.conditionKey, out def) && def != null)
                    {
                        pageSatisfied = RecordObservedConditionPage(decision, def);
                    }
                }

                ActiveObservedConditionState row = FindObservedConditionRow(decision.state);
                if (decision.removeState)
                {
                    if (pageSatisfied)
                    {
                        if (row != null)
                        {
                            activeObservedConditions.Remove(row);
                        }
                    }
                    else if (row != null)
                    {
                        // End page could not be written yet: keep the row so the end retries next scan,
                        // persisting missing-since progress but leaving endRecorded false.
                        ObservedConditionStateSnapshot retryable = decision.state.Clone();
                        retryable.endRecorded = false;
                        row.CopyFrom(retryable);
                    }
                }
                else if (row != null)
                {
                    if (pageSatisfied)
                    {
                        row.CopyFrom(decision.state);
                    }
                    else
                    {
                        // Start page could not be written yet: persist tick/evidence progress but leave
                        // startRecorded false so the next scan re-enters StartRecorded.
                        ObservedConditionStateSnapshot retryable = decision.state.Clone();
                        retryable.startRecorded = false;
                        row.CopyFrom(retryable);
                    }
                }
                else
                {
                    ObservedConditionStateSnapshot toAdd = decision.state;
                    if (!pageSatisfied)
                    {
                        toAdd = decision.state.Clone();
                        toAdd.startRecorded = false;
                        toAdd.endRecorded = false;
                    }

                    activeObservedConditions.Add(ActiveObservedConditionState.FromSnapshot(toAdd));
                }
            }
        }

        // Records the optional start/end diary page for a condition transition, gated by the def's
        // recordStartEvent / recordEndEvent flag and deduped per phase/map/subject. Returns true when the
        // transition is SATISFIED (a page was written, or the def does not record this phase, or it was
        // already deduped) and false when a page was wanted but could not be written (no eligible pawn),
        // so the caller can keep the saved state retryable instead of permanently losing the page.
        private bool RecordObservedConditionPage(ObservedConditionDecision decision, DiaryObservedConditionDef def)
        {
            bool isStart = decision.kind == ObservedConditionDecisionKind.StartRecorded;
            if (isStart && !def.recordStartEvent)
            {
                return true; // def does not record starts: nothing wanted, so the transition is satisfied.
            }

            if (!isStart && !def.recordEndEvent)
            {
                return true; // def does not record ends: nothing wanted, so the transition is satisfied.
            }

            string phase = isStart ? ObservedConditionPhaseStart : ObservedConditionPhaseEnd;
            string dedupKey = def.defName + "|" + phase + "|" + decision.state.mapUniqueId + "|"
                + decision.state.subjectPawnId;

            // Check dedup WITHOUT marking: a prior call within the window means a page was already
            // written, so the transition is satisfied. We only consume the window below, after at least
            // one page is actually written, so a failed attempt can retry without being suppressed.
            if (IsRecentlyRecorded(recentObservedConditionEvents, dedupKey, def.EffectiveDedupTicks()))
            {
                return true;
            }

            List<Pawn> pawns = ObservedConditionPawns(def, decision.state);
            if (pawns.Count == 0)
            {
                return false; // retryable: no recipient right now.
            }

            string label = DiaryLineCleaner.CleanLine(def.LabelCap.Resolve());
            string instruction = ObservedConditionInstruction(def, label);
            string textKey = ObservedConditionTextKey(def, phase);
            string signalLabel = ObservedConditionSignalLabel(def, decision.state, label);
            string gameContext = BuildObservedConditionGameContext(def, decision.state, phase);

            bool wroteAny = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !IsDiaryEligible(pawn))
                {
                    continue;
                }

                string text = textKey.Translate(pawn.LabelShortCap, signalLabel).Resolve();
                DiaryEvent diaryEvent = AddSoloEvent(pawn, null, def.defName, label, text, instruction, gameContext);
                if (diaryEvent == null)
                {
                    continue;
                }

                wroteAny = true;
                if (!string.IsNullOrWhiteSpace(def.colorCue))
                {
                    diaryEvent.colorCue = def.colorCue;
                }

                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            }

            // Only consume the dedup window once at least one page is actually written.
            if (wroteAny)
            {
                MarkRecentlyRecorded(recentObservedConditionEvents, dedupKey, def.EffectiveDedupTicks());
            }

            return wroteAny;
        }

        // ---------------------------------------------------------------------------------------------
        // Prompt biasing (the unblocked half of "Prompt integration")
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Mirrors <see cref="ActiveEventWindowPromptCandidates"/>: returns the active observed
        /// conditions that should bias this pawn's prompt, plus a normal-context weight multiplier so a
        /// condition can suppress ordinary health/mood context (e.g. gray-flesh suspicion overriding it).
        /// Map conditions apply only to pawns on that map; Pawn conditions only to the subject; Colony
        /// conditions to everyone. A condition still inside its start debounce, or already ended, is not
        /// a candidate.
        /// </summary>
        private List<PromptEnchantmentCandidate> ActiveObservedConditionPromptCandidates(Pawn pawn,
            out float normalCandidateWeightMultiplier)
        {
            normalCandidateWeightMultiplier = 1f;
            List<PromptEnchantmentCandidate> candidates = new List<PromptEnchantmentCandidate>();
            if (pawn == null || activeObservedConditions == null || activeObservedConditions.Count == 0)
            {
                return candidates;
            }

            int pawnMapUniqueId = MapUniqueId(pawn.Map);
            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < activeObservedConditions.Count; i++)
            {
                ActiveObservedConditionState active = activeObservedConditions[i];
                if (active == null || !active.startRecorded || active.endRecorded)
                {
                    continue; // not yet started (debounce) or already ended.
                }

                DiaryObservedConditionDef def = ObservedConditionDefFor(active);
                if (def == null || !def.enabled || !def.promptEnabled)
                {
                    continue;
                }

                if (!ObservedConditionAppliesToPawn(def, active, pawnMapUniqueId, pawnId))
                {
                    continue;
                }

                normalCandidateWeightMultiplier *= SafePromptWeightMultiplier(def.normalPromptWeightMultiplier);
                PromptEnchantmentCandidate candidate = PromptCandidateForObservedCondition(def, active);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private static bool ObservedConditionAppliesToPawn(DiaryObservedConditionDef def,
            ActiveObservedConditionState active, int pawnMapUniqueId, string pawnId)
        {
            switch (def.scope)
            {
                case ObservedConditionScope.Pawn:
                    return string.Equals(active.subjectPawnId, pawnId, StringComparison.Ordinal);
                case ObservedConditionScope.Map:
                    return active.mapUniqueId < 0 || active.mapUniqueId == pawnMapUniqueId;
                default:
                    return true; // Colony: applies to everyone.
            }
        }

        private PromptEnchantmentCandidate PromptCandidateForObservedCondition(DiaryObservedConditionDef def,
            ActiveObservedConditionState active)
        {
            float weight = SafePromptWeight(def.promptWeight);
            if (weight <= 0f)
            {
                return null;
            }

            List<string> cues = new List<string>();
            if (!string.IsNullOrWhiteSpace(def.promptDescriptionKey))
            {
                string description = def.promptDescriptionKey.Translate().Resolve();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    cues.Add("PawnDiary.Prompt.ObservedCondition.Detail".Translate(description).Resolve());
                }
            }

            // Fold in the live observable evidence (e.g. "gray flesh sample x2") so the prompt reflects
            // what is actually present right now, not just static XML wording.
            string evidence = DiaryLineCleaner.CleanLine(active.lastSeenEvidenceLabel);
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                cues.Add("PawnDiary.Prompt.ObservedCondition.Detail".Translate(evidence).Resolve());
            }

            if (def.promptCueKeys != null)
            {
                for (int i = 0; i < def.promptCueKeys.Count; i++)
                {
                    string cueKey = def.promptCueKeys[i];
                    if (string.IsNullOrWhiteSpace(cueKey))
                    {
                        continue;
                    }

                    string cue = cueKey.Translate().Resolve();
                    if (!string.IsNullOrWhiteSpace(cue))
                    {
                        cues.Add(cue);
                    }
                }
            }

            string label = DiaryLineCleaner.CleanLine(def.LabelCap.Resolve());
            return new PromptEnchantmentCandidate
            {
                weight = weight,
                // EventWindowPromptText is the shared "preferred key or fallback key" translator.
                priorityText = EventWindowPromptText(def.promptPriorityKey,
                    "PawnDiary.Prompt.ObservedCondition.Priority"),
                conditionText = EventWindowPromptText(def.promptConditionKey,
                    "PawnDiary.Prompt.ObservedCondition.ConditionFallback", label),
                configuredCues = cues
            };
        }

        // ---------------------------------------------------------------------------------------------
        // Page helpers
        // ---------------------------------------------------------------------------------------------

        private List<Pawn> ObservedConditionPawns(DiaryObservedConditionDef def, ObservedConditionStateSnapshot state)
        {
            List<Pawn> pawns = new List<Pawn>();
            if (def.recordScope == ObservedConditionRecordScope.SubjectPawn)
            {
                Pawn pawn = ObservedConditionSubjectPawn(state.subjectPawnId);
                if (pawn != null)
                {
                    pawns.Add(pawn);
                }

                return pawns;
            }

            HashSet<string> seenPawnIds = new HashSet<string>();
            Map map = MapForUniqueId(state.mapUniqueId);
            if (map != null)
            {
                AddEventWindowPawnsFromMap(map, pawns, seenPawnIds);
                return pawns;
            }

            // Map id unknown (-1, colony scope, or the map is gone): fall back to all current maps.
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                AddEventWindowPawnsFromMap(maps[i], pawns, seenPawnIds);
            }

            return pawns;
        }

        private static Pawn ObservedConditionSubjectPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Pawn pawn = EventWindowSubjectPawnFromMap(maps[i], pawnId);
                if (pawn != null)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static string ObservedConditionInstruction(DiaryObservedConditionDef def, string label)
        {
            if (!string.IsNullOrWhiteSpace(def.instruction))
            {
                return def.instruction;
            }

            return "PawnDiary.Event.ObservedCondition.Generic.Instruction".Translate(label).Resolve();
        }

        private static string ObservedConditionTextKey(DiaryObservedConditionDef def, string phase)
        {
            if (string.Equals(phase, ObservedConditionPhaseStart, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(def.startTextKey))
            {
                return def.startTextKey;
            }

            if (string.Equals(phase, ObservedConditionPhaseEnd, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(def.endTextKey))
            {
                return def.endTextKey;
            }

            return "PawnDiary.Event.ObservedCondition.Generic." + phase;
        }

        private static string ObservedConditionSignalLabel(DiaryObservedConditionDef def,
            ObservedConditionStateSnapshot state, string label)
        {
            string evidence = DiaryLineCleaner.CleanLine(state.lastSeenEvidenceLabel);
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                return evidence;
            }

            return string.IsNullOrWhiteSpace(label)
                ? DiaryLineCleaner.CleanLine(def.LabelCap.Resolve())
                : label;
        }

        private static string BuildObservedConditionGameContext(DiaryObservedConditionDef def,
            ObservedConditionStateSnapshot state, string phase)
        {
            // Reuses the event-window context-part helpers (SafeContextValue / AddContextPart) so the
            // gameContext string format stays consistent across both systems.
            List<string> parts = new List<string>
            {
                "observed_condition=" + SafeContextValue(def.EffectiveConditionKey()),
                "phase=" + SafeContextValue(phase),
                "condition=" + SafeContextValue(DiaryLineCleaner.CleanLine(def.LabelCap.Resolve()))
            };

            AddContextPart(parts, "evidence", state.lastSeenEvidenceLabel);
            if (state.lastSeenEvidenceCount > 0)
            {
                AddContextPart(parts, "evidence_count", state.lastSeenEvidenceCount.ToString());
            }

            AddContextPart(parts, "subject_id", state.subjectPawnId);
            return string.Join("; ", parts.ToArray());
        }

        // ---------------------------------------------------------------------------------------------
        // Small shared helpers
        // ---------------------------------------------------------------------------------------------

        private static ObservedConditionObservation NewObservation(DiaryObservedConditionDef def,
            int mapUniqueId, string subjectPawnId, string evidenceDefName, string evidenceLabel, int evidenceCount)
        {
            return new ObservedConditionObservation
            {
                conditionDefName = def.defName,
                conditionKey = def.EffectiveConditionKey(),
                scope = def.scope,
                // Normalize identity by scope: Map keeps the map id; Pawn keys on the subject; Colony
                // collapses across maps. This must match ObservedConditionStateSnapshot.Identity.
                mapUniqueId = def.scope == ObservedConditionScope.Map ? mapUniqueId : -1,
                subjectPawnId = def.scope == ObservedConditionScope.Pawn ? (subjectPawnId ?? string.Empty) : string.Empty,
                evidenceDefName = evidenceDefName ?? string.Empty,
                evidenceLabel = evidenceLabel ?? string.Empty,
                evidenceCount = evidenceCount
            };
        }

        private static bool MapEligible(DiaryObservedConditionDef def, Map map)
        {
            if (map == null)
            {
                return false;
            }

            if (def.includeHomeMapsOnly)
            {
                return map.IsPlayerHome;
            }

            return map.IsPlayerHome || def.includeNonPlayerMaps;
        }

        private static int CountSpawnedHostiles(Map map, int cap)
        {
            if (map.mapPawns == null)
            {
                return 0;
            }

            // AllPawnsSpawned is exposed as IReadOnlyList; we only index + count, so no copy is needed.
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            if (pawns == null)
            {
                return 0;
            }

            Faction player = Faction.OfPlayer;
            int count = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || pawn.Downed)
                {
                    continue;
                }

                if (player != null && pawn.HostileTo(player))
                {
                    count++;
                    if (count >= cap)
                    {
                        break; // we only need to know the threshold is met.
                    }
                }
            }

            return count;
        }

        private static StoryDanger ParseDanger(string value)
        {
            StoryDanger danger;
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out danger))
            {
                return danger;
            }

            return StoryDanger.Low;
        }

        // Plain string matching shared by the condition/thing/hediff observers. Exact defName first
        // (preferred, DLC-safe), then optional substring fallbacks over defName, then over label.
        private static bool MatchesObservedConditionDef(DiaryObservedConditionDef def, string defName, string label)
        {
            if (ContainsExact(def.matchDefNames, defName))
            {
                return true;
            }

            if (ContainsSubstring(def.matchDefNameContains, defName))
            {
                return true;
            }

            return ContainsSubstring(def.matchLabels, label);
        }

        // True when a matcher list has no entries. The Def initializes these as non-null empty lists, so
        // observers must check Count (not just null) before deciding a scan is worthwhile.
        private static bool IsMatcherListEmpty(List<string> values)
        {
            return values == null || values.Count == 0;
        }

        private static bool ContainsExact(List<string> values, string actual)
        {
            if (values == null || string.IsNullOrWhiteSpace(actual))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && string.Equals(values[i].Trim(), actual, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSubstring(List<string> tokens, string haystack)
        {
            if (tokens == null || string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (!string.IsNullOrWhiteSpace(token)
                    && haystack.IndexOf(token.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || maxChars <= 0 || value.Length <= maxChars)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxChars);
        }

        private ActiveObservedConditionState FindObservedConditionRow(ObservedConditionStateSnapshot snapshot)
        {
            if (activeObservedConditions == null || snapshot == null)
            {
                return null;
            }

            string identity = snapshot.IdentityKey();
            for (int i = 0; i < activeObservedConditions.Count; i++)
            {
                ActiveObservedConditionState row = activeObservedConditions[i];
                if (row != null && ActiveObservedConditionIdentity(row) == identity)
                {
                    return row;
                }
            }

            return null;
        }

        private static string ActiveObservedConditionIdentity(ActiveObservedConditionState row)
        {
            return ObservedConditionStateSnapshot.Identity(
                row.conditionKey, row.scope, row.mapUniqueId, row.subjectPawnId);
        }

        private static DiaryObservedConditionDef ObservedConditionDefFor(ActiveObservedConditionState active)
        {
            if (active == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(active.conditionDefName))
            {
                DiaryObservedConditionDef byDefName =
                    DefDatabase<DiaryObservedConditionDef>.GetNamedSilentFail(active.conditionDefName);
                if (byDefName != null)
                {
                    return byDefName;
                }
            }

            List<DiaryObservedConditionDef> defs = DefDatabase<DiaryObservedConditionDef>.AllDefsListForReading;
            if (defs == null)
            {
                return null;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryObservedConditionDef def = defs[i];
                if (def != null
                    && string.Equals(def.EffectiveConditionKey(), active.conditionKey, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }

            return null;
        }

        /// <summary>
        /// Post-load repair for saved observed conditions: drop nulls/keyless rows, null-coalesce
        /// strings, and collapse duplicate identities so the runtime list is clean before the first scan.
        /// </summary>
        private void NormalizeActiveObservedConditions()
        {
            if (activeObservedConditions == null)
            {
                activeObservedConditions = new List<ActiveObservedConditionState>();
                return;
            }

            HashSet<string> seenIdentities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = activeObservedConditions.Count - 1; i >= 0; i--)
            {
                ActiveObservedConditionState active = activeObservedConditions[i];
                if (active == null)
                {
                    activeObservedConditions.RemoveAt(i);
                    continue;
                }

                active.NormalizeOnLoad();
                if (string.IsNullOrWhiteSpace(active.conditionKey))
                {
                    activeObservedConditions.RemoveAt(i);
                    continue;
                }

                if (!seenIdentities.Add(ActiveObservedConditionIdentity(active)))
                {
                    activeObservedConditions.RemoveAt(i); // duplicate identity — keep one.
                }
            }
        }
    }
}
