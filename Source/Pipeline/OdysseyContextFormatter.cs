// Pure, bounded prompt-context formatting for one Odyssey landing. Only visible event-time labels and
// qualitative schema tokens are emitted; stable IDs, tiles, coordinates, ticks, fuel, and subsystem
// mechanics never enter the result.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>Formats the frozen §15.3 Odyssey schema as semicolon-separated context facts.</summary>
    internal static class OdysseyContextFormatter
    {
        // Pair events own one saved gameContext, so these two internal facts preserve each DiaryEvent
        // role until the pure prompt planner projects the one truthful pov_journey_role for its POV.
        internal const string InitiatorJourneyRoleKey = "odyssey_initiator_role";
        internal const string RecipientJourneyRoleKey = "odyssey_recipient_role";

        /// <summary>Builds one POV's bounded context, preserving core reason/role facts before extras.</summary>
        public static string FormatLanding(
            OdysseyJourneySnapshot journey,
            OdysseyLocationSnapshot destination,
            OdysseyLandingPlan plan,
            string povPawnId,
            OdysseyPolicySnapshot policy)
        {
            return FormatLandingCore(
                journey,
                destination,
                plan,
                RoleFor(plan?.selectedWriters, povPawnId),
                string.Empty,
                string.Empty,
                policy);
        }

        /// <summary>
        /// Builds one shared pair-event context while preserving both event-role-to-journey-role mappings.
        /// The prompt planner replaces these internal facts with one POV-specific public schema field.
        /// </summary>
        public static string FormatLandingPair(
            OdysseyJourneySnapshot journey,
            OdysseyLocationSnapshot destination,
            OdysseyLandingPlan plan,
            string initiatorPawnId,
            string recipientPawnId,
            OdysseyPolicySnapshot policy)
        {
            return FormatLandingCore(
                journey,
                destination,
                plan,
                string.Empty,
                RoleFor(plan?.selectedWriters, initiatorPawnId),
                RoleFor(plan?.selectedWriters, recipientPawnId),
                policy);
        }

        private static string FormatLandingCore(
            OdysseyJourneySnapshot journey,
            OdysseyLocationSnapshot destination,
            OdysseyLandingPlan plan,
            string povJourneyRole,
            string initiatorJourneyRole,
            string recipientJourneyRole,
            OdysseyPolicySnapshot policy)
        {
            if (journey == null || plan == null || string.IsNullOrWhiteSpace(plan.primaryReason))
            {
                return string.Empty;
            }

            OdysseyPolicySnapshot effective = policy ?? OdysseyPolicySnapshot.CreateDefault();
            int maximum = effective.maximumContextCharacters > 0
                ? effective.maximumContextCharacters
                : 900;
            int valueMaximum = effective.maximumContextValueCharacters > 0
                ? effective.maximumContextValueCharacters
                : 120;
            OdysseyLocationSnapshot finalDestination = destination ?? journey.destination;
            List<string> fields = new List<string>();

            // Central facts go first so a deliberately small XML budget drops decoration, not truth.
            Add(fields, "odyssey_journey", "true", maximum, valueMaximum);
            Add(fields, "journey_phase", "landing", maximum, valueMaximum);
            Add(fields, "journey_reason", plan.primaryReason, maximum, valueMaximum);
            Add(fields, "journey_secondary_reason", plan.secondaryReason, maximum, valueMaximum);
            Add(fields, "journey_duration", plan.durationBand, maximum, valueMaximum);
            Add(fields, "pov_journey_role", povJourneyRole, maximum, valueMaximum);
            Add(fields, InitiatorJourneyRoleKey, initiatorJourneyRole, maximum, valueMaximum);
            Add(fields, RecipientJourneyRoleKey, recipientJourneyRole, maximum, valueMaximum);
            Add(fields, "rough_landing", journey.roughLanding ? "true" : "false", maximum, valueMaximum);
            Add(fields, "launch_quality", journey.launchQualityBand, maximum, valueMaximum);

            Add(fields, "ship_name", journey.shipName, maximum, valueMaximum);
            Add(fields, "origin", LocationLabel(journey.origin), maximum, valueMaximum);
            Add(fields, "destination", LocationLabel(finalDestination), maximum, valueMaximum);
            Add(fields, "landing_outcome", journey.landingOutcomeLabel, maximum, valueMaximum);
            Add(fields, "destination_layer",
                finalDestination == null || !finalDestination.visible
                    ? string.Empty
                    : OdysseyLocationLayerTokens.Normalize(finalDestination.layerToken),
                maximum, valueMaximum);
            Add(fields, "destination_biome",
                finalDestination == null || !finalDestination.visible
                    ? string.Empty
                    : finalDestination.biomeLabel,
                maximum, valueMaximum);
            Add(fields, "destination_site",
                finalDestination == null || !finalDestination.visible
                    ? string.Empty
                    : finalDestination.siteLabel,
                maximum, valueMaximum);
            Add(fields, "pilot", WriterName(journey.writers, OdysseyJourneyRoleTokens.Pilot),
                maximum, valueMaximum);
            Add(fields, "copilot", WriterName(journey.writers, OdysseyJourneyRoleTokens.Copilot),
                maximum, valueMaximum);
            Add(fields, "crew_count", CrewCount(journey.writers).ToString(), maximum, valueMaximum);
            return string.Join("; ", fields.ToArray());
        }

        /// <summary>
        /// Replaces shared pair-only role mappings with the one public role fact for this prompt. The
        /// result never grows beyond the saved context because two internal facts become at most one.
        /// </summary>
        internal static string ProjectPairRoleForPov(string context, string diaryPovRole)
        {
            if (string.IsNullOrWhiteSpace(context)
                || !string.IsNullOrWhiteSpace(ContextValue(context, "pov_journey_role")))
            {
                return context ?? string.Empty;
            }

            string selectedKey = string.Equals(
                diaryPovRole,
                "recipient",
                StringComparison.OrdinalIgnoreCase)
                    ? RecipientJourneyRoleKey
                    : InitiatorJourneyRoleKey;
            string journeyRole = ContextValue(context, selectedKey);
            List<string> projected = new List<string>();
            string[] facts = context.Split(';');
            for (int i = 0; i < facts.Length; i++)
            {
                string fact = facts[i].Trim();
                if (fact.Length == 0
                    || IsContextKey(fact, InitiatorJourneyRoleKey)
                    || IsContextKey(fact, RecipientJourneyRoleKey))
                {
                    continue;
                }
                projected.Add(fact);
            }

            if (OdysseyJourneyRoleTokens.Rank(journeyRole) != int.MaxValue)
            {
                projected.Add("pov_journey_role=" + journeyRole);
            }
            return string.Join("; ", projected.ToArray());
        }

        private static bool IsContextKey(string fact, string key)
        {
            int separator = fact.IndexOf('=');
            return separator > 0
                && string.Equals(fact.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase);
        }

        private static string ContextValue(string context, string key)
        {
            if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(key)) return string.Empty;
            string[] facts = context.Split(';');
            for (int i = 0; i < facts.Length; i++)
            {
                string fact = facts[i].Trim();
                int separator = fact.IndexOf('=');
                if (separator > 0
                    && string.Equals(
                        fact.Substring(0, separator).Trim(),
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return fact.Substring(separator + 1).Trim();
                }
            }
            return string.Empty;
        }

        private static void Add(
            List<string> fields,
            string key,
            string rawValue,
            int maximum,
            int valueMaximum)
        {
            string value = CleanValue(rawValue, valueMaximum);
            if (value.Length == 0) return;
            string field = key + "=" + value;
            int currentLength = JoinedLength(fields);
            int added = fields.Count == 0 ? field.Length : 2 + field.Length;
            if (currentLength + added <= maximum)
            {
                fields.Add(field);
            }
        }

        private static int JoinedLength(List<string> fields)
        {
            int result = 0;
            for (int i = 0; i < fields.Count; i++)
            {
                result += fields[i].Length;
                if (i > 0) result += 2;
            }

            return result;
        }

        private static string LocationLabel(OdysseyLocationSnapshot location)
        {
            if (location == null || !location.visible) return string.Empty;
            if (!string.IsNullOrWhiteSpace(location.visibleLabel)) return location.visibleLabel;
            if (!string.IsNullOrWhiteSpace(location.siteLabel)) return location.siteLabel;
            return location.biomeLabel ?? string.Empty;
        }

        private static string RoleFor(List<OdysseyWriterCandidate> writers, string pawnId)
        {
            if (writers == null || string.IsNullOrWhiteSpace(pawnId)) return string.Empty;
            for (int i = 0; i < writers.Count; i++)
            {
                OdysseyWriterCandidate writer = writers[i];
                if (writer != null && string.Equals(writer.pawnId, pawnId, StringComparison.Ordinal)
                    && OdysseyJourneyRoleTokens.Rank(writer.roleToken) != int.MaxValue)
                {
                    return writer.roleToken;
                }
            }

            return string.Empty;
        }

        private static string WriterName(List<OdysseyWriterCandidate> writers, string role)
        {
            if (writers == null) return string.Empty;
            OdysseyWriterCandidate best = null;
            for (int i = 0; i < writers.Count; i++)
            {
                OdysseyWriterCandidate writer = writers[i];
                if (writer == null || writer.roleToken != role || string.IsNullOrWhiteSpace(writer.pawnId))
                {
                    continue;
                }

                if (best == null || string.Compare(writer.pawnId, best.pawnId, StringComparison.Ordinal) < 0)
                {
                    best = writer;
                }
            }

            return best == null ? string.Empty : best.displayName;
        }

        private static int CrewCount(List<OdysseyWriterCandidate> writers)
        {
            if (writers == null) return 0;
            List<string> ids = new List<string>();
            for (int i = 0; i < writers.Count; i++)
            {
                OdysseyWriterCandidate writer = writers[i];
                if (writer == null || !writer.present || string.IsNullOrWhiteSpace(writer.pawnId)) continue;
                bool found = false;
                for (int j = 0; j < ids.Count; j++)
                {
                    if (string.Equals(ids[j], writer.pawnId, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found) ids.Add(writer.pawnId);
            }

            return ids.Count;
        }

        /// <summary>Collapses whitespace, removes context delimiters/control characters, and caps text.</summary>
        internal static string CleanValue(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder builder = new StringBuilder(value.Length);
            bool previousSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsControl(c) || c == ';' || c == '=') c = ' ';
                if (char.IsWhiteSpace(c))
                {
                    if (previousSpace) continue;
                    c = ' ';
                    previousSpace = true;
                }
                else
                {
                    previousSpace = false;
                }

                builder.Append(c);
                if (maximum > 0 && builder.Length >= maximum) break;
            }

            return builder.ToString().Trim();
        }
    }
}
