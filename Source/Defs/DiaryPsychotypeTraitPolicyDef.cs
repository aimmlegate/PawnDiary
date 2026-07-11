// XML boundary for trait-driven psychotype tuning. RimWorld loads the Def below at startup; this file
// copies it into plain pipeline DTOs so the roll algorithm never depends on Verse or live Def objects.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One named numeric bonus inside an XML trait-affinity rule.</summary>
    public class DiaryPsychotypeTraitBonus
    {
        public string target;
        public float bonus;
    }

    /// <summary>One trait/degree mapping and its family/member weight bonuses.</summary>
    public class DiaryPsychotypeTraitAffinityRule
    {
        public string traitDefName;
        public bool matchDegree;
        public int degree;
        public string key;
        public List<DiaryPsychotypeTraitBonus> familyBonuses = new List<DiaryPsychotypeTraitBonus>();
        public List<DiaryPsychotypeTraitBonus> memberBonuses = new List<DiaryPsychotypeTraitBonus>();
    }

    /// <summary>Single XML-owned policy Def for trait mapping and gated-psychotype takeover.</summary>
    public class DiaryPsychotypeTraitPolicyDef : Def
    {
        public float gatedTakeoverChance = 0.45f;
        public List<DiaryPsychotypeTraitAffinityRule> rules = new List<DiaryPsychotypeTraitAffinityRule>();
    }

    /// <summary>Finds the policy Def and safely projects it into the pure roll contract.</summary>
    internal static class DiaryPsychotypeTraitPolicy
    {
        private const string DefName = "Diary_PsychotypeTraitPolicy";

        /// <summary>Returns a fresh snapshot so settings/reloads cannot mutate an in-progress roll.</summary>
        public static PsychotypeTraitAffinityPolicy Snapshot()
        {
            DiaryPsychotypeTraitPolicyDef source = DefDatabase<DiaryPsychotypeTraitPolicyDef>.GetNamedSilentFail(DefName);
            PsychotypeTraitAffinityPolicy snapshot = new PsychotypeTraitAffinityPolicy();
            if (source == null)
            {
                return snapshot;
            }

            snapshot.gatedTakeoverChance = Clamp01(source.gatedTakeoverChance);
            if (source.rules == null)
            {
                return snapshot;
            }

            for (int i = 0; i < source.rules.Count; i++)
            {
                DiaryPsychotypeTraitAffinityRule sourceRule = source.rules[i];
                if (sourceRule == null || string.IsNullOrWhiteSpace(sourceRule.traitDefName)
                    || string.IsNullOrWhiteSpace(sourceRule.key))
                {
                    continue;
                }

                PsychotypeTraitAffinityRule rule = new PsychotypeTraitAffinityRule
                {
                    traitDefName = sourceRule.traitDefName.Trim(),
                    matchDegree = sourceRule.matchDegree,
                    degree = sourceRule.degree,
                    key = sourceRule.key.Trim()
                };
                CopyBonuses(sourceRule.familyBonuses, rule.familyBonuses);
                CopyBonuses(sourceRule.memberBonuses, rule.memberBonuses);
                snapshot.rules.Add(rule);
            }

            return snapshot;
        }

        private static void CopyBonuses(List<DiaryPsychotypeTraitBonus> source,
            Dictionary<string, float> destination)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                DiaryPsychotypeTraitBonus item = source[i];
                if (item != null && !string.IsNullOrWhiteSpace(item.target))
                {
                    destination[item.target.Trim()] = item.bonus;
                }
            }
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
