// Shared, pure contracts for the pawn memory subsystem (design/MEMORY_SYSTEM_DESIGN.md). Pawns
// accumulate small tagged/keyworded text fragments of remembered experience; a deterministic
// selector later recalls zero to two of them by similarity to the current event (plus one hop of
// spreading activation) and an eviction planner keeps the store bounded. This file deliberately
// knows nothing about RimWorld, Verse, settings, Defs, saves, or prompt transport: the impure
// capture/recall seams copy frozen DiaryEvent strings into these DTOs and copy results back out.
//
// STATUS: the memory layer is implemented but deliberately NOT wired into capture, prompts, or
// the game component yet. Only the pure helpers and the pure test project reference these types.
//
// FUTURE INTEGRATION CONTRACT (design §14 steps 4-7 — keep this order and boundary):
// 1. On the main thread, require both settings.enableMemorySystem and DiaryMemoryPolicy.Snapshot().enabled.
// 2. After a DiaryEvent is registered/referenced, RECALL BEFORE DEPOSIT for each first-person POV.
// 3. Build query/deposit inputs only from frozen DiaryEvent strings. Use Pawn.LabelShort only at the
//    adapter edge; never pass Pawn, Def, settings, Verse, or Unity objects into this folder.
// 4. Copy PawnMemoryRepository.ForPawn's live rows through MemoryFragment.ToSnapshot, call Recall,
//    freeze result.memoryContext onto the POV, then bump each selected live row exactly once.
// 5. Deposit only a nonblank fragmentText above the noise gate. HasDeposit is a cheap preflight;
//    PawnMemoryRepository.Register remains the final idempotency guard.
// 6. Apply per-pawn eviction after overflow, then ALWAYS apply PlanGlobalCap after all pawn plans.
// 7. Scribe the repository on DiaryGameComponent, RebuildIndex in PostLoadInit, and schedule eviction
//    by an elapsed deadline (never TicksGame modulo). Empty recall stays an absent prompt field.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// The closed, append-only tag vocabulary for memory fragments (design §5). String tokens —
    /// not a [Flags] enum — so unknown tags loaded from a newer mod version degrade to "no match"
    /// instead of corrupting a bitfield. Tags classify WHAT a memory is; persons and places live
    /// in keywords instead, which is what lets a shared name bridge two unrelated memories.
    /// </summary>
    internal static class MemoryTagTokens
    {
        public const string Combat = "combat";
        public const string Danger = "danger";
        public const string Conflict = "conflict";
        public const string Breakdown = "breakdown";
        public const string Dread = "dread";
        public const string Body = "body";
        public const string Psychic = "psychic";
        public const string Royalty = "royalty";
        public const string Ritual = "ritual";
        public const string Work = "work";
        public const string Family = "family";
        public const string Romance = "romance";
        public const string Death = "death";
        public const string Arrival = "arrival";
        public const string Illness = "illness";
        public const string Joy = "joy";
        public const string Sorrow = "sorrow";
        public const string Social = "social";

        public static bool IsKnown(string value)
        {
            return value == Combat || value == Danger || value == Conflict || value == Breakdown
                || value == Dread || value == Body || value == Psychic || value == Royalty
                || value == Ritual || value == Work || value == Family || value == Romance
                || value == Death || value == Arrival || value == Illness || value == Joy
                || value == Sorrow || value == Social;
        }
    }

    /// <summary>Stable dev-only diagnostic tokens emitted by the pure recall selector.</summary>
    internal static class MemoryDiagnosticTokens
    {
        public const string PolicyDisabled = "policy_disabled";
        public const string NoQuery = "no_query";
        public const string StoreTooSmall = "store_too_small";
        public const string RecallGateMiss = "recall_gate";
        public const string NoDirectPick = "no_direct_pick";
        public const string SpreadGateMiss = "spread_gate";
        public const string NoSpreadPick = "no_spread_pick";
        public const string SelectedDirect = "selected_direct";
        public const string SelectedAssociative = "selected_associative";
        public const string BudgetDroppedAssociative = "budget_dropped_associative";
        public const string BudgetDroppedDirect = "budget_dropped_direct";
    }

    /// <summary>One XML-owned colorCue -> base importance row (design §7.3 importance table).</summary>
    internal sealed class MemoryCueImportance
    {
        public string cue = string.Empty;
        public float importance;
    }

    /// <summary>One XML-owned colorCue -> tag set row (design §7.3 tag mapping).</summary>
    internal sealed class MemoryCueTags
    {
        public string cue = string.Empty;
        public List<string> tags = new List<string>();
    }

    /// <summary>
    /// One XML-owned gameContext marker -> tag set row. The marker uses the DiaryContextFields
    /// convention: "key=" means "field present with a meaningful value", "key=value" means an
    /// exact value match. Presence matches deliberately ignore the saved sentinel words
    /// none/n/a/unknown, so "royal_title=none" never tags a memory as royal.
    /// </summary>
    internal sealed class MemoryContextMarkerTags
    {
        public string marker = string.Empty;
        public List<string> tags = new List<string>();
    }

    /// <summary>
    /// One age -> label band for recall rendering (design §8.4). Bands are evaluated in list
    /// order; the first band whose maxAgeTicks covers the fragment's age wins. Labels reach the
    /// LLM prompt, so they live in XML and are localized via DefInjected.
    /// </summary>
    internal sealed class MemoryAgeBand
    {
        public int maxAgeTicks;
        public string label = string.Empty;
    }

    /// <summary>
    /// Pure copy of the memory tuning Def (design §11). CreateDefault matches every shipped scoring,
    /// table, cap, and age-band value. The natural-language memoryContextInstruction deliberately
    /// stays blank in code and is supplied only by localized XML; a missing Def therefore omits that
    /// optional guidance line without changing selection behavior. Pure code never touches the Def.
    /// </summary>
    internal sealed class MemoryPolicySnapshot
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
        public List<MemoryCueImportance> cueImportance = new List<MemoryCueImportance>();
        public List<MemoryCueTags> cueTags = new List<MemoryCueTags>();
        public List<MemoryContextMarkerTags> contextMarkerTags = new List<MemoryContextMarkerTags>();
        public List<string> contextKeywordKeys = new List<string>();

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
        public List<MemoryAgeBand> ageBands = new List<MemoryAgeBand>();
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

        /// <summary>
        /// Returns a fresh policy matching 1.6/Defs/DiaryMemoryTuningDef.xml for all behavioral
        /// values; memoryContextInstruction is the intentional exception and stays blank so code
        /// never hardcodes its natural-language prompt guidance. Callers may safely adjust a test
        /// snapshot in place. Age labels retain English missing-XML fallbacks, while the shipped Def
        /// and DefInjected files supply/localize the normal runtime text.
        /// </summary>
        public static MemoryPolicySnapshot CreateDefault()
        {
            MemoryPolicySnapshot policy = new MemoryPolicySnapshot();

            // Importance table (design §7.3). Unknown/empty cues use fallbackCueImportance.
            AddCueImportance(policy, "extremeDark", 0.9f);
            AddCueImportance(policy, "bodyPartLost", 0.85f);
            AddCueImportance(policy, "danger", 0.8f);
            AddCueImportance(policy, "combat", 0.75f);
            AddCueImportance(policy, "mentalBreak", 0.7f);
            AddCueImportance(policy, "bodyPartAnomalous", 0.7f);
            AddCueImportance(policy, "psychic", 0.65f);
            AddCueImportance(policy, "bodyPartArtificial", 0.6f);
            AddCueImportance(policy, "royalty", 0.6f);
            AddCueImportance(policy, "socialFight", 0.55f);
            AddCueImportance(policy, "white", 0.5f);
            AddCueImportance(policy, "daze", 0.5f);
            AddCueImportance(policy, "strangeChat", 0.35f);
            AddCueImportance(policy, "quiet", 0.2f);

            // Tag mapping (design §7.3). Values are MemoryTagTokens; unknown tokens are dropped
            // at extraction time so a typo in XML can never invent a tag.
            AddCueTags(policy, "combat", MemoryTagTokens.Combat);
            AddCueTags(policy, "danger", MemoryTagTokens.Combat, MemoryTagTokens.Danger);
            AddCueTags(policy, "socialFight", MemoryTagTokens.Conflict, MemoryTagTokens.Social);
            AddCueTags(policy, "mentalBreak", MemoryTagTokens.Breakdown);
            AddCueTags(policy, "extremeDark", MemoryTagTokens.Dread);
            AddCueTags(policy, "strangeChat", MemoryTagTokens.Dread, MemoryTagTokens.Social);
            AddCueTags(policy, "bodyPartLost", MemoryTagTokens.Body, MemoryTagTokens.Illness);
            AddCueTags(policy, "bodyPartAnomalous", MemoryTagTokens.Body, MemoryTagTokens.Illness);
            AddCueTags(policy, "bodyPartArtificial", MemoryTagTokens.Body, MemoryTagTokens.Illness);
            AddCueTags(policy, "psychic", MemoryTagTokens.Psychic);
            AddCueTags(policy, "royalty", MemoryTagTokens.Royalty);
            AddCueTags(policy, "white", MemoryTagTokens.Joy);
            AddCueTags(policy, "daze", MemoryTagTokens.Breakdown);
            AddCueTags(policy, "quiet");

            // gameContext marker -> tags. Presence markers skip none/n/a/unknown values, so a
            // "royal_title=none" ritual row does not tag royalty.
            AddContextMarkerTags(policy, "ritual=", MemoryTagTokens.Ritual);
            AddContextMarkerTags(policy, "psychic_ritual=", MemoryTagTokens.Ritual, MemoryTagTokens.Psychic);
            AddContextMarkerTags(policy, "royal_title=", MemoryTagTokens.Royalty);

            // gameContext keys whose VALUES become association keywords (design §7.3 step 2).
            policy.contextKeywordKeys.Add("weapon");
            policy.contextKeywordKeys.Add("royal_title");
            policy.contextKeywordKeys.Add("ideological_role");
            policy.contextKeywordKeys.Add("quest_name");
            policy.contextKeywordKeys.Add("animal_name");
            policy.contextKeywordKeys.Add("room");
            policy.contextKeywordKeys.Add("place");

            // Age bands (design §8.4), evaluated in order. The final band is the "else" catch-all.
            policy.ageBands.Add(new MemoryAgeBand { maxAgeTicks = 300000, label = "a few days ago" });
            policy.ageBands.Add(new MemoryAgeBand { maxAgeTicks = 900000, label = "a couple of weeks ago" });
            policy.ageBands.Add(new MemoryAgeBand { maxAgeTicks = 3600000, label = "a few quadrums ago" });
            policy.ageBands.Add(new MemoryAgeBand { maxAgeTicks = int.MaxValue, label = "a long time ago" });
            return policy;
        }

        private static void AddCueImportance(MemoryPolicySnapshot policy, string cue, float importance)
        {
            policy.cueImportance.Add(new MemoryCueImportance { cue = cue, importance = importance });
        }

        private static void AddCueTags(MemoryPolicySnapshot policy, string cue, params string[] tags)
        {
            policy.cueTags.Add(new MemoryCueTags { cue = cue, tags = new List<string>(tags) });
        }

        private static void AddContextMarkerTags(MemoryPolicySnapshot policy, string marker, params string[] tags)
        {
            policy.contextMarkerTags.Add(new MemoryContextMarkerTags { marker = marker, tags = new List<string>(tags) });
        }
    }

    /// <summary>
    /// Plain copy of one saved MemoryFragment (design §8). The pure selector/planner only ever
    /// see these snapshots — never the save model — so the pure layer cannot mutate saved state.
    /// pawnId and recallCount ride along for the eviction planner's global pass and tie-breaks.
    /// </summary>
    internal sealed class MemoryFragmentSnapshot
    {
        public string memoryId = string.Empty;
        public string pawnId = string.Empty;
        public string sourceEventId = string.Empty;
        public List<string> tags = new List<string>();
        public List<string> keywords = new List<string>();
        public float importance;
        public int createdTick;
        public int lastRecalledTick;
        public int recallCount;
        public string text = string.Empty;
    }

    /// <summary>
    /// One associative recall request (design §8). tags/keywords come from the SAME extraction
    /// function used at deposit time — there is no second vocabulary and no query language. The
    /// seed is the FNV-1a stable seed of "eventId|pawnId" (the HumorChancePolicy.StableSeed
    /// pattern), so recall is deterministic per event+pawn and tests can pin it.
    /// </summary>
    internal sealed class MemoryRecallQuery
    {
        public List<string> tags = new List<string>();
        public List<string> keywords = new List<string>();
        public string currentEventId = string.Empty;
        public int currentTick;
        public int seed;
    }

    /// <summary>One recalled fragment. kind is MemoryRecallPick.Direct or .Associative.</summary>
    internal sealed class MemoryRecallPick
    {
        public const string Direct = "direct";
        public const string Associative = "associative";

        public string memoryId = string.Empty;
        public string kind = Direct;
        public float score;
    }

    /// <summary>
    /// Recall outcome (design §8): 0..2 picks, the rendered memoryContext prompt-field value
    /// (empty when nothing surfaced — empty fields cost zero prompt tokens), and dev-only
    /// diagnostic reason tokens. The selector never mutates its inputs; the impure applier owns
    /// the only writes (freezing memoryContext and bumping lastRecalledTick/recallCount).
    /// </summary>
    internal sealed class MemoryRecallResult
    {
        public List<MemoryRecallPick> picks = new List<MemoryRecallPick>();
        public string memoryContext = string.Empty;
        public List<string> diagnostics = new List<string>();
    }
}
