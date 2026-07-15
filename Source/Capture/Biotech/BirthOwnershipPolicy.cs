// Pure birth-writer selection and context formatting. The canonical live boundary supplies exact
// participant roles and outcomes; this policy merely de-duplicates adults, applies the frozen role
// order, caps writers at two, and formats event-time facts without assuming joy or intent.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>Deterministic writer selection: birther, distinct genetic mother, then father.</summary>
    internal static class BirthOwnershipPolicy
    {
        /// <summary>Selects up to two distinct eligible adults in frozen family-role priority order.</summary>
        public static BirthWriterSelection SelectWriters(
            BirthMutationSnapshot snapshot,
            BiotechPolicySnapshot policy)
        {
            BirthWriterSelection selection = new BirthWriterSelection();
            if (snapshot == null)
            {
                return selection;
            }

            int configuredMaximum = policy?.maximumBirthWriters ?? 2;
            int maximum = configuredMaximum < 1 || configuredMaximum > 2 ? 2 : configuredMaximum;
            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            Add(selection, selectedIds, snapshot.birther, BiotechFamilyRoleTokens.Birther, maximum);
            Add(selection, selectedIds, snapshot.geneticMother, BiotechFamilyRoleTokens.GeneticMother, maximum);
            Add(selection, selectedIds, snapshot.father, BiotechFamilyRoleTokens.Father, maximum);
            return selection;
        }

        private static void Add(
            BirthWriterSelection selection,
            HashSet<string> selectedIds,
            FamilyParticipantFact participant,
            string role,
            int maximum)
        {
            if (selection.writers.Count >= maximum || participant == null || !participant.eligible)
            {
                return;
            }

            string id = (participant.pawnId ?? string.Empty).Trim();
            if (id.Length == 0 || !selectedIds.Add(id))
            {
                return;
            }

            selection.writers.Add(new BirthWriterFact
            {
                pawnId = id,
                displayName = participant.displayName ?? string.Empty,
                roleToken = role
            });
        }
    }

    /// <summary>Pure, fixed-order shared context for the canonical birth event.</summary>
    internal static class BirthContextFormatter
    {
        /// <summary>Builds bounded birth context without persistence deadlines, ticks, or guessed roles.</summary>
        public static string Build(BirthMutationSnapshot snapshot, BirthWriterSelection writers)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            Append(builder, BiotechContextKeys.FamilyBirth, "true");
            Append(builder, BiotechContextKeys.FamilyArcId, snapshot.familyArcId);
            Append(builder, BiotechContextKeys.ChildId, snapshot.childId);
            Append(builder, BiotechContextKeys.ChildName, snapshot.currentChildName);
            Append(builder, BiotechContextKeys.BirthOutcome, snapshot.outcomeToken);
            Append(builder, BiotechContextKeys.BirthMethod, snapshot.methodToken);
            AppendParticipant(builder, snapshot.birther, "birther");
            AppendParticipant(builder, snapshot.geneticMother, "genetic_mother");
            AppendParticipant(builder, snapshot.father, "father");
            AppendParticipant(builder, snapshot.doctor, "doctor");
            if (snapshot.birtherDied)
            {
                Append(builder, BiotechContextKeys.BirtherDied, "true");
            }
            if (snapshot.ritualBirth)
            {
                Append(builder, BiotechContextKeys.RitualBirth, "true");
            }

            if (writers != null && writers.writers.Count > 0)
            {
                Append(builder, BiotechContextKeys.InitiatorFamilyRole, writers.writers[0].roleToken);
                if (writers.writers.Count > 1)
                {
                    Append(builder, BiotechContextKeys.RecipientFamilyRole, writers.writers[1].roleToken);
                }
            }

            return builder.ToString();
        }

        private static void AppendParticipant(
            StringBuilder builder,
            FamilyParticipantFact participant,
            string keyPrefix)
        {
            if (participant == null)
            {
                return;
            }

            Append(builder, keyPrefix + "_id", participant.pawnId);
            Append(builder, keyPrefix + "_name", participant.displayName);
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

    /// <summary>Pure migration policy for new group settings inheriting explicit legacy intent.</summary>
    internal static class BiotechSettingsInheritance
    {
        /// <summary>Prefers the new growth override, then explicit birthday intent, then the new default.</summary>
        public static bool GrowthEnabled(bool? newGroupOverride, bool? birthdayOverride, bool newGroupDefault)
        {
            if (newGroupOverride.HasValue) return newGroupOverride.Value;
            if (birthdayOverride.HasValue) return birthdayOverride.Value;
            return newGroupDefault;
        }

        /// <summary>Resolves family-birth intent while preserving explicit nonritual or ritual choices.</summary>
        public static bool FamilyBirthEnabled(
            bool? newGroupOverride,
            bool? taleLifeOverride,
            bool? ritualChildbirthOverride,
            bool ritualBirth,
            bool newGroupDefault)
        {
            if (newGroupOverride.HasValue)
            {
                return newGroupOverride.Value;
            }

            if (!ritualBirth)
            {
                return taleLifeOverride.HasValue ? taleLifeOverride.Value : newGroupDefault;
            }

            if (taleLifeOverride == true || ritualChildbirthOverride == true)
            {
                return true;
            }

            if (taleLifeOverride.HasValue || ritualChildbirthOverride.HasValue)
            {
                return false;
            }

            return newGroupDefault;
        }
    }
}
