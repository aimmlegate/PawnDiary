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
    /// <summary>Captures exact humanlike-meat facts for existing thought pages without scanning pawns.</summary>
    internal static class FoodIngestionEvidencePatch
    {
        // Vanilla currently limits this list to three. The defensive cap also keeps an unexpectedly
        // large modded CompIngredients list from turning one ingestion into unbounded hot-path work.
        private const int MaximumIngredientsInspected = 32;

        /// <summary>Defensively registers the two installed RimWorld 1.6 ingestion boundaries.</summary>
        internal static void TryRegister(Harmony harmony)
        {
            MethodBase ingested = AccessTools.Method(
                typeof(Thing), nameof(Thing.Ingested), new[] { typeof(Pawn), typeof(float) });
            MethodBase thoughts = AccessTools.Method(
                typeof(FoodUtility), nameof(FoodUtility.ThoughtsFromIngesting),
                new[] { typeof(Pawn), typeof(Thing), typeof(ThingDef) });
            if (ingested == null || thoughts == null)
            {
                Log.Warning("[Pawn Diary] Could not find the RimWorld food-ingestion evidence "
                    + "boundaries; optional food belief enrichment is disabled.");
                return;
            }

            harmony.Patch(
                ingested,
                prefix: new HarmonyMethod(typeof(FoodIngestionEvidencePatch), nameof(IngestedPrefix)),
                postfix: new HarmonyMethod(typeof(FoodIngestionEvidencePatch), nameof(IngestedPostfix)),
                finalizer: new HarmonyMethod(typeof(FoodIngestionEvidencePatch), nameof(IngestedFinalizer)));
            harmony.Patch(
                thoughts,
                postfix: new HarmonyMethod(typeof(FoodIngestionEvidencePatch), nameof(ThoughtsPostfix)));
        }

        /// <summary>
        /// Opens a scope only for active-Ideology, diary-eligible pawns eating an exact humanlike-meat
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
        private static Exception IngestedFinalizer(
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
            FoodIngestionEvidenceFact fact = ExactHumanlikeMeatFact(food);
            return fact == null ? null : FoodIngestionEvidenceContext.Begin(
                pawn.GetUniqueLoadID(), food.GetUniqueLoadID(), fact);
        }

        private static FoodIngestionEvidenceFact ExactHumanlikeMeatFact(Thing food)
        {
            CompIngredients ingredients = food.TryGetComp<CompIngredients>();
            if (ingredients?.ingredients != null)
            {
                // This exact-Def list is the same source vanilla uses to decide
                // AteHumanMeatAsIngredient. Stop at the defensive cap and fail open if a mod exceeds it.
                int count = Math.Min(ingredients.ingredients.Count, MaximumIngredientsInspected);
                for (int i = 0; i < count; i++)
                {
                    ThingDef ingredient = ingredients.ingredients[i];
                    if (ingredient != null && FoodUtility.GetMeatSourceCategory(ingredient)
                        == MeatSourceCategory.Humanlike)
                    {
                        return FactFor(ingredient);
                    }
                }
            }

            // Raw/direct humanlike meat is equally exact. Corpses deliberately stay out of this first
            // slice because a corpse label is not an ingredient label.
            return food.def != null && !food.def.IsCorpse
                && FoodUtility.GetMeatSourceCategory(food.def) == MeatSourceCategory.Humanlike
                    ? FactFor(food.def)
                    : null;
        }

        private static FoodIngestionEvidenceFact FactFor(ThingDef ingredient)
        {
            string label = DiaryLineCleaner.CleanLine(ingredient.LabelCap.Resolve());
            if (string.IsNullOrWhiteSpace(ingredient.defName) || string.IsNullOrWhiteSpace(label))
                return null;
            return new FoodIngestionEvidenceFact
            {
                ingredientKind = FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                ingredientDefName = ingredient.defName,
                ingredientLabel = label
            };
        }
    }
}
