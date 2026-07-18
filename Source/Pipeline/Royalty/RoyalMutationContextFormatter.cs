// Pure saved-context formatting for Royalty Phase 4 title and psylink mutations. The runtime edge
// supplies already-localized bounded labels; this helper only orders and sanitizes detached facts.
// Schema keys and none/unknown sentinels intentionally remain English per AGENTS.md localization rules.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>Builds append-only semicolon context for progression and ritual page owners.</summary>
    internal static class RoyalMutationContextFormatter
    {
        public static string Format(
            RoyalMutationBatchSnapshot batch,
            string transitionToken,
            int maximumCharacters,
            int maximumDutyCategories,
            bool includeOptionalDuties)
        {
            if (batch == null || !SafeId(batch.pawnId)) return string.Empty;
            StringBuilder builder = new StringBuilder();
            Append(builder, "royal_mutation_pawn", CleanText(batch.pawnName, maximumCharacters));
            Append(builder, "royal_cause", KnownCause(batch.causeToken));

            RoyalTitleMutationSnapshot title = batch.titleMutation;
            if (title != null)
            {
                RoyalTitleSnapshot before = title.previousTitle;
                RoyalTitleSnapshot after = title.newTitle;
                RoyalTitleSnapshot identity = after ?? before;
                if (identity != null && SafeId(identity.factionId))
                {
                    Append(builder, "royal_transition", KnownTransition(transitionToken));
                    Append(builder, "royal_faction_id", CleanToken(identity.factionId));
                    Append(builder, "royal_faction", CleanText(identity.factionName, maximumCharacters));
                    Append(builder, "previous_title", TitleLabel(before, maximumCharacters));
                    Append(builder, "previous_title_def", TitleDef(before));
                    Append(builder, "title", TitleLabel(after, maximumCharacters));
                    Append(builder, "title_def", TitleDef(after));
                    if (includeOptionalDuties)
                    {
                        Append(builder, "royal_duty_changes", Duties(
                            before, after, maximumDutyCategories));
                    }
                }
            }

            RoyalPsychicMutationSnapshot psylink = batch.psylinkMutation;
            if (psylink != null && psylink.previousPsylinkLevel >= 0
                && psylink.newPsylinkLevel >= 0
                && psylink.previousPsylinkLevel != psylink.newPsylinkLevel)
            {
                Append(builder, "previous_psylink_level", psylink.previousPsylinkLevel.ToString());
                Append(builder, "psylink_level", psylink.newPsylinkLevel.ToString());
                Append(builder, "psylink_cause", KnownCause(batch.causeToken));
            }
            return builder.ToString();
        }

        private static string Duties(
            RoyalTitleSnapshot before,
            RoyalTitleSnapshot after,
            int requestedCap)
        {
            if (after?.dutyCategoryTokens == null) return string.Empty;
            HashSet<string> old = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (before?.dutyCategoryTokens?.Count ?? 0); i++)
            {
                string token = CleanToken(before.dutyCategoryTokens[i]);
                if (token.Length > 0) old.Add(token);
            }
            List<string> added = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < after.dutyCategoryTokens.Count; i++)
            {
                string token = CleanToken(after.dutyCategoryTokens[i]);
                if (token.Length > 0 && !old.Contains(token) && seen.Add(token)) added.Add(token);
            }
            added.Sort(StringComparer.Ordinal.Compare);
            int cap = requestedCap < 1 || requestedCap > 8 ? 2 : requestedCap;
            if (added.Count > cap) added.RemoveRange(cap, added.Count - cap);
            return string.Join(", ", added.ToArray());
        }

        private static string TitleLabel(RoyalTitleSnapshot title, int cap)
        {
            if (title == null) return "none";
            string value = CleanText(title.titleLabel, cap);
            return value.Length == 0 ? "unknown" : value;
        }

        private static string TitleDef(RoyalTitleSnapshot title)
        {
            if (title == null) return "none";
            string value = CleanToken(title.titleDefName);
            return value.Length == 0 ? "unknown" : value;
        }

        private static string KnownCause(string value)
        {
            string token = CleanToken(value);
            return RoyalMutationCauseTokens.IsKnown(token) ? token : RoyalMutationCauseTokens.Unknown;
        }

        private static string KnownTransition(string value)
        {
            string token = CleanToken(value);
            return RoyalTitleTransitionTokens.IsNarrative(token) ? token : RoyalTitleTransitionTokens.Invalid;
        }

        private static string CleanText(string value, int requestedCap)
        {
            string cleaned = (value ?? string.Empty).Replace(";", ",").Replace("\r", " ").Replace("\n", " ").Trim();
            int cap = requestedCap < 1 || requestedCap > 512 ? 120 : requestedCap;
            return cleaned.Length <= cap ? cleaned : cleaned.Substring(0, cap).TrimEnd();
        }

        private static string CleanToken(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf(';') >= 0 || cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }

        private static bool SafeId(string value)
        {
            return CleanToken(value).Length > 0;
        }

        private static void Append(StringBuilder builder, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(key).Append('=').Append(value.Trim());
        }
    }
}
