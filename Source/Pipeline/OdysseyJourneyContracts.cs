// Plain, DLC-free contracts and frozen schema tokens for Odyssey gravship journeys. Runtime adapters
// will copy live RimWorld maps, pawns, tiles, and gravships into these DTOs before calling the pure
// policies. Keeping this file free of Verse/Unity/Harmony makes O1 decisions testable without the game.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable catalog Def names introduced by the Odyssey landing route.</summary>
    internal static class OdysseyEventDefNames
    {
        public const string Landing = "OdysseyGravshipLanding";
    }

    /// <summary>Stable XML group keys shared by the launch adapter and landing owner.</summary>
    internal static class OdysseyGroupDefNames
    {
        public const string Launch = "ritualGravship";
        public const string Landing = "odysseyGravshipLanding";
    }

    /// <summary>Additive component save keys frozen before Odyssey persistence is introduced.</summary>
    internal static class OdysseySaveKeys
    {
        public const string ActiveJourney = "odysseyActiveJourney";
        public const string TravelHistory = "odysseyTravelHistory";
    }

    /// <summary>Stable IDs shared by save ownership, deduplication, and Narrative Continuity.</summary>
    internal static class OdysseyArcKeys
    {
        private const string JourneyPrefix = "odyssey-journey|";
        private const string LandingPrefix = "odyssey-landing|";

        /// <summary>Builds the one committed journey ID; the same value is its narrative arc key.</summary>
        public static string Journey(string shipStableId, int departureTick)
        {
            string ship = CleanId(shipStableId);
            return ship.Length == 0 || departureTick < 0
                ? string.Empty
                : JourneyPrefix + ship + "|" + departureTick;
        }

        /// <summary>Builds the landing owner key from the same identity used by the journey.</summary>
        public static string Landing(string shipStableId, int departureTick)
        {
            string ship = CleanId(shipStableId);
            return ship.Length == 0 || departureTick < 0
                ? string.Empty
                : LandingPrefix + ship + "|" + departureTick;
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }
    }

    /// <summary>Source-owned Narrative Continuity phases for a gravship journey.</summary>
    internal static class OdysseyJourneyPhaseTokens
    {
        public const string Departed = "departed";
        public const string Arrived = "arrived";
        public const string Returned = "returned";
    }

    /// <summary>Exact reasons that may authorize one Odyssey landing chapter.</summary>
    internal static class OdysseyLandingReasonTokens
    {
        public const string FirstOrbit = "first_orbit";
        public const string NewBiomeCategory = "new_biome_category";
        public const string MajorDestination = "major_destination";
        public const string Homecoming = "homecoming";
        public const string LongJourney = "long_journey";
        public const string RoughLanding = "rough_landing";

        /// <summary>Returns true only for a reason whose semantics are frozen by O1.0.</summary>
        public static bool IsKnown(string value)
        {
            return value == FirstOrbit || value == NewBiomeCategory || value == MajorDestination
                || value == Homecoming || value == LongJourney || value == RoughLanding;
        }
    }

    /// <summary>Deterministic first-person roles for at most two landing writers.</summary>
    internal static class OdysseyJourneyRoleTokens
    {
        public const string Pilot = "pilot";
        public const string Copilot = "copilot";
        public const string Crew = "crew";

        /// <summary>Lower ranks win writer selection; unknown roles are ineligible.</summary>
        public static int Rank(string value)
        {
            if (value == Pilot) return 0;
            if (value == Copilot) return 1;
            if (value == Crew) return 2;
            return int.MaxValue;
        }
    }

    /// <summary>Stable destination-layer tokens; raw tile/layer IDs never enter prompts.</summary>
    internal static class OdysseyLocationLayerTokens
    {
        public const string Surface = "surface";
        public const string Orbit = "orbit";
        public const string Unknown = "unknown";

        /// <summary>Normalizes live adapter input to the frozen three-value schema.</summary>
        public static string Normalize(string value)
        {
            if (string.Equals(value, Surface, StringComparison.OrdinalIgnoreCase)) return Surface;
            if (string.Equals(value, Orbit, StringComparison.OrdinalIgnoreCase)) return Orbit;
            return Unknown;
        }
    }

    /// <summary>Qualitative journey-duration bands selected from XML-owned thresholds.</summary>
    internal static class OdysseyDurationTokens
    {
        public const string Short = "short";
        public const string Ordinary = "ordinary";
        public const string Long = "long";
    }

    /// <summary>Qualitative launch-quality bands; numeric ritual quality stays out of prompts.</summary>
    internal static class OdysseyLaunchQualityTokens
    {
        public const string Poor = "poor";
        public const string Ordinary = "ordinary";
        public const string Excellent = "excellent";
        public const string Unknown = "unknown";
    }

    /// <summary>One exact Def-name-to-visible-category mapping copied from later XML policy.</summary>
    internal sealed class OdysseyLocationCategoryRule
    {
        public string defName = string.Empty;
        public string categoryToken = string.Empty;
        public bool majorDestination;
    }

    /// <summary>One XML-shaped landing-reason priority and cooldown exception.</summary>
    internal sealed class OdysseyReasonRule
    {
        public string reasonToken = string.Empty;
        public int priority;
        public bool bypassCooldown;
        public bool important;
    }

    /// <summary>
    /// Pure policy snapshot consumed by O1.1. O1.2 will copy a live DiaryOdysseyPolicyDef into this
    /// shape; these defaults keep missing or partial XML fail-open and deterministic.
    /// </summary>
    internal sealed class OdysseyPolicySnapshot
    {
        public bool enabled = true;
        public bool landingPageEnabled;
        public string packageId = "Ludeon.RimWorld.Odyssey";
        public string launchGroupKey = OdysseyGroupDefNames.Launch;
        public string landingGroupKey = OdysseyGroupDefNames.Landing;
        // Provider prose is DefInjected at runtime. Empty is the safe no-Def fallback: omit only
        // that optional lens while keeping the source-owned event and its evidence/reference.
        public string mobileHomeNarrativeFormat = string.Empty;
        public string seasonalFloodNarrativeFormat = string.Empty;
        public int takeoffCorrelationTicks = 2500;
        public int landingCorrelationTicks = 2500;
        public int staleJourneyRetentionTicks = 3600000;
        public int launchCooldownTicks = 60000;
        public int landingCooldownTicks = 60000;
        public int shortJourneyMaximumTicks = 15000;
        public int longJourneyMinimumTicks = 60000;
        public int longHeldHomeMinimumTicks = 900000;
        public int maximumLaunchWriters = 2;
        public int maximumLandingWriters = 2;
        public int maximumVisitedLocations = 128;
        public int maximumVisitedCategories = 64;
        public int maximumHomeKeys = 32;
        public int maximumEmittedJourneyIds = 128;
        public int maximumContextCharacters = 900;
        public int maximumContextValueCharacters = 120;
        public float poorLaunchQualityMaximum = 0.35f;
        public float excellentLaunchQualityMinimum = 0.75f;
        public List<OdysseyLocationCategoryRule> biomeCategories =
            new List<OdysseyLocationCategoryRule>();
        public List<OdysseyLocationCategoryRule> siteCategories =
            new List<OdysseyLocationCategoryRule>();
        public List<OdysseyReasonRule> reasonRules = new List<OdysseyReasonRule>();

        /// <summary>Creates the safe code fallback, including the frozen default reason ordering.</summary>
        public static OdysseyPolicySnapshot CreateDefault()
        {
            OdysseyPolicySnapshot snapshot = new OdysseyPolicySnapshot();
            snapshot.reasonRules.Add(new OdysseyReasonRule
            {
                reasonToken = OdysseyLandingReasonTokens.MajorDestination,
                priority = 0,
                bypassCooldown = true,
                important = true
            });
            snapshot.reasonRules.Add(new OdysseyReasonRule
            {
                reasonToken = OdysseyLandingReasonTokens.Homecoming,
                priority = 10,
                bypassCooldown = true,
                important = true
            });
            snapshot.reasonRules.Add(new OdysseyReasonRule
            {
                reasonToken = OdysseyLandingReasonTokens.FirstOrbit,
                priority = 20,
                important = true
            });
            snapshot.reasonRules.Add(new OdysseyReasonRule
            {
                reasonToken = OdysseyLandingReasonTokens.NewBiomeCategory,
                priority = 30
            });
            snapshot.reasonRules.Add(new OdysseyReasonRule
            {
                reasonToken = OdysseyLandingReasonTokens.RoughLanding,
                priority = 40,
                bypassCooldown = true,
                important = true
            });
            snapshot.reasonRules.Add(new OdysseyReasonRule
            {
                reasonToken = OdysseyLandingReasonTokens.LongJourney,
                priority = 50
            });
            return snapshot;
        }
    }

    /// <summary>Visible, event-time location facts copied from a live map/world destination.</summary>
    internal sealed class OdysseyLocationSnapshot
    {
        public string stableKey = string.Empty;
        public string visibleLabel = string.Empty;
        public string layerToken = OdysseyLocationLayerTokens.Unknown;
        public string biomeDefName = string.Empty;
        public string biomeLabel = string.Empty;
        public string siteDefName = string.Empty;
        public string siteLabel = string.Empty;
        public string categoryToken = string.Empty;
        public bool majorDestination;
        public bool isPlayerHome;
        public bool visible = true;
    }

    /// <summary>Detached truth that a specific pawn is presently aboard one player gravship.</summary>
    internal sealed class OdysseyMobileHomeSnapshot
    {
        public string shipStableId = string.Empty;
        public string shipName = string.Empty;
        public OdysseyLocationSnapshot location;
    }

    /// <summary>One exact pilot/copilot/crew candidate captured for deterministic selection.</summary>
    internal sealed class OdysseyWriterCandidate
    {
        public string pawnId = string.Empty;
        public string displayName = string.Empty;
        public string roleToken = string.Empty;
        public bool eligible;
        public bool present;
        public bool hasDiary;
    }

    /// <summary>Transient pre-commit takeoff facts; O1.3 will own their runtime lifetime.</summary>
    internal sealed class OdysseyTakeoffIntent
    {
        public string engineId = string.Empty;
        public string shipName = string.Empty;
        public OdysseyLocationSnapshot origin;
        public string selectedTargetKey = string.Empty;
        public int captureTick;
        public string launchQualityBand = OdysseyLaunchQualityTokens.Unknown;
        public bool roughLanding;
        public List<OdysseyWriterCandidate> writers = new List<OdysseyWriterCandidate>();
    }

    /// <summary>Detached facts observed after vanilla commits a gravship to world travel.</summary>
    internal sealed class OdysseyTravelCommitObservation
    {
        public string shipStableId = string.Empty;
        public string engineId = string.Empty;
        public string shipName = string.Empty;
        public OdysseyLocationSnapshot origin;
        public OdysseyLocationSnapshot destination;
        public int departureTick;
        public string launchQualityBand = OdysseyLaunchQualityTokens.Unknown;
        public bool roughLanding;
        public List<OdysseyWriterCandidate> writers = new List<OdysseyWriterCandidate>();
    }

    /// <summary>Transient detached landing facts retained only across the vanilla landing cutscene.</summary>
    internal sealed class OdysseyPendingLanding
    {
        public string shipStableId = string.Empty;
        public OdysseyLocationSnapshot destination;
        public int captureTick;
        public string landingOutcomeDefName = string.Empty;
        public string landingOutcomeLabel = string.Empty;
        public List<OdysseyWriterCandidate> writers = new List<OdysseyWriterCandidate>();
    }

    /// <summary>Committed, live-reference-free journey facts suitable for later Scribe persistence.</summary>
    internal sealed class OdysseyJourneySnapshot
    {
        public int schemaVersion = 1;
        public string journeyId = string.Empty;
        public string shipStableId = string.Empty;
        public string shipName = string.Empty;
        public OdysseyLocationSnapshot origin;
        public OdysseyLocationSnapshot destination;
        public int departureTick;
        public string launchQualityBand = OdysseyLaunchQualityTokens.Unknown;
        public bool roughLanding;
        public string landingOutcomeDefName = string.Empty;
        public string landingOutcomeLabel = string.Empty;
        public bool sourceComplete;
        public bool baselineOnly;
        public bool landingApplied;
        public List<OdysseyWriterCandidate> writers = new List<OdysseyWriterCandidate>();
    }

    /// <summary>Successful-finish input assembled before pure landing planning.</summary>
    internal sealed class OdysseyLandingObservation
    {
        public OdysseyJourneySnapshot journey;
        public OdysseyLocationSnapshot destination;
        public int landingTick;
        public bool groupEnabled = true;
        public List<OdysseyWriterCandidate> writers = new List<OdysseyWriterCandidate>();
    }

    /// <summary>Detached saved novelty/history input; lists are normalized by pure policy.</summary>
    internal sealed class OdysseyTravelHistorySnapshot
    {
        public int schemaVersion = 2;
        public bool historyInitialized;
        public bool historyTrustworthyForFirstClaims;
        public int featureStartTick;
        public int committedJourneyCount;
        public int lastDepartureTick = -1;
        public int lastLaunchPageTick = -1;
        public int lastLandingObservationTick = -1;
        public int lastLandingPageTick = -1;
        public string currentHomeKey = string.Empty;
        public int currentHomeSinceTick = -1;
        public List<string> visitedLayerKeys = new List<string>();
        public List<string> visitedCategoryKeys = new List<string>();
        public List<string> visitedLocationKeys = new List<string>();
        public List<string> homeKeys = new List<string>();
        public List<string> emittedJourneyIds = new List<string>();
    }

    /// <summary>Exact history changes returned even when page generation is disabled or dropped.</summary>
    internal sealed class OdysseyHistoryMutation
    {
        public int landingObservationTick = -1;
        public int landingPageTick = -1;
        public bool destinationObserved;
        public string destinationHomeKey = string.Empty;
        public string journeyIdToMarkEmitted = string.Empty;
        public List<string> visitedLayerKeys = new List<string>();
        public List<string> visitedCategoryKeys = new List<string>();
        public List<string> visitedLocationKeys = new List<string>();
        public List<string> homeKeys = new List<string>();
    }

    /// <summary>Pure result for one successful landing observation.</summary>
    internal sealed class OdysseyLandingPlan
    {
        public bool writePage;
        public bool important;
        public bool droppedByCooldown;
        public string primaryReason = string.Empty;
        public string secondaryReason = string.Empty;
        public string durationBand = OdysseyDurationTokens.Ordinary;
        public List<OdysseyWriterCandidate> selectedWriters = new List<OdysseyWriterCandidate>();
        public OdysseyHistoryMutation historyMutation = new OdysseyHistoryMutation();
    }
}
