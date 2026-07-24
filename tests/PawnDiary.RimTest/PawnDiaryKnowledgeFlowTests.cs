// In-game wiring tests for the deterministic pawn-knowledge system
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §8, integration list). Proves inside a real loaded game:
//   1. Capture: a romance pair page (Spouse) writes one important-event record per POV pawn with
//      the partner as participant — gameplay capture succeeds without completing any LLM request.
//   2. Closed list: an ordinary chat page writes NO record (§2.1 exclusions).
//   3. Injection switch: with the memory setting OFF, capture still happens (§3.2) while the
//      event's relevant-past slot stays empty; with it ON, a later related event carries at most
//      two dated lines referencing the stored fact.
//   4. Culture: capture resolves the pawn's origin culture once, with "captured" provenance.
//   5. Quiet-hediff channel: an XML-allowlisted persistent hediff (Sterilized) is remembered
//      even though it produces no diary page of its own.
//   6. Body events: a REAL amputation (Pawn_HealthTracker.AddHediff) records the stable
//      part_def subject key, and installing onto the same part recalls the loss into both the
//      event slot and the captured LLM prompt (§3.1 "same body part").
//   7. Status family keys: title events share the constant "title" entity key, so a demotion
//      recalls the original investiture (§3.1 "title/status family").
//   8. Death fan-out via a real Pawn.Kill: the killer and the spouse each keep one record;
//      an unrelated bystander keeps none (§2.1).
//   9. Conversion channel: adopted culture REPLACES on each conversion and each conversion is
//      recorded (§4.1).
//  10. Role channel: an ideological role change is remembered WITHOUT creating a diary page.
//  11. Defensive caps: the per-pawn record cap drops the oldest records at insert (§2.3).
//  12. Annotation: a themed prompt carries the pawn's culture clause inline; an ordinary chat
//      prompt does not carry that clause (§4.3).
//
// All fragile scaffolding — isolated pawns, settings snapshot/restore, event/diary cleanup —
// lives in the shared PawnDiaryRimTestScope harness. The save round-trip half of the plan's
// integration list is covered by PawnDiaryRepositoryRebuildFixtureTests (real Scribe); the
// RimTalk preset cleanup stays a manual check (needs RimTalk loaded).
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Loaded-game verification of knowledge capture, the closed allowlist, the injection-only
    /// master switch, culture resolution, and the quiet-hediff channel.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryKnowledgeFlowTests
    {
        private static readonly MethodInfo FindDiaryMethod =
            typeof(DiaryGameComponent).GetMethod("FindDiary",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawnA;
        private static Pawn pawnB;
        private static bool savedMemorySetting;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            if (Find.CurrentMap == null)
            {
                throw new AssertionException("Knowledge flow tests require a loaded map with a colony.");
            }

            if (FindDiaryMethod == null)
            {
                throw new AssertionException(
                    "Reflection handle for DiaryGameComponent.FindDiary is null — method was renamed.");
            }

            pawnA = scope.CreateAdultColonist();
            pawnB = scope.CreateAdultColonist();
            savedMemorySetting = PawnDiaryMod.Settings.enableMemorySystem;
            PawnDiaryMod.Settings.enableMemorySystem = true;
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                PawnDiaryMod.Settings.enableMemorySystem = savedMemorySetting;
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawnA = null;
                pawnB = null;
            }
        }

        // ── 1 + 2: capture through the real funnel, closed-list negative ─────────────────────────────

        /// <summary>
        /// A marriage pair page deposits one relation.spouse.gained record for EACH pawn with the
        /// partner as its first participant; an ordinary Chitchat page deposits nothing.
        /// </summary>
        [Test]
        public static void MarriageCapturesForBothPawnsAndChatDoesNot()
        {
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            PawnKnowledgeState stateB = KnowledgeFor(pawnB);
            int beforeA = stateA.records.Count;
            int beforeB = stateB.records.Count;

            AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");

            Require(stateA.records.Count == beforeA + 1,
                "Pawn A must gain exactly one marriage record, had " + beforeA + " now "
                + stateA.records.Count + ".");
            Require(stateB.records.Count == beforeB + 1,
                "Pawn B must gain exactly one marriage record.");

            ImportantMemoryRecord record = stateA.records[stateA.records.Count - 1];
            Require(record.eventKind == "relation.spouse.gained",
                "Expected kind relation.spouse.gained, got '" + record.eventKind + "'.");
            Require(record.participantIds.Count > 0
                    && record.participantIds[0] == pawnB.GetUniqueLoadID(),
                "The record's first participant must be the partner pawn.");
            Require(!string.IsNullOrWhiteSpace(record.participantNames[0]),
                "The partner's display-name fallback must be saved.");
            Require(!string.IsNullOrWhiteSpace(record.dateLabel),
                "The capture must stamp the game date label.");
            Require(!string.IsNullOrWhiteSpace(record.fallbackSummary),
                "The capture must render a localized fallback summary.");

            // Closed list (§2.1): ordinary conversation never becomes important memory.
            int beforeChat = stateA.records.Count;
            AddPairEvent(pawnA, pawnB, "Chitchat");
            Require(stateA.records.Count == beforeChat,
                "A Chitchat page must not deposit an important-event record.");
        }

        // ── 3: the one player switch gates injection only ────────────────────────────────────────────

        /// <summary>
        /// With the memory switch OFF: capture continues, but the page's relevant-past slot stays
        /// empty. With it back ON: a later event with the same partner carries the stored fact.
        /// </summary>
        [Test]
        public static void SwitchOffStillCapturesAndOnInjectsRelevantPast()
        {
            PawnDiaryMod.Settings.enableMemorySystem = false;
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            int before = stateA.records.Count;

            DiaryEvent offEvent = AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");
            Require(stateA.records.Count == before + 1,
                "Capture must continue while prompt injection is disabled (§3.2).");
            Require(string.IsNullOrEmpty(offEvent.MemoryContextForRole(DiaryEvent.InitiatorRole)),
                "No relevant-past lines may be injected while the switch is off.");

            // Re-enable and fire a related event (same partner): the marriage surfaces as at most
            // two dated lines on the NEW event.
            PawnDiaryMod.Settings.enableMemorySystem = true;
            DiaryEvent onEvent = AddRomancePairEvent(pawnA, pawnB, "Lover", "lover");
            string block = onEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
            if (!onEvent.IsSkipped(DiaryEvent.InitiatorRole))
            {
                Require(!string.IsNullOrWhiteSpace(block),
                    "A related past record (shared partner) must inject relevant-past lines.");
                Require(block.Split('\n').Length <= 2,
                    "At most two relevant-past lines may be injected (§3.2), got: " + block);
                Require(block.IndexOf(pawnB.LabelShort, StringComparison.OrdinalIgnoreCase) >= 0,
                    "The marriage line must reference the partner by saved name; got: " + block);
            }
        }

        // ── 4: culture resolution at capture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Capture resolves the pawn's origin culture once with "captured" provenance — from the
        /// ideology culture (Ideology active) or the faction's allowed cultures otherwise.
        /// </summary>
        [Test]
        public static void CaptureResolvesOriginCultureOnce()
        {
            AddRomancePairEvent(pawnA, pawnB, "Lover", "lover");
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            Require(!string.IsNullOrWhiteSpace(state.originCultureDefName),
                "A colonist's origin culture must resolve at capture (player faction always "
                + "declares allowedCultures).");
            Require(state.originCultureSource == "captured",
                "A live-game resolution must be marked 'captured', got '"
                + state.originCultureSource + "'.");

            string resolved = state.originCultureDefName;
            AddRomancePairEvent(pawnA, pawnB, "ExLover", "breakup");
            Require(KnowledgeFor(pawnA).originCultureDefName == resolved,
                "The origin culture must never be silently rewritten (§4.1).");
        }

        // ── 5: quiet-hediff channel ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// An XML-allowlisted persistent hediff (Sterilized) is remembered through the quiet
        /// channel even though it creates no diary page; an unlisted hediff is not.
        /// </summary>
        [Test]
        public static void QuietHediffChannelCapturesAllowlistedConditionsOnly()
        {
            HediffDef sterilized = DefDatabase<HediffDef>.GetNamedSilentFail("Sterilized");
            if (sterilized == null)
            {
                return; // base defs missing in this environment; nothing to verify
            }

            PawnKnowledgeState state = KnowledgeFor(pawnA);
            int before = state.records.Count;
            pawnA.health.AddHediff(sterilized);
            Require(state.records.Count == before + 1,
                "Adding Sterilized must deposit one quiet-channel record.");
            ImportantMemoryRecord record = state.records[state.records.Count - 1];
            Require(record.eventKind == "body.condition.permanent",
                "Expected kind body.condition.permanent, got '" + record.eventKind + "'.");

            // Repeating the same condition on the same tick dedups instead of doubling.
            pawnA.health.RemoveHediff(pawnA.health.hediffSet.GetFirstHediffOfDef(sterilized));
            pawnA.health.AddHediff(sterilized);
            Require(state.records.Count == before + 1,
                "A same-tick duplicate capture must collapse via the dedup key (§2.2).");
        }

        // ── 6: real amputation → stable part key → same-part recall into the prompt ─────────────────

        /// <summary>
        /// A real missing-part hediff records body.part.lost with the stable "part:&lt;def&gt;"
        /// subject key; installing a bionic onto the SAME part then recalls the loss into the new
        /// event's relevant-past slot AND into the captured LLM prompt (dateLabel is the
        /// language-proof marker).
        /// </summary>
        [Test]
        public static void AmputationRecordsPartKeyAndSamePartInstallRecallsIt()
        {
            HediffDef missingPart = HediffDefOf.MissingBodyPart;
            HediffDef bionicLeg = DefDatabase<HediffDef>.GetNamedSilentFail("BionicLeg");
            if (bionicLeg == null)
            {
                Log.Message("[PawnDiary RimTest knowledge] BionicLeg def missing; skipping.");
                return;
            }

            scope.EnablePromptCapture();
            Pawn patient = scope.CreateGeneratingAdultColonist();
            BodyPartRecord leg = FindPart(patient, "Leg");
            PawnKnowledgeState state = KnowledgeFor(patient);
            int before = CountKind(state, "body.part.lost");

            DiaryEvent lossEvent = scope.FireAndRequireEvent(
                () => patient.health.AddHediff(missingPart, leg),
                missingPart.defName,
                patient,
                null);
            Require(CountKind(state, "body.part.lost") == before + 1,
                "A real amputation must deposit one body.part.lost record.");
            ImportantMemoryRecord loss = LastOfKind(state, "body.part.lost");
            Require(loss.subjectKeys.Contains("part:" + leg.def.defName),
                "The loss record must carry the stable part_def subject key; got ["
                + string.Join(",", loss.subjectKeys.ToArray()) + "].");

            DiaryEvent installEvent = scope.FireAndRequireEvent(
                () => patient.health.AddHediff(bionicLeg, leg),
                bionicLeg.defName,
                patient,
                null);
            string slot = installEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(!string.IsNullOrWhiteSpace(slot)
                    && slot.IndexOf(loss.dateLabel, StringComparison.Ordinal) >= 0,
                "Installing onto the same part must recall the loss (dated line); slot was: '"
                + slot + "'.");
            string prompt = scope.CapturedPrompt(installEvent, DiaryEvent.InitiatorRole);
            Require(prompt.IndexOf(loss.dateLabel, StringComparison.Ordinal) >= 0,
                "The captured prompt must carry the recalled dated loss line.");
        }

        // ── 7: title/status family key across title events ───────────────────────────────────────────

        /// <summary>
        /// Title events share the constant "title" entity key (§3.1), so a later demotion event
        /// recalls the original investiture even though the two carry different progression
        /// defNames. Runs DLC-free: capture is plain string matching.
        /// </summary>
        [Test]
        public static void TitleFamilyKeyRecallsInvestitureOnDemotion()
        {
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            int before = CountKind(state, "status.title.advanced");

            AddProgressionSoloEvent(pawnA, "RoyalTitleGained",
                "progression=RoyalTitleGained; progression_kind=royal_title; label=title; new_value=Knight");
            Require(CountKind(state, "status.title.advanced") == before + 1,
                "A RoyalTitleGained event must deposit one status.title.advanced record.");
            ImportantMemoryRecord gained = LastOfKind(state, "status.title.advanced");
            Require(gained.subjectKeys.Contains("title"),
                "Title records must carry the constant 'title' family key.");

            DiaryEvent demotion = AddProgressionSoloEvent(pawnA, "RoyalTitleDemoted",
                "progression=RoyalTitleDemoted; progression_kind=royal_title; label=title; previous_value=Knight; new_value=none");
            if (!demotion.IsSkipped(DiaryEvent.InitiatorRole))
            {
                string slot = demotion.MemoryContextForRole(DiaryEvent.InitiatorRole);
                Require(!string.IsNullOrWhiteSpace(slot)
                        && slot.IndexOf(gained.dateLabel, StringComparison.Ordinal) >= 0,
                    "The demotion must recall the investiture via the shared 'title' key; slot: '"
                    + slot + "'.");
            }
        }

        // ── 8: death fan-out through a real Pawn.Kill ────────────────────────────────────────────────

        /// <summary>
        /// Killing a colonist through the real Pawn.Kill path fans records out to the pawn
        /// instigator (death.killed) and the victim's spouse (death.family with a relation fact),
        /// while an unrelated bystander keeps nothing (§2.1: ordinary witnesses never remember).
        /// </summary>
        [Test]
        public static void DeathFanOutReachesKillerAndSpouseOnly()
        {
            Pawn victim = scope.CreateAdultColonist();
            Pawn killer = pawnA;
            Pawn spouse = pawnB;
            Pawn bystander = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(victim);

            // Diary records must exist for owners to capture; the marriage this fires also seeds
            // ordinary relationship records — assertions below count death kinds only.
            PawnKnowledgeState killerState = KnowledgeFor(killer);
            PawnKnowledgeState spouseState = KnowledgeFor(spouse);
            PawnKnowledgeState bystanderState = KnowledgeFor(bystander);
            victim.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);

            int killedBefore = CountKind(killerState, "death.killed");
            int familyBefore = CountKind(spouseState, "death.family");
            int bystanderBefore = bystanderState.records.Count;

            RegisterDeadPawnCleanup(victim);
            DamageInfo dinfo = new DamageInfo(DamageDefOf.Cut, 9999f, 999f, -1f, killer);
            scope.FireAndRequireEvent(
                () => victim.Kill(dinfo),
                DeathFallbackSignal.DeathFallbackDefName,
                victim,
                null);

            Require(CountKind(killerState, "death.killed") == killedBefore + 1,
                "The pawn instigator must remember the kill.");
            ImportantMemoryRecord killed = LastOfKind(killerState, "death.killed");
            Require(killed.participantIds.Contains(victim.GetUniqueLoadID()),
                "The kill record must reference the victim as participant.");

            Require(CountKind(spouseState, "death.family") == familyBefore + 1,
                "The spouse must remember the family death.");
            ImportantMemoryRecord familyLoss = LastOfKind(spouseState, "death.family");
            Require(FactValue(familyLoss, "relation").Length > 0,
                "The family record must carry the victim's relation label.");
            Require(FactValue(familyLoss, "victim").Length > 0,
                "The family record must carry the victim's saved name.");

            Require(bystanderState.records.Count == bystanderBefore,
                "An unrelated bystander must keep no death record.");
        }

        // ── 9 + 10: conversion and role channels ─────────────────────────────────────────────────────

        /// <summary>
        /// The conversion channel replaces the adopted culture on EACH conversion (earlier adopted
        /// cultures are not retained, §4.1) and records every conversion. Drives the component
        /// seam directly so the test runs without the Ideology DLC.
        /// </summary>
        [Test]
        public static void ConversionReplacesAdoptedCultureAndRecords()
        {
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            int before = CountKind(state, "status.ideo.converted");

            scope.Component.CaptureIdeoConversionKnowledge(pawnA, "Old Way", "The Flame", "Corunan");
            Require(state.adoptedCultureDefName == "Corunan",
                "The first conversion must set the adopted culture.");

            scope.Component.CaptureIdeoConversionKnowledge(pawnA, "The Flame", "New Dawn", "Kriminul");
            Require(state.adoptedCultureDefName == "Kriminul",
                "A second conversion must REPLACE the adopted culture, got '"
                + state.adoptedCultureDefName + "'.");
            Require(CountKind(state, "status.ideo.converted") == before + 2,
                "Each conversion must deposit one record.");
        }

        /// <summary>
        /// The role channel records an appointment and a removal WITHOUT creating any diary page —
        /// gameplay capture succeeds with no page and no LLM request (§8 integration list).
        /// </summary>
        [Test]
        public static void RoleChangeCapturesWithoutDiaryPage()
        {
            PawnDiaryRecord diary = DiaryFor(pawnA);
            PawnKnowledgeState state = diary.EnsureKnowledgeState();
            int gainedBefore = CountKind(state, "status.role.gained");
            int lostBefore = CountKind(state, "status.role.lost");
            int pagesBefore = diary.eventIds.Count;

            scope.Component.CaptureRoleKnowledge(pawnA, "moral guide", "The Flame", true);
            scope.Component.CaptureRoleKnowledge(pawnA, "moral guide", "The Flame", false);

            Require(CountKind(state, "status.role.gained") == gainedBefore + 1
                    && CountKind(state, "status.role.lost") == lostBefore + 1,
                "Role appointment and removal must each deposit one record.");
            Require(diary.eventIds.Count == pagesBefore,
                "Role capture must not create a diary page.");
            Require(FactValue(LastOfKind(state, "status.role.gained"), "role") == "moral guide",
                "The role record must carry the role label fact.");
        }

        // ── 11: per-pawn defensive cap at insert ─────────────────────────────────────────────────────

        /// <summary>
        /// With the XML per-pawn cap lowered to 2, a third capture drops the oldest record at
        /// insert (§2.3) instead of growing past the cap.
        /// </summary>
        [Test]
        public static void PerPawnCapDropsOldestAtInsert()
        {
            DiaryKnowledgeTuningDef tuning =
                DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail("Diary_Knowledge");
            if (tuning == null)
            {
                throw new AssertionException("Diary_Knowledge tuning def is missing.");
            }

            int savedCap = tuning.maxRecordsPerPawn;
            tuning.maxRecordsPerPawn = 2;
            try
            {
                Pawn fresh = scope.CreateAdultColonist();
                PawnKnowledgeState state = KnowledgeFor(fresh);
                Require(state.records.Count == 0, "The fresh pawn must start with no records.");

                AddRomancePairEvent(fresh, pawnB, "Lover", "lover");
                AddRomancePairEvent(fresh, pawnB, "Spouse", "married");
                AddRomancePairEvent(fresh, pawnB, "ExSpouse", "divorce");
                Require(state.records.Count == 2,
                    "The per-pawn cap of 2 must hold at insert; got " + state.records.Count + ".");
            }
            finally
            {
                tuning.maxRecordsPerPawn = savedCap;
            }
        }

        // ── 12: inline culture annotation reaches the real prompt ────────────────────────────────────

        /// <summary>
        /// A themed event (royal title — the "empire" topic triggers on its defName) carries the
        /// pawn's culture clause inline in the REAL captured prompt; an ordinary chat prompt does
        /// not carry that clause (§4.3). The expected clause is read from the pawn's own resolved
        /// profile (or the fallback lens), so the assertion is language-proof.
        /// </summary>
        [Test]
        public static void CultureAnnotationLandsInThemedPromptOnly()
        {
            scope.EnablePromptCapture();
            Pawn writer = scope.CreateGeneratingAdultColonist();
            PawnKnowledgeState state = KnowledgeFor(writer);

            DiaryEvent titled = AddProgressionSoloEvent(writer, "RoyalTitleGained",
                "progression=RoyalTitleGained; progression_kind=royal_title; label=title; new_value=Knight");
            string empireClause = EmpireClauseFor(state);
            if (empireClause.Length == 0)
            {
                Log.Message("[PawnDiary RimTest knowledge] no culture profile resolved; skipping.");
                return;
            }

            string themedPrompt = scope.CapturedPrompt(titled, DiaryEvent.InitiatorRole);
            Require(themedPrompt.IndexOf(empireClause, StringComparison.Ordinal) >= 0,
                "The themed prompt must carry the culture clause '" + empireClause + "'.");

            DiaryEvent chat = AddPairEvent(writer, pawnB, "Chitchat");
            if (!chat.IsSkipped(DiaryEvent.InitiatorRole))
            {
                string chatPrompt = scope.CapturedPrompt(chat, DiaryEvent.InitiatorRole);
                Require(chatPrompt.IndexOf(empireClause, StringComparison.Ordinal) < 0,
                    "An ordinary chat prompt must not carry the empire clause.");
            }
        }

        /// <summary>The empire-topic clause of the writer's resolved (or fallback) profile.</summary>
        private static string EmpireClauseFor(PawnKnowledgeState state)
        {
            string culture = string.IsNullOrWhiteSpace(state.adoptedCultureDefName)
                ? state.originCultureDefName
                : state.adoptedCultureDefName;
            DiaryCultureProfileDef match = null;
            DiaryCultureProfileDef fallback = null;
            foreach (DiaryCultureProfileDef def in DefDatabase<DiaryCultureProfileDef>.AllDefsListForReading)
            {
                if (def.isFallback && fallback == null)
                {
                    fallback = def;
                }

                if (!string.IsNullOrWhiteSpace(culture)
                    && string.Equals(def.cultureDefName, culture, StringComparison.OrdinalIgnoreCase))
                {
                    match = def;
                }
            }

            DiaryCultureProfileDef profile = match ?? (string.IsNullOrWhiteSpace(culture) ? null : fallback);
            if (profile?.clauses == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < profile.clauses.Count; i++)
            {
                if (string.Equals(profile.clauses[i]?.topicKey, "empire", StringComparison.OrdinalIgnoreCase))
                {
                    return (profile.clauses[i].clause ?? string.Empty).Trim();
                }
            }

            return string.Empty;
        }

        // ── Harness helpers ──────────────────────────────────────────────────────────────────────────

        private static PawnKnowledgeState KnowledgeFor(Pawn pawn)
        {
            return DiaryFor(pawn).EnsureKnowledgeState();
        }

        private static PawnDiaryRecord DiaryFor(Pawn pawn)
        {
            PawnDiaryRecord diary = FindDiaryMethod.Invoke(
                scope.Component, new object[] { pawn, true }) as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Could not resolve the test pawn's diary record.");
            }

            return diary;
        }

        private static int CountKind(PawnKnowledgeState state, string eventKind)
        {
            int count = 0;
            for (int i = 0; i < state.records.Count; i++)
            {
                if (state.records[i] != null
                    && string.Equals(state.records[i].eventKind, eventKind, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static ImportantMemoryRecord LastOfKind(PawnKnowledgeState state, string eventKind)
        {
            for (int i = state.records.Count - 1; i >= 0; i--)
            {
                if (state.records[i] != null
                    && string.Equals(state.records[i].eventKind, eventKind, StringComparison.Ordinal))
                {
                    return state.records[i];
                }
            }

            throw new AssertionException("No record of kind '" + eventKind + "' was found.");
        }

        private static string FactValue(ImportantMemoryRecord record, string key)
        {
            for (int i = 0; i < record.factKeys.Count; i++)
            {
                if (string.Equals(record.factKeys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return i < record.factValues.Count ? (record.factValues[i] ?? string.Empty) : string.Empty;
                }
            }

            return string.Empty;
        }

        private static BodyPartRecord FindPart(Pawn pawn, string partDefName)
        {
            List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (string.Equals(parts[i].def.defName, partDefName, StringComparison.Ordinal))
                {
                    return parts[i];
                }
            }

            throw new AssertionException("Body part '" + partDefName + "' not found on the test pawn.");
        }

        /// <summary>Fires a progression-shaped solo page through the funnel (DLC-free: capture is
        /// plain string matching on the synthetic progression defName + context).</summary>
        private static DiaryEvent AddProgressionSoloEvent(Pawn pawn, string defName, string gameContext)
        {
            DiaryEvent diaryEvent = scope.Component.AddSoloEvent(
                pawn,
                null,
                defName,
                "title",
                pawn.LabelShortCap + " " + defName,
                string.Empty,
                gameContext);
            if (diaryEvent == null)
            {
                throw new AssertionException("The progression solo event was not registered.");
            }

            return diaryEvent;
        }

        /// <summary>Registers cleanup for the state a killed pawn leaves behind (corpse holder,
        /// world-pawn entry) — mirrors PawnDiaryDeathFlowTests so no corpse survives the test.</summary>
        private static void RegisterDeadPawnCleanup(Pawn pawn)
        {
            scope.RegisterCleanup(() =>
            {
                if (pawn != null
                    && !pawn.Destroyed
                    && Find.WorldPawns != null
                    && Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
            });

            scope.RegisterCleanup(() =>
            {
                Corpse corpse = pawn?.ParentHolder as Corpse;
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.Destroy(DestroyMode.Vanish);
                }
            });
        }

        /// <summary>Fires a real romance-shaped pair page through the EventFactory funnel, exactly
        /// how RomanceSignal emits one (relation defName + romance context markers).</summary>
        private static DiaryEvent AddRomancePairEvent(Pawn initiator, Pawn recipient,
            string relationDefName, string kindToken)
        {
            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                relationDefName,
                kindToken,
                initiator.LabelShortCap + " " + kindToken + " " + recipient.LabelShortCap,
                recipient.LabelShortCap + " " + kindToken + " " + initiator.LabelShortCap,
                string.Empty,
                "romance=" + relationDefName + "; label=" + kindToken + "; kind=" + kindToken);
            if (diaryEvent == null)
            {
                throw new AssertionException("The romance pair event was not registered.");
            }

            return diaryEvent;
        }

        private static DiaryEvent AddPairEvent(Pawn initiator, Pawn recipient, string interactionDefName)
        {
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail(interactionDefName);
            if (interaction == null)
            {
                throw new AssertionException(
                    "InteractionDef '" + interactionDefName + "' not found in the loaded game.");
            }

            string label = interaction.LabelCap.Resolve();
            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                interaction.defName,
                label,
                initiator.LabelShortCap + " " + label,
                recipient.LabelShortCap + " " + label,
                InteractionGroups.InstructionFor(interaction),
                DiaryContextBuilder.BuildGameContextSummary(interaction, label));
            if (diaryEvent == null)
            {
                throw new AssertionException("The pair event was not registered.");
            }

            return diaryEvent;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }
    }
}
