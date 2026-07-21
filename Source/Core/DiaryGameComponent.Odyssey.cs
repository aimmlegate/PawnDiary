// Odyssey O1.2 persistence, O1.3 lifecycle ownership, O1.4 landing emission, and N2-O/N3-O
// event-time provider snapshots. Harmony adapters call guarded live-object overloads here; every
// lasting value is immediately detached and only the successful landing boundary may create a page.
using System;
using System.Collections.Generic;
using System.Linq;
using PawnDiary.Ingestion;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private OdysseyJourneyState odysseyActiveJourney;
        private OdysseyTravelHistoryState odysseyTravelHistory = new OdysseyTravelHistoryState();
        private OdysseyTakeoffIntent odysseyTakeoffIntent;
        private OdysseyPendingLanding odysseyPendingLanding;

        /// <summary>Scribes the two additive O1 keys and normalizes every loaded detached row.</summary>
        private void ExposeOdysseyData()
        {
            Scribe_Deep.Look(
                ref odysseyActiveJourney,
                OdysseySaveKeys.ActiveJourney);
            Scribe_Deep.Look(
                ref odysseyTravelHistory,
                OdysseySaveKeys.TravelHistory);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
                odysseyActiveJourney = OdysseyStatePersistence.NormalizeJourney(
                    odysseyActiveJourney,
                    policy);
                odysseyTravelHistory = OdysseyStatePersistence.NormalizeHistory(
                    odysseyTravelHistory,
                    policy);
            }
        }

        /// <summary>
        /// Starts a new colony with trustworthy empty history, seeding only the exact visible starting
        /// location when Odyssey is active. No journey or page is manufactured.
        /// </summary>
        private void ResetOdysseyForNewGame()
        {
            odysseyActiveJourney = null;
            odysseyTakeoffIntent = null;
            odysseyPendingLanding = null;
            int now = Find.TickManager?.TicksGame ?? 0;
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            OdysseyTravelHistorySnapshot history = new OdysseyTravelHistorySnapshot
            {
                historyInitialized = true,
                historyTrustworthyForFirstClaims = true,
                featureStartTick = Math.Max(0, now)
            };

            OdysseyLocationSnapshot visible = FirstVisibleOdysseyLocation();
            if (visible != null)
            {
                OdysseyTravelHistorySnapshot seeded = OdysseyHistoryPolicy.BaselineOldSave(
                    new OdysseyTravelHistorySnapshot(),
                    visible,
                    now,
                    policy);
                seeded.historyTrustworthyForFirstClaims = true;
                history = seeded;
            }

            odysseyTravelHistory = OdysseyTravelHistoryState.FromSnapshot(history);
        }

        /// <summary>
        /// Initializes a save written before O1.2 without backfilling a launch/landing. A currently
        /// travelling gravship becomes an incomplete baseline journey so O1.3 can correlate its later
        /// landing while novelty-first claims remain disabled.
        /// </summary>
        private void BootstrapOdysseyForLoadedSave()
        {
            // Intent/landing rows are cutscene-local only and never survive a save/load boundary.
            odysseyTakeoffIntent = null;
            odysseyPendingLanding = null;
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            odysseyTravelHistory = OdysseyStatePersistence.NormalizeHistory(
                odysseyTravelHistory,
                policy);
            if (odysseyTravelHistory.historyInitialized)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            OdysseyJourneySnapshot travelling;
            OdysseyLocationSnapshot visible;
            if (DlcContext.TryCaptureOdysseyTravellingBaseline(
                now,
                out travelling,
                out visible))
            {
                odysseyActiveJourney = OdysseyStatePersistence.NormalizeJourney(
                    OdysseyJourneyState.FromSnapshot(travelling),
                    policy);
            }
            else
            {
                visible = FirstVisibleOdysseyLocation();
            }

            OdysseyTravelHistorySnapshot baseline = OdysseyHistoryPolicy.BaselineOldSave(
                odysseyTravelHistory.ToSnapshot(),
                visible,
                now,
                policy);
            // BaselineOldSave already sets this false; repeat the assignment here because it is the
            // central compatibility promise and must stay obvious beside component initialization.
            baseline.historyTrustworthyForFirstClaims = false;
            odysseyTravelHistory = OdysseyTravelHistoryState.FromSnapshot(baseline);
        }

        /// <summary>True only when this eligible pawn already owns a persisted diary record.</summary>
        internal bool HasOdysseyDiary(Pawn pawn)
        {
            return pawn != null && FindDiary(pawn, false) != null;
        }

        /// <summary>
        /// Applies the Odyssey-only launch cooldown to the existing Ritual owner. Other ritual groups
        /// never call this seam and retain their normal fanout behavior.
        /// </summary>
        internal bool AllowsOdysseyLaunchRitualAt(int ritualTick, Pawn ritualPawn)
        {
            if (!ModsConfig.OdysseyActive) return false;
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            OdysseyTravelHistorySnapshot history = odysseyTravelHistory?.ToSnapshot()
                ?? new OdysseyTravelHistorySnapshot();
            OdysseyLocationSnapshot currentLocation;
            bool leavingLongHeldHome = DlcContext.TryCaptureOdysseyLocation(
                    ritualPawn,
                    out currentLocation)
                && OdysseyLaunchPolicy.IsLeavingLongHeldHome(
                    history,
                    currentLocation,
                    ritualTick,
                    policy);
            return OdysseyLaunchPolicy.AllowsPage(
                history.lastLaunchPageTick,
                ritualTick,
                leavingLongHeldHome,
                policy);
        }

        /// <summary>
        /// Commits the Odyssey launch cooldown only after the ritual owner created a real DiaryEvent.
        /// Repeated fan-out children at the same tick remain idempotent.
        /// </summary>
        internal void MarkOdysseyLaunchPageAt(int ritualTick)
        {
            if (!ModsConfig.OdysseyActive || ritualTick < 0) return;
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            odysseyTravelHistory = OdysseyTravelHistoryState.FromSnapshot(
                OdysseyHistoryPolicy.MarkLaunchPage(
                    odysseyTravelHistory?.ToSnapshot(),
                    ritualTick,
                    policy));
        }

        /// <summary>
        /// Freezes one exact current mobile-home snapshot for an already-authorized event. The guarded
        /// collector proves this pawn is physically inside this gravship; N3-O may additionally copy
        /// the existing seasonal-flood observer row only when it belongs to this exact map and POV.
        /// </summary>
        internal OdysseyNarrativeSnapshot OdysseyNarrativeSnapshotFor(Pawn pawn, int sourceTick)
        {
            if (!ModsConfig.OdysseyActive || pawn == null)
            {
                return null;
            }

            OdysseyMobileHomeSnapshot home;
            if (!DlcContext.TryCaptureOdysseyMobileHome(pawn, out home)
                || home == null || home.location == null
                || string.IsNullOrWhiteSpace(home.location.stableKey)
                || string.IsNullOrWhiteSpace(home.location.visibleLabel))
            {
                return null;
            }

            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            string text = FormatOdysseyNarrative(
                policy.mobileHomeNarrativeFormat,
                home.shipName,
                home.location.visibleLabel);
            string journeyId = odysseyActiveJourney != null
                && string.Equals(
                    odysseyActiveJourney.shipStableId,
                    home.shipStableId,
                    StringComparison.Ordinal)
                ? odysseyActiveJourney.journeyId
                : string.Empty;
            OdysseyNarrativeSnapshot snapshot = new OdysseyNarrativeSnapshot
            {
                providerAvailable = policy.enabled,
                povPawnId = pawn.GetUniqueLoadID(),
                shipStableId = home.shipStableId,
                shipName = home.shipName,
                journeyId = journeyId ?? string.Empty,
                locationKey = home.location.stableKey,
                locationLabel = home.location.visibleLabel,
                homeText = text,
                sourceTick = Math.Max(0, sourceTick),
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };

            OdysseyEnvironmentalNarrativeFact pressure = CaptureOdysseySeasonalFloodPressure(
                pawn,
                home,
                policy,
                snapshot.povPawnId,
                snapshot.sourceTick);
            if (pressure != null) snapshot.environmentalPressures.Add(pressure);

            // A broken translation/override omits only its own optional lens. If neither provider
            // format survived, return the normal no-provider path without affecting source evidence.
            return text.Length > 0 || snapshot.environmentalPressures.Count > 0 ? snapshot : null;
        }

        /// <summary>
        /// Reuses O2's saved prompt-only observer fact; it never scans SeasonalFlood Things itself.
        /// Every check fails closed so a stale/corrupt/cross-map row cannot become narrative context.
        /// </summary>
        private OdysseyEnvironmentalNarrativeFact CaptureOdysseySeasonalFloodPressure(
            Pawn pawn,
            OdysseyMobileHomeSnapshot home,
            OdysseyPolicySnapshot policy,
            string povPawnId,
            int eventTick)
        {
            try
            {
                if (pawn?.Map == null || home?.location == null || policy == null
                    || activeObservedConditions == null || activeObservedConditions.Count == 0)
                {
                    return null;
                }

                int mapUniqueId = MapUniqueId(pawn.Map);
                ActiveObservedConditionState newest = null;
                for (int i = 0; i < activeObservedConditions.Count; i++)
                {
                    ActiveObservedConditionState active = activeObservedConditions[i];
                    if (active == null || active.scope != ObservedConditionScope.Map
                        || active.mapUniqueId != mapUniqueId
                        || active.lastObservedTick < 0 || active.lastObservedTick > eventTick
                        || active.lastSeenEvidenceCount < 1
                        || !string.Equals(active.conditionDefName,
                            OdysseyEnvironmentalNarrativeTokens.SeasonalFloodConditionDefName,
                            StringComparison.Ordinal)
                        || !string.Equals(active.conditionKey,
                            OdysseyEnvironmentalNarrativeTokens.SeasonalFloodConditionDefName,
                            StringComparison.Ordinal)
                        || !string.Equals(active.lastSeenEvidenceDefName,
                            OdysseyEnvironmentalNarrativeTokens.SeasonalFloodEvidenceDefName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    DiaryObservedConditionDef def = ObservedConditionDefFor(active);
                    if (!ExactSeasonalFloodObserver(def)
                        || !ObservedConditionPromptActivity.IsPromptActive(
                            active.startRecorded,
                            active.endRecorded,
                            active.firstMissingTick,
                            eventTick,
                            def.endDebounceTicks))
                    {
                        continue;
                    }

                    if (newest == null || active.lastObservedTick > newest.lastObservedTick)
                        newest = active;
                }

                if (newest == null) return null;
                string text = FormatOdysseyNarrative(
                    policy.seasonalFloodNarrativeFormat,
                    home.shipName,
                    home.location.visibleLabel);
                if (text.Length == 0) return null;

                return new OdysseyEnvironmentalNarrativeFact
                {
                    sourceKind = OdysseyEnvironmentalNarrativeTokens.SeasonalFlood,
                    sourceDefName = newest.conditionDefName,
                    evidenceDefName = newest.lastSeenEvidenceDefName,
                    povPawnId = povPawnId,
                    shipStableId = home.shipStableId,
                    locationKey = home.location.stableKey,
                    text = text,
                    sourceTick = newest.lastObservedTick,
                    pawnCanKnow = true,
                    hasVerifiedPovConnection = true
                };
            }
            catch (Exception exception)
            {
                // Environmental context is optional. Keep the already-authorized event and the N2-O
                // home snapshot even if a malformed Def/save row breaks this one N3-O projection.
                Log.ErrorOnce(
                    "[Pawn Diary] Odyssey seasonal-flood narrative context failed; the event keeps "
                    + "its ordinary evidence and home context: " + exception,
                    "PawnDiary.OdysseyNarrative.SeasonalFlood".GetHashCode());
                return null;
            }
        }

        private static bool ExactSeasonalFloodObserver(DiaryObservedConditionDef def)
        {
            return def != null && def.enabled && def.promptEnabled
                && def.scope == ObservedConditionScope.Map
                && def.observerType == ObservedConditionObserverType.ThingPresent
                && def.matchDefNames != null && def.matchDefNames.Count == 1
                && string.Equals(
                    def.matchDefNames[0],
                    OdysseyEnvironmentalNarrativeTokens.SeasonalFloodEvidenceDefName,
                    StringComparison.Ordinal);
        }

        private static string FormatOdysseyNarrative(string format, params object[] values)
        {
            if (string.IsNullOrWhiteSpace(format)) return string.Empty;
            try
            {
                return DiaryLineCleaner.CleanLine(string.Format(format, values));
            }
            catch (FormatException)
            {
                // A malformed custom translation disables only this optional provider lens.
                return string.Empty;
            }
        }

        /// <summary>Guarded takeoff-prefix adapter; captures intent only and never writes a page.</summary>
        internal bool ObserveOdysseyTakeoffIntent(Building_GravEngine engine, PlanetTile targetTile)
        {
            OdysseyTakeoffIntent intent;
            return DlcContext.TryCaptureOdysseyTakeoffIntent(
                    engine,
                    targetTile,
                    Find.TickManager?.TicksGame ?? 0,
                    out intent)
                && CaptureOdysseyTakeoffIntent(intent);
        }

        /// <summary>Stores one bounded transient intent, rejecting overlapping different ships.</summary>
        internal bool CaptureOdysseyTakeoffIntent(OdysseyTakeoffIntent intent)
        {
            if (intent == null || string.IsNullOrWhiteSpace(intent.engineId)) return false;
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            if (odysseyTakeoffIntent != null
                && !OdysseyLifecyclePolicy.IsExpired(
                    odysseyTakeoffIntent.captureTick,
                    intent.captureTick,
                    policy.takeoffCorrelationTicks)
                && !string.Equals(
                    odysseyTakeoffIntent.engineId,
                    intent.engineId,
                    StringComparison.Ordinal))
            {
                if (Prefs.DevMode)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] Ignored overlapping Odyssey takeoff intents for different engines; "
                        + "no journey state was cross-linked.",
                        "PawnDiary.Odyssey.OverlappingTakeoff".GetHashCode());
                }
                return false;
            }

            odysseyTakeoffIntent = intent;
            return true;
        }

        /// <summary>Guarded TravelTo-postfix adapter; commits saved journey/history and emits nothing.</summary>
        internal bool ObserveOdysseyTravelCommit(
            Gravship gravship,
            PlanetTile oldTile,
            PlanetTile newTile)
        {
            OdysseyTravelCommitObservation observation;
            return DlcContext.TryCaptureOdysseyTravelCommit(
                    gravship,
                    oldTile,
                    newTile,
                    Find.TickManager?.TicksGame ?? 0,
                    out observation)
                && CommitOdysseyTravel(observation);
        }

        /// <summary>Applies one detached travel commit idempotently by its frozen journey ID.</summary>
        internal bool CommitOdysseyTravel(OdysseyTravelCommitObservation observation)
        {
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            bool matchedIntent;
            OdysseyJourneySnapshot journey = OdysseyLifecyclePolicy.BuildJourney(
                observation,
                odysseyTakeoffIntent,
                policy,
                out matchedIntent);
            OdysseyJourneyState normalized = OdysseyStatePersistence.NormalizeJourney(
                OdysseyJourneyState.FromSnapshot(journey),
                policy);
            if (normalized == null) return false;

            if (odysseyActiveJourney != null
                && string.Equals(
                    odysseyActiveJourney.journeyId,
                    normalized.journeyId,
                    StringComparison.Ordinal))
            {
                if (matchedIntent) odysseyTakeoffIntent = null;
                return true;
            }

            OdysseyTravelHistorySnapshot history = OdysseyHistoryPolicy.ApplyDeparture(
                odysseyTravelHistory?.ToSnapshot(),
                normalized.ToSnapshot(),
                policy);
            odysseyTravelHistory = OdysseyTravelHistoryState.FromSnapshot(history);
            odysseyActiveJourney = normalized;
            odysseyPendingLanding = null;
            if (matchedIntent
                || OdysseyLifecyclePolicy.IsExpired(
                    odysseyTakeoffIntent?.captureTick ?? -1,
                    observation?.departureTick ?? 0,
                    policy.takeoffCorrelationTicks))
            {
                odysseyTakeoffIntent = null;
            }
            return true;
        }

        /// <summary>Guarded landing-prefix adapter; snapshots pending destination facts only.</summary>
        internal bool ObserveOdysseyLandingStart(Gravship gravship, Map map)
        {
            OdysseyPendingLanding pending;
            return DlcContext.TryCaptureOdysseyPendingLanding(
                    gravship,
                    map,
                    Find.TickManager?.TicksGame ?? 0,
                    out pending)
                && CaptureOdysseyLandingStart(pending);
        }

        /// <summary>Stores pending landing state only when it matches the one active journey.</summary>
        internal bool CaptureOdysseyLandingStart(OdysseyPendingLanding pending)
        {
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            if (ClearExpiredOdysseyJourney(pending?.captureTick ?? 0, policy)
                || odysseyActiveJourney == null || pending == null
                || !string.Equals(
                    odysseyActiveJourney.shipStableId,
                    pending.shipStableId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            odysseyPendingLanding = pending;
            return true;
        }

        /// <summary>
        /// LandingEnded prefix adapter. It preserves final controller fields before vanilla clears
        /// them; the returned detached state is carried to the postfix through Harmony __state.
        /// </summary>
        internal OdysseyPendingLanding BeginOdysseyLandingFinish(Gravship gravship, Map map)
        {
            OdysseyPendingLanding finish;
            if (!DlcContext.TryCaptureOdysseyPendingLanding(
                gravship,
                map,
                Find.TickManager?.TicksGame ?? 0,
                out finish))
            {
                return null;
            }

            return CaptureOdysseyLandingFinish(finish) ? finish : null;
        }

        /// <summary>Validates a detached successful-finish capture against the active journey.</summary>
        internal bool CaptureOdysseyLandingFinish(OdysseyPendingLanding finish)
        {
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            bool valid = !ClearExpiredOdysseyJourney(finish?.captureTick ?? 0, policy)
                && odysseyActiveJourney != null
                && finish != null
                && string.Equals(
                    odysseyActiveJourney.shipStableId,
                    finish.shipStableId,
                    StringComparison.Ordinal);
            if (valid)
            {
                // Keep the exact finish object as the synchronous outcome hand-off. The outcome
                // worker runs after LandingEnded's prefix and before its postfix.
                odysseyPendingLanding = finish;
            }
            return valid;
        }

        /// <summary>
        /// Receives one exact outcome only after its concrete vanilla worker returned successfully.
        /// The live gravship is reduced to its stable ID before pure correlation mutates transient state.
        /// </summary>
        internal bool ObserveOdysseyLandingOutcome(
            Gravship gravship,
            string outcomeDefName,
            string outcomeLabel)
        {
            if (!ModsConfig.OdysseyActive || gravship == null) return false;
            string shipStableId = DlcContext.OdysseyStableShipId(gravship);
            return OdysseyLifecyclePolicy.TryAttachLandingOutcome(
                odysseyPendingLanding,
                shipStableId,
                outcomeDefName,
                outcomeLabel);
        }

        /// <summary>
        /// LandingEnded postfix state sink. It always applies the successful landing observation and
        /// clears lifecycle state. A novelty-authorized page marker is committed only after the one
        /// canonical GravshipJourney event has actually been created.
        /// </summary>
        internal bool CompleteOdysseyLanding(OdysseyPendingLanding finish, List<Pawn> livePawns = null)
        {
            if (finish == null || odysseyActiveJourney == null) return false;
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            OdysseyJourneySnapshot journey = odysseyActiveJourney.ToSnapshot();
            if (!string.Equals(journey.shipStableId, finish.shipStableId, StringComparison.Ordinal))
                return false;

            OdysseyPendingLanding correlated = OdysseyLifecyclePolicy.PendingLandingMatches(
                odysseyPendingLanding,
                finish.shipStableId,
                finish.captureTick,
                policy)
                ? odysseyPendingLanding
                : null;
            OdysseyLocationSnapshot destination = finish.destination ?? correlated?.destination ?? journey.destination;
            OdysseyPendingLanding outcomeSource = correlated ?? finish;
            journey.landingOutcomeDefName = outcomeSource?.landingOutcomeDefName ?? string.Empty;
            journey.landingOutcomeLabel = outcomeSource?.landingOutcomeLabel ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(journey.landingOutcomeDefName)) journey.roughLanding = true;
            List<OdysseyWriterCandidate> writers = finish.writers != null && finish.writers.Count > 0
                ? finish.writers
                : correlated?.writers;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyGravshipJourney(
                OdysseyEventDefNames.Landing);
            bool groupEnabled = ModsConfig.OdysseyActive
                && policy.landingPageEnabled
                && group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            OdysseyLandingPlan plan = OdysseyLandingPolicy.Plan(
                new OdysseyLandingObservation
                {
                    journey = journey,
                    destination = destination,
                    landingTick = finish.captureTick,
                    groupEnabled = groupEnabled,
                    writers = writers ?? new List<OdysseyWriterCandidate>()
                },
                odysseyTravelHistory?.ToSnapshot(),
                policy);
            if (plan.historyMutation == null || plan.historyMutation.landingObservationTick < 0)
                return false;

            bool pageCreated = false;
            if (plan.writePage)
            {
                try
                {
                    GravshipJourneySignal signal = new GravshipJourneySignal(
                        journey,
                        destination,
                        plan,
                        policy,
                        group,
                        livePawns ?? new List<Pawn>());
                    Dispatch(signal);
                    pageCreated = signal.CreatedEvent != null;
                }
                catch (Exception exception)
                {
                    // Landing history is more important than optional prose. A malformed runtime
                    // writer or prompt cannot leave a stale active journey or consume page history.
                    Log.ErrorOnce(
                        "[Pawn Diary] Odyssey landing page creation failed; the successful landing "
                        + "was still recorded without a page: " + exception,
                        "PawnDiary.OdysseyLanding.Emit".GetHashCode());
                }
            }

            if (!pageCreated)
            {
                plan.historyMutation.landingPageTick = -1;
                plan.historyMutation.journeyIdToMarkEmitted = string.Empty;
            }

            odysseyTravelHistory = OdysseyTravelHistoryState.FromSnapshot(
                OdysseyHistoryPolicy.Apply(
                    odysseyTravelHistory?.ToSnapshot(),
                    plan,
                    policy));
            odysseyActiveJourney = null;
            odysseyPendingLanding = null;
            return true;
        }

        private bool ClearExpiredOdysseyJourney(int now, OdysseyPolicySnapshot policy)
        {
            if (odysseyActiveJourney == null) return false;
            if (!OdysseyLifecyclePolicy.IsExpired(
                odysseyActiveJourney.departureTick,
                now,
                policy.staleJourneyRetentionTicks))
            {
                return false;
            }

            odysseyActiveJourney = null;
            odysseyPendingLanding = null;
            return true;
        }

        private static OdysseyLocationSnapshot FirstVisibleOdysseyLocation()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return null;
            }

            IEnumerable<Map> maps = Find.Maps;
            if (maps == null)
            {
                return null;
            }

            // Prefer a player-home map, then the current map, then any loaded map. Exact map state is
            // copied immediately; no Map reference enters history.
            Map candidate = maps.FirstOrDefault(map => map != null && map.IsPlayerHome)
                ?? Verse.Current.Game?.CurrentMap
                ?? maps.FirstOrDefault(map => map != null);
            OdysseyLocationSnapshot location;
            return DlcContext.TryCaptureOdysseyLocation(candidate, out location)
                ? location
                : null;
        }
    }
}
