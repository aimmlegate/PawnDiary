// Short-lived food evidence bridge. Vanilla owns the exact Thing/CompIngredients before it creates
// memory thoughts, so the Harmony edge opens a primitive-only scope around Thing.Ingested, records
// the exact ThoughtDefs returned by FoodUtility, and closes it even when vanilla throws. ThoughtSignal
// then asks this adapter for one detached fact; no live Thing, Def, Pawn, or localized description is
// retained beyond the synchronous ingestion call.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One nested primitive ingestion scope; retained only on the current game thread.</summary>
    internal sealed class FoodIngestionEvidenceScope
    {
        internal string pawnId = string.Empty;
        internal string foodThingId = string.Empty;
        internal FoodIngestionEvidenceFact fact;
        internal List<string> directThoughtDefNames = new List<string>();
        internal FoodIngestionEvidenceScope previous;
        internal bool directThoughtsCaptured;
        internal bool closed;
    }

    /// <summary>Correlates an exact ingested ingredient only with vanilla's directly returned thoughts.</summary>
    internal static class FoodIngestionEvidenceContext
    {
        [ThreadStatic]
        private static FoodIngestionEvidenceScope current;

        /// <summary>Cheap patch gate: false for every preview/search call outside actual ingestion.</summary>
        internal static bool HasOpenScope => current != null && !current.closed;

        /// <summary>Opens one exact primitive scope. Null/empty facts never become ambient state.</summary>
        internal static FoodIngestionEvidenceScope Begin(
            string pawnId,
            string foodThingId,
            FoodIngestionEvidenceFact fact)
        {
            if (fact == null || string.IsNullOrWhiteSpace(pawnId)
                || string.IsNullOrWhiteSpace(foodThingId))
            {
                return null;
            }

            FoodIngestionEvidenceScope scope = new FoodIngestionEvidenceScope
            {
                pawnId = pawnId,
                foodThingId = foodThingId,
                fact = Copy(fact),
                previous = current
            };
            current = scope;
            return scope;
        }

        /// <summary>
        /// Freezes the exact thought Def identities returned by vanilla for this same pawn and food.
        /// Only the first matching call in the scope is accepted.
        /// </summary>
        internal static void CaptureDirectThought(
            string pawnId,
            string foodThingId,
            string thoughtDefName)
        {
            FoodIngestionEvidenceScope scope = current;
            if (scope == null || scope.closed || scope.directThoughtsCaptured
                || !string.Equals(scope.pawnId, pawnId, StringComparison.Ordinal)
                || !string.Equals(scope.foodThingId, foodThingId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(thoughtDefName))
            {
                return;
            }

            string value = thoughtDefName.Trim();
            for (int i = 0; i < scope.directThoughtDefNames.Count; i++)
                if (string.Equals(scope.directThoughtDefNames[i], value, StringComparison.Ordinal)) return;
            if (scope.directThoughtDefNames.Count < 32) scope.directThoughtDefNames.Add(value);
        }

        /// <summary>Seals the returned-thought set after the one matching FoodUtility call completes.</summary>
        internal static void SealDirectThoughts(string pawnId, string foodThingId)
        {
            FoodIngestionEvidenceScope scope = current;
            if (scope != null && !scope.closed
                && string.Equals(scope.pawnId, pawnId, StringComparison.Ordinal)
                && string.Equals(scope.foodThingId, foodThingId, StringComparison.Ordinal))
            {
                scope.directThoughtsCaptured = true;
            }
        }

        /// <summary>Returns a copy only for one exact thought vanilla returned for this ingestion.</summary>
        internal static FoodIngestionEvidenceFact CaptureForThought(
            string pawnId,
            string thoughtDefName)
        {
            FoodIngestionEvidenceScope scope = current;
            if (scope == null || scope.closed || !scope.directThoughtsCaptured
                || !string.Equals(scope.pawnId, pawnId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(thoughtDefName))
            {
                return null;
            }

            for (int i = 0; i < scope.directThoughtDefNames.Count; i++)
                if (string.Equals(scope.directThoughtDefNames[i], thoughtDefName, StringComparison.Ordinal))
                    return Copy(scope.fact);
            return null;
        }

        /// <summary>Closes one nested scope and restores its parent without leaking cross-event state.</summary>
        internal static void End(FoodIngestionEvidenceScope scope)
        {
            if (scope == null || scope.closed) return;
            scope.closed = true;
            if (ReferenceEquals(current, scope))
            {
                current = scope.previous;
                return;
            }

            // An unexpected patch-order mismatch is safer as an empty correlation window than as a
            // stale fact which could enrich a later, unrelated thought.
            current = null;
        }

        private static FoodIngestionEvidenceFact Copy(FoodIngestionEvidenceFact source)
        {
            return source == null ? null : new FoodIngestionEvidenceFact
            {
                ingredientKind = source.ingredientKind ?? string.Empty,
                ingredientDefName = source.ingredientDefName ?? string.Empty,
                ingredientLabel = source.ingredientLabel ?? string.Empty
            };
        }
    }

    /// <summary>Fail-open boundary between the runtime scope, XML snapshot, and pure food policy.</summary>
    internal static class FoodThoughtEvidenceAdapter
    {
        private static bool failForTests;

        /// <summary>Adds optional exact food evidence; every failure leaves the base thought row intact.</summary>
        internal static bool TryEnrichCurrent(
            BeliefEventEvidence evidence,
            string pawnId,
            string thoughtDefName,
            bool ideologyActive)
        {
            if (!ideologyActive) return false;
            try
            {
                FoodIngestionEvidenceFact fact =
                    FoodIngestionEvidenceContext.CaptureForThought(pawnId, thoughtDefName);
                if (fact == null) return false;
                if (failForTests) throw new InvalidOperationException("food evidence adapter test fault");
                return FoodBeliefEvidencePolicy.TryEnrich(
                    evidence, fact, ideologyActive, DiaryBeliefPolicy.Snapshot());
            }
            catch (Exception exception)
            {
                Type type = exception.GetType();
                Log.WarningOnce(
                    "[Pawn Diary] Food belief enrichment failed; this page keeps ordinary thought "
                    + "context: " + type.FullName + ": " + exception.Message,
                    ("PawnDiary.FoodThoughtEvidenceAdapter." + type.FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>RimTest-only fault seam for the optional-enrichment fail-open contract.</summary>
        internal static void SetFailureForTests(bool value)
        {
            failForTests = value;
        }
    }
}
