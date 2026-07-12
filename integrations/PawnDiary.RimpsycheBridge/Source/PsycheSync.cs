// Rimpsyche read/sync edge for Tier A (prompt context) and Tier B (psychotype override).
//
// Flow and ownership:
//   Rimpsyche CompPsyche (live/impure, read-only) -> plain PsycheNodeValue snapshot -> pure mapping /
//   formatting -> Pawn Diary public API (impure provider or source-owned override).
//
// The bridge never writes into Rimpsyche. Every method whose BODY names RimPsyche.dll types is marked
// [NoInlining] and is reached only after PawnDiaryRimpsycheMod.RimpsycheActive. That lets the adapter
// assembly itself remain loadable/inert when a user ignores About.xml's hard dependency, matching the
// established 1-2-3 Personalities isolation pattern.
//
// Tier A runs during every prompt build, so its formatted line is cached per pawn by the rounded node
// vector hash. A cache hit still reads the 34 floats to detect changes, but avoids adjective translation,
// interest ranking, and string building. Tier B runs only on the GameComponent's coarse 250-tick pass.
//
// New to C#/RimWorld? See AGENTS.md, docs/lore/pawns.md, and PersonaSync.cs in the RimTalk bridge.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using PawnDiaryRimpsyche.Pure;
using RimWorld;
using Verse;

namespace PawnDiaryRimpsyche
{
    /// <summary>Read-only Rimpsyche snapshot adapter plus Pawn Diary context/override synchronization.</summary>
    internal static class PsycheSync
    {
        // Tier-A cache: pawn load id -> rounded vector hash + already-localized one-line summary.
        private static readonly Dictionary<string, CachedContextLine> ContextLineByPawn =
            new Dictionary<string, CachedContextLine>();

        // Tier-B bookkeeping: pawn load id -> rounded vector hash most recently accepted by
        // SetPsychotypeOverride. In-memory only; a reload safely reapplies after the reset sweep.
        private static readonly Dictionary<string, string> AppliedVectorHash =
            new Dictionary<string, string>();

        private static bool tierBWasActive;
        private static bool newGameResetPending;

        /// <summary>
        /// Registers the process-global Tier-A context provider. Registration is safe before a game
        /// exists and idempotent by provider id; caller checks ApiVersion and target-mod activity.
        /// </summary>
        public static void RegisterContextProvider()
        {
            if (!PawnDiaryRimpsycheMod.SupportsApiVersion(1))
            {
                return;
            }

            PawnDiaryApi.RegisterPawnContextProvider(BridgeIds.ContextProviderId, ProvidePsycheLine);
        }

        /// <summary>
        /// Clears process-static caches for a newly loaded game and schedules the source-owned override
        /// sweep for the first real main-thread tick. GameComponent.FinalizeInit can be off-thread in
        /// RimWorld 1.6, so it must not call PawnDiaryApi directly.
        /// </summary>
        public static void PrepareForNewGame()
        {
            ContextLineByPawn.Clear();
            AppliedVectorHash.Clear();
            tierBWasActive = false;
            newGameResetPending = true;
        }

        /// <summary>
        /// Main-thread Tier-B pass. Applies only on rounded-vector change, resets all bridge-owned
        /// overrides when the setting/master switch turns off, and performs the deferred new-game sweep.
        /// </summary>
        public static void RunTierBPass()
        {
            if (!PawnDiaryRimpsycheMod.SupportsApiVersion(1))
            {
                return;
            }

            // Reset first, then apply the current game's values in the same pass. If Pawn Diary is not
            // ready yet, retain the pending flag and retry rather than falsely declaring the sweep done.
            if (newGameResetPending)
            {
                if (!PawnDiaryApi.IsReady || !PawnDiaryApi.IsExternalApiEnabled)
                {
                    return;
                }

                ResetAllOverridesInternal();
                newGameResetPending = false;
            }

            PawnDiaryRimpsycheSettings settings = PawnDiaryRimpsycheMod.Settings;
            bool active = PawnDiaryRimpsycheMod.RimpsycheActive
                && settings != null
                && settings.usePsychotypeOverride
                && PawnDiaryApi.IsReady
                && PawnDiaryApi.IsExternalApiEnabled;

            if (!active)
            {
                // A master-toggle drop can make reset calls temporarily ineligible. Keep the prior-state
                // marker until the API is usable so we do not strand a bridge-owned saved override.
                if (tierBWasActive || AppliedVectorHash.Count > 0)
                {
                    if (!PawnDiaryApi.IsReady || !PawnDiaryApi.IsExternalApiEnabled)
                    {
                        return;
                    }

                    ResetAllOverridesInternal();
                }

                tierBWasActive = false;
                return;
            }

            tierBWasActive = true;
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                ApplyTierBFor(pawn);
            }
        }

