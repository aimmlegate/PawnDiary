// Raid ingestion signal — the impure capture+emit half of the "colony raid" source
// (IncidentWorker.TryExecute, filtered to raid-like incidents). Replaces the old
// the old raid recorder. A raid is a colony-wide FAN-OUT: one solo entry per eligible
// colonist on the target map, with a single colony-level dedup window (peek before the loop, mark
// only after at least one entry emits — handled by the shared DiaryFanoutSignal dispatch).
//
// The raid-level facts (incident defName, raider faction, points, arrival mode, strategy) are
// computed once in the fan-out constructor and copied onto each per-pawn payload, so the pure
// per-pawn Decide stays self-contained. Pure decision + game-context + delay policy live in
// Source/Capture/Events/RaidEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony-wide raid fan-out. Built by <see cref="RaidExecutePatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>. All raid-level gates (group classification +
    /// user toggle, valid target map) run in the constructor; if any fails the fan-out yields nothing.
    /// </summary>
    internal sealed class RaidFanoutSignal : DiaryFanoutSignal
    {
        private readonly bool valid;
        private readonly Map map;
        private readonly string colonyDedupKey;

        // Shared raid facts copied onto each per-pawn payload + read by each child's Emit.
        internal string IncidentDefName { get; }
        internal string FactionId { get; }
        internal string MapId { get; }
        internal string CleanedLabel { get; }
        internal string RaiderFactionDefName { get; }
        internal string PointsText { get; }
        internal string ArrivalModeDefName { get; }
        internal string StrategyDefName { get; }
        internal string Instruction { get; }
        internal bool DelayGeneration { get; }
        internal int GenerationReadyTick { get; }

        public RaidFanoutSignal(IncidentParms parms, IncidentDef incidentDef)
        {
            // GamePlaying first (before any Find.TickManager access), mirroring the old raid recorder's
            // leading CanRecordGameplayEventNow guard.
            if (!DiaryGameComponent.GamePlaying || parms == null || incidentDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            IncidentDefName = incidentDef.defName;
            if (string.IsNullOrEmpty(IncidentDefName))
            {
                return;
            }

            ArrivalModeDefName = parms.raidArrivalMode?.defName;
            StrategyDefName = parms.raidStrategy?.defName;

            // XML owns which raid incidents count as diary-worthy. The Raid catch-all ("Raids") matches
            // everything not claimed by friendly/drop-pod/infestation subsets.
            string raidClassifierKey = RaidClassifierKey(IncidentDefName, ArrivalModeDefName, StrategyDefName);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRaid(raidClassifierKey);
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            // Raids target a single map; caravan/world raids have no FreeColonists list to fan out to.
            map = parms.target as Map;
            if (map == null || map.mapPawns == null)
            {
                map = null;
                return;
            }

            RaiderFactionDefName = parms.faction?.def?.defName;
            if (string.IsNullOrEmpty(RaiderFactionDefName))
            {
                RaiderFactionDefName = RaidEventData.FactionUnknown;
            }
            FactionId = parms.faction?.GetUniqueLoadID() ?? string.Empty;
            MapId = map.GetUniqueLoadID() ?? string.Empty;

            int points = Math.Max(0, (int)Math.Round(parms.points));
            PointsText = points.ToString(CultureInfo.InvariantCulture);

            string rawLabel = incidentDef.LabelCap.Resolve();
            CleanedLabel = !string.IsNullOrWhiteSpace(rawLabel)
                ? DiaryLineCleaner.CleanLine(rawLabel)
                : IncidentDefName;

            Instruction = InteractionGroups.InstructionForGroup(group);
            DelayGeneration = RaidEventData.ShouldDelayGeneration(
                IncidentDefName, ArrivalModeDefName, StrategyDefName, DiaryTuning.Current.raidGenerationDelayTicks);
            GenerationReadyTick = Find.TickManager.TicksGame + Math.Max(0, DiaryTuning.Current.raidGenerationDelayTicks);

            colonyDedupKey = "raid|" + IncidentDefName + "|" + map.Index + "|" + RaiderFactionDefName + "|" + PointsText;
            valid = true;
        }

        internal bool IsValid => valid;

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiaryTuning.Current.raidDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            HashSet<string> seen = new HashSet<string>();
            for (int j = 0; j < colonists.Count; j++)
            {
                Pawn pawn = colonists[j];
                if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                {
                    continue;
                }

                // A pawn could appear more than once during transitions; skip duplicates.
                string pawnId = pawn.GetUniqueLoadID();
                if (!seen.Add(pawnId))
                {
                    continue;
                }

                yield return new RaidPawnSignal(this, pawn, pawnId);
            }
        }

        /// <summary>
        /// Builds the string classifier XML groups match: the incident defName first (so old
        /// exact/token matchers still see the source), then arrival/strategy defNames when present.
        /// Moved verbatim from the old DiaryGameComponent.RaidClassifierKey.
        /// </summary>
        private static string RaidClassifierKey(string incidentDefName, string arrivalModeDefName, string strategyDefName)
        {
            List<string> parts = new List<string> { incidentDefName };
            if (!string.IsNullOrWhiteSpace(arrivalModeDefName))
            {
                parts.Add("arrival=" + arrivalModeDefName);
            }

            if (!string.IsNullOrWhiteSpace(strategyDefName))
            {
                parts.Add("strategy=" + strategyDefName);
            }

            return string.Join("; ", parts.ToArray());
        }
    }

    /// <summary>
    /// Lossless bounded owner arbitration for quick military-aid raids. Vanilla completes the
    /// RaidFriendly incident before it calls FactionPermit.Notify_Used, so the raid waits briefly for
    /// the exact permit success. Unclaimed or overflowed raids return to the ordinary fan-out path.
    /// </summary>
    internal static class QuickMilitaryAidRaidCorrelation
    {
        private sealed class PendingRaid
        {
            public RoyalQuickAidSnapshot snapshot;
            public RaidFanoutSignal signal;
        }

        private static readonly List<PendingRaid> pending = new List<PendingRaid>();
        private static readonly List<RoyalPermitUseSnapshot> recentOwners =
            new List<RoyalPermitUseSnapshot>();
        private static long nextCorrelationId;
        private static Action<RaidFanoutSignal> dispatchOverrideForTests;

        /// <summary>Stages a valid quick-aid raid, or consumes a reverse-order permit owner.</summary>
        public static bool TryStageOrSuppress(
            RaidFanoutSignal signal,
            int tick,
            RoyaltyPolicySnapshot policy)
        {
            if (signal == null || !signal.IsValid || policy == null
                || string.IsNullOrWhiteSpace(signal.FactionId)
                || string.IsNullOrWhiteSpace(signal.MapId)) return false;
            int now = Math.Max(0, tick);
            FlushExpired(now, policy);
            RoyalQuickAidSnapshot snapshot = new RoyalQuickAidSnapshot
            {
                correlationId = "royal-quick-aid-" + (++nextCorrelationId).ToString(CultureInfo.InvariantCulture),
                factionId = signal.FactionId,
                mapId = signal.MapId,
                tick = now
            };
            for (int i = recentOwners.Count - 1; i >= 0; i--)
            {
                if (!RoyalPermitPolicy.MatchesRecentOwner(
                    recentOwners[i], snapshot, policy.quickAidCorrelationTicks)) continue;
                recentOwners.RemoveAt(i);
                return true;
            }

            int cap = Clamp(policy.maximumPendingQuickAid, 1, 256, 32);
            while (pending.Count >= cap) FlushOldest();
            pending.Add(new PendingRaid { snapshot = snapshot, signal = signal });
            return true;
        }

        /// <summary>
        /// Gives one successful military-aid permit ownership of its matching staged raid. If a
        /// compatibility mod reports the permit first, a bounded recent token suppresses its raid.
        /// </summary>
        public static bool Claim(
            RoyalPermitUseSnapshot use,
            int tick,
            RoyaltyPolicySnapshot policy)
        {
            if (use == null || policy == null
                || use.permitFamilyToken != RoyalPermitFamilyTokens.MilitaryAid) return false;
            int now = Math.Max(0, tick);
            FlushExpired(now, policy);
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (!RoyalPermitPolicy.MatchesQuickAid(
                    pending[i].snapshot, use, now, policy.quickAidCorrelationTicks)) continue;
                pending.RemoveAt(i);
                return true;
            }

            int cap = Clamp(policy.maximumRecentQuickAidOwners, 1, 256, 32);
            while (recentOwners.Count >= cap) recentOwners.RemoveAt(0);
            recentOwners.Add(use);
            return false;
        }

        /// <summary>Returns expired staged raids to the existing generic RaidSignal owner.</summary>
        public static void FlushExpired(int tick, RoyaltyPolicySnapshot policy)
        {
            if (policy == null) return;
            int now = Math.Max(0, tick);
            List<PendingRaid> expired = null;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (!RoyalPermitPolicy.QuickAidExpired(
                    pending[i].snapshot.tick, now, policy.quickAidCorrelationTicks)) continue;
                if (expired == null) expired = new List<PendingRaid>();
                expired.Add(pending[i]);
                pending.RemoveAt(i);
            }
            if (expired != null)
            {
                expired.Sort(ComparePending);
                for (int i = 0; i < expired.Count; i++) DispatchFallback(expired[i].signal);
            }
            for (int i = recentOwners.Count - 1; i >= 0; i--)
            {
                long elapsed = (long)now - recentOwners[i].tick;
                if (elapsed < 0 || elapsed >= Math.Max(1, policy.quickAidCorrelationTicks))
                    recentOwners.RemoveAt(i);
            }
        }

        /// <summary>Flushes every staged raid before save so transient state never loses a story.</summary>
        public static void FlushAll()
        {
            while (pending.Count > 0) FlushOldest();
            recentOwners.Clear();
        }

        /// <summary>Drops cross-game state after a new-game/load/menu lifecycle boundary.</summary>
        public static void Reset()
        {
            pending.Clear();
            recentOwners.Clear();
            nextCorrelationId = 0;
            dispatchOverrideForTests = null;
        }

        internal static int PendingCountForTests => pending.Count;
        internal static int RecentOwnerCountForTests => recentOwners.Count;
        internal static bool HasState => pending.Count > 0 || recentOwners.Count > 0;

        /// <summary>
        /// Loaded-test seam that observes fallback ownership without fanning out into a developer's
        /// real colony. Production never sets it, and every lifecycle reset clears it.
        /// </summary>
        internal static void SetDispatchOverrideForTests(Action<RaidFanoutSignal> callback)
        {
            dispatchOverrideForTests = callback;
        }

        private static void FlushOldest()
        {
            if (pending.Count == 0) return;
            int oldest = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (pending[i].snapshot.tick < pending[oldest].snapshot.tick
                    || (pending[i].snapshot.tick == pending[oldest].snapshot.tick
                        && string.CompareOrdinal(
                            pending[i].snapshot.correlationId,
                            pending[oldest].snapshot.correlationId) < 0)) oldest = i;
            }
            RaidFanoutSignal signal = pending[oldest].signal;
            pending.RemoveAt(oldest);
            DispatchFallback(signal);
        }

        private static int ComparePending(PendingRaid left, PendingRaid right)
        {
            int ticks = left.snapshot.tick.CompareTo(right.snapshot.tick);
            return ticks != 0 ? ticks : string.CompareOrdinal(
                left.snapshot.correlationId, right.snapshot.correlationId);
        }

        private static void DispatchFallback(RaidFanoutSignal signal)
        {
            Action<RaidFanoutSignal> callback = dispatchOverrideForTests;
            if (callback != null) callback(signal);
            else DiaryEvents.Submit(signal);
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value >= minimum && value <= maximum ? value : fallback;
        }
    }

    /// <summary>
    /// One colonist's solo raid entry. Created by <see cref="RaidFanoutSignal.PerPawnSignals"/>; carries
    /// the shared raid facts via its parent fan-out so the pure per-pawn Decide is self-contained.
    /// </summary>
    internal sealed class RaidPawnSignal : DiarySignal
    {
        private readonly RaidFanoutSignal raid;
        private readonly Pawn pawn;
        private readonly RaidEventData payload;

        public RaidPawnSignal(RaidFanoutSignal raid, Pawn pawn, string pawnId)
        {
            this.raid = raid;
            this.pawn = pawn;
            payload = new RaidEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = raid.IncidentDefName,
                Label = raid.CleanedLabel,
                FactionDefName = raid.RaiderFactionDefName,
                Points = raid.PointsText,
                ArrivalModeDefName = raid.ArrivalModeDefName,
                StrategyDefName = raid.StrategyDefName,
            };
        }

        public override DiaryEventData Payload => payload;

        // Raid-level gates already passed in the fan-out constructor; each eligible colonist records.
        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true, userEnabled: true, signalEnabled: true, ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string perPawnContext = RaidEventData.BuildGameContext(
                raid.IncidentDefName, raid.CleanedLabel, raid.RaiderFactionDefName, raid.PointsText,
                raid.ArrivalModeDefName, raid.StrategyDefName);

            string text = "PawnDiary.Event.Raid".Translate(pawn.LabelShortCap).Resolve();

            DiaryEvent raidEvent = sink.AddSoloEvent(pawn, null, raid.IncidentDefName, raid.CleanedLabel,
                text, raid.Instruction, perPawnContext);
            if (raidEvent == null)
            {
                return;
            }

            if (raid.DelayGeneration)
            {
                sink.DelaySolo(raidEvent, DiaryEvent.InitiatorRole, raid.GenerationReadyTick);
            }
            else
            {
                sink.QueueSolo(raidEvent, DiaryEvent.InitiatorRole);
            }
        }
    }
}
