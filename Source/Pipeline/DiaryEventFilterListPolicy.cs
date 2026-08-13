// Pure search/group/collapse projection for the Events settings list. The RimWorld UI supplies
// already-localized row and domain labels, then renders this detached projection. Keeping the list
// math here makes scroll-height and search behavior testable without Unity immediate-mode GUI.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Plain UI-search facts for one settings-visible event group.</summary>
    internal sealed class DiaryEventFilterListRowSnapshot
    {
        public string groupKey = string.Empty;
        public string label = string.Empty;
        public string domainToken = string.Empty;
        public string domainLabel = string.Empty;
    }

    /// <summary>One domain heading and the ordered group keys visible beneath it.</summary>
    internal sealed class DiaryEventFilterListSection
    {
        public string domainToken = string.Empty;
        public string domainLabel = string.Empty;
        public bool collapsed;
        public int totalCount;
        public readonly List<string> visibleGroupKeys = new List<string>();
    }

    /// <summary>Builds the stable Events-tab list projection from detached, already-sorted rows.</summary>
    internal static class DiaryEventFilterListPolicy
    {
        /// <summary>
        /// Groups rows by first-seen domain. Search matches label, stable key, or localized domain and
        /// temporarily expands matching sections without discarding the caller's stored collapse state.
        /// </summary>
        public static List<DiaryEventFilterListSection> Build(
            IEnumerable<DiaryEventFilterListRowSnapshot> rows,
            string search,
            ISet<string> collapsedDomainTokens)
        {
            List<DiaryEventFilterListSection> result = new List<DiaryEventFilterListSection>();
            Dictionary<string, DiaryEventFilterListSection> byDomain =
                new Dictionary<string, DiaryEventFilterListSection>(StringComparer.OrdinalIgnoreCase);
            string needle = (search ?? string.Empty).Trim();
            bool searching = needle.Length > 0;
            if (rows == null)
            {
                return result;
            }

            foreach (DiaryEventFilterListRowSnapshot row in rows)
            {
                string key = (row?.groupKey ?? string.Empty).Trim();
                string domain = (row?.domainToken ?? string.Empty).Trim();
                if (key.Length == 0 || domain.Length == 0)
                {
                    continue;
                }

                DiaryEventFilterListSection section;
                if (!byDomain.TryGetValue(domain, out section))
                {
                    section = new DiaryEventFilterListSection
                    {
                        domainToken = domain,
                        domainLabel = string.IsNullOrWhiteSpace(row.domainLabel)
                            ? domain
                            : row.domainLabel.Trim(),
                        collapsed = !searching && Contains(collapsedDomainTokens, domain)
                    };
                    byDomain[domain] = section;
                    result.Add(section);
                }

                section.totalCount++;
                if ((!searching || Matches(row, needle)) && !section.collapsed)
                {
                    section.visibleGroupKeys.Add(key);
                }
            }

            if (searching)
            {
                result.RemoveAll(section => section.visibleGroupKeys.Count == 0);
            }

            return result;
        }

        private static bool Matches(DiaryEventFilterListRowSnapshot row, string needle)
        {
            return Contains(row.label, needle)
                || Contains(row.groupKey, needle)
                || Contains(row.domainLabel, needle)
                || Contains(row.domainToken, needle);
        }

        private static bool Contains(string text, string needle)
        {
            return (text ?? string.Empty).IndexOf(
                needle ?? string.Empty,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Contains(ISet<string> values, string wanted)
        {
            if (values == null)
            {
                return false;
            }

            foreach (string value in values)
            {
                if (string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
