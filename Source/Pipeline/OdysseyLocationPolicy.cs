// Pure Odyssey location classification. Runtime code supplies exact Def names and visible labels;
// this policy uses only XML-shaped exact mappings and never guesses from English text.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Projects visible biome/site facts into one stable XML-owned destination category.</summary>
    internal static class OdysseyLocationPolicy
    {
        /// <summary>
        /// Returns a detached classified copy. An exact site mapping wins over a biome mapping because
        /// it expresses the more specific visible destination; invisible input authorizes no category.
        /// </summary>
        public static OdysseyLocationSnapshot Classify(
            OdysseyLocationSnapshot source,
            OdysseyPolicySnapshot policy)
        {
            OdysseyLocationSnapshot result = Copy(source);
            result.layerToken = OdysseyLocationLayerTokens.Normalize(result.layerToken);
            result.categoryToken = string.Empty;
            result.majorDestination = false;
            if (source == null || !source.visible)
            {
                return result;
            }

            OdysseyLocationCategoryRule rule = FindExact(
                source.siteDefName,
                policy == null ? null : policy.siteCategories);
            if (rule == null)
            {
                rule = FindExact(
                    source.biomeDefName,
                    policy == null ? null : policy.biomeCategories);
            }

            if (rule != null)
            {
                result.categoryToken = CleanToken(rule.categoryToken);
                result.majorDestination = result.categoryToken.Length > 0 && rule.majorDestination;
            }

            return result;
        }

        /// <summary>
        /// Classifies one exact QuestScriptDef root without inferring a reward, recovery, or ending
        /// choice. The returned object is detached so mutable XML-backed policy cannot leak outward.
        /// </summary>
        public static OdysseyQuestRootClassification ClassifyQuestRoot(
            string questRootDefName,
            OdysseyPolicySnapshot policy)
        {
            OdysseyLocationCategoryRule rule = FindExact(
                questRootDefName,
                policy == null ? null : policy.questCategories);
            string token = rule == null ? string.Empty : CleanToken(rule.categoryToken);
            return new OdysseyQuestRootClassification
            {
                recognized = token.Length > 0,
                categoryToken = token,
                majorDestination = token.Length > 0 && rule.majorDestination
            };
        }

        /// <summary>Returns true only for an exact case-insensitive Def-name rule.</summary>
        private static OdysseyLocationCategoryRule FindExact(
            string defName,
            List<OdysseyLocationCategoryRule> rules)
        {
            string key = (defName ?? string.Empty).Trim();
            if (key.Length == 0 || rules == null)
            {
                return null;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                OdysseyLocationCategoryRule rule = rules[i];
                if (rule != null
                    && string.Equals(key, (rule.defName ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    && CleanToken(rule.categoryToken).Length > 0)
                {
                    return rule;
                }
            }

            return null;
        }

        private static string CleanToken(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0
                ? string.Empty
                : cleaned;
        }

        private static OdysseyLocationSnapshot Copy(OdysseyLocationSnapshot source)
        {
            if (source == null)
            {
                return new OdysseyLocationSnapshot { visible = false };
            }

            return new OdysseyLocationSnapshot
            {
                stableKey = source.stableKey ?? string.Empty,
                visibleLabel = source.visibleLabel ?? string.Empty,
                layerToken = source.layerToken ?? OdysseyLocationLayerTokens.Unknown,
                biomeDefName = source.biomeDefName ?? string.Empty,
                biomeLabel = source.biomeLabel ?? string.Empty,
                siteDefName = source.siteDefName ?? string.Empty,
                siteLabel = source.siteLabel ?? string.Empty,
                categoryToken = source.categoryToken ?? string.Empty,
                majorDestination = source.majorDestination,
                isPlayerHome = source.isPlayerHome,
                visible = source.visible
            };
        }
    }
}