        // Tier A provider. Registered only while Rimpsyche is active, but retain every guard because
        // providers are process-global and defensive adapters should remain harmless during odd load states.
        private static string ProvidePsycheLine(Pawn pawn)
        {
            if (pawn == null
                || !PawnDiaryRimpsycheMod.RimpsycheActive
                || !PawnDiaryRimpsycheMod.SupportsApiVersion(1))
            {
                return null;
            }

            NodeVectorSnapshot snapshot = ReadNodeVector(pawn);
            string pawnId = pawn.GetUniqueLoadID();
            if (snapshot == null || snapshot.Nodes.Count == 0)
            {
                ContextLineByPawn.Remove(pawnId);
                return null;
            }

            CachedContextLine cached;
            if (ContextLineByPawn.TryGetValue(pawnId, out cached)
                && string.Equals(cached.VectorHash, snapshot.VectorHash, StringComparison.Ordinal))
            {
                return cached.Line;
            }

            RimpsycheBridgeTuningDef tuning = RimpsycheBridgeTuningDef.Current;
            List<string> interests = ReadRankedInterestLabels(pawn, Math.Max(0, tuning.summaryMaxInterests));
            string line = PsycheSummaryFormat.Format(
                snapshot.Nodes,
                interests,
                tuning.summaryMagnitudeFloor,
                Math.Max(0, tuning.summaryMaxDescriptors),
                Math.Max(0, tuning.summaryMaxInterests),
                ResolveKeyedOrBlank);

            if (string.IsNullOrWhiteSpace(line))
            {
                ContextLineByPawn.Remove(pawnId);
                return null;
            }

            ContextLineByPawn[pawnId] = new CachedContextLine
            {
                VectorHash = snapshot.VectorHash,
                Line = line
            };
            return line;
        }

        // Applies or refreshes one Tier-B source-owned outlook. This method itself names only plain
        // Pawn/pure/API types; the target-mod types remain isolated inside ReadNodeVector.
        private static void ApplyTierBFor(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryApi.IsDiaryEligible(pawn))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            NodeVectorSnapshot snapshot = ReadNodeVector(pawn);
            if (snapshot == null || snapshot.Nodes.Count == 0)
            {
                ReleasePawnOverride(pawn, pawnId);
                return;
            }

            string previousHash;
            if (AppliedVectorHash.TryGetValue(pawnId, out previousHash)
                && string.Equals(previousHash, snapshot.VectorHash, StringComparison.Ordinal))
            {
                return;
            }

            PsycheLensPlan plan = PsycheLensMapping.SelectDominantPair(snapshot.Nodes);
            string rule = PsycheLensMapping.ComposeRule(plan, ResolveKeyedOrBlank);
            if (string.IsNullOrWhiteSpace(rule))
            {
                ReleasePawnOverride(pawn, pawnId);
                return;
            }

