// In-game flow tests for the Quality Wave B6 daily soft cap and digest lines (EVT-26).
//
// B6 has one job: a colonist whose day is nothing but small moments should not end up with a diary
// full of near-identical pages. After a per-day allowance of LOW-SALIENCE pages (groups XML marks
// neither important nor combat), further ones fold into a short digest the evening reflection can
// mention instead. Anything the player would call a real event is never paced.
//
// These tests drive the real PlayLog.Add choke point with vanilla DeepTalk rows, exactly like the
// EVT-02 batch suite. DeepTalk's shipped group ("heartfelt") is important, so SetUp temporarily flips
// that one XML flag to false (restored in teardown) to get a deterministic, always-present,
// NON-batching low-salience pair interaction. The shipped catch-all really is low-salience — the
// fixture suite pins that separately — so this is a determinism device, not a behavior fiction.
//
// Determinism: the generic same-type dedup window is zeroed (identical rows fire in one frame, as in
// EVT-02), and the cap/buffer tuning is forced to known values. Every saved pacing row this suite
// creates is removed in teardown, because unlike the filler/hediff stores these rows ARE saved.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-26.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the soft cap emits under its allowance, folds an over-cap page into BOTH diaries of a
    /// pair, never half-emits a shared page, exempts important moments, survives a reload, and that
    /// the resulting digest lines colour a day reflection without ever creating one.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDigestPacingFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Small deterministic allowance: two pages emit, the third folds away.
        private const int TestSoftCap = 2;
        private const int TestDigestLines = 4;

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;
        private static DiaryInteractionGroupDef heartfeltGroup;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("heartfelt");
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();

            heartfeltGroup = RequireGroup("heartfelt");
            ForceLowSalienceGroup(heartfeltGroup);
            ForcePacingTuning(TestSoftCap, TestDigestLines);

            // Identical rows in one RimTest frame would otherwise collapse into the dispatcher's short
            // same-type safety window and never reach the cap. Real play spaces them across thousands
            // of ticks. (Same device as EVT-02.)
            DiaryTuningDef tuning = DiaryTuning.Current;
            int originalDedupTicks = tuning.genericEventTypeDedupTicks;
            tuning.genericEventTypeDedupTicks = 0;
            scope.RegisterCleanup(() => tuning.genericEventTypeDedupTicks = originalDedupTicks);

            RegisterPacingRowCleanup();
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                firstPawn = null;
                secondPawn = null;
                heartfeltGroup = null;
            }
        }

        /// <summary>
        /// EVT-26. Under the allowance every low-salience page still emits normally, and each emitted
        /// page advances BOTH participants' daily counts.
        /// </summary>
        [Test]
        public static void UnderCapLowSaliencePagesEmitAndAdvanceBothCounts()
        {
            int day = CurrentDayIndex();
            RequireCount(firstPawn, day, 0, "before any page");
            RequireCount(secondPawn, day, 0, "before any page");

            for (int i = 0; i < TestSoftCap; i++)
            {
                scope.FireAndRequireEvent(
                    () => AddDeepTalkRow(firstPawn, secondPawn), "DeepTalk", firstPawn, secondPawn);
            }

            RequireCount(firstPawn, day, TestSoftCap, "after emitting the whole allowance");
            RequireCount(secondPawn, day, TestSoftCap, "after emitting the whole allowance");
            PawnDiaryRimTestScope.Require(DigestLineCount(firstPawn, day) == 0,
                "Pages that actually emitted must not also be buffered as digest lines.");
        }

        /// <summary>
        /// EVT-26. Once BOTH participants are at their allowance the shared page is suppressed and each
        /// diary receives its own side of the moment as one digest line.
        /// </summary>
        [Test]
        public static void BothCappedPairFoldsIntoBothDigests()
        {
            int day = CurrentDayIndex();
            SetCount(firstPawn, day, TestSoftCap);
            SetCount(secondPawn, day, TestSoftCap);

            scope.RequireNoNewEvent(() => AddDeepTalkRow(firstPawn, secondPawn));

            PawnDiaryRimTestScope.Require(DigestLineCount(firstPawn, day) == 1,
                "The initiator's diary did not remember the folded-away moment.");
            PawnDiaryRimTestScope.Require(DigestLineCount(secondPawn, day) == 1,
                "The recipient's diary did not remember the folded-away moment.");
            PawnDiaryRimTestScope.Require(
                FirstDigestLine(firstPawn, day) != FirstDigestLine(secondPawn, day),
                "Each diary must remember its OWN side of a suppressed shared interaction.");
            PawnDiaryRimTestScope.Require(
                string.Equals(FirstDigestSourceKind(firstPawn, day), "interaction", StringComparison.Ordinal),
                "The buffered line did not carry the stable interaction source token.");
            RequireCount(firstPawn, day, TestSoftCap, "a suppressed page must not advance the count");
        }

        /// <summary>
        /// EVT-26. If EITHER participant still has room the shared page emits for both — a pair page is
        /// never half-visible, and the already-capped side deliberately exceeds this soft limit.
        /// </summary>
        [Test]
        public static void AsymmetricPairStillEmitsForBothParticipants()
        {
            int day = CurrentDayIndex();
            SetCount(firstPawn, day, TestSoftCap);
            SetCount(secondPawn, day, 0);

            scope.FireAndRequireEvent(
                () => AddDeepTalkRow(firstPawn, secondPawn), "DeepTalk", firstPawn, secondPawn);

            RequireCount(firstPawn, day, TestSoftCap + 1, "the capped side still advances after emitting");
            RequireCount(secondPawn, day, 1, "the side with room advances normally");
            PawnDiaryRimTestScope.Require(DigestLineCount(firstPawn, day) == 0,
                "A page that emitted must never also be folded into a digest.");
        }

        /// <summary>EVT-26. An important group is exempt: it emits however full the day already is.</summary>
        [Test]
        public static void ImportantGroupIsNeverPaced()
        {
            int day = CurrentDayIndex();
            heartfeltGroup.important = true;
            SetCount(firstPawn, day, TestSoftCap + 5);
            SetCount(secondPawn, day, TestSoftCap + 5);

            scope.FireAndRequireEvent(
                () => AddDeepTalkRow(firstPawn, secondPawn), "DeepTalk", firstPawn, secondPawn);

            PawnDiaryRimTestScope.Require(DigestLineCount(firstPawn, day) == 0,
                "An important page must not leave a digest line behind.");
            RequireCount(firstPawn, day, TestSoftCap + 5,
                "An exempt page must not consume the low-salience allowance.");
        }

        /// <summary>EVT-26. A soft cap of zero restores the pre-B6 behavior exactly.</summary>
        [Test]
        public static void DisabledSoftCapEmitsEveryPage()
        {
            int day = CurrentDayIndex();
            DiaryTuning.Current.lowSalienceDailySoftCap = 0;
            SetCount(firstPawn, day, 99);
            SetCount(secondPawn, day, 99);

            scope.FireAndRequireEvent(
                () => AddDeepTalkRow(firstPawn, secondPawn), "DeepTalk", firstPawn, secondPawn);

            PawnDiaryRimTestScope.Require(DigestLineCount(firstPawn, day) == 0,
                "With pacing off nothing may be folded into a digest.");
        }

        /// <summary>
        /// EVT-26. The buffer keeps the four newest UNIQUE lines: an exact repeat is one memory, and a
        /// fifth distinct moment evicts the oldest rather than growing the save.
        /// </summary>
        [Test]
        public static void DigestBufferKeepsFourNewestUniqueLines()
        {
            int day = CurrentDayIndex();
            string pawnId = firstPawn.GetUniqueLoadID();
            for (int i = 1; i <= 5; i++)
            {
                scope.Component.AddDayDigestLine(pawnId, day, "thought", "moment " + i, i);
            }

            scope.Component.AddDayDigestLine(pawnId, day, "thought", "moment 5", 6);

            PawnDayDigestState state = scope.Component.DayDigestStateFor(pawnId, day);
            PawnDiaryRimTestScope.Require(state != null && state.lines.Count == TestDigestLines,
                "The digest buffer did not stop at its configured size.");
            PawnDiaryRimTestScope.Require(state.lines[0].line == "moment 2",
                "The buffer evicted the wrong end; the OLDEST line should go first.");
            PawnDiaryRimTestScope.Require(state.lines[TestDigestLines - 1].line == "moment 5",
                "The newest line is not last, so the buffer is no longer chronological.");
        }

        /// <summary>
        /// EVT-26. Digest lines are evidence, never a reason: a day holding nothing but folded-away
        /// moments still writes no reflection.
        /// </summary>
        [Test]
        public static void DigestOnlyDayWritesNoReflection()
        {
            int day = CurrentDayIndex();
            string pawnId = firstPawn.GetUniqueLoadID();
            ForceDeterministicReflectionTuning();
            scope.Component.AddDayDigestLine(pawnId, day, "interaction", "traded a quiet joke", 1);

            scope.RequireNoNewEvent(() => InvokeDayFlush(firstPawn));
        }

        /// <summary>
        /// EVT-26. When an important signal DOES earn the reflection, the buffered moments join it as
        /// context — tagged with the stable digest kind — and the flush consumes them so the next day
        /// cannot re-use them.
        /// </summary>
        [Test]
        public static void EarnedReflectionCarriesDigestEvidenceThenConsumesIt()
        {
            int day = CurrentDayIndex();
            string pawnId = firstPawn.GetUniqueLoadID();
            ForceDeterministicReflectionTuning();
            SeedImportantDayEvidence(day);
            scope.Component.AddDayDigestLine(pawnId, day, "work", "hauled steel all morning", 1);

            DiaryEvent reflection = scope.FireAndRequireEvent(
                () => InvokeDayFlush(firstPawn), "DayReflection", firstPawn, null);

            string context = reflection.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("digest:work", StringComparison.Ordinal) >= 0,
                "The reflection did not carry the buffered digest moment. Context: " + context);
            PawnDiaryRimTestScope.Require(DigestLineCount(firstPawn, day) == 0,
                "The flush must consume the digest lines, or they leak into another day.");
            RequireCount(firstPawn, day, 0,
                "Writing the evening reflection must NOT reset the day's pacing count.");
        }

        /// <summary>
        /// EVT-26. Pacing is saved state: the rollover prune drops yesterday's row (fresh allowance,
        /// forgotten moments) while today's row survives, which is what makes a mid-day reload safe.
        /// </summary>
        [Test]
        public static void DayRolloverDropsYesterdayButKeepsToday()
        {
            int day = CurrentDayIndex();
            string pawnId = firstPawn.GetUniqueLoadID();
            SetCount(firstPawn, day - 1, TestSoftCap);
            scope.Component.AddDayDigestLine(pawnId, day - 1, "thought", "yesterday's small talk", 1);
            SetCount(firstPawn, day, 1);
            scope.Component.AddDayDigestLine(pawnId, day, "thought", "today's small talk", 2);

            InvokeDayDigestPrune(day);

            PawnDiaryRimTestScope.Require(
                scope.Component.DayDigestStateFor(pawnId, day - 1) == null,
                "Yesterday's pacing row survived the rollover, so the allowance never resets.");
            PawnDayDigestState today = scope.Component.DayDigestStateFor(pawnId, day);
            PawnDiaryRimTestScope.Require(today != null && today.lowSalienceCount == 1,
                "Today's pacing row was discarded; a mid-day reload would hand out a fresh allowance.");
            PawnDiaryRimTestScope.Require(today.lines.Count == 1,
                "Today's buffered moments were discarded by the rollover prune.");
        }

        // ----- interaction driving -----------------------------------------------------------------

        private static void AddDeepTalkRow(Pawn initiator, Pawn recipient)
        {
            InteractionDef deepTalk = RequireDef("DeepTalk");
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                deepTalk, initiator, recipient);
            if (entry == null)
            {
                throw new AssertionException("Could not construct the vanilla DeepTalk PlayLog row.");
            }

            scope.TrackPlayLogEntry(entry);
            Find.PlayLog.Add(entry);
        }

        private static InteractionDef RequireDef(string defName)
        {
            InteractionDef def = DefDatabase<InteractionDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException("Base RimWorld InteractionDef '" + defName + "' is missing.");
            }

            return def;
        }

        private static DiaryInteractionGroupDef RequireGroup(string defName)
        {
            DiaryInteractionGroupDef group =
                DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail(defName);
            if (group == null)
            {
                throw new AssertionException("Diary interaction group '" + defName + "' is missing.");
            }

            return group;
        }

        // ----- pacing-store access -------------------------------------------------------------------

        private static void RequireCount(Pawn pawn, int day, int expected, string why)
        {
            int actual = scope.Component.LowSalienceCountForDay(pawn.GetUniqueLoadID(), day);
            PawnDiaryRimTestScope.Require(actual == expected,
                "Daily low-salience count was " + actual + ", expected " + expected + " (" + why + ").");
        }

        private static void SetCount(Pawn pawn, int day, int count)
        {
            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < count; i++)
            {
                scope.Component.RecordLowSalienceEmission(pawnId, day);
            }

            if (count == 0)
            {
                // Materialize the row so the test's own teardown always finds it.
                scope.Component.RecordLowSalienceEmission(pawnId, day);
                scope.Component.DayDigestStateFor(pawnId, day).lowSalienceCount = 0;
            }
        }

        private static int DigestLineCount(Pawn pawn, int day)
        {
            PawnDayDigestState state = scope.Component.DayDigestStateFor(pawn.GetUniqueLoadID(), day);
            return state?.lines == null ? 0 : state.lines.Count;
        }

        private static string FirstDigestLine(Pawn pawn, int day)
        {
            PawnDayDigestState state = scope.Component.DayDigestStateFor(pawn.GetUniqueLoadID(), day);
            return state?.lines == null || state.lines.Count == 0 ? string.Empty : state.lines[0].line;
        }

        private static string FirstDigestSourceKind(Pawn pawn, int day)
        {
            PawnDayDigestState state = scope.Component.DayDigestStateFor(pawn.GetUniqueLoadID(), day);
            return state?.lines == null || state.lines.Count == 0
                ? string.Empty
                : state.lines[0].sourceKind;
        }

        // ----- deterministic tuning + cleanup ---------------------------------------------------------

        private static void ForceLowSalienceGroup(DiaryInteractionGroupDef group)
        {
            bool originalImportant = group.important;
            bool originalCombat = group.combat;
            group.important = false;
            group.combat = false;
            scope.RegisterCleanup(() =>
            {
                group.important = originalImportant;
                group.combat = originalCombat;
            });
        }

        private static void ForcePacingTuning(int cap, int maxLines)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            int originalCap = tuning.lowSalienceDailySoftCap;
            int originalLines = tuning.dayDigestMaxLines;
            tuning.lowSalienceDailySoftCap = cap;
            tuning.dayDigestMaxLines = maxLines;
            scope.RegisterCleanup(() =>
            {
                tuning.lowSalienceDailySoftCap = originalCap;
                tuning.dayDigestMaxLines = originalLines;
            });
        }

        // The reflection tests need the daily reflection to be the ONLY one that can fire, and the
        // seeded hediff to be the one kind that can justify it (mirrors EVT-19's device).
        private static void ForceDeterministicReflectionTuning()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            bool savedDay = tuning.daySummaryEnabled;
            bool savedQuadrum = tuning.quadrumReflectionEnabled;
            bool savedArc = tuning.arcReflectionEnabled;
            List<string> savedKinds = tuning.daySummaryImportantSignalKinds;

            tuning.daySummaryEnabled = true;
            tuning.quadrumReflectionEnabled = false;
            tuning.arcReflectionEnabled = false;
            tuning.daySummaryImportantSignalKinds = new List<string> { "hediff" };
            SuppressColonyNewsForThisTest();

            string pawnId = firstPawn.GetUniqueLoadID();
            scope.RegisterCleanup(() =>
            {
                tuning.daySummaryEnabled = savedDay;
                tuning.quadrumReflectionEnabled = savedQuadrum;
                tuning.arcReflectionEnabled = savedArc;
                tuning.daySummaryImportantSignalKinds = savedKinds;
                ScrubGuardSet("writtenDayReflections", pawnId);
                ScrubGuardSet("writtenQuadrumReflections", pawnId);
            });
        }

        // The colony-news collector reads the DEVELOPER's real letter archive, so a letter their colony
        // filed today would be a legitimate extra candidate. Switch that one collector off outright.
        private static void SuppressColonyNewsForThisTest()
        {
            DiaryContextReactionDef policy =
                DiaryContextReactions.ForKey(DiaryContextReactions.ColonyNews);
            if (policy == null)
            {
                throw new AssertionException("Could not resolve the colony-news reaction policy.");
            }

            bool saved = policy.enabled;
            policy.enabled = false;
            scope.RegisterCleanup(() => policy.enabled = saved);
        }

        // Seeds the one important day-evidence signal the reflection needs, using the same private
        // pendingDayHediffs store the live AddHediff hook fills (mirrors EVT-19).
        private static void SeedImportantDayEvidence(int day)
        {
            const string SeedHediffDefName = "PawnDiaryDigestPacingTestHediff";
            Type recordType = typeof(DiaryGameComponent).GetNestedType(
                "DayHediffRecord", BindingFlags.NonPublic);
            if (recordType == null)
            {
                throw new AssertionException("Could not locate DiaryGameComponent.DayHediffRecord.");
            }

            object record = Activator.CreateInstance(recordType);
            SetRecordField(recordType, record, "defName", SeedHediffDefName);
            SetRecordField(recordType, record, "label", "a test affliction");
            SetRecordField(recordType, record, "weight", 1f);
            SetRecordField(recordType, record, "progressed", false);

            Type listType = typeof(List<>).MakeGenericType(recordType);
            object list = Activator.CreateInstance(listType);
            listType.GetMethod("Add").Invoke(list, new[] { record });

            string key = firstPawn.GetUniqueLoadID() + "|" + day;
            IDictionary pending = PendingDayHediffs();
            pending[key] = list;
            scope.RegisterCleanup(() => PendingDayHediffs().Remove(key));
        }

        private static void SetRecordField(Type type, object instance, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new AssertionException("Field '" + fieldName + "' is missing on " + type.Name + ".");
            }

            field.SetValue(instance, value);
        }

        private static IDictionary PendingDayHediffs()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField("pendingDayHediffs", PrivateInstance);
            IDictionary dictionary = field?.GetValue(scope.Component) as IDictionary;
            if (dictionary == null)
            {
                throw new AssertionException("Could not read DiaryGameComponent.pendingDayHediffs.");
            }

            return dictionary;
        }

        private static void InvokeDayFlush(Pawn target)
        {
            MethodInfo flush = typeof(DiaryGameComponent).GetMethod(
                "FlushDaySummaryForPawn", PrivateInstance);
            if (flush == null)
            {
                throw new AssertionException("Could not locate DiaryGameComponent.FlushDaySummaryForPawn.");
            }

            flush.Invoke(scope.Component, new object[] { target });
        }

        private static void InvokeDayDigestPrune(int currentDay)
        {
            MethodInfo prune = typeof(DiaryGameComponent).GetMethod(
                "PruneStaleDayDigest", PrivateInstance);
            if (prune == null)
            {
                throw new AssertionException("Could not locate DiaryGameComponent.PruneStaleDayDigest.");
            }

            prune.Invoke(scope.Component, new object[] { currentDay });
        }

        private static void ScrubGuardSet(string fieldName, string pawnId)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, PrivateInstance);
            HashSet<string> set = field?.GetValue(scope.Component) as HashSet<string>;
            set?.RemoveWhere(key => key != null && key.IndexOf(pawnId, StringComparison.Ordinal) >= 0);
        }

        // B6 pacing rows are SAVED, so leftovers would follow the developer's colony into their next
        // save. Remove every row this suite could have created for its own pawns.
        private static void RegisterPacingRowCleanup()
        {
            string firstId = firstPawn.GetUniqueLoadID();
            string secondId = secondPawn.GetUniqueLoadID();
            DiaryGameComponent component = scope.Component;
            scope.RegisterCleanup(() => RemovePacingRowsFor(component, firstId, secondId));
        }

        private static void RemovePacingRowsFor(DiaryGameComponent component, params string[] pawnIds)
        {
            FieldInfo listField = typeof(DiaryGameComponent).GetField("dayDigestStates", PrivateInstance);
            List<PawnDayDigestState> rows = listField?.GetValue(component) as List<PawnDayDigestState>;
            if (rows == null)
            {
                return;
            }

            rows.RemoveAll(row => row != null && Array.IndexOf(pawnIds, row.pawnId) >= 0);
            MethodInfo rebuild = typeof(DiaryGameComponent).GetMethod(
                "RebuildDayDigestIndex", PrivateInstance);
            rebuild?.Invoke(component, null);
        }

        // Matches DiaryGameComponent.CurrentDayIndex: absolute in-game day from the world clock.
        private static int CurrentDayIndex()
        {
            return Find.TickManager.TicksAbs / GenDate.TicksPerDay;
        }
    }
}
