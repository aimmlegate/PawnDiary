// Strict pure parser for the small batched conversation-assessment schema. It first isolates one
// JSON array (optionally inside Markdown fences), then uses the framework DataContract serializer.
// Unknown aliases/tokens are ignored, missing candidates become ignore, and malformed output never
// creates a diary entry. No external JSON package is required on RimWorld's Mono runtime.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Stable machine-schema tokens shared by the parser, planner, and runtime coordinator.</summary>
    public static class ConversationAssessmentTokens
    {
        public const string Ignore = "ignore";
        public const string Related = "related";
        public const string Standalone = "standalone";

        private static readonly HashSet<string> Reasons = new HashSet<string>(StringComparer.Ordinal)
        {
            "echo", "banter", "disclosure", "commitment", "conflict", "reconciliation",
            "rejection", "other"
        };

        public static bool IsDecision(string value)
        {
            return value == Ignore || value == Related || value == Standalone;
        }

        public static bool IsReason(string value)
        {
            return value != null && Reasons.Contains(value);
        }
    }

    /// <summary>
    /// Code-owned English machine contract. Editorial instructions may be localized or player-edited,
    /// but these parser tokens must never be translated or removed from the actual system prompt.
    /// </summary>
    public static class ConversationAssessmentWireContract
    {
        public const string SystemPromptPrefix =
            "MACHINE OUTPUT CONTRACT (follow exactly): Return one object per supplied conversation as a JSON array only. "
            + "Every object must have exactly these fields: "
            + "{\"id\":\"c1\",\"decision\":\"ignore\",\"event\":\"\",\"reason\":\"banter\",\"focus\":\"\"}. "
            + "decision must be ignore, related, or standalone. reason must be echo, banter, disclosure, "
            + "commitment, conflict, reconciliation, rejection, or other. related requires one supplied eN "
            + "event alias; every other decision requires an empty event. related and standalone require a "
            + "short explicit focus; ignore requires an empty focus.";

        /// <summary>Prefixes the immutable schema so downstream defensive prompt caps preserve it.</summary>
        public static string Compose(string editorialInstructions)
        {
            string editorial = editorialInstructions == null ? string.Empty : editorialInstructions.Trim();
            return editorial.Length == 0
                ? SystemPromptPrefix
                : SystemPromptPrefix + "\n\nEDITORIAL POLICY:\n" + editorial;
        }
    }

    /// <summary>One validated result, mapped back from aliases to actual stable ids.</summary>
    public sealed class ConversationAssessmentResult
    {
        public string ConversationId;
        public string Decision;
        public string EventId;
        public string Reason;
        public string Focus;
    }

    /// <summary>Whole-parser outcome; Success=false means the batch failed conservatively.</summary>
    public sealed class ConversationAssessmentParseResult
    {
        public bool Success;
        public string Error;
        public readonly List<ConversationAssessmentResult> Results = new List<ConversationAssessmentResult>();
    }

    /// <summary>Extracts and validates the compact assessment response.</summary>
    public static class ConversationAssessmentResponseParser
    {
        /// <summary>
        /// Parses one response. A syntactically valid array always returns one result per active
        /// candidate; omitted or invalid rows are filled with ignore.
        /// </summary>
        public static ConversationAssessmentParseResult Parse(
            string response,
            ConversationAssessmentBatch batch,
            int maxFocusChars)
        {
            ConversationAssessmentParseResult parsed = new ConversationAssessmentParseResult();
            if (batch == null || batch.CandidateAliases.Count == 0)
            {
                parsed.Error = "missing_batch";
                return parsed;
            }

            string json = ExtractFirstJsonArray(response);
            if (json.Length == 0)
            {
                parsed.Error = "missing_json_array";
                return parsed;
            }

            List<WireResult> rows;
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<WireResult>));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    rows = serializer.ReadObject(stream) as List<WireResult>;
                }
            }
            catch (Exception)
            {
                parsed.Error = "malformed_json";
                return parsed;
            }

            if (rows == null)
            {
                parsed.Error = "wrong_schema";
                return parsed;
            }

            Dictionary<string, ConversationAssessmentResult> byAlias =
                new Dictionary<string, ConversationAssessmentResult>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                WireResult row = rows[i];
                string alias = Trim(row != null ? row.Id : null);
                if (alias.Length == 0 || byAlias.ContainsKey(alias) || !batch.CandidateByAlias.ContainsKey(alias))
                {
                    continue;
                }

                string decision = Trim(row.Decision);
                string reason = Trim(row.Reason);
                string eventAlias = Trim(row.Event);
                string focus = UnicodeText.CapUtf16(UnicodeText.CleanOneLine(row.Focus), maxFocusChars);
                if (!ConversationAssessmentTokens.IsDecision(decision)
                    || !ConversationAssessmentTokens.IsReason(reason))
                {
                    continue;
                }

                if (decision != ConversationAssessmentTokens.Related && eventAlias.Length > 0)
                {
                    // A standalone/ignore row that names an event contradicts its own schema. Fail
                    // that candidate closed instead of silently changing the model's classification.
                    continue;
                }

                RecentDiaryEvent actualEvent = null;
                if (eventAlias.Length > 0)
                {
                    HashSet<string> allowed;
                    if (!batch.AllowedEventAliasesByCandidateAlias.TryGetValue(alias, out allowed)
                        || !allowed.Contains(eventAlias)
                        || !batch.EventByAlias.TryGetValue(eventAlias, out actualEvent))
                    {
                        continue;
                    }
                }

                if (decision == ConversationAssessmentTokens.Related && actualEvent == null)
                {
                    continue;
                }

                if ((decision == ConversationAssessmentTokens.Related
                    || decision == ConversationAssessmentTokens.Standalone) && focus.Length == 0)
                {
                    continue;
                }

                QueuedConversationCandidate candidate = batch.CandidateByAlias[alias];
                byAlias[alias] = new ConversationAssessmentResult
                {
                    ConversationId = candidate.ConversationId,
                    Decision = decision,
                    EventId = decision == ConversationAssessmentTokens.Related && actualEvent != null
                        ? actualEvent.EventId : string.Empty,
                    Reason = reason,
                    Focus = decision == ConversationAssessmentTokens.Ignore ? string.Empty : focus
                };
            }

            // Alias order is formatter order. This makes duplicate/missing handling deterministic and
            // lets the runtime apply outcomes without depending on dictionary enumeration order.
            for (int i = 0; i < batch.CandidateAliases.Count; i++)
            {
                string alias = batch.CandidateAliases[i];
                ConversationAssessmentResult result;
                if (!byAlias.TryGetValue(alias, out result))
                {
                    result = IgnoreFor(batch.CandidateByAlias[alias]);
                }

                parsed.Results.Add(result);
            }

            parsed.Success = true;
            return parsed;
        }

        /// <summary>Returns the first balanced JSON array outside quoted strings, or empty on failure.</summary>
        public static string ExtractFirstJsonArray(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            for (int start = 0; start < response.Length; start++)
            {
                if (response[start] != '[')
                {
                    continue;
                }

                bool inString = false;
                bool escaping = false;
                int squareDepth = 0;
                int objectDepth = 0;
                for (int i = start; i < response.Length; i++)
                {
                    char c = response[i];
                    if (inString)
                    {
                        if (escaping)
                        {
                            escaping = false;
                        }
                        else if (c == '\\')
                        {
                            escaping = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                    }
                    else if (c == '[')
                    {
                        squareDepth++;
                    }
                    else if (c == ']')
                    {
                        squareDepth--;
                        if (squareDepth == 0 && objectDepth == 0)
                        {
                            return response.Substring(start, i - start + 1);
                        }

                        if (squareDepth < 0)
                        {
                            break;
                        }
                    }
                    else if (c == '{')
                    {
                        objectDepth++;
                    }
                    else if (c == '}')
                    {
                        objectDepth--;
                        if (objectDepth < 0)
                        {
                            break;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private static ConversationAssessmentResult IgnoreFor(QueuedConversationCandidate candidate)
        {
            return new ConversationAssessmentResult
            {
                ConversationId = candidate != null ? candidate.ConversationId : string.Empty,
                Decision = ConversationAssessmentTokens.Ignore,
                EventId = string.Empty,
                Reason = "other",
                Focus = string.Empty
            };
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        [DataContract]
        private sealed class WireResult
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "decision")]
            public string Decision { get; set; }

            [DataMember(Name = "event")]
            public string Event { get; set; }

            [DataMember(Name = "reason")]
            public string Reason { get; set; }

            [DataMember(Name = "focus")]
            public string Focus { get; set; }
        }
    }
}
