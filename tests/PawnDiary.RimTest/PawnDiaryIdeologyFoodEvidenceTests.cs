// Loaded-game contract coverage for the bounded Ideology Phase 2 food client. These fixtures keep
// page ownership in ThoughtSignal, drive the primitive scope's defensive branches, and exercise one
// real human-meat meal through the installed Harmony boundaries. Manual DLC profiles stay deferred.
using System;
using System.Linq;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves optional food evidence enriches one existing page and fails open.</summary>
    [TestSuite]
    public static class PawnDiaryIdeologyFoodEvidenceTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Ideology Food Evidence] ";
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("hediffPartGainedArtificial", "thoughtPositive");
            pawn = scope.CreateAdultColonist();
            FoodThoughtEvidenceAdapter.SetFailureForTests(false);
            scope.RegisterCleanup(() => FoodThoughtEvidenceAdapter.SetFailureForTests(false));
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                FoodThoughtEvidenceAdapter.SetFailureForTests(false);
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawn = null;
            }
        }

        /// <summary>
        /// Exact human-meat evidence changes neither page ownership nor dedup/Rand, reaches one frozen
        /// saved context, and disappears once the synchronous ingestion scope closes.
        /// </summary>
        [Test]
        public static void ExactHumanMeatEnrichesOneExistingFrozenThoughtPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "exact human-meat enrichment: not applicable (Ideology inactive). ");
                return;
            }

            InstallCannibalismStance();
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            PawnDiaryRimTestScope.Require(thoughtDef != null,
                "The food evidence fixture needs the base-game AteFineMeal thought.");
            Thought_Memory thought = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
            thought.pawn = pawn;

            ThoughtSignal ordinary = new ThoughtSignal(pawn, thought);
            ThoughtSignal enriched = null;
            FoodIngestionEvidenceScope foodScope = null;
            const int Seed = 742091;
            Rand.PushState(Seed);
            float expectedNext = Rand.Value;
            Rand.PopState();
            Rand.PushState(Seed);
            try
            {
                foodScope = OpenExactFoodScope(thoughtDef.defName);
                PawnDiaryRimTestScope.Require(
                    FoodIngestionEvidenceContext.CaptureForThought(
                        pawn.GetUniqueLoadID(), thoughtDef.defName + "_NotReturned") == null,
                    "Exact food evidence attached to a thought not returned by this ingestion.");
                enriched = new ThoughtSignal(pawn, thought);
                float actualNext = Rand.Value;
                PawnDiaryRimTestScope.Require(Math.Abs(actualNext - expectedNext) < 0.000001f,
                    "Exact food enrichment consumed global Rand.");
            }
            finally
            {
                FoodIngestionEvidenceContext.End(foodScope);
                Rand.PopState();
            }

            PawnDiaryRimTestScope.Require(enriched != null && enriched.Payload != null,
                "Exact food evidence cancelled the already-valid ThoughtSignal.");
            PawnDiaryRimTestScope.Require(enriched.DedupKey == ordinary.DedupKey,
                "Exact food evidence changed the existing thought dedup key.");
            BeliefEventEvidence evidence = enriched.CapturedBeliefEvidence;
            PawnDiaryRimTestScope.Require(evidence != null && evidence.groupKey == "cannibal_meal",
                "Exact human-meat evidence did not attach the XML-owned food group.");
            PawnDiaryRimTestScope.Require(evidence.matchFields.Any(field => field != null
                    && field.field == "ingredient_label" && field.value == "human meat"),
                "Exact human-meat evidence did not freeze ingredient_label=human meat.");
            PawnDiaryRimTestScope.Require(
                FoodIngestionEvidenceContext.CaptureForThought(
                    pawn.GetUniqueLoadID(), thoughtDef.defName) == null,
                "The food evidence scope leaked after the synchronous ingestion window.");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => enriched.Emit(scope.Component, PawnDiary.Capture.CaptureDecision.GenerateSolo),
                thoughtDef.defName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            string frozen = diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(frozen),
                "Exact human-meat evidence did not enrich the existing thought page.");

            Ideo later = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(later != null,
                "The frozen-context fixture could not generate its later ideoligion.");
            pawn.ideo.SetIdeo(later);
            PawnDiaryRimTestScope.Require(
                diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole) == frozen,
                "A later ideoligion change rewrote saved food context.");
        }

        /// <summary>An optional adapter exception must preserve one ordinary thought page.</summary>
        [Test]
        public static void FoodAdapterFailureKeepsTheOrdinaryThoughtPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "adapter failure: not applicable (Ideology inactive). ");
                return;
            }

            InstallCannibalismStance();
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            PawnDiaryRimTestScope.Require(thoughtDef != null,
                "The adapter-failure fixture needs the base-game AteFineMeal thought.");
            Thought_Memory thought = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
            thought.pawn = pawn;
            ThoughtSignal signal = null;
            FoodIngestionEvidenceScope foodScope = OpenExactFoodScope(thoughtDef.defName);
            FoodThoughtEvidenceAdapter.SetFailureForTests(true);
            try
            {
                signal = new ThoughtSignal(pawn, thought);
            }
            finally
            {
                FoodThoughtEvidenceAdapter.SetFailureForTests(false);
                FoodIngestionEvidenceContext.End(foodScope);
            }

            PawnDiaryRimTestScope.Require(signal != null && signal.Payload != null,
                "Food adapter failure cancelled the ordinary ThoughtSignal.");
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(signal.CapturedBeliefEvidence.groupKey)
                    && !signal.CapturedBeliefEvidence.matchFields.Any(field =>
                        field != null && field.field == "ingredient_label"),
                "Food adapter failure left partial food evidence on the ordinary signal.");
            scope.FireAndRequireEvent(
                () => signal.Emit(scope.Component, PawnDiary.Capture.CaptureDecision.GenerateSolo),
                thoughtDef.defName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
        }

        /// <summary>The sealed first result set and a mismatched nested close both fail closed.</summary>
        [Test]
        public static void ScopeSealingAndMismatchRecoveryRejectStaleEvidence()
        {
            FoodIngestionEvidenceScope outer = null;
            FoodIngestionEvidenceScope inner = null;
            try
            {
                outer = FoodIngestionEvidenceContext.Begin(
                    "FoodEvidenceOuterPawn",
                    "FoodEvidenceOuterThing",
                    HumanMeatFact());
                inner = FoodIngestionEvidenceContext.Begin(
                    "FoodEvidenceInnerPawn",
                    "FoodEvidenceInnerThing",
                    HumanMeatFact());
                FoodIngestionEvidenceContext.CaptureDirectThought(
                    "FoodEvidenceInnerPawn", "FoodEvidenceInnerThing", "AteFineMeal");
                FoodIngestionEvidenceContext.SealDirectThoughts(
                    "FoodEvidenceInnerPawn", "FoodEvidenceInnerThing");

                // A second ThoughtsFromIngesting-shaped result set must not extend the sealed set.
                FoodIngestionEvidenceContext.CaptureDirectThought(
                    "FoodEvidenceInnerPawn", "FoodEvidenceInnerThing", "AteLavishMeal");
                PawnDiaryRimTestScope.Require(
                    FoodIngestionEvidenceContext.CaptureForThought(
                        "FoodEvidenceInnerPawn", "AteFineMeal") != null
                        && FoodIngestionEvidenceContext.CaptureForThought(
                            "FoodEvidenceInnerPawn", "AteLavishMeal") == null,
                    "A second food-thought result set changed the sealed direct-thought identities.");

                // Closing the non-current parent is an impossible vanilla order, but compatibility
                // patches can mis-nest. The defensive branch must clear every ambient fact.
                FoodIngestionEvidenceContext.End(outer);
                PawnDiaryRimTestScope.Require(
                    !FoodIngestionEvidenceContext.HasOpenScope
                        && FoodIngestionEvidenceContext.CaptureForThought(
                            "FoodEvidenceInnerPawn", "AteFineMeal") == null,
                    "A mismatched food-scope close leaked the nested exact ingredient fact.");

                FoodIngestionEvidenceScope failing = FoodIngestionEvidenceContext.Begin(
                    "FoodEvidenceFailingPawn",
                    "FoodEvidenceFailingThing",
                    HumanMeatFact());
                Exception expected = new InvalidOperationException("food finalizer fixture");
                Exception actual = FoodIngestionEvidencePatch.IngestedFinalizer(expected, failing);
                PawnDiaryRimTestScope.Require(
                    ReferenceEquals(actual, expected) && !FoodIngestionEvidenceContext.HasOpenScope,
                    "The ingestion finalizer changed the original exception or leaked its scope.");
            }
            finally
            {
                FoodIngestionEvidenceContext.End(inner);
                FoodIngestionEvidenceContext.End(outer);
            }
        }

        /// <summary>Classifier coverage pins meal ingredients, direct meat, and corpse rejection.</summary>
        [Test]
        public static void ExactClassifierAcceptsIngredientAndRawMeatButRejectsCorpse()
        {
            ThingDef humanMeat;
            Thing meal = CreateHumanMeatMeal(out humanMeat);
            Thing rawMeat = ThingMaker.MakeThing(humanMeat);
            ThingDef human = DefDatabase<ThingDef>.GetNamedSilentFail("Human");
            PawnDiaryRimTestScope.Require(human?.race?.corpseDef != null,
                "The exact food classifier fixture needs the base-game human corpse Def.");
            Thing corpse = ThingMaker.MakeThing(human.race.corpseDef);
            TrackThing(meal);
            TrackThing(rawMeat);
            TrackThing(corpse);

            FoodIngestionEvidenceFact mealFact =
                FoodIngestionEvidencePatch.ExactHumanlikeMeatFact(meal);
            FoodIngestionEvidenceFact rawFact =
                FoodIngestionEvidencePatch.ExactHumanlikeMeatFact(rawMeat);
            PawnDiaryRimTestScope.Require(
                mealFact?.ingredientDefName == humanMeat.defName
                    && rawFact?.ingredientDefName == humanMeat.defName,
                "The exact food classifier did not accept human meat as an ingredient and direct food.");
            PawnDiaryRimTestScope.Require(
                FoodIngestionEvidencePatch.ExactHumanlikeMeatFact(corpse) == null,
                "The exact food classifier treated a corpse label as an ingredient label.");
        }

        /// <summary>A real fine meal proves both registered Harmony boundaries and evidence handoff.</summary>
        [Test]
        public static void RealHumanMeatMealRunsInstalledHarmonyBoundaries()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "real human-meat meal: not applicable (Ideology inactive). ");
                return;
            }

            PawnDiaryRimTestScope.Require(FoodIngestionEvidencePatch.HooksReady,
                "The complete RimWorld 1.6 food-ingestion Harmony bridge was not registered.");
            InstallCannibalismStance();
            ThingDef humanMeat;
            Thing meal = CreateHumanMeatMeal(out humanMeat);
            TrackThing(meal);
            ThoughtDef fineMeal = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            PawnDiaryRimTestScope.Require(fineMeal != null,
                "The real food fixture needs the base-game AteFineMeal thought.");
            PawnDiaryRimTestScope.Require(
                FoodUtility.ThoughtsFromIngesting(pawn, meal, meal.def)
                    .Any(row => row.thought == fineMeal),
                "Vanilla did not return AteFineMeal for the crafted fine-meal fixture.");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => meal.Ingested(pawn, 999f),
                fineMeal.defName,
                pawn,
                null);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(
                    diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "The real Thing.Ingested path did not carry human-meat evidence into the thought page.");
            PawnDiaryRimTestScope.Require(!FoodIngestionEvidenceContext.HasOpenScope,
                "The real Thing.Ingested path left its exact food scope open.");
        }

        private static FoodIngestionEvidenceScope OpenExactFoodScope(string thoughtDefName)
        {
            string pawnId = pawn.GetUniqueLoadID();
            const string FoodThingId = "FoodEvidenceFixtureThing";
            FoodIngestionEvidenceScope result = FoodIngestionEvidenceContext.Begin(
                pawnId,
                FoodThingId,
                new FoodIngestionEvidenceFact
                {
                    ingredientKind = FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                    ingredientDefName = "Meat_Human",
                    ingredientLabel = "human meat"
                });
            FoodIngestionEvidenceContext.CaptureDirectThought(
                pawnId, FoodThingId, thoughtDefName);
            FoodIngestionEvidenceContext.SealDirectThoughts(pawnId, FoodThingId);
            return result;
        }

        private static FoodIngestionEvidenceFact HumanMeatFact()
        {
            return new FoodIngestionEvidenceFact
            {
                ingredientKind = FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                ingredientDefName = "Meat_Human",
                ingredientLabel = "human meat"
            };
        }

        private static Thing CreateHumanMeatMeal(out ThingDef humanMeat)
        {
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealFine");
            humanMeat = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Human");
            PawnDiaryRimTestScope.Require(mealDef != null && humanMeat != null,
                "The real food fixture needs the base-game MealFine and Meat_Human Defs.");
            Thing meal = ThingMaker.MakeThing(mealDef);
            CompIngredients ingredients = meal?.TryGetComp<CompIngredients>();
            PawnDiaryRimTestScope.Require(meal != null && ingredients != null,
                "The crafted fine meal did not expose vanilla CompIngredients.");
            ingredients.RegisterIngredient(humanMeat);
            meal.stackCount = 1;
            return meal;
        }

        private static void TrackThing(Thing thing)
        {
            scope.RegisterCleanup(() =>
            {
                if (thing != null && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            });
        }

        private static void InstallCannibalismStance()
        {
            PawnDiaryRimTestScope.Require(pawn?.ideo != null,
                "The active-Ideology food fixture needs a Pawn_IdeoTracker.");
            PreceptDef target = DefDatabase<PreceptDef>.GetNamedSilentFail("Cannibalism_Preferred");
            PawnDiaryRimTestScope.Require(target?.issue != null,
                "The food fixture needs the vanilla Cannibalism_Preferred precept.");
            Ideo fixture = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(fixture != null,
                "RimWorld did not generate a disposable food-evidence ideoligion.");
            Precept existing = fixture.PreceptsListForReading
                .FirstOrDefault(precept => precept?.def?.issue == target.issue);
            // Removing a non-default stance normally makes vanilla insert the issue's default
            // stance immediately. This fixture is replacing that stance, so use replacement mode;
            // otherwise two Cannibalism precepts survive and the resolver correctly fails closed
            // on the contradictory live doctrine.
            if (existing != null) fixture.RemovePrecept(existing, true);
            fixture.AddPrecept(
                PreceptMaker.MakePrecept(target), false, Faction.OfPlayer.def, null);
            PawnDiaryRimTestScope.Require(
                fixture.PreceptsListForReading.Count(precept => precept?.def?.issue == target.issue) == 1
                    && fixture.PreceptsListForReading.Any(precept => precept?.def == target),
                "The disposable food-evidence Ideology did not retain one Cannibalism_Preferred stance.");
            pawn.ideo.SetIdeo(fixture);
        }
    }
}
