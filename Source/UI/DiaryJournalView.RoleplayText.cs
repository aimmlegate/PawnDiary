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
    /// Roleplay-text helpers for the reusable diary journal renderer.
    /// </summary>
    internal sealed partial class DiaryJournalView
    {
        /// <summary>
        /// Draws generated text as light roleplay prose. Each line is formatted to rich text by
        /// <see cref="DiaryTextFormat"/> so markdown emphasis renders, direct-speech marker blocks
        /// become separate colored lines, and ordinary quoted speech is colored inline.
        /// The fade-in alpha is applied through GUI.color so inline-colored spans fade with the rest.
        /// </summary>
        private static void DrawRoleplayText(
            Rect rect,
            string text,
            Color dialogueColor,
            float alpha,
            string atmosphereCue,
            bool allowDirectSpeechBlocks,
            DiaryTextDecorationContext decorationContext,
            int seed,
            IEnumerable<DiaryNameHighlight> nameHighlights)
        {
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            // GUI.color multiplies with both the style color and any inline <color> spans, so a single
            // alpha here fades the whole page uniformly during the first-seen reveal.
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

            GUIStyle style = BodyStyle();
            float curY = rect.y;
            foreach (RoleplayLineBlock block in RoleplayBlocks(text, atmosphereCue, allowDirectSpeechBlocks))
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
                string rich = RoleplayRichText(block, dialogueColor, decorationContext, seed, style.fontSize, nameHighlights);
                float lineWidth = Mathf.Max(80f, rect.width - block.leftInset - block.rightInset);
                float textHeight = style.CalcHeight(new GUIContent(rich), lineWidth);
                float blockHeight = textHeight;
                Rect labelRect = new Rect(rect.x + block.leftInset, curY, lineWidth, textHeight);
                if (block.directSpeech)
                {
                    blockHeight += SpeechBlockVerticalPadding * 2f;
                    Rect speechRect = new Rect(
                        labelRect.x - 8f,
                        curY,
                        lineWidth + 12f,
                        blockHeight);
                    Widgets.DrawBoxSolid(speechRect, SpeechBlockBgColor);
                    Widgets.DrawBoxSolid(
                        new Rect(speechRect.x, speechRect.y, 3f, speechRect.height),
                        new Color(dialogueColor.r, dialogueColor.g, dialogueColor.b, UiStyle.speechBlockAccentAlpha));
                    labelRect.y += SpeechBlockVerticalPadding;
                }

                GUI.Label(labelRect, rich, style);
                style.alignment = oldAlignment;
                style.fontStyle = oldStyle;
                curY += blockHeight + RoleplayLineGap + block.extraBottomGap;
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
            Color textColor = Color.Lerp(UiStyle.WritingPlaceholderLowColor, UiStyle.WritingPlaceholderHighColor, WritingPulse(0f));
            Rect dotsRect = new Rect(rect.x, rect.y + Text.LineHeight * 0.5f - 1f, 28f, 8f);
            DrawWritingDots(dotsRect, textColor, 0.4f);
        }

        private static void DrawWritingDots(Rect rect, Color color, float phaseOffset)
        {
            Color oldColor = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                float pulse = WritingPulse(phaseOffset - i * 0.75f);
                Color dotColor = new Color(color.r, color.g, color.b, Mathf.Lerp(UiStyle.writingDotMinAlpha, UiStyle.writingDotMaxAlpha, pulse));
                float yOffset = Mathf.Lerp(UiStyle.writingDotLowYOffset, UiStyle.writingDotHighYOffset, pulse);
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
            bool allowDirectSpeechBlocks,
            DiaryTextDecorationContext decorationContext,
            int seed,
            IEnumerable<DiaryNameHighlight> nameHighlights)
        {
            GUIStyle style = BodyStyle();
            float height = 0f;
            foreach (RoleplayLineBlock block in RoleplayBlocks(text, atmosphereCue, allowDirectSpeechBlocks))
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
                string rich = RoleplayRichText(block, FallbackDialogueColor, decorationContext, seed, style.fontSize, nameHighlights);
                float lineWidth = Mathf.Max(80f, width - block.leftInset - block.rightInset);
                float textHeight = style.CalcHeight(new GUIContent(rich), lineWidth);
                if (block.directSpeech)
                {
                    textHeight += SpeechBlockVerticalPadding * 2f;
                }

                height += textHeight + RoleplayLineGap + block.extraBottomGap;
                style.alignment = oldAlignment;
                style.fontStyle = oldStyle;
            }

            return Mathf.Max(Text.LineHeight, height);
        }

        private static string RoleplayRichText(
            RoleplayLineBlock block,
            Color dialogueColor,
            DiaryTextDecorationContext decorationContext,
            int seed,
            int baseFontSize,
            IEnumerable<DiaryNameHighlight> nameHighlights)
        {
            DiaryTextDecorationPlan decorations = RoleplayDecorationPlan(decorationContext, block != null && block.directSpeech);
            string rich = block != null && block.directSpeech
                ? DiaryTextFormat.ToSpeechBlockRichText(block.line, dialogueColor, decorations, seed ^ block.seedSalt, baseFontSize, nameHighlights)
                : DiaryTextFormat.ToRichText(block?.line ?? string.Empty, dialogueColor, decorations, seed ^ block.seedSalt, baseFontSize, nameHighlights);

            return rich;
        }

        private static DiaryTextDecorationPlan RoleplayDecorationPlan(DiaryTextDecorationContext context, bool directSpeech)
        {
            if (context == null)
            {
                return new DiaryTextDecorationPlan();
            }

            return DiaryTextDecorations.Select(
                context,
                DiaryTextDecorationDefs.CurrentRules,
                directSpeech ? DiaryTextDecorationScopes.DirectSpeech : DiaryTextDecorationScopes.Body);
        }

        private static IEnumerable<RoleplayLineBlock> RoleplayBlocks(string text, string atmosphereCue, bool allowDirectSpeechBlocks)
        {
            if (string.Equals(atmosphereCue, DiaryEntryView.AtmosphereMemorial, StringComparison.Ordinal))
            {
                foreach (RoleplayLineBlock block in MemorialRoleplayBlocks(text, allowDirectSpeechBlocks))
                {
                    yield return block;
                }

                yield break;
            }

            if (string.Equals(atmosphereCue, DiaryEntryView.AtmosphereUnsettled, StringComparison.Ordinal))
            {
                foreach (RoleplayLineBlock block in UnsettledRoleplayBlocks(text, allowDirectSpeechBlocks))
                {
                    yield return block;
                }

                yield break;
            }

            if (string.Equals(atmosphereCue, DiaryEntryView.AtmosphereFractured, StringComparison.Ordinal))
            {
                foreach (RoleplayLineBlock block in FracturedRoleplayBlocks(text, allowDirectSpeechBlocks))
                {
                    yield return block;
                }

                yield break;
            }

            foreach (RoleplayLineBlock block in NormalRoleplayBlocks(text, allowDirectSpeechBlocks))
            {
                yield return block;
            }
        }

        private static IEnumerable<RoleplayLineBlock> NormalRoleplayBlocks(string text, bool allowDirectSpeechBlocks)
        {
            int index = 0;
            DiaryParagraphReflowOptions reflow = DiaryUiStyles.BuildParagraphReflowOptions();
            foreach (DiaryDirectSpeechLine line in RoleplayLines(text, allowDirectSpeechBlocks))
            {
                // Speech blocks and explicit blank lines are never reflowed — only ordinary prose.
                if (line.directSpeech)
                {
                    yield return MakeSpeechRoleplayBlock(line.line, index++);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    continue;
                }

                // Display-only paragraph reflow: split one long prose line into readable chunks. Each
                // chunk becomes its own block, with a blank-line gap between chunks so the split reads
                // as a real paragraph break (the render loop turns empty blocks into paragraph gaps).
                List<string> chunks = DiaryParagraphReflow.ReflowLine(line.line, reflow);
                for (int i = 0; i < chunks.Count; i++)
                {
                    yield return MakeRoleplayBlock(chunks[i], 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    if (i < chunks.Count - 1)
                    {
                        yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    }
                }
            }
        }

        private static IEnumerable<RoleplayLineBlock> FracturedRoleplayBlocks(string text, bool allowDirectSpeechBlocks)
        {
            int index = 0;
            foreach (DiaryDirectSpeechLine line in RoleplayLines(text, allowDirectSpeechBlocks))
            {
                if (line.directSpeech)
                {
                    yield return MakeSpeechRoleplayBlock(line.line, index++);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    continue;
                }

                List<string> fragments = SentenceFragments(line.line);
                for (int i = 0; i < fragments.Count; i++)
                {
                    float indent = index % 3 == 1
                        ? AtmosphereInset * UiStyle.fracturedPrimaryInsetMultiplier
                        : (index % 3 == 2 ? AtmosphereInset * UiStyle.fracturedSecondaryInsetMultiplier : 0f);
                    float rightInset = index % 2 == 0 ? AtmosphereInset * UiStyle.fracturedRightInsetMultiplier : 0f;
                    float topGap = index == 0 ? 0f : (fragments[i].Length <= 28 ? UiStyle.fracturedShortTopGap : UiStyle.fracturedLongTopGap);
                    FontStyle fontStyle = index % 2 == 1 ? FontStyle.Italic : FontStyle.Normal;
                    yield return MakeRoleplayBlock(fragments[i], indent, rightInset, topGap, UiStyle.fracturedBottomGap, fontStyle, TextAnchor.UpperLeft, index++);
                }
            }
        }

        private static IEnumerable<RoleplayLineBlock> UnsettledRoleplayBlocks(string text, bool allowDirectSpeechBlocks)
        {
            int index = 0;
            foreach (DiaryDirectSpeechLine line in RoleplayLines(text, allowDirectSpeechBlocks))
            {
                if (line.directSpeech)
                {
                    yield return MakeSpeechRoleplayBlock(line.line, index++);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperLeft, index++);
                    continue;
                }

                float leftInset = index % 3 == 0 ? AtmosphereInset : (index % 3 == 1 ? AtmosphereInset * 1.7f : AtmosphereInset * 0.55f);
                float rightInset = AtmosphereInset;
                FontStyle fontStyle = index % 2 == 1 ? FontStyle.Italic : FontStyle.Normal;
                yield return MakeRoleplayBlock(line.line, leftInset, rightInset, index == 0 ? 0f : 2f, 2f, fontStyle, TextAnchor.UpperLeft, index++);
            }
        }

        private static IEnumerable<RoleplayLineBlock> MemorialRoleplayBlocks(string text, bool allowDirectSpeechBlocks)
        {
            int index = 0;
            foreach (DiaryDirectSpeechLine line in RoleplayLines(text, allowDirectSpeechBlocks))
            {
                if (line.directSpeech)
                {
                    yield return MakeSpeechRoleplayBlock(line.line, index++);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.line))
                {
                    yield return MakeRoleplayBlock(string.Empty, 0f, 0f, 0f, 0f, FontStyle.Normal, TextAnchor.UpperCenter, index++);
                    continue;
                }

                List<string> fragments = SentenceFragments(line.line);
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
            return MakeRoleplayBlock(line, leftInset, rightInset, extraTopGap, extraBottomGap, fontStyle, alignment, seedSalt, false);
        }

        private static RoleplayLineBlock MakeRoleplayBlock(
            string line,
            float leftInset,
            float rightInset,
            float extraTopGap,
            float extraBottomGap,
            FontStyle fontStyle,
            TextAnchor alignment,
            int seedSalt,
            bool directSpeech)
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
                seedSalt = seedSalt * 7919,
                directSpeech = directSpeech
            };
        }

        private static RoleplayLineBlock MakeSpeechRoleplayBlock(string line, int seedSalt)
        {
            return MakeRoleplayBlock(
                line,
                SpeechBlockLeftInset,
                SpeechBlockLeftInset * 0.5f,
                seedSalt == 0 ? 2f : 4f,
                6f,
                FontStyle.Italic,
                TextAnchor.UpperLeft,
                seedSalt,
                true);
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
        private static IEnumerable<DiaryDirectSpeechLine> RoleplayLines(string text, bool allowDirectSpeechBlocks)
        {
            foreach (DiaryDirectSpeechLine line in DiaryDirectSpeechParser.Lines(
                text,
                allowDirectSpeechBlocks,
                SpeechBlockOpenMarker,
                SpeechBlockCloseMarker))
            {
                yield return line;
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
