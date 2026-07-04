using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using PawnDiary;

namespace LlmResponseParserTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestResponseTextExtraction();
            TestProviderErrors();
            TestReasoningScrub();
            TestReasoningScrubNoCloseTag();
            TestReasoningScrubNeverEmptiesValidProse();
            TestReasoningTagOverride();
            TestGeneratedTextCleanup();
            TestGeneratedTagSanitizer();
            TestTitleFallback();
            TestSpeechMarkerConstantsMirrorDirectSpeechParser();
            TestMiniJsonRejectsMalformedNumbers();
            TestMiniJsonRejectsExcessiveDepth();

            Console.WriteLine("LlmResponseParserTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestResponseTextExtraction()
        {
            AssertEqual(
                "chat text",
                "A small entry.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"choices\":[{\"message\":{\"content\":\"A small entry.\"}}]}"),
                    LlmResponseMode.OpenAIChatCompletions));

            AssertEqual(
                "chat text parts",
                "Part one.\nPart two.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Part one.\"},{\"type\":\"reasoning_text\",\"text\":\"hidden\"},{\"type\":\"text\",\"text\":\"Part two.\"}]}}]}"),
                    LlmResponseMode.OpenAIChatCompletions));

            AssertEqual(
                "chat reasoning_content parts",
                "Visible answer.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"reasoning_content\",\"text\":\"hidden chain\"},{\"type\":\"text\",\"text\":\"Visible answer.\"}]}}]}"),
                    LlmResponseMode.OpenAIChatCompletions));

            AssertEqual(
                "chat sibling reasoning field ignored",
                "Visible answer.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"reasoning\":\"Wait, looking at the instructions: hidden draft\",\"content\":\"Visible answer.\"}}]}"),
                    LlmResponseMode.OpenAIChatCompletions));

            AssertEqual(
                "responses output_text fallback",
                "Preferred text.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"output_text\":\"Preferred text.\"}"),
                    LlmResponseMode.OpenAIResponses));

            AssertEqual(
                "responses typed output beats leaked output_text",
                "Visible diary.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"output_text\":\"hidden thinking transcript\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Visible diary.\"}]}]}"),
                    LlmResponseMode.OpenAIResponses));

            AssertEqual(
                "responses string content beats leaked output_text",
                "Visible string diary.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"output_text\":\"hidden thinking transcript\",\"output\":[{\"type\":\"message\",\"content\":\"Visible string diary.\"}]}"),
                    LlmResponseMode.OpenAIResponses));

            AssertEqual(
                "responses parts skip reasoning",
                "Alpha.\nBeta.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"output\":[{\"type\":\"reasoning\",\"content\":[{\"text\":\"secret\"}]},{\"content\":[{\"type\":\"output_text\",\"text\":\"Alpha.\"},{\"type\":\"reasoning_text\",\"text\":\"hidden\"},{\"type\":\"output_text\",\"text\":\"Beta.\"}]}]}"),
                    LlmResponseMode.OpenAIResponses));

            AssertEqual(
                "chat output_text part field",
                "Visible from output_text.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"output_text\",\"output_text\":\"Visible from output_text.\"}]}}]}"),
                    LlmResponseMode.OpenAIChatCompletions));

        }

        private static void TestProviderErrors()
        {
            AssertEqual(
                "api error object",
                "API error: bad key",
                LlmResponseParser.ExtractProviderError(
                    Root("{\"error\":{\"message\":\"bad key\"}}"),
                    LlmResponseMode.OpenAIChatCompletions,
                    false));

            AssertEqual(
                "responses incomplete",
                "Responses API returned an incomplete response with no message content (reason=max_output_tokens).",
                LlmResponseParser.ExtractProviderError(
                    Root("{\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}"),
                    LlmResponseMode.OpenAIResponses,
                    false));

            AssertEqual(
                "chat finish length",
                "Chat completion finished with no message content (finish_reason=length).",
                LlmResponseParser.ExtractProviderError(
                    Root("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{}}]}"),
                    LlmResponseMode.OpenAIChatCompletions,
                    false));

            AssertEqual(
                "api error reason and code",
                "API error: quota_exceeded, code=rate_limit",
                LlmResponseParser.ExtractProviderError(
                    Root("{\"error\":{\"reason\":\"quota_exceeded\",\"code\":\"rate_limit\"}}"),
                    LlmResponseMode.OpenAIChatCompletions,
                    false));

        }

        private static void TestReasoningScrub()
        {
            AssertEqual(
                "paired think tag",
                "Final.",
                LlmResponseParser.StripReasoningTextBlocks("<think>secret</think>\nFinal."));

            AssertEqual(
                "orphan closing think tag",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("hidden</think>\nVisible."));

            AssertEqual(
                "multiple orphan closing think tags are all stripped",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("hidden</think>more hidden</think>Visible."));

            AssertEqual(
                "fenced reasoning",
                "Answer.",
                LlmResponseParser.StripReasoningTextBlocks("```thinking\nsecret\n```\nAnswer."));

            AssertEqual(
                "reasoning heading",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("Thinking:\nsecret\nAnswer: Visible."));

            AssertEqual(
                "reasoning heading final response",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("Thinking:\nsecret\nFinal response: Visible."));

            AssertEqual(
                "instruction self-edit transcript keeps last rewrite",
                "Stirring the pot by the conduit, the desert's ugliness faded for a moment. Talking mechanoids with Townsend - just a look, a pause - and I felt steadied.",
                LlmResponseParser.StripReasoningTextBlocks(
                    "Cooking in this ugly stretch of desert, Townsend and I found a moment.\n"
                    + "[[speech]]It's good to talk to someone who gets it.[[/speech]]\n\n"
                    + "Wait, looking at the instructions: \"If the notes are thin, react specifically to what happened rather than inventing detail.\" "
                    + "The notes say \"Gerald connected on the topic of mechanoids with Townsend\" and \"warmth exchanged\". I should focus on that connection.\n\n"
                    + "Let me refine:\n"
                    + "The desert heat, the ugly view, cooking at the stove... but Townsend was there. The mechanoid talk shifted something between us.\n"
                    + "[[speech]]Same wavelength.[[/speech]]\n\n"
                    + "Or maybe shorter, more restrained:\n"
                    + "Stirring the pot by the conduit, the desert's ugliness faded for a moment. Talking mechanoids with Townsend - just a look, a pause - and I felt steadied."));

            AssertEqual(
                "instruction echo without rewrite keeps visible draft",
                "I wrote down the meal before the thought wandered.",
                LlmResponseParser.StripReasoningTextBlocks(
                    "I wrote down the meal before the thought wandered.\n\n"
                    + "Wait, looking at the instructions: I should not invent detail."));

            AssertEqual(
                "in-world wait line survives",
                "Wait, Townsend paused by the stove.",
                LlmResponseParser.StripReasoningTextBlocks("Wait, Townsend paused by the stove."));

            AssertEqual(
                "whole text fence unwrapped",
                "Visible entry.",
                LlmResponseParser.StripReasoningTextBlocks("```text\nVisible entry.\n```"));

            AssertEqual(
                "whole json fence unwrapped",
                "{\"title\":\"Gate\"}",
                LlmResponseParser.StripReasoningTextBlocks("```json\n{\"title\":\"Gate\"}\n```"));

            AssertEqual(
                "inline backticks survive",
                "I marked `south gate` in the plan.",
                LlmResponseParser.StripReasoningTextBlocks("I marked `south gate` in the plan."));
        }

        // Covers the trickier fallback branches of StripTaggedReasoningBlocks: an opening reasoning
        // tag that never closes (the chat template ate the close tag, or the model just forgot it).
        // These are the highest-risk branches and were previously untested.
        private static void TestReasoningScrubNoCloseTag()
        {
            // No close tag, but a "final answer" marker line: trim reasoning up to that line. The
            // marker line itself is kept by design (the cut stops at the start of the marker line).
            AssertEqual(
                "no-close final-answer marker",
                "We held the line.",
                LlmResponseParser.StripReasoningTextBlocks("<think>\nweigh options\nfinal answer: We held the line."));

            // No close tag and no marker, but a blank line separates reasoning from the answer.
            AssertEqual(
                "no-close blank-line split",
                "The harvest came in early.",
                LlmResponseParser.StripReasoningTextBlocks("<think>\ndeliberating quietly\n\nThe harvest came in early."));

            // No close tag, no marker, no blank line: drop everything from the open tag to the end.
            AssertEqual(
                "no-close remove to end",
                "Visible intro.",
                LlmResponseParser.StripReasoningTextBlocks("Visible intro. <think>then only private reasoning until the end"));

            AssertEqual(
                "truncated think opening remove to end",
                "Visible intro.",
                LlmResponseParser.StripReasoningTextBlocks("Visible intro. <think"));

            // Two paired blocks with different tag names exercise the outer per-tag loop.
            AssertEqual(
                "multiple paired tags",
                "Held the gate. Bob smiled.",
                LlmResponseParser.StripReasoningTextBlocks("<think>plan</think>Held the gate.<reasoning>why</reasoning> Bob smiled."));
        }

        // Guards against the review's data-loss class: a single malformed reasoning tag, or an
        // ordinary diary phrase that merely LOOKS like a label/heading, must never silently empty or
        // truncate a valid entry (SendOnce treats an empty cleaned result as a permanent failure).
        private static void TestReasoningScrubNeverEmptiesValidProse()
        {
            // Mismatched close tag name (opened <thinking>, closed </think>): recover the answer via
            // any known closer instead of deleting everything after the opener.
            AssertEqual(
                "mismatched close tag keeps the answer",
                "We held the gate at dawn.",
                LlmResponseParser.StripReasoningTextBlocks("<thinking>hidden reasoning</think>We held the gate at dawn."));
            AssertEqual(
                "mismatched close tag (think/reasoning) keeps the answer",
                "We held the gate at dawn.",
                LlmResponseParser.StripReasoningTextBlocks("<think>hidden reasoning</reasoning>We held the gate at dawn."));

            // Trailing stray closer with the answer BEFORE it: drop only the closer, keep the answer.
            AssertEqual(
                "trailing orphan closer keeps the answer",
                "We survived the raid, barely.",
                LlmResponseParser.StripReasoningTextBlocks("We survived the raid, barely.</think>"));
            AssertEqual(
                "trailing orphan closer keeps multi-line answer",
                "The night was long and cold.\nEveryone lived.",
                LlmResponseParser.StripReasoningTextBlocks("The night was long and cold.\nEveryone lived.</think>"));

            // Ordinary diary openings that happen to begin with a common word + colon must survive.
            AssertEqual("result: opening survives", "Result: we held the wall today.",
                LlmResponseParser.StripReasoningTextBlocks("Result: we held the wall today."));
            AssertEqual("entry: opening survives", "Entry: day 12 was long.",
                LlmResponseParser.StripReasoningTextBlocks("Entry: day 12 was long."));
            AssertEqual("diary: opening survives", "Diary: it rained all day.",
                LlmResponseParser.StripReasoningTextBlocks("Diary: it rained all day."));
            AssertEqual("answer: opening survives", "Answer: I still don't have one.",
                LlmResponseParser.StripReasoningTextBlocks("Answer: I still don't have one."));

            // The narrow "final answer:" label is still stripped from the very start.
            AssertEqual("leading final-answer label still stripped", "we lost three.",
                LlmResponseParser.StripReasoningTextBlocks("Final answer: we lost three."));

            // A first-person intent line that opens the entry is prose, not an instruction self-audit.
            AssertEqual("i-should-focus opening survives",
                "I should focus on the wall before winter.",
                LlmResponseParser.StripReasoningTextBlocks("I should focus on the wall before winter."));

            // Inline "Analysis:" prose is not a standalone reasoning heading; its first sentence stays.
            AssertEqual("inline analysis prose is not truncated",
                "Analysis: the numbers were grim.\nWe lost three.",
                LlmResponseParser.StripReasoningTextBlocks("Analysis: the numbers were grim.\nWe lost three."));

            // Prose that merely begins and ends with a fence line (with an interior fence) is left
            // intact -- it is not one wrapped block.
            AssertEqual("multi-fence prose left intact",
                "```\nMy day.\n```\ncode\n```",
                LlmResponseParser.StripReasoningTextBlocks("```\nMy day.\n```\ncode\n```"));
        }

        // The reasoning-tag system. Auto now covers a broad built-in list (think/thinking/reasoning/
        // analysis/thought/reflection/scratchpad), so exotic wrappers are stripped even without
        // pinning. A pinned tag adds PRIORITY (tried first) and coverage for any wrapper not yet in
        // the base list, but never weakens detection of the common ones.
        private static void TestReasoningTagOverride()
        {
            // Pinned reflection tag: the wrapper is removed.
            AssertEqual(
                "pinned reflection tag stripped",
                "Visible entry.",
                LlmResponseParser.StripReasoningTextBlocks("<reflection>private musings</reflection>Visible entry.", "reflection"));

            // Auto now ALSO strips reflection (and scratchpad/thought), since the built-in list was
            // widened so most players never need to pin a tag manually.
            AssertEqual(
                "auto strips reflection too",
                "Visible entry.",
                LlmResponseParser.StripReasoningTextBlocks("<reflection>private musings</reflection>Visible entry.", "auto"));

            AssertEqual(
                "auto strips scratchpad",
                "Answer.",
                LlmResponseParser.StripReasoningTextBlocks("<scratchpad>draft notes</scratchpad>Answer.", "auto"));

            AssertEqual(
                "auto strips thought",
                "Result.",
                LlmResponseParser.StripReasoningTextBlocks("<thought>musing</thought>Result.", "auto"));

            // Union: a pinned reflection tag does NOT weaken stripping of the common <think> tag.
            AssertEqual(
                "pinned tag still strips common think leaks",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("<think>hidden</think>Visible.", "reflection"));

            // Pinned tag with no close tag: a "final answer" marker still cuts the block.
            AssertEqual(
                "pinned tag no close final-answer marker",
                "Held.",
                LlmResponseParser.StripReasoningTextBlocks("<reflection>\ndeliberating\nfinal answer: Held.", "reflection"));

            // Pinned tag as a fenced block label is also stripped.
            AssertEqual(
                "pinned tag fenced block stripped",
                "Answer.",
                LlmResponseParser.StripReasoningTextBlocks("```reflection\nsecret\n```\nAnswer.", "reflection"));

            // Auto also strips fenced blocks whose label is one of the built-in tags.
            AssertEqual(
                "auto strips reflection fenced block",
                "Answer.",
                LlmResponseParser.StripReasoningTextBlocks("```reflection\nsecret\n```\nAnswer.", "auto"));

            // Pinned tag as a heading prefix is cut up to the final-answer marker.
            AssertEqual(
                "pinned tag heading cut to final marker",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("Reflection:\nsecret\nAnswer: Visible.", "reflection"));

            // An unrecognized tag string normalizes back to Auto (built-in detection), so reflection
            // is still stripped via the widened base list.
            AssertEqual(
                "unknown tag normalizes to auto and still strips known wrappers",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("<reflection>private</reflection>Visible.", "totally-not-a-real-tag"));
        }

        private static void TestGeneratedTextCleanup()
        {
            AssertEqual(
                "trim dangling fragment",
                "One sentence.",
                LlmResponseParser.CleanGeneratedText("One sentence. Two words without", 20, false));

            AssertEqual(
                "title keeps fragment",
                "One sentence. Two words without",
                LlmResponseParser.CleanGeneratedText("One sentence. Two words without", 20, true));

            AssertEqual(
                "cap at sentence boundary",
                "One two.",
                LlmResponseParser.CleanGeneratedText("One two. Three four five.", 3, false));

            AssertEqual(
                "cap without sentence boundary",
                "One two...",
                LlmResponseParser.CleanGeneratedText("One two three four", 2, false));
        }

        private static void TestGeneratedTagSanitizer()
        {
            AssertEqual(
                "valid speech survives sanitizer",
                "I said it.\n[[speech]]Enough.[[/speech]]",
                LlmResponseParser.CleanGeneratedText("I said it.\n[[speech]]Enough.[[/speech]]", 50, false));

            AssertEqual(
                "malformed speech close repaired",
                "[[speech]]We are doing this now.[[/speech]]",
                LlmResponseParser.CleanGeneratedText("[[speech]]We are doing this now.[/speech]]", 50, false));

            AssertEqual(
                "misspelled speech close repaired",
                "[[speech]]We are doing this now.[[/speech]]",
                LlmResponseParser.CleanGeneratedText("[[speech]]We are doing this now.[[/speach", 50, false));

            AssertEqual(
                "misspelled speech pair repaired",
                "[[speech]]We are doing this now.[[/speech]]",
                LlmResponseParser.CleanGeneratedText("[[speach]]We are doing this now.[[/speach", 50, false));

            AssertEqual(
                "truncated misspelled speech open stripped",
                "I spoke. Enough.",
                LlmResponseParser.CleanGeneratedText("I spoke. [[speach... Enough.", 50, false));

            AssertEqual(
                "bracketed prose flattened",
                "I froze. You do not belong here.",
                LlmResponseParser.CleanGeneratedText("I froze. [[You do not belong here.]]", 50, false));

            AssertEqual(
                "schema punctuation tokens stripped",
                "I froze. You do not belong here.",
                LlmResponseParser.CleanGeneratedText("I froze. ; = | : You do not belong here.", 50, false));

            AssertEqual(
                "prose semicolon survives",
                "I froze; you did not.",
                LlmResponseParser.CleanGeneratedText("I froze; you did not.", 50, false));

            AssertEqual(
                "unknown paired tag markers stripped",
                "The plan was first.",
                LlmResponseParser.CleanGeneratedText("The plan was [[work]]first[[/work]].", 50, false));

            AssertEqual(
                "unpaired speech marker stripped to prose",
                "I spoke. Enough.",
                LlmResponseParser.CleanGeneratedText("I spoke. [[speech]]Enough.", 50, false));

            AssertEqual(
                "incomplete bracket tag stripped",
                "The hallway cooled.",
                LlmResponseParser.CleanGeneratedText("The hallway cooled. [[mood", 50, false));

            AssertEqual(
                "incomplete bracket tag prefix stripped",
                "The plan was first.",
                LlmResponseParser.CleanGeneratedText("The plan was [[work first.", 50, false));

            AssertEqual(
                "angle rich text tags stripped",
                "We moved.",
                LlmResponseParser.CleanGeneratedText("<i>We moved.</i>", 50, false));

            AssertEqual(
                "incomplete angle rich text tag stripped",
                "We moved.",
                LlmResponseParser.CleanGeneratedText("We moved. <color=#ffcc00", 50, false));

            AssertEqual(
                "less-than prose survives",
                "I counted 3 < 4 stones.",
                LlmResponseParser.CleanGeneratedText("I counted 3 < 4 stones.", 50, false));

            AssertEqual(
                "title sanitizes bracket tags",
                "The Storm Watch",
                LlmResponseParser.CleanGeneratedText("[[The Storm Watch]]", 20, true));
        }

        private static void TestTitleFallback()
        {
            AssertEqual(
                "plain title kept",
                "O'Neil's Last Stand",
                LlmResponseParser.TitleOrFallback(
                    "O'Neil's Last Stand",
                    "Alice pulled Bob out of the smoke and into cold rain."));

            AssertEqual(
                "labeled title falls back",
                "Alice pulled Bob out of the...",
                LlmResponseParser.TitleOrFallback(
                    "Title: Smoke in Cold Rain",
                    "Alice pulled Bob out of the smoke and into cold rain."));

            AssertEqual(
                "reasoning title line falls back",
                "Alice pulled Bob out of the...",
                LlmResponseParser.TitleOrFallback(
                    "We need answer with only the title, no commentary",
                    "Alice pulled Bob out of the smoke and into cold rain."));

            AssertEqual(
                "overlong title falls back",
                "Alice pulled Bob out of the...",
                LlmResponseParser.TitleOrFallback(
                    "Smoke and Cold Rain Around the Hospital Doorway Tonight",
                    "Alice pulled Bob out of the smoke and into cold rain."));

            AssertEqual(
                "period-ended title falls back",
                "Alice pulled Bob out of the...",
                LlmResponseParser.TitleOrFallback(
                    "Smoke in Cold Rain.",
                    "Alice pulled Bob out of the smoke and into cold rain."));

            AssertEqual(
                "angle tag title falls back",
                "Alice pulled Bob out of the...",
                LlmResponseParser.TitleOrFallback(
                    "<uncensored_response>",
                    "Alice pulled Bob out of the smoke and into cold rain."));

            AssertEqual(
                "bare schema token title falls back",
                "Alice found the medicine in a...",
                LlmResponseParser.TitleOrFallback(
                    "uncensored_response",
                    "Alice found the medicine in a burned storehouse."));

            AssertEqual(
                "multiline title falls back",
                "Alice kept moving through the smoke...",
                LlmResponseParser.TitleOrFallback(
                    "Good Title\nExtra commentary",
                    "Alice kept moving through the smoke despite the heat."));

            AssertEqual(
                "fallback strips speech markers",
                "Enough. The hallway filled with smoke...",
                LlmResponseParser.TitleOrFallback(
                    "<uncensored_response>",
                    "[[speech]]Enough.[[/speech]] The hallway filled with smoke."));

            AssertEqual(
                "short fallback still ellipsizes",
                "Alice survived...",
                LlmResponseParser.TitleOrFallback(
                    "<uncensored_response>",
                    "Alice survived."));
        }

        private static void TestSpeechMarkerConstantsMirrorDirectSpeechParser()
        {
            AssertEqual(
                "speech open marker mirrors direct parser",
                DiaryDirectSpeechParser.DefaultOpenMarker,
                PrivateStringConstant(typeof(LlmResponseParser), "SpeechOpenMarker"));

            AssertEqual(
                "speech close marker mirrors direct parser",
                DiaryDirectSpeechParser.DefaultCloseMarker,
                PrivateStringConstant(typeof(LlmResponseParser), "SpeechCloseMarker"));
        }

        private static string PrivateStringConstant(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                throw new InvalidOperationException(type.FullName + "." + fieldName + " was not found.");
            }

            return field.GetRawConstantValue() as string ?? string.Empty;
        }

        private static void TestMiniJsonRejectsMalformedNumbers()
        {
            AssertThrows("double minus number", delegate { MiniJson.Deserialize("--1"); });
            AssertThrows("plus number", delegate { MiniJson.Deserialize("+5"); });
            AssertThrows("double decimal number", delegate { MiniJson.Deserialize("1.2.3"); });
            AssertThrows("missing exponent number", delegate { MiniJson.Deserialize("1e"); });
        }

        private static void TestMiniJsonRejectsExcessiveDepth()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < 300; i++)
            {
                builder.Append('[');
            }

            for (int i = 0; i < 300; i++)
            {
                builder.Append(']');
            }

            AssertThrows("too deep json", delegate { MiniJson.Deserialize(builder.ToString()); });
        }

        private static Dictionary<string, object> Root(string json)
        {
            return LlmResponseParser.ParseResponseRoot(json);
        }

        private static void AssertEqual(string name, string expected, string actual)
        {
            assertions++;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertThrows(string name, Action action)
        {
            assertions++;
            try
            {
                action();
            }
            catch (FormatException)
            {
                return;
            }

            throw new InvalidOperationException(name + " expected a FormatException.");
        }
    }
}
