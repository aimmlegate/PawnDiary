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
                "final answer: We held the line.",
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

            // Two paired blocks with different tag names exercise the outer per-tag loop.
            AssertEqual(
                "multiple paired tags",
                "Held the gate. Bob smiled.",
                LlmResponseParser.StripReasoningTextBlocks("<think>plan</think>Held the gate.<reasoning>why</reasoning> Bob smiled."));
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
