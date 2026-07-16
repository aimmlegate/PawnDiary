// Odyssey O1.2 persistence and silent baseline ownership. This partial deliberately contains no
// Harmony hooks and emits no DiaryEvent: it only saves detached journey/history state and establishes
// truthful new-game/old-save baselines for the later O1.3 lifecycle adapter.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private OdysseyJourneyState odysseyActiveJourney;
        private OdysseyTravelHistoryState odysseyTravelHistory = new OdysseyTravelHistoryState();

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
