// Pure exact-name classification for canonical Biotech birth ownership and miscarriage roles.
// XML supplies every accepted Def name through BiotechPolicySnapshot; no live Def/Pawn is read here.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Classifies mature birth signals and exact miscarriage participant roles.</summary>
    internal static class BirthCorrelationPolicy
    {
        /// <summary>Returns true only for an XML-owned exact mature-birth Def name.</summary>
        public static bool IsMatureBirthDef(string defName, BiotechPolicySnapshot policy)
        {
            return Contains(policy?.matureBirthDefNames, defName);
        }

        /// <summary>
        /// Resolves the exact miscarriage role for one pawn/Thought pair, or empty when the Thought
        /// is unrelated or the pawn is not the participant that vanilla assigned that memory to.
        /// </summary>
        public static string MiscarriageRole(
            string thoughtDefName,
            string pawnId,
            string birtherId,
            string geneticMotherId,
            string fatherId,
            BiotechPolicySnapshot policy)
        {
            string id = Clean(pawnId);
            if (id.Length == 0)
            {
                return string.Empty;
            }

            if (Contains(policy?.miscarriageBirtherThoughtDefNames, thoughtDefName)
                && string.Equals(id, Clean(birtherId), StringComparison.Ordinal))
            {
                return BiotechFamilyRoleTokens.Birther;
            }

            if (!Contains(policy?.miscarriagePartnerThoughtDefNames, thoughtDefName))
            {
                return string.Empty;
            }

            if (string.Equals(id, Clean(geneticMotherId), StringComparison.Ordinal))
            {
                return BiotechFamilyRoleTokens.GeneticMother;
            }

            return string.Equals(id, Clean(fatherId), StringComparison.Ordinal)
                ? BiotechFamilyRoleTokens.Father
                : string.Empty;
        }

        private static bool Contains(List<string> values, string candidate)
        {
            string target = Clean(candidate);
            if (target.Length == 0 || values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(Clean(values[i]), target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
