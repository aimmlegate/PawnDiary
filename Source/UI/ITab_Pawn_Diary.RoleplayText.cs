// Roleplay prose measurement and drawing helpers for the Diary tab. Split from ITab_Pawn_Diary.cs with no behavior change.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary
    {
        /// <summary>
        /// Draws generated text as light roleplay prose. Each line is formatted to rich text by
        /// <see cref="DiaryTextFormat"/> so markdown emphasis renders and quoted speech is colored
        /// inline with the pawn's dialogue color, while the surrounding narration stays muted prose.
        /// The fade-in alpha is applied through GUI.color so inline-colored spans fade with the rest.
        /// </summary>
        private static void DrawRoleplayText(
            Rect rect,
            string text,
            Color dialogueColor,
            float alpha,
            string atmosphereCue,
            int staggeredIntensity,
            bool distortDirectSpeech,
            int seed)
        {
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            // GUI.color multiplies with both the style color and any inline <color> spans, so a single
            // alpha here fades the whole page uniformly during the first-seen reveal.
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

            GUIStyle style = BodyStyle();
            float curY = rect.y;
            foreach (RoleplayLineBlock block in RoleplayBlocks(text, atmosphereCue))
            {
                curY += block.extraTopGap;
                if (string.IsNullOrWhiteSpace(block.line))
                {
                    curY += RoleplayParagraphGap;
                    continue;
                }

                FontStyle oldStyle = style.fontStyle;
                TextAnchor oldAlignment = style.alignment;
                style.fontStyle = block.fontStyle;
                style.alignment = block.alignment;
                string rich = RoleplayRichText(block, dialogueColor, staggeredIntensity, distortDirectSpeech, seed, style.fontSize);
                float lineWidth = Mathf.Max(80f, rect.width - block.leftInset - block.rightInset);
                float height = style.CalcHeight(new GUIContent(rich), lineWidth);
                GUI.Label(new Rect(rect.x + block.leftInset, curY, lineWidth, height), rich, style);
                style.alignment = oldAlignment;
                style.fontStyle = oldStyle;
                curY += height + RoleplayLineGap + block.extraBottomGap;
            }

            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws a soft pending-row indicator. The dots are simple rectangles, which keeps the
        /// animation cheap in RimWorld's immediate-mode GUI.
        /// </summary>
        private static void DrawWritingPlaceholder(Rect rect)
        {
            Color textColor = Color.Lerp(new Color(0.58f, 0.72f, 0.66f), new Color(0.80f, 0.96f, 0.84f), WritingPulse(0f));
            Rect dotsRect = new Rect(rect.x, rect.y + Text.LineHeight * 0.5f - 1f, 28f, 8f);
            DrawWritingDots(dotsRect, textColor, 0.4f);
        }

        private static void DrawWritingDots(Rect rect, Color color, float phaseOffset)
        {
            Color oldColor = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                float pulse = WritingPulse(phaseOffset - i * 0.75f);
                Color dotColor = new Color(color.r, color.g, color.b, Mathf.Lerp(0.25f, 0.95f, pulse));
                float yOffset = Mathf.Lerp(2f, -1f, pulse);
                Widgets.DrawBoxSolid(
                    new Rect(rect.x + i * (WritingDotSize + WritingDotGap), rect.y + yOffset, WritingDotSize, WritingDotSize),
                    dotColor);
            }

            GUI.color = oldColor;
        }

        /// <summary>
        /// Measures the same roleplay lines that DrawRoleplayText renders. Uses the same rich-text
        /// formatting and body style so the measured wrap height matches what is drawn; the dialogue
        /// color is irrelevant to height (only the bold spans matter, and those are applied here too),
        /// so a fixed fallback color is passed.
        /// </summary>
        private static float RoleplayTextHeight(
            string text,
            float width,
            string atmosphereCue,
            int staggeredIntensity,
            bool distortDirectSpeech,
            int seed)
        {
            GUIStyle style = BodyStyle();
            float height = 0f;
            foreach (RoleplayLineBlock block in RoleplayBlocks(text, atmosphereCue))
            {
                height += block.extraTopGap;
                if (string.IsNullOrWhiteSpace(block.line))
                {
                    height += RoleplayParagraphGap;
                    continue;
                }

                FontStyle oldStyle = style.fontStyle;
                TextAnchor oldAlignment = style.alignment;
                style.fontStyle = block.fontStyle;
                style.alignment = block.alignment;
                string rich = RoleplayRichText(block, FallbackDialogueColor, staggeredIntensity, distortDirectSpeech, seed, style.fontSize);
                float lineWidth = Mathf.Max(80f, width - block.leftInset - block.rightInset);
                height += style.CalcHeight(new GUIContent(rich), lineWidth) + RoleplayLineGap + block.extraBottomGap;
                style.alignment = oldAlignment;
                style.fontStyle = oldStyle;
            }

            return Mathf.Max(Text.LineHeight, height);
        }

        private static string RoleplayRichText(
            RoleplayLineBlock block,
            Color dialogueColor,
            int staggeredIntensity,
            bool distortDirectSpeech,
            int seed,
            int baseFontSize)
        {
            string rich = DiaryTextFormat.ToRichText(block?.line ?? string.Empty, dialogueColor, distortDirectSpeech, seed ^ block.seedSalt);
            if (staggeredIntensity > 0)
            {
                rich = DiaryTextFormat.ApplyStaggeredWordSizes(
                    rich,
                    staggeredIntensity,
                    seed ^ block.seedSalt,
                    baseFontSize);
            }

            return rich;
        }

        private static IEnumerable<RoleplayLineBlock> RoleplayBlocks(string text, string atmosphereCue)
        {
            if (string.Equals(atmosphereCue, DiaryEntryView.AtmosphereMemorial, StringComparison.Ordinal))
            {
                foreach (RoleplayLineBlock block in MemorialRoleplayBlocks(text))
                {
                    yield return block;
                }

                yield break;
            }

            if (string.Equals(atmosphereCue, DiaryEntryView.AtmosphereUnsettled, StringComparison.Ordinal))
            {
                foreach (RoleplayLineBlock block in UnsettledRoleplayBlocks(text))
                {
                    yield return block;
                }

                yield break;
            }

            if (string.Equals(atmosphereCue, DiaryEntryView.AtmosphereFractured, StringComparison.Ordinal))
            {
                foreach (RoleplayLineBlock block in FracturedRoleplayBlocks(text))
                {
                    yield return block;
                }

                yield break;
            }

            foreach (RoleplayLineBlock block in NormalRoleplayBlocks(text))
            {
                yield return block;
            }
        }

        private static IEnumerable<RoleplayLineBlock> NormalRoleplayBlocks(string text)
        {
            int index = 0;
            foreach (string line in RoleplayLines(text))
            {
                yield return MakeRoleplayBlock(line, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
            }
        }

        private static IEnumerable<RoleplayLineBlock> FracturedRoleplayBlocks(string text)
        {
            int index = 0;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    continue;
                }

                List<string> fragments = SentenceFragments(line);
                for (int i = 0; i < fragments.Count; i++)
                {
                    float indent = index % 3 == 1 ? AtmosphereInset : (index % 3 == 2 ? AtmosphereInset * 0.45f : 0f);
                    float topGap = index == 0 ? 0f : (fragments[i].Length <= 28 ? 3f : 1f);
                    yield return MakeRoleplayBlock(fragments[i], indent, 0f, topGap, 1f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                }
            }
        }

        private static IEnumerable<RoleplayLineBlock> UnsettledRoleplayBlocks(string text)
        {
            int index = 0;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    continue;
                }

                float leftInset = index % 3 == 0 ? AtmosphereInset : (index % 3 == 1 ? AtmosphereInset * 1.7f : AtmosphereInset * 0.55f);
                float rightInset = AtmosphereInset;
                FontStyle fontStyle = index % 2 == 1 ? FontStyle.Italic : FontStyle.Normal;
                yield return MakeRoleplayBlock(line, leftInset, rightInset, index == 0 ? 0f : 2f, 2f, fontStyle, TextAnchor.UpperLeft, index++);
            }
        }

        private static IEnumerable<RoleplayLineBlock> MemorialRoleplayBlocks(string text)
        {
            int index = 0;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperCenter, index++);
                    continue;
                }

                List<string> fragments = SentenceFragments(line);
                for (int i = 0; i < fragments.Count; i++)
                {
                    yield return MakeRoleplayBlock(
                        fragments[i],
                        MemorialInset,
                        MemorialInset,
                        index == 0 ? 5f : 3f,
                        4f,
                        FontStyle.Normal,
                        TextAnchor.UpperCenter,
                        index++);
                }
            }
        }

        private static RoleplayLineBlock MakeRoleplayBlock(
            string line,
            float leftInset,
            float rightInset,
            float extraTopGap,
            float extraBottomGap,
            FontStyle fontStyle,
            TextAnchor alignment,
            int seedSalt)
        {
            return new RoleplayLineBlock
            {
                line = line ?? string.Empty,
                leftInset = leftInset,
                rightInset = rightInset,
                extraTopGap = extraTopGap,
                extraBottomGap = extraBottomGap,
                fontStyle = fontStyle,
                alignment = alignment,
                seedSalt = seedSalt * 7919
            };
        }

        private static List<string> SentenceFragments(string line)
        {
            List<string> fragments = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
            {
                return fragments;
            }

            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c != '.' && c != '!' && c != '?')
                {
                    continue;
                }

                int end = i + 1;
                while (end < line.Length && IsClosingSentenceMark(line[end]))
                {
                    end++;
                }

                AddSentenceFragment(fragments, line.Substring(start, end - start));
                start = end;
                while (start < line.Length && char.IsWhiteSpace(line[start]))
                {
                    start++;
                }

                i = start - 1;
            }

            if (start < line.Length)
            {
                AddSentenceFragment(fragments, line.Substring(start));
            }

            if (fragments.Count == 0)
            {
                fragments.Add(line.Trim());
            }

            return fragments;
        }

        private static void AddSentenceFragment(List<string> fragments, string fragment)
        {
            fragment = fragment?.Trim();
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                fragments.Add(fragment);
            }
        }

        private static bool IsClosingSentenceMark(char c)
        {
            return c == '"'
                || c == '\''
                || c == ')'
                || c == ']'
                || c == '\u2019'
                || c == '\u201d';
        }

        private static int StableTextSeed(string text)
        {
            unchecked
            {
                int hash = 17;
                if (!string.IsNullOrEmpty(text))
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        hash = hash * 31 + text[i];
                    }
                }

                return hash == 0 ? 17 : hash;
            }
        }

        /// <summary>
        /// Splits generated text into author-provided lines while preserving blank paragraph breaks.
        /// </summary>
        private static IEnumerable<string> RoleplayLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                yield return lines[i].Trim();
            }
        }

        /// <summary>
        /// Returns the shared body style for diary prose, refreshed each call so it tracks UI-scale
        /// (font/size) changes without reallocating. Rich text is enabled so the inline tags from
        /// <see cref="DiaryTextFormat"/> render; the base color is the muted narrative color, and
        /// inline &lt;color&gt; spans supply the dialogue color. The fade alpha is applied by the
        /// caller through GUI.color, so the style color itself stays at full alpha.
        /// </summary>
        private static GUIStyle BodyStyle()
        {
            GUIStyle baseStyle = Text.CurFontStyle;
            if (bodyStyle == null)
            {
                bodyStyle = new GUIStyle(baseStyle) { wordWrap = true, fontStyle = FontStyle.Normal, richText = true };
            }

            // Refresh the bits that can change at runtime (UI scale) without reallocating the style.
            bodyStyle.font = baseStyle.font;
            bodyStyle.fontSize = baseStyle.fontSize;
            bodyStyle.fontStyle = FontStyle.Normal;
            bodyStyle.alignment = TextAnchor.UpperLeft;
            bodyStyle.normal.textColor = NarrativeColor;
            return bodyStyle;
        }
    }
}