            if (PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, rule))
            {
                AppliedVectorHash[pawnId] = snapshot.VectorHash;
            }
        }

        private static void ReleasePawnOverride(Pawn pawn, string pawnId)
        {
            if (!AppliedVectorHash.ContainsKey(pawnId))
            {
                return;
            }

            PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            AppliedVectorHash.Remove(pawnId);
        }

        // Every pawn the bridge may have touched: spawned/free colonists, caravans/travelling pods,
        // and alive world pawns (which covers kidnapped, caravan, cryptosleep, and departed colonists).
        // This exactly follows the established PersonaSync sweep and deduplicates by load id.
        private static IEnumerable<Pawn> TouchedPawns()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                {
                    yield return pawn;
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                    {
                        yield return pawn;
                    }
                }
            }
        }

        private static void ResetAllOverridesInternal()
        {
            foreach (Pawn pawn in TouchedPawns())
            {
                PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            }

            AppliedVectorHash.Clear();
        }

        /// <summary>
        /// Reads the enabled CompPsyche's 34 known PersonalityDefs into a plain vector. Every target-mod
        /// type is a local inside this NoInlining method, reached only behind RimpsycheActive. Missing or
        /// renamed defs are skipped by dictionary lookup; values are never indexed by installed order.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static NodeVectorSnapshot ReadNodeVector(Pawn pawn)
        {
            try
            {
                Maux36.RimPsyche.CompPsyche comp = Maux36.RimPsyche.PawnExtensions.compPsyche(pawn);
                if (comp == null || !comp.Enabled || comp.Personality == null
                    || Maux36.RimPsyche.RimpsycheDatabase.PersonalityDict == null)
                {
                    return null;
                }

                List<PsycheNodeValue> nodes = new List<PsycheNodeValue>(PsycheLensMapping.Definitions.Count);
                for (int i = 0; i < PsycheLensMapping.Definitions.Count; i++)
                {
                    PsycheNodeDefinition definition = PsycheLensMapping.Definitions[i];
                    Maux36.RimPsyche.PersonalityDef personalityDef;
                    if (!Maux36.RimPsyche.RimpsycheDatabase.PersonalityDict.TryGetValue(
                        definition.DefName, out personalityDef)
                        || personalityDef == null)
                    {
                        continue;
                    }

                    float value = comp.Personality.GetPersonality(personalityDef);
                    nodes.Add(new PsycheNodeValue(definition.DefName, value));
                }

                return nodes.Count == 0
                    ? null
                    : new NodeVectorSnapshot(nodes, PsycheLensMapping.StableVectorHash(nodes));
            }
            catch
            {
                // Odd half-generated pawns and target-mod version drift should omit one context line,
                // never break the whole Pawn Diary prompt build or periodic sync pass.
                return null;
            }
        }

        /// <summary>
        /// Reads up to N strongest positive raw interests and returns their already-localized embedded
        /// Def labels. Access is isolated/no-inline for the same missing-assembly reason as node reads.
        /// The tracker exposes no non-initializing label query; this runs only on a Tier-A cache miss.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<string> ReadRankedInterestLabels(Pawn pawn, int maxCount)
        {
            List<string> labels = new List<string>();
            if (maxCount <= 0)
            {
                return labels;
            }

            try
            {
                Maux36.RimPsyche.CompPsyche comp = Maux36.RimPsyche.PawnExtensions.compPsyche(pawn);
                if (comp == null || !comp.Enabled || comp.Interests == null
                    || comp.Interests.interestScore == null
                    || Maux36.RimPsyche.RimpsycheDatabase.InterestList == null)
                {
                    return labels;
                }

                List<InterestCandidate> candidates = new List<InterestCandidate>();
                for (int i = 0; i < Maux36.RimPsyche.RimpsycheDatabase.InterestList.Count; i++)
                {
                    Maux36.RimPsyche.Interest interest = Maux36.RimPsyche.RimpsycheDatabase.InterestList[i];
                    if (interest == null || string.IsNullOrWhiteSpace(interest.name)
                        || string.IsNullOrWhiteSpace(interest.label))
                    {
                        continue;
                    }

                    float score;
                    if (!comp.Interests.interestScore.TryGetValue(interest.name, out score)
                        || score <= 0f
                        || float.IsNaN(score)
                        || float.IsInfinity(score))
                    {
                        continue;
                    }

                    candidates.Add(new InterestCandidate
                    {
                        Label = interest.label,
                        Score = score,
                        SourceOrder = i
                    });
                }

                candidates.Sort(delegate(InterestCandidate left, InterestCandidate right)
                {
                    int score = right.Score.CompareTo(left.Score);
                    return score != 0 ? score : left.SourceOrder.CompareTo(right.SourceOrder);
                });

                for (int i = 0; i < candidates.Count && labels.Count < maxCount; i++)
                {
                    labels.Add(candidates[i].Label);
                }
            }
            catch
            {
                // Interests are optional context. A tracker/data inconsistency should leave the
                // personality adjectives intact rather than aborting the whole provider.
            }

            return labels;
        }

        // Main-thread-only: RimWorld's translation database is not thread-safe. Returning blank on a
        // missing key lets the pure helpers use their English source fallback (avoids diacritic key text).
        private static string ResolveKeyedOrBlank(string key)
        {
            return !string.IsNullOrEmpty(key) && key.CanTranslate()
                ? key.Translate().Resolve()
                : string.Empty;
        }

        private sealed class NodeVectorSnapshot
        {
            public NodeVectorSnapshot(List<PsycheNodeValue> nodes, string vectorHash)
            {
                Nodes = nodes;
                VectorHash = vectorHash;
            }

            public List<PsycheNodeValue> Nodes { get; }
            public string VectorHash { get; }
        }

        private sealed class CachedContextLine
        {
            public string VectorHash;
            public string Line;
        }

        private sealed class InterestCandidate
        {
            public string Label;
            public float Score;
            public int SourceOrder;
        }
    }
}
