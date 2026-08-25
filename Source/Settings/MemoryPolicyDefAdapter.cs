// MemoryPolicyDefAdapter.cs — main-thread bridge from XML tuning to the pure M5 settings bounds.
//
// DefDatabase is a RimWorld object and never crosses into the normalizer or standalone tests. Missing
// or malformed XML is harmless: the pure normalizer repairs this detached snapshot to code fallbacks.
using Verse;

namespace PawnDiary
{
    /// <summary>Builds detached memory-settings bounds from the one knowledge tuning Def.</summary>
    internal static class MemoryPolicyDefAdapter
    {
        public static MemorySettingsBounds Bounds()
        {
            return TryBounds(out MemorySettingsBounds bounds)
                ? bounds : new MemorySettingsBounds();
        }

        /// <summary>
        /// Returns false while startup has not populated the Def database. Callers can then retain
        /// version-zero migration evidence and retry at the first post-Def lifecycle boundary.
        /// </summary>
        public static bool TryBounds(out MemorySettingsBounds bounds)
        {
            DiaryKnowledgeTuningDef tuning =
                DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail(
                    DiaryKnowledgePolicy.TuningDefName);
            if (tuning == null)
            {
                bounds = null;
                return false;
            }
            bounds = new MemorySettingsBounds
            {
                minorMinimumDays = tuning.minorMemoryLifetimeMinimumDays,
                minorDefaultDays = tuning.minorMemoryLifetimeDefaultDays,
                minorMaximumDays = tuning.minorMemoryLifetimeMaximumDays,
                regularMinimumDays = tuning.regularMemoryLifetimeMinimumDays,
                regularDefaultDays = tuning.regularMemoryLifetimeDefaultDays,
                regularMaximumDays = tuning.regularMemoryLifetimeMaximumDays,
                threadTargetMinimum = tuning.memoryThreadTargetMinimum,
                threadTargetDefault = tuning.memoryThreadTargetDefault,
                threadTargetMaximum = tuning.memoryThreadTargetMaximum,
                reuseMinimumDays = tuning.memoryReuseDaysMinimum,
                reuseDefaultDays = tuning.memoryReuseDaysDefault,
                reuseMaximumDays = tuning.memoryReuseDaysMaximum,
                revisitMinimumEntries = tuning.memoryRevisitEntryCountMinimum,
                revisitDefaultEntries = tuning.memoryRevisitEntryCountDefault,
                revisitMaximumEntries = tuning.memoryRevisitEntryCountMaximum
            };
            return true;
        }
    }
}
