// Pure dramatic-permit policy. Runtime adapters prove a successful vanilla use and copy all live
// objects into the contracts in RoyaltyContracts.cs; this file only normalizes XML string mappings,
// resolves one unambiguous owner, chooses a stable event family, and matches quick-aid identities.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Deterministic allowlist, owner, page, and quick-aid arbitration decisions.</summary>
    internal static class RoyalPermitPolicy
    {
        public const string MilitaryAidEventDefName = "RoyalPermitMilitaryAid";
        public const string TransportShuttleEventDefName = "RoyalPermitTransportShuttle";
        public const string OrbitalStrikeEventDefName = "RoyalPermitOrbitalStrike";
        public const string OrbitalSalvoEventDefName = "RoyalPermitOrbitalSalvo";

        /// <summary>
        /// Keeps the first valid row for each exact permit defName, preserves XML order, and applies a
        /// hard cap. Malformed/unknown rows fail closed instead of widening the allowlist.
        /// </summary>
        public static List<RoyalPermitFamilyRule> NormalizeMappings(
            IList<RoyalPermitFamilyRule> source,
            int maximumMappings)
        {
            List<RoyalPermitFamilyRule> result = new List<RoyalPermitFamilyRule>();
            if (source == null) return result;
            int cap = Clamp(maximumMappings, 1, 128, 32);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count && result.Count < cap; i++)
            {
                RoyalPermitFamilyRule row = source[i];
                string defName = SafeToken(row == null ? null : row.permitDefName);
                string family = SafeToken(row == null ? null : row.familyToken).ToLowerInvariant();
                if (defName.Length == 0 || !RoyalPermitFamilyTokens.IsKnown(family)
                    || !seen.Add(defName)) continue;
                result.Add(new RoyalPermitFamilyRule
                {
                    permitDefName = defName,
                    familyToken = family
                });
            }
            return result;
        }

        /// <summary>Maps one exact permit defName to a reviewed dramatic family, or empty.</summary>
        public static string FamilyFor(string permitDefName, RoyaltyPolicySnapshot policy)
        {
            string candidate = SafeToken(permitDefName);
            if (candidate.Length == 0) return string.Empty;
            IList<RoyalPermitFamilyRule> rules = policy?.permitFamilyRules;
            for (int i = 0; i < (rules == null ? 0 : rules.Count); i++)
            {
                RoyalPermitFamilyRule rule = rules[i];
                if (rule != null
                    && RoyalPermitFamilyTokens.IsKnown(rule.familyToken)
                    && string.Equals(rule.permitDefName, candidate, StringComparison.OrdinalIgnoreCase))
                    return rule.familyToken;
            }
            return string.Empty;
        }

        /// <summary>
        /// Returns one exact distinct owner. Repeated observations of the same owner are deduplicated
        /// and the newest deterministic snapshot wins; multiple distinct owners or cap overflow are
        /// deliberately ambiguous and return null.
        /// </summary>
        public static RoyalPermitOwnerCandidate SelectOwner(
            IList<RoyalPermitOwnerCandidate> candidates,
            int maximumDistinctOwners)
        {
            if (candidates == null) return null;
            int cap = Clamp(maximumDistinctOwners, 1, 16, 4);
            Dictionary<string, RoyalPermitOwnerCandidate> distinct =
                new Dictionary<string, RoyalPermitOwnerCandidate>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                RoyalPermitOwnerCandidate candidate = candidates[i];
                if (!ValidOwner(candidate)) continue;
                RoyalPermitOwnerCandidate previous;
                if (!distinct.TryGetValue(candidate.ownerPawnId, out previous))
                {
                    if (distinct.Count >= cap) return null;
                    distinct.Add(candidate.ownerPawnId, candidate);
                }
                else if (Prefer(candidate, previous))
                {
                    distinct[candidate.ownerPawnId] = candidate;
                }
            }
            if (distinct.Count != 1) return null;
            foreach (RoyalPermitOwnerCandidate owner in distinct.Values) return owner;
            return null;
        }

        /// <summary>Builds one exact success DTO after family and owner proof, or null.</summary>
        public static RoyalPermitUseSnapshot BuildUse(
            RoyalPermitOwnerCandidate owner,
            string permitDefName,
            string permitLabel,
            bool usedDuringCooldown,
            int tick,
            RoyaltyPolicySnapshot policy)
        {
            string defName = SafeToken(permitDefName);
            string family = FamilyFor(defName, policy);
            if (!ValidOwner(owner) || family.Length == 0
                || !string.Equals(owner.permitDefName, defName, StringComparison.OrdinalIgnoreCase))
                return null;
            return new RoyalPermitUseSnapshot
            {
                ownerPawnId = owner.ownerPawnId,
                ownerPawnName = CleanText(owner.ownerPawnName),
                permitDefName = defName,
                permitLabel = CleanText(permitLabel),
                permitFamilyToken = family,
                factionId = owner.factionId,
                factionName = CleanText(owner.factionName),
                titleDefName = SafeToken(owner.titleDefName),
                titleLabel = CleanText(owner.titleLabel),
                mapId = SafeId(owner.mapId),
                mapLabel = CleanText(owner.mapLabel),
                usedDuringCooldown = usedDuringCooldown,
                tick = Math.Max(0, tick)
            };
        }

        /// <summary>Separates recognized source ownership from player-configured page output.</summary>
        public static RoyalPermitDecision Decide(
            RoyalPermitUseSnapshot use,
            bool policyEnabled,
            bool outputEnabled)
        {
            RoyalPermitDecision result = new RoyalPermitDecision();
            if (!ValidUse(use)) return result;
            result.recognized = true;
            result.familyToken = use.permitFamilyToken;
            result.eventDefName = EventDefNameForFamily(use.permitFamilyToken);
            result.shouldEmit = policyEnabled && outputEnabled && result.eventDefName.Length > 0;
            return result;
        }

        /// <summary>
        /// Builds one source-owned identity row for an exact allowlisted successful permit use. The
        /// evidence can let the existing Royalty title provider describe the caller's current authority;
        /// it never authorizes a page or claims that the requested aid, shuttle, or strike finished.
        /// </summary>
        public static NarrativeEvidence BuildNarrativeEvidence(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            RoyalPermitUseSnapshot use,
            RoyaltyPolicySnapshot policy)
        {
            string safeEventId = SafeId(eventId);
            string safePovPawnId = SafeId(povPawnId);
            if (safeEventId.Length == 0 || safePovPawnId.Length == 0 || tick < 0
                || !ValidUse(use) || tick != use.tick
                || !string.Equals(safePovPawnId, use.ownerPawnId, StringComparison.Ordinal))
            {
                return null;
            }

            // Re-check the exact XML mapping at the evidence boundary. A forged/malformed DTO, routine
            // vanilla permit, or unknown modded permit must not acquire narrative authority merely by
            // carrying a known family token. Explicit XML opt-in remains the only extension mechanism.
            string mappedFamily = FamilyFor(use.permitDefName, policy);
            string eventDefName = EventDefNameForFamily(mappedFamily);
            if (mappedFamily.Length == 0 || eventDefName.Length == 0
                || !string.Equals(mappedFamily, use.permitFamilyToken, StringComparison.Ordinal))
            {
                return null;
            }

            List<string> topics = new List<string> { "authority", "service" };
            if (mappedFamily == RoyalPermitFamilyTokens.MilitaryAid
                || mappedFamily == RoyalPermitFamilyTokens.OrbitalStrike
                || mappedFamily == RoyalPermitFamilyTokens.OrbitalSalvo)
            {
                topics.Add("violence");
            }

            return new NarrativeEvidence
            {
                eventId = safeEventId,
                tick = tick,
                povPawnId = safePovPawnId,
                povRole = (povRole ?? string.Empty).Trim(),
                facet = NarrativeFacetTokens.IdentityTransition,
                phase = mappedFamily,
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = use.ownerPawnId.Trim(),
                subjectLabel = CleanText(use.ownerPawnName),
                arcKey = string.Empty,
                beliefTopics = topics,
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                sourceDomain = RoyaltyNarrativeEvidenceFactory.PermitSourceDomain,
                sourceDefName = eventDefName
            };
        }

        /// <summary>Maps a reviewed family to its stable saved event Def name.</summary>
        public static string EventDefNameForFamily(string family)
        {
            if (family == RoyalPermitFamilyTokens.MilitaryAid) return MilitaryAidEventDefName;
            if (family == RoyalPermitFamilyTokens.TransportShuttle) return TransportShuttleEventDefName;
            if (family == RoyalPermitFamilyTokens.OrbitalStrike) return OrbitalStrikeEventDefName;
            if (family == RoyalPermitFamilyTokens.OrbitalSalvo) return OrbitalSalvoEventDefName;
            return string.Empty;
        }

        /// <summary>True only for the same exact faction/map inside the XML-owned elapsed window.</summary>
        public static bool MatchesQuickAid(
            RoyalQuickAidSnapshot raid,
            RoyalPermitUseSnapshot use,
            int currentTick,
            int correlationTicks)
        {
            if (!ValidQuickAid(raid) || !ValidUse(use)
                || use.permitFamilyToken != RoyalPermitFamilyTokens.MilitaryAid
                || !string.Equals(raid.factionId, use.factionId, StringComparison.Ordinal)
                || !string.Equals(raid.mapId, use.mapId, StringComparison.Ordinal)) return false;
            int window = Math.Max(1, correlationTicks);
            long useDistance = (long)use.tick - raid.tick;
            long nowDistance = (long)currentTick - raid.tick;
            return useDistance >= 0 && useDistance < window && nowDistance >= 0 && nowDistance < window;
        }

        /// <summary>Reverse-order matcher for a modded permit owner observed just before its raid.</summary>
        public static bool MatchesRecentOwner(
            RoyalPermitUseSnapshot use,
            RoyalQuickAidSnapshot raid,
            int correlationTicks)
        {
            if (!ValidQuickAid(raid) || !ValidUse(use)
                || use.permitFamilyToken != RoyalPermitFamilyTokens.MilitaryAid
                || !string.Equals(raid.factionId, use.factionId, StringComparison.Ordinal)
                || !string.Equals(raid.mapId, use.mapId, StringComparison.Ordinal)) return false;
            long distance = (long)raid.tick - use.tick;
            return distance >= 0 && distance < Math.Max(1, correlationTicks);
        }

        /// <summary>Elapsed expiry tolerates a reset/backwards game clock by expiring stale state.</summary>
        public static bool QuickAidExpired(int stagedTick, int currentTick, int correlationTicks)
        {
            long elapsed = (long)currentTick - stagedTick;
            return elapsed < 0 || elapsed >= Math.Max(1, correlationTicks);
        }

        private static bool ValidOwner(RoyalPermitOwnerCandidate owner)
        {
            return owner != null && SafeId(owner.ownerPawnId).Length > 0
                && SafeToken(owner.permitDefName).Length > 0 && SafeId(owner.factionId).Length > 0;
        }

        private static bool ValidUse(RoyalPermitUseSnapshot use)
        {
            return use != null && SafeId(use.ownerPawnId).Length > 0
                && SafeToken(use.permitDefName).Length > 0 && SafeId(use.factionId).Length > 0
                && RoyalPermitFamilyTokens.IsKnown(use.permitFamilyToken) && use.tick >= 0;
        }

        private static bool ValidQuickAid(RoyalQuickAidSnapshot raid)
        {
            return raid != null && SafeId(raid.correlationId).Length > 0
                && SafeId(raid.factionId).Length > 0 && SafeId(raid.mapId).Length > 0 && raid.tick >= 0;
        }

        private static bool Prefer(RoyalPermitOwnerCandidate candidate, RoyalPermitOwnerCandidate previous)
        {
            if (candidate.observedTick != previous.observedTick)
                return candidate.observedTick > previous.observedTick;
            return string.CompareOrdinal(CandidateKey(candidate), CandidateKey(previous)) < 0;
        }

        private static string CandidateKey(RoyalPermitOwnerCandidate value)
        {
            return (value.ownerPawnName ?? string.Empty) + "|" + (value.titleDefName ?? string.Empty)
                + "|" + (value.mapId ?? string.Empty);
        }

        private static string SafeToken(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }

        private static string SafeId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }

        private static string CleanText(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value >= minimum && value <= maximum ? value : fallback;
        }
    }
}
