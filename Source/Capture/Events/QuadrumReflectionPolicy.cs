// Pure policy for rare quadrum reflections. The live component supplies the current calendar day,
// pawn id, XML tuning values, and candidate counts; this helper only answers "is this pawn's
// randomized quadrum window open?" and "are there enough important entries?" See AGENTS.md.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Testable scheduling and threshold rules for once-per-quadrum long reflections.
    /// </summary>
    public static class QuadrumReflectionPolicy
    {
        /// <summary>
        /// Returns true once this pawn's deterministic spread day has arrived inside the quadrum's
        /// final timing window. The spread is stable per pawn/quadrum, so reloads do not reshuffle it.
        /// </summary>
        public static bool IsDueForPawn(string pawnId, int quadrumIndex, int dayInQuadrum,
            int daysPerQuadrum, int timingWindowDays)
        {
            if (daysPerQuadrum <= 0)
            {
                return false;
            }

            int dueDay = DueDayInQuadrum(pawnId, quadrumIndex, daysPerQuadrum, timingWindowDays);
            return dayInQuadrum >= dueDay && dayInQuadrum < daysPerQuadrum;
        }

        /// <summary>
        /// Chooses the first day-in-quadrum on which this pawn may write. The result always falls in
        /// the last <paramref name="timingWindowDays"/> days of the quadrum.
        /// </summary>
        public static int DueDayInQuadrum(string pawnId, int quadrumIndex,
            int daysPerQuadrum, int timingWindowDays)
        {
            if (daysPerQuadrum <= 0)
            {
                return 0;
            }

            int window = TimingWindowDays(daysPerQuadrum, timingWindowDays);
            int firstWindowDay = daysPerQuadrum - window;
            return firstWindowDay + StableOffset(pawnId, quadrumIndex, window);
        }

        /// <summary>
        /// True when the quadrum has enough important diary entries to justify the longer prompt.
        /// </summary>
        public static bool HasEnoughHighValueEntries(int importantEntryCount, int minImportantEntries)
        {
            int minimum = minImportantEntries < 1 ? 1 : minImportantEntries;
            return importantEntryCount >= minimum;
        }

        private static int TimingWindowDays(int daysPerQuadrum, int timingWindowDays)
        {
            if (timingWindowDays < 1)
            {
                return 1;
            }

            return timingWindowDays > daysPerQuadrum ? daysPerQuadrum : timingWindowDays;
        }

        private static int StableOffset(string pawnId, int quadrumIndex, int window)
        {
            if (window <= 1)
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;
                string text = pawnId ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash = hash * 31 + text[i];
                }

                hash = hash * 31 + quadrumIndex;
                if (hash < 0)
                {
                    hash = ~hash;
                }

                return hash % window;
            }
        }
    }
}
