// Quality Wave H1, impure half: reads RimWorld's combat log and turns one raid's fight into plain
// candidate "beats" for the diary prompt. Every decision about which beats survive, how long to keep
// waiting, and how the result is stored lives in the pure Source/Pipeline/BattleBeatsPolicy.cs — this
// file only collects facts. Main thread only (it touches Find.BattleLog and Find.TickManager).
//
// TWO TICK FRAMES, verified against the 1.6 assemblies — this is the trap in this feature:
//   * LogEntry.Tick and LogEntry.Timestamp BOTH return the private ticksAbs field, so every combat-log
//     tick is an ABSOLUTE tick, and Battle.LastEntryTimestamp inherits that frame.
//   * Battle.CreationTimestamp is assigned from Find.TickManager.TicksGame, so the same class mixes
//     frames. We therefore never read CreationTimestamp; all bounds come from entry ticks.
// Everything leaving this file is converted to the GAME frame, because that is what DiaryEvent.tick
// and the generation queue use.
//
// New to C#/RimWorld? See AGENTS.md. Comparable impure collector: Source/Generation/DlcContext.cs.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// What one mining attempt learned about a raid. All ticks are GAME ticks.
    /// </summary>
    internal sealed class BattleBeatsInspection
    {
        /// <summary>True when a battle involving both the POV pawn and the raider faction was found.</summary>
        public bool battleFound;

        /// <summary>Game tick of the newest log entry about the POV pawn, or 0 when none was found.</summary>
        public int latestRelevantGameTick;

        /// <summary>Plain candidate beats, already POV-rendered and cleaned. Never null.</summary>
        public List<BattleBeatCandidate> candidates = new List<BattleBeatCandidate>();
    }

    /// <summary>
    /// Collects raid combat beats from <see cref="Find.BattleLog"/>. Fail-soft everywhere: a pruned
    /// log, a modded entry type, or a grammar-resolution failure simply yields fewer beats.
    /// </summary>
    internal static class BattleBeatsBuilder
    {
        // RimWorld marks these damage-result fields "protected", so there is no public way to tell a
        // landed hit from a deflection. AccessTools caches the lookup and returns null (with one
        // engine warning) if a future version renames them; every read below null-checks, so the worst
        // case is that all damage entries classify as "other" and scoring degrades gracefully.
        private static readonly System.Reflection.FieldInfo DeflectedField =
            AccessTools.Field(typeof(LogEntry_DamageResult), "deflected");

        private static readonly System.Reflection.FieldInfo DamagedPartsField =
            AccessTools.Field(typeof(LogEntry_DamageResult), "damagedParts");

        /// <summary>
        /// Examines the newest battles for one raid and returns plain facts about the POV pawn's fight.
        /// <paramref name="raidGameTick"/> and <paramref name="nowGameTick"/> are game ticks; the
        /// combat log's absolute ticks are converted internally.
        /// </summary>
        public static BattleBeatsInspection Inspect(
            Pawn pov,
            string raiderFactionDefName,
            int raidGameTick,
            int nowGameTick,
            DiaryTuningDef tuning)
        {
            BattleBeatsInspection inspection = new BattleBeatsInspection();
            if (pov == null || tuning == null || !tuning.battleBeatsEnabled
                || string.IsNullOrWhiteSpace(raiderFactionDefName)
                || string.Equals(raiderFactionDefName, PawnDiary.Capture.RaidEventData.FactionUnknown,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Faction identity is part of ownership. Treating "unknown" as a wildcard can attach
                // the POV's unrelated battle (including a concurrent infestation) to this raid page.
                return inspection;
            }

            List<Battle> battles = Find.BattleLog?.Battles;
            if (battles == null || battles.Count == 0)
            {
                return inspection;
            }

            // Absolute-minus-game offset. Computed once so every entry comparison below stays in one
            // frame even if the tick manager advances mid-scan.
            TickManager ticks = Find.TickManager;
            if (ticks == null)
            {
                return inspection;
            }

            int absOffset = ticks.TicksAbs - ticks.TicksGame;
            int graceTicks = Math.Max(0, tuning.battleBeatsGraceTicks);
            int earliestAbsTick = raidGameTick + absOffset - graceTicks;
            int latestAbsTick = nowGameTick + absOffset;
            int maxBattles = Math.Max(1, tuning.battleBeatsScanBackBattles);
            int maxEntries = Math.Max(1, tuning.battleBeatsScanBackEntries);

            // BattleLog.Add inserts each new battle at index 0, so this list is newest-first.
            int scanned = 0;
            for (int i = 0; i < battles.Count && scanned < maxBattles; i++)
            {
                Battle battle = battles[i];
                if (battle == null || battle.Entries == null || battle.Entries.Count == 0)
                {
                    continue;
                }

                scanned++;

                // Battle.Concerns is an O(1) hash lookup over the participants RimWorld already
                // tracked, so an unrelated concurrent fight is rejected before any entry work.
                if (!battle.Concerns(pov))
                {
                    continue;
                }

                // A battle whose newest entry predates the raid belongs to an earlier fight.
                if (battle.LastEntryTimestamp < earliestAbsTick)
                {
                    continue;
                }

                if (TryCollectBattle(
                    battle, pov, raiderFactionDefName, earliestAbsTick, latestAbsTick,
                    absOffset, maxEntries, inspection))
                {
                    // Requiring the POV pawn AND a raider-faction pawn in the same battle stops a
                    // concurrent same-faction fight elsewhere on the map from stealing ownership.
                    return inspection;
                }
            }

            return inspection;
        }

        /// <summary>
        /// Scans one battle newest-first. Returns true when it belongs to this raid: it must contain
        /// at least one in-window entry about the POV pawn and at least one pawn of the raider faction.
        /// On a false result the inspection is left untouched for the next battle.
        /// </summary>
        private static bool TryCollectBattle(
            Battle battle,
            Pawn pov,
            string raiderFactionDefName,
            int earliestAbsTick,
            int latestAbsTick,
            int absOffset,
            int maxEntries,
            BattleBeatsInspection inspection)
        {
            List<BattleBeatCandidate> candidates = new List<BattleBeatCandidate>();
            bool raiderSeen = false;
            int latestPovAbsTick = int.MinValue;

            List<LogEntry> entries = battle.Entries;
            int scanned = 0;
            // Battle.Add inserts at index 0 too, so entries are newest-first; index doubles as the
            // stable tie-break the pure selector needs.
            for (int i = 0; i < entries.Count && scanned < maxEntries; i++)
            {
                LogEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                scanned++;

                int entryAbsTick = entry.Tick;
                if (entryAbsTick < earliestAbsTick || entryAbsTick > latestAbsTick)
                {
                    continue;
                }

                if (!raiderSeen)
                {
                    raiderSeen = ConcernsFaction(entry, raiderFactionDefName);
                }

                if (!ConcernsSafely(entry, pov))
                {
                    continue;
                }

                if (entryAbsTick > latestPovAbsTick)
                {
                    latestPovAbsTick = entryAbsTick;
                }

                string text = RenderFromPov(entry, pov);
                if (text.Length == 0)
                {
                    continue;
                }

                candidates.Add(new BattleBeatCandidate
                {
                    tick = entryAbsTick - absOffset,
                    kind = ClassifyEntry(entry),
                    text = text,
                    // Later entries sit at LOWER indexes in this newest-first list, so invert the index
                    // to give the pure selector an "order within the battle" that grows with time.
                    sequence = entries.Count - i
                });
            }

            if (!raiderSeen || latestPovAbsTick == int.MinValue)
            {
                return false;
            }

            inspection.battleFound = true;
            inspection.latestRelevantGameTick = latestPovAbsTick - absOffset;
            inspection.candidates = candidates;
            return true;
        }

        /// <summary>
        /// Maps one log entry onto a stable kind token for the pure scorer. Type checks only, so no
        /// DLC or modded def is referenced; anything unrecognized becomes the lowest-scoring kind.
        /// </summary>
        private static string ClassifyEntry(LogEntry entry)
        {
            // A state transition is RimWorld's "X was downed" / "X died" record — the strongest beat.
            if (entry is BattleLogEntry_StateTransition)
            {
                return BattleBeatsPolicy.KindTransition;
            }

            LogEntry_DamageResult damage = entry as LogEntry_DamageResult;
            if (damage == null)
            {
                return BattleBeatsPolicy.KindOther;
            }

            try
            {
                if (DeflectedField != null && (bool)DeflectedField.GetValue(damage))
                {
                    return BattleBeatsPolicy.KindDeflected;
                }

                if (DamagedPartsField != null)
                {
                    System.Collections.ICollection parts =
                        DamagedPartsField.GetValue(damage) as System.Collections.ICollection;
                    return parts != null && parts.Count > 0
                        ? BattleBeatsPolicy.KindHit
                        : BattleBeatsPolicy.KindMiss;
                }
            }
            catch (Exception)
            {
                // A renamed or retyped field must never break raid pages; fall through to "other".
            }

            return BattleBeatsPolicy.KindOther;
        }

        /// <summary>
        /// True when the entry involves a pawn belonging to the raid's faction. Uses the public
        /// GetConcerns list, because a Battle exposes no faction accessor in 1.6.
        /// </summary>
        private static bool ConcernsFaction(LogEntry entry, string factionDefName)
        {
            try
            {
                IEnumerable<Thing> concerns = entry.GetConcerns();
                if (concerns == null)
                {
                    return false;
                }

                foreach (Thing thing in concerns)
                {
                    Pawn pawn = thing as Pawn;
                    string defName = pawn?.Faction?.def?.defName;
                    if (!string.IsNullOrEmpty(defName)
                        && string.Equals(defName, factionDefName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // A modded entry with a broken concerns list simply fails to prove faction membership.
            }

            return false;
        }

        private static bool ConcernsSafely(LogEntry entry, Pawn pov)
        {
            try
            {
                return entry.Concerns(pov);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Renders one entry as the POV pawn saw it. forceLog stays false — the same value the combat
        /// log tab passes — because LogEntry caches its rendered string per POV without keying on that
        /// flag, so a forced render here would change what the player later reads in-game.
        /// RimWorld's own ToGameStringFromPOV already wraps its grammar resolution in
        /// Rand.PushState/PopState, so this call cannot perturb the gameplay RNG stream.
        /// </summary>
        private static string RenderFromPov(LogEntry entry, Pawn pov)
        {
            try
            {
                return DiaryLineCleaner.CleanLine(entry.ToGameStringFromPOV(pov, false));
            }
            catch (Exception)
            {
                // Grammar resolution can throw on a malformed modded rule pack; skip that one beat.
                return string.Empty;
            }
        }
    }
}
