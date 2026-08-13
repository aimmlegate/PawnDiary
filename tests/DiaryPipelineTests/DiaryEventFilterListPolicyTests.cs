// Pure list-projection tests for Events-tab search and domain collapse. Pixel layout remains a manual
// RimWorld check; these tests pin which headings and rows the immediate-mode adapter must render.
using System.Collections.Generic;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryEventFilterListPolicy()
        {
            List<DiaryEventFilterListRowSnapshot> rows = new List<DiaryEventFilterListRowSnapshot>
            {
                FilterRow("smalltalk", "Small talk", "Interaction", "Social interactions"),
                FilterRow("insults", "Insults", "Interaction", "Social interactions"),
                FilterRow("raid", "Raid", "Raid", "Threats and raids"),
                FilterRow("raidDropPod", "Drop-pod raid", "Raid", "Threats and raids")
            };
            HashSet<string> collapsed = new HashSet<string> { "Interaction" };

            List<DiaryEventFilterListSection> ordinary =
                DiaryEventFilterListPolicy.Build(rows, string.Empty, collapsed);
            AssertEqual("event list has two domain sections", 2, ordinary.Count);
            AssertTrue("stored interaction collapse is applied", ordinary[0].collapsed);
            AssertEqual("collapsed domain keeps total count", 2, ordinary[0].totalCount);
            AssertEqual("collapsed domain emits no rows", 0, ordinary[0].visibleGroupKeys.Count);
            AssertEqual("expanded raid emits both rows", 2, ordinary[1].visibleGroupKeys.Count);

            List<DiaryEventFilterListSection> keySearch =
                DiaryEventFilterListPolicy.Build(rows, "dropPod", collapsed);
            AssertEqual("search removes domains without matches", 1, keySearch.Count);
            AssertEqual("stable key search finds one row", "raidDropPod",
                keySearch[0].visibleGroupKeys[0]);

            List<DiaryEventFilterListSection> domainSearch =
                DiaryEventFilterListPolicy.Build(rows, "social", collapsed);
            AssertEqual("localized domain search restores collapsed domain", 1, domainSearch.Count);
            AssertTrue("search temporarily expands stored collapse", !domainSearch[0].collapsed);
            AssertEqual("domain search exposes both matching-domain rows", 2,
                domainSearch[0].visibleGroupKeys.Count);

            List<DiaryEventFilterListSection> noMatches =
                DiaryEventFilterListPolicy.Build(rows, "not present", collapsed);
            AssertEqual("no-match search yields no headings", 0, noMatches.Count);
            AssertTrue("projection tolerates null rows",
                DiaryEventFilterListPolicy.Build(null, null, null).Count == 0);
        }

        private static DiaryEventFilterListRowSnapshot FilterRow(
            string key,
            string label,
            string domain,
            string domainLabel)
        {
            return new DiaryEventFilterListRowSnapshot
            {
                groupKey = key,
                label = label,
                domainToken = domain,
                domainLabel = domainLabel
            };
        }
    }
}
