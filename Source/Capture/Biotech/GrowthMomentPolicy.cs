// Pure Biotech growth-choice diff and context formatting. Live capture supplies two bounded snapshots;
// this file verifies what actually changed, maps the private numeric tier to XML-owned qualitative
// policy, and emits only stable/context-safe fields. It never reads RimWorld or settings state.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>Pure mutation and opportunity-band policy for ages 7, 10, and 13.</summary>
    internal static class GrowthMomentPolicy
    {
        private const string NoTraitSentinel = "NoTrait";

        /// <summary>
        /// Compares the actual state before/after a committed growth choice. Parameters are evidence,
        /// not authority: a requested trait or passion is included only when the snapshot proves it.
        /// </summary>
        public static GrowthMomentMutation Diff(
            GrowthPawnSnapshot before,
            GrowthPawnSnapshot after,
            GrowthCommittedChoice committedChoice,
            BiotechPolicySnapshot policy)
        {
            if (!Compatible(before, after) || committedChoice == null)
            {
                return null;
            }

            string stage = BiotechGrowthStageTokens.ForAge(after.biologicalAge);
            string source = BiotechGrowthSourceTokens.IsKnown(committedChoice.sourceToken)
                ? committedChoice.sourceToken
                : string.Empty;
            if (stage.Length == 0 || source.Length == 0)
            {
                return null;
            }

            List<GrowthTraitFact> newTraits = NewTraits(before.traits, after.traits);
            List<PassionMutation> passionChanges = PassionChanges(
                before.skills,
                after.skills,
                committedChoice.selectedPassionSkillDefNames,
                source == BiotechGrowthSourceTokens.PlayerChoice);
            bool nicknameChanged = !string.Equals(
                before.shortName ?? string.Empty,
                after.shortName ?? string.Empty,
                StringComparison.Ordinal);
            bool newResponsibilities = !before.hasNewResponsibilities && after.hasNewResponsibilities;

            if (newTraits.Count == 0 && passionChanges.Count == 0 && !nicknameChanged && !newResponsibilities)
            {
                return null;
            }

            GrowthMomentMutation mutation = new GrowthMomentMutation
            {
                childId = after.pawnId.Trim(),
                age = after.biologicalAge,
                stageToken = stage,
                opportunityBand = OpportunityBandFor(after.growthTier, policy).token,
                nicknameChanged = nicknameChanged,
                previousShortName = nicknameChanged ? before.shortName ?? string.Empty : string.Empty,
                currentShortName = nicknameChanged ? after.shortName ?? string.Empty : string.Empty,
                newResponsibilities = newResponsibilities,
                familyArcId = committedChoice.familyArcId ?? string.Empty,
                sourceToken = source,
                correlationId = BiotechArcKeys.GrowthCorrelation(after.pawnId, after.biologicalAge),
                passionChanges = passionChanges
            };

            for (int i = 0; i < newTraits.Count; i++)
            {
                mutation.additionalTraitKeysToConsume.Add(newTraits[i].traitKey);
            }

            mutation.additionalTraitKeysToConsume.Sort(StringComparer.Ordinal);
            mutation.selectedTrait = SelectedTrait(newTraits, committedChoice.selectedTraitKey, source);
            return mutation;
        }

        /// <summary>Returns the XML band covering a tier, or a safe code fallback token.</summary>
        public static BiotechOpportunityBandRule OpportunityBandFor(int growthTier, BiotechPolicySnapshot policy)
        {
            List<BiotechOpportunityBandRule> rules = policy?.opportunityBands;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    BiotechOpportunityBandRule rule = rules[i];
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.token)
                        && growthTier >= rule.minimumTier && growthTier <= rule.maximumTier)
                    {
                        return rule;
                    }
                }
            }

            // Default descriptions remain blank: player/prompt prose must come from DefInjected XML.
            if (growthTier <= 2) return new BiotechOpportunityBandRule { token = "narrow" };
            if (growthTier <= 5) return new BiotechOpportunityBandRule { token = "mixed" };
            if (growthTier <= 7) return new BiotechOpportunityBandRule { token = "broad" };
            return new BiotechOpportunityBandRule { token = "exceptional" };
        }

        private static bool Compatible(GrowthPawnSnapshot before, GrowthPawnSnapshot after)
        {
            return before != null && after != null
                && !string.IsNullOrWhiteSpace(before.pawnId)
                && string.Equals(before.pawnId.Trim(), (after.pawnId ?? string.Empty).Trim(), StringComparison.Ordinal)
                && before.biologicalAge > 0
                && before.biologicalAge == after.biologicalAge;
        }

        private static List<GrowthTraitFact> NewTraits(
            List<GrowthTraitFact> before,
            List<GrowthTraitFact> after)
        {
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTraitKeys(known, before);
            List<GrowthTraitFact> added = new List<GrowthTraitFact>();
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (after == null)
            {
                return added;
            }

            for (int i = 0; i < after.Count; i++)
            {
                GrowthTraitFact trait = after[i];
                string key = (trait?.traitKey ?? string.Empty).Trim();
                if (key.Length == 0 || IsNoTrait(key) || known.Contains(key) || !emitted.Add(key))
                {
                    continue;
                }

                added.Add(new GrowthTraitFact
                {
                    traitKey = key,
                    label = trait.label ?? string.Empty,
                    description = trait.description ?? string.Empty
                });
            }

            added.Sort((left, right) => string.CompareOrdinal(left.traitKey, right.traitKey));
            return added;
        }

        private static void AddTraitKeys(HashSet<string> destination, List<GrowthTraitFact> values)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string key = (values[i]?.traitKey ?? string.Empty).Trim();
                if (key.Length > 0)
                {
                    destination.Add(key);
                }
            }
        }

        private static GrowthTraitFact SelectedTrait(
            List<GrowthTraitFact> added,
            string requestedTraitKey,
            string sourceToken)
        {
            if (added == null || added.Count == 0)
            {
                return null;
            }

            if (sourceToken == BiotechGrowthSourceTokens.AutoResolved)
            {
                return added.Count == 1 ? added[0] : null;
            }

            string requested = (requestedTraitKey ?? string.Empty).Trim();
            if (requested.Length == 0 || IsNoTrait(requested))
            {
                return null;
            }

            for (int i = 0; i < added.Count; i++)
            {
                if (string.Equals(added[i].traitKey, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return added[i];
                }
            }

            return null;
        }

        private static bool IsNoTrait(string key)
        {
            return string.Equals(key, NoTraitSentinel, StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("." + NoTraitSentinel, StringComparison.OrdinalIgnoreCase);
        }

        private static List<PassionMutation> PassionChanges(
            List<GrowthSkillFact> before,
            List<GrowthSkillFact> after,
            List<string> selectedSkills,
            bool requireSelected)
        {
            Dictionary<string, GrowthSkillFact> oldByKey = SkillMap(before);
            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedSkills != null)
            {
                for (int i = 0; i < selectedSkills.Count; i++)
                {
                    string key = (selectedSkills[i] ?? string.Empty).Trim();
                    if (key.Length > 0)
                    {
                        selected.Add(key);
                    }
                }
            }

            List<PassionMutation> changes = new List<PassionMutation>();
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (after == null)
            {
                return changes;
            }

            for (int i = 0; i < after.Count; i++)
            {
                GrowthSkillFact current = after[i];
                string key = (current?.skillDefName ?? string.Empty).Trim();
                if (key.Length == 0 || !emitted.Add(key) || (requireSelected && !selected.Contains(key)))
                {
                    continue;
                }

                GrowthSkillFact previous;
                string oldPassion = oldByKey.TryGetValue(key, out previous)
                    ? BiotechPassionTokens.Normalize(previous.passion)
                    : BiotechPassionTokens.None;
                string newPassion = BiotechPassionTokens.Normalize(current.passion);
                if (BiotechPassionTokens.Rank(newPassion) <= BiotechPassionTokens.Rank(oldPassion))
                {
                    continue;
                }

                changes.Add(new PassionMutation
                {
                    skillDefName = key,
                    label = current.label ?? string.Empty,
                    beforePassion = oldPassion,
                    afterPassion = newPassion
                });
            }

            changes.Sort((left, right) => string.CompareOrdinal(left.skillDefName, right.skillDefName));
            return changes;
        }

        private static Dictionary<string, GrowthSkillFact> SkillMap(List<GrowthSkillFact> values)
        {
            Dictionary<string, GrowthSkillFact> result =
                new Dictionary<string, GrowthSkillFact>(StringComparer.OrdinalIgnoreCase);
            if (values == null)
            {
                return result;
            }

            for (int i = 0; i < values.Count; i++)
            {
                GrowthSkillFact value = values[i];
                string key = (value?.skillDefName ?? string.Empty).Trim();
                if (key.Length > 0 && !result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Pure normalization and lookup for saved postponed growth choices. Runtime code owns pawn and
    /// letter resolution; this helper owns only stable IDs, ages, ticks, and detached snapshots.
    /// </summary>
    internal static class PendingBiotechGrowthMomentPolicy
    {
        /// <summary>
        /// Repairs rows, drops malformed entries, and keeps the newest row for each pawn/age pair.
        /// Future ticks are clamped to the current game tick so corrupt saves cannot postpone cleanup.
        /// </summary>
        public static List<PendingBiotechGrowthMoment> Normalize(
            IList<PendingBiotechGrowthMoment> source,
            int currentTick)
        {
            int now = Math.Max(0, currentTick);
            Dictionary<string, PendingBiotechGrowthMoment> newestByKey =
                new Dictionary<string, PendingBiotechGrowthMoment>(StringComparer.Ordinal);
            if (source == null)
            {
                return new List<PendingBiotechGrowthMoment>();
            }

            for (int i = 0; i < source.Count; i++)
            {
                PendingBiotechGrowthMoment row = NormalizeRow(source[i], now);
                if (row == null)
                {
                    continue;
                }

                string key = row.pawnId + "|" + row.birthdayAge;
                PendingBiotechGrowthMoment existing;
                if (!newestByKey.TryGetValue(key, out existing)
                    || row.birthdayTick > existing.birthdayTick
                    || (row.birthdayTick == existing.birthdayTick
                        && row.configuredTick >= existing.configuredTick))
                {
                    newestByKey[key] = row;
                }
            }

            List<PendingBiotechGrowthMoment> result =
                new List<PendingBiotechGrowthMoment>(newestByKey.Values);
            result.Sort((left, right) =>
            {
                int pawn = string.CompareOrdinal(left.pawnId, right.pawnId);
                return pawn != 0 ? pawn : left.birthdayAge.CompareTo(right.birthdayAge);
            });
            return result;
        }

        /// <summary>Finds the newest unresolved row for one exact pawn and canonical growth age.</summary>
        public static PendingBiotechGrowthMoment FindNewest(
            IList<PendingBiotechGrowthMoment> source,
            string pawnId,
            int birthdayAge)
        {
            string id = (pawnId ?? string.Empty).Trim();
            if (id.Length == 0 || BiotechGrowthStageTokens.ForAge(birthdayAge).Length == 0 || source == null)
            {
                return null;
            }

            PendingBiotechGrowthMoment newest = null;
            for (int i = 0; i < source.Count; i++)
            {
                PendingBiotechGrowthMoment row = source[i];
                if (row == null
                    || row.birthdayAge != birthdayAge
                    || !string.Equals(row.pawnId, id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (newest == null
                    || row.birthdayTick > newest.birthdayTick
                    || (row.birthdayTick == newest.birthdayTick
                        && row.configuredTick > newest.configuredTick))
                {
                    newest = row;
                }
            }

            return newest;
        }

        /// <summary>True once elapsed ticks meet the XML-owned pending-row expiry.</summary>
        public static bool IsExpired(PendingBiotechGrowthMoment row, int currentTick, int expiryTicks)
        {
            if (row == null || expiryTicks <= 0)
            {
                return false;
            }

            int now = Math.Max(0, currentTick);
            int started = Math.Max(0, row.birthdayTick);
            return now >= started && now - started >= expiryTicks;
        }

        /// <summary>
        /// True once the XML grace has elapsed. Runtime code combines this with concrete evidence that
        /// the pawn can no longer match the saved birthday; this helper never guesses about live letters.
        /// </summary>
        public static bool IsPastFallbackGrace(
            PendingBiotechGrowthMoment row,
            int currentTick,
            int graceTicks)
        {
            if (row == null || graceTicks < 0)
            {
                return false;
            }

            int now = Math.Max(0, currentTick);
            int started = Math.Max(0, row.configuredTick);
            return now >= started && now - started >= graceTicks;
        }

        private static PendingBiotechGrowthMoment NormalizeRow(
            PendingBiotechGrowthMoment row,
            int currentTick)
        {
            if (row == null)
            {
                return null;
            }

            row.pawnId = (row.pawnId ?? string.Empty).Trim();
            if (row.pawnId.Length == 0 || row.pawnId.IndexOf('|') >= 0
                || BiotechGrowthStageTokens.ForAge(row.birthdayAge).Length == 0)
            {
                return null;
            }

            row.birthdayTick = ClampTick(row.birthdayTick, currentTick);
            row.configuredTick = ClampTick(row.configuredTick, currentTick);
            if (row.configuredTick < row.birthdayTick)
            {
                row.configuredTick = row.birthdayTick;
            }

            row.growthTier = Math.Max(0, Math.Min(8, row.growthTier));
            row.correlationId = BiotechArcKeys.GrowthCorrelation(row.pawnId, row.birthdayAge);
            row.familyArcId = (row.familyArcId ?? string.Empty).Trim();
            if (row.familyArcId.Length == 0 || row.familyArcId.IndexOf('|') < 0)
            {
                row.familyArcId = BiotechArcKeys.FamilyFromChild(row.pawnId);
            }
            row.birthdaySnapshot = NormalizeSnapshot(
                row.birthdaySnapshot,
                row.pawnId,
                row.birthdayAge,
                row.growthTier,
                row.birthdayTick);
            return row.birthdaySnapshot == null ? null : row;
        }

        private static GrowthPawnSnapshot NormalizeSnapshot(
            GrowthPawnSnapshot snapshot,
            string pawnId,
            int birthdayAge,
            int growthTier,
            int birthdayTick)
        {
            if (snapshot == null)
            {
                return null;
            }

            snapshot.pawnId = pawnId;
            snapshot.displayName = (snapshot.displayName ?? string.Empty).Trim();
            snapshot.biologicalAge = birthdayAge;
            snapshot.growthTier = growthTier;
            snapshot.shortName = (snapshot.shortName ?? string.Empty).Trim();
            snapshot.capturedTick = ClampTick(snapshot.capturedTick, birthdayTick);
            snapshot.traits = NormalizeTraits(snapshot.traits);
            snapshot.skills = NormalizeSkills(snapshot.skills);
            return snapshot;
        }

        private static List<GrowthTraitFact> NormalizeTraits(IList<GrowthTraitFact> source)
        {
            List<GrowthTraitFact> result = new List<GrowthTraitFact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                GrowthTraitFact fact = source[i];
                string key = (fact?.traitKey ?? string.Empty).Trim();
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                fact.traitKey = key;
                fact.label = (fact.label ?? string.Empty).Trim();
                fact.description = (fact.description ?? string.Empty).Trim();
                result.Add(fact);
            }

            result.Sort((left, right) => string.CompareOrdinal(left.traitKey, right.traitKey));
            return result;
        }

        private static List<GrowthSkillFact> NormalizeSkills(IList<GrowthSkillFact> source)
        {
            List<GrowthSkillFact> result = new List<GrowthSkillFact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                GrowthSkillFact fact = source[i];
                string key = (fact?.skillDefName ?? string.Empty).Trim();
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                fact.skillDefName = key;
                fact.label = (fact.label ?? string.Empty).Trim();
                fact.passion = BiotechPassionTokens.Normalize(fact.passion);
                fact.level = Math.Max(0, fact.level);
                result.Add(fact);
            }

            result.Sort((left, right) => string.CompareOrdinal(left.skillDefName, right.skillDefName));
            return result;
        }

        private static int ClampTick(int tick, int maximum)
        {
            return Math.Max(0, Math.Min(Math.Max(0, maximum), tick));
        }
    }

    /// <summary>Pure, fixed-order game-context formatting for a verified growth mutation.</summary>
    internal static class GrowthMomentContextFormatter
    {
        private const int MaximumInterestRows = 4;

        /// <summary>Builds bounded semicolon context containing only verified qualitative growth facts.</summary>
        public static string Build(
            GrowthMomentMutation mutation,
            string opportunityDescription,
            string upbringingDescription,
            string initiatorRole,
            string recipientRole,
            string newInterestDescription = "",
            string deepenedInterestDescription = "")
        {
            if (mutation == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            Append(builder, BiotechContextKeys.GrowthMoment, "true");
            Append(builder, BiotechContextKeys.ChildId, mutation.childId);
            Append(builder, BiotechContextKeys.BirthdayAge, mutation.age.ToString());
            Append(builder, BiotechContextKeys.GrowthStage, mutation.stageToken);
            Append(builder, BiotechContextKeys.FamilyArcId, mutation.familyArcId);
            Append(builder, BiotechContextKeys.OpportunityBand, mutation.opportunityBand);
            Append(builder, BiotechContextKeys.OpportunityDescription, opportunityDescription);

            if (mutation.supporter != null)
            {
                Append(builder, BiotechContextKeys.UpbringingBand, mutation.supporter.observationBand);
                Append(builder, BiotechContextKeys.UpbringingDescription, upbringingDescription);
            }

            if (mutation.selectedTrait != null)
            {
                Append(builder, BiotechContextKeys.SelectedTrait, mutation.selectedTrait.label);
                Append(builder, BiotechContextKeys.SelectedTraitDescription, mutation.selectedTrait.description);
            }

            int interestCount = Math.Min(MaximumInterestRows, mutation.passionChanges?.Count ?? 0);
            for (int i = 0; i < interestCount; i++)
            {
                PassionMutation passion = mutation.passionChanges[i];
                int ordinal = i + 1;
                Append(builder, BiotechContextKeys.NewInterestPrefix + ordinal, passion.label);
                Append(builder, BiotechContextKeys.InterestChangePrefix + ordinal,
                    BiotechPassionTokens.Rank(passion.beforePassion) == 0
                        ? newInterestDescription
                        : deepenedInterestDescription);
            }

            if (mutation.nicknameChanged)
            {
                Append(builder, BiotechContextKeys.NicknameChanged, "true");
                Append(builder, BiotechContextKeys.PreviousName, mutation.previousShortName);
                Append(builder, BiotechContextKeys.CurrentName, mutation.currentShortName);
            }

            if (mutation.newResponsibilities)
            {
                Append(builder, BiotechContextKeys.NewResponsibilities, "true");
            }

            if (mutation.supporter != null)
            {
                Append(builder, BiotechContextKeys.SupporterId, mutation.supporter.adultId);
                Append(builder, BiotechContextKeys.SupporterName, mutation.supporter.displayName);
                Append(builder, BiotechContextKeys.SupporterRole, mutation.supporter.roleToken);
            }

            Append(builder, BiotechContextKeys.InitiatorFamilyRole, initiatorRole);
            Append(builder, BiotechContextKeys.RecipientFamilyRole, recipientRole);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string key, string value)
        {
            string safe = BiotechContextText.Clean(value);
            if (safe.Length == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(key).Append("=").Append(safe);
        }
    }

    /// <summary>Defensive one-line context cleaning shared by the pure B1 formatters.</summary>
    internal static class BiotechContextText
    {
        private const int MaximumValueCharacters = 240;

        /// <summary>Collapses delimiters and line breaks into bounded single-line context text.</summary>
        public static string Clean(string value)
        {
            string text = (value ?? string.Empty)
                .Replace(";", ",")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            return text.Length <= MaximumValueCharacters
                ? text
                : text.Substring(0, MaximumValueCharacters).TrimEnd();
        }
    }
}
