// Pure Odyssey novelty, cooldown, history, and qualitative-band policy. A successful landing always
// advances bounded history when its identity is valid, even when settings, cooldown, or writer loss
// suppresses the page. Runtime adapters remain responsible only for capture and persistence.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Qualitative projections shared by journey capture and prompt formatting.</summary>
    internal static class OdysseyJourneyPolicy
    {
        /// <summary>Maps elapsed ticks into XML-thresholded short/ordinary/long prose bands.</summary>
        public static string DurationBand(int departureTick, int landingTick, OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            long elapsed = Math.Max(0L, (long)landingTick - departureTick);
            int shortMaximum = Math.Max(0, effective.shortJourneyMaximumTicks);
            int longMinimum = EffectiveLongMinimum(effective);
            if (elapsed <= shortMaximum) return OdysseyDurationTokens.Short;
            if (elapsed >= longMinimum) return OdysseyDurationTokens.Long;
            return OdysseyDurationTokens.Ordinary;
        }

        /// <summary>Keeps the long-reason and duration-band thresholds consistent under bad XML.</summary>
        internal static int EffectiveLongMinimum(OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            return Math.Max(Math.Max(0, effective.shortJourneyMaximumTicks) + 1,
                effective.longJourneyMinimumTicks);
        }

        /// <summary>Maps numeric ritual quality into a stable token; NaN/infinity remains unknown.</summary>
        public static string LaunchQualityBand(float quality, OdysseyPolicySnapshot policy)
        {
            if (float.IsNaN(quality) || float.IsInfinity(quality))
            {
                return OdysseyLaunchQualityTokens.Unknown;
            }

            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            float poorMaximum = Math.Max(0f, Math.Min(1f, effective.poorLaunchQualityMaximum));
            float excellentMinimum = Math.Max(
                poorMaximum,
                Math.Min(1f, effective.excellentLaunchQualityMinimum));
            if (quality <= poorMaximum) return OdysseyLaunchQualityTokens.Poor;
            if (quality >= excellentMinimum) return OdysseyLaunchQualityTokens.Excellent;
            return OdysseyLaunchQualityTokens.Ordinary;
        }
    }

    /// <summary>Plans a single landing page and the history changes that survive every drop path.</summary>
    internal static class OdysseyLandingPolicy
    {
        /// <summary>
        /// Chooses primary/secondary novelty reasons, applies cooldown and deterministic writer policy,
        /// and returns exact history mutations. Invalid or already-applied journeys fail closed.
        /// </summary>
        public static OdysseyLandingPlan Plan(
            OdysseyLandingObservation observation,
            OdysseyTravelHistorySnapshot history,
            OdysseyPolicySnapshot policy)
        {
            OdysseyLandingPlan plan = new OdysseyLandingPlan();
            OdysseyJourneySnapshot journey = observation == null ? null : observation.journey;
            if (journey == null
                || journey.landingApplied
                || string.IsNullOrWhiteSpace(journey.journeyId)
                || string.IsNullOrWhiteSpace(journey.shipStableId)
                || observation.landingTick < 0)
            {
                return plan;
            }

            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyTravelHistorySnapshot prior = history ?? new OdysseyTravelHistorySnapshot();
            OdysseyLocationSnapshot rawDestination = observation.destination ?? journey.destination;
            OdysseyLocationSnapshot destination = OdysseyLocationPolicy.Classify(rawDestination, effective);
            // A pre-feature mid-flight baseline uses the load tick only as a correlation ID. It is not
            // a truthful departure observation, so never turn it into elapsed-time prose.
            plan.durationBand = journey.baselineOnly
                ? string.Empty
                : OdysseyJourneyPolicy.DurationBand(
                    journey.departureTick,
                    observation.landingTick,
                    effective);
            plan.historyMutation = BuildMutation(journey, destination, observation.landingTick);

            List<ReasonCandidate> reasons = QualifyingReasons(journey, destination, prior, observation, effective);
            if (reasons.Count == 0)
            {
                return plan;
            }

            reasons.Sort(CompareReason);
            plan.primaryReason = reasons[0].rule.reasonToken;
            plan.important = reasons[0].rule.important;
            if (reasons.Count > 1)
            {
                plan.secondaryReason = reasons[1].rule.reasonToken;
                plan.important = plan.important || reasons[1].rule.important;
            }

            bool duplicate = ContainsOrdinal(prior.emittedJourneyIds, journey.journeyId);
            bool inCooldown = IsInCooldown(
                prior.lastLandingPageTick,
                observation.landingTick,
                effective.landingCooldownTicks);
            plan.droppedByCooldown = inCooldown && !AnyCooldownBypass(reasons);

            List<OdysseyWriterCandidate> writerSource = observation.writers != null
                && observation.writers.Count > 0
                ? observation.writers
                : journey.writers;
            plan.selectedWriters = OdysseyWriterPolicy.Select(
                writerSource,
                effective.maximumLandingWriters);

            plan.writePage = effective.enabled
                && observation.groupEnabled
                && !duplicate
                && !plan.droppedByCooldown
                && plan.selectedWriters.Count > 0;
            if (plan.writePage)
            {
                plan.historyMutation.landingPageTick = observation.landingTick;
                plan.historyMutation.journeyIdToMarkEmitted = journey.journeyId;
            }

            return plan;
        }

        private static List<ReasonCandidate> QualifyingReasons(
            OdysseyJourneySnapshot journey,
            OdysseyLocationSnapshot destination,
            OdysseyTravelHistorySnapshot history,
            OdysseyLandingObservation observation,
            OdysseyPolicySnapshot policy)
        {
            List<ReasonCandidate> result = new List<ReasonCandidate>();
            AddIf(result, OdysseyLandingReasonTokens.MajorDestination,
                destination.majorDestination && !string.IsNullOrWhiteSpace(destination.categoryToken), policy);

            bool returningHome = !string.IsNullOrWhiteSpace(destination.stableKey)
                && ContainsIgnoreCase(history.homeKeys, destination.stableKey)
                && history.committedJourneyCount > 1
                && journey.origin != null
                && !string.Equals(
                    Clean(journey.origin.stableKey),
                    Clean(destination.stableKey),
                    StringComparison.OrdinalIgnoreCase);
            AddIf(result, OdysseyLandingReasonTokens.Homecoming, returningHome, policy);

            bool firstOrbit = history.historyTrustworthyForFirstClaims
                && journey.origin != null
                && OdysseyLocationLayerTokens.Normalize(journey.origin.layerToken)
                    == OdysseyLocationLayerTokens.Surface
                && destination.layerToken == OdysseyLocationLayerTokens.Orbit
                && !ContainsIgnoreCase(history.visitedLayerKeys, OdysseyLocationLayerTokens.Orbit);
            AddIf(result, OdysseyLandingReasonTokens.FirstOrbit, firstOrbit, policy);

            bool newCategory = history.historyTrustworthyForFirstClaims
                && !string.IsNullOrWhiteSpace(destination.categoryToken)
                && !ContainsIgnoreCase(history.visitedCategoryKeys, destination.categoryToken);
            AddIf(result, OdysseyLandingReasonTokens.NewBiomeCategory, newCategory, policy);

            AddIf(result, OdysseyLandingReasonTokens.RoughLanding, journey.roughLanding, policy);

            long elapsed = Math.Max(0L, (long)observation.landingTick - journey.departureTick);
            AddIf(result, OdysseyLandingReasonTokens.LongJourney,
                !journey.baselineOnly
                && elapsed >= OdysseyJourneyPolicy.EffectiveLongMinimum(policy), policy);
            return result;
        }

        private static void AddIf(
            List<ReasonCandidate> target,
            string token,
            bool condition,
            OdysseyPolicySnapshot policy)
        {
            if (!condition || policy.reasonRules == null)
            {
                return;
            }

            for (int i = 0; i < policy.reasonRules.Count; i++)
            {
                OdysseyReasonRule rule = policy.reasonRules[i];
                if (rule != null && rule.reasonToken == token && OdysseyLandingReasonTokens.IsKnown(token))
                {
                    target.Add(new ReasonCandidate { rule = rule });
                    return;
                }
            }
        }

        private static int CompareReason(ReasonCandidate left, ReasonCandidate right)
        {
            int value = left.rule.priority.CompareTo(right.rule.priority);
            return value != 0
                ? value
                : string.Compare(left.rule.reasonToken, right.rule.reasonToken, StringComparison.Ordinal);
        }

        private static bool AnyCooldownBypass(List<ReasonCandidate> reasons)
        {
            for (int i = 0; i < reasons.Count; i++)
            {
                if (reasons[i].rule.bypassCooldown) return true;
            }

            return false;
        }

        private static OdysseyHistoryMutation BuildMutation(
            OdysseyJourneySnapshot journey,
            OdysseyLocationSnapshot destination,
            int landingTick)
        {
            OdysseyHistoryMutation mutation = new OdysseyHistoryMutation
            {
                landingObservationTick = landingTick,
                destinationObserved = destination != null && destination.visible,
                destinationHomeKey = destination != null && destination.visible && destination.isPlayerHome
                    ? Clean(destination.stableKey)
                    : string.Empty
            };
            AddNonBlank(mutation.visitedLayerKeys, destination.layerToken);
            AddNonBlank(mutation.visitedCategoryKeys, destination.categoryToken);
            AddNonBlank(mutation.visitedLocationKeys, destination.stableKey);
            if (destination.isPlayerHome)
            {
                AddNonBlank(mutation.homeKeys, destination.stableKey);
            }

            return mutation;
        }

        private static bool IsInCooldown(int previousTick, int currentTick, int cooldownTicks)
        {
            return previousTick >= 0
                && cooldownTicks > 0
                && (long)currentTick - previousTick < cooldownTicks;
        }

        private static bool ContainsOrdinal(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(Clean(values[i]), Clean(value), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddNonBlank(List<string> values, string value)
        {
            string cleaned = Clean(value);
            if (cleaned.Length > 0) values.Add(cleaned);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private sealed class ReasonCandidate
        {
            public OdysseyReasonRule rule;
        }
    }

    /// <summary>Bounds the existing launch-ritual route without owning a second journey event.</summary>
    internal static class OdysseyLaunchPolicy
    {
        /// <summary>
        /// Allows the first observed launch, a launch leaving a verified long-held home, or a launch
        /// after the prior emitted launch page has left the XML-owned cooldown.
        /// </summary>
        public static bool AllowsPage(
            int previousLaunchPageTick,
            int ritualTick,
            bool leavingLongHeldHome,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            if (!effective.enabled || ritualTick < 0) return false;
            if (leavingLongHeldHome || previousLaunchPageTick < 0 || effective.launchCooldownTicks <= 0)
                return true;
            return (long)ritualTick - previousLaunchPageTick >= effective.launchCooldownTicks;
        }

        /// <summary>Checks only saved home-tenure facts against one exact currently visible home.</summary>
        public static bool IsLeavingLongHeldHome(
            OdysseyTravelHistorySnapshot history,
            OdysseyLocationSnapshot currentLocation,
            int ritualTick,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            return history != null
                && currentLocation != null
                && currentLocation.visible
                && currentLocation.isPlayerHome
                && ritualTick >= 0
                && history.currentHomeSinceTick >= 0
                && history.lastLaunchPageTick < history.currentHomeSinceTick
                && !string.IsNullOrWhiteSpace(history.currentHomeKey)
                && string.Equals(
                    history.currentHomeKey.Trim(),
                    (currentLocation.stableKey ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase)
                && (long)ritualTick - history.currentHomeSinceTick
                    >= Math.Max(1, effective.longHeldHomeMinimumTicks);
        }
    }

    /// <summary>Applies landing mutations and old-save baselines to detached bounded history.</summary>
    internal static class OdysseyHistoryPolicy
    {
        /// <summary>
        /// Records one post-feature travel commit before landing. The origin becomes known history and
        /// future first/new claims become trustworthy; callers enforce journey-id idempotence.
        /// </summary>
        public static OdysseyTravelHistorySnapshot ApplyDeparture(
            OdysseyTravelHistorySnapshot source,
            OdysseyJourneySnapshot journey,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyTravelHistorySnapshot result = Normalize(source, effective);
            if (journey == null || journey.departureTick < 0
                || string.IsNullOrWhiteSpace(journey.journeyId)
                || string.IsNullOrWhiteSpace(journey.shipStableId))
            {
                return result;
            }

            result.historyInitialized = true;
            result.historyTrustworthyForFirstClaims = true;
            if (result.committedJourneyCount < int.MaxValue)
            {
                result.committedJourneyCount++;
            }
            result.lastDepartureTick = Math.Max(result.lastDepartureTick, journey.departureTick);
            OdysseyLocationSnapshot origin = OdysseyLocationPolicy.Classify(journey.origin, effective);
            AppendUnique(result.visitedLayerKeys, One(origin.layerToken), 8, true);
            AppendUnique(result.visitedCategoryKeys, One(origin.categoryToken),
                Positive(effective.maximumVisitedCategories, 64), true);
            AppendUnique(result.visitedLocationKeys, One(origin.stableKey),
                Positive(effective.maximumVisitedLocations, 128), true);
            if (origin.isPlayerHome)
            {
                AppendUnique(result.homeKeys, One(origin.stableKey),
                    Positive(effective.maximumHomeKeys, 32), true);
            }

            // Once travel commits, the ship is no longer occupying its prior surface home. A later
            // successful landing observation will establish a new tenure when appropriate.
            result.currentHomeKey = string.Empty;
            result.currentHomeSinceTick = -1;

            return result;
        }

        /// <summary>Records a launch cooldown only after at least one ritual DiaryEvent exists.</summary>
        public static OdysseyTravelHistorySnapshot MarkLaunchPage(
            OdysseyTravelHistorySnapshot source,
            int launchPageTick,
            OdysseyPolicySnapshot policy)
        {
            OdysseyTravelHistorySnapshot result = Normalize(source, policy);
            if (launchPageTick >= 0)
            {
                result.lastLaunchPageTick = Math.Max(result.lastLaunchPageTick, launchPageTick);
            }
            return result;
        }

        /// <summary>Applies one plan idempotently and keeps newest unique rows under defensive caps.</summary>
        public static OdysseyTravelHistorySnapshot Apply(
            OdysseyTravelHistorySnapshot source,
            OdysseyLandingPlan plan,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyTravelHistorySnapshot result = Normalize(source, effective);
            OdysseyHistoryMutation mutation = plan == null ? null : plan.historyMutation;
            if (mutation == null || mutation.landingObservationTick < 0)
            {
                return result;
            }

            result.historyInitialized = true;
            result.lastLandingObservationTick = Math.Max(
                result.lastLandingObservationTick,
                mutation.landingObservationTick);
            if (mutation.landingPageTick >= 0)
            {
                result.lastLandingPageTick = Math.Max(result.lastLandingPageTick, mutation.landingPageTick);
            }

            if (mutation.destinationObserved)
            {
                string destinationHomeKey = Clean(mutation.destinationHomeKey);
                if (destinationHomeKey.Length == 0)
                {
                    result.currentHomeKey = string.Empty;
                    result.currentHomeSinceTick = -1;
                }
                else if (!string.Equals(
                    result.currentHomeKey,
                    destinationHomeKey,
                    StringComparison.OrdinalIgnoreCase))
                {
                    result.currentHomeKey = destinationHomeKey;
                    result.currentHomeSinceTick = mutation.landingObservationTick;
                }
                else if (result.currentHomeSinceTick < 0)
                {
                    result.currentHomeSinceTick = mutation.landingObservationTick;
                }
            }

            AppendUnique(result.visitedLayerKeys, mutation.visitedLayerKeys, 8, true);
            AppendUnique(result.visitedCategoryKeys, mutation.visitedCategoryKeys,
                Positive(effective.maximumVisitedCategories, 64), true);
            AppendUnique(result.visitedLocationKeys, mutation.visitedLocationKeys,
                Positive(effective.maximumVisitedLocations, 128), true);
            AppendUnique(result.homeKeys, mutation.homeKeys,
                Positive(effective.maximumHomeKeys, 32), true);
            AppendUnique(result.emittedJourneyIds,
                One(mutation.journeyIdToMarkEmitted),
                Positive(effective.maximumEmittedJourneyIds, 128), false);
            return result;
        }

        /// <summary>
        /// Initializes a pre-feature save from visible facts without authorizing first/new claims or
        /// manufacturing departure/landing history. Repeated calls preserve an established history.
        /// </summary>
        public static OdysseyTravelHistorySnapshot BaselineOldSave(
            OdysseyTravelHistorySnapshot source,
            OdysseyLocationSnapshot visibleLocation,
            int featureStartTick,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyTravelHistorySnapshot result = Normalize(source, effective);
            if (result.historyInitialized)
            {
                return result;
            }

            result.historyInitialized = true;
            result.historyTrustworthyForFirstClaims = false;
            result.featureStartTick = Math.Max(0, featureStartTick);
            OdysseyLocationSnapshot location = OdysseyLocationPolicy.Classify(visibleLocation, effective);
            AppendUnique(result.visitedLayerKeys, One(location.layerToken), 8, true);
            AppendUnique(result.visitedCategoryKeys, One(location.categoryToken),
                Positive(effective.maximumVisitedCategories, 64), true);
            AppendUnique(result.visitedLocationKeys, One(location.stableKey),
                Positive(effective.maximumVisitedLocations, 128), true);
            if (location.isPlayerHome)
            {
                AppendUnique(result.homeKeys, One(location.stableKey),
                    Positive(effective.maximumHomeKeys, 32), true);
                result.currentHomeKey = Clean(location.stableKey);
                result.currentHomeSinceTick = Math.Max(0, featureStartTick);
            }

            return result;
        }

        /// <summary>Returns a non-null, de-duplicated snapshot without changing trust semantics.</summary>
        public static OdysseyTravelHistorySnapshot Normalize(
            OdysseyTravelHistorySnapshot source,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyTravelHistorySnapshot result = new OdysseyTravelHistorySnapshot();
            if (source != null)
            {
                result.schemaVersion = Math.Max(2, source.schemaVersion);
                result.historyInitialized = source.historyInitialized;
                result.historyTrustworthyForFirstClaims = source.historyTrustworthyForFirstClaims;
                result.featureStartTick = Math.Max(0, source.featureStartTick);
                result.committedJourneyCount = Math.Max(0, source.committedJourneyCount);
                result.lastDepartureTick = source.lastDepartureTick < -1 ? -1 : source.lastDepartureTick;
                result.lastLaunchPageTick = source.lastLaunchPageTick < -1 ? -1 : source.lastLaunchPageTick;
                result.lastLandingObservationTick = source.lastLandingObservationTick < -1
                    ? -1 : source.lastLandingObservationTick;
                result.lastLandingPageTick = source.lastLandingPageTick < -1
                    ? -1 : source.lastLandingPageTick;
                result.currentHomeKey = Clean(source.currentHomeKey);
                result.currentHomeSinceTick = source.currentHomeSinceTick < -1
                    ? -1
                    : source.currentHomeSinceTick;
                if (result.currentHomeKey.Length == 0)
                {
                    result.currentHomeSinceTick = -1;
                }
            }

            AppendUnique(result.visitedLayerKeys, source == null ? null : source.visitedLayerKeys, 8, true);
            AppendUnique(result.visitedCategoryKeys, source == null ? null : source.visitedCategoryKeys,
                Positive(effective.maximumVisitedCategories, 64), true);
            AppendUnique(result.visitedLocationKeys, source == null ? null : source.visitedLocationKeys,
                Positive(effective.maximumVisitedLocations, 128), true);
            AppendUnique(result.homeKeys, source == null ? null : source.homeKeys,
                Positive(effective.maximumHomeKeys, 32), true);
            AppendUnique(result.emittedJourneyIds, source == null ? null : source.emittedJourneyIds,
                Positive(effective.maximumEmittedJourneyIds, 128), false);
            return result;
        }

        private static void AppendUnique(
            List<string> target,
            List<string> values,
            int maximum,
            bool ignoreCase)
        {
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    string value = (values[i] ?? string.Empty).Trim();
                    if (value.Length == 0) continue;
                    Remove(target, value, ignoreCase);
                    target.Add(value);
                }
            }

            int overflow = target.Count - Math.Max(1, maximum);
            if (overflow > 0) target.RemoveRange(0, overflow);
        }

        private static void Remove(List<string> values, string value, bool ignoreCase)
        {
            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (string.Equals(values[i], value, comparison)) values.RemoveAt(i);
            }
        }

        private static List<string> One(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : new List<string> { value };
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
