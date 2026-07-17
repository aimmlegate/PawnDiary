// Verse-facing persistence for Odyssey O1 journey and travel-history state. The pure Pipeline DTOs
// remain assembly-free; these models add stable Scribe keys and conversion at the impure save edge.
// No live Pawn, Map, Def, tile, gravship, controller, or Harmony object is retained here.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>Scribe representation of one detached visible Odyssey location.</summary>
    internal sealed class OdysseyLocationState : IExposable
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

        public void ExposeData()
        {
            Scribe_Values.Look(ref stableKey, "stableKey");
            Scribe_Values.Look(ref visibleLabel, "visibleLabel");
            Scribe_Values.Look(ref layerToken, "layerToken", OdysseyLocationLayerTokens.Unknown);
            Scribe_Values.Look(ref biomeDefName, "biomeDefName");
            Scribe_Values.Look(ref biomeLabel, "biomeLabel");
            Scribe_Values.Look(ref siteDefName, "siteDefName");
            Scribe_Values.Look(ref siteLabel, "siteLabel");
            Scribe_Values.Look(ref categoryToken, "categoryToken");
            Scribe_Values.Look(ref majorDestination, "majorDestination", false);
            Scribe_Values.Look(ref isPlayerHome, "isPlayerHome", false);
            Scribe_Values.Look(ref visible, "visible", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) EnsureStrings();
        }

        internal OdysseyLocationSnapshot ToSnapshot()
        {
            EnsureStrings();
            return new OdysseyLocationSnapshot
            {
                stableKey = stableKey,
                visibleLabel = visibleLabel,
                layerToken = layerToken,
                biomeDefName = biomeDefName,
                biomeLabel = biomeLabel,
                siteDefName = siteDefName,
                siteLabel = siteLabel,
                categoryToken = categoryToken,
                majorDestination = majorDestination,
                isPlayerHome = isPlayerHome,
                visible = visible
            };
        }

        internal static OdysseyLocationState FromSnapshot(OdysseyLocationSnapshot source)
        {
            if (source == null) return null;
            return new OdysseyLocationState
            {
                stableKey = source.stableKey,
                visibleLabel = source.visibleLabel,
                layerToken = source.layerToken,
                biomeDefName = source.biomeDefName,
                biomeLabel = source.biomeLabel,
                siteDefName = source.siteDefName,
                siteLabel = source.siteLabel,
                categoryToken = source.categoryToken,
                majorDestination = source.majorDestination,
                isPlayerHome = source.isPlayerHome,
                visible = source.visible
            };
        }

        private void EnsureStrings()
        {
            stableKey = stableKey ?? string.Empty;
            visibleLabel = visibleLabel ?? string.Empty;
            layerToken = layerToken ?? OdysseyLocationLayerTokens.Unknown;
            biomeDefName = biomeDefName ?? string.Empty;
            biomeLabel = biomeLabel ?? string.Empty;
            siteDefName = siteDefName ?? string.Empty;
            siteLabel = siteLabel ?? string.Empty;
            categoryToken = categoryToken ?? string.Empty;
        }
    }

    /// <summary>Scribe representation of one event-time pilot/copilot/crew candidate.</summary>
    internal sealed class OdysseyWriterState : IExposable
    {
        public string pawnId = string.Empty;
        public string displayName = string.Empty;
        public string roleToken = string.Empty;
        public bool eligible;
        public bool present;
        public bool hasDiary;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref displayName, "displayName");
            Scribe_Values.Look(ref roleToken, "roleToken");
            Scribe_Values.Look(ref eligible, "eligible", false);
            Scribe_Values.Look(ref present, "present", false);
            Scribe_Values.Look(ref hasDiary, "hasDiary", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                displayName = displayName ?? string.Empty;
                roleToken = roleToken ?? string.Empty;
            }
        }

        internal OdysseyWriterCandidate ToSnapshot()
        {
            return new OdysseyWriterCandidate
            {
                pawnId = pawnId ?? string.Empty,
                displayName = displayName ?? string.Empty,
                roleToken = roleToken ?? string.Empty,
                eligible = eligible,
                present = present,
                hasDiary = hasDiary
            };
        }

        internal static OdysseyWriterState FromSnapshot(OdysseyWriterCandidate source)
        {
            if (source == null) return null;
            return new OdysseyWriterState
            {
                pawnId = source.pawnId,
                displayName = source.displayName,
                roleToken = source.roleToken,
                eligible = source.eligible,
                present = source.present,
                hasDiary = source.hasDiary
            };
        }
    }

    /// <summary>Saved active gravship journey; vanilla exposes one active controller lifecycle.</summary>
    internal sealed class OdysseyJourneyState : IExposable
    {
        public int schemaVersion = 1;
        public string journeyId = string.Empty;
        public string shipStableId = string.Empty;
        public string shipName = string.Empty;
        public OdysseyLocationState origin;
        public OdysseyLocationState destination;
        public int departureTick;
        public string launchQualityBand = OdysseyLaunchQualityTokens.Unknown;
        public bool roughLanding;
        public bool sourceComplete;
        public bool baselineOnly;
        public bool landingApplied;
        public List<OdysseyWriterState> writers = new List<OdysseyWriterState>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1);
            Scribe_Values.Look(ref journeyId, "journeyId");
            Scribe_Values.Look(ref shipStableId, "shipStableId");
            Scribe_Values.Look(ref shipName, "shipName");
            Scribe_Deep.Look(ref origin, "origin");
            Scribe_Deep.Look(ref destination, "destination");
            Scribe_Values.Look(ref departureTick, "departureTick", 0);
            Scribe_Values.Look(ref launchQualityBand, "launchQualityBand", OdysseyLaunchQualityTokens.Unknown);
            Scribe_Values.Look(ref roughLanding, "roughLanding", false);
            Scribe_Values.Look(ref sourceComplete, "sourceComplete", false);
            Scribe_Values.Look(ref baselineOnly, "baselineOnly", false);
            Scribe_Values.Look(ref landingApplied, "landingApplied", false);
            Scribe_Collections.Look(ref writers, "writers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                journeyId = journeyId ?? string.Empty;
                shipStableId = shipStableId ?? string.Empty;
                shipName = shipName ?? string.Empty;
                launchQualityBand = launchQualityBand ?? OdysseyLaunchQualityTokens.Unknown;
                writers = writers ?? new List<OdysseyWriterState>();
            }
        }

        internal OdysseyJourneySnapshot ToSnapshot()
        {
            List<OdysseyWriterCandidate> copiedWriters = new List<OdysseyWriterCandidate>();
            if (writers != null)
            {
                for (int i = 0; i < writers.Count; i++)
                {
                    if (writers[i] != null) copiedWriters.Add(writers[i].ToSnapshot());
                }
            }

            return new OdysseyJourneySnapshot
            {
                schemaVersion = schemaVersion,
                journeyId = journeyId ?? string.Empty,
                shipStableId = shipStableId ?? string.Empty,
                shipName = shipName ?? string.Empty,
                origin = origin?.ToSnapshot(),
                destination = destination?.ToSnapshot(),
                departureTick = departureTick,
                launchQualityBand = launchQualityBand ?? OdysseyLaunchQualityTokens.Unknown,
                roughLanding = roughLanding,
                sourceComplete = sourceComplete,
                baselineOnly = baselineOnly,
                landingApplied = landingApplied,
                writers = copiedWriters
            };
        }

        internal static OdysseyJourneyState FromSnapshot(OdysseyJourneySnapshot source)
        {
            if (source == null) return null;
            OdysseyJourneyState result = new OdysseyJourneyState
            {
                schemaVersion = source.schemaVersion,
                journeyId = source.journeyId,
                shipStableId = source.shipStableId,
                shipName = source.shipName,
                origin = OdysseyLocationState.FromSnapshot(source.origin),
                destination = OdysseyLocationState.FromSnapshot(source.destination),
                departureTick = source.departureTick,
                launchQualityBand = source.launchQualityBand,
                roughLanding = source.roughLanding,
                sourceComplete = source.sourceComplete,
                baselineOnly = source.baselineOnly,
                landingApplied = source.landingApplied
            };
            if (source.writers != null)
            {
                for (int i = 0; i < source.writers.Count; i++)
                {
                    OdysseyWriterState writer = OdysseyWriterState.FromSnapshot(source.writers[i]);
                    if (writer != null) result.writers.Add(writer);
                }
            }
            return result;
        }
    }

    /// <summary>Saved bounded novelty, home, cooldown, and emitted-journey history.</summary>
    internal sealed class OdysseyTravelHistoryState : IExposable
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

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 2);
            Scribe_Values.Look(ref historyInitialized, "historyInitialized", false);
            Scribe_Values.Look(ref historyTrustworthyForFirstClaims, "historyTrustworthyForFirstClaims", false);
            Scribe_Values.Look(ref featureStartTick, "featureStartTick", 0);
            Scribe_Values.Look(ref committedJourneyCount, "committedJourneyCount", 0);
            Scribe_Values.Look(ref lastDepartureTick, "lastDepartureTick", -1);
            Scribe_Values.Look(ref lastLaunchPageTick, "lastLaunchPageTick", -1);
            Scribe_Values.Look(ref lastLandingObservationTick, "lastLandingObservationTick", -1);
            Scribe_Values.Look(ref lastLandingPageTick, "lastLandingPageTick", -1);
            Scribe_Values.Look(ref currentHomeKey, "currentHomeKey");
            Scribe_Values.Look(ref currentHomeSinceTick, "currentHomeSinceTick", -1);
            Scribe_Collections.Look(ref visitedLayerKeys, "visitedLayerKeys", LookMode.Value);
            Scribe_Collections.Look(ref visitedCategoryKeys, "visitedCategoryKeys", LookMode.Value);
            Scribe_Collections.Look(ref visitedLocationKeys, "visitedLocationKeys", LookMode.Value);
            Scribe_Collections.Look(ref homeKeys, "homeKeys", LookMode.Value);
            Scribe_Collections.Look(ref emittedJourneyIds, "emittedJourneyIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) EnsureLists();
        }

        internal OdysseyTravelHistorySnapshot ToSnapshot()
        {
            EnsureLists();
            return new OdysseyTravelHistorySnapshot
            {
                schemaVersion = schemaVersion,
                historyInitialized = historyInitialized,
                historyTrustworthyForFirstClaims = historyTrustworthyForFirstClaims,
                featureStartTick = featureStartTick,
                committedJourneyCount = committedJourneyCount,
                lastDepartureTick = lastDepartureTick,
                lastLaunchPageTick = lastLaunchPageTick,
                lastLandingObservationTick = lastLandingObservationTick,
                lastLandingPageTick = lastLandingPageTick,
                currentHomeKey = currentHomeKey ?? string.Empty,
                currentHomeSinceTick = currentHomeSinceTick,
                visitedLayerKeys = new List<string>(visitedLayerKeys),
                visitedCategoryKeys = new List<string>(visitedCategoryKeys),
                visitedLocationKeys = new List<string>(visitedLocationKeys),
                homeKeys = new List<string>(homeKeys),
                emittedJourneyIds = new List<string>(emittedJourneyIds)
            };
        }

        internal static OdysseyTravelHistoryState FromSnapshot(OdysseyTravelHistorySnapshot source)
        {
            if (source == null) return new OdysseyTravelHistoryState();
            return new OdysseyTravelHistoryState
            {
                schemaVersion = source.schemaVersion,
                historyInitialized = source.historyInitialized,
                historyTrustworthyForFirstClaims = source.historyTrustworthyForFirstClaims,
                featureStartTick = source.featureStartTick,
                committedJourneyCount = source.committedJourneyCount,
                lastDepartureTick = source.lastDepartureTick,
                lastLaunchPageTick = source.lastLaunchPageTick,
                lastLandingObservationTick = source.lastLandingObservationTick,
                lastLandingPageTick = source.lastLandingPageTick,
                currentHomeKey = source.currentHomeKey ?? string.Empty,
                currentHomeSinceTick = source.currentHomeSinceTick,
                visitedLayerKeys = source.visitedLayerKeys == null
                    ? new List<string>() : new List<string>(source.visitedLayerKeys),
                visitedCategoryKeys = source.visitedCategoryKeys == null
                    ? new List<string>() : new List<string>(source.visitedCategoryKeys),
                visitedLocationKeys = source.visitedLocationKeys == null
                    ? new List<string>() : new List<string>(source.visitedLocationKeys),
                homeKeys = source.homeKeys == null
                    ? new List<string>() : new List<string>(source.homeKeys),
                emittedJourneyIds = source.emittedJourneyIds == null
                    ? new List<string>() : new List<string>(source.emittedJourneyIds)
            };
        }

        private void EnsureLists()
        {
            currentHomeKey = currentHomeKey ?? string.Empty;
            visitedLayerKeys = visitedLayerKeys ?? new List<string>();
            visitedCategoryKeys = visitedCategoryKeys ?? new List<string>();
            visitedLocationKeys = visitedLocationKeys ?? new List<string>();
            homeKeys = homeKeys ?? new List<string>();
            emittedJourneyIds = emittedJourneyIds ?? new List<string>();
        }
    }

    /// <summary>Defensive conversion and normalization shared by the component and RimTests.</summary>
    internal static class OdysseyStatePersistence
    {
        private const int HardMaximumRows = 2048;
        private const int HardMaximumIdentityCharacters = 200;

        internal static OdysseyJourneyState NormalizeJourney(
            OdysseyJourneyState source,
            OdysseyPolicySnapshot policy)
        {
            if (source == null) return null;
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyJourneySnapshot snapshot = source.ToSnapshot();
            snapshot.schemaVersion = Math.Max(1, snapshot.schemaVersion);
            snapshot.shipStableId = CleanStableIdentity(snapshot.shipStableId);
            snapshot.journeyId = CleanIdentity(snapshot.journeyId);
            snapshot.shipName = CleanLabel(snapshot.shipName, effective);
            snapshot.departureTick = Math.Max(0, snapshot.departureTick);
            snapshot.launchQualityBand = NormalizeQuality(snapshot.launchQualityBand);
            snapshot.origin = NormalizeLocation(snapshot.origin, effective);
            snapshot.destination = NormalizeLocation(snapshot.destination, effective);
            snapshot.writers = NormalizeWriters(snapshot.writers, effective);
            if (snapshot.shipStableId.Length == 0 || snapshot.journeyId.Length == 0)
            {
                return null;
            }

            return OdysseyJourneyState.FromSnapshot(snapshot);
        }

        internal static OdysseyTravelHistoryState NormalizeHistory(
            OdysseyTravelHistoryState source,
            OdysseyPolicySnapshot policy)
        {
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyTravelHistorySnapshot input = source == null
                ? new OdysseyTravelHistorySnapshot()
                : source.ToSnapshot();
            TrimNewest(input.visitedLayerKeys, HardMaximumRows);
            TrimNewest(input.visitedCategoryKeys, HardMaximumRows);
            TrimNewest(input.visitedLocationKeys, HardMaximumRows);
            TrimNewest(input.homeKeys, HardMaximumRows);
            TrimNewest(input.emittedJourneyIds, HardMaximumRows);
            OdysseyTravelHistorySnapshot normalized = OdysseyHistoryPolicy.Normalize(input, effective);
            return OdysseyTravelHistoryState.FromSnapshot(normalized);
        }

        internal static OdysseyLocationSnapshot NormalizeLocation(
            OdysseyLocationSnapshot source,
            OdysseyPolicySnapshot policy)
        {
            if (source == null) return null;
            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            OdysseyLocationSnapshot cleaned = new OdysseyLocationSnapshot
            {
                stableKey = CleanIdentity(source.stableKey),
                visibleLabel = CleanLabel(source.visibleLabel, effective),
                layerToken = OdysseyLocationLayerTokens.Normalize(source.layerToken),
                biomeDefName = CleanIdentity(source.biomeDefName),
                biomeLabel = CleanLabel(source.biomeLabel, effective),
                siteDefName = CleanIdentity(source.siteDefName),
                siteLabel = CleanLabel(source.siteLabel, effective),
                categoryToken = CleanIdentity(source.categoryToken),
                majorDestination = source.majorDestination,
                isPlayerHome = source.isPlayerHome,
                visible = source.visible
            };
            return OdysseyLocationPolicy.Classify(cleaned, effective);
        }

        private static List<OdysseyWriterCandidate> NormalizeWriters(
            List<OdysseyWriterCandidate> source,
            OdysseyPolicySnapshot policy)
        {
            List<OdysseyWriterCandidate> candidates = new List<OdysseyWriterCandidate>();
            if (source != null)
            {
                for (int i = 0; i < source.Count && candidates.Count < HardMaximumRows; i++)
                {
                    OdysseyWriterCandidate row = source[i];
                    string pawnId = CleanIdentity(row?.pawnId);
                    if (pawnId.Length == 0 || OdysseyJourneyRoleTokens.Rank(row.roleToken) == int.MaxValue)
                        continue;
                    candidates.Add(new OdysseyWriterCandidate
                    {
                        pawnId = pawnId,
                        displayName = CleanLabel(row.displayName, policy),
                        roleToken = row.roleToken,
                        eligible = row.eligible,
                        present = row.present,
                        hasDiary = row.hasDiary
                    });
                }
            }

            return OdysseyWriterPolicy.Select(candidates, policy.maximumLandingWriters);
        }

        private static string NormalizeQuality(string value)
        {
            if (value == OdysseyLaunchQualityTokens.Poor
                || value == OdysseyLaunchQualityTokens.Ordinary
                || value == OdysseyLaunchQualityTokens.Excellent)
                return value;
            return OdysseyLaunchQualityTokens.Unknown;
        }

        private static string CleanIdentity(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            if (cleaned.IndexOf(';') >= 0 || cleaned.IndexOf('=') >= 0) return string.Empty;
            return cleaned.Length <= HardMaximumIdentityCharacters
                ? cleaned
                : cleaned.Substring(0, HardMaximumIdentityCharacters);
        }

        private static string CleanStableIdentity(string value)
        {
            string raw = (value ?? string.Empty).Trim();
            return raw.IndexOf('|') >= 0 ? string.Empty : CleanIdentity(raw);
        }

        private static string CleanLabel(string value, OdysseyPolicySnapshot policy)
        {
            int maximum = policy.maximumContextValueCharacters > 0
                ? Math.Min(1000, policy.maximumContextValueCharacters)
                : 120;
            return OdysseyContextFormatter.CleanValue(value, maximum);
        }

        private static void TrimNewest(List<string> values, int maximum)
        {
            if (values == null || values.Count <= maximum) return;
            values.RemoveRange(0, values.Count - maximum);
        }
    }
}
