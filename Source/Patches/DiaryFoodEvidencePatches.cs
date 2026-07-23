// Exact food-ingestion correlation patches. RimWorld computes food thoughts from the live Thing and
// then immediately gains those memories, but the memory object itself retains no stable ingredient
// Def. These two guarded boundaries bridge only that synchronous window and never emit a page.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Captures exact supported-food facts for existing thought pages without scanning pawns.</summary>
    internal static class FoodIngestionEvidencePatch
    {
        // Vanilla currently limits this list to three. The defensive cap also keeps an unexpectedly
        // large modded CompIngredients list from turning one ingestion into unbounded hot-path work.
        private const int MaximumIngredientsInspected = 32;

        /// <summary>True only when both sides of the exact ingestion bridge registered successfully.</summary>
        internal static bool HooksReady { get; private set; }

        /// <summary>Defensively registers the two installed RimWorld 1.6 ingestion boundaries.</summary>
        internal static void TryRegister(Harmony harmony)
        {
            HooksReady = false;
            if (harmony == null) return;

            MethodBase ingested = AccessTools.Method(
                typeof(Thing), nameof(Thing.Ingested), new[] { typeof(Pawn), typeof(float) });
            MethodBase thoughts = AccessTools.Method(
                typeof(FoodUtility), nameof(FoodUtility.ThoughtsFromIngesting),
                new[] { typeof(Pawn), typeof(Thing), typeof(ThingDef) });

            bool ingestedReady = TryPatch(
                harmony,
                ingested,
                "Thing.Ingested(Pawn, float)",
                nameof(IngestedPrefix),
                nameof(IngestedPostfix),
                nameof(IngestedFinalizer));
            bool thoughtsReady = TryPatch(
                harmony,
                thoughts,
                "FoodUtility.ThoughtsFromIngesting(Pawn, Thing, ThingDef)",
                null,
                nameof(ThoughtsPostfix),
                null);
            HooksReady = ingestedReady && thoughtsReady;
        }

        private static bool TryPatch(
            Harmony harmony,
            MethodBase target,
            string targetName,
            string prefixName,
            string postfixName,
            string finalizerName)
        {
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find " + targetName
                    + "; optional food belief enrichment is disabled.");
                return false;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: prefixName == null
                        ? null
                        : new HarmonyMethod(typeof(FoodIngestionEvidencePatch), prefixName),
                    postfix: postfixName == null
                        ? null
                        : new HarmonyMethod(typeof(FoodIngestionEvidencePatch), postfixName),
                    finalizer: finalizerName == null
                        ? null
                        : new HarmonyMethod(typeof(FoodIngestionEvidencePatch), finalizerName));
                return true;
            }
            catch (Exception exception)
            {
                // One changed signature or incompatible patch must disable only this optional bridge;
                // registration then continues to every later DLC/compatibility hook in the registrar.
                Log.Warning("[Pawn Diary] Could not register " + targetName
                    + "; optional food belief enrichment is disabled. " + exception);
                return false;
            }
        }

        /// <summary>
        /// Opens a scope only for active-Ideology, diary-eligible pawns eating an exact supported-food
        /// food or ingredient. Generic meal identity, quality, and broad eating labels are ignored.
        /// </summary>
        private static void IngestedPrefix(
            Thing __instance,
            Pawn ingester,
            ref FoodIngestionEvidenceScope __state)
        {
            __state = null;
            if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying || __instance == null
                || ingester == null || !DiaryGameComponent.IsDiaryEligible(ingester))
            {
                return;
            }

            __state = DiaryPatchSafety.Run(
                "FoodIngestionEvidencePatch.Prefix",
                (food: __instance, pawn: ingester),
                s => BeginExactScope(s.food, s.pawn),
                (FoodIngestionEvidenceScope)null);
        }

        /// <summary>Closes the primitive window after normal completion.</summary>
        private static void IngestedPostfix(FoodIngestionEvidenceScope __state)
        {
            FoodIngestionEvidenceContext.End(__state);
        }

        /// <summary>Closes the primitive window when vanilla or another patch throws.</summary>
        internal static Exception IngestedFinalizer(
            Exception __exception,
            FoodIngestionEvidenceScope __state)
        {
            FoodIngestionEvidenceContext.End(__state);
            return __exception;
        }

        /// <summary>
        /// Freezes only the exact ThoughtDefs vanilla returned for the active ingestion. Preview and
        /// food-search calls hit the leading scope gate and do no identifier work or allocation.
        /// </summary>
        private static void ThoughtsPostfix(
            Pawn ingester,
            Thing foodSource,
            List<FoodUtility.ThoughtFromIngesting> __result)
        {
            if (!FoodIngestionEvidenceContext.HasOpenScope || ingester == null || foodSource == null
                || __result == null)
            {
                return;
            }

            DiaryPatchSafety.Run(
                "FoodIngestionEvidencePatch.Thoughts",
                (pawn: ingester, food: foodSource, thoughts: __result),
                s =>
                {
                    string pawnId = s.pawn.GetUniqueLoadID();
                    string foodThingId = s.food.GetUniqueLoadID();
                    for (int i = 0; i < s.thoughts.Count; i++)
                    {
                        FoodUtility.ThoughtFromIngesting row = s.thoughts[i];
                        FoodIngestionEvidenceContext.CaptureDirectThought(
                            pawnId, foodThingId, row.thought?.defName);
                    }
                    FoodIngestionEvidenceContext.SealDirectThoughts(pawnId, foodThingId);
                });
        }

        private static FoodIngestionEvidenceScope BeginExactScope(Thing food, Pawn pawn)
        {
            List<FoodIngestionEvidenceFact> facts = ExactSupportedFoodFacts(food);
            FoodIngestionEvidenceFact fact = FoodBeliefEvidencePolicy.SelectFact(
                facts, DiaryBeliefPolicy.Snapshot());
            return fact == null ? null : FoodIngestionEvidenceContext.Begin(
                pawn.GetUniqueLoadID(), food.GetUniqueLoadID(), fact);
        }

        /// <summary>
        /// Returns the first supported exact category in policy order. Humanlike meat remains first so
        /// adding insect support cannot change the already-shipped result for a mixed modded meal.
        /// </summary>
        internal static FoodIngestionEvidenceFact ExactSupportedMeatFact(Thing food)
        {
            return ExactHumanlikeMeatFact(food) ?? ExactInsectMeatFact(food);
        }

        /// <summary>
        /// Projects every bounded mechanical category vanilla can prove from this exact food. This does
        /// not choose prompt policy: <see cref="FoodBeliefEvidencePolicy.SelectFact"/> applies XML order.
        /// </summary>
        internal static List<FoodIngestionEvidenceFact> ExactSupportedFoodFacts(Thing food)
        {
            List<FoodIngestionEvidenceFact> result = new List<FoodIngestionEvidenceFact>();
            if (food == null || food.def == null || food.def.IsCorpse) return result;

            CompIngredients ingredients = food.TryGetComp<CompIngredients>();
            if (ingredients?.ingredients != null)
            {
                int count = Math.Min(ingredients.ingredients.Count, MaximumIngredientsInspected);
                for (int i = 0; i < count; i++)
                    AddExactIngredientFacts(result, ingredients.ingredients[i]);
            }
            else
            {
                AddExactIngredientFacts(result, food.def);
            }

            // Nutrient paste is exact because the live ingestible Def itself declares the stable
            // AteNutrientPaste HistoryEvent used by vanilla's PreceptComp correlation path.
            if (string.Equals(food.def.ingestible?.ateEvent?.defName, "AteNutrientPaste",
                    StringComparison.Ordinal))
                AddUniqueFact(result, FactFor(food.def, FoodIngestionEvidenceKindTokens.NutrientPaste));
            return result;
        }

        /// <summary>Exact ordinary animal meat, excluding the already-owned humanlike/insect families.</summary>
        internal static FoodIngestionEvidenceFact ExactAnimalMeatFact(Thing food)
        {
            return FirstFact(ExactSupportedFoodFacts(food), FoodIngestionEvidenceKindTokens.AnimalMeat);
        }

        /// <summary>Exact raw or ingredient fungus using vanilla's PlantFoodRaw plus IsFungus test.</summary>
        internal static FoodIngestionEvidenceFact ExactFungusFact(Thing food)
        {
            return FirstFact(ExactSupportedFoodFacts(food), FoodIngestionEvidenceKindTokens.Fungus);
        }

        /// <summary>Exact nutrient paste identified by its live ingestible HistoryEvent.</summary>
        internal static FoodIngestionEvidenceFact ExactNutrientPasteFact(Thing food)
        {
            return FirstFact(ExactSupportedFoodFacts(food), FoodIngestionEvidenceKindTokens.NutrientPaste);
        }

        /// <summary>Classifies exact humanlike meat through vanilla's mechanical meat category.</summary>
        internal static FoodIngestionEvidenceFact ExactHumanlikeMeatFact(Thing food)
        {
            return ExactMeatFact(
                food,
                MeatSourceCategory.Humanlike,
                FoodIngestionEvidenceKindTokens.HumanlikeMeat);
        }

        /// <summary>Classifies exact insect meat through vanilla's mechanical meat category.</summary>
        internal static FoodIngestionEvidenceFact ExactInsectMeatFact(Thing food)
        {
            return ExactMeatFact(
                food,
                MeatSourceCategory.Insect,
                FoodIngestionEvidenceKindTokens.InsectMeat);
        }

        private static FoodIngestionEvidenceFact ExactMeatFact(
            Thing food,
            MeatSourceCategory category,
            string ingredientKind)
        {
            if (food == null) return null;
            CompIngredients ingredients = food.TryGetComp<CompIngredients>();
            if (ingredients?.ingredients != null)
            {
                // This exact-Def list and FoodUtility category are the same evidence vanilla uses for
                // AteHumanMeatAsIngredient / AteInsectMeatAsIngredient. Stop at the defensive cap.
                int count = Math.Min(ingredients.ingredients.Count, MaximumIngredientsInspected);
                for (int i = 0; i < count; i++)
                {
                    ThingDef ingredient = ingredients.ingredients[i];
                    if (ingredient != null && FoodUtility.GetMeatSourceCategory(ingredient)
                        == category)
                    {
                        return FactFor(ingredient, ingredientKind);
                    }
                }
            }

            // Raw/direct meat is equally exact. Corpses deliberately stay out because a corpse label
            // is not an ingredient label and must never be repurposed as one.
            return food.def != null && !food.def.IsCorpse
                && FoodUtility.GetMeatSourceCategory(food.def) == category
                    ? FactFor(food.def, ingredientKind)
                    : null;
        }

        private static void AddExactIngredientFacts(
            List<FoodIngestionEvidenceFact> result,
            ThingDef ingredient)
        {
            if (ingredient?.ingestible == null) return;
            MeatSourceCategory meat = FoodUtility.GetMeatSourceCategory(ingredient);
            if (meat == MeatSourceCategory.Humanlike)
                AddUniqueFact(result, FactFor(ingredient, FoodIngestionEvidenceKindTokens.HumanlikeMeat));
            else if (meat == MeatSourceCategory.Insect)
                AddUniqueFact(result, FactFor(ingredient, FoodIngestionEvidenceKindTokens.InsectMeat));
            else if (ingredient.IsMeat && ingredient.ingestible.sourceDef?.race?.Animal == true)
                AddUniqueFact(result, FactFor(ingredient, FoodIngestionEvidenceKindTokens.AnimalMeat));

            if (ingredient.IsFungus && ingredient.thingCategories != null
                && ingredient.thingCategories.Contains(ThingCategoryDefOf.PlantFoodRaw))
                AddUniqueFact(result, FactFor(ingredient, FoodIngestionEvidenceKindTokens.Fungus));
        }

        private static void AddUniqueFact(
            List<FoodIngestionEvidenceFact> target,
            FoodIngestionEvidenceFact fact)
        {
            if (target == null || fact == null) return;
            for (int i = 0; i < target.Count; i++)
                if (string.Equals(target[i]?.ingredientKind, fact.ingredientKind, StringComparison.Ordinal))
                    return;
            target.Add(fact);
        }

        private static FoodIngestionEvidenceFact FirstFact(
            List<FoodIngestionEvidenceFact> facts,
            string kind)
        {
            if (facts == null) return null;
            for (int i = 0; i < facts.Count; i++)
                if (string.Equals(facts[i]?.ingredientKind, kind, StringComparison.Ordinal))
                    return facts[i];
            return null;
        }

        private static FoodIngestionEvidenceFact FactFor(ThingDef ingredient, string ingredientKind)
        {
            string label = DiaryLineCleaner.CleanLine(ingredient.LabelCap.Resolve());
            if (string.IsNullOrWhiteSpace(ingredientKind)
                || string.IsNullOrWhiteSpace(ingredient.defName)
                || string.IsNullOrWhiteSpace(label))
                return null;
            return new FoodIngestionEvidenceFact
            {
                ingredientKind = ingredientKind,
                ingredientDefName = ingredient.defName,
                ingredientLabel = label
            };
        }
    }
}
