// XML contract for authored lore-seed memories (design/LORE_MEMORY_SEED_PLAN.md §5). Each Def is
// one first-person remembered-past sentence plus stable, language-neutral matcher data. The L3
// catalog ships in 1.6/Defs/DiaryLoreSeedDefs.xml; until then the DefDatabase may simply be empty
// and the whole lore layer no-ops. `text`/`label` localize through DefInjected; every matcher
// field holds Def names or closed tokens only — never localized prose (§16 G4).
//
// The pure planner never sees this type: ToCandidate copies a Def into the plain
// LoreSeedCandidate contract on the main thread.
//
// New to C#/RimWorld? See AGENTS.md ("XML Defs", "DLC-safety"). DLC/mod Def names in matcher
// lists sit inert when that content is absent — matching is string-based by design (§16 G14).
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One authored lore-seed memory: prose, selection weight, and eligibility matchers.</summary>
    public class DiaryLoreSeedDef : Def
    {
        // Hard authoring bound (§5): mirrors the memory policy's fragmentTextMaxChars default so a
        // seed can never exceed what a deposited fragment may hold.
        public const int MaxTextChars = 200;
        public const int MaxKeywords = 8;

        // First-person remembered-past prose, DefInjected-translatable.
        public string text = string.Empty;
        // Closed tokens (LoreSeedTokens): initial | progression | both.
        public string usage = LoreSeedTokens.UsageInitial;
        // Closed tokens (LoreSeedTokens): ordinary | core.
        public string retentionTier = LoreSeedTokens.TierOrdinary;
        public float weight = 1f;
        // Mutex only within one initial plan construction (§7.1); no lifetime semantics.
        public string mutexGroup = string.Empty;
        // MemoryTagTokens vocabulary; `lore` is implied and added at deposit.
        public List<string> tags;
        // Stable, language-neutral memory-query values only (§10).
        public List<string> keywords;
        public List<string> backstoryCategories;
        public List<string> excludeBackstoryCategories;
        public List<string> backstoryDefNames;
        public List<string> excludeBackstoryDefNames;
        public List<string> xenotypeDefNames;
        public List<string> hediffDefNames;
        // Exact registered progression event Def tokens (§8.3); required for progression usage.
        public List<string> progressionEventDefNames;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                yield return "lore seed has empty text";
            }
            else if (text.Trim().Length > MaxTextChars)
            {
                yield return "lore seed text exceeds " + MaxTextChars + " characters";
            }

            if (!LoreSeedTokens.IsKnownUsage(usage))
            {
                yield return "unknown lore seed usage token '" + usage + "'";
            }

            if (!LoreSeedTokens.IsKnownTier(retentionTier))
            {
                yield return "unknown lore seed retentionTier token '" + retentionTier + "'";
            }

            if (!(weight > 0f) || float.IsInfinity(weight) || float.IsNaN(weight))
            {
                yield return "lore seed weight must be a positive finite number";
            }

            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    // Unknown tags are also dropped at candidate build; reporting here makes an
                    // XML typo visible instead of silently weakening recall.
                    if (!string.IsNullOrWhiteSpace(tags[i]) && !MemoryTagTokens.IsKnown(tags[i].Trim()))
                    {
                        yield return "unknown memory tag token '" + tags[i] + "'";
                    }
                }
            }

            if (keywords != null && keywords.Count > MaxKeywords)
            {
                yield return "lore seed declares more than " + MaxKeywords + " keywords";
            }

            // §10: authored keywords match live queries only through the shared tokenizer. The
            // identity path keeps short/stopword tokens, but the prose query path drops anything
            // under three characters or in the stopword list, so such a keyword is deposited yet can
            // never match. Report it here (matching the unknown-tag philosophy) instead of shipping
            // a silently dead keyword. Reachability of the token's VALUE (whether any capture path
            // emits it) is a separate authoring concern documented in DiaryLoreSeedDefs.xml.
            List<string> normalizedKeywords = MemoryExtraction.NormalizeAuthoredKeywords(keywords, MaxKeywords);
            for (int i = 0; i < normalizedKeywords.Count; i++)
            {
                if (!MemoryExtraction.IsQueryReachableToken(normalizedKeywords[i]))
                {
                    yield return "lore seed keyword token '" + normalizedKeywords[i]
                        + "' is under three characters or a stopword and can never match a live query";
                }
            }

            bool progressionUsage = usage == LoreSeedTokens.UsageProgression
                || usage == LoreSeedTokens.UsageBoth;
            if (progressionUsage && (progressionEventDefNames == null || progressionEventDefNames.Count == 0))
            {
                yield return "progression-usage lore seed declares no progressionEventDefNames";
            }

            // §4/G5: lifelong core identity claims need exact high-confidence evidence. Broad
            // culture/category matchers alone can never author a core seed.
            if (retentionTier == LoreSeedTokens.TierCore
                && CountOf(backstoryDefNames) == 0
                && CountOf(xenotypeDefNames) == 0
                && CountOf(hediffDefNames) == 0)
            {
                yield return "core lore seed lacks a high-confidence positive constraint"
                    + " (exact backstoryDefNames, xenotypeDefNames, or hediffDefNames)";
            }
        }

        /// <summary>
        /// Copies this Def into the plain candidate the pure planner consumes. Unknown tags are
        /// dropped, keywords are normalized by the exact deposit/query tokenizer, and matcher
        /// lists are copied so pure code can never touch the live Def.
        /// </summary>
        internal LoreSeedCandidate ToCandidate(MemoryPolicySnapshot policy)
        {
            MemoryPolicySnapshot safePolicy = policy ?? MemoryPolicySnapshot.CreateDefault();
            LoreSeedCandidate candidate = new LoreSeedCandidate
            {
                seedDefName = defName ?? string.Empty,
                fallbackText = (text ?? string.Empty).Trim(),
                usage = (usage ?? string.Empty).Trim(),
                retentionTier = (retentionTier ?? string.Empty).Trim(),
                weight = weight,
                mutexGroup = (mutexGroup ?? string.Empty).Trim(),
                keywords = MemoryExtraction.NormalizeAuthoredKeywords(
                    keywords, Math.Min(MaxKeywords, safePolicy.maxKeywordsPerFragment)),
                backstoryCategories = CleanCopy(backstoryCategories),
                excludeBackstoryCategories = CleanCopy(excludeBackstoryCategories),
                backstoryDefNames = CleanCopy(backstoryDefNames),
                excludeBackstoryDefNames = CleanCopy(excludeBackstoryDefNames),
                xenotypeDefNames = CleanCopy(xenotypeDefNames),
                hediffDefNames = CleanCopy(hediffDefNames),
                progressionEventDefNames = CleanCopy(progressionEventDefNames)
            };

            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = string.IsNullOrWhiteSpace(tags[i]) ? string.Empty : tags[i].Trim();
                    if (tag.Length > 0 && MemoryTagTokens.IsKnown(tag) && !candidate.tags.Contains(tag))
                    {
                        candidate.tags.Add(tag);
                    }
                }
            }

            return candidate;
        }

        private static int CountOf(List<string> values)
        {
            int count = 0;
            if (values == null)
            {
                return 0;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<string> CleanCopy(List<string> values)
        {
            List<string> copy = new List<string>();
            if (values == null)
            {
                return copy;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    copy.Add(values[i].Trim());
                }
            }

            return copy;
        }
    }
}
