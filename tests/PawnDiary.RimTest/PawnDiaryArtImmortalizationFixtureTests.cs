// Loaded-game fixture for art immortalization (Quality Wave §7.2, H6).
//
// The RULES — deed-identity verification, the ownership key, deterministic per-artwork sampling, and
// the three-tier writer order — are pure and covered headlessly by DiaryPipelineTests and
// DiaryCapturePolicyTests. What only a loaded game can prove is the wiring:
//   (a) the two CompArt members this feature reaches by name still exist with the shapes it assumes —
//       the public InitializeArt(ArtGenerationContext)/JustCreatedBy(Pawn) hooks and the private
//       TaleReference.tale field the exact deed identity is built from;
//   (b) the colony-wide ownership query really finds a claim in the hot store, and really fails
//       CLOSED on an identity it cannot compare;
//   (c) the diary-existence check the artist fallback depends on agrees with a real pawn's pages;
//   (d) the XML group, prompt Def, and localized strings all load.
//
// This fixture does not craft a real sculpture. Making one means running RimWorld's art generator
// against the player's own colony and leaving a Thing plus a Tale behind, and Tale rows cannot be
// cleanly removed from a live TaleManager. Watching a real artwork produce a page is therefore a
// hands-on row in tests/SAVE_COMPATIBILITY_SMOKETEST.md.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Pins the live-game half of H6: the patched CompArt surface, the colony-wide ownership and
    /// diary-existence queries, and the Def/localization wiring.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryArtImmortalizationFixtureTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn artPawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("artImmortalized");
            artPawn = scope.CreateAdultColonist();
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
                artPawn = null;
            }
        }

        /// <summary>
        /// Every RimWorld member this feature reaches by name must still exist. The two hooks are
        /// patched by attribute, so a renamed or re-overloaded method would fail at startup; the
        /// private tale field is read reflectively, and losing it silently disables the feature
        /// (deliberately — the alternative would be matching translated art prose).
        /// </summary>
        [Test]
        public static void PatchedCompArtSurfaceStillExists()
        {
            MethodInfo initializeArt = typeof(CompArt).GetMethod(
                nameof(CompArt.InitializeArt),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(ArtGenerationContext) },
                null);
            PawnDiaryRimTestScope.Require(initializeArt != null,
                "CompArt.InitializeArt(ArtGenerationContext) is gone; the H6 postfix cannot bind.");

            MethodInfo justCreatedBy = typeof(CompArt).GetMethod(
                nameof(CompArt.JustCreatedBy),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Pawn) },
                null);
            PawnDiaryRimTestScope.Require(justCreatedBy != null,
                "CompArt.JustCreatedBy(Pawn) is gone; the H6 artist-fallback postfix cannot bind.");

            PawnDiaryRimTestScope.Require(
                typeof(CompArt).GetProperty("TaleRef", BindingFlags.Instance | BindingFlags.Public) != null,
                "CompArt.TaleRef is gone; H6 cannot reach an artwork's deed.");

            FieldInfo taleField = typeof(TaleReference).GetField(
                "tale", BindingFlags.Instance | BindingFlags.NonPublic);
            PawnDiaryRimTestScope.Require(taleField != null && taleField.FieldType == typeof(Tale),
                "TaleReference.tale is gone or changed type; H6 can no longer build an exact deed "
                + "identity and now silently records nothing.");

            PawnDiaryRimTestScope.Require(
                typeof(Tale).GetField("id", BindingFlags.Instance | BindingFlags.Public) != null
                    && typeof(Tale).GetField("def", BindingFlags.Instance | BindingFlags.Public) != null,
                "Tale.id / Tale.def are gone; the exact deed identity cannot be composed.");
            PawnDiaryRimTestScope.Require(
                typeof(Tale).GetProperty("DominantPawn", BindingFlags.Instance | BindingFlags.Public) != null,
                "Tale.DominantPawn is gone; H6 cannot find the pawn a deed is about.");
            PawnDiaryRimTestScope.Require(
                typeof(Tale).GetMethod("Concerns", BindingFlags.Instance | BindingFlags.Public) != null,
                "Tale.Concerns is gone; H6 cannot find the colonists a deed involves.");
        }

        /// <summary>
        /// The colony-wide ownership query: an unclaimed deed is writable, a deed already carried by a
        /// live page is not, and an identity that cannot be compared fails CLOSED. Fail-closed is the
        /// important one — a key we cannot match is a key we could silently write about twice.
        /// </summary>
        [Test]
        public static void OwnershipQueryFindsLiveClaimsAndFailsClosedOnFuzzyIdentities()
        {
            DiaryGameComponent component = scope.Component;

            // An identity no page can carry: a real Tale ID is never negative.
            string unclaimed = ArtImmortalizationPolicy.TaleIdentity("PawnDiaryRimTest_Deed", 987654321);
            PawnDiaryRimTestScope.Require(
                ArtImmortalizationPolicy.IsVerifiedTaleIdentity(unclaimed),
                "The fixture could not compose a verifiable deed identity.");
            PawnDiaryRimTestScope.Require(!component.HasArtImmortalizationFor(unclaimed),
                "An unclaimed deed already reported an owner.");

            PawnDiaryRimTestScope.Require(component.HasArtImmortalizationFor("Killing of the pirate chief"),
                "A translated art label was treated as a comparable deed identity instead of failing closed.");
            PawnDiaryRimTestScope.Require(component.HasArtImmortalizationFor(null),
                "A missing deed identity was treated as unclaimed instead of failing closed.");
            PawnDiaryRimTestScope.Require(component.HasArtImmortalizationFor("PawnDiaryRimTest_Deed"),
                "A bare def name with no tale ID was treated as unclaimed instead of failing closed.");

            // Register a real page carrying that deed, exactly as ArtImmortalizedSignal would, and
            // prove the query sees it. This is the claim that silences every later artwork.
            DiaryEvent claim = component.AddSoloEvent(
                artPawn,
                null,
                ArtImmortalizedEventData.ArtImmortalizedDefName,
                "Immortalized in art",
                "fixture claim",
                string.Empty,
                ArtImmortalizedEventData.BuildGameContext(
                    "SculptureLarge", "Thing_PawnDiaryRimTestArt", "Fixture Piece", "Fixture Sculptor",
                    "PawnDiaryRimTest_Deed", unclaimed));
            PawnDiaryRimTestScope.Require(claim != null,
                "The fixture could not register its art-immortalization claim.");

            PawnDiaryRimTestScope.Require(component.HasArtImmortalizationFor(unclaimed),
                "A registered art page did not claim its deed, so a second artwork would write it again.");

            // A neighbouring deed ID must stay writable: ownership is exact, not prefix-based.
            PawnDiaryRimTestScope.Require(
                !component.HasArtImmortalizationFor(
                    ArtImmortalizationPolicy.TaleIdentity("PawnDiaryRimTest_Deed", 987654322)),
                "A different deed of the same kind was reported as already claimed.");
        }

        /// <summary>
        /// The diary-existence check behind the artist fallback. Art about someone who never wrote a
        /// line here must stay silent; art about someone who has pages may be written by its artist.
        /// </summary>
        [Test]
        public static void ColonyDiaryCheckAgreesWithRealPages()
        {
            DiaryGameComponent component = scope.Component;

            PawnDiaryRimTestScope.Require(!component.HasColonyDiaryFor("Thing_PawnDiaryRimTestNobody"),
                "A pawn with no diary at all reported colony pages.");
            PawnDiaryRimTestScope.Require(!component.HasColonyDiaryFor(null),
                "A missing pawn ID reported colony pages.");

            string pawnId = artPawn.GetUniqueLoadID();
            DiaryEvent page = component.AddSoloEvent(
                artPawn, null, ArtImmortalizedEventData.ArtImmortalizedDefName,
                "Immortalized in art", "fixture page", string.Empty,
                ArtImmortalizedEventData.BuildGameContext(
                    "SculptureLarge", "Thing_PawnDiaryRimTestArt2", "Fixture Piece", "Fixture Sculptor",
                    "PawnDiaryRimTest_Deed",
                    ArtImmortalizationPolicy.TaleIdentity("PawnDiaryRimTest_Deed", 987654323)));
            PawnDiaryRimTestScope.Require(page != null,
                "The fixture could not register the page its diary check depends on.");

            PawnDiaryRimTestScope.Require(component.HasColonyDiaryFor(pawnId),
                "A pawn with a live diary page reported no colony diary, so the artist fallback would "
                + "never fire for art about them.");
        }

        /// <summary>
        /// The XML group, prompt Def, catalog registration, and every localized string must load.
        /// A missing Keyed row would render its raw key straight into a diary page.
        /// </summary>
        [Test]
        public static void GroupPromptAndLocalizationAreWired()
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(
                GroupDomain.Interaction, ArtImmortalizedEventData.ArtImmortalizedDefName);
            PawnDiaryRimTestScope.Require(
                group != null && string.Equals(group.defName, "artImmortalized", StringComparison.Ordinal),
                "PawnDiary_ArtImmortalized does not classify to the 'artImmortalized' group (it fell "
                + "through to " + (group == null ? "nothing" : group.defName) + ").");
            PawnDiaryRimTestScope.Require(!group.important,
                "The art-immortalization group must stay non-important; it is a quiet aside, not a headline.");
            PawnDiaryRimTestScope.Require(group.defaultEnabled,
                "The art-immortalization group must be enabled by default.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(InteractionGroups.InstructionForGroup(group)),
                "The art-immortalization group has no loaded instruction.");

            PawnDiaryRimTestScope.Require(
                DiaryEventCatalog.Get(DiaryEventType.ArtImmortalized) != null,
                "DiaryEventType.ArtImmortalized has no registered catalog spec, so every art event "
                + "would be dropped silently.");

            PawnDiaryRimTestScope.Require(
                DefDatabase<DiaryEventPromptDef>.GetNamedSilentFail("DiaryEventPrompt_ArtImmortalized") != null,
                "The art-immortalization event prompt Def was not loaded.");

            RequireLocalized("PawnDiary.Event.ArtImmortalizedLabel", "PawnDiary.Event.ArtImmortalizedLabel".Translate());
            RequireLocalized("PawnDiary.Event.ArtImmortalizedFallbackTitle",
                "PawnDiary.Event.ArtImmortalizedFallbackTitle".Translate());
            string text = "PawnDiary.Event.ArtImmortalized".Translate("Nell", "The Long Watch").Resolve();
            RequireLocalized("PawnDiary.Event.ArtImmortalized", text);
            PawnDiaryRimTestScope.Require(
                text.IndexOf("Nell", StringComparison.Ordinal) >= 0
                    && text.IndexOf("The Long Watch", StringComparison.Ordinal) >= 0,
                "The art-immortalization text dropped one of its arguments.");
        }

        /// <summary>The shipped tuning must load with a usable chance, or the feature is dead on arrival.</summary>
        [Test]
        public static void ShippedTuningIsUsable()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            PawnDiaryRimTestScope.Require(tuning != null, "The Pawn Diary tuning Def was not loaded.");
            PawnDiaryRimTestScope.Require(tuning.artImmortalizationEnabled,
                "Art immortalization ships disabled.");
            PawnDiaryRimTestScope.Require(
                tuning.artImmortalizationChance > 0f && tuning.artImmortalizationChance <= 1f,
                "The shipped art-immortalization chance is outside (0,1], so the feature can never fire.");
        }

        private static void RequireLocalized(string key, string resolved)
        {
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(resolved)
                    && resolved.IndexOf(key, StringComparison.Ordinal) < 0,
                "Keyed string '" + key + "' did not resolve in the loaded language.");
        }
    }
}
