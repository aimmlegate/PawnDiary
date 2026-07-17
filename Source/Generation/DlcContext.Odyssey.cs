// Odyssey-specific guarded live reads. All RimWorld Maps, tiles, Sites, grav engines, and world
// gravships are converted here into detached snapshots before pure policy or saved state sees them.
// The entire file is inert without Odyssey: every public entry point checks ModsConfig.OdysseyActive.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    internal static partial class DlcContext
    {
        private static bool odysseyLaunchFieldsSearched;
        private static FieldInfo odysseyLaunchInfoField;
        private static FieldInfo odysseyPilotField;
        private static FieldInfo odysseyCopilotField;
        private static FieldInfo odysseyQualityField;
        private static FieldInfo odysseyNegativeOutcomeField;

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

                // A travelling Gravship and its parked engine use different load IDs. Prefer the
                // world-object ID only when vanilla proves this exact engine belongs to it, allowing
                // Narrative N2-O to correlate the mobile home with the committed journey exactly.
                Gravship travelling = Verse.Current.Game?.Gravship;
                string shipId = travelling != null && ReferenceEquals(travelling.Engine, engine)
                    ? CleanStableId(travelling.GetUniqueLoadID())
                    : CleanStableId(engine.GetUniqueLoadID());
                snapshot = new OdysseyMobileHomeSnapshot
                {
                    shipStableId = shipId,
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

        /// <summary>Captures pre-despawn takeoff truth while the origin map and exact crew still exist.</summary>
        internal static bool TryCaptureOdysseyTakeoffIntent(
            Building_GravEngine engine,
            PlanetTile targetTile,
            int captureTick,
            out OdysseyTakeoffIntent intent)
        {
            intent = null;
            if (!ModsConfig.OdysseyActive || engine == null || !engine.Spawned || engine.Map == null)
            {
                return false;
            }

            try
            {
                OdysseyLocationSnapshot origin;
                if (!TryCaptureOdysseyLocation(engine.Map, out origin)) return false;
                OdysseyLocationSnapshot target = CaptureTileLocation(targetTile);
                Pawn pilot;
                Pawn copilot;
                float quality;
                bool rough;
                ReadLaunchFacts(engine, out pilot, out copilot, out quality, out rough);
                string name;
                if (!GravshipUtility.TryGetNameOfGravshipOnMap(engine.Map, out name))
                    name = engine.LabelShortCap;

                intent = new OdysseyTakeoffIntent
                {
                    engineId = CleanStableId(engine.GetUniqueLoadID()),
                    shipName = CleanLabel(name),
                    origin = origin,
                    selectedTargetKey = target?.stableKey ?? string.Empty,
                    captureTick = Math.Max(0, captureTick),
                    launchQualityBand = OdysseyJourneyPolicy.LaunchQualityBand(
                        quality, DiaryOdysseyPolicy.Snapshot()),
                    roughLanding = rough,
                    writers = CaptureMapWriters(engine.Map, engine, pilot, copilot)
                };
                return intent.engineId.Length > 0;
            }
            catch (Exception)
            {
                intent = null;
                return false;
            }
        }

        /// <summary>Captures the world-object identity that vanilla has successfully committed to travel.</summary>
        internal static bool TryCaptureOdysseyTravelCommit(
            Gravship gravship,
            PlanetTile oldTile,
            PlanetTile newTile,
            int departureTick,
            out OdysseyTravelCommitObservation observation)
        {
            observation = null;
            if (!ModsConfig.OdysseyActive || gravship == null || !newTile.Valid)
            {
                return false;
            }

            try
            {
                Building_GravEngine engine = gravship.Engine;
                Pawn pilot;
                Pawn copilot;
                float quality;
                bool rough;
                ReadLaunchFacts(engine, out pilot, out copilot, out quality, out rough);
                observation = new OdysseyTravelCommitObservation
                {
                    shipStableId = CleanStableId(gravship.GetUniqueLoadID()),
                    engineId = CleanStableId(engine?.GetUniqueLoadID()),
                    shipName = CleanLabel(gravship.LabelCap),
                    origin = CaptureTileLocation(oldTile),
                    destination = CaptureTileLocation(newTile),
                    departureTick = Math.Max(0, departureTick),
                    launchQualityBand = OdysseyJourneyPolicy.LaunchQualityBand(
                        quality, DiaryOdysseyPolicy.Snapshot()),
                    roughLanding = rough,
                    writers = CaptureWriters(gravship.Pawns, pilot, copilot)
                };
                return observation.shipStableId.Length > 0 && observation.destination != null;
            }
            catch (Exception)
            {
                observation = null;
                return false;
            }
        }

        /// <summary>Captures actual target-map facts for landing start or successful-finish correlation.</summary>
        internal static bool TryCaptureOdysseyPendingLanding(
            Gravship gravship,
            Map map,
            int captureTick,
            out OdysseyPendingLanding pending)
        {
            pending = null;
            if (!ModsConfig.OdysseyActive || gravship == null || map == null)
            {
                return false;
            }

            try
            {
                OdysseyLocationSnapshot destination;
                if (!TryCaptureOdysseyLocation(map, out destination)) return false;
                Pawn pilot;
                Pawn copilot;
                float quality;
                bool rough;
                ReadLaunchFacts(gravship.Engine, out pilot, out copilot, out quality, out rough);
                pending = new OdysseyPendingLanding
                {
                    shipStableId = CleanStableId(gravship.GetUniqueLoadID()),
                    destination = destination,
                    captureTick = Math.Max(0, captureTick),
                    writers = CaptureWriters(gravship.Pawns, pilot, copilot)
                };
                return pending.shipStableId.Length > 0;
            }
            catch (Exception)
            {
                pending = null;
                return false;
            }
        }

        /// <summary>
        /// Returns the exact travelling gravship identity for synchronous landing correlation.
        /// No-Odyssey and malformed live-object paths fail closed to an empty identity.
        /// </summary>
        internal static string OdysseyStableShipId(Gravship gravship)
        {
            if (!ModsConfig.OdysseyActive || gravship == null) return string.Empty;
            try
            {
                return CleanStableId(gravship.GetUniqueLoadID());
            }
            catch (Exception)
            {
                return string.Empty;
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

        private static OdysseyLocationSnapshot CaptureTileLocation(PlanetTile tile)
        {
            if (!tile.Valid) return null;
            PlanetLayer layer = tile.Layer;
            BiomeDef biome = null;
            if (layer?.Tiles != null && tile.tileId >= 0 && tile.tileId < layer.Tiles.Count)
            {
                biome = layer.Tiles[tile.tileId]?.PrimaryBiome;
            }

            MapParent parent = Find.WorldObjects?.MapParentAt(tile);
            return CaptureLocation(tile, biome, parent, parent?.Map?.IsPlayerHome == true);
        }

        private static List<OdysseyWriterCandidate> CaptureMapWriters(
            Map map,
            Building_GravEngine engine,
            Pawn pilot,
            Pawn copilot)
        {
            List<Pawn> onboard = new List<Pawn>();
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn != null && GravshipUtility.IsOnboardGravship_NewTemp(
                        pawn.Position, engine, pawn, desperate: false, respectAllowedAreas: false))
                    {
                        onboard.Add(pawn);
                    }
                }
            }

            return CaptureWriters(onboard, pilot, copilot);
        }

        private static List<OdysseyWriterCandidate> CaptureWriters(
            IEnumerable<Pawn> pawns,
            Pawn pilot,
            Pawn copilot)
        {
            List<OdysseyWriterCandidate> result = new List<OdysseyWriterCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (pawns == null) return result;
            foreach (Pawn pawn in pawns)
            {
                string pawnId = CleanStableId(pawn?.GetUniqueLoadID());
                if (pawnId.Length == 0 || !seen.Add(pawnId)) continue;
                result.Add(new OdysseyWriterCandidate
                {
                    pawnId = pawnId,
                    displayName = CleanLabel(pawn.LabelShortCap),
                    roleToken = pawn == pilot
                        ? OdysseyJourneyRoleTokens.Pilot
                        : pawn == copilot
                            ? OdysseyJourneyRoleTokens.Copilot
                            : OdysseyJourneyRoleTokens.Crew,
                    eligible = DiaryGameComponent.IsDiaryEligible(pawn),
                    present = !pawn.Dead,
                    hasDiary = DiaryGameComponent.Instance?.HasOdysseyDiary(pawn) == true
                });
            }

            return result;
        }

        private static void ReadLaunchFacts(
            Building_GravEngine engine,
            out Pawn pilot,
            out Pawn copilot,
            out float quality,
            out bool rough)
        {
            pilot = null;
            copilot = null;
            quality = float.NaN;
            rough = false;
            EnsureOdysseyLaunchFields();
            object launchInfo = engine == null ? null : odysseyLaunchInfoField?.GetValue(engine);
            if (launchInfo == null) return;
            pilot = odysseyPilotField?.GetValue(launchInfo) as Pawn;
            copilot = odysseyCopilotField?.GetValue(launchInfo) as Pawn;
            object qualityValue = odysseyQualityField?.GetValue(launchInfo);
            if (qualityValue is float) quality = (float)qualityValue;
            object negativeValue = odysseyNegativeOutcomeField?.GetValue(launchInfo);
            rough = negativeValue is bool && (bool)negativeValue;
        }

        private static void EnsureOdysseyLaunchFields()
        {
            if (odysseyLaunchFieldsSearched) return;
            odysseyLaunchFieldsSearched = true;
            odysseyLaunchInfoField = AccessTools.Field(typeof(Building_GravEngine), "launchInfo");
            Type launchInfoType = odysseyLaunchInfoField?.FieldType;
            if (launchInfoType == null) return;
            odysseyPilotField = AccessTools.Field(launchInfoType, "pilot");
            odysseyCopilotField = AccessTools.Field(launchInfoType, "copilot");
            odysseyQualityField = AccessTools.Field(launchInfoType, "quality");
            odysseyNegativeOutcomeField = AccessTools.Field(launchInfoType, "doNegativeOutcome");
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
