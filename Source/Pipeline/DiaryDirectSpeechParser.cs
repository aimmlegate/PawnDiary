// Shared direct-speech marker parser. The Diary tab and generated PlayLog injection both use this
// helper so the rule stays identical everywhere: only callers that explicitly allow direct-speech
// blocks get `[[speech]]...[[/speech]]` as speech; every other caller receives marker-stripped prose.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stateless helpers for splitting generated diary prose into ordinary lines and explicit
    /// direct-speech blocks.
    /// </summary>
    public static class DiaryDirectSpeechParser
    {
        public const string DefaultOpenMarker = "[[speech]]";
        public const string DefaultCloseMarker = "[[/speech]]";

        /// <summary>
        /// Splits generated text into author-provided lines while preserving blank paragraph breaks.
        /// Direct-speech blocks are recognized only when <paramref name="allowDirectSpeechBlocks"/>
        /// is true and both markers appear on the same line.
        /// </summary>
        public static IEnumerable<DiaryDirectSpeechLine> Lines(
            string text,
            bool allowDirectSpeechBlocks,
            string openMarker,
            string closeMarker)
        {
            openMarker = NormalizeMarker(openMarker, DefaultOpenMarker);
            closeMarker = NormalizeMarker(closeMarker, DefaultCloseMarker);

            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!allowDirectSpeechBlocks)
                {
                    yield return new DiaryDirectSpeechLine(RemoveMarkers(line, openMarker, closeMarker), false);
                    continue;
                }

                int cursor = 0;
                while (cursor < line.Length)
                {
                    int openIndex = IndexOfMarker(line, openMarker, cursor);
                    if (openIndex < 0)
                    {
                        string prose = line.Substring(cursor).Trim();
                        if (!string.IsNullOrWhiteSpace(prose))
                        {
                            yield return new DiaryDirectSpeechLine(RemoveMarkers(prose, openMarker, closeMarker), false);
                        }

                        break;
                    }

                    string before = line.Substring(cursor, openIndex - cursor).Trim();
                    if (!string.IsNullOrWhiteSpace(before))
                    {
                        yield return new DiaryDirectSpeechLine(RemoveMarkers(before, openMarker, closeMarker), false);
                    }

                    int contentStart = openIndex + openMarker.Length;
                    int closeSameLine = IndexOfMarker(line, closeMarker, contentStart);
                    if (closeSameLine < 0)
                    {
                        // If the model forgot the closing marker, hide the marker but keep the rest
                        // as ordinary prose instead of letting an unclosed block consume later text.
                        string remainder = line.Substring(openIndex).Trim();
                        if (!string.IsNullOrWhiteSpace(remainder))
                        {
                            yield return new DiaryDirectSpeechLine(RemoveMarkers(remainder, openMarker, closeMarker), false);
                        }

                        break;
                    }

                    string speech = line.Substring(contentStart, closeSameLine - contentStart).Trim();
                    if (!string.IsNullOrWhiteSpace(speech))
                    {
                        yield return new DiaryDirectSpeechLine(speech, true);
                    }

                    cursor = closeSameLine + closeMarker.Length;
                }
            }
        }

        /// <summary>
        /// Returns the first successfully parsed direct-speech block, or empty string when none exists.
        /// </summary>
        public static string FirstDirectSpeechBlock(string text, string openMarker, string closeMarker)
        {
            foreach (DiaryDirectSpeechLine line in Lines(text, true, openMarker, closeMarker))
            {
                if (line.directSpeech && !string.IsNullOrWhiteSpace(line.line))
                {
                    return line.line.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Removes all configured speech markers from text without otherwise changing it.
        /// </summary>
        public static string RemoveMarkers(string text, string openMarker, string closeMarker)
        {
            openMarker = NormalizeMarker(openMarker, DefaultOpenMarker);
            closeMarker = NormalizeMarker(closeMarker, DefaultCloseMarker);
            string stripped = RemoveMarker(text, openMarker);
            return RemoveMarker(stripped, closeMarker).Trim();
        }

        private static string NormalizeMarker(string marker, string fallback)
        {
            return string.IsNullOrWhiteSpace(marker) ? fallback : marker;
        }

        private static int IndexOfMarker(string line, string marker, int startIndex)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(marker) || startIndex >= line.Length)
            {
                return -1;
            }

            return line.IndexOf(marker, Math.Max(0, startIndex), StringComparison.OrdinalIgnoreCase);
        }

        private static string RemoveMarker(string text, string marker)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(marker))
            {
                return text ?? string.Empty;
            }

            int index = IndexOfMarker(text, marker, 0);
            while (index >= 0)
            {
                text = text.Remove(index, marker.Length);
                index = IndexOfMarker(text, marker, index);
            }

            return text;
        }
    }

    /// <summary>
    /// One parsed generated-text line, tagged when it came from an explicit direct-speech block.
    /// </summary>
    public struct DiaryDirectSpeechLine
    {
        public readonly string line;
        public readonly bool directSpeech;

        public DiaryDirectSpeechLine(string line, bool directSpeech)
        {
            this.line = line ?? string.Empty;
            this.directSpeech = directSpeech;
        }
    }
}
