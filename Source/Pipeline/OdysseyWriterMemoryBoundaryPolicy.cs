// Pure per-writer memory boundaries for an active Odyssey gravship journey. The shared journey is
// world truth and must remain available to other crew; this helper only prevents a Brainwiped pawn
// from later narrating origin, duration, or launch-quality facts observed before their reset.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Normalizes exact excluded pawn IDs and projects eligible landing writers.</summary>
    internal static class OdysseyWriterMemoryBoundaryPolicy
    {
        // Defensive save ceiling, not player-facing policy. A gravship cannot approach this number of
        // writers, but the bound keeps a corrupt hand-edited list from growing without limit.
        public const int HardMaximumExcludedWriters = 2048;
        private const int HardMaximumPawnIdCharacters = 200;

        /// <summary>
        /// Adds one exact pawn to this active journey's memory boundary. The returned list is detached,
        /// de-duplicated, stable, and safe to deep-copy into Scribe state.
        /// </summary>
        public static List<string> AddExcludedWriter(IList<string> source, string pawnId)
        {
            List<string> normalized = NormalizeExcludedWriterIds(source);
            string id = CleanPawnId(pawnId);
            if (id.Length > 0 && !normalized.Contains(id) && normalized.Count < HardMaximumExcludedWriters)
            {
                normalized.Add(id);
                normalized.Sort(StringComparer.Ordinal);
            }

            return normalized;
        }

        /// <summary>Repairs a loaded exclusion list without guessing near/prefix pawn identities.</summary>
        public static List<string> NormalizeExcludedWriterIds(IList<string> source)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count && result.Count < HardMaximumExcludedWriters; i++)
                {
                    string id = CleanPawnId(source[i]);
                    if (id.Length > 0 && seen.Add(id)) result.Add(id);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// Returns landing candidates whose exact pawn IDs are outside this journey's memory boundary.
        /// Candidate objects are detached already, so preserving their references cannot expose game state.
        /// </summary>
        public static List<OdysseyWriterCandidate> ExcludeWriters(
            IList<OdysseyWriterCandidate> writers,
            IList<string> excludedWriterPawnIds)
        {
            HashSet<string> excluded = new HashSet<string>(
                NormalizeExcludedWriterIds(excludedWriterPawnIds),
                StringComparer.Ordinal);
            List<OdysseyWriterCandidate> result = new List<OdysseyWriterCandidate>();
            if (writers == null) return result;

            for (int i = 0; i < writers.Count; i++)
            {
                OdysseyWriterCandidate writer = writers[i];
                string writerId = CleanPawnId(writer?.pawnId);
                if (writer != null && writerId.Length > 0 && !excluded.Contains(writerId))
                {
                    result.Add(writer);
                }
            }

            return result;
        }

        private static string CleanPawnId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            if (cleaned.Length == 0 || cleaned.IndexOf('|') >= 0
                || cleaned.IndexOf(';') >= 0 || cleaned.IndexOf('=') >= 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < cleaned.Length; i++)
            {
                if (char.IsControl(cleaned[i])) return string.Empty;
            }

            // Never truncate identity. Two modded IDs can share an arbitrarily long prefix; collapsing
            // both to the cap would let one pawn's Brainwipe exclude the other from the landing POV.
            return cleaned.Length <= HardMaximumPawnIdCharacters ? cleaned : string.Empty;
        }
    }
}
