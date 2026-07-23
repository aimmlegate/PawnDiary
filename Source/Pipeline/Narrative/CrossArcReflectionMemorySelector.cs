// Pure linked-memory qualification for Narrative N4 reflections. Runtime adapters project hot
// DiaryEvent pages and compact ArchivedDiaryEntry rows into these plain candidates; this file knows
// nothing about RimWorld, Verse, save models, Defs, settings, localization, or dispatch.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>One existing diary page plus its exact, prose-free saved continuity references.</summary>
    internal sealed class CrossArcMemoryCandidate
    {
        public string eventId = string.Empty;
        public string pawnId = string.Empty;
        public int tick;
        public string date = string.Empty;
        public string text = string.Empty;
        public string generatedText = string.Empty;
        public string title = string.Empty;
        public string label = string.Empty;
        public string salience = NarrativeSalienceTokens.Minor;
        public bool reflection;
        public bool recap;
        public List<NarrativeReference> references = new List<NarrativeReference>();
    }

    /// <summary>Plain XML-copied gates and bounded candidate input for one pawn.</summary>
    internal sealed class CrossArcMemorySelectionRequest
    {
        public string pawnId = string.Empty;
        public int currentTick;
        public int eligibleAfterTick = -1;
        public int candidateScanCap = 64;
        public int memoryCap = 8;
        public int minimumLinkedMemories = 2;
        public int minimumDistinctPhases = 2;
        public int maximumSpanTicks = 3600000;
        public bool requireChangeOrConsequence = true;
        public List<string> changeOrConsequenceFacets = new List<string>();
        public List<CrossArcMemoryCandidate> candidates = new List<CrossArcMemoryCandidate>();
    }

    /// <summary>Deterministic selected memories and the exact facts used to qualify them.</summary>
    internal sealed class CrossArcMemorySelection
    {
        public List<CrossArcMemoryCandidate> selected = new List<CrossArcMemoryCandidate>();
        public List<string> sourceEventIds = new List<string>();
        public List<string> arcKeys = new List<string>();
        public int candidateCount;
        public int linkedMemoryCount;
        public int distinctPhaseCount;
        public int memorySpanTicks;
        public bool hasCoherentLink;
        public bool hasPhaseChange;
        public bool hasChangeOrConsequence;

        public bool qualified;
    }

    /// <summary>
    /// Finds one exact arc/subject-connected set. Provider/DLC identity is deliberately absent, so
    /// merely coming from different DLCs can never create a connection.
    /// </summary>
    internal static class CrossArcReflectionMemorySelector
    {
        private sealed class LinkedCandidate
        {
            public CrossArcMemoryCandidate candidate;
            public string phase = string.Empty;
            public string facet = string.Empty;
            public string arcKey = string.Empty;
        }

        private sealed class LinkGroup
        {
            public string key = string.Empty;
            public List<LinkedCandidate> candidates = new List<LinkedCandidate>();
            public CrossArcMemorySelection selection;
        }

        /// <summary>Filters, groups, qualifies, and selects one stable linked-memory set.</summary>
        public static CrossArcMemorySelection Select(CrossArcMemorySelectionRequest request)
        {
            CrossArcMemorySelection empty = new CrossArcMemorySelection();
            if (request == null || string.IsNullOrWhiteSpace(request.pawnId))
            {
                return empty;
            }

            List<CrossArcMemoryCandidate> usable = UsableCandidates(request);
            empty.candidateCount = usable.Count;
            if (usable.Count < Math.Max(2, request.minimumLinkedMemories))
            {
                return empty;
            }

            Dictionary<string, LinkGroup> groups = BuildGroups(usable);
            List<LinkGroup> qualified = new List<LinkGroup>();
            foreach (KeyValuePair<string, LinkGroup> pair in groups)
            {
                LinkGroup group = pair.Value;
                group.selection = SelectFromGroup(group, request, usable.Count);
                if (group.selection.qualified)
                {
                    qualified.Add(group);
                }
            }

            if (qualified.Count == 0)
            {
                return empty;
            }

            qualified.Sort(CompareGroups);
            return qualified[0].selection;
        }

        /// <summary>Returns the same complete memory text preference used by annual arc reflections.</summary>
        public static string MemoryText(CrossArcMemoryCandidate candidate)
        {
            if (candidate == null) return string.Empty;
            string value = !string.IsNullOrWhiteSpace(candidate.generatedText)
                ? candidate.generatedText
                : !string.IsNullOrWhiteSpace(candidate.text)
                    ? candidate.text
                    : !string.IsNullOrWhiteSpace(candidate.title)
                        ? candidate.title
                        : candidate.label;
            return (value ?? string.Empty).Trim();
        }

        private static List<CrossArcMemoryCandidate> UsableCandidates(CrossArcMemorySelectionRequest request)
        {
            List<CrossArcMemoryCandidate> ordered = new List<CrossArcMemoryCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<CrossArcMemoryCandidate> source = request.candidates ?? new List<CrossArcMemoryCandidate>();
            for (int i = 0; i < source.Count; i++)
            {
                CrossArcMemoryCandidate candidate = source[i];
                if (candidate == null
                    || string.IsNullOrWhiteSpace(candidate.eventId)
                    || !seen.Add(candidate.eventId.Trim())
                    || !string.Equals(candidate.pawnId?.Trim(), request.pawnId.Trim(), StringComparison.Ordinal)
                    || candidate.reflection
                    || candidate.recap
                    || candidate.tick <= request.eligibleAfterTick
                    || candidate.tick > request.currentTick
                    || string.IsNullOrWhiteSpace(MemoryText(candidate))
                    || !HasExactReference(candidate.references))
                {
                    continue;
                }

                ordered.Add(candidate);
            }

            ordered.Sort(CompareNewestCandidate);
            int cap = Math.Max(2, request.candidateScanCap);
            if (ordered.Count > cap)
            {
                ordered.RemoveRange(cap, ordered.Count - cap);
            }

            return ordered;
        }

        private static Dictionary<string, LinkGroup> BuildGroups(List<CrossArcMemoryCandidate> candidates)
        {
            Dictionary<string, LinkGroup> groups = new Dictionary<string, LinkGroup>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                CrossArcMemoryCandidate candidate = candidates[i];
                List<NarrativeReference> references =
                    NarrativeReferencePolicy.Unique(candidate.references ?? new List<NarrativeReference>());
                for (int r = 0; r < references.Count; r++)
                {
                    NarrativeReference reference = references[r];
                    AddGroup(groups, ArcLinkKey(reference), candidate, reference);
                    AddGroup(groups, SubjectLinkKey(reference), candidate, reference);
                }
            }

            return groups;
        }

        private static void AddGroup(
            Dictionary<string, LinkGroup> groups,
            string key,
            CrossArcMemoryCandidate candidate,
            NarrativeReference reference)
        {
            if (string.IsNullOrEmpty(key)) return;
            LinkGroup group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new LinkGroup { key = key };
                groups.Add(key, group);
            }

            for (int i = 0; i < group.candidates.Count; i++)
            {
                if (string.Equals(group.candidates[i].candidate.eventId, candidate.eventId, StringComparison.Ordinal))
                {
                    // Keep the lexically first phase/facet for a candidate that carries duplicate link rows.
                    if (CompareReference(reference, group.candidates[i]) < 0)
                    {
                        group.candidates[i].phase = Clean(reference.phase);
                        group.candidates[i].facet = Clean(reference.facet);
                        group.candidates[i].arcKey = Clean(reference.arcKey);
                    }
                    return;
                }
            }

            group.candidates.Add(new LinkedCandidate
            {
                candidate = candidate,
                phase = Clean(reference.phase),
                facet = Clean(reference.facet),
                arcKey = Clean(reference.arcKey)
            });
        }

        private static CrossArcMemorySelection SelectFromGroup(
            LinkGroup group,
            CrossArcMemorySelectionRequest request,
            int candidateCount)
        {
            CrossArcMemorySelection result = new CrossArcMemorySelection { candidateCount = candidateCount };
            List<LinkedCandidate> linked = new List<LinkedCandidate>(group.candidates);
            linked.Sort(CompareNewestLinked);
            int memoryCap = Math.Max(2, request.memoryCap);
            if (linked.Count > memoryCap)
            {
                linked.RemoveRange(memoryCap, linked.Count - memoryCap);
            }

            linked.Sort(CompareOldestLinked);
            HashSet<string> phases = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> arcs = new HashSet<string>(StringComparer.Ordinal);
            int earliest = int.MaxValue;
            int latest = int.MinValue;
            bool changeOrConsequence = false;
            for (int i = 0; i < linked.Count; i++)
            {
                LinkedCandidate row = linked[i];
                if (row?.candidate == null || !ids.Add(row.candidate.eventId)) continue;
                result.selected.Add(row.candidate);
                result.sourceEventIds.Add(row.candidate.eventId);
                if (!string.IsNullOrEmpty(row.arcKey) && arcs.Add(row.arcKey))
                {
                    result.arcKeys.Add(row.arcKey);
                }
                if (!string.IsNullOrEmpty(row.phase)) phases.Add(row.phase);
                if (ContainsOrdinal(request.changeOrConsequenceFacets, row.facet))
                {
                    changeOrConsequence = true;
                }
                earliest = Math.Min(earliest, row.candidate.tick);
                latest = Math.Max(latest, row.candidate.tick);
            }

            result.linkedMemoryCount = result.selected.Count;
            result.distinctPhaseCount = phases.Count;
            result.memorySpanTicks = earliest == int.MaxValue ? 0 : Math.Max(0, latest - earliest);
            result.hasCoherentLink = result.linkedMemoryCount >= Math.Max(2, request.minimumLinkedMemories);
            result.hasPhaseChange = result.distinctPhaseCount >= Math.Max(2, request.minimumDistinctPhases);
            result.hasChangeOrConsequence = changeOrConsequence;
            result.qualified = result.hasCoherentLink
                && result.hasPhaseChange
                && (!request.requireChangeOrConsequence || result.hasChangeOrConsequence)
                && (request.maximumSpanTicks <= 0 || result.memorySpanTicks <= request.maximumSpanTicks);
            return result;
        }

        private static int CompareGroups(LinkGroup left, LinkGroup right)
        {
            int linked = right.selection.linkedMemoryCount.CompareTo(left.selection.linkedMemoryCount);
            if (linked != 0) return linked;
            int phases = right.selection.distinctPhaseCount.CompareTo(left.selection.distinctPhaseCount);
            if (phases != 0) return phases;
            int newest = NewestTick(right.selection).CompareTo(NewestTick(left.selection));
            return newest != 0 ? newest : string.Compare(left.key, right.key, StringComparison.Ordinal);
        }

        private static int NewestTick(CrossArcMemorySelection selection)
        {
            int newest = int.MinValue;
            for (int i = 0; i < selection.selected.Count; i++)
                newest = Math.Max(newest, selection.selected[i].tick);
            return newest;
        }

        private static bool HasExactReference(List<NarrativeReference> references)
        {
            if (references == null) return false;
            for (int i = 0; i < references.Count; i++)
            {
                NarrativeReference reference = references[i];
                if (reference != null
                    && (!string.IsNullOrWhiteSpace(reference.arcKey)
                        || (!string.IsNullOrWhiteSpace(reference.subjectKind)
                            && !string.IsNullOrWhiteSpace(reference.subjectId))))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ArcLinkKey(NarrativeReference reference)
        {
            string arc = Clean(reference?.arcKey);
            return arc.Length == 0 ? string.Empty : "arc|" + arc;
        }

        private static string SubjectLinkKey(NarrativeReference reference)
        {
            string kind = Clean(reference?.subjectKind);
            string id = Clean(reference?.subjectId);
            return kind.Length == 0 || id.Length == 0 ? string.Empty : "subject|" + kind + "|" + id;
        }

        private static int CompareReference(NarrativeReference left, LinkedCandidate right)
        {
            int phase = string.Compare(Clean(left?.phase), right?.phase ?? string.Empty, StringComparison.Ordinal);
            return phase != 0
                ? phase
                : string.Compare(Clean(left?.facet), right?.facet ?? string.Empty, StringComparison.Ordinal);
        }

        private static int CompareNewestCandidate(CrossArcMemoryCandidate left, CrossArcMemoryCandidate right)
        {
            int tick = right.tick.CompareTo(left.tick);
            return tick != 0 ? tick : string.Compare(left.eventId, right.eventId, StringComparison.Ordinal);
        }

        private static int CompareNewestLinked(LinkedCandidate left, LinkedCandidate right)
        {
            return CompareNewestCandidate(left.candidate, right.candidate);
        }

        private static int CompareOldestLinked(LinkedCandidate left, LinkedCandidate right)
        {
            int tick = left.candidate.tick.CompareTo(right.candidate.tick);
            return tick != 0
                ? tick
                : string.Compare(left.candidate.eventId, right.candidate.eventId, StringComparison.Ordinal);
        }

        private static bool ContainsOrdinal(List<string> values, string wanted)
        {
            if (values == null || string.IsNullOrWhiteSpace(wanted)) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(Clean(values[i]), Clean(wanted), StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
