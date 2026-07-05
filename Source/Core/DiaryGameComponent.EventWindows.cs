// XML event windows: a generic bridge from neutral game signals to long-lived prompt context.
// Signals come from broad hooks (incidents, quests, health changes, spawned things, letters,
// proximity letters, and special story objects). XML decides which signals matter, what diary
// entries they create, how long the window lasts, and how strongly it biases prompt enchantments
// while active.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string EventWindowSourceIncident = "Incident";
        // internal: the QuestFanoutSignal emits the quest lifecycle event-window signal at capture.
        internal const string EventWindowSourceQuest = "Quest";
        private const string EventWindowSourceThingSpawned = "ThingSpawned";
        private const string EventWindowSourceLetter = "Letter";
        private const string EventWindowSourceProximityLetter = "ProximityLetter";
        private const string EventWindowSourceVoidMonolith = "VoidMonolith";
        private const string EventWindowSourcePawnAge = "PawnAge";
        private const string EventWindowSourceHediff = "Hediff";
        private const string EventWindowSourcePrisonBreak = "PrisonBreak";
        private const string EventWindowSignalExecuted = "executed";
        private const string EventWindowSignalSpawned = "spawned";
        private const string EventWindowSignalReceived = "received";
        private const string EventWindowSignalActivated = "activated";
        private const string EventWindowSignalBirthday = "birthday";
        private const string EventWindowSignalAdded = "added";
        private const string EventWindowSignalStarted = "started";
        private const string EventWindowPhaseStart = "start";
        private const string EventWindowPhaseEnd = "end";
        private const string EventWindowPhaseTimeout = "timeout";

        /// <summary>
        /// Generic signal entry point used by Harmony patches and existing recorders.
        /// </summary>
        internal void RecordEventWindowSignal(string source, string defName, string signal, string label,
            Map map = null, Pawn subjectPawn = null)
        {
            if (!CanRecordGameplayEventNow() || string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            List<DiaryEventWindowDef> defs = DefDatabase<DiaryEventWindowDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                return;
            }

            Map signalMap = map ?? MapForSignalSubject(subjectPawn);
            string subjectLabel = DiaryLineCleaner.CleanLine(subjectPawn == null ? null : subjectPawn.LabelShortCap);
            EventWindowSignalFacts facts = new EventWindowSignalFacts
            {
                source = source,
                defName = defName ?? string.Empty,
                signal = signal ?? string.Empty,
                label = DiaryLineCleaner.CleanLine(label),
                subjectPawnId = subjectPawn == null ? string.Empty : subjectPawn.GetUniqueLoadID(),
                subjectLabel = subjectLabel ?? string.Empty
            };

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryEventWindowDef def = defs[i];
                if (def == null || !def.enabled)
                {
                    continue;
                }

                if (EventWindowPolicy.MatchesAny(def.EndRules(), facts))
                {
                    ProcessEventWindowEnd(def, facts, signalMap, subjectPawn);
                }

                if (EventWindowPolicy.MatchesAny(def.StartRules(), facts))
                {
                    ProcessEventWindowStart(def, facts, signalMap, subjectPawn);
                }
            }
        }

        /// <summary>
        /// Convenience wrapper for successful IncidentWorker.TryExecute signals.
        /// </summary>
        internal void RecordEventWindowIncident(IncidentDef incidentDef, IncidentParms parms)
        {
            if (incidentDef == null)
            {
                return;
            }

            Map map = parms == null ? null : parms.target as Map;
            RecordEventWindowSignal(
                EventWindowSourceIncident,
                incidentDef.defName,
                EventWindowSignalExecuted,
                DiaryLineCleaner.CleanLine(incidentDef.LabelCap.Resolve()),
                map);
        }

        /// <summary>
        /// Convenience wrapper for Thing.SpawnSetup signals.
        /// </summary>
        internal void RecordEventWindowThingSpawned(Thing thing, Map map)
        {
            if (thing == null || thing.def == null)
            {
                return;
            }

            // Thing.SpawnSetup is one of the hottest paths in the game (every projectile, filth, item,
            // and plant). Resolving LabelShortCap + cleaning it for every spawn is wasted work unless
            // some def could actually match this thing, so gate on the cheap pre-check first.
            if (!CanRecordGameplayEventNow()
                || !CouldMatchEventWindow(EventWindowSourceThingSpawned, thing.def.defName))
            {
                return;
            }

            string label = DiaryLineCleaner.CleanLine(thing.LabelShortCap);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryLineCleaner.CleanLine(thing.def.LabelCap.Resolve());
            }

            RecordEventWindowSignal(
                EventWindowSourceThingSpawned,
                thing.def.defName,
                EventWindowSignalSpawned,
                label,
                map);
        }

        /// <summary>
        /// Convenience wrapper for generic vanilla letters. XML matches the stable letter key.
        /// </summary>
        internal void RecordEventWindowLetter(string letterKey, string label, Pawn subjectPawn)
        {
            if (string.IsNullOrWhiteSpace(letterKey))
            {
                return;
            }

            RecordEventWindowSignal(
                EventWindowSourceLetter,
                letterKey,
                EventWindowSignalReceived,
                label,
                MapForSignalSubject(subjectPawn),
                subjectPawn);
        }

        /// <summary>
        /// Convenience wrapper for biological birthdays, keyed by a stable pseudo-def name.
        /// </summary>
        internal void RecordEventWindowBirthday(Pawn pawn, int birthdayAge)
        {
            if (pawn == null)
            {
                return;
            }

            RecordEventWindowSignal(
                EventWindowSourcePawnAge,
                "Birthday",
                EventWindowSignalBirthday,
                birthdayAge > 0 ? birthdayAge.ToString() : string.Empty,
                MapForSignalSubject(pawn),
                pawn);
        }

        /// <summary>
        /// Convenience wrapper for newly-added hediffs, keyed by HediffDef name.
        /// </summary>
        internal void RecordEventWindowHediffAdded(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null || hediff.def == null)
            {
                return;
            }

            // AddHediff fires for every colonist wound in combat; skip the label work unless a def
            // could match this hediff's defName.
            if (!CanRecordGameplayEventNow()
                || !CouldMatchEventWindow(EventWindowSourceHediff, hediff.def.defName))
            {
                return;
            }

            string label = DiaryLineCleaner.CleanLine(hediff.LabelCap);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryLineCleaner.CleanLine(hediff.def.LabelCap.Resolve());
            }

            RecordEventWindowSignal(
                EventWindowSourceHediff,
                hediff.def.defName,
                EventWindowSignalAdded,
                label,
                MapForSignalSubject(pawn),
                pawn);
        }

        /// <summary>
        /// Convenience wrapper for prisoner breakouts. Map-scoped XML rules decide who records it.
        /// </summary>
        internal void RecordEventWindowPrisonBreak(Pawn initiator, string label, List<Pawn> escapingPrisoners)
        {
            Map map = MapForSignalSubject(initiator) ?? FirstPawnMap(escapingPrisoners);
            string cleanedLabel = DiaryLineCleaner.CleanLine(label);
            if (string.IsNullOrWhiteSpace(cleanedLabel))
            {
                cleanedLabel = "PawnDiary.Event.EventWindow.PrisonBreak.SignalLabel".Translate().Resolve();
            }

            RecordEventWindowSignal(
                EventWindowSourcePrisonBreak,
                "PrisonBreak",
                EventWindowSignalStarted,
                cleanedLabel,
                map);
        }

        /// <summary>
        /// Convenience wrapper for ThingComp proximity letters, keyed by the parent ThingDef name.
        /// </summary>
        internal void RecordEventWindowProximityLetter(Thing thing, string label, Pawn subjectPawn)
        {
            if (thing == null || thing.def == null)
            {
                return;
            }

            string cleanedLabel = DiaryLineCleaner.CleanLine(label);
            if (string.IsNullOrWhiteSpace(cleanedLabel))
            {
                cleanedLabel = DiaryLineCleaner.CleanLine(thing.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(cleanedLabel))
            {
                cleanedLabel = DiaryLineCleaner.CleanLine(thing.def.LabelCap.Resolve());
            }

            RecordEventWindowSignal(
                EventWindowSourceProximityLetter,
                thing.def.defName,
                EventWindowSignalReceived,
                cleanedLabel,
                thing.Map,
                subjectPawn);
        }

        /// <summary>
        /// Convenience wrapper for completed void monolith activations, keyed by the reached level defName.
        /// </summary>
        internal void RecordEventWindowVoidMonolithActivation(Thing thing, string levelDefName, string label,
            Pawn subjectPawn)
        {
            if (thing == null || thing.def == null)
            {
                return;
            }

            string cleanedLabel = DiaryLineCleaner.CleanLine(label);
            if (string.IsNullOrWhiteSpace(cleanedLabel))
            {
                cleanedLabel = DiaryLineCleaner.CleanLine(thing.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(cleanedLabel))
            {
                cleanedLabel = DiaryLineCleaner.CleanLine(thing.def.LabelCap.Resolve());
            }

            RecordEventWindowSignal(
                EventWindowSourceVoidMonolith,
                string.IsNullOrWhiteSpace(levelDefName) ? thing.def.defName : levelDefName,
                EventWindowSignalActivated,
                cleanedLabel,
                thing.Map,
                subjectPawn);
        }

        private void ProcessEventWindowStart(DiaryEventWindowDef def, EventWindowSignalFacts facts, Map map,
            Pawn subjectPawn)
        {
            int mapUniqueId = MapUniqueId(map);
            ActiveEventWindowState active = def.keepActive ? ActiveEventWindowFor(def, mapUniqueId) : null;
            if (def.keepActive && active != null && !def.restartOnStart)
            {
                return;
            }

            string dedupKey = EventWindowDedupKey(def, EventWindowPhaseStart, facts, mapUniqueId);
            if (IsRecentlyRecorded(recentEventWindowEvents, dedupKey, def.EffectiveDedupTicks()))
            {
                return;
            }

            if (def.keepActive)
            {
                if (active == null)
                {
                    active = new ActiveEventWindowState();
                    if (activeEventWindows == null)
                    {
                        activeEventWindows = new List<ActiveEventWindowState>();
                    }

                    activeEventWindows.Add(active);
                }

                SnapshotEventWindowStart(active, def, facts, mapUniqueId);
            }

            MarkRecentlyRecorded(recentEventWindowEvents, dedupKey, def.EffectiveDedupTicks());

            if (def.recordStartEvent)
            {
                RecordEventWindowPhase(def, active, EventWindowPhaseStart, facts, map, subjectPawn);
            }
        }

        private void ProcessEventWindowEnd(DiaryEventWindowDef def, EventWindowSignalFacts facts, Map map,
            Pawn subjectPawn)
        {
            int mapUniqueId = MapUniqueId(map);
            ActiveEventWindowState active = ActiveEventWindowFor(def, mapUniqueId);
            if (active == null && !def.recordEndWithoutActive)
            {
                return;
            }

            string dedupKey = EventWindowDedupKey(def, EventWindowPhaseEnd, facts, mapUniqueId);
            if (IsRecentlyRecorded(recentEventWindowEvents, dedupKey, def.EffectiveDedupTicks()))
            {
                return;
            }

            if (active != null)
            {
                activeEventWindows.Remove(active);
            }

            MarkRecentlyRecorded(recentEventWindowEvents, dedupKey, def.EffectiveDedupTicks());
            if (def.recordEndEvent)
            {
                RecordEventWindowPhase(def, active, EventWindowPhaseEnd, facts, map, subjectPawn);
            }
        }

        private void ScanEventWindowTimeouts()
        {
            if (activeEventWindows == null || activeEventWindows.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            for (int i = activeEventWindows.Count - 1; i >= 0; i--)
            {
                ActiveEventWindowState active = activeEventWindows[i];
                if (active == null)
                {
                    activeEventWindows.RemoveAt(i);
                    continue;
                }

                DiaryEventWindowDef def = EventWindowDefFor(active);
                if (def == null || !def.enabled)
                {
                    activeEventWindows.RemoveAt(i);
                    continue;
                }

                // Still-present probe: an optional XML-declared check that closes the window EARLY when
                // its spawning threat is no longer on the window's map (see stillPresentThingDefNames /
                // stillPresentFactionDefNames). This keeps a timeout-bounded dread window (e.g.
                // MechClusterLanded) from coloring prompts for days after the cluster is destroyed. The
                // close is silent (no end page) — mirrors the def-disabled removal just above; a def that
                // wants a resolution page should declare endSignals instead.
                if (EventWindowHasProbe(def))
                {
                    Map probeMap = MapForUniqueId(active.mapUniqueId);
                    if (!EventWindowProbeSatisfied(def, probeMap))
                    {
                        activeEventWindows.RemoveAt(i);
                        continue;
                    }
                }

                if (active.expiresTick < 0 || now < active.expiresTick)
                {
                    continue;
                }

                activeEventWindows.RemoveAt(i);
                if (!def.recordTimeoutEvent)
                {
                    continue;
                }

                EventWindowSignalFacts facts = new EventWindowSignalFacts
                {
                    source = active.startSource,
                    signal = EventWindowPhaseTimeout,
                    defName = active.startDefName,
                    label = active.startLabel,
                    subjectPawnId = active.startSubjectPawnId,
                    subjectLabel = active.startSubjectLabel
                };
                RecordEventWindowPhase(def, active, EventWindowPhaseTimeout, facts,
                    MapForUniqueId(active.mapUniqueId), null);
            }
        }

        // True if the def declares any still-present probe (thing-def or faction list). Used to skip the
        // probe work entirely for the common case of a window with no probe configured.
        private static bool EventWindowHasProbe(DiaryEventWindowDef def)
        {
            return def != null
                && ((def.stillPresentThingDefNames != null && def.stillPresentThingDefNames.Count > 0)
                    || (def.stillPresentFactionDefNames != null && def.stillPresentFactionDefNames.Count > 0));
        }

        // Returns true if the window's spawning threat is still present on the map (so the window may
        // stay active); false when the threat is gone (so the timeout scan closes it early). A null map
        // (map gone, or a map-agnostic window with mapUniqueId<0) cannot confirm presence → not
        // satisfied. Empty/unconfigured probes are gated out by EventWindowHasProbe before this is
        // called, so reaching here means at least one matcher is configured.
        private static bool EventWindowProbeSatisfied(DiaryEventWindowDef def, Map map)
        {
            if (def == null || map == null)
            {
                return false;
            }

            // OR semantics: the threat is "still present" if ANY matcher fires. The thing-def arm reuses
            // the observed-conditions leaf helper (DLC-safe via GetNamedSilentFail).
            if (AnyThingDefPresentOnMap(map, def.stillPresentThingDefNames))
            {
                return true;
            }

            return AnyFactionPawnPresentOnMap(map, def.stillPresentFactionDefNames);
        }

        // True if any spawned pawn of a listed faction defName is on the map. Cheap (a pawn-count walk,
        // not a thing scan) and DLC-safe: faction is matched by plain string, so a listed faction that
        // does not exist in the player's mod set simply never matches. Used by the mech-cluster probe
        // ("Mechanoid"), which is stable across all DLCs because every mechanoid belongs to that one
        // base-game FactionDef.
        private static bool AnyFactionPawnPresentOnMap(Map map, List<string> factionDefNames)
        {
            if (map == null || map.mapPawns == null || factionDefNames == null || factionDefNames.Count == 0)
            {
                return false;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            if (pawns == null || pawns.Count == 0)
            {
                return false;
            }

            // Two-pointer scan: pawns are typically few-to-tens, faction list is one or two entries, so a
            // nested linear compare is cheaper than building a HashSet.
            for (int p = 0; p < pawns.Count; p++)
            {
                Pawn pawn = pawns[p];
                if (pawn == null)
                {
                    continue;
                }

                string pawnFactionDef = pawn.Faction?.def?.defName;
                if (string.IsNullOrEmpty(pawnFactionDef))
                {
                    continue;
                }

                for (int f = 0; f < factionDefNames.Count; f++)
                {
                    if (string.Equals(pawnFactionDef, factionDefNames[f], System.StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private List<PromptEnchantmentCandidate> ActiveEventWindowPromptCandidates(Pawn pawn,
            out float normalCandidateWeightMultiplier)
        {
            normalCandidateWeightMultiplier = 1f;
            List<PromptEnchantmentCandidate> candidates = new List<PromptEnchantmentCandidate>();
            if (pawn == null || activeEventWindows == null || activeEventWindows.Count == 0)
            {
                return candidates;
            }

            int pawnMapUniqueId = MapUniqueId(pawn.Map);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < activeEventWindows.Count; i++)
            {
                ActiveEventWindowState active = activeEventWindows[i];
                if (active == null || (active.mapUniqueId >= 0 && active.mapUniqueId != pawnMapUniqueId))
                {
                    continue;
                }

                DiaryEventWindowDef def = EventWindowDefFor(active);
                if (def == null || !def.enabled || !def.promptEnabled)
                {
                    continue;
                }

                if (def.recordScope == EventWindowRecordScope.SubjectPawn
                    && !string.Equals(active.startSubjectPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal))
                {
                    continue;
                }

                float ageFactor = PromptEnchantmentDecayPolicy.AgeFactor(
                    now, active.startedTick, def.promptDecayTicks, def.promptDecayMinMultiplier);
                normalCandidateWeightMultiplier *= PromptEnchantmentDecayPolicy.RelaxedNormalMultiplier(
                    SafePromptWeightMultiplier(def.normalPromptWeightMultiplier), ageFactor);
                PromptEnchantmentCandidate candidate = PromptCandidateForEventWindow(def, ageFactor);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private PromptEnchantmentCandidate PromptCandidateForEventWindow(DiaryEventWindowDef def, float ageFactor)
        {
            float weight = PromptEnchantmentDecayPolicy.DecayedWeight(SafePromptWeight(def.promptWeight), ageFactor);
            if (weight <= 0f)
            {
                return null;
            }

            List<string> cues = new List<string>();
            string description = EventWindowPromptText(def.promptDescriptionText, def.promptDescriptionKey, null);
            if (!string.IsNullOrWhiteSpace(description))
            {
                cues.Add("PawnDiary.Prompt.EventWindow.Detail".Translate(description).Resolve());
            }

            AddPromptCueTexts(cues, def.promptCueTexts, def.promptCueKeys);

            string label = DiaryLineCleaner.CleanLine(def.LabelCap.Resolve());
            return new PromptEnchantmentCandidate
            {
                weight = weight,
                priorityText = EventWindowPromptText(def.promptPriorityText, def.promptPriorityKey,
                    "PawnDiary.Prompt.EventWindow.Priority"),
                conditionText = EventWindowPromptText(def.promptConditionText, def.promptConditionKey,
                    "PawnDiary.Prompt.EventWindow.ConditionFallback", label),
                configuredCues = cues
            };
        }

        private void RecordEventWindowPhase(DiaryEventWindowDef def, ActiveEventWindowState active,
            string phase, EventWindowSignalFacts facts, Map map, Pawn subjectPawn)
        {
            List<Pawn> pawns = EventWindowPawns(def, active, facts, map, subjectPawn);
            if (pawns.Count == 0)
            {
                return;
            }

            string label = DiaryLineCleaner.CleanLine(def.LabelCap.Resolve());
            string instruction = EventWindowInstruction(def, label);
            string signalLabel = EventWindowSignalLabel(def, active, facts);
            string gameContext = BuildEventWindowGameContext(def, active, phase, facts);

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !IsDiaryEligible(pawn))
                {
                    continue;
                }

                string text = EventWindowPhaseText(def, phase, pawn.LabelShortCap, signalLabel);
                DiaryEvent diaryEvent = AddSoloEvent(pawn, null, def.defName, label, text, instruction, gameContext);
                if (diaryEvent == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(def.colorCue))
                {
                    diaryEvent.colorCue = def.colorCue;
                }

                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            }
        }

        private List<Pawn> EventWindowPawns(DiaryEventWindowDef def, ActiveEventWindowState active,
            EventWindowSignalFacts facts, Map map, Pawn subjectPawn)
        {
            List<Pawn> pawns = new List<Pawn>();
            HashSet<string> seenPawnIds = new HashSet<string>();
            if (def != null && def.recordScope == EventWindowRecordScope.SubjectPawn)
            {
                Pawn pawn = EventWindowSubjectPawn(active, facts, map, subjectPawn);
                if (pawn != null)
                {
                    pawns.Add(pawn);
                }

                return pawns;
            }

            if (map != null)
            {
                AddEventWindowPawnsFromMap(map, pawns, seenPawnIds);
                return pawns;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                AddEventWindowPawnsFromMap(maps[i], pawns, seenPawnIds);
            }

            return pawns;
        }

        private Pawn EventWindowSubjectPawn(ActiveEventWindowState active, EventWindowSignalFacts facts,
            Map map, Pawn subjectPawn)
        {
            if (subjectPawn != null)
            {
                return subjectPawn;
            }

            string pawnId = facts == null ? null : facts.subjectPawnId;
            if (string.IsNullOrWhiteSpace(pawnId) && active != null)
            {
                pawnId = active.startSubjectPawnId;
            }

            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            Pawn pawn = EventWindowSubjectPawnFromMap(map, pawnId);
            if (pawn != null)
            {
                return pawn;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                pawn = EventWindowSubjectPawnFromMap(maps[i], pawnId);
                if (pawn != null)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static Pawn EventWindowSubjectPawnFromMap(Map map, string pawnId)
        {
            if (map == null || map.mapPawns == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn != null && string.Equals(pawn.GetUniqueLoadID(), pawnId, StringComparison.Ordinal))
                {
                    return pawn;
                }
            }

            return null;
        }

        private void AddEventWindowPawnsFromMap(Map map, List<Pawn> pawns, HashSet<string> seenPawnIds)
        {
            if (map == null || map.mapPawns == null)
            {
                return;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn == null)
                {
                    continue;
                }

                string pawnId = pawn.GetUniqueLoadID();
                if (seenPawnIds.Add(pawnId))
                {
                    pawns.Add(pawn);
                }
            }
        }

        private static string EventWindowInstruction(DiaryEventWindowDef def, string label)
        {
            if (!string.IsNullOrWhiteSpace(def.instruction))
            {
                return def.instruction;
            }

            return "PawnDiary.Event.EventWindow.Generic.Instruction".Translate(label).Resolve();
        }

        private static string EventWindowTextKey(DiaryEventWindowDef def, string phase)
        {
            if (string.Equals(phase, EventWindowPhaseStart, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(def.startTextKey))
            {
                return def.startTextKey;
            }

            if (string.Equals(phase, EventWindowPhaseEnd, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(def.endTextKey))
            {
                return def.endTextKey;
            }

            if (string.Equals(phase, EventWindowPhaseTimeout, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(def.timeoutTextKey))
            {
                return def.timeoutTextKey;
            }

            return "PawnDiary.Event.EventWindow.Generic." + phase;
        }

        private static string EventWindowPhaseText(DiaryEventWindowDef def, string phase,
            string pawnLabel, string signalLabel)
        {
            string literal = EventWindowLiteralText(def, phase);
            if (!string.IsNullOrWhiteSpace(literal))
            {
                return PromptTextTemplate.Format(literal, pawnLabel, signalLabel);
            }

            return EventWindowTextKey(def, phase).Translate(pawnLabel, signalLabel).Resolve();
        }

        private static string EventWindowLiteralText(DiaryEventWindowDef def, string phase)
        {
            if (string.Equals(phase, EventWindowPhaseStart, StringComparison.OrdinalIgnoreCase))
            {
                return def.startText;
            }

            if (string.Equals(phase, EventWindowPhaseEnd, StringComparison.OrdinalIgnoreCase))
            {
                return def.endText;
            }

            if (string.Equals(phase, EventWindowPhaseTimeout, StringComparison.OrdinalIgnoreCase))
            {
                return def.timeoutText;
            }

            return string.Empty;
        }

        private static string EventWindowPromptText(string preferredText, string preferredKey, string fallbackKey)
        {
            if (!string.IsNullOrWhiteSpace(preferredText))
            {
                return preferredText;
            }

            if (!string.IsNullOrWhiteSpace(preferredKey))
            {
                return preferredKey.Translate().Resolve();
            }

            return string.IsNullOrWhiteSpace(fallbackKey) ? string.Empty : fallbackKey.Translate().Resolve();
        }

        private static string EventWindowPromptText(string preferredText, string preferredKey,
            string fallbackKey, string fallbackArg)
        {
            if (!string.IsNullOrWhiteSpace(preferredText))
            {
                return preferredText;
            }

            if (!string.IsNullOrWhiteSpace(preferredKey))
            {
                return preferredKey.Translate().Resolve();
            }

            return fallbackKey.Translate(fallbackArg).Resolve();
        }

        private static void AddPromptCueTexts(List<string> cues, List<string> literalTexts, List<string> keyedTexts)
        {
            if (cues == null)
            {
                return;
            }

            if (literalTexts == null)
            {
                return; // Explicit settings/XML null suppresses configured cues.
            }

            if (literalTexts.Count > 0)
            {
                for (int i = 0; i < literalTexts.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(literalTexts[i]))
                    {
                        cues.Add(literalTexts[i]);
                    }
                }

                return;
            }

            if (keyedTexts == null)
            {
                return;
            }

            for (int i = 0; i < keyedTexts.Count; i++)
            {
                string cueKey = keyedTexts[i];
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

        private static string EventWindowSignalLabel(DiaryEventWindowDef def, ActiveEventWindowState active,
            EventWindowSignalFacts facts)
        {
            string label = DiaryLineCleaner.CleanLine(facts == null ? null : facts.label);
            if (string.IsNullOrWhiteSpace(label) && active != null)
            {
                label = DiaryLineCleaner.CleanLine(active.startLabel);
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryLineCleaner.CleanLine(def.LabelCap.Resolve());
            }

            return label;
        }

        private static string BuildEventWindowGameContext(DiaryEventWindowDef def, ActiveEventWindowState active,
            string phase, EventWindowSignalFacts facts)
        {
            List<string> parts = new List<string>
            {
                "event_window=" + SafeContextValue(def.EffectiveWindowKey()),
                "phase=" + SafeContextValue(phase)
            };

            AddContextPart(parts, "source", facts == null ? null : facts.source);
            AddContextPart(parts, "signal", facts == null ? null : facts.signal);
            AddContextPart(parts, "def", facts == null ? null : facts.defName);
            AddContextPart(parts, "label", facts == null ? null : facts.label);
            AddContextPart(parts, "subject", facts == null ? null : facts.subjectLabel);
            AddContextPart(parts, "subject_id", facts == null ? null : facts.subjectPawnId);
            if (active != null)
            {
                AddContextPart(parts, "startSource", active.startSource);
                AddContextPart(parts, "startSignal", active.startSignal);
                AddContextPart(parts, "startDef", active.startDefName);
                AddContextPart(parts, "startLabel", active.startLabel);
                AddContextPart(parts, "startSubject", active.startSubjectLabel);
                AddContextPart(parts, "startSubjectId", active.startSubjectPawnId);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static void AddContextPart(List<string> parts, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(key + "=" + SafeContextValue(value));
            }
        }

        private static string SafeContextValue(string value)
        {
            return DiaryLineCleaner.CleanLine(value) ?? string.Empty;
        }

        /// <summary>
        /// Cheap pre-check used by hot signal wrappers (spawned things, added hediffs) before they
        /// resolve a label: returns true only when some enabled event-window def could match this
        /// source+defName. Uses each def's cached rule projection, so it allocates nothing on the hot
        /// path. A false result guarantees no def can match, so the caller can skip the label work.
        /// </summary>
        private static bool CouldMatchEventWindow(string source, string defName)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            List<DiaryEventWindowDef> defs = DefDatabase<DiaryEventWindowDef>.AllDefsListForReading;
            if (defs == null)
            {
                return false;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryEventWindowDef def = defs[i];
                if (def == null || !def.enabled)
                {
                    continue;
                }

                if (EventWindowPolicy.CouldMatchByDefName(def.StartRules(), source, defName)
                    || EventWindowPolicy.CouldMatchByDefName(def.EndRules(), source, defName))
                {
                    return true;
                }
            }

            return false;
        }

        private void SnapshotEventWindowStart(ActiveEventWindowState active, DiaryEventWindowDef def,
            EventWindowSignalFacts facts, int mapUniqueId)
        {
            int now = Find.TickManager.TicksGame;
            active.windowDefName = def.defName;
            active.windowKey = def.EffectiveWindowKey();
            active.startedTick = now;
            active.expiresTick = def.timeoutTicks > 0 ? now + def.timeoutTicks : -1;
            active.mapUniqueId = mapUniqueId;
            active.startSource = facts.source ?? string.Empty;
            active.startSignal = facts.signal ?? string.Empty;
            active.startDefName = facts.defName ?? string.Empty;
            active.startLabel = facts.label ?? string.Empty;
            active.startSubjectPawnId = facts.subjectPawnId ?? string.Empty;
            active.startSubjectLabel = facts.subjectLabel ?? string.Empty;
        }

        private ActiveEventWindowState ActiveEventWindowFor(DiaryEventWindowDef def, int mapUniqueId)
        {
            if (def == null || activeEventWindows == null)
            {
                return null;
            }

            string key = def.EffectiveWindowKey();
            for (int i = 0; i < activeEventWindows.Count; i++)
            {
                ActiveEventWindowState active = activeEventWindows[i];
                if (active != null
                    && string.Equals(active.windowKey, key, StringComparison.OrdinalIgnoreCase)
                    && active.mapUniqueId == mapUniqueId)
                {
                    return active;
                }
            }

            return null;
        }

        private static DiaryEventWindowDef EventWindowDefFor(ActiveEventWindowState active)
        {
            if (active == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(active.windowDefName))
            {
                DiaryEventWindowDef byDefName = DefDatabase<DiaryEventWindowDef>.GetNamedSilentFail(active.windowDefName);
                if (byDefName != null)
                {
                    return byDefName;
                }
            }

            List<DiaryEventWindowDef> defs = DefDatabase<DiaryEventWindowDef>.AllDefsListForReading;
            if (defs == null)
            {
                return null;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryEventWindowDef def = defs[i];
                if (def != null
                    && string.Equals(def.EffectiveWindowKey(), active.windowKey, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }

            return null;
        }

        private void NormalizeActiveEventWindows()
        {
            if (activeEventWindows == null)
            {
                activeEventWindows = new List<ActiveEventWindowState>();
                return;
            }

            for (int i = activeEventWindows.Count - 1; i >= 0; i--)
            {
                ActiveEventWindowState active = activeEventWindows[i];
                if (active == null || string.IsNullOrWhiteSpace(active.windowDefName))
                {
                    activeEventWindows.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(active.windowKey))
                {
                    DiaryEventWindowDef def = EventWindowDefFor(active);
                    active.windowKey = def == null ? active.windowDefName : def.EffectiveWindowKey();
                }

                active.startSubjectPawnId = active.startSubjectPawnId ?? string.Empty;
                active.startSubjectLabel = active.startSubjectLabel ?? string.Empty;
            }
        }

        private static Map MapForSignalSubject(Pawn subjectPawn)
        {
            return subjectPawn != null && subjectPawn.Spawned ? subjectPawn.Map : null;
        }

        private static Map FirstPawnMap(List<Pawn> pawns)
        {
            if (pawns == null)
            {
                return null;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && pawn.Spawned)
                {
                    return pawn.Map;
                }
            }

            return null;
        }

        private static int MapUniqueId(Map map)
        {
            return map == null ? -1 : map.uniqueID;
        }

        private static Map MapForUniqueId(int mapUniqueId)
        {
            if (mapUniqueId < 0)
            {
                return null;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map != null && map.uniqueID == mapUniqueId)
                {
                    return map;
                }
            }

            return null;
        }

        private static string EventWindowDedupKey(DiaryEventWindowDef def, string phase,
            EventWindowSignalFacts facts, int mapUniqueId)
        {
            return def.defName + "|" + phase + "|" + mapUniqueId + "|"
                + (facts == null ? string.Empty : facts.source) + "|"
                + (facts == null ? string.Empty : facts.signal) + "|"
                + (facts == null ? string.Empty : facts.defName) + "|"
                + (facts == null ? string.Empty : facts.subjectPawnId);
        }

        private static float SafePromptWeight(float value)
        {
            return float.IsNaN(value) || value < 0f ? 0f : value;
        }

        private static float SafePromptWeightMultiplier(float value)
        {
            return float.IsNaN(value) || value < 0f ? 0f : value;
        }
    }
}
