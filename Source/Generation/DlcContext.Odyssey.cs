// Odyssey-specific guarded live reads. All RimWorld Maps, tiles, Sites, grav engines, and world
// gravships are converted here into detached snapshots before pure policy or saved state sees them.
// The entire file is inert without Odyssey: every public entry point checks ModsConfig.OdysseyActive.
using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    internal static partial class DlcContext
    {
        /// <summary>Copies one visible live map location for pure Odyssey classification.</summary>
        internal static bool TryCaptureOdysseyLocation(
            Map map,
            out OdysseyLocationSnapshot snapshot)
        {
            snapshot = null;
            if (!ModsConfig.OdysseyActive || map == null)
            {
                return false;
            }

            try
            {
                snapshot = CaptureLocation(
                    map.Tile,
                    map.Biome,
                    map.Parent,
                    map.IsPlayerHome);
                return snapshot != null;
            }
            catch (Exception)
            {
                // Surroundings are optional prompt context. A transient map/world-object teardown
                // must omit this fragment instead of aborting the owning diary event.
                snapshot = null;
                return false;
            }
        }

        /// <summary>Copies the spawned pawn's visible map location, with the same no-DLC guard.</summary>
        internal static bool TryCaptureOdysseyLocation(
            Pawn pawn,
            out OdysseyLocationSnapshot snapshot)
        {
            snapshot = null;
            return pawn != null && pawn.Spawned
                && TryCaptureOdysseyLocation(pawn.Map, out snapshot);
        }

        /// <summary>
        /// Returns mobile-home context only when this exact spawned pawn is inside the player grav
        /// field on this exact map. Merely sharing a map that contains an engine is not sufficient.
        /// </summary>
        internal static bool TryCaptureOdysseyMobileHome(
            Pawn pawn,
            out OdysseyMobileHomeSnapshot snapshot)
        {
            snapshot = null;
            if (!ModsConfig.OdysseyActive || pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            try
            {
                Building_GravEngine engine = GravshipUtility.GetPlayerGravEngine_NewTemp(pawn.Map);
                if (engine == null
                    || !GravshipUtility.IsOnboardGravship_NewTemp(
                        pawn.Position,
                        engine,
                        pawn,
                        desperate: false,
                        respectAllowedAreas: false))
                {
                    return false;
                }

                OdysseyLocationSnapshot location;
                if (!TryCaptureOdysseyLocation(pawn.Map, out location))
                {
                    return false;
                }

                string shipName;
                if (!GravshipUtility.TryGetNameOfGravshipOnMap(pawn.Map, out shipName)
                    || string.IsNullOrWhiteSpace(shipName))
                {
                    shipName = engine.LabelShortCap;
                }

                snapshot = new OdysseyMobileHomeSnapshot
                {
                    shipStableId = CleanStableId(engine.GetUniqueLoadID()),
                    shipName = CleanLabel(shipName),
                    location = location
                };
                return snapshot.shipStableId.Length > 0 && snapshot.shipName.Length > 0;
            }
            catch (Exception)
            {
                snapshot = null;
                return false;
            }
        }

        /// <summary>
        /// Captures the currently travelling vanilla gravship for an old-save baseline. This never
        /// invents a completed departure; the returned journey is explicitly incomplete/baseline-only.
        /// </summary>
        internal static bool TryCaptureOdysseyTravellingBaseline(
            int featureStartTick,
            out OdysseyJourneySnapshot journey,
            out OdysseyLocationSnapshot location)
        {
            journey = null;
            location = null;
            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            try
            {
                Gravship gravship = Verse.Current.Game?.Gravship;
                if (gravship == null)
                {
                    return false;
                }

                string shipId = CleanStableId(gravship.GetUniqueLoadID());
                int tick = Math.Max(0, featureStartTick);
                if (shipId.Length == 0)
                {
                    return false;
                }

                location = CaptureLocation(gravship.Tile, gravship.Biome, null, false);
                journey = new OdysseyJourneySnapshot
                {
                    journeyId = OdysseyArcKeys.Journey(shipId, tick),
                    shipStableId = shipId,
                    shipName = CleanLabel(gravship.LabelCap),
                    destination = location,
                    departureTick = tick,
                    launchQualityBand = OdysseyLaunchQualityTokens.Unknown,
                    sourceComplete = false,
                    baselineOnly = true,
                    landingApplied = false
                };
                return journey.journeyId.Length > 0;
            }
            catch (Exception)
            {
                journey = null;
                location = null;
                return false;
            }
        }

        private static OdysseyLocationSnapshot CaptureLocation(
            PlanetTile tile,
            BiomeDef biome,
            MapParent parent,
            bool isPlayerHome)
        {
            string layer = OdysseyLocationLayerTokens.Unknown;
            string stableKey = string.Empty;
            if (tile.Valid)
            {
                PlanetLayer planetLayer = tile.Layer;
                if (planetLayer != null)
                {
                    layer = planetLayer.IsRootSurface
                        ? OdysseyLocationLayerTokens.Surface
                        : OdysseyLocationLayerTokens.Orbit;
                    stableKey = "planet-layer-" + planetLayer.LayerID + "-tile-" + tile.tileId;
                }
            }

            string siteDefName = string.Empty;
            string siteLabel = string.Empty;
            if (parent != null)
            {
                Site site = parent as Site;
                siteDefName = site?.MainSitePartDef?.defName
                    ?? parent.def?.defName
                    ?? string.Empty;
                siteLabel = CleanLabel(parent.LabelCap);
            }

            string biomeLabel = CleanLabel(biome?.LabelCap.Resolve());
            OdysseyLocationSnapshot raw = new OdysseyLocationSnapshot
            {
                stableKey = stableKey,
                visibleLabel = siteLabel.Length > 0 ? siteLabel : biomeLabel,
                layerToken = layer,
                biomeDefName = biome?.defName ?? string.Empty,
                biomeLabel = biomeLabel,
                siteDefName = siteDefName,
                siteLabel = siteLabel,
                isPlayerHome = isPlayerHome,
                visible = true
            };
            return OdysseyLocationPolicy.Classify(raw, DiaryOdysseyPolicy.Snapshot());
        }

        private static string CleanStableId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            if (cleaned.IndexOf('|') >= 0) return string.Empty;
            return cleaned.Length <= 160 ? cleaned : cleaned.Substring(0, 160);
        }

        private static string CleanLabel(string value)
        {
            return OdysseyContextFormatter.CleanValue(
                PromptTextSanitizer.LocalizedPromptText(value),
                DiaryOdysseyPolicy.Snapshot().maximumContextValueCharacters);
        }
    }
}
