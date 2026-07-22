// Loaded-game contract coverage for the bounded Ideology Phase 2 food client. These fixtures keep
// page ownership in ThoughtSignal, drive the primitive scope's defensive branches, and exercise real
// humanlike- and insect-meat meals through the installed Harmony boundaries. Manual DLC profiles stay deferred.
using System;
using System.Collections.Generic;
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

            AssertExactFoodEnrichment(
                HumanMeatFact(),
                "Cannibalism_Preferred",
                "cannibal_meal");
        }

        /// <summary>Exact insect meat shares the same dedup/Rand/scope and save-freezing invariants.</summary>
        [Test]
        public static void ExactInsectMeatEnrichesOneExistingFrozenThoughtPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "exact insect-meat enrichment: not applicable (Ideology inactive). ");
                return;
            }

            AssertExactFoodEnrichment(
                InsectMeatFact(),
                "InsectMeatEating_Loved",
                "insect_meal");
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

            InstallFoodStance("Cannibalism_Preferred");
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            PawnDiaryRimTestScope.Require(thoughtDef != null,
                "The adapter-failure fixture needs the base-game AteFineMeal thought.");
            Thought_Memory thought = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
            thought.pawn = pawn;
            ThoughtSignal signal = null;
            FoodIngestionEvidenceScope foodScope = OpenExactFoodScope(
                thoughtDef.defName, HumanMeatFact());
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

        /// <summary>Classifier coverage pins both categories, negative meals, corpses, and precedence.</summary>
        [Test]
        public static void ExactClassifierAcceptsSupportedMeatAndRejectsAmbiguousSources()
        {
            ThingDef humanMeat;
            Thing meal = CreateHumanMeatMeal(out humanMeat);
            Thing rawMeat = ThingMaker.MakeThing(humanMeat);
            ThingDef human = DefDatabase<ThingDef>.GetNamedSilentFail("Human");
            PawnDiaryRimTestScope.Require(human?.race?.corpseDef != null,
                "The exact food classifier fixture needs the base-game human corpse Def.");
            Thing corpse = ThingMaker.MakeThing(human.race.corpseDef);
            ThingDef insectMeat;
            Thing insectMeal = CreateInsectMeatMeal(out insectMeat);
            Thing rawInsectMeat = ThingMaker.MakeThing(insectMeat);
            ThingDef megaspider = DefDatabase<ThingDef>.GetNamedSilentFail("Megaspider");
            PawnDiaryRimTestScope.Require(megaspider?.race?.corpseDef != null,
                "The exact food classifier fixture needs the base-game megaspider corpse Def.");
            Thing insectCorpse = ThingMaker.MakeThing(megaspider.race.corpseDef);
            Thing ordinaryMeal = CreateFineMeal();
            Thing mixedMeal = CreateFineMeal();
            CompIngredients mixedIngredients = mixedMeal.TryGetComp<CompIngredients>();
            // Register insect first to prove policy order, rather than CompIngredients order, preserves
            // the already-shipped humanlike classification for mixed modded meals.
            mixedIngredients.RegisterIngredient(insectMeat);
            mixedIngredients.RegisterIngredient(humanMeat);
            TrackThing(meal);
            TrackThing(rawMeat);
            TrackThing(corpse);
            TrackThing(insectMeal);
            TrackThing(rawInsectMeat);
            TrackThing(insectCorpse);
            TrackThing(ordinaryMeal);
            TrackThing(mixedMeal);

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
            FoodIngestionEvidenceFact insectMealFact =
                FoodIngestionEvidencePatch.ExactInsectMeatFact(insectMeal);
            FoodIngestionEvidenceFact rawInsectFact =
                FoodIngestionEvidencePatch.ExactInsectMeatFact(rawInsectMeat);
            PawnDiaryRimTestScope.Require(
                insectMealFact?.ingredientDefName == insectMeat.defName
                    && rawInsectFact?.ingredientDefName == insectMeat.defName
                    && insectMealFact.ingredientKind == FoodIngestionEvidenceKindTokens.InsectMeat,
                "The exact food classifier did not accept insect meat as an ingredient and direct food.");
            PawnDiaryRimTestScope.Require(
                FoodIngestionEvidencePatch.ExactInsectMeatFact(insectCorpse) == null
                    && FoodIngestionEvidencePatch.ExactSupportedMeatFact(ordinaryMeal) == null,
                "A corpse or ingredient-free ordinary meal invented exact food evidence.");
            FoodIngestionEvidenceFact mixedFact =
                FoodIngestionEvidencePatch.ExactSupportedMeatFact(mixedMeal);
            PawnDiaryRimTestScope.Require(
                mixedFact?.ingredientKind == FoodIngestionEvidenceKindTokens.HumanlikeMeat
                    && mixedFact.ingredientDefName == humanMeat.defName,
                "Adding insect support changed humanlike-meat precedence in a mixed meal.");
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
            InstallFoodStance("Cannibalism_Preferred");
            ThingDef humanMeat;
            Thing meal = CreateHumanMeatMeal(out humanMeat);
            TrackThing(meal);
            ThoughtDef fineMeal = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            PawnDiaryRimTestScope.Require(fineMeal != null,
                "The real food fixture needs the base-game AteFineMeal thought.");
            List<FoodUtility.ThoughtFromIngesting> returnedThoughts =
                FoodUtility.ThoughtsFromIngesting(pawn, meal, meal.def);
            PawnDiaryRimTestScope.Require(returnedThoughts.Any(row => row.thought == fineMeal),
                "Vanilla did not return AteFineMeal for the crafted fine-meal fixture.");
            AllowFoodThoughtFixturePages();
            ClearReturnedMemories(returnedThoughts);

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

        /// <summary>
        /// A real insect fine meal proves vanilla's exact category seam and ingestion-wide handoff:
        /// AteFineMeal receives the same factual ingredient row as the insect-specific thought.
        /// </summary>
        [Test]
        public static void RealInsectMeatMealRunsInstalledHarmonyBoundaries()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "real insect-meat meal: not applicable (Ideology inactive). ");
                return;
            }

            PawnDiaryRimTestScope.Require(FoodIngestionEvidencePatch.HooksReady,
                "The complete RimWorld 1.6 food-ingestion Harmony bridge was not registered.");
            InstallFoodStance("InsectMeatEating_Loved");
            ThingDef insectMeat;
            Thing meal = CreateInsectMeatMeal(out insectMeat);
            TrackThing(meal);
            ThoughtDef fineMeal = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            PawnDiaryRimTestScope.Require(fineMeal != null,
                "The real insect-food fixture needs the base-game AteFineMeal thought.");
            List<FoodUtility.ThoughtFromIngesting> returnedThoughts =
                FoodUtility.ThoughtsFromIngesting(pawn, meal, meal.def);
            PawnDiaryRimTestScope.Require(
                returnedThoughts.Any(row => row.thought == fineMeal)
                    && returnedThoughts.Any(row => row.thought != null
                        && row.thought.defName.IndexOf("InsectMeat", StringComparison.Ordinal) >= 0),
                "Vanilla did not return both fine-meal and insect-meat thoughts for the exact fixture.");
            AllowFoodThoughtFixturePages();
            ClearReturnedMemories(returnedThoughts);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => meal.Ingested(pawn, 999f),
                fineMeal.defName,
                pawn,
                null);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(
                    diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "The real Thing.Ingested path did not carry insect-meat evidence into AteFineMeal.");
            PawnDiaryRimTestScope.Require(!FoodIngestionEvidenceContext.HasOpenScope,
                "The real insect Thing.Ingested path left its exact food scope open.");
        }

        private static void AssertExactFoodEnrichment(
            FoodIngestionEvidenceFact fact,
            string targetPreceptDefName,
            string expectedGroupKey)
        {
            InstallFoodStance(targetPreceptDefName);
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
                foodScope = OpenExactFoodScope(thoughtDef.defName, fact);
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
            PawnDiaryRimTestScope.Require(evidence != null && evidence.groupKey == expectedGroupKey,
                "Exact food evidence did not attach XML group " + expectedGroupKey + ".");
            PawnDiaryRimTestScope.Require(evidence.matchFields.Any(field => field != null
                    && field.field == "ingredient_label" && field.value == fact.ingredientLabel),
                "Exact food evidence did not freeze ingredient_label=" + fact.ingredientLabel + ".");
            PawnDiaryRimTestScope.Require(
                FoodIngestionEvidenceContext.CaptureForThought(
                    pawn.GetUniqueLoadID(), thoughtDef.defName) == null,
                "The food evidence scope leaked after the synchronous ingestion window.");
            RequireProjectedFoodStance(evidence, targetPreceptDefName, expectedGroupKey);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => enriched.Emit(scope.Component, PawnDiary.Capture.CaptureDecision.GenerateSolo),
                thoughtDef.defName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            string frozen = diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(frozen),
                "Exact food evidence did not enrich the existing thought page.");

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

        private static void RequireProjectedFoodStance(
            BeliefEventEvidence evidence,
            string targetPreceptDefName,
            string groupKey)
        {
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(pawn, policy);
            PawnDiaryRimTestScope.Require(snapshot.precepts.Any(precept => precept != null
                    && precept.defName == targetPreceptDefName),
                "The live food fixture did not project " + targetPreceptDefName + ".");
            string pawnId = pawn.GetUniqueLoadID();
            int tick = Find.TickManager?.TicksGame ?? 0;
            string eventId = "PawnDiaryFoodPreview_" + groupKey;
            BeliefEventEvidence povEvidence = BeliefEventEvidenceFactory.ForPov(
                evidence, eventId, tick, pawnId, DiaryEvent.InitiatorRole);
            BeliefContextBuildResult preview = BeliefContextBuilder.BuildSyntheticPreview(
                snapshot, povEvidence, eventId, pawnId, policy);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(preview.fullContext)
                    && preview.resolution.stances.Any(stance => stance?.precept?.defName
                        == targetPreceptDefName),
                "Detached food resolution did not select " + targetPreceptDefName
                    + " from group " + groupKey + " (projected precepts="
                    + snapshot.precepts.Count + ").");
        }

        private static FoodIngestionEvidenceScope OpenExactFoodScope(
            string thoughtDefName,
            FoodIngestionEvidenceFact fact)
        {
            string pawnId = pawn.GetUniqueLoadID();
            const string FoodThingId = "FoodEvidenceFixtureThing";
            FoodIngestionEvidenceScope result = FoodIngestionEvidenceContext.Begin(
                pawnId,
                FoodThingId,
                fact);
            FoodIngestionEvidenceContext.CaptureDirectThought(
                pawnId, FoodThingId, thoughtDefName);
            FoodIngestionEvidenceContext.SealDirectThoughts(pawnId, FoodThingId);
            return result;
        }

        private static FoodIngestionEvidenceFact HumanMeatFact()
        {
            ThingDef humanMeat = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Human");
            PawnDiaryRimTestScope.Require(humanMeat != null,
                "The food fixture needs the base-game Meat_Human Def.");
            return new FoodIngestionEvidenceFact
            {
                ingredientKind = FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                ingredientDefName = humanMeat.defName,
                ingredientLabel = DiaryLineCleaner.CleanLine(humanMeat.LabelCap.Resolve())
            };
        }

        private static FoodIngestionEvidenceFact InsectMeatFact()
        {
            ThingDef insectMeat = InsectMeatDef();
            return new FoodIngestionEvidenceFact
            {
                ingredientKind = FoodIngestionEvidenceKindTokens.InsectMeat,
                ingredientDefName = insectMeat.defName,
                ingredientLabel = DiaryLineCleaner.CleanLine(insectMeat.LabelCap.Resolve())
            };
        }

        private static Thing CreateHumanMeatMeal(out ThingDef humanMeat)
        {
            humanMeat = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Human");
            PawnDiaryRimTestScope.Require(humanMeat != null,
                "The real food fixture needs the base-game Meat_Human Def.");
            return CreateMealWithIngredient(humanMeat);
        }

        private static Thing CreateInsectMeatMeal(out ThingDef insectMeat)
        {
            insectMeat = InsectMeatDef();
            return CreateMealWithIngredient(insectMeat);
        }

        private static ThingDef InsectMeatDef()
        {
            ThingDef megaspider = DefDatabase<ThingDef>.GetNamedSilentFail("Megaspider");
            ThingDef insectMeat = megaspider?.race?.meatDef;
            PawnDiaryRimTestScope.Require(insectMeat != null
                    && FoodUtility.GetMeatSourceCategory(insectMeat) == MeatSourceCategory.Insect,
                "The food fixture needs the base-game megaspider's mechanically classified meat Def.");
            return insectMeat;
        }

        private static Thing CreateMealWithIngredient(ThingDef ingredient)
        {
            Thing meal = CreateFineMeal();
            meal.TryGetComp<CompIngredients>().RegisterIngredient(ingredient);
            return meal;
        }

        private static Thing CreateFineMeal()
        {
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealFine");
            PawnDiaryRimTestScope.Require(mealDef != null,
                "The real food fixture needs the base-game MealFine Def.");
            Thing meal = ThingMaker.MakeThing(mealDef);
            PawnDiaryRimTestScope.Require(meal?.TryGetComp<CompIngredients>() != null,
                "The crafted fine meal did not expose vanilla CompIngredients.");
            meal.stackCount = 1;
            return meal;
        }

        private static void ClearReturnedMemories(
            List<FoodUtility.ThoughtFromIngesting> returnedThoughts)
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            PawnDiaryRimTestScope.Require(memories != null,
                "The real food fixture needs a mood memory handler.");
            List<ThoughtDef> defs = returnedThoughts
                .Where(row => row.thought != null)
                .Select(row => row.thought)
                .Distinct()
                .ToList();
            for (int i = 0; i < defs.Count; i++) memories.RemoveMemoriesOfDef(defs[i]);
            scope.RegisterCleanup(() =>
            {
                MemoryThoughtHandler current = pawn?.needs?.mood?.thoughts?.memories;
                for (int i = 0; i < defs.Count; i++) current?.RemoveMemoriesOfDef(defs[i]);
            });
        }

        private static void AllowFoodThoughtFixturePages()
        {
            DiarySignalPolicyDef thoughtPolicy =
                DiarySignalPolicies.ForKey(DiarySignalPolicies.Thought);
            PawnDiaryRimTestScope.Require(thoughtPolicy != null,
                "The real food fixture needs the loaded Thought signal policy.");
            float original = thoughtPolicy.eatingMinMoodOffset;
            // AteFineMeal is +5 while the shipped eating threshold is 15. Lower only the disposable
            // loaded-test Def so the real memory hook can create the existing ThoughtSignal page whose
            // optional food enrichment is under test; teardown restores the player's loaded policy.
            thoughtPolicy.eatingMinMoodOffset = 0f;
            scope.RegisterCleanup(() => thoughtPolicy.eatingMinMoodOffset = original);
        }

        private static void TrackThing(Thing thing)
        {
            scope.RegisterCleanup(() =>
            {
                if (thing != null && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            });
        }

        private static void InstallFoodStance(string targetPreceptDefName)
        {
            PawnDiaryRimTestScope.Require(pawn?.ideo != null,
                "The active-Ideology food fixture needs a Pawn_IdeoTracker.");
            PreceptDef target = DefDatabase<PreceptDef>.GetNamedSilentFail(targetPreceptDefName);
            PawnDiaryRimTestScope.Require(target?.issue != null,
                "The food fixture needs vanilla precept " + targetPreceptDefName + ".");
            Ideo fixture = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(fixture != null,
                "RimWorld did not generate a disposable food-evidence ideoligion.");
            // Random full ideoligions are deliberately poor positive fixtures: another food stance can
            // create a legitimate lexical runner-up and make the production resolver fail closed. Keep
            // roles/rituals/memes intact, but remove issue stances in replacement mode so this disposable
            // fixture proves exactly one doctrine. Pure tests cover unrelated and contradictory rivals.
            List<Precept> issueStances = fixture.PreceptsListForReading
                .Where(precept => precept?.def?.issue != null)
                .ToList();
            for (int i = 0; i < issueStances.Count; i++)
                fixture.RemovePrecept(issueStances[i], true);
            fixture.AddPrecept(
                PreceptMaker.MakePrecept(target), false, Faction.OfPlayer.def, null);
            PawnDiaryRimTestScope.Require(
                fixture.PreceptsListForReading.Count(precept => precept?.def?.issue != null) == 1
                    && fixture.PreceptsListForReading.Any(precept => precept?.def == target),
                "The disposable food-evidence Ideology did not retain only "
                    + targetPreceptDefName + ".");
            pawn.ideo.SetIdeo(fixture);
            PawnDiaryRimTestScope.Require(ReferenceEquals(pawn.ideo.Ideo, fixture),
                "The test pawn did not adopt the disposable food-evidence ideoligion.");
        }
    }
}
