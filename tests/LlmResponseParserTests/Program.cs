using System;
using System.Collections.Generic;
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
            TestGeneratedTextCleanup();

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
                "responses output_text",
                "Preferred text.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"output_text\":\"Preferred text.\",\"output\":[{\"content\":[{\"text\":\"Fallback.\"}]}]}"),
                    LlmResponseMode.OpenAIResponses));

            AssertEqual(
                "responses parts skip reasoning",
                "Alpha.\nBeta.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"output\":[{\"type\":\"reasoning\",\"content\":[{\"text\":\"secret\"}]},{\"content\":[{\"type\":\"output_text\",\"text\":\"Alpha.\"},{\"type\":\"reasoning_text\",\"text\":\"hidden\"},{\"type\":\"output_text\",\"text\":\"Beta.\"}]}]}"),
                    LlmResponseMode.OpenAIResponses));

            AssertEqual(
                "ollama message content",
                "Ollama entry.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"message\":{\"content\":\"Ollama entry.\"},\"done\":true}"),
                    LlmResponseMode.OllamaNativeChat));

            AssertEqual(
                "ollama response fallback",
                "Legacy response.",
                LlmResponseParser.ParseGeneratedText(
                    Root("{\"response\":\"Legacy response.\",\"done\":true}"),
                    LlmResponseMode.OllamaNativeChat));
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
                "ollama thinking only",
                "Ollama returned thinking text but no message content.",
                LlmResponseParser.ExtractProviderError(
                    Root("{\"message\":{\"thinking\":\"secret\"},\"done\":true}"),
                    LlmResponseMode.OllamaNativeChat,
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
                "fenced reasoning",
                "Answer.",
                LlmResponseParser.StripReasoningTextBlocks("```thinking\nsecret\n```\nAnswer."));

            AssertEqual(
                "reasoning heading",
                "Visible.",
                LlmResponseParser.StripReasoningTextBlocks("Thinking:\nsecret\nAnswer: Visible."));
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
    }
}
