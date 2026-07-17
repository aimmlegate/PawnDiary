// Pure correlation and state construction for Odyssey's takeoff/travel/landing lifecycle. Runtime
// adapters copy live RimWorld objects into these DTOs; this file decides only identity, timing,
// fallback completeness, and stale-state rules without Verse, Unity, Harmony, or mutable game state.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Correlates transient Odyssey observations and builds one committed journey snapshot.</summary>
    internal static class OdysseyLifecyclePolicy
    {
        /// <summary>True when a takeoff intent belongs to this exact commit and remains timely.</summary>
        public static bool IntentMatches(
            OdysseyTakeoffIntent intent,
            OdysseyTravelCommitObservation observation,
            OdysseyPolicySnapshot policy)
        {
            if (intent == null || observation == null
                || !Same(intent.engineId, observation.engineId)
                || intent.captureTick < 0 || observation.departureTick < intent.captureTick)
            {
                return false;
            }

            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            if ((long)observation.departureTick - intent.captureTick
                > Math.Max(1, effective.takeoffCorrelationTicks))
            {
                return false;
            }

            string selected = Clean(intent.selectedTargetKey);
            string committed = Clean(observation.destination?.stableKey);
            return selected.Length == 0 || (committed.Length > 0 && Same(selected, committed));
        }

        /// <summary>
        /// Creates one valid journey. A matched intent supplies pre-despawn origin/launch truth;
        /// otherwise the commit remains conservative and explicitly incomplete.
        /// </summary>
        public static OdysseyJourneySnapshot BuildJourney(
            OdysseyTravelCommitObservation observation,
            OdysseyTakeoffIntent intent,
            OdysseyPolicySnapshot policy,
            out bool matchedIntent)
        {
            matchedIntent = IntentMatches(intent, observation, policy);
            if (observation == null || observation.departureTick < 0)
            {
                return null;
            }

            string shipId = CleanIdentity(observation.shipStableId);
            string journeyId = OdysseyArcKeys.Journey(shipId, observation.departureTick);
            if (journeyId.Length == 0)
            {
                return null;
            }

            OdysseyTakeoffIntent source = matchedIntent ? intent : null;
            string shipName = Clean(observation.shipName);
            if (shipName.Length == 0 && source != null)
            {
                shipName = Clean(source.shipName);
            }

            return new OdysseyJourneySnapshot
            {
                journeyId = journeyId,
                shipStableId = shipId,
                shipName = shipName,
                origin = CopyLocation(source?.origin ?? observation.origin),
                destination = CopyLocation(observation.destination),
                departureTick = observation.departureTick,
                launchQualityBand = source == null
                    ? NormalizeQuality(observation.launchQualityBand)
                    : NormalizeQuality(source.launchQualityBand),
                roughLanding = source == null ? observation.roughLanding : source.roughLanding,
                sourceComplete = source != null && source.origin != null && observation.destination != null,
                baselineOnly = false,
                landingApplied = false,
                writers = CopyWriters(source != null && source.writers != null && source.writers.Count > 0
                    ? source.writers
                    : observation.writers)
            };
        }

        /// <summary>True when pending landing facts belong to this exact ship and finish window.</summary>
        public static bool PendingLandingMatches(
            OdysseyPendingLanding pending,
            string shipStableId,
            int finishTick,
            OdysseyPolicySnapshot policy)
        {
            if (pending == null || !Same(pending.shipStableId, shipStableId)
                || pending.captureTick < 0 || finishTick < pending.captureTick)
            {
                return false;
            }

            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            return (long)finishTick - pending.captureTick
                <= Math.Max(1, effective.landingCorrelationTicks);
        }

        /// <summary>
        /// Attaches an exact, successfully applied vanilla outcome to its correlated pending landing.
        /// Empty identities, another ship, or delimiter-only prompt data are rejected fail-soft.
        /// </summary>
        public static bool TryAttachLandingOutcome(
            OdysseyPendingLanding pending,
            string shipStableId,
            string outcomeDefName,
            string outcomeLabel)
        {
            if (pending == null || !Same(pending.shipStableId, shipStableId)) return false;
            string defName = CleanIdentity(outcomeDefName);
            if (defName.Length == 0) return false;
            pending.landingOutcomeDefName = defName;
            pending.landingOutcomeLabel = Clean(outcomeLabel);
            return true;
        }

        /// <summary>True when a transient or active row has exceeded its conservative retention cap.</summary>
        public static bool IsExpired(int capturedTick, int nowTick, int retentionTicks)
        {
            return capturedTick < 0 || nowTick < capturedTick
                || (long)nowTick - capturedTick > Math.Max(1, retentionTicks);
        }

        private static List<OdysseyWriterCandidate> CopyWriters(List<OdysseyWriterCandidate> source)
        {
            List<OdysseyWriterCandidate> result = new List<OdysseyWriterCandidate>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                OdysseyWriterCandidate row = source[i];
                if (row == null) continue;
                result.Add(new OdysseyWriterCandidate
                {
                    pawnId = Clean(row.pawnId),
                    displayName = row.displayName ?? string.Empty,
                    roleToken = row.roleToken ?? string.Empty,
                    eligible = row.eligible,
                    present = row.present,
                    hasDiary = row.hasDiary
                });
            }

            return result;
        }

        private static OdysseyLocationSnapshot CopyLocation(OdysseyLocationSnapshot source)
        {
            if (source == null) return null;
            return new OdysseyLocationSnapshot
            {
                stableKey = source.stableKey ?? string.Empty,
                visibleLabel = source.visibleLabel ?? string.Empty,
                layerToken = OdysseyLocationLayerTokens.Normalize(source.layerToken),
                biomeDefName = source.biomeDefName ?? string.Empty,
                biomeLabel = source.biomeLabel ?? string.Empty,
                siteDefName = source.siteDefName ?? string.Empty,
                siteLabel = source.siteLabel ?? string.Empty,
                categoryToken = source.categoryToken ?? string.Empty,
                majorDestination = source.majorDestination,
                isPlayerHome = source.isPlayerHome,
                visible = source.visible
            };
        }

        private static string NormalizeQuality(string value)
        {
            if (value == OdysseyLaunchQualityTokens.Poor
                || value == OdysseyLaunchQualityTokens.Ordinary
                || value == OdysseyLaunchQualityTokens.Excellent)
                return value;
            return OdysseyLaunchQualityTokens.Unknown;
        }

        private static bool Same(string left, string right)
        {
            string a = Clean(left);
            string b = Clean(right);
            return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
        }

        private static string CleanIdentity(string value)
        {
            string cleaned = Clean(value);
            return cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
