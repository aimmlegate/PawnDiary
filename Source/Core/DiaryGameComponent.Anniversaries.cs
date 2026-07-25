// Quality Wave H2 — anniversaries and personal records. A slow scanner for the dates and totals a
// colonist would notice about themselves: another birthday, another year since they joined, the
// anniversary of losing someone they loved, and the moment a personal tally becomes part of who they
// are. RimWorld gives no one-shot hook for any of these (a birthday hook exists, but the OTHER three
// are pure elapsed-time facts), so like the skill/trait scanners this compares live state against
// small saved bookkeeping.
//
// Three properties keep it honest, and every one of them lives in the pure AnniversaryPolicy:
//   * Nothing is retroactive. A pawn's FIRST scan only records where they already are, so loading an
//     old save never produces a burst of missed birthdays and milestones.
//   * A story is owned once. If a direct page already covers this exact birthday — Biotech's growth
//     letter or RimWorld's ordinary birthday event window — this scanner stays silent and just
//     advances past it.
//   * Sampling is deterministic. Bonded-death recall past the guaranteed years is a stable hash of
//     (pawn, victim, year), so reloading cannot reroll it and we never touch the gameplay RNG stream.
//
// New to C#/RimWorld? See AGENTS.md. This is one piece of the partial DiaryGameComponent class —
// see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Globalization;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Elapsed-time deadline, never a TicksGame modulo: modulo scheduling silently dies on a dev
        // time-skip and on save/load (see the scribe-saving/performance lore).
        private int nextAnniversaryScanTick;

        /// <summary>
        /// One remembered loss as the scanner sees it this pass: the saved memory row plus the whole
        /// anniversary year that just came due.
        /// </summary>
        private sealed class DueBondedDeath
        {
            public BondedDeathMemoryState memory;
            public int anniversaryYear;
        }

        /// <summary>Clears the transient anniversary schedule so the next tick scans immediately.</summary>
        private void ResetAnniversaryScanSchedule()
        {
            nextAnniversaryScanTick = 0;
        }

        /// <summary>
        /// Returns one pawn's saved progression row, creating the diary record if needed. Exposed so the
        /// in-game suite can seed and read H2 bookkeeping without reaching into private save stores.
        /// </summary>
        internal PawnProgressionState AnniversaryStateFor(Pawn pawn)
        {
            return FindDiary(pawn, true)?.EnsureProgressionState();
        }

        /// <summary>
        /// Periodically checks every eligible colonist for a birthday, an arrival anniversary, a
        /// remembered loss, and newly crossed personal records. The first pass for a pawn baselines
        /// their state and emits nothing.
        /// </summary>
        private void ScanAnniversariesForDiaryEvents()
        {
            List<Pawn> colonists = SnapshotFreeColonists();
            if (colonists.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            // Resolved once per scan rather than once per pawn: GetNamedSilentFail is cheap, but a
            // missing/modded record should also only be looked up (and missed) once here.
            Dictionary<string, RecordDef> recordDefs = SnapshotAnniversaryRecordDefs();
            for (int i = 0; i < colonists.Count; i++)
            {
                ScanAnniversariesForPawn(colonists[i], now, recordDefs);
            }
        }

        /// <summary>
        /// Runs one pawn through every anniversary observer. Kept internal so the in-game suite can
        /// drive a single fixture pawn without touching a developer's real colonists.
        /// </summary>
        internal void ScanAnniversariesForPawn(
            Pawn pawn,
            int now,
            Dictionary<string, RecordDef> recordDefs)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return;
            }

            PawnProgressionState state = diary.EnsureProgressionState();
            bool baseline = state.baselineAnniversariesOnNextScan;

            // Each observer advances its own saved state whether or not it emits. Settings and
            // ownership control page CREATION; they never freeze observation, so re-enabling a row
            // later cannot invent a catch-up page for a date that has already passed.
            ObserveBirthday(pawn, state, baseline);
            ObserveArrivalAnniversary(pawn, diary, state, baseline, now);
            ObserveBondedDeaths(pawn, state, baseline, now);
            ObserveRecordMilestones(pawn, state, baseline, recordDefs);

            if (baseline)
            {
                state.baselineAnniversariesOnNextScan = false;
            }
        }

        // ---- Birthdays ---------------------------------------------------------------------------

        /// <summary>
        /// Notices that the pawn's biological age went up. Stays silent when a direct page already
        /// owns this exact birthday, which is what makes Biotech's growth letter and RimWorld's
        /// ordinary birthday event window authoritative rather than duplicated.
        /// </summary>
        private void ObserveBirthday(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            int currentAge = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears : 0;
            int newObservedAge;
            int age = AnniversaryPolicy.BirthdayToConsider(
                currentAge,
                state.lastObservedBiologicalAgeYears,
                baseline,
                out newObservedAge);
            // Advance FIRST and unconditionally: a birthday we chose not to write about is a birthday
            // the pawn has still had.
            state.lastObservedBiologicalAgeYears = newObservedAge;
            if (age <= 0)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            string ownershipKey = AnniversaryPolicy.BirthdayOwnershipKey(pawnId, age);
            if (string.IsNullOrEmpty(ownershipKey) || HasBirthdayOwner(pawnId, age, ownershipKey))
            {
                return;
            }

            string ageText = age.ToString(CultureInfo.InvariantCulture);
            string extraContext = AnniversaryPolicy.BirthdayAgeContextKey + "=" + ageText
                + "; " + AnniversaryPolicy.OwnershipContextKey + "=" + ownershipKey;
            EmitAnniversaryPage(
                pawn,
                ProgressionEventData.PawnBirthdayDefName,
                AnniversaryPolicy.KindBirthday,
                ageText,
                (age - 1).ToString(CultureInfo.InvariantCulture),
                ageText,
                extraContext,
                "PawnDiary.Event.AnniversaryBirthdayLabel".Translate(age).Resolve(),
                "PawnDiary.Event.AnniversaryBirthdayText".Translate(pawn.LabelShortCap, age).Resolve(),
                "anniversary-birthday|" + pawnId + "|" + ageText);
        }

        /// <summary>
        /// True when some saved diary page already tells the story of this exact birthday. Searches
        /// the whole colony's hot and archived history, not just this pawn's own pages, because a
        /// Biotech growth page can belong to the supervising adult rather than to the child. Runs at
        /// most once per pawn per year — only after an age actually increased.
        /// </summary>
        private bool HasBirthdayOwner(string pawnId, int age, string ownershipKey)
        {
            IReadOnlyList<DiaryEvent> hot = events.AllEvents;
            for (int i = hot.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = hot[i];
                if (diaryEvent != null
                    && BirthdayPageMatches(
                        diaryEvent.interactionDefName,
                        diaryEvent.gameContext,
                        pawnId,
                        age,
                        ownershipKey))
                {
                    return true;
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.AllEntries;
            for (int i = archived.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (entry != null
                    && BirthdayPageMatches(
                        entry.interactionDefName,
                        entry.decorationGameContext,
                        pawnId,
                        age,
                        ownershipKey))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches one saved page against this birthday through stable schema tokens only — never
        /// translated prose. Three owners qualify: a Biotech growth page for the same child and age,
        /// RimWorld's birthday event window for the same subject and age, and an anniversary page that
        /// already claimed this exact ownership key.
        /// </summary>
        private static bool BirthdayPageMatches(
            string interactionDefName,
            string gameContext,
            string pawnId,
            int age,
            string ownershipKey)
        {
            if (GrowthRecordPolicy.Matches(
                interactionDefName,
                DiaryContextFields.Value(gameContext, BiotechContextKeys.ChildId),
                DiaryContextFields.Value(gameContext, BiotechContextKeys.BirthdayAge),
                pawnId,
                age))
            {
                return true;
            }

            if (AnniversaryPolicy.EventWindowPageOwnsBirthday(
                DiaryContextFields.Value(gameContext, AnniversaryPolicy.EventWindowKeyContextKey),
                DiaryContextFields.Value(gameContext, AnniversaryPolicy.EventWindowSubjectIdContextKey),
                DiaryContextFields.Value(gameContext, AnniversaryPolicy.EventWindowLabelContextKey),
                pawnId,
                age))
            {
                return true;
            }

            return DiaryContextFields.FieldEquals(
                gameContext,
                AnniversaryPolicy.OwnershipContextKey,
                ownershipKey);
        }

        // ---- Arrival anniversaries --------------------------------------------------------------

        /// <summary>
        /// Marks whole years since the pawn joined the colony. Every newly reached year advances the
        /// saved value — including the quiet ones — so year 4 can never resurface as a page later.
        /// </summary>
        private void ObserveArrivalAnniversary(
            Pawn pawn,
            PawnDiaryRecord diary,
            PawnProgressionState state,
            bool baseline,
            int now)
        {
            // The pawn's arrival page is their diary's first boundary. FirstArrivalTickFor already
            // merges hot pages, the compact archive, and the durable joining knowledge record, so a
            // player who disabled arrival pages still has a joining date to measure from.
            int? arrivalTick = FirstArrivalTickFor(pawn.GetUniqueLoadID(), diary);
            if (!arrivalTick.HasValue)
            {
                return;
            }

            int years = AnniversaryPolicy.YearsBetween(
                arrivalTick.Value,
                now,
                GenDate.TicksPerYear);
            if (years <= state.lastArrivalAnniversaryYear)
            {
                return;
            }

            int emitYear = NewestArrivalMilestoneInRange(state.lastArrivalAnniversaryYear + 1, years);
            state.lastArrivalAnniversaryYear = years;
            if (baseline || emitYear <= 0)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            string ownershipKey = AnniversaryPolicy.ArrivalOwnershipKey(pawnId, emitYear);
            if (string.IsNullOrEmpty(ownershipKey))
            {
                return;
            }

            string yearText = emitYear.ToString(CultureInfo.InvariantCulture);
            string extraContext = AnniversaryPolicy.AnniversaryYearContextKey + "=" + yearText
                + "; " + AnniversaryPolicy.OwnershipContextKey + "=" + ownershipKey;
            EmitAnniversaryPage(
                pawn,
                ProgressionEventData.ArrivalAnniversaryDefName,
                AnniversaryPolicy.KindArrivalAnniversary,
                yearText,
                string.Empty,
                yearText,
                extraContext,
                "PawnDiary.Event.AnniversaryArrivalLabel".Translate(emitYear).Resolve(),
                "PawnDiary.Event.AnniversaryArrivalText".Translate(pawn.LabelShortCap, emitYear).Resolve(),
                "anniversary-arrival|" + pawnId + "|" + yearText);
        }

        /// <summary>
        /// Newest configured milestone inside a newly reached year range, or 0 when none is. Walks
        /// downward so a large dev time-skip stops at the first (newest) hit, and is bounded so a
        /// hostile jump cannot turn one scan into a long loop.
        /// </summary>
        private static int NewestArrivalMilestoneInRange(int fromYear, int toYear)
        {
            const int MaximumYearsInspected = 250;
            DiaryTuningDef tuning = DiaryTuning.Current;
            int lowest = Math.Max(1, Math.Max(fromYear, toYear - MaximumYearsInspected + 1));
            for (int year = toYear; year >= lowest; year--)
            {
                if (AnniversaryPolicy.IsArrivalMilestoneYear(
                    year,
                    tuning.arrivalAnniversaryMilestoneYears,
                    tuning.arrivalAnniversaryRecurringIntervalYears))
                {
                    return year;
                }
            }

            return 0;
        }

        // ---- Remembered losses -------------------------------------------------------------------

        /// <summary>
        /// Keeps the pawn's bonded-death memories current, then writes at most one page when one or
        /// more of those anniversaries falls today.
        /// </summary>
        private void ObserveBondedDeaths(
            Pawn pawn,
            PawnProgressionState state,
            bool baseline,
            int now)
        {
            DiscoverBondedDeaths(pawn, state, now);
            RecallBondedDeaths(pawn, state, baseline, now);
        }

        /// <summary>
        /// Finds losses the pawn has not remembered yet, keeps the closest bonds inside the XML cap,
        /// and advances the discovery cursor. The cursor only ever moves forward, so a memory the cap
        /// evicted is forgotten for good instead of being rediscovered on every later scan.
        /// </summary>
        private void DiscoverBondedDeaths(Pawn pawn, PawnProgressionState state, int now)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            List<BondedDeathCandidate> rows = ExistingBondedDeathRows(state, tuning);
            int cursor = state.lastBondedDeathDiscoveryTick;
            AppendNewBondedDeathRows(pawn, state, tuning, cursor, rows);

            List<BondedDeathCandidate> retained = AnniversaryPolicy.RetainStrongestBonds(
                rows,
                Math.Max(0, tuning.bondedDeathMemoryCap));
            state.bondedDeathMemories = ToBondedDeathStates(retained);
            // Advance after EVERY scan, not only when something was found: that is what makes
            // eviction permanent.
            state.lastBondedDeathDiscoveryTick = Math.Max(cursor, now);
        }

        /// <summary>Re-ranks the already-saved memories with the current XML bond priority.</summary>
        private static List<BondedDeathCandidate> ExistingBondedDeathRows(
            PawnProgressionState state,
            DiaryTuningDef tuning)
        {
            List<BondedDeathCandidate> rows = new List<BondedDeathCandidate>();
            if (state.bondedDeathMemories == null)
            {
                return rows;
            }

            for (int i = 0; i < state.bondedDeathMemories.Count; i++)
            {
                BondedDeathMemoryState memory = state.bondedDeathMemories[i];
                if (memory == null || !memory.IsUsable())
                {
                    continue;
                }

                rows.Add(new BondedDeathCandidate
                {
                    victimId = memory.victimId,
                    victimName = memory.victimName,
                    relationDefName = memory.relationDefName,
                    relationLabel = memory.relationLabel,
                    // Re-ranked from the CURRENT list, so editing the XML priority order re-sorts the
                    // memories a pawn already carries instead of freezing yesterday's ranking.
                    bondPriority = AnniversaryPolicy.BondPriority(
                        memory.relationDefName,
                        tuning.bondedDeathRelationPriority),
                    deathTick = memory.deathTick,
                    lastProcessedAnniversaryYear = memory.lastProcessedAnniversaryYear
                });
            }

            return rows;
        }

        /// <summary>
        /// Walks the pawn's own related pawns (bounded by their family/bond set, which keeps dead
        /// relatives) and admits the ones who died after the cursor and whose loss the diary can
        /// actually date. A relation not in the XML priority list is never admitted, which is also
        /// what keeps this DLC-safe: the list is plain defName strings.
        /// </summary>
        private void AppendNewBondedDeathRows(
            Pawn pawn,
            PawnProgressionState state,
            DiaryTuningDef tuning,
            int cursor,
            List<BondedDeathCandidate> rows)
        {
            if (pawn.relations == null)
            {
                return;
            }

            try
            {
                foreach (Pawn other in pawn.relations.RelatedPawns)
                {
                    // Only a death the diary can DATE becomes an anniversary. A relative who died
                    // before this colony existed (generated backstory family) has no page, so there
                    // is nothing to be the anniversary of.
                    if (other == null || other == pawn || !other.Dead)
                    {
                        continue;
                    }

                    string victimId = other.GetUniqueLoadID();
                    if (string.IsNullOrWhiteSpace(victimId)
                        || state.FindBondedDeathMemory(victimId) != null)
                    {
                        continue;
                    }

                    string relationDefName = StrongestBondRelationDefName(pawn, other, tuning);
                    int priority = AnniversaryPolicy.BondPriority(
                        relationDefName,
                        tuning.bondedDeathRelationPriority);
                    if (priority < 0)
                    {
                        continue;
                    }

                    int deathTick;
                    if (!TryResolveRememberedDeathTick(pawn, victimId, out deathTick)
                        || deathTick <= cursor)
                    {
                        continue;
                    }

                    rows.Add(new BondedDeathCandidate
                    {
                        victimId = victimId,
                        victimName = DiaryLineCleaner.CleanLine(other.LabelShortCap),
                        relationDefName = relationDefName,
                        relationLabel = BondRelationLabel(pawn, other, relationDefName),
                        bondPriority = priority,
                        deathTick = deathTick,
                        lastProcessedAnniversaryYear = 0
                    });
                }
            }
            catch (Exception e)
            {
                // A single fragile relation (another mod's relation def, a pawn mid-despawn) must cost
                // only this pawn's discovery pass, never the whole GameComponent tick.
                Log.WarningOnce(
                    "[Pawn Diary] Skipped bonded-death discovery for one pawn: " + e,
                    ("PawnDiary.Anniversary.Discovery." + e.GetType().Name).GetHashCode());
            }
        }

        /// <summary>
        /// The closest relation between the two pawns that this feature remembers, as a stable
        /// defName. Uses <c>Pawn.GetRelations</c> (the same API the death-knowledge fan-out uses) so
        /// derived relations like sibling resolve the same way vanilla shows them.
        /// </summary>
        private static string StrongestBondRelationDefName(Pawn pawn, Pawn other, DiaryTuningDef tuning)
        {
            string best = string.Empty;
            int bestPriority = int.MaxValue;
            foreach (PawnRelationDef def in pawn.GetRelations(other))
            {
                string defName = def?.defName;
                int priority = AnniversaryPolicy.BondPriority(
                    defName,
                    tuning.bondedDeathRelationPriority);
                if (priority >= 0 && priority < bestPriority)
                {
                    bestPriority = priority;
                    best = defName;
                }
            }

            return best;
        }

        /// <summary>
        /// The localized relation label shown in the prompt ("husband", "bonded animal"). Falls back
        /// to the plain def label, then to nothing — the page still names the pawn either way.
        /// </summary>
        private static string BondRelationLabel(Pawn pawn, Pawn other, string relationDefName)
        {
            PawnRelationDef def = DefDatabase<PawnRelationDef>.GetNamedSilentFail(relationDefName);
            if (def == null)
            {
                return string.Empty;
            }

            string label = null;
            try
            {
                // The relation def describes the OTHER pawn's role toward this pawn, so the
                // gender-specific label is resolved for the other pawn.
                label = def.GetGenderSpecificLabelCap(other);
            }
            catch
            {
                label = null;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = def.LabelCap.Resolve();
            }

            return DiaryLineCleaner.CleanLine(label);
        }

        /// <summary>
        /// Resolves when a remembered loss happened. Prefers the victim's own death page (the exact
        /// boundary the diary already tracks), then falls back to a page in THIS pawn's diary that
        /// names the victim — which is how a bonded animal, who keeps no diary of its own, is dated.
        /// </summary>
        private bool TryResolveRememberedDeathTick(Pawn pawn, string victimId, out int deathTick)
        {
            deathTick = 0;
            int? victimPageTick = FinalDeathTickFor(victimId, FindDiaryByPawnId(victimId));
            if (victimPageTick.HasValue)
            {
                deathTick = victimPageTick.Value;
                return true;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary?.eventIds != null)
            {
                for (int i = diary.eventIds.Count - 1; i >= 0; i--)
                {
                    DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                    if (diaryEvent != null
                        && DiaryContextFields.FieldEquals(
                            diaryEvent.gameContext, "death_victim_id", victimId))
                    {
                        deathTick = diaryEvent.tick;
                        return true;
                    }
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.EntriesForPawn(pawnId);
            for (int i = archived.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (entry != null
                    && DiaryContextFields.FieldEquals(
                        entry.decorationGameContext, "death_victim_id", victimId))
                {
                    deathTick = entry.tick;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Converts retained pure rows back into the saved list, preserving processed years.</summary>
        private static List<BondedDeathMemoryState> ToBondedDeathStates(
            List<BondedDeathCandidate> retained)
        {
            List<BondedDeathMemoryState> states = new List<BondedDeathMemoryState>();
            for (int i = 0; i < retained.Count; i++)
            {
                BondedDeathCandidate row = retained[i];
                states.Add(new BondedDeathMemoryState
                {
                    victimId = row.victimId,
                    victimName = row.victimName,
                    relationDefName = row.relationDefName,
                    relationLabel = row.relationLabel,
                    deathTick = row.deathTick,
                    lastProcessedAnniversaryYear = row.lastProcessedAnniversaryYear
                });
            }

            return states;
        }

        /// <summary>
        /// Writes at most one page for the anniversaries that came due. Every evaluated year is marked
        /// processed whether or not it produced a page, so a reload cannot reroll a decision that has
        /// already been made.
        /// </summary>
        private void RecallBondedDeaths(
            Pawn pawn,
            PawnProgressionState state,
            bool baseline,
            int now)
        {
            if (state.bondedDeathMemories == null || state.bondedDeathMemories.Count == 0)
            {
                return;
            }

            DiaryTuningDef tuning = DiaryTuning.Current;
            string pawnId = pawn.GetUniqueLoadID();
            List<DueBondedDeath> due = new List<DueBondedDeath>();
            for (int i = 0; i < state.bondedDeathMemories.Count; i++)
            {
                BondedDeathMemoryState memory = state.bondedDeathMemories[i];
                if (memory == null || !memory.IsUsable())
                {
                    continue;
                }

                int years = AnniversaryPolicy.YearsBetween(
                    memory.deathTick,
                    now,
                    GenDate.TicksPerYear);
                if (years <= 0 || years <= memory.lastProcessedAnniversaryYear)
                {
                    continue;
                }

                bool recall = !baseline && AnniversaryPolicy.ShouldRecall(
                    AnniversaryPolicy.DeterministicRoll(pawnId, memory.victimId, years),
                    AnniversaryPolicy.RecallChance(
                        years,
                        tuning.bondedDeathGuaranteedYears,
                        tuning.bondedDeathFirstDecayChance,
                        tuning.bondedDeathDecayMultiplier,
                        tuning.bondedDeathFloorChance));
                // Mark the year processed either way. Skipped intermediate years are marked too: an
                // anniversary that has already passed must not be written on the wrong date later.
                memory.lastProcessedAnniversaryYear = years;
                if (recall)
                {
                    due.Add(new DueBondedDeath { memory = memory, anniversaryYear = years });
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            // Locked rule: at most one combined bonded-death page per pawn per day. Everything due in
            // the same scan is combined below, so this guard only trips for a second batch on the same
            // day — those years stay marked processed, which is the same "decided once" trade-off the
            // failed-sample path makes.
            int day = DayIndexForGameTick(now);
            if (state.lastBondedDeathPageDay == day)
            {
                return;
            }

            EmitBondedDeathAnniversary(pawn, state, due, day);
        }

        private void EmitBondedDeathAnniversary(
            Pawn pawn,
            PawnProgressionState state,
            List<DueBondedDeath> due,
            int day)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            // The pure selector re-uses the retention order, so the closest bond always leads.
            List<BondedDeathCandidate> ranked = AnniversaryPolicy.SelectCoincident(
                ToCandidateRows(due, tuning),
                Math.Max(1, tuning.bondedDeathMaxCombinedNames));
            if (ranked.Count == 0)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<string> victimIds = new List<string>();
            List<string> combinedEntries = new List<string>();
            for (int i = 0; i < ranked.Count; i++)
            {
                BondedDeathCandidate row = ranked[i];
                victimIds.Add(row.victimId);
                combinedEntries.Add("PawnDiary.Event.AnniversaryDeathCombinedEntry"
                    .Translate(row.victimName, row.relationLabel, row.lastProcessedAnniversaryYear)
                    .Resolve());
            }

            int leadingYear = ranked[0].lastProcessedAnniversaryYear;
            string ownershipKey = AnniversaryPolicy.DeathOwnershipKey(pawnId, leadingYear, victimIds);
            if (string.IsNullOrEmpty(ownershipKey))
            {
                return;
            }

            string remembered = AnniversaryPolicy.FormatRemembered(ranked);
            string yearText = leadingYear.ToString(CultureInfo.InvariantCulture);
            string extraContext = AnniversaryPolicy.AnniversaryYearContextKey + "=" + yearText
                + "; " + AnniversaryPolicy.RememberedContextKey + "=" + remembered
                + "; " + AnniversaryPolicy.OwnershipContextKey + "=" + ownershipKey;
            string text = ranked.Count == 1
                ? "PawnDiary.Event.AnniversaryDeathText".Translate(
                    pawn.LabelShortCap, leadingYear, ranked[0].victimName, ranked[0].relationLabel)
                    .Resolve()
                : "PawnDiary.Event.AnniversaryDeathCombinedText".Translate(
                    pawn.LabelShortCap,
                    DiaryListText.JoinComma(combinedEntries))
                    .Resolve();

            bool emitted = EmitAnniversaryPage(
                pawn,
                ProgressionEventData.BondedDeathAnniversaryDefName,
                AnniversaryPolicy.KindDeathAnniversary,
                remembered,
                string.Empty,
                yearText,
                extraContext,
                "PawnDiary.Event.AnniversaryDeathLabel".Translate(ranked[0].victimName).Resolve(),
                text,
                "anniversary-death|" + pawnId + "|" + ownershipKey);
            if (emitted)
            {
                state.lastBondedDeathPageDay = day;
            }
        }

        /// <summary>Projects the due memories into the pure row shape, ranked by current XML order.</summary>
        private static List<BondedDeathCandidate> ToCandidateRows(
            List<DueBondedDeath> due,
            DiaryTuningDef tuning)
        {
            List<BondedDeathCandidate> rows = new List<BondedDeathCandidate>();
            for (int i = 0; i < due.Count; i++)
            {
                DueBondedDeath row = due[i];
                if (row?.memory == null)
                {
                    continue;
                }

                rows.Add(new BondedDeathCandidate
                {
                    victimId = row.memory.victimId,
                    victimName = row.memory.victimName,
                    relationDefName = row.memory.relationDefName,
                    relationLabel = row.memory.relationLabel,
                    bondPriority = AnniversaryPolicy.BondPriority(
                        row.memory.relationDefName,
                        tuning.bondedDeathRelationPriority),
                    deathTick = row.memory.deathTick,
                    // Carries the year being remembered on THIS page, which may differ per victim when
                    // two losses share a calendar date in different years.
                    lastProcessedAnniversaryYear = row.anniversaryYear
                });
            }

            return rows;
        }

        // ---- Personal records --------------------------------------------------------------------

        /// <summary>
        /// Resolves the configured record Defs once per scan. GetNamedSilentFail (never GetNamed) so a
        /// record from a mod the player removed is ignored instead of throwing.
        /// </summary>
        internal static Dictionary<string, RecordDef> SnapshotAnniversaryRecordDefs()
        {
            Dictionary<string, RecordDef> defs =
                new Dictionary<string, RecordDef>(StringComparer.OrdinalIgnoreCase);
            List<RecordMilestoneRule> rules = DiaryTuning.Current.recordMilestones;
            if (rules == null)
            {
                return defs;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                RecordMilestoneRule rule = rules[i];
                string defName = rule?.recordDefName;
                if (string.IsNullOrWhiteSpace(defName) || defs.ContainsKey(defName.Trim()))
                {
                    continue;
                }

                RecordDef def = DefDatabase<RecordDef>.GetNamedSilentFail(defName.Trim());
                if (def != null)
                {
                    defs[defName.Trim()] = def;
                }
            }

            return defs;
        }

        /// <summary>
        /// Notices a personal tally crossing a configured total. Only the highest newly passed
        /// threshold per record emits, and the comparison is against a monotonic high-water mark so a
        /// modded record reset cannot award the same milestone twice.
        /// </summary>
        private void ObserveRecordMilestones(
            Pawn pawn,
            PawnProgressionState state,
            bool baseline,
            Dictionary<string, RecordDef> recordDefs)
        {
            List<RecordMilestoneRule> rules = DiaryTuning.Current.recordMilestones;
            if (rules == null || recordDefs == null || recordDefs.Count == 0 || pawn.records == null)
            {
                return;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                RecordMilestoneRule rule = rules[i];
                RecordDef def;
                if (rule == null || string.IsNullOrWhiteSpace(rule.recordDefName)
                    || !recordDefs.TryGetValue(rule.recordDefName.Trim(), out def))
                {
                    continue;
                }

                float value;
                if (!TryReadRecordValue(pawn, def, out value))
                {
                    continue;
                }

                string recordDefName = rule.recordDefName.Trim();
                float previous = state.HighestRecordValue(recordDefName);
                RecordMilestoneCrossing crossing = AnniversaryPolicy.EvaluateRecord(
                    recordDefName,
                    value,
                    previous,
                    rule.thresholds,
                    baseline);
                state.SetRecordHighWater(recordDefName, crossing.newHighWater);
                if (!crossing.shouldEmit)
                {
                    continue;
                }

                EmitRecordMilestone(pawn, def, recordDefName, crossing.threshold);
            }
        }

        /// <summary>
        /// Reads one record fail-soft. A modded RecordDef's worker can throw, and one bad record must
        /// not abort the scan for the rest of the colony.
        /// </summary>
        private static bool TryReadRecordValue(Pawn pawn, RecordDef def, out float value)
        {
            value = 0f;
            try
            {
                value = pawn.records.GetValue(def);
                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce(
                    "[Pawn Diary] Skipped a personal-record read that threw: " + e,
                    ("PawnDiary.Anniversary.Record." + e.GetType().Name).GetHashCode());
                return false;
            }
        }

        private void EmitRecordMilestone(Pawn pawn, RecordDef def, string recordDefName, int threshold)
        {
            string pawnId = pawn.GetUniqueLoadID();
            string ownershipKey = AnniversaryPolicy.RecordOwnershipKey(pawnId, recordDefName, threshold);
            if (string.IsNullOrEmpty(ownershipKey))
            {
                return;
            }

            string recordLabel = CleanLabel(def.LabelCap.Resolve(), recordDefName);
            string thresholdText = threshold.ToString(CultureInfo.InvariantCulture);
            string extraContext = AnniversaryPolicy.RecordNameContextKey + "=" + recordLabel
                + "; " + AnniversaryPolicy.RecordValueContextKey + "=" + thresholdText
                + "; " + AnniversaryPolicy.OwnershipContextKey + "=" + ownershipKey;
            EmitAnniversaryPage(
                pawn,
                ProgressionEventData.RecordMilestoneDefName,
                AnniversaryPolicy.KindRecord,
                recordLabel,
                string.Empty,
                thresholdText,
                extraContext,
                "PawnDiary.Event.AnniversaryRecordLabel".Translate(recordLabel, threshold).Resolve(),
                "PawnDiary.Event.AnniversaryRecordText"
                    .Translate(pawn.LabelShortCap, threshold, recordLabel).Resolve(),
                "anniversary-record|" + pawnId + "|" + recordDefName + "|" + thresholdText);
        }

        // ---- Shared emission ---------------------------------------------------------------------

        /// <summary>
        /// Sends one anniversary through the ordinary progression dispatcher, so it inherits the
        /// existing group toggle, prompt policy, dedup gate, and generation queue for free. Returns
        /// true only when the page actually committed.
        /// </summary>
        private bool EmitAnniversaryPage(
            Pawn pawn,
            string defName,
            string kind,
            string label,
            string previousValue,
            string newValue,
            string extraContext,
            string displayLabel,
            string text,
            string dedupKey)
        {
            ProgressionEventData data = ProgressionData(
                pawn,
                defName,
                kind,
                label,
                previousValue,
                newValue,
                extraContext);
            return DispatchProgression(
                pawn,
                data,
                displayLabel,
                text,
                majorArcCandidate: false,
                dedupKey: dedupKey,
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks);
        }
    }
}
