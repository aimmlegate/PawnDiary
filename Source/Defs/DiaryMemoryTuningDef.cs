// XML boundary for the pawn memory subsystem (design/MEMORY_SYSTEM_DESIGN.md §11). RimWorld loads
// this singleton Def at startup, while the pure extraction/selector/planner stay pure by receiving
// a copied MemoryPolicySnapshot instead of this live Verse Def. Numeric/table defaults match the
// shipped DiaryMemoryTuningDef.xml. Natural-language memoryContextInstruction intentionally falls
// back to blank in code, so a missing Def omits optional guidance instead of hardcoding English.
//
// STATUS: nothing calls DiaryMemoryPolicy.Snapshot() yet — the capture/recall appliers that would
// snapshot it are deliberately not wired in.
//
// New to C#/RimWorld? See AGENTS.md ("XML Defs" and "DLC-safety"). All fields are plain tuning
// data — no DLC Def references anywhere; DLC events flow through the same colorCue/gameContext
// string matchers, which simply never fire without the DLC.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One colorCue -> base importance row authored in DiaryMemoryTuningDef.xml.</summary>
    public class DiaryMemoryCueImportanceDef
    {
        public string cue;
        public float importance;
    }

    /// <summary>One colorCue -> tag set row authored in XML.</summary>
    public class DiaryMemoryCueTagsDef
    {
        public string cue;
        public List<string> tags;
    }

    /// <summary>One gameContext marker ("key=" or "key=value") -> tag set row authored in XML.</summary>
    public class DiaryMemoryContextMarkerTagsDef
    {
        public string marker;
        public List<string> tags;
    }

    /// <summary>One age -> label rendering band authored in XML. Labels are prompt prose (DefInjected).</summary>
    public class DiaryMemoryAgeBandDef
    {
        public int maxAgeTicks;
        public string label;
    }

    /// <summary>
    /// Singleton XML-owned policy for memory deposit, recall, and eviction. All values are plain
    /// tuning numbers, closed-vocabulary tag tokens, or DefInjected prompt prose.
    /// </summary>
    public class DiaryMemoryTuningDef : Def
    {
        public bool enabled = true;

        // Deposit (design §7).
        public float minDepositImportance = 0.3f;
        public int fragmentTextMaxChars = 200;
        public int maxKeywordsPerFragment = 8;
        public float fallbackCueImportance = 0.3f;
        public float importantGroupBonus = 0.15f;
        public float negativeMoodBonus = 0.10f;
        public float positiveMoodBonus = 0.05f;
        public List<DiaryMemoryCueImportanceDef> cueImportance;
        public List<DiaryMemoryCueTagsDef> cueTags;
        public List<DiaryMemoryContextMarkerTagsDef> contextMarkerTags;
        public List<string> contextKeywordKeys;

        // Retrieval (design §8).
        public int minFragmentsForRecall = 4;
        public float recallGateChance = 0.6f;
        public float tagWeight = 0.4f;
        public float keywordWeight = 0.6f;
        public int tagSaturationCount = 2;
        public int keywordSaturationCount = 3;
        public int recencyHalfLifeTicks = 1800000;
        public float recencyFloor = 0.25f;
        public int minRecallAgeTicks = 60000;
        public float minDirectScore = 0.30f;
        public float spreadGateChance = 0.5f;
        public float spreadDamping = 0.5f;
        public float minSpreadScore = 0.15f;
        public int recallCooldownTicks = 300000;
        public float repetitionPenaltyFactor = 0.25f;
        public int memoryContextMaxChars = 500;
        public List<DiaryMemoryAgeBandDef> ageBands;
        // Prompt prose is localized through DefInjected. Blank remains a safe fallback when a Def
        // is missing or a translation has not supplied the optional instruction.
        public string memoryContextInstruction = string.Empty;

        // Eviction (design §10).
        public int maxFragmentsPerPawn = 60;
        public float coreImportanceThreshold = 0.8f;
        public int maxCoreFragmentsPerPawn = 15;
        public int retentionHalfLifeTicks = 1800000;
        public int staleEvictTicks = 7200000;
        public int maxTotalFragments = 3000;
        public int deadOwnerGraceTicks = 3600000;
        public int memoryEvictionScanIntervalTicks = 150000;
    }

    /// <summary>
    /// Copies the live Def into a fresh plain snapshot on the main thread. Future integration must
    /// call this only at the impure main-thread edge, never from the pure memory pipeline. A
    /// missing/partial Def cannot crash capture: behavioral code defaults remain in force, optional
    /// prompt guidance stays blank, malformed rows are ignored, and tables are replaced only when
    /// XML supplies at least one valid row.
    /// </summary>
    internal static class DiaryMemoryPolicy
    {
        private const string DefName = "Diary_Memory";

        /// <summary>Returns an immutable-by-convention snapshot for one pure extraction/recall/plan call.</summary>
        public static MemoryPolicySnapshot Snapshot()
        {
            MemoryPolicySnapshot snapshot = MemoryPolicySnapshot.CreateDefault();
            DiaryMemoryTuningDef source = DefDatabase<DiaryMemoryTuningDef>.GetNamedSilentFail(DefName);
            if (source == null)
            {
                return snapshot;
            }

            snapshot.enabled = source.enabled;
            snapshot.minDepositImportance = Clamp01(source.minDepositImportance);
            snapshot.fragmentTextMaxChars = PositiveOrFallback(source.fragmentTextMaxChars, snapshot.fragmentTextMaxChars);
            snapshot.maxKeywordsPerFragment = PositiveOrFallback(source.maxKeywordsPerFragment, snapshot.maxKeywordsPerFragment);
            snapshot.fallbackCueImportance = Clamp01(source.fallbackCueImportance);
            snapshot.importantGroupBonus = Math.Max(0f, source.importantGroupBonus);
            snapshot.negativeMoodBonus = Math.Max(0f, source.negativeMoodBonus);
            snapshot.positiveMoodBonus = Math.Max(0f, source.positiveMoodBonus);
            snapshot.minFragmentsForRecall = Math.Max(0, source.minFragmentsForRecall);
            snapshot.recallGateChance = Clamp01(source.recallGateChance);
            snapshot.tagWeight = Math.Max(0f, source.tagWeight);
            snapshot.keywordWeight = Math.Max(0f, source.keywordWeight);
            snapshot.tagSaturationCount = PositiveOrFallback(source.tagSaturationCount, snapshot.tagSaturationCount);
            snapshot.keywordSaturationCount = PositiveOrFallback(source.keywordSaturationCount, snapshot.keywordSaturationCount);
            snapshot.recencyHalfLifeTicks = PositiveOrFallback(source.recencyHalfLifeTicks, snapshot.recencyHalfLifeTicks);
            snapshot.recencyFloor = Clamp01(source.recencyFloor);
            snapshot.minRecallAgeTicks = Math.Max(0, source.minRecallAgeTicks);
            snapshot.minDirectScore = Math.Max(0f, source.minDirectScore);
            snapshot.spreadGateChance = Clamp01(source.spreadGateChance);
            snapshot.spreadDamping = Math.Max(0f, source.spreadDamping);
            snapshot.minSpreadScore = Math.Max(0f, source.minSpreadScore);
            snapshot.recallCooldownTicks = Math.Max(0, source.recallCooldownTicks);
            snapshot.repetitionPenaltyFactor = Clamp01(source.repetitionPenaltyFactor);
            snapshot.memoryContextMaxChars = PositiveOrFallback(source.memoryContextMaxChars, snapshot.memoryContextMaxChars);
            snapshot.memoryContextInstruction = source.memoryContextInstruction ?? string.Empty;
            snapshot.maxFragmentsPerPawn = PositiveOrFallback(source.maxFragmentsPerPawn, snapshot.maxFragmentsPerPawn);
            snapshot.coreImportanceThreshold = Clamp01(source.coreImportanceThreshold);
            snapshot.maxCoreFragmentsPerPawn = PositiveOrFallback(source.maxCoreFragmentsPerPawn, snapshot.maxCoreFragmentsPerPawn);
            snapshot.retentionHalfLifeTicks = PositiveOrFallback(source.retentionHalfLifeTicks, snapshot.retentionHalfLifeTicks);
            snapshot.staleEvictTicks = PositiveOrFallback(source.staleEvictTicks, snapshot.staleEvictTicks);
            snapshot.maxTotalFragments = PositiveOrFallback(source.maxTotalFragments, snapshot.maxTotalFragments);
            snapshot.deadOwnerGraceTicks = PositiveOrFallback(source.deadOwnerGraceTicks, snapshot.deadOwnerGraceTicks);
            snapshot.memoryEvictionScanIntervalTicks = PositiveOrFallback(
                source.memoryEvictionScanIntervalTicks, snapshot.memoryEvictionScanIntervalTicks);

            CopyCueImportance(source.cueImportance, snapshot.cueImportance);
            CopyCueTags(source.cueTags, snapshot.cueTags);
            CopyContextMarkerTags(source.contextMarkerTags, snapshot.contextMarkerTags);
            CopyKeywordKeys(source.contextKeywordKeys, snapshot.contextKeywordKeys);
            CopyAgeBands(source.ageBands, snapshot.ageBands);
            return snapshot;
        }

        private static void CopyCueImportance(
            List<DiaryMemoryCueImportanceDef> source,
            List<MemoryCueImportance> destination)
        {
            if (source == null)
            {
                return;
            }

            List<MemoryCueImportance> copied = new List<MemoryCueImportance>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryMemoryCueImportanceDef row = source[i];
                if (row != null && !string.IsNullOrWhiteSpace(row.cue))
                {
                    copied.Add(new MemoryCueImportance
                    {
                        cue = row.cue.Trim(),
                        importance = Clamp01(row.importance)
                    });
                }
            }

            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static void CopyCueTags(List<DiaryMemoryCueTagsDef> source, List<MemoryCueTags> destination)
        {
            if (source == null)
            {
                return;
            }

            List<MemoryCueTags> copied = new List<MemoryCueTags>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryMemoryCueTagsDef row = source[i];
                if (row != null && !string.IsNullOrWhiteSpace(row.cue))
                {
                    copied.Add(new MemoryCueTags { cue = row.cue.Trim(), tags = CleanTokens(row.tags) });
                }
            }

            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static void CopyContextMarkerTags(
            List<DiaryMemoryContextMarkerTagsDef> source,
            List<MemoryContextMarkerTags> destination)
        {
            if (source == null)
            {
                return;
            }

            List<MemoryContextMarkerTags> copied = new List<MemoryContextMarkerTags>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryMemoryContextMarkerTagsDef row = source[i];
                if (row != null && !string.IsNullOrWhiteSpace(row.marker))
                {
                    copied.Add(new MemoryContextMarkerTags { marker = row.marker.Trim(), tags = CleanTokens(row.tags) });
                }
            }

            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static void CopyKeywordKeys(List<string> source, List<string> destination)
        {
            List<string> copied = CleanTokens(source);
            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static void CopyAgeBands(List<DiaryMemoryAgeBandDef> source, List<MemoryAgeBand> destination)
        {
            if (source == null)
            {
                return;
            }

            // Order is authored policy: MemoryRecallSelector takes the first covering row. Do not
            // silently sort here; an editor that reorders bands is intentionally changing policy.
            List<MemoryAgeBand> copied = new List<MemoryAgeBand>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryMemoryAgeBandDef row = source[i];
                if (row != null && row.maxAgeTicks > 0 && !string.IsNullOrWhiteSpace(row.label))
                {
                    copied.Add(new MemoryAgeBand { maxAgeTicks = row.maxAgeTicks, label = row.label.Trim() });
                }
            }

            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static List<string> CleanTokens(List<string> source)
        {
            List<string> cleaned = new List<string>();
            if (source == null)
            {
                return cleaned;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(source[i]))
                {
                    cleaned.Add(source[i].Trim());
                }
            }

            return cleaned;
        }

        private static int PositiveOrFallback(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
