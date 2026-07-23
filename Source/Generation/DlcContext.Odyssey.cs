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
    /// <summary>
    /// Live references retained only across one synchronous private DeactivateCore call. Detached facts
    /// freeze the exact operator, branch, quest identity, and prompt evidence before vanilla ends the
    /// quest and may destroy the core.
    /// </summary>
    internal sealed class OdysseyMechhiveOutcomeCapture
    {
        internal object coreComp;
        internal Thing core;
        internal Pawn actor;
        internal Quest quest;
        internal OdysseyMechhiveOutcomeFacts facts;
        internal bool suppressesQuestSuccess;
        internal bool scopeClosed;

        internal Pawn ResolveActor(string pawnId)
        {
            return actor != null
                && string.Equals(actor.GetUniqueLoadID(), pawnId, StringComparison.Ordinal)
                ? actor
                : null;
        }
    }

    internal static partial class DlcContext
    {
        private static bool odysseyLaunchFieldsSearched;
        private static FieldInfo odysseyLaunchInfoField;
        private static FieldInfo odysseyPilotField;
        private static FieldInfo odysseyCopilotField;
        private static FieldInfo odysseyQualityField;
        private static FieldInfo odysseyNegativeOutcomeField;
        private static FieldInfo odysseyMechhiveInteractedPawnField;
        private static FieldInfo odysseyMechhiveDeactivatedField;
        private static FieldInfo odysseyMechhiveScavengingField;
        private static Type odysseyMechhiveQuestPartType;
        private static FieldInfo odysseyMechhiveQuestPartCoreField;
        private static Func<object, OdysseyMechhiveOutcomeCapture, bool,
            OdysseyMechhiveOutcomeFacts> odysseyMechhiveCompletionOverrideForTests;

        /// <summary>
        /// Resolves every private field required by the verified 1.6 owner. A changed field disables
        /// only the specialized ending patch; generic Quest completion remains untouched.
        /// </summary>
        internal static bool ResolveOdysseyMechhiveReflection(
            Type compType,
            Type questPartType)
        {
            odysseyMechhiveInteractedPawnField = AccessTools.Field(compType, "interactedPawn");
            odysseyMechhiveDeactivatedField = AccessTools.Field(compType, "deactivated");
            odysseyMechhiveScavengingField = AccessTools.Field(compType, "scavenging");
            odysseyMechhiveQuestPartType = questPartType;
            odysseyMechhiveQuestPartCoreField = AccessTools.Field(questPartType, "core");
            return compType != null && questPartType != null
                && odysseyMechhiveInteractedPawnField != null
                && odysseyMechhiveDeactivatedField != null
                && odysseyMechhiveScavengingField != null
                && odysseyMechhiveQuestPartCoreField != null;
        }

        /// <summary>
        /// Captures the exact core operator, choice, and owning quest before vanilla sends CoreDefeated.
        /// Quest correlation requires the active exact root plus its QuestPart_CerebrexCore.core
        /// reference matching this core; no root-name-only guess may claim another completion.
        /// </summary>
        internal static bool TryCaptureOdysseyMechhiveOutcomeBefore(
            object coreComp,
            bool scavenging,
            out OdysseyMechhiveOutcomeCapture capture)
        {
            capture = null;
            ThingComp comp = coreComp as ThingComp;
            Thing core = comp?.parent;
            if (!ModsConfig.OdysseyActive || coreComp == null || core == null
                || odysseyMechhiveInteractedPawnField == null
                || odysseyMechhiveQuestPartCoreField == null)
            {
                return false;
            }

            try
            {
                Pawn actor = odysseyMechhiveInteractedPawnField.GetValue(coreComp) as Pawn;
                Quest quest = FindExactMechhiveQuest(core);
                string actorId = actor?.GetUniqueLoadID() ?? string.Empty;
                if (actor == null || quest == null || quest.id <= 0
                    || string.IsNullOrWhiteSpace(actorId))
                {
                    return false;
                }

                capture = new OdysseyMechhiveOutcomeCapture
                {
                    coreComp = coreComp,
                    core = core,
                    actor = actor,
                    quest = quest,
                    facts = new OdysseyMechhiveOutcomeFacts
                    {
                        actorPawnId = actorId,
                        actorLabel = DiaryLineCleaner.CleanLine(actor.LabelShortCap),
                        actorSummary = BuildOptionalOdysseyMechhiveText(
                            () => DiaryContextBuilder.BuildPawnSummary(actor)),
                        setting = BuildOptionalOdysseyMechhiveText(
                            () => DiaryContextBuilder.BuildSurroundingsSummary(actor)),
                        outcomeToken = scavenging
                            ? OdysseyMechhiveOutcomeTokens.Scavenged
                            : OdysseyMechhiveOutcomeTokens.Destroyed,
                        questRootDefName = quest.root?.defName ?? string.Empty,
                        questId = quest.id,
                        tick = Find.TickManager?.TicksGame ?? 0,
                        playerVisible = true,
                        actorEligible = DiaryGameComponent.IsDiaryEligible(actor)
                    }
                };
                return true;
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Pawn Diary] Could not correlate the exact Odyssey Mechhive quest/core owner; "
                        + "the generic Quest completion remains unchanged: "
                        + exception.GetType().Name + ": " + exception.Message,
                    "PawnDiary.Odyssey.Mechhive.Capture".GetHashCode());
                capture = null;
                return false;
            }
        }

        /// <summary>
        /// Verifies the normally-returned private owner against its committed core state and the exact
        /// deferred Quest success. Destroy requires a destroyed core; scavenge requires the surviving
        /// deactivated core. Both require the captured actor and branch fields to remain exact.
        /// </summary>
        internal static bool TryCompleteOdysseyMechhiveOutcome(
            object coreComp,
            OdysseyMechhiveOutcomeCapture capture,
            bool questSuccessObserved,
            out OdysseyMechhiveOutcomeFacts facts)
        {
            facts = null;
            if (!ModsConfig.OdysseyActive || capture?.facts == null
                || !ReferenceEquals(coreComp, capture.coreComp))
            {
                return false;
            }

            Func<object, OdysseyMechhiveOutcomeCapture, bool,
                OdysseyMechhiveOutcomeFacts> testOverride =
                odysseyMechhiveCompletionOverrideForTests;
            if (testOverride != null)
            {
                facts = testOverride(coreComp, capture, questSuccessObserved);
                return facts != null;
            }

            try
            {
                bool deactivated = (bool)odysseyMechhiveDeactivatedField.GetValue(coreComp);
                bool scavenging = (bool)odysseyMechhiveScavengingField.GetValue(coreComp);
                Pawn actor = odysseyMechhiveInteractedPawnField.GetValue(coreComp) as Pawn;
                string expected = capture.facts.outcomeToken;
                bool branchMatches = expected == OdysseyMechhiveOutcomeTokens.Scavenged
                    ? scavenging && capture.core != null && !capture.core.Destroyed
                    : expected == OdysseyMechhiveOutcomeTokens.Destroyed
                        && !scavenging && capture.core != null && capture.core.Destroyed;
                if (!deactivated || !branchMatches || !ReferenceEquals(actor, capture.actor))
                    return false;

                facts = CopyMechhiveFacts(capture.facts);
                facts.methodReturnedNormally = true;
                facts.questSuccessObserved = questSuccessObserved;
                return true;
            }
            catch (Exception)
            {
                facts = null;
                return false;
            }
        }

        /// <summary>Installs a null-by-default loaded-test completion seam.</summary>
        internal static void SetOdysseyMechhiveCompletionOverrideForTests(
            Func<object, OdysseyMechhiveOutcomeCapture, bool,
                OdysseyMechhiveOutcomeFacts> value)
        {
            odysseyMechhiveCompletionOverrideForTests = value;
        }

        private static Quest FindExactMechhiveQuest(Thing core)
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (quests == null || odysseyMechhiveQuestPartType == null) return null;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest == null || quest.Historical
                    || !string.Equals(
                        quest.root?.defName,
                        OdysseyMechhiveSourceTokens.QuestRootDefName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                List<QuestPart> parts = quest.PartsListForReading;
                if (parts == null) continue;
                for (int j = 0; j < parts.Count; j++)
                {
                    QuestPart part = parts[j];
                    if (part != null && part.GetType() == odysseyMechhiveQuestPartType
                        && ReferenceEquals(
                            odysseyMechhiveQuestPartCoreField.GetValue(part), core))
                    {
                        return quest;
                    }
                }
            }
            return null;
        }

        private static OdysseyMechhiveOutcomeFacts CopyMechhiveFacts(
            OdysseyMechhiveOutcomeFacts source)
        {
            return new OdysseyMechhiveOutcomeFacts
            {
                actorPawnId = source.actorPawnId ?? string.Empty,
                actorLabel = source.actorLabel ?? string.Empty,
                actorSummary = source.actorSummary ?? string.Empty,
                setting = source.setting ?? string.Empty,
                outcomeToken = source.outcomeToken ?? string.Empty,
                questRootDefName = source.questRootDefName ?? string.Empty,
                questId = source.questId,
                tick = source.tick,
                playerVisible = source.playerVisible,
                actorEligible = source.actorEligible
            };
        }

        private static string BuildOptionalOdysseyMechhiveText(Func<string> builder)
        {
            try
            {
                return OdysseyContextFormatter.CleanValue(
                    builder == null ? string.Empty : builder(),
                    DiaryOdysseyPolicy.Snapshot().maximumContextValueCharacters);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Clears only process-static O3 test state at every game lifecycle boundary.</summary>
        internal static void ResetOdysseyMechhiveTestState()
        {
            odysseyMechhiveCompletionOverrideForTests = null;
        }

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
