// In-game orchestration tests for the canonical Biotech growth owner. Pure tests already prove the
// before/after diff; this fixture proves the impure boundary creates one family-keyed page, attaches N1
// source evidence, consumes progression baselines, and releases the mature Birthday fallback when the
// canonical row is disabled. The test pawn is generation-disabled, so no LLM request can leave the game.
//
// Most tests use detached snapshots rather than opening vanilla's growth-choice UI. Focused cases now
// invoke vanilla ConfigureGrowthLetter/MakeChoices for ages 7/10/13, the source-owned auto-resolution
// branch, and a Scribe-round-tripped postponed owner. Only the visual letter interaction remains manual.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PawnDiary.Capture;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves canonical emission, real letter hooks, loaded prompts, family context, once-only consumption,
    /// ordinary fallback, and disabled-page observation against a loaded component and isolated pawn.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryBiotechGrowthFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AllInstance = PrivateInstance | BindingFlags.Public;
        private const string PreCapFixturePrefix = "PawnDiary_RimTest_PreCapGrowth_";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static MethodInfo completeGrowthMethod;
        private static MethodInfo findDiaryMethod;
        private static MethodInfo configureGrowthLetterMethod;
        private static MethodInfo makeGrowthChoicesMethod;
        private static FieldInfo pendingGrowthField;

        /// <summary>Creates a no-generation pawn and enables the shared Progression signal.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            pawn = scope.CreateAdultColonist();
            completeGrowthMethod = typeof(DiaryGameComponent).GetMethod(
                "CompleteBiotechGrowth",
                PrivateInstance);
            findDiaryMethod = typeof(DiaryGameComponent).GetMethod("FindDiary", PrivateInstance);
            configureGrowthLetterMethod = typeof(ChoiceLetter_GrowthMoment).GetMethod(
                "ConfigureGrowthLetter",
                AllInstance,
                null,
                new[]
                {
                    typeof(Pawn), typeof(int), typeof(int), typeof(int), typeof(List<string>), typeof(Name)
                },
                null);
            makeGrowthChoicesMethod = typeof(ChoiceLetter_GrowthMoment).GetMethod(
                "MakeChoices",
                AllInstance,
                null,
                new[] { typeof(List<SkillDef>), typeof(Trait) },
                null);
            pendingGrowthField = typeof(DiaryGameComponent).GetField(
                "pendingBiotechGrowthMoments",
                PrivateInstance);
            RequireHandle(completeGrowthMethod, "CompleteBiotechGrowth");
            RequireHandle(findDiaryMethod, "FindDiary");
            RequireHandle(configureGrowthLetterMethod, "ChoiceLetter_GrowthMoment.ConfigureGrowthLetter");
            RequireHandle(makeGrowthChoicesMethod, "ChoiceLetter_GrowthMoment.MakeChoices");
            RequireHandle(pendingGrowthField, "pendingBiotechGrowthMoments");
            scope.RegisterCleanup(RemovePendingRowsForTestPawn);

            DiarySignalPolicyDef signal = DiarySignalPolicies.ForKey(DiarySignalPolicies.Progression);
            bool original = signal.enabled;
            signal.enabled = true;
            scope.RegisterCleanup(() => signal.enabled = original);
        }

        /// <summary>Restores settings, removes test events/diary state, and destroys the isolated pawn.</summary>
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
                pawn = null;
                completeGrowthMethod = null;
                findDiaryMethod = null;
                configureGrowthLetterMethod = null;
                makeGrowthChoicesMethod = null;
                pendingGrowthField = null;
            }
        }

        /// <summary>
        /// A verified age-13 mutation creates exactly one canonical solo page, carries qualitative
        /// context plus source-owned identity evidence, consumes every post-choice trait, advances the
        /// newly passionate skill baseline, and rejects a second completion for the same pawn/age.
        /// </summary>
        [Test]
        public static void CanonicalGrowthEmitsOnceAndConsumesProgressionBaselines()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", true);
            SetGroupEnabled("eventWindowBirthday", true);
            GrowthFixture fixture = Fixture(age: 13);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeComplete(fixture),
                BiotechEventDefNames.GrowthMoment,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContext(diaryEvent, "growth_moment=true");
            RequireContext(diaryEvent, "growth_stage=age_13");
            RequireContext(diaryEvent, "opportunity_band=");
            RequireContext(diaryEvent, "family_arc_id=biotech-family|");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext.IndexOf("growth_tier=", StringComparison.Ordinal) < 0,
                "Canonical growth context leaked the private numeric growth tier.");

            List<NarrativeEvidence> evidence = diaryEvent.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                evidence.Exists(item => item != null
                    && item.facet == NarrativeFacetTokens.IdentityTransition
                    && item.sourceDomain == "biotech_growth"
                    && item.sourceDefName == BiotechEventDefNames.GrowthMoment),
                "Canonical growth page did not retain its source-owned N1 identity-transition evidence.");

            PawnProgressionState progression = ProgressionState();
            PawnDiaryRimTestScope.Require(
                progression.EnsureBiotechState().HasConsumedGrowthAge(13),
                "Canonical age 13 was not marked consumed.");
            PawnDiaryRimTestScope.Require(
                progression.knownTraitKeys.Contains("Kind|0")
                    && progression.knownTraitKeys.Contains("Tough|0")
                    && progression.knownTraitKeys.Contains("Bisexual|0"),
                "Post-growth trait baselines did not consume selected plus automatic age-13 traits.");
            PawnDiaryRimTestScope.Require(
                progression.HighestSkillMilestone("Shooting") > 0,
                "Newly passionate high-skill subject did not advance its saved milestone baseline.");

            scope.RequireNoNewEvent(() => InvokeComplete(fixture));

            // Simulate a damaged/legacy nested row while the canonical event itself survived. The
            // durable event is still authoritative: completion must rebuild consumption, not release a
            // Birthday or add a second growth page.
            progression.EnsureBiotechState().consumedGrowthAges.Clear();
            scope.RequireNoNewEvent(() => InvokeComplete(fixture));
            PawnDiaryRimTestScope.Require(
                progression.EnsureBiotechState().HasConsumedGrowthAge(13),
                "Existing canonical page did not repair a missing consumed-age marker.");
        }

        /// <summary>
        /// An explicit canonical-group opt-out with Birthday enabled releases one ordinary page only
        /// after completion, while still consuming the exact growth state.
        /// </summary>
        [Test]
        public static void DisabledCanonicalGroupReleasesOneOrdinaryBirthday()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", false);
            SetGroupEnabled("eventWindowBirthday", true);
            GrowthFixture fixture = Fixture(age: 10);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeComplete(fixture),
                "Birthday",
                pawn,
                null);
            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                ProgressionState().EnsureBiotechState().HasConsumedGrowthAge(10),
                "Fallback birthday did not consume canonical growth ownership.");

            scope.RequireNoNewEvent(() => InvokeComplete(fixture));
        }

        /// <summary>
        /// Disabling both canonical growth and Birthday creates no page, but observation and dedup state
        /// still advance so the periodic trait/skill scanners cannot replay the same mutation.
        /// </summary>
        [Test]
        public static void BothGroupsDisabledStillConsumeWithoutPage()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", false);
            SetGroupEnabled("eventWindowBirthday", false);
            GrowthFixture fixture = Fixture(age: 7);

            scope.RequireNoNewEvent(() => InvokeComplete(fixture));
            PawnProgressionState progression = ProgressionState();
            PawnDiaryRimTestScope.Require(
                progression.EnsureBiotechState().HasConsumedGrowthAge(7),
                "Disabled growth observation did not mark age 7 consumed.");
            PawnDiaryRimTestScope.Require(
                progression.knownTraitKeys.Contains("Tough|0"),
                "Disabled growth observation did not consume the post-choice trait baseline.");
        }

        /// <summary>
        /// Invokes the real vanilla ConfigureGrowthLetter and MakeChoices methods, allowing the installed
        /// Harmony callbacks to carry one birthday through configured ownership and a committed NoTrait
        /// choice. This is the loaded-game boundary that detached completion fixtures cannot exercise.
        /// </summary>
        [Test]
        public static void VanillaGrowthLetterHooksClaimAndCompleteNoTraitChoice()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth hooks] not applicable (Biotech inactive).");
                return;
            }

            PawnDiaryRimTestScope.Require(
                BiotechGrowthLetterPatch.HooksReady,
                "The complete RimWorld 1.6 growth-letter hook set was not registered.");
            SetGroupEnabled("progressionGrowthMoment", true);
            SetGroupEnabled("eventWindowBirthday", true);

            BiotechGrowthBirthdayState birthday = scope.Component.BeginBiotechGrowthBirthday(pawn, 7);
            PawnDiaryRimTestScope.Require(
                birthday != null,
                "The canonical growth owner refused a valid age-7 birthday fixture.");

            ChoiceLetter_GrowthMoment letter = new ChoiceLetter_GrowthMoment();
            BiotechGrowthCorrelation.BeginBirthday(birthday);
            try
            {
                Invoke(configureGrowthLetterMethod, letter, new object[]
                {
                    pawn,
                    8,
                    1,
                    0,
                    new List<string>(),
                    pawn.Name
                });
            }
            finally
            {
                BiotechGrowthCorrelation.EndBirthday(birthday);
            }

            PawnDiaryRimTestScope.Require(
                birthday.configuredLetterOwnsBirthday,
                "The configured vanilla growth letter did not claim Birthday ownership.");
            PawnDiaryRimTestScope.Require(
                ReferenceEquals(BiotechGrowthLetterPatch.LetterPawn(letter), pawn),
                "The registered growth-letter pawn field did not resolve the configured pawn.");
            PawnDiaryRimTestScope.Require(
                PendingGrowthRows().Exists(row => row != null
                    && row.pawnId == pawn.GetUniqueLoadID()
                    && row.birthdayAge == 7),
                "The configured vanilla growth letter did not create a saved pending owner.");

            SkillDef selectedSkill = FirstPassionGainCandidate(pawn);
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => Invoke(makeGrowthChoicesMethod, letter, new object[]
                {
                    new List<SkillDef> { selectedSkill },
                    null // NoTrait is a valid committed growth choice.
                }),
                BiotechEventDefNames.GrowthMoment,
                pawn,
                null);

            RequireContext(diaryEvent, "growth_stage=age_7");
            RequireContext(diaryEvent, "new_interest_1=");
            PawnDiaryRimTestScope.Require(
                !PendingGrowthRows().Exists(row => row != null
                    && row.pawnId == pawn.GetUniqueLoadID()
                    && row.birthdayAge == 7),
                "The committed vanilla choice left its pending growth owner behind.");
        }

        /// <summary>
        /// The real vanilla letter boundary handles every canonical age, two simultaneous passion gains,
        /// an age-13 nickname, and newly opened responsibilities without leaving an owner or duplicate.
        /// </summary>
        [Test]
        public static void VanillaGrowthLettersCoverAllAgesMultiplePassionsAndVisibleChanges()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth hooks] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", true);
            SetGroupEnabled("eventWindowBirthday", true);
            int[] ages = { 7, 10, 13 };
            for (int i = 0; i < ages.Length; i++)
            {
                int age = ages[i];
                // Use a fresh pawn per age so the selected two passions from age 7 cannot exhaust the
                // candidate pool for the age-10/13 fixture on a restrictive random backstory.
                Pawn subject = i == 0 ? pawn : scope.CreateAdultColonist();
                BiotechGrowthBirthdayState birthday = scope.Component.BeginBiotechGrowthBirthday(subject, age);
                PawnDiaryRimTestScope.Require(birthday != null,
                    "The canonical growth owner refused age " + age + ".");

                ChoiceLetter_GrowthMoment letter = new ChoiceLetter_GrowthMoment();
                BiotechGrowthCorrelation.BeginBirthday(birthday);
                try
                {
                    Invoke(configureGrowthLetterMethod, letter, new object[]
                    {
                        subject,
                        2,
                        1,
                        2,
                        age == 13 ? new List<string> { "Construction" } : new List<string>(),
                        subject.Name
                    });
                }
                finally
                {
                    BiotechGrowthCorrelation.EndBirthday(birthday);
                }

                List<SkillDef> selectedSkills = PassionGainCandidates(subject, 2);
                if (age == 13)
                {
                    subject.Name = new NameTriple("RimTest", "B1 Growth Nickname", "Diary");
                }

                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => Invoke(makeGrowthChoicesMethod, letter, new object[] { selectedSkills, null }),
                    BiotechEventDefNames.GrowthMoment,
                    subject,
                    null);

                RequireContext(diaryEvent, "growth_stage=age_" + age);
                RequireContext(diaryEvent, "new_interest_1=");
                RequireContext(diaryEvent, "new_interest_2=");
                if (age == 13)
                {
                    RequireContext(diaryEvent, "nickname_changed=true");
                    RequireContext(diaryEvent, "new_responsibilities=true");
                }

                PawnDiaryRimTestScope.Require(
                    !PendingGrowthRows().Exists(row => row != null
                        && row.pawnId == subject.GetUniqueLoadID()
                        && row.birthdayAge == age),
                    "Age " + age + " left its committed pending growth owner behind.");
            }
        }

        /// <summary>
        /// A birthday that changes live growth state without creating a choice letter enters the exact
        /// auto-resolved branch and still owns one canonical page.
        /// </summary>
        [Test]
        public static void AutoResolvedGrowthOwnsOneCanonicalPage()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech auto growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", true);
            SetGroupEnabled("eventWindowBirthday", true);
            BiotechGrowthBirthdayState birthday = scope.Component.BeginBiotechGrowthBirthday(pawn, 7);
            PawnDiaryRimTestScope.Require(birthday != null,
                "The auto-resolution fixture could not start birthday ownership.");

            SkillRecord changed = FirstPassionGainRecord(pawn);
            changed.passion = changed.passion == Passion.None ? Passion.Minor : Passion.Major;
            bool owned = false;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => owned = scope.Component.TryFinishBiotechGrowthBirthday(birthday),
                BiotechEventDefNames.GrowthMoment,
                pawn,
                null);

            PawnDiaryRimTestScope.Require(owned,
                "The verified auto-resolved mutation did not claim the birthday.");
            RequireContext(diaryEvent, "growth_stage=age_7");
            RequireContext(diaryEvent, "new_interest_1=");
            scope.RequireNoNewEvent(() => scope.Component.TryFinishBiotechGrowthBirthday(birthday));
        }

        /// <summary>
        /// A configured pending owner survives a real Scribe round trip, then a re-created vanilla letter
        /// finds that original birthday row and completes it exactly once.
        /// </summary>
        [Test]
        public static void PostponedGrowthOwnerSurvivesScribeAndCompletes()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech postponed growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", true);
            BiotechGrowthBirthdayState birthday = scope.Component.BeginBiotechGrowthBirthday(pawn, 10);
            PawnDiaryRimTestScope.Require(birthday != null,
                "The postponed-owner fixture could not start birthday ownership.");
            ChoiceLetter_GrowthMoment originalLetter = new ChoiceLetter_GrowthMoment();
            BiotechGrowthCorrelation.BeginBirthday(birthday);
            try
            {
                Invoke(configureGrowthLetterMethod, originalLetter, new object[]
                {
                    pawn, 1, 1, 1, new List<string>(), pawn.Name
                });
            }
            finally
            {
                BiotechGrowthCorrelation.EndBirthday(birthday);
            }

            PendingBiotechGrowthMoment pending = PendingBiotechGrowthMomentPolicy.FindNewest(
                PendingGrowthRows(), pawn.GetUniqueLoadID(), 10);
            PawnDiaryRimTestScope.Require(pending != null,
                "The configured postponed letter did not create a saved owner.");
            PendingBiotechGrowthMoment loaded = ScribeRoundTrip(pending);
            PendingGrowthRows().RemoveAll(row => row != null && row.pawnId == pawn.GetUniqueLoadID());
            PendingGrowthRows().Add(loaded);

            ChoiceLetter_GrowthMoment reloadedLetter = new ChoiceLetter_GrowthMoment();
            Invoke(configureGrowthLetterMethod, reloadedLetter, new object[]
            {
                pawn, 1, 1, 1, new List<string>(), pawn.Name
            });
            SkillDef selectedSkill = FirstPassionGainCandidate(pawn);
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => Invoke(makeGrowthChoicesMethod, reloadedLetter,
                    new object[] { new List<SkillDef> { selectedSkill }, null }),
                BiotechEventDefNames.GrowthMoment,
                pawn,
                null);

            RequireContext(diaryEvent, "growth_stage=age_10");
            PawnDiaryRimTestScope.Require(
                !PendingGrowthRows().Exists(row => row != null && row.pawnId == pawn.GetUniqueLoadID()),
                "The Scribe-restored postponed owner remained after its committed choice.");
        }

        /// <summary>
        /// The real component preserves every established pre-cap owner, rejects a new letter without
        /// eviction, and admits/completes that same new owner after fixture rows make room.
        /// </summary>
        [Test]
        public static void LivePreCapGrowthAdmissionRejectsThenResumes()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech pre-cap] not applicable (Biotech inactive).");
                return;
            }

            int limit = DiaryBiotechPolicy.Snapshot().maximumPendingGrowthRows;
            List<PendingBiotechGrowthMoment> rows = PendingGrowthRows();
            if (rows.Count >= limit)
            {
                Log.Message("[PawnDiary RimTest Biotech pre-cap] skipped: the loaded save already owns "
                    + rows.Count + " pending growth rows.");
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            int required = limit + 1 - rows.Count;
            for (int i = 0; i < required; i++)
            {
                string id = PreCapFixturePrefix + i;
                rows.Add(ValidPendingGrowth(id, 10, now));
            }

            HashSet<string> establishedIds = PendingRowIds(PendingGrowthRows());
            BiotechGrowthBirthdayState rejected = ConfigureOwnedLetter(7, out _);
            PawnDiaryRimTestScope.Require(rejected != null && !rejected.configuredLetterOwnsBirthday,
                "An over-limit live component incorrectly admitted a new growth owner.");
            HashSet<string> afterReject = PendingRowIds(PendingGrowthRows());
            PawnDiaryRimTestScope.Require(establishedIds.SetEquals(afterReject),
                "Rejecting a new over-limit owner changed the established pending set.");

            rows = PendingGrowthRows();
            rows.RemoveAll(row => row != null
                && row.pawnId != null
                && row.pawnId.StartsWith(PreCapFixturePrefix, StringComparison.Ordinal));
            PawnDiaryRimTestScope.Require(rows.Count < limit,
                "The fixture could not return the live component below its admission limit.");

            BiotechGrowthBirthdayState admitted = ConfigureOwnedLetter(7, out ChoiceLetter_GrowthMoment letter);
            PawnDiaryRimTestScope.Require(admitted != null && admitted.configuredLetterOwnsBirthday,
                "Growth ownership did not resume after room returned below the admission limit.");
            SkillDef selectedSkill = FirstPassionGainCandidate(pawn);
            scope.FireAndRequireEvent(
                () => Invoke(makeGrowthChoicesMethod, letter,
                    new object[] { new List<SkillDef> { selectedSkill }, null }),
                BiotechEventDefNames.GrowthMoment,
                pawn,
                null);
        }

        /// <summary>
        /// Loaded XML templates preserve the central B1 growth facts under every context-detail preset,
        /// while private save/correlation identifiers never enter the captured model prompt.
        /// </summary>
        [Test]
        public static void GrowthPromptsStayTruthfulAcrossAllDetailPresets()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth prompts] not applicable (Biotech inactive).");
                return;
            }

            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(pawn, true);
            SetGroupEnabled("progressionGrowthMoment", true);
            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            int[] ages = { 7, 10, 13 };

            for (int i = 0; i < levels.Length; i++)
            {
                PawnDiaryMod.Settings.contextDetailLevel = levels[i];
                GrowthFixture fixture = Fixture(ages[i]);
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => InvokeComplete(fixture),
                    BiotechEventDefNames.GrowthMoment,
                    pawn,
                    null);
                string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
                string suffix = " (" + levels[i] + ")";

                RequirePromptContains(prompt, "tough", "chosen trait" + suffix);
                RequirePromptContains(prompt, "shooting", "chosen interest" + suffix);
                RequirePromptOmits(prompt, pawn.GetUniqueLoadID(), "pawn Thing ID" + suffix);
                RequirePromptOmits(prompt, "biotech-family|", "family arc ID" + suffix);
                RequirePromptOmits(prompt, "growth_tier=", "numeric growth tier" + suffix);
                RequirePromptOmits(prompt, fixture.mutation.correlationId, "correlation token" + suffix);
            }
        }

        private static GrowthFixture Fixture(int age)
        {
            string pawnId = pawn.GetUniqueLoadID();
            GrowthPawnSnapshot before = Snapshot(pawnId, age, "Alex", "none", "Kind|0");
            GrowthPawnSnapshot after = Snapshot(
                pawnId,
                age,
                age == 13 ? "Lex" : "Alex",
                "minor",
                "Kind|0",
                "Tough|0",
                age == 13 ? "Bisexual|0" : string.Empty);
            after.hasNewResponsibilities = age == 13;
            GrowthMomentMutation mutation = GrowthMomentPolicy.Diff(
                before,
                after,
                new GrowthCommittedChoice
                {
                    selectedTraitKey = "Tough|0",
                    selectedPassionSkillDefNames = new List<string> { "Shooting" },
                    sourceToken = BiotechGrowthSourceTokens.PlayerChoice
                },
                DiaryBiotechPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(mutation != null, "Growth fixture did not produce a verified mutation.");
            return new GrowthFixture { before = before, after = after, mutation = mutation, age = age };
        }

        private static GrowthPawnSnapshot Snapshot(
            string pawnId,
            int age,
            string shortName,
            string shootingPassion,
            params string[] traitKeys)
        {
            GrowthPawnSnapshot snapshot = new GrowthPawnSnapshot
            {
                pawnId = pawnId,
                displayName = pawn.LabelShortCap,
                biologicalAge = age,
                growthTier = 6,
                shortName = shortName,
                skills = new List<GrowthSkillFact>
                {
                    new GrowthSkillFact
                    {
                        skillDefName = "Shooting",
                        label = "shooting",
                        passion = shootingPassion,
                        level = 20
                    }
                }
            };
            for (int i = 0; i < traitKeys.Length; i++)
            {
                string key = traitKeys[i];
                if (!string.IsNullOrWhiteSpace(key))
                {
                    snapshot.traits.Add(new GrowthTraitFact
                    {
                        traitKey = key,
                        label = key.Split('|')[0].ToLowerInvariant(),
                        description = "verified growth trait"
                    });
                }
            }

            return snapshot;
        }

        private static void InvokeComplete(GrowthFixture fixture)
        {
            Invoke(completeGrowthMethod, new object[]
            {
                pawn,
                fixture.before,
                fixture.after,
                fixture.mutation,
                fixture.age
            });
        }

        private static PawnProgressionState ProgressionState()
        {
            PawnDiaryRecord diary = Invoke(findDiaryMethod, new object[] { pawn, false }) as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Growth fixture could not resolve the test pawn's diary record.");
            }

            return diary.EnsureProgressionState();
        }

        private static object Invoke(MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(scope.Component, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static object Invoke(MethodInfo method, object instance, object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static SkillDef FirstPassionGainCandidate(Pawn subject)
        {
            return FirstPassionGainRecord(subject).def;
        }

        private static SkillRecord FirstPassionGainRecord(Pawn subject)
        {
            if (subject?.skills?.skills != null)
            {
                for (int i = 0; i < subject.skills.skills.Count; i++)
                {
                    SkillRecord skill = subject.skills.skills[i];
                    if (skill != null && !skill.TotallyDisabled && skill.passion != Passion.Major)
                    {
                        return skill;
                    }
                }
            }

            throw new AssertionException("The generated growth pawn had no enabled skill below major passion.");
        }

        private static List<SkillDef> PassionGainCandidates(Pawn subject, int count)
        {
            List<SkillDef> result = new List<SkillDef>();
            if (subject?.skills?.skills != null)
            {
                for (int i = 0; i < subject.skills.skills.Count && result.Count < count; i++)
                {
                    SkillRecord skill = subject.skills.skills[i];
                    if (skill != null && !skill.TotallyDisabled && skill.passion != Passion.Major)
                    {
                        result.Add(skill.def);
                    }
                }
            }

            if (result.Count != count)
            {
                throw new AssertionException("The generated growth pawn did not have " + count
                    + " enabled skills below major passion.");
            }

            return result;
        }

        private static BiotechGrowthBirthdayState ConfigureOwnedLetter(
            int age,
            out ChoiceLetter_GrowthMoment letter)
        {
            BiotechGrowthBirthdayState birthday = scope.Component.BeginBiotechGrowthBirthday(pawn, age);
            letter = new ChoiceLetter_GrowthMoment();
            if (birthday == null)
            {
                return null;
            }

            BiotechGrowthCorrelation.BeginBirthday(birthday);
            try
            {
                Invoke(configureGrowthLetterMethod, letter, new object[]
                {
                    pawn, 1, 1, 1, new List<string>(), pawn.Name
                });
            }
            finally
            {
                BiotechGrowthCorrelation.EndBirthday(birthday);
            }

            return birthday;
        }

        private static PendingBiotechGrowthMoment ValidPendingGrowth(string pawnId, int age, int tick)
        {
            return new PendingBiotechGrowthMoment
            {
                pawnId = pawnId,
                birthdayAge = age,
                birthdayTick = tick,
                configuredTick = tick,
                growthTier = 4,
                correlationId = "growth|" + pawnId + "|" + age,
                familyArcId = "biotech-family|" + pawnId,
                birthdaySnapshot = new GrowthPawnSnapshot
                {
                    pawnId = pawnId,
                    displayName = "Pre-cap growth owner",
                    biologicalAge = age,
                    growthTier = 4,
                    shortName = "Pre-cap"
                }
            };
        }

        private static HashSet<string> PendingRowIds(List<PendingBiotechGrowthMoment> rows)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                PendingBiotechGrowthMoment row = rows[i];
                if (row != null)
                {
                    result.Add((row.pawnId ?? string.Empty) + "|" + row.birthdayAge);
                }
            }

            return result;
        }

        private static PendingBiotechGrowthMoment ScribeRoundTrip(PendingBiotechGrowthMoment original)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "pawndiary_growth_rimtest_" + Guid.NewGuid().ToString("N") + ".xml");
            PendingBiotechGrowthMoment loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                PendingBiotechGrowthMoment saveRef = original;
                Scribe_Deep.Look(ref saveRef, "owner");
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "owner");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    Scribe.ForceStop();
                }

                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup: a locked test file must not hide the actual assertion.
                }
            }

            if (loaded == null)
            {
                throw new AssertionException("The postponed growth owner did not survive Scribe.");
            }

            return loaded;
        }

        private static List<PendingBiotechGrowthMoment> PendingGrowthRows()
        {
            List<PendingBiotechGrowthMoment> rows = pendingGrowthField?.GetValue(scope.Component)
                as List<PendingBiotechGrowthMoment>;
            if (rows == null)
            {
                throw new AssertionException("Could not inspect pending Biotech growth ownership.");
            }

            return rows;
        }

        private static void RemovePendingRowsForTestPawn()
        {
            if (scope?.Component == null || pawn == null || pendingGrowthField == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<PendingBiotechGrowthMoment> rows = pendingGrowthField.GetValue(scope.Component)
                as List<PendingBiotechGrowthMoment>;
            rows?.RemoveAll(row => row != null
                && (row.pawnId == pawnId
                    || (row.pawnId != null
                        && row.pawnId.StartsWith(PreCapFixturePrefix, StringComparison.Ordinal))));
            BiotechGrowthCorrelation.Clear();
        }

        private static void RequirePromptContains(string prompt, string fragment, string label)
        {
            PawnDiaryRimTestScope.Require(
                prompt != null && prompt.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0,
                "The growth prompt omitted " + label + " ('" + fragment + "').");
        }

        private static void RequirePromptOmits(string prompt, string fragment, string label)
        {
            if (string.IsNullOrEmpty(fragment))
            {
                return;
            }

            PawnDiaryRimTestScope.Require(
                prompt == null || prompt.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0,
                "The growth prompt leaked " + label + " ('" + fragment + "').");
        }

        private static void SetGroupEnabled(string defName, bool enabled)
        {
            PawnDiaryMod.Settings?.SetGroupEnabled(defName, enabled);
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Growth page context did not contain '" + fragment + "'.");
        }

        private static void RequireHandle(object handle, string member)
        {
            if (handle == null)
            {
                throw new AssertionException("Could not bind production growth member '" + member + "'.");
            }
        }

        private sealed class GrowthFixture
        {
            public GrowthPawnSnapshot before;
            public GrowthPawnSnapshot after;
            public GrowthMomentMutation mutation;
            public int age;
        }
    }
}
