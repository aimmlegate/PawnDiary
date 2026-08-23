// MemorySavedIdentityCarrierRegistry.cs — the exhaustive §T13.2 allocator-carrier registry.
//
// BEFORE any epoch/faction allocation, reservation, semantic migration, or Brainwipe, the component
// scans every epoch/faction-generation carrier in the save and publishes repaired high-waters
// atomically. This file is the PURE planning half: it consumes detached carrier tuples (the impure
// adapter extracts them from saved rows) and returns the repaired values plus fail-closed flags.
// Only exact expected codecs are parsed here; arbitrary text is never searched.
//
// Rules implemented (§T13.2 / §T6.9 / §T5.5):
// - every valid NORMAL memory-epoch-v1 token and every valid reservation raises
//   lastIssuedAutobiographicalEpochSequence; fallback tokens enter the live collision set without
//   a numeric sequence; malformed tokens are inert;
// - a valid nonempty fallback chain forces permanent fallback mode and pins the high-water at
//   long.MaxValue so a corrupt-low saved value can never re-enter normal allocation;
// - an invalid nonempty chain, or an empty chain with live fallback carriers, is a repair-needed
//   state that refuses ordinary allocation;
// - faction allocator generations raise monotonically and never wrap;
// - duplicate/corrupt reservations repair deterministically: owners in ordinal order each keep
//   their lowest valid sequence not already claimed by an earlier owner row.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>One detached legacy reservation candidate extracted by the adapter.</summary>
    internal sealed class MemoryLegacyEpochReservationInput
    {
        public string ownerPawnId = string.Empty;
        public long reservedEpochSequence;
    }

    /// <summary>Detached carrier scan input assembled from one loaded save.</summary>
    internal sealed class MemorySavedCarrierScanInput
    {
        /// <summary>Every epoch-token string found in any §T13.2 carrier field.</summary>
        public List<string> epochTokenCarriers = new List<string>();
        /// <summary>Every reservation row found in legacyOwnerEpochReservations.</summary>
        public List<MemoryLegacyEpochReservationInput> legacyReservations =
            new List<MemoryLegacyEpochReservationInput>();
        /// <summary>Every SavedGlobalFactionSnapshot.allocatorGeneration witness.</summary>
        public List<long> factionAllocatorGenerationCarriers = new List<long>();
        public long lastIssuedAutobiographicalEpochSequence;
        public string lastIssuedAutobiographicalEpochFallbackChain = string.Empty;
        public long globalFactionSnapshotAllocatorGeneration;
    }

    /// <summary>The pure registry plan. The adapter commits these fields together only when the
    /// repair-needed flags are clear or the caller is performing that exact repair.</summary>
    internal sealed class MemorySavedCarrierRegistryPlan
    {
        public bool canPublish;

        /// <summary>The monotone-raised autobiographical high-water (never lowered or wrapped).</summary>
        public long repairedAutobiographicalHighWater;
        /// <summary>The input chain when valid; empty otherwise. Repair paths publish new chains
        /// only through the checked allocator during Brainwipe/repair.</summary>
        public string effectiveFallbackChain = string.Empty;
        /// <summary>A valid nonempty chain permanently saturates the numeric allocator.</summary>
        public bool fallbackModeForced;
        /// <summary>Nonempty chain present but not 64 lowercase hex characters.</summary>
        public bool invalidFallbackChainNeedsRepair;
        /// <summary>Fallback carriers exist while the saved chain is still empty.</summary>
        public bool inconsistentFallbackRegistryNeedsRepair;

        /// <summary>All valid normal+fallback tokens; the allocator's live collision set P.</summary>
        public List<string> liveEpochTokens = new List<string>();

        /// <summary>The raised component faction allocator generation.</summary>
        public long globalFactionAllocatorGeneration;
        /// <summary>True when a carrier sits exactly at long.MaxValue; typed refusal, no wrap.</summary>
        public bool factionGenerationSaturated;

        /// <summary>Deterministically repaired reservation rows, sorted by owner ID ordinal.</summary>
        public List<MemoryLegacyEpochReservationInput> normalizedReservations =
            new List<MemoryLegacyEpochReservationInput>();
        public int droppedReservationCount;
        public int malformedEpochCarrierCount;
    }

    internal static class MemorySavedIdentityCarrierRegistry
    {
        private const int MaximumOwnerIdentifierCharacters =
            MemoryIdentityCodec.MaximumRawIdentityCharacters;

        public static MemorySavedCarrierRegistryPlan Plan(MemorySavedCarrierScanInput input)
        {
            MemorySavedCarrierRegistryPlan plan = new MemorySavedCarrierRegistryPlan();
            if (input == null)
            {
                return plan;
            }

            long highWater = input.lastIssuedAutobiographicalEpochSequence >= 0
                ? input.lastIssuedAutobiographicalEpochSequence
                : 0;
            SortedSet<string> liveTokens = new SortedSet<string>(StringComparer.Ordinal);
            bool hasFallbackCarrier = false;
            plan.malformedEpochCarrierCount = 0;

            foreach (string candidate in input.epochTokenCarriers ?? new List<string>())
            {
                bool isFallback;
                long normalSequence;
                if (!MemoryIdentityCodec.TryParseEpochToken(
                        candidate, out isFallback, out normalSequence))
                {
                    // Malformed carriers are inert: never guessed into a second identity.
                    plan.malformedEpochCarrierCount++;
                    continue;
                }

                liveTokens.Add(candidate);
                hasFallbackCarrier |= isFallback;
                if (!isFallback && normalSequence > highWater)
                {
                    highWater = normalSequence;
                }
            }

            // Reservations are themselves sequence witnesses (§T13.2 carrier table).
            List<MemoryLegacyEpochReservationInput> validReservations =
                new List<MemoryLegacyEpochReservationInput>();
            foreach (MemoryLegacyEpochReservationInput reservation
                in input.legacyReservations ?? new List<MemoryLegacyEpochReservationInput>())
            {
                if (!IsValidReservation(reservation))
                {
                    plan.droppedReservationCount++;
                    continue;
                }

                validReservations.Add(reservation);
                if (reservation.reservedEpochSequence > highWater)
                {
                    highWater = reservation.reservedEpochSequence;
                }
            }

            string chain = input.lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty;
            bool chainEmpty = chain.Length == 0;
            bool chainValid = IsLowercaseSha256(chain);
            plan.invalidFallbackChainNeedsRepair = !chainEmpty && !chainValid;
            plan.inconsistentFallbackRegistryNeedsRepair = chainEmpty && hasFallbackCarrier;
            plan.fallbackModeForced = chainValid && !chainEmpty;
            plan.effectiveFallbackChain = chainValid ? chain : string.Empty;
            if (plan.fallbackModeForced)
            {
                // A nonempty valid chain forces permanent fallback mode even if a corrupt saved
                // numeric high-water is lower (§T6.9); repair first raises the high-water.
                highWater = long.MaxValue;
            }

            plan.repairedAutobiographicalHighWater = highWater;
            plan.liveEpochTokens = new List<string>(liveTokens);

            plan.globalFactionAllocatorGeneration = input.globalFactionSnapshotAllocatorGeneration > 0
                ? input.globalFactionSnapshotAllocatorGeneration
                : 0;
            foreach (long generation
                in input.factionAllocatorGenerationCarriers ?? new List<long>())
            {
                if (generation <= 0)
                {
                    continue;
                }

                if (generation == long.MaxValue)
                {
                    plan.factionGenerationSaturated = true;
                }

                if (generation > plan.globalFactionAllocatorGeneration)
                {
                    plan.globalFactionAllocatorGeneration = generation;
                }
            }

            plan.normalizedReservations = NormalizeReservations(validReservations);
            plan.canPublish = true;
            return plan;
        }

        /// <summary>
        /// Deterministic reservation repair (§T6.9): owners visit in ordinal order; each owner
        /// keeps its lowest valid sequence not already claimed by an earlier owner row; all
        /// replacements are allocated later through the checked component reservation plan, never
        /// guessed here.
        /// </summary>
        private static List<MemoryLegacyEpochReservationInput> NormalizeReservations(
            List<MemoryLegacyEpochReservationInput> validReservations)
        {
            // Ascending owner, then ascending sequence, so each owner's candidate list is ordered.
            validReservations.Sort((left, right) =>
            {
                int owner = string.CompareOrdinal(left.ownerPawnId, right.ownerPawnId);
                return owner != 0
                    ? owner
                    : left.reservedEpochSequence.CompareTo(right.reservedEpochSequence);
            });

            List<MemoryLegacyEpochReservationInput> normalized =
                new List<MemoryLegacyEpochReservationInput>();
            HashSet<long> claimedSequences = new HashSet<long>();
            string currentOwner = null;
            bool currentOwnerResolved = false;
            foreach (MemoryLegacyEpochReservationInput reservation in validReservations)
            {
                if (!string.Equals(currentOwner, reservation.ownerPawnId, StringComparison.Ordinal))
                {
                    currentOwner = reservation.ownerPawnId;
                    currentOwnerResolved = false;
                }

                if (currentOwnerResolved
                    || claimedSequences.Contains(reservation.reservedEpochSequence))
                {
                    continue;
                }

                claimedSequences.Add(reservation.reservedEpochSequence);
                currentOwnerResolved = true;
                normalized.Add(new MemoryLegacyEpochReservationInput
                {
                    ownerPawnId = reservation.ownerPawnId,
                    reservedEpochSequence = reservation.reservedEpochSequence
                });
            }

            return normalized;
        }

        private static bool IsValidReservation(MemoryLegacyEpochReservationInput reservation)
        {
            return reservation != null
                && !string.IsNullOrWhiteSpace(reservation.ownerPawnId)
                && reservation.ownerPawnId.Length <= MaximumOwnerIdentifierCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(reservation.ownerPawnId)
                && reservation.reservedEpochSequence > 0;
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9')
                    || (current >= 'a' && current <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
