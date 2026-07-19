// Pure prompt-context formatter for one successful dramatic Royalty permit use. Runtime collection
// localizes and sanitizes visible strings on RimWorld's main thread; only stable schema keys and the
// reviewed family token are introduced here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Builds bounded exact permit context without claiming effect completion.</summary>
    internal static class RoyalPermitContextFormatter
    {
        public const string MarkerKey = "royal_permit";

        public static string Format(RoyalPermitUseSnapshot use, RoyaltyPolicySnapshot policy)
        {
            if (use == null || !RoyalPermitFamilyTokens.IsKnown(use.permitFamilyToken))
                return string.Empty;
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            List<string> parts = new List<string>();
            Add(parts, MarkerKey, use.permitFamilyToken, 80);
            Add(parts, "permit_def", use.permitDefName, 120);
            Add(parts, "permit_label", use.permitLabel, effective.maximumPermitLabelCharacters);
            Add(parts, "permit_family", use.permitFamilyToken, 80);
            Add(parts, "permit_faction", use.factionName, effective.maximumPermitLabelCharacters);
            Add(parts, "permit_title", use.titleLabel, effective.maximumPermitLabelCharacters);
            Add(parts, "permit_setting", use.mapLabel, effective.maximumPermitSettingCharacters);
            if (use.usedDuringCooldown) parts.Add("used_during_cooldown=true");
            return string.Join(";", parts.ToArray());
        }

        private static void Add(List<string> parts, string key, string value, int cap)
        {
            string cleaned = Clean(value, cap);
            if (cleaned.Length > 0) parts.Add(key + "=" + cleaned);
        }

        private static string Clean(string value, int cap)
        {
            string cleaned = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
                .Replace(';', ',').Trim();
            int maximum = Math.Max(1, cap);
            return cleaned.Length <= maximum ? cleaned : cleaned.Substring(0, maximum).TrimEnd();
        }
    }
}
