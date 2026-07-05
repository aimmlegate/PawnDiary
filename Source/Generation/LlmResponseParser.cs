// Pure model-response parsing and cleanup. LlmClient owns HTTP, retries, failover, and thread
// handoff; this file owns the deterministic text work after a response body has been received.
//
// "Pure" here means: no RimWorld / Verse / Unity types, no DefDatabase, no .Translate(), no RNG,
// no IO. Tests can compile this file with MiniJson alone, without loading the game assemblies.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// The response shape an API lane speaks. Kept separate from the settings enum so this parser
    /// stays independent from RimWorld settings/save-load types.
    /// </summary>
    internal enum LlmResponseMode
    {
        OpenAIChatCompletions,
        OpenAIResponses
    }

    /// <summary>
    /// Stateless helpers that extract visible model text, surface provider errors, strip known
    /// reasoning blocks/self-edit transcripts, and apply the local length/sentence cleanup before
    /// diary text is saved.
    /// </summary>
    internal static class LlmResponseParser
    {
        /// <summary>
        /// Parses the endpoint JSON body into the dictionary shape used by the response extractor.
        /// Returns null when the body is valid JSON but not an object.
        /// </summary>
        public static Dictionary<string, object> ParseResponseRoot(string json)
        {
            return MiniJson.Deserialize(json ?? string.Empty) as Dictionary<string, object>;
        }

        /// <summary>
        /// Extracts the generated text from a compatible endpoint response.
        /// </summary>
        public static string ParseGeneratedText(Dictionary<string, object> root, LlmResponseMode mode)
        {
            if (root == null)
            {
                return null;
            }

            switch (mode)
            {
                case LlmResponseMode.OpenAIResponses:
                    return ParseOpenAIResponsesText(root) ?? ParseOpenAIChatText(root);
                default:
                    return ParseOpenAIChatText(root);
            }
        }

        /// <summary>
        /// Pulls provider-level errors/incomplete statuses from successful HTTP responses before the
        /// generic "empty message" path hides the useful reason.
        /// </summary>
        public static string ExtractProviderError(Dictionary<string, object> root, LlmResponseMode mode, bool hasGeneratedText)
        {
            if (root == null)
            {
                return "The endpoint did not return a JSON object.";
            }

            if (root.TryGetValue("error", out object errorObject))
            {
                string error = ErrorDetail(errorObject);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return "API error: " + error;
                }
            }

            switch (mode)
            {
                case LlmResponseMode.OpenAIResponses:
                    return ExtractOpenAIResponsesStatusError(root, hasGeneratedText);
                default:
                    return ExtractOpenAIChatStatusError(root, hasGeneratedText);
            }
        }

        /// <summary>
        /// Removes reasoning/transcript blocks that some "compatible" APIs place inside normal
        /// message content. This keeps private thinking text out of saved diary pages and debug UI.
        /// Uses the built-in broad tag detection ("auto").
        /// </summary>
        public static string StripReasoningTextBlocks(string text)
        {
            return StripReasoningTextBlocks(text, ApiEndpointPolicy.DefaultReasoningTag);
        }

        /// <summary>
        /// Removes reasoning/transcript blocks that some "compatible" APIs place inside normal
        /// message content, additionally recognizing a lane-pinned tag name. "auto" keeps the
        /// built-in broad guess-list; any other known tag (think/thinking/reasoning/analysis/
        /// thought/reflection/scratchpad) is added ON TOP of the base list so broad coverage
        /// survives as a safety net. This keeps private thinking text out of saved diary pages.
        /// </summary>
        public static string StripReasoningTextBlocks(string text, string reasoningTag)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string[] tags = ReasoningTagsToStrip(reasoningTag);
            string cleaned = StripTaggedReasoningBlocks(text, tags);
            cleaned = StripOrphanClosingReasoningTags(cleaned, tags);
            cleaned = StripReasoningFencedBlocks(cleaned, tags);
            cleaned = StripReasoningHeadingPrefix(cleaned, tags);
            cleaned = StripInstructionReflectionTranscript(cleaned);
            cleaned = StripWholeResponseCodeFence(cleaned);
            cleaned = StripLeadingFinalAnswerLabel(cleaned);
            return CompactReasoningCleanupWhitespace(cleaned).Trim();
        }

        /// <summary>
        /// The tag names the strippers look for: the built-in Auto list (think/thinking/reasoning/
        /// analysis/thought/reflection/scratchpad) plus, when the lane pins a specific non-auto tag,
        /// that tag prepended so it is tried first. The base list is NEVER removed -- a pinned tag
        /// only adds priority for a tag already covered by Auto, or adds an exotic one not in the
        /// list. "auto" yields the base list unchanged, which is broad enough that most players never
        /// need to pick a tag manually. False-positive risk is negligible: the strippers only act on
        /// the wrapper form (&lt;tag&gt;...&lt;/tag&gt;), fenced ```tag blocks, and "Tag:" headings --
        /// never on the bare word in prose, so a pawn writing "my reflections on the raid" is safe.
        /// </summary>
        private static string[] ReasoningTagsToStrip(string reasoningTag)
        {
            string normalized = ApiEndpointPolicy.NormalizeReasoningTag(reasoningTag);
            string[] baseTags = { "think", "thinking", "reasoning", "analysis", "thought", "reflection", "scratchpad" };
            if (string.Equals(normalized, ApiEndpointPolicy.DefaultReasoningTag, StringComparison.Ordinal))
            {
                return baseTags;
            }

            // Prepend the pinned tag unless it is already one of the base tags (case-insensitive).
            for (int i = 0; i < baseTags.Length; i++)
            {
                if (string.Equals(baseTags[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return baseTags;
                }
            }

            string[] combined = new string[baseTags.Length + 1];
            combined[0] = normalized;
            Array.Copy(baseTags, 0, combined, 1, baseTags.Length);
            return combined;
        }

        /// <summary>
        /// Applies local response cleanup before text is saved: length cap first, marker/tag
        /// sanitizing second, then trailing fragment removal for diary/note text. Title requests
        /// skip sentence-fragment trimming.
        /// </summary>
        public static string CleanGeneratedText(string text, int maxTokens, bool isTitleRequest)
        {
            string capped = TrimToMaxTokens(text, maxTokens);
            string sanitized = SanitizeGeneratedMarkup(capped);
            return isTitleRequest ? sanitized : TrimTrailingIncompleteSentence(sanitized);
        }

        /// <summary>
        /// Returns a model title only when it looks like a plain one-line title. Some compatible
        /// endpoints leak schema/tag tokens such as &lt;uncensored_response&gt;; those are not useful
        /// player-facing headers, so title callers can fall back to a generic excerpt from the entry.
        /// </summary>
        public static string TitleOrFallback(string generatedTitle, string entryText)
        {
            string title = CleanGeneratedText(StripReasoningTextBlocks(generatedTitle), 0, true);
            if (IsUsableGeneratedTitle(title))
            {
                return title;
            }

            return GenericTitleFromText(entryText, GenericTitleFallbackWords);
        }

        /// <summary>Supports the standard choices[0].message.content chat-completions shape.</summary>
        private static string ParseOpenAIChatText(Dictionary<string, object> root)
        {
            Dictionary<string, object> firstChoice = FirstChoice(root);
            if (firstChoice == null)
            {
                return null;
            }

            if (firstChoice.TryGetValue("message", out object messageObject))
            {
                Dictionary<string, object> message = messageObject as Dictionary<string, object>;
                if (message != null && message.TryGetValue("content", out object contentObject))
                {
                    return TextFromContentObject(contentObject);
                }
            }

            return null;
        }

        /// <summary>
        /// Supports OpenAI Responses' typed output array, plus the convenience output_text field when a
        /// compatible proxy includes it. The typed array wins because it lets us skip reasoning items
        /// before any proxy-flattened text can leak thinking into the diary.
        /// </summary>
        private static string ParseOpenAIResponsesText(Dictionary<string, object> root)
        {
            string outputText = StringField(root, "output_text");

            if (!root.TryGetValue("output", out object outputObject))
            {
                return string.IsNullOrWhiteSpace(outputText) ? null : outputText;
            }

            object[] output = outputObject as object[];
            if (output == null)
            {
                return string.IsNullOrWhiteSpace(outputText) ? null : outputText;
            }

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < output.Length; i++)
            {
                Dictionary<string, object> item = output[i] as Dictionary<string, object>;
                if (IsReasoningResponseItem(item))
                {
                    continue;
                }

                if (item == null || !item.TryGetValue("content", out object contentObject))
                {
                    continue;
                }

                string contentText = TextFromContentObject(contentObject);
                if (string.IsNullOrWhiteSpace(contentText))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append("\n");
                }

                text.Append(contentText);
            }

            return text.Length > 0
                ? text.ToString()
                : (string.IsNullOrWhiteSpace(outputText) ? null : outputText);
        }

        private static string TextFromContentObject(object contentObject)
        {
            string direct = contentObject as string;
            if (direct != null)
            {
                return direct;
            }

            object[] parts = contentObject as object[];
            if (parts == null)
            {
                return null;
            }

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                Dictionary<string, object> part = parts[i] as Dictionary<string, object>;
                if (IsReasoningResponseItem(part))
                {
                    continue;
                }

                string partText = StringField(part, "text");
                if (string.IsNullOrWhiteSpace(partText))
                {
                    partText = StringField(part, "content");
                }
                if (string.IsNullOrWhiteSpace(partText))
                {
                    partText = StringField(part, "output_text");
                }

                if (string.IsNullOrWhiteSpace(partText))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append("\n");
                }

                text.Append(partText);
            }

            return text.Length > 0 ? text.ToString() : null;
        }

        private static string ExtractOpenAIResponsesStatusError(Dictionary<string, object> root, bool hasGeneratedText)
        {
            string status = StringField(root, "status").Trim().ToLowerInvariant();
            if (status == "failed" || status == "cancelled")
            {
                string detail = ErrorDetailFromField(root, "incomplete_details");
                return string.IsNullOrWhiteSpace(detail)
                    ? "Responses API status: " + status + "."
                    : "Responses API status: " + status + " (" + detail + ").";
            }

            if (status == "incomplete" && !hasGeneratedText)
            {
                string detail = ErrorDetailFromField(root, "incomplete_details");
                return string.IsNullOrWhiteSpace(detail)
                    ? "Responses API returned an incomplete response with no message content."
                    : "Responses API returned an incomplete response with no message content (" + detail + ").";
            }

            return null;
        }

        private static string ExtractOpenAIChatStatusError(Dictionary<string, object> root, bool hasGeneratedText)
        {
            Dictionary<string, object> firstChoice = FirstChoice(root);
            if (firstChoice == null)
            {
                return null;
            }

            string finishReason = StringField(firstChoice, "finish_reason").Trim().ToLowerInvariant();
            if (!hasGeneratedText && (finishReason == "content_filter" || finishReason == "length"))
            {
                return "Chat completion finished with no message content (finish_reason=" + finishReason + ").";
            }

            return null;
        }

        private static Dictionary<string, object> FirstChoice(Dictionary<string, object> root)
        {
            if (root == null || !root.TryGetValue("choices", out object choicesObject))
            {
                return null;
            }

            object[] choices = choicesObject as object[];
            if (choices == null || choices.Length == 0)
            {
                return null;
            }

            return choices[0] as Dictionary<string, object>;
        }

        private static string ErrorDetailFromField(Dictionary<string, object> root, string fieldName)
        {
            if (root == null || !root.TryGetValue(fieldName, out object value))
            {
                return null;
            }

            return ErrorDetail(value);
        }

        private static string ErrorDetail(object value)
        {
            if (value == null)
            {
                return null;
            }

            string text = value as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            Dictionary<string, object> fields = value as Dictionary<string, object>;
            if (fields == null)
            {
                return null;
            }

            string message = StringField(fields, "message");
            string reason = StringField(fields, "reason");
            string code = StringField(fields, "code");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message.Trim();
            }

            if (!string.IsNullOrWhiteSpace(reason) && !string.IsNullOrWhiteSpace(code))
            {
                return reason.Trim() + ", code=" + code.Trim();
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                return "reason=" + reason.Trim();
            }

            return string.IsNullOrWhiteSpace(code) ? null : "code=" + code.Trim();
        }

        private static string StringField(Dictionary<string, object> fields, string fieldName)
        {
            if (fields == null || !fields.TryGetValue(fieldName, out object value))
            {
                return string.Empty;
            }

            return value as string ?? string.Empty;
        }

        private static bool IsReasoningResponseItem(Dictionary<string, object> item)
        {
            if (item == null || !item.TryGetValue("type", out object typeObject))
            {
                return false;
            }

            string type = (typeObject as string ?? string.Empty).Trim().ToLowerInvariant();
            return type == "reasoning"
                || type == "reasoning_text"
                || type == "reasoning_content"
                || type == "reasoning_summary"
                || type == "summary_text"
                || type == "thinking"
                || type == "thinking_text"
                || type == "analysis";
        }

        private static string StripTaggedReasoningBlocks(string text, string[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                string closeNeedle = "</" + tag + ">";
                int guard = 0;
                while (guard++ < 32)
                {
                    int open = IndexOfOpeningTag(text, tag);
                    if (open < 0)
                    {
                        break;
                    }

                    int openEnd = text.IndexOf('>', open);
                    int contentStart = openEnd >= 0 ? openEnd + 1 : open + tag.Length + 1;

                    int close = IndexOfOrdinalIgnoreCase(text, closeNeedle, contentStart);
                    if (close >= 0)
                    {
                        int closeEnd = close + closeNeedle.Length;
                        text = text.Remove(open, closeEnd - open);
                        continue;
                    }

                    // The exact close tag is missing. Before assuming everything to the end of the
                    // response is reasoning (which deletes a valid answer when the model MISMATCHED
                    // its tag names -- opened <thinking> but closed </think>, or opened <think> but
                    // closed </reasoning>), cut through the earliest close tag of ANY known reasoning
                    // name if one is present. Only fall through to the end-of-text heuristics when no
                    // recognizable closer exists at all.
                    int mismatchedCloseLength;
                    int mismatchedClose = IndexOfAnyClosingTag(text, tags, contentStart, out mismatchedCloseLength);
                    if (mismatchedClose >= 0)
                    {
                        text = text.Remove(open, (mismatchedClose + mismatchedCloseLength) - open);
                        continue;
                    }

                    int labelLength;
                    string remainder = text.Substring(Math.Min(contentStart, text.Length));
                    int finalRelative = FindLineStartingWithAny(remainder, FinalAnswerLabels(), out labelLength);
                    if (finalRelative >= 0)
                    {
                        int finalStart = contentStart + finalRelative;
                        text = text.Remove(open, finalStart - open);
                        continue;
                    }

                    int afterBlankLine = IndexAfterBlankLine(text, contentStart);
                    if (afterBlankLine >= 0)
                    {
                        text = text.Remove(open, afterBlankLine - open);
                        continue;
                    }

                    text = text.Remove(open);
                    break;
                }
            }

            return text;
        }

        /// <summary>
        /// Handles reasoning models whose chat template emits the opening think tag as part of the
        /// prompt, so the completion begins inside the reasoning block and only returns a closing tag.
        /// </summary>
        private static string StripOrphanClosingReasoningTags(string text, string[] tags)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            for (int i = 0; i < tags.Length; i++)
            {
                string closeNeedle = "</" + tags[i] + ">";
                // A model can emit more than one stray closing tag of the same name (e.g. it re-opens
                // its reasoning template mid-answer). Keep dropping leading orphan closers until the
                // next one has a real opening tag before it (a genuine block StripTaggedReasoningBlocks
                // owns) or none remain. `text` shrinks every iteration; the guard bounds it anyway.
                int guard = 0;
                while (guard++ < 32)
                {
                    int close = IndexOfOrdinalIgnoreCase(text, closeNeedle, 0);
                    if (close < 0)
                    {
                        break;
                    }

                    // If a real opening tag precedes this close, StripTaggedReasoningBlocks already
                    // handled it. Do not over-trim the visible answer here.
                    int open = IndexOfOpeningTag(text, tags[i]);
                    if (open >= 0 && open < close)
                    {
                        break;
                    }

                    // If nothing but whitespace follows the closer, the answer is BEFORE it (the model
                    // finished its entry, then leaked a stray close tag). Drop only the closer and keep
                    // the answer -- discarding everything before it would empty the entry, which
                    // SendOnce then reports as a permanent "no content" failure.
                    if (string.IsNullOrWhiteSpace(text.Substring(close + closeNeedle.Length)))
                    {
                        text = text.Remove(close, closeNeedle.Length);
                        break;
                    }

                    text = text.Substring(close + closeNeedle.Length);
                }
            }

            return text;
        }

        private static int IndexAfterBlankLine(string text, int startIndex)
        {
            for (int i = Math.Max(0, startIndex); i < text.Length - 1; i++)
            {
                if (text[i] == '\n' && text[i + 1] == '\n')
                {
                    return i + 2;
                }

                if (i < text.Length - 3
                    && text[i] == '\r'
                    && text[i + 1] == '\n'
                    && text[i + 2] == '\r'
                    && text[i + 3] == '\n')
                {
                    return i + 4;
                }
            }

            return -1;
        }

        private static int IndexOfOpeningTag(string text, string tag)
        {
            string needle = "<" + tag;
            int start = 0;
            while (start < text.Length)
            {
                int index = IndexOfOrdinalIgnoreCase(text, needle, start);
                if (index < 0)
                {
                    return -1;
                }

                int after = index + needle.Length;
                if (after == text.Length || text[after] == '>' || char.IsWhiteSpace(text[after]))
                {
                    return index;
                }

                start = after;
            }

            return -1;
        }

        /// <summary>
        /// Finds the earliest closing tag (<c>&lt;/name&gt;</c>) for ANY of the known reasoning tag
        /// names at or after <paramref name="startIndex"/>. Lets the tagged-block stripper recover the
        /// answer when a model mismatches its open/close tag names, instead of deleting everything
        /// after the opener. Returns -1 (and a zero <paramref name="matchedLength"/>) when none match.
        /// </summary>
        private static int IndexOfAnyClosingTag(string text, string[] tags, int startIndex, out int matchedLength)
        {
            int best = -1;
            matchedLength = 0;
            int from = Math.Min(Math.Max(0, startIndex), text.Length);
            for (int i = 0; i < tags.Length; i++)
            {
                string needle = "</" + tags[i] + ">";
                int index = IndexOfOrdinalIgnoreCase(text, needle, from);
                if (index >= 0 && (best < 0 || index < best))
                {
                    best = index;
                    matchedLength = needle.Length;
                }
            }

            return best;
        }

        private static string StripReasoningFencedBlocks(string text, string[] tags)
        {
            string[] fenceLabels = ReasoningFenceLabels(tags);
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            StringBuilder builder = new StringBuilder(text.Length);
            bool skipping = false;
            bool changed = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (IsFenceLine(trimmed))
                {
                    if (skipping)
                    {
                        skipping = false;
                        changed = true;
                        continue;
                    }

                    if (IsReasoningFenceLine(trimmed, fenceLabels))
                    {
                        skipping = true;
                        changed = true;
                        continue;
                    }
                }

                if (skipping)
                {
                    changed = true;
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(line);
            }

            return changed ? builder.ToString() : text;
        }

        private static bool IsFenceLine(string trimmedLine)
        {
            return trimmedLine.StartsWith("```", StringComparison.Ordinal)
                || trimmedLine.StartsWith("~~~", StringComparison.Ordinal);
        }

        private static bool IsReasoningFenceLine(string trimmedLine, string[] fenceLabels)
        {
            if (!IsFenceLine(trimmedLine) || trimmedLine.Length <= 3)
            {
                return false;
            }

            string info = trimmedLine.Substring(3).Trim().ToLowerInvariant();
            return StartsWithAny(info, fenceLabels);
        }

        /// <summary>
        /// Some local gateways wrap the whole final answer in a generic Markdown fence. Unwrap only
        /// when the entire response is one fenced block; embedded backticks in diary prose survive.
        /// </summary>
        private static string StripWholeResponseCodeFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            string[] lines = normalized.Split('\n');
            if (lines.Length < 2)
            {
                return text;
            }

            string open = lines[0].Trim();
            string close = lines[lines.Length - 1].Trim();
            if (!IsFenceLine(open) || !IsBareFenceLine(close))
            {
                return text;
            }

            // Only unwrap when this is ONE fenced block. If any interior line is also a fence, the
            // response is prose that merely begins and ends with a fence line (e.g. two code blocks,
            // or a fenced block followed by narration) -- stripping the outer pair would leave a
            // dangling interior fence and mangle the text, so leave it untouched.
            for (int i = 1; i < lines.Length - 1; i++)
            {
                if (IsFenceLine(lines[i].Trim()))
                {
                    return text;
                }
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 1; i < lines.Length - 1; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(lines[i]);
            }

            return builder.ToString().Trim();
        }

        private static bool IsBareFenceLine(string trimmedLine)
        {
            return string.Equals(trimmedLine, "```", StringComparison.Ordinal)
                || string.Equals(trimmedLine, "~~~", StringComparison.Ordinal);
        }

        private static string StripReasoningHeadingPrefix(string text, string[] tags)
        {
            string[] headingLabels = ReasoningHeadingLabels(tags);
            string trimmedStart = text.TrimStart();

            // Only a STANDALONE heading line ("Analysis:" alone on its line, with the reasoning on the
            // lines below) is a reasoning heading. An inline "Analysis: the raid was grim." is ordinary
            // prose whose first sentence must not be truncated, so require the first line to hold
            // nothing but the label.
            int firstLineEnd = trimmedStart.IndexOf('\n');
            string firstLine = firstLineEnd < 0 ? trimmedStart : trimmedStart.Substring(0, firstLineEnd);
            int headingLength = MatchedPrefixLength(firstLine.ToLowerInvariant(), headingLabels);
            if (headingLength < 0 || !string.IsNullOrWhiteSpace(firstLine.Substring(headingLength)))
            {
                return text;
            }

            int labelLength;
            int finalIndex = FindLineStartingWithAny(trimmedStart, FinalAnswerLabels(), out labelLength);
            if (finalIndex < 0)
            {
                return text;
            }

            return trimmedStart.Substring(finalIndex + labelLength).TrimStart();
        }

        /// <summary>Returns the length of the first matching label prefix, or -1 when none match.</summary>
        private static int MatchedPrefixLength(string value, string[] labels)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (value.StartsWith(labels[i], StringComparison.Ordinal))
                {
                    return labels[i].Length;
                }
            }

            return -1;
        }

        private static string StripLeadingFinalAnswerLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string trimmed = text.TrimStart();
            string comparable = trimmed.ToLowerInvariant();
            // Deliberately NARROWER than FinalAnswerLabels(): only "final"/"final answer"/"final
            // response" are safe to strip from the very start of a saved entry. The broader set
            // ("answer:", "result:", "diary:", "entry:", "output:", "response:") are ordinary diary
            // openings ("Result: we held the wall.", "Entry: day 12"), and stripping them silently
            // corrupted intentional prose. The broad set is still used INTERNALLY to locate the answer
            // after a detected reasoning block, where the context already proves it is a label.
            string[] labels = LeadingFinalAnswerLabels();
            for (int i = 0; i < labels.Length; i++)
            {
                if (!comparable.StartsWith(labels[i], StringComparison.Ordinal))
                {
                    continue;
                }

                string remainder = trimmed.Substring(labels[i].Length).TrimStart();
                return string.IsNullOrWhiteSpace(remainder) ? text : remainder;
            }

            return text;
        }

        /// <summary>
        /// The only label prefixes safe to strip from the START of a whole response. Kept separate
        /// from <see cref="FinalAnswerLabels"/> (which is broad on purpose for locating the answer that
        /// follows a reasoning block) so a diary entry that legitimately opens with a common word plus
        /// a colon is never truncated.
        /// </summary>
        private static string[] LeadingFinalAnswerLabels()
        {
            return new[] { "final:", "final answer:", "final response:" };
        }

        /// <summary>
        /// Removes visible "thinking out loud" rewrites from models that ignore hidden-reasoning
        /// boundaries and emit prompt self-audits such as "Wait, looking at the instructions...".
        /// When a later rewrite marker exists, the last rewrite wins; otherwise the visible draft
        /// before the self-audit is kept.
        /// </summary>
        private static string StripInstructionReflectionTranscript(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            int reflectionLine = FirstInstructionReflectionLine(lines);
            if (reflectionLine < 0)
            {
                return text;
            }

            int answerLine;
            int answerColumn;
            if (TryFindLastRevisionAnswerStart(lines, reflectionLine + 1, true, out answerLine, out answerColumn))
            {
                return TextFromLineSegment(lines, answerLine, answerColumn);
            }

            // Keep the visible draft written before the self-audit. If there is none -- the response
            // OPENS with a reflection-looking line, which for ordinary first-person diary prose is a
            // false positive ("I need to focus on the wall before winter.") -- leave the text as-is
            // rather than emptying the entry.
            string before = TextBeforeLine(lines, reflectionLine);
            return string.IsNullOrWhiteSpace(before) ? text : before;
        }

        private static int FirstInstructionReflectionLine(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string comparable = lines[i].TrimStart().ToLowerInvariant();
                if (IsInstructionReflectionLine(comparable))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsInstructionReflectionLine(string comparable)
        {
            if (string.IsNullOrWhiteSpace(comparable))
            {
                return false;
            }

            if (StartsWithAny(comparable, InstructionReflectionPrefixes()))
            {
                return true;
            }

            return comparable.StartsWith("wait,", StringComparison.Ordinal)
                && (comparable.Contains("instruction")
                    || comparable.Contains("prompt")
                    || comparable.Contains("notes say"));
        }

        private static bool TryFindLastRevisionAnswerStart(
            string[] lines,
            int startLine,
            bool allowDirectiveLabels,
            out int answerLine,
            out int answerColumn)
        {
            answerLine = -1;
            answerColumn = 0;
            for (int i = Math.Max(0, startLine); i < lines.Length; i++)
            {
                int column;
                if (TryGetRevisionAnswerStart(lines[i], allowDirectiveLabels, out column)
                    && HasNonWhitespaceFrom(lines, i, column))
                {
                    answerLine = i;
                    answerColumn = column;
                }
            }

            return answerLine >= 0;
        }

        private static bool TryGetRevisionAnswerStart(string line, bool allowDirectiveLabels, out int answerColumn)
        {
            answerColumn = 0;
            if (line == null)
            {
                return false;
            }

            int leading = 0;
            while (leading < line.Length && char.IsWhiteSpace(line[leading]))
            {
                leading++;
            }

            string comparable = line.Substring(leading).ToLowerInvariant();
            string[] labels = RevisionAnswerLabels();
            for (int i = 0; i < labels.Length; i++)
            {
                if (comparable.StartsWith(labels[i], StringComparison.Ordinal))
                {
                    answerColumn = leading + labels[i].Length;
                    return true;
                }
            }

            if (!allowDirectiveLabels || !StartsWithAny(comparable, RevisionDirectivePrefixes()))
            {
                return false;
            }

            int colon = line.IndexOf(':', leading);
            if (colon < 0 || colon - leading > 120)
            {
                return false;
            }

            answerColumn = colon + 1;
            return true;
        }

        private static bool HasNonWhitespaceFrom(string[] lines, int lineIndex, int column)
        {
            for (int i = Math.Max(0, lineIndex); i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                int start = i == lineIndex ? Math.Min(Math.Max(0, column), line.Length) : 0;
                for (int j = start; j < line.Length; j++)
                {
                    if (!char.IsWhiteSpace(line[j]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string TextFromLineSegment(string[] lines, int lineIndex, int column)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = Math.Max(0, lineIndex); i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                string segment = i == lineIndex
                    ? line.Substring(Math.Min(Math.Max(0, column), line.Length))
                    : line;

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(segment);
            }

            return builder.ToString().Trim();
        }

        private static string TextBeforeLine(string[] lines, int lineIndex)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < Math.Min(lineIndex, lines.Length); i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(lines[i]);
            }

            return builder.ToString().Trim();
        }

        private static int FindLineStartingWithAny(string text, string[] labels, out int labelLength)
        {
            int lineStart = 0;
            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                int leading = 0;
                while (lineStart + leading < lineEnd && char.IsWhiteSpace(text[lineStart + leading]))
                {
                    leading++;
                }

                string comparable = text.Substring(lineStart + leading, lineEnd - lineStart - leading).ToLowerInvariant();
                for (int i = 0; i < labels.Length; i++)
                {
                    if (comparable.StartsWith(labels[i], StringComparison.Ordinal))
                    {
                        labelLength = leading + labels[i].Length;
                        return lineStart;
                    }
                }

                lineStart = lineEnd + 1;
            }

            labelLength = 0;
            return -1;
        }

        private static bool StartsWithAny(string value, string[] labels)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (value.StartsWith(labels[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOfOrdinalIgnoreCase(string value, string needle, int startIndex)
        {
            return value.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithOrdinalIgnoreCase(string value, int startIndex, string needle)
        {
            return value != null
                && needle != null
                && startIndex >= 0
                && startIndex + needle.Length <= value.Length
                && string.Compare(value, startIndex, needle, 0, needle.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool IsPrecededBy(string value, int index, char expected)
        {
            return index > 0 && value[index - 1] == expected;
        }

        private static string[] ReasoningFenceLabels(string[] tags)
        {
            // Base fence info-strings (the word after ```). Includes the longer forms that some
            // chat templates emit, which are not single tag names.
            List<string> labels = new List<string> { "think", "thinking", "reasoning", "analysis", "chain-of-thought", "chain of thought", "cot" };
            // A pinned lane tag (e.g. reflection/scratchpad) is also recognized as a fence label.
            for (int i = 0; i < tags.Length; i++)
            {
                if (!ContainsOrdinalIgnoreCase(labels, tags[i]))
                {
                    labels.Add(tags[i]);
                }
            }

            return labels.ToArray();
        }

        private static string[] ReasoningHeadingLabels(string[] tags)
        {
            List<string> labels = new List<string> { "thinking:", "reasoning:", "analysis:", "chain-of-thought:", "chain of thought:" };
            for (int i = 0; i < tags.Length; i++)
            {
                string heading = tags[i] + ":";
                if (!ContainsOrdinalIgnoreCase(labels, heading))
                {
                    labels.Add(heading);
                }
            }

            return labels.ToArray();
        }

        /// <summary>Case-insensitive membership test for a string list, avoiding the LINQ/IEqualityComparer
        /// overloads that are not present on RimWorld's Mono runtime.</summary>
        private static bool ContainsOrdinalIgnoreCase(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] FinalAnswerLabels()
        {
            return new[] { "final:", "final answer:", "final response:", "answer:", "response:", "result:", "diary:", "entry:", "output:" };
        }

        private static string[] InstructionReflectionPrefixes()
        {
            // Only phrases that explicitly reference the prompt/instructions/source notes belong here.
            // Bare first-person intent lines ("I should focus on...", "I need to avoid...") were
            // removed: they are ordinary diary voice, and matching them truncated legitimate entries
            // (and, when such a line opened the response, emptied it entirely).
            return new[]
            {
                "wait, looking at the instructions",
                "wait, the instructions",
                "wait, the prompt",
                "looking at the instructions",
                "the instructions say",
                "the prompt says",
                "the notes say",
                "source notes say",
                "notes say",
                "i should not invent",
                "i shouldn't invent"
            };
        }

        private static string[] RevisionAnswerLabels()
        {
            return new[]
            {
                "final:",
                "final answer:",
                "final response:",
                "answer:",
                "response:",
                "result:",
                "diary:",
                "entry:",
                "output:",
                "revised:",
                "revision:",
                "revised version:",
                "rewrite:",
                "clean version:",
                "cleaned up:"
            };
        }

        private static string[] RevisionDirectivePrefixes()
        {
            return new[]
            {
                "let me refine",
                "let me revise",
                "let me rewrite",
                "let's refine",
                "let's revise",
                "let's rewrite",
                "i'll refine",
                "i'll revise",
                "i'll rewrite",
                "i will refine",
                "i will revise",
                "i will rewrite",
                "or maybe",
                "maybe shorter",
                "shorter version",
                "more restrained"
            };
        }

        private static string CompactReasoningCleanupWhitespace(string text)
        {
            while (text.Contains("\n\n\n"))
            {
                text = text.Replace("\n\n\n", "\n\n");
            }

            return text;
        }

        /// <summary>
        /// Removes model-hallucinated bracket tags while preserving the one marker contract the UI
        /// understands: a complete [[speech]]...[[/speech]] block. Small local models sometimes copy
        /// the speech-marker shape for thoughts, stage directions, or malformed closing tags; saved
        /// diary text should stay readable and parser-safe even when they do.
        /// </summary>
        private static string SanitizeGeneratedMarkup(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = NormalizeMalformedSpeechMarkers(text);
            normalized = StripAngleMarkupTags(normalized);
            string sanitized = SanitizeGeneratedTagMarkers(normalized, true);
            sanitized = StripStandaloneSchemaPunctuationTokens(sanitized);
            return CompactGeneratedMarkupWhitespace(sanitized).Trim();
        }

        private static bool IsUsableGeneratedTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            string trimmed = title.Trim();
            if (LooksLikeTitleInstructionLeak(trimmed)
                || EndsWithForbiddenTitlePunctuation(trimmed))
            {
                return false;
            }

            bool hasLetterOrDigit = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (IsUnexpectedTitleCharacter(c))
                {
                    return false;
                }

                if (char.IsLetterOrDigit(c))
                {
                    hasLetterOrDigit = true;
                }
            }

            int wordCount = CountTitleWords(trimmed);
            return hasLetterOrDigit
                && wordCount >= GeneratedTitleMinWords
                && wordCount <= GeneratedTitleMaxWords;
        }

        /// <summary>
        /// Catches one-line reasoning or instruction echoes that look like ordinary prose after tag
        /// stripping. Title callers should fall back instead of saving these as page headers.
        /// </summary>
        private static bool LooksLikeTitleInstructionLeak(string title)
        {
            string comparable = CollapseTitleWhitespace(title).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(comparable))
            {
                return true;
            }

            return comparable.StartsWith("title:", StringComparison.Ordinal)
                || StartsWithAny(comparable, FinalAnswerLabels())
                || IsInstructionReflectionLine(comparable)
                || comparable.Contains("only the title")
                || comparable.Contains("title only")
                || comparable.Contains("no quotes")
                || comparable.Contains("no period")
                || comparable.Contains("no labels")
                || comparable.Contains("no commentary")
                || comparable.Contains("no markdown")
                || comparable.Contains("3-8 word")
                || comparable.Contains("three to eight word");
        }

        private static int CountTitleWords(string title)
        {
            int words = 0;
            bool inWord = false;
            for (int i = 0; i < title.Length; i++)
            {
                if (char.IsWhiteSpace(title[i]))
                {
                    inWord = false;
                    continue;
                }

                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }

            return words;
        }

        private static bool EndsWithForbiddenTitlePunctuation(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            char last = title[title.Length - 1];
            return last == '.' || last == ':' || last == ';';
        }

        private static string GenericTitleFromText(string entryText, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(entryText) || maxWords <= 0)
            {
                return string.Empty;
            }

            string normalized = NormalizeMalformedSpeechMarkers(entryText);
            normalized = StripAngleMarkupTags(normalized);
            normalized = SanitizeGeneratedTagMarkers(normalized, false);
            normalized = StripStandaloneSchemaPunctuationTokens(normalized);
            normalized = ReplaceUnexpectedTitleCharactersWithSpaces(normalized);
            normalized = CollapseTitleWhitespace(normalized);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string firstWords = FirstTitleWords(normalized, maxWords);
            firstWords = TrimFallbackTitleEnding(firstWords);
            return string.IsNullOrWhiteSpace(firstWords) ? string.Empty : firstWords + "...";
        }

        private static string ReplaceUnexpectedTitleCharactersWithSpaces(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                builder.Append(IsUnexpectedTitleCharacter(c) ? ' ' : c);
            }

            return builder.ToString();
        }

        private static string CollapseTitleWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool previousWhitespace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    previousWhitespace = true;
                    continue;
                }

                builder.Append(c);
                previousWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private static string FirstTitleWords(string normalized, int maxWords)
        {
            int words = 0;
            int end = 0;
            bool inWord = false;
            for (int i = 0; i < normalized.Length; i++)
            {
                bool whitespace = char.IsWhiteSpace(normalized[i]);
                if (!whitespace && !inWord)
                {
                    words++;
                    inWord = true;
                }
                else if (whitespace)
                {
                    if (words >= maxWords)
                    {
                        break;
                    }

                    inWord = false;
                }

                end = i + 1;
                if (words >= maxWords && whitespace)
                {
                    break;
                }
            }

            return normalized.Substring(0, Math.Min(end, normalized.Length)).Trim();
        }

        private static string TrimFallbackTitleEnding(string title)
        {
            return string.IsNullOrWhiteSpace(title)
                ? string.Empty
                : title.Trim().TrimEnd('.', ',', ';', ':', '!', '?', '"', '\'');
        }

        private static bool IsUnexpectedTitleCharacter(char c)
        {
            if (char.IsControl(c))
            {
                return true;
            }

            switch (c)
            {
                case '<':
                case '>':
                case '[':
                case ']':
                case '{':
                case '}':
                case '_':
                case '=':
                case '|':
                case '\\':
                case '/':
                case '`':
                case '~':
                case '*':
                case '#':
                    return true;
                default:
                    return false;
            }
        }

        private static string NormalizeMalformedSpeechMarkers(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                string replacement;
                int markerLength;
                if (TryNormalizeSpeechMarkerAttempt(text, i, out replacement, out markerLength))
                {
                    builder.Append(replacement);
                    i += markerLength;
                    continue;
                }

                builder.Append(text[i]);
                i++;
            }

            return builder.ToString();
        }

        private static bool TryNormalizeSpeechMarkerAttempt(
            string text,
            int index,
            out string replacement,
            out int markerLength)
        {
            if (TrySpeechMarkerPrefix(text, index, "[[/speech", true, out markerLength)
                || TrySpeechMarkerPrefix(text, index, "[[/speach", true, out markerLength)
                || TrySpeechMarkerPrefix(text, index, "[/speech", false, out markerLength)
                || TrySpeechMarkerPrefix(text, index, "[/speach", false, out markerLength))
            {
                replacement = SpeechCloseMarker;
                return true;
            }

            if (TrySpeechMarkerPrefix(text, index, "[[speech", true, out markerLength)
                || TrySpeechMarkerPrefix(text, index, "[[speach", true, out markerLength)
                || TrySpeechMarkerPrefix(text, index, "[speech", false, out markerLength)
                || TrySpeechMarkerPrefix(text, index, "[speach", false, out markerLength))
            {
                replacement = SpeechOpenMarker;
                return true;
            }

            replacement = string.Empty;
            markerLength = 0;
            return false;
        }

        private static bool TrySpeechMarkerPrefix(
            string text,
            int index,
            string prefix,
            bool allowPrecedingOpenBracket,
            out int markerLength)
        {
            markerLength = MalformedMarkerLengthForPrefix(text, index, prefix);
            return markerLength > 0 && (allowPrecedingOpenBracket || !IsPrecededBy(text, index, '['));
        }

        private static string StripAngleMarkupTags(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '<')
                {
                    int markerLength = AngleMarkupMarkerLength(text, i);
                    if (markerLength > 0)
                    {
                        i += markerLength;
                        continue;
                    }
                }

                builder.Append(text[i]);
                i++;
            }

            return builder.ToString();
        }

        private static string SanitizeGeneratedTagMarkers(string text, bool preserveSpeechPairs)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (StartsWithOrdinalIgnoreCase(text, i, SpeechOpenMarker))
                {
                    if (preserveSpeechPairs)
                    {
                        int contentStart = i + SpeechOpenMarker.Length;
                        int close = IndexOfOrdinalIgnoreCase(text, SpeechCloseMarker, contentStart);
                        if (close >= 0)
                        {
                            builder.Append(SpeechOpenMarker);
                            builder.Append(SanitizeGeneratedTagMarkers(text.Substring(contentStart, close - contentStart), false));
                            builder.Append(SpeechCloseMarker);
                            i = close + SpeechCloseMarker.Length;
                            continue;
                        }
                    }

                    i += SpeechOpenMarker.Length;
                    continue;
                }

                if (StartsWithOrdinalIgnoreCase(text, i, SpeechCloseMarker))
                {
                    i += SpeechCloseMarker.Length;
                    continue;
                }

                int malformedSpeechMarkerLength = MalformedSpeechMarkerLength(text, i);
                if (malformedSpeechMarkerLength > 0)
                {
                    i += malformedSpeechMarkerLength;
                    continue;
                }

                if (i + 1 < text.Length && text[i] == '[' && text[i + 1] == '[')
                {
                    int close = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        string inner = text.Substring(i + 2, close - i - 2).Trim();
                        if (!IsLowercaseTagName(inner))
                        {
                            builder.Append(inner);
                        }

                        i = close + 2;
                        continue;
                    }
                }

                int incompleteTagLength = IncompleteBracketTagMarkerLength(text, i);
                if (incompleteTagLength > 0)
                {
                    i += incompleteTagLength;
                    continue;
                }

                builder.Append(text[i]);
                i++;
            }

            return builder.ToString();
        }

        private static int AngleMarkupMarkerLength(string text, int index)
        {
            if (index < 0 || index >= text.Length || text[index] != '<')
            {
                return 0;
            }

            int i = index + 1;
            if (i >= text.Length)
            {
                return 0;
            }

            if (text[i] == '/')
            {
                i++;
            }

            int nameStart = i;
            if (nameStart >= text.Length || !IsAsciiLower(text[nameStart]))
            {
                return 0;
            }

            while (i < text.Length && IsMarkupTagNameCharacter(text[i]))
            {
                i++;
            }

            if (i == nameStart || i - nameStart > 24)
            {
                return 0;
            }

            if (i >= text.Length)
            {
                return i - index;
            }

            char next = text[i];
            if (next == '>')
            {
                return i - index + 1;
            }

            if (next != '=' && next != '/' && !char.IsWhiteSpace(next))
            {
                return 0;
            }

            while (i < text.Length && text[i] != '<' && text[i] != '\r' && text[i] != '\n')
            {
                if (text[i] == '>')
                {
                    return i - index + 1;
                }

                i++;
            }

            return i - index;
        }

        private static int MalformedSpeechMarkerLength(string text, int index)
        {
            int length = MalformedMarkerLengthForPrefix(text, index, "[[speech");
            if (length > 0)
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[[/speech");
            if (length > 0)
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[[speach");
            if (length > 0)
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[[/speach");
            if (length > 0)
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[speech");
            if (length > 0 && !IsPrecededBy(text, index, '['))
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[/speech");
            if (length > 0 && !IsPrecededBy(text, index, '['))
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[speach");
            if (length > 0 && !IsPrecededBy(text, index, '['))
            {
                return length;
            }

            length = MalformedMarkerLengthForPrefix(text, index, "[/speach");
            return length > 0 && !IsPrecededBy(text, index, '[') ? length : 0;
        }

        private static int MalformedMarkerLengthForPrefix(string text, int index, string prefix)
        {
            if (!StartsWithOrdinalIgnoreCase(text, index, prefix))
            {
                return 0;
            }

            int afterPrefix = index + prefix.Length;
            if (afterPrefix >= text.Length)
            {
                return prefix.Length;
            }

            char next = text[afterPrefix];
            if (next != ']' && next != '>' && !char.IsWhiteSpace(next) && IsMarkerNameContinuation(next))
            {
                return 0;
            }

            int markerEnd = afterPrefix;
            while (markerEnd < text.Length
                && !char.IsWhiteSpace(text[markerEnd])
                && text[markerEnd] != '[')
            {
                if (text[markerEnd] == ']' || text[markerEnd] == '>')
                {
                    while (markerEnd < text.Length && (text[markerEnd] == ']' || text[markerEnd] == '>'))
                    {
                        markerEnd++;
                    }

                    return markerEnd - index;
                }

                markerEnd++;
            }

            return markerEnd - index;
        }

        private static int IncompleteBracketTagMarkerLength(string text, int index)
        {
            if (index < 0
                || index + 1 >= text.Length
                || text[index] != '['
                || text[index + 1] != '[')
            {
                return 0;
            }

            int i = index + 2;
            if (i < text.Length && text[i] == '/')
            {
                i++;
            }

            int nameStart = i;
            if (nameStart >= text.Length || !IsAsciiLower(text[nameStart]))
            {
                return 0;
            }

            while (i < text.Length && IsMarkupTagNameCharacter(text[i]))
            {
                i++;
            }

            if (i == nameStart || i - nameStart > 24)
            {
                return 0;
            }

            if (i >= text.Length)
            {
                return i - index;
            }

            char next = text[i];
            if (char.IsWhiteSpace(next))
            {
                return i - index;
            }

            if (next != ']' && next != '>')
            {
                if (IsMarkerNameContinuation(next))
                {
                    return 0;
                }

                while (i < text.Length
                    && !char.IsWhiteSpace(text[i])
                    && text[i] != '[')
                {
                    i++;
                }

                return i - index;
            }

            while (i < text.Length && (text[i] == ']' || text[i] == '>'))
            {
                i++;
            }

            return i - index;
        }

        private static bool IsLowercaseTagName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string name = value.Trim();
            int start = name[0] == '/' ? 1 : 0;
            if (start >= name.Length || name.Length - start > 24)
            {
                return false;
            }

            if (!IsAsciiLower(name[start]))
            {
                return false;
            }

            for (int i = start + 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!IsAsciiLower(c) && !char.IsDigit(c) && c != '-' && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiLower(char c)
        {
            return c >= 'a' && c <= 'z';
        }

        private static bool IsMarkupTagNameCharacter(char c)
        {
            return IsAsciiLower(c) || char.IsDigit(c) || c == '-' || c == '_';
        }

        private static bool IsMarkerNameContinuation(char c)
        {
            return char.IsLetterOrDigit(c) || c == '-' || c == '_';
        }

        private static string StripStandaloneSchemaPunctuationTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (IsSchemaPunctuation(text[i])
                    && IsAtTokenBoundary(text, i - 1)
                    && IsSchemaPunctuationToken(text, i, out int tokenEnd)
                    && IsAtTokenBoundary(text, tokenEnd))
                {
                    i = tokenEnd;
                    continue;
                }

                builder.Append(text[i]);
                i++;
            }

            return builder.ToString();
        }

        private static bool IsSchemaPunctuationToken(string text, int start, out int end)
        {
            int i = start;
            while (i < text.Length && IsSchemaPunctuation(text[i]))
            {
                i++;
            }

            end = i;
            return i > start;
        }

        private static bool IsSchemaPunctuation(char c)
        {
            return c == ';' || c == '=' || c == ':' || c == '|';
        }

        private static bool IsAtTokenBoundary(string text, int index)
        {
            return index < 0 || index >= text.Length || char.IsWhiteSpace(text[index]);
        }

        private static string CompactGeneratedMarkupWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            text = text.Replace(" \n", "\n");
            text = text.Replace("\n ", "\n");
            return text;
        }

        /// <summary>
        /// Enforces a hard upper bound on response length by counting whitespace-delimited tokens,
        /// preferring to end at the last complete sentence before the cap.
        /// </summary>
        private static string TrimToMaxTokens(string text, int maxTokens)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (maxTokens <= 0)
            {
                return trimmed;
            }

            bool insideToken = false;
            int tokenCount = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsWhiteSpace(c))
                {
                    insideToken = false;
                    continue;
                }

                if (insideToken)
                {
                    continue;
                }

                insideToken = true;
                tokenCount++;
                if (tokenCount > maxTokens)
                {
                    int sentenceEnd = LastSentenceEndBefore(trimmed, i);
                    if (sentenceEnd > 0)
                    {
                        return trimmed.Substring(0, sentenceEnd).TrimEnd();
                    }

                    string capped = trimmed.Substring(0, i).TrimEnd();
                    return string.IsNullOrEmpty(capped) ? string.Empty : capped + "...";
                }
            }

            return trimmed;
        }

        /// <summary>
        /// Removes a dangling final sentence fragment from main diary/note output. This catches the
        /// common API-stop case where the model obeys max_tokens by cutting off mid-sentence.
        /// </summary>
        private static string TrimTrailingIncompleteSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (EndsWithCompleteSentence(trimmed))
            {
                return trimmed;
            }

            // A closed direct-speech block ("[[speech]]...[[/speech]]") is complete by construction,
            // but it ends in "]]" rather than sentence punctuation, so the sentence heuristic alone
            // would rewind past it and delete the whole block when the model ends an entry on a speech
            // line. Treat the end of the last closed block as a valid boundary and cut at whichever
            // complete boundary is furthest right, so the block is never trimmed away.
            int sentenceEnd = LastSentenceEndBefore(trimmed, trimmed.Length);
            int speechBlockEnd = LastClosedSpeechBlockEnd(trimmed);
            int cut = Math.Max(sentenceEnd, speechBlockEnd);
            if (cut > 0)
            {
                return trimmed.Substring(0, cut).TrimEnd();
            }

            return trimmed;
        }

        // Direct-speech sentinels, mirrored locally so this file keeps compiling in isolation (see the
        // header note). These are the literal markers the prompt asks the model to emit and match
        // DiaryDirectSpeechParser.Default{Open,Close}Marker.
        private const string SpeechOpenMarker = "[[speech]]";
        private const string SpeechCloseMarker = "[[/speech]]";
        private const int GenericTitleFallbackWords = 6;
        private const int GeneratedTitleMinWords = 3;
        private const int GeneratedTitleMaxWords = 8;

        /// <summary>
        /// Returns the index just past the last closed direct-speech block, or 0 when none is present.
        /// Used so trailing-fragment trimming treats a finished speech block as a complete ending.
        /// </summary>
        private static int LastClosedSpeechBlockEnd(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int close = text.LastIndexOf(SpeechCloseMarker, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                return 0;
            }

            // Only treat the block as a completion boundary when it is at the end. If prose follows
            // the closed block and then gets truncated, keep the prose for the normal fragment
            // heuristic instead of cutting back to the earlier speech block.
            int end = close + SpeechCloseMarker.Length;
            for (int i = end; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return 0;
                }
            }

            // Require a matching open marker before the close so a stray close marker in ordinary
            // prose cannot suppress trimming of a genuinely truncated tail.
            int open = text.Substring(0, close)
                .IndexOf(SpeechOpenMarker, StringComparison.OrdinalIgnoreCase);
            return open >= 0 ? end : 0;
        }

        /// <summary>
        /// Finds a sentence boundary that fits inside the token cap. This deliberately uses a small
        /// punctuation heuristic rather than culture-heavy sentence parsing.
        /// </summary>
        private static int LastSentenceEndBefore(string text, int maxEndExclusive)
        {
            if (string.IsNullOrEmpty(text) || maxEndExclusive <= 0)
            {
                return -1;
            }

            int cappedEnd = Math.Min(maxEndExclusive, text.Length);
            for (int i = cappedEnd - 1; i >= 0; i--)
            {
                if (!IsSentenceEndingPunctuation(text[i]))
                {
                    continue;
                }

                int end = i + 1;
                while (end < cappedEnd && IsSentenceClosingCharacter(text[end]))
                {
                    end++;
                }

                if (end == text.Length || end == cappedEnd || char.IsWhiteSpace(text[end]))
                {
                    return end;
                }
            }

            return -1;
        }

        private static bool EndsWithCompleteSentence(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int i = text.Length - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i]))
            {
                i--;
            }

            while (i >= 0 && IsSentenceClosingCharacter(text[i]))
            {
                i--;
            }

            return i >= 0 && IsSentenceEndingPunctuation(text[i]);
        }

        private static bool IsSentenceEndingPunctuation(char c)
        {
            return c == '.' || c == '!' || c == '?';
        }

        private static bool IsSentenceClosingCharacter(char c)
        {
            return c == '"' || c == '\'' || c == ')' || c == ']' || c == '}';
        }
    }
}
