// Pure memory selection for pawn arc reflections. Runtime adapters project hot DiaryEvents and
// compact ArchivedDiaryEntry rows into ArcMemoryCandidate values; this selector filters and samples
// them without knowing about RimWorld, save models, Defs, or settings.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One diary page that can become evidence for an arc reflection.
    /// </summary>
    public class ArcMemoryCandidate
    {
        public string eventId;
        public string pawnId;
        public string povRole;
        public int tick;
        public int year;
        public string date;
        public string defName;
        public string domain;
        public string groupKey;
        public string label;
        public string text;
        public string generatedText;
        public string title;
        public bool important;
        public bool reflection;
        public bool deathDescription;
        public bool sameQuadrum;
        public bool progression;
        public bool highStakes;
    }

    /// <summary>
    /// Selector input. All numbers are copied from XML/default tuning by the runtime caller.
    /// </summary>
    public class ArcMemorySelectionRequest
    {
        public List<ArcMemoryCandidate> candidates = new List<ArcMemoryCandidate>();
        public List<string> recentlyUsedEventIds = new List<string>();
        public int currentYear;
        public int maxMemories = 8;
        public int minMemories = 4;
        public int sameDomainGroupCap = 2;
        public int seed = 1;
    }

    /// <summary>
    /// Selector output plus diagnostics.
    /// </summary>
    public class ArcMemorySelectionResult
    {
        public List<ArcMemoryCandidate> selected = new List<ArcMemoryCandidate>();
        public int candidateCount;
        public bool hasEnoughMemories;
    }

    /// <summary>
    /// Filters and weighted-samples existing diary pages for an arc prompt.
    /// </summary>
    public static class ArcReflectionMemorySelector
    {
        public static ArcMemorySelectionResult Select(ArcMemorySelectionRequest request)
        {
            request = request ?? new ArcMemorySelectionRequest();
            List<ArcMemoryCandidate> pool = FilterCandidates(request);
            ArcMemorySelectionResult result = new ArcMemorySelectionResult
            {
                candidateCount = pool.Count
            };

            int max = Math.Max(1, request.maxMemories);
            int cap = Math.Max(1, request.sameDomainGroupCap);
            Random random = new Random(request.seed);
            Dictionary<string, int> groupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            while (result.selected.Count < max && pool.Count > 0)
            {
                int picked = PickWeighted(pool, random);
                ArcMemoryCandidate candidate = pool[picked];
                pool.RemoveAt(picked);

                string key = GroupCapKey(candidate);
                int currentCount;
                groupCounts.TryGetValue(key, out currentCount);
                if (currentCount >= cap)
                {
                    continue;
                }

                groupCounts[key] = currentCount + 1;
                result.selected.Add(candidate);
            }

            result.selected.Sort((left, right) => left.tick.CompareTo(right.tick));
            result.hasEnoughMemories = result.selected.Count >= Math.Max(1, request.minMemories);
            return result;
        }

        public static string MemoryText(ArcMemoryCandidate candidate, int maxChars)
        {
            if (candidate == null)
            {
                return string.Empty;
            }

            string text = !string.IsNullOrWhiteSpace(candidate.generatedText)
                ? candidate.generatedText
                : !string.IsNullOrWhiteSpace(candidate.text)
                    ? candidate.text
                    : !string.IsNullOrWhiteSpace(candidate.title)
                        ? candidate.title
                        : candidate.label;
            text = text ?? string.Empty;
            text = text.Trim();
            return text.Length > maxChars
                ? TextTruncation.SafePrefix(text, Math.Max(0, maxChars)).TrimEnd() + "..."
                : text;
        }

        private static List<ArcMemoryCandidate> FilterCandidates(ArcMemorySelectionRequest request)
        {
            HashSet<string> recentlyUsed = new HashSet<string>(
                request.recentlyUsedEventIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            List<ArcMemoryCandidate> result = new List<ArcMemoryCandidate>();
            if (request.candidates == null)
            {
                return result;
            }

            for (int i = 0; i < request.candidates.Count; i++)
            {
                ArcMemoryCandidate candidate = request.candidates[i];
                if (candidate == null
                    || string.IsNullOrWhiteSpace(candidate.eventId)
                    || recentlyUsed.Contains(candidate.eventId)
                    || candidate.reflection
                    || candidate.deathDescription)
                {
                    continue;
                }

                if (request.currentYear > 0 && candidate.year > 0 && candidate.year != request.currentYear)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(MemoryText(candidate, 220)))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        private static int PickWeighted(List<ArcMemoryCandidate> pool, Random random)
        {
            int picked = pool.Count - 1;
            double total = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                total += Math.Max(0.0001, Weight(pool[i]));
            }

            double roll = random.NextDouble() * total;
            double acc = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += Math.Max(0.0001, Weight(pool[i]));
                if (roll <= acc)
                {
                    picked = i;
                    break;
                }
            }

            return picked;
        }

        private static double Weight(ArcMemoryCandidate candidate)
        {
            double weight = 10;
            if (candidate.important) weight += 20;
            if (candidate.sameQuadrum) weight += 10;
            if (!string.IsNullOrWhiteSpace(candidate.generatedText)) weight += 5;
            if (candidate.progression) weight += 20;
            if (candidate.highStakes) weight += 15;
            return weight;
        }

        private static string GroupCapKey(ArcMemoryCandidate candidate)
        {
            string domain = string.IsNullOrWhiteSpace(candidate?.domain) ? "unknown" : candidate.domain;
            string group = string.IsNullOrWhiteSpace(candidate?.groupKey) ? candidate?.defName : candidate.groupKey;
            return domain + "|" + (group ?? string.Empty);
        }
    }
}
