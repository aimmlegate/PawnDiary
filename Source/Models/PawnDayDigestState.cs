// Saved per-pawn/per-day pacing row for Quality Wave B6 (digest lines + low-salience soft cap).
//
// One row per diary-eligible pawn per in-game day. It carries the two things B6 must remember across
// a reload: how many low-value pages that pawn has already written today (so reloading mid-day cannot
// reset the pacing), and the small moments that were folded away instead of becoming pages (so the
// evening reflection can still mention them).
//
// Rows for earlier days are discarded at the day rollover, so this collection stays bounded to
// roughly one row per colonist. Save/load is additive: an old save simply loads an empty list, and
// Normalize() turns missing/junk values into "nothing recorded yet" rather than into fake data.
// New to C#/RimWorld? See AGENTS.md ("IExposable").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One buffered small moment, persisted with the game.</summary>
    public class DayDigestRecord : IExposable
    {
        /// <summary>Game tick the moment happened.</summary>
        public int tick;

        /// <summary>Stable source token — see <see cref="DigestPacingPolicy"/>'s SourceKind constants.</summary>
        public string sourceKind;

        /// <summary>The cleaned, already-localized one-line cue.</summary>
        public string line;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref sourceKind, "sourceKind");
            Scribe_Values.Look(ref line, "line");
        }
    }

    /// <summary>
    /// One pawn's pacing and digest state for one in-game day.
    /// </summary>
    public class PawnDayDigestState : IExposable
    {
        // Defensive cap on one saved line. Digest lines are one-sentence cues; anything longer is a
        // sign of unexpected input, and the reflection truncates for the prompt anyway.
        private const int MaxLineLength = 240;

        public string pawnId;
        public int day;

        /// <summary>How many low-salience pages this pawn has already written today.</summary>
        public int lowSalienceCount;

        /// <summary>Moments folded away by the soft cap, oldest first.</summary>
        public List<DayDigestRecord> lines = new List<DayDigestRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref day, "day");
            Scribe_Values.Look(ref lowSalienceCount, "lowSalienceCount");
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        /// <summary>
        /// Turns an absent or hand-edited row into a safe one: no nulls, no negative counts, no blank
        /// or duplicated lines. Called on load and before the row is used, so the rest of the session
        /// never has to null-check.
        /// </summary>
        public void Normalize()
        {
            pawnId = (pawnId ?? string.Empty).Trim();
            if (day < 0)
            {
                day = 0;
            }

            if (lowSalienceCount < 0)
            {
                lowSalienceCount = 0;
            }

            if (lines == null)
            {
                lines = new List<DayDigestRecord>();
                return;
            }

            List<DayDigestRecord> kept = new List<DayDigestRecord>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                DayDigestRecord record = lines[i];
                if (record == null)
                {
                    continue;
                }

                record.sourceKind = (record.sourceKind ?? string.Empty).Trim();
                string line = (record.line ?? string.Empty).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Length > MaxLineLength)
                {
                    line = TextTruncation.SafePrefix(line, MaxLineLength);
                }

                record.line = line;
                if (record.tick < 0)
                {
                    record.tick = 0;
                }

                if (ContainsLine(kept, line))
                {
                    continue;
                }

                kept.Add(record);
            }

            lines = kept;
        }

        /// <summary>Drops the buffered moments without touching the day's pacing count.</summary>
        public void ClearLines()
        {
            if (lines == null)
            {
                lines = new List<DayDigestRecord>();
                return;
            }

            lines.Clear();
        }

        private static bool ContainsLine(List<DayDigestRecord> records, string line)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].line, line, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
