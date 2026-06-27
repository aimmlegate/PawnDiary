// Diary entry card chrome, headers, metadata, and linked-entry navigation. Split from ITab_Pawn_Diary.cs with no behavior change.
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
        /// Returns the entry body text for the current view mode: polished generated output in
        /// normal play, or the existing generated/raw/status fallback when debug info is enabled.
        /// </summary>
        private static string EntryBodyText(DiaryEntryView entry, bool showLlmDebugInfo)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (IsPromptOnly(entry) && !string.IsNullOrWhiteSpace(entry.LlmPrompt))
            {
                return entry.LlmPrompt;
            }

            if (IsArchivedGenerationFallback(entry))
            {
                return ArchivedGenerationFallbackBody(entry);
            }

            // Generating entries (revealed by the debug toggle OR the dev-only "show generating"
            // toggle) use DisplayText so an in-progress card shows the "writing..." placeholder /
            // raw text instead of rendering blank. Both the measure and draw passes call this, so
            // card height stays consistent.
            if (showLlmDebugInfo || IsGenerating(entry))
            {
                return entry.DisplayText;
            }

            return entry.GeneratedText;
        }

        // Dev-only footer badge geometry. The footer reserves a thin strip at the bottom of expanded
        // cards (dev mode only) so the tiny action icons clear both the body text and the model-name
        // line drawn just above it. Production card heights are unchanged because DevCopyFooter is 0
        // outside dev.
        private const float DevCopyButtonSize = 16f;
        private const float DevFooterButtonGap = 6f;
        private const float DevCopyButtonFooter = 20f;
        private static float DevCopyFooter => Prefs.DevMode ? DevCopyButtonFooter : 0f;

        /// <summary>
        /// Dev-only: draws small, subtle utility icons anchored to the bottom-left of an expanded
        /// card. Drawn AFTER the body text and model-name line so they live in their own reserved
        /// footer strip (see DevCopyFooter) and never overlap or clip them. Collapsed cards have no
        /// footer icons.
        /// </summary>
        private static void DrawDevFooterButtons(Rect cardRect, DiaryEntryView entry, Pawn pawn, DiaryGameComponent component)
        {
            if (!Prefs.DevMode || entry == null)
            {
                return;
            }

            Rect copyRect = new Rect(
                cardRect.x + 6f,
                cardRect.yMax - DevCopyButtonSize - 5f,
                DevCopyButtonSize,
                DevCopyButtonSize);

            DrawCopyButton(copyRect, entry);

            Rect regenerateRect = new Rect(
                copyRect.xMax + DevFooterButtonGap,
                copyRect.y,
                DevCopyButtonSize,
                DevCopyButtonSize);

            DrawRegenerateButton(regenerateRect, entry, pawn, component);
        }

        /// <summary>
        /// Dev-only: copies the entry's prompt (for prompt-only cards) or generated/visible text to
        /// the system clipboard.
        /// </summary>
        private static void DrawCopyButton(Rect copyRect, DiaryEntryView entry)
        {
            if (entry == null)
            {
                return;
            }

            if (!DrawDevFooterIcon(copyRect, TexButton.Copy, "PawnDiary.Tab.CopyEntryTip".Translate()))
            {
                return;
            }

            string text = EntryCopyText(entry);
            if (!string.IsNullOrWhiteSpace(text))
            {
                GUIUtility.systemCopyBuffer = text;
                Messages.Message("PawnDiary.Tab.CopyEntryCopied".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>
        /// Dev-only: queues this saved entry for regeneration, including the linked POV for pairwise
        /// events when that other POV is still eligible for generation.
        /// </summary>
        private static void DrawRegenerateButton(Rect regenerateRect, DiaryEntryView entry, Pawn pawn, DiaryGameComponent component)
        {
            if (entry == null
                || pawn == null
                || component == null
                || entry.LlmStatus == DiaryEvent.PendingStatus
                || IsPromptOnly(entry))
            {
                return;
            }

            if (!DrawDevFooterIcon(regenerateRect, TexButton.Reload, "PawnDiary.Tab.RegenerateEntryTip".Translate()))
            {
                return;
            }

            if (component.RegenerateEntry(pawn, entry))
            {
                Messages.Message("PawnDiary.Tab.RegenerateEntryQueued".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("PawnDiary.Tab.RegenerateEntryUnavailable".Translate(), MessageTypeDefOf.RejectInput, false);
            }
        }

        private static bool DrawDevFooterIcon(Rect rect, Texture2D icon, string tooltip)
        {
            // CSS-opacity style: dim at rest, slight brighten on hover so it still reads as clickable.
            bool hover = Mouse.IsOver(rect);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hover ? 0.85f : 0.5f);
            bool clicked = Widgets.ButtonImage(rect, icon);
            GUI.color = oldColor;

            if (!string.IsNullOrWhiteSpace(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
        }

        /// <summary>
        /// The text a dev copy-button copies for an entry: the captured prompt for prompt-only cards,
        /// otherwise the LLM-generated text, falling back to the raw display text.
        /// </summary>
        private static string EntryCopyText(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (IsPromptOnly(entry) && !string.IsNullOrWhiteSpace(entry.LlmPrompt))
            {
                return entry.LlmPrompt;
            }

            if (IsArchivedGenerationFallback(entry))
            {
                return ArchivedGenerationFallbackBody(entry);
            }

            if (!string.IsNullOrWhiteSpace(entry.GeneratedText))
            {
                return entry.GeneratedText;
            }

            return entry.DisplayText ?? string.Empty;
        }

        /// <summary>
        /// True while an entry is still waiting on the background LLM generation pipeline.
        /// </summary>
        private static bool IsGenerating(DiaryEntryView entry)
        {
            return entry != null && entry.LlmStatus == DiaryEvent.PendingStatus && !entry.ArchivedGenerationStale;
        }

        /// <summary>
        /// True for archived pending entries that are no longer in the hot retry/title/orphan scan
        /// window. They render as failed archive fallbacks, not active writing jobs.
        /// </summary>
        private static bool IsArchivedGenerationFallback(DiaryEntryView entry)
        {
            return entry != null && entry.ArchivedGenerationStale;
        }

        /// <summary>
        /// True for dev prompt-capture cards. These render the stored prompt text directly and never
        /// pretend an LLM wrote a diary page.
        /// </summary>
        private static bool IsPromptOnly(DiaryEntryView entry)
        {
            return entry != null && entry.LlmStatus == DiaryEvent.PromptOnlyStatus;
        }

        /// <summary>
        /// Title follow-ups run after the main entry succeeds, so a completed diary page can still
        /// be waiting on its short header title. This flag drives the small header-only animation.
        /// </summary>
        private static bool IsTitleGenerating(DiaryEntryView entry)
        {
            return entry != null
                && TitlesEnabled()
                && entry.TitlePending
                && string.IsNullOrWhiteSpace(entry.Title);
        }

        /// <summary>
        /// True when an entry has a model id worth showing as a quiet provenance hint.
        /// </summary>
        private static bool HasModelName(DiaryEntryView entry)
        {
            return !string.IsNullOrWhiteSpace(EntryFooterNote(entry));
        }

        /// <summary>
        /// Produces the compact header shown on each entry card: the date, then a stored
        /// LLM-generated subject ("date — title") when title display is enabled and one exists.
        /// Otherwise entries show just the date — no dangling separator.
        /// </summary>
        private static string EntryHeader(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string title = EntryDisplayTitle(entry);
            if (string.IsNullOrWhiteSpace(title))
            {
                return entry.Date ?? string.Empty;
            }

            return (entry.Date ?? string.Empty) + " \u2014 " + title;
        }

        private static string EntryDisplayTitle(DiaryEntryView entry)
        {
            if (entry == null || !TitlesEnabled())
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.Title))
            {
                return entry.Title;
            }

            return IsArchivedGenerationFallback(entry) ? ArchivedGenerationFallbackTitle(entry) : string.Empty;
        }

        /// <summary>
        /// Draws the date/title line. Finished titles get a short fade and a soft color pulse;
        /// pending title follow-ups keep the date visible and animate a tiny placeholder where the
        /// subject will appear.
        /// </summary>
        private static void DrawEntryHeader(Rect rect, DiaryEntryView entry, Color accent)
        {
            if (IsTitleGenerating(entry))
            {
                DrawPendingTitleHeader(rect, entry, accent);
                return;
            }

            string header = EntryHeader(entry);
            if (string.IsNullOrWhiteSpace(header))
            {
                return;
            }

            bool hasTitle = !string.IsNullOrWhiteSpace(EntryDisplayTitle(entry));
            Color oldColor = GUI.color;
            if (hasTitle)
            {
                float age = Time.realtimeSinceStartup - TitleFirstSeenAt(entry);
                float alpha = Mathf.Clamp01(age / TitleFadeDurationSeconds);
                float pulse = Mathf.Lerp(0.22f, 0.38f, WritingPulse(1.4f));
                Color titleColor = Color.Lerp(UiStyle.TitleTextColor, accent, pulse);
                titleColor.a = alpha;
                GUI.color = titleColor;
            }
            else
            {
                GUI.color = UiStyle.TitleTextColor;
            }

            Widgets.LabelFit(rect, header);
            GUI.color = oldColor;
        }

        /// <summary>
        /// Keeps the completed date readable while the cheaper title follow-up is still in flight.
        /// The animated dots occupy the future title slot so the card still feels active.
        /// </summary>
        private static void DrawPendingTitleHeader(Rect rect, DiaryEntryView entry, Color accent)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            float dotsWidth = WritingDotSize * 3f + WritingDotGap * 2f;
            string prefix = string.IsNullOrWhiteSpace(entry?.Date)
                ? string.Empty
                : (entry.Date + " \u2014 ");

            Color oldColor = GUI.color;
            float dotsX = rect.x;
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                // Measure the date prefix itself so the dots sit at the title's future left edge,
                // not at the far side of a mostly empty LabelFit rectangle.
                float availablePrefixWidth = Mathf.Max(0f, rect.width - dotsWidth);
                float prefixWidth = Mathf.Min(Text.CalcSize(prefix).x, availablePrefixWidth);
                if (prefixWidth > 0f)
                {
                    Rect prefixRect = new Rect(rect.x, rect.y, prefixWidth, rect.height);
                    GUI.color = UiStyle.PendingTitlePrefixColor;
                    Widgets.LabelFit(prefixRect, prefix);
                    dotsX = prefixRect.xMax;
                }
            }

            if (dotsX + dotsWidth > rect.xMax)
            {
                dotsX = Mathf.Max(rect.x, rect.xMax - dotsWidth);
            }

            Color dotColor = Color.Lerp(UiStyle.PendingTitleDotBaseColor, accent, 0.45f);
            Rect dotsRect = new Rect(dotsX, rect.y + rect.height * 0.5f - 3f, dotsWidth, 8f);
            DrawWritingDots(dotsRect, dotColor, 1.1f);
            GUI.color = oldColor;
        }

        /// <summary>
        /// The title-generation setting doubles as the display toggle: disabling it means no
        /// titles in card headers, even if older entries already have stored titles.
        /// </summary>
        private static bool TitlesEnabled()
        {
            return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.generateTitles;
        }

        /// <summary>
        /// Master display toggle for rare extreme-entry typography. Defaults on for old settings.
        /// </summary>
        private static bool AtmosphericFormattingEnabled()
        {
            return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.enableAtmosphericFormatting;
        }

        private static string EntryAtmosphereCue(DiaryEntryView entry)
        {
            if (IsPromptOnly(entry))
            {
                return string.Empty;
            }

            return AtmosphericFormattingEnabled() ? (entry?.AtmosphereCue ?? string.Empty) : string.Empty;
        }

        private static DiaryTextDecorationContext EntryTextDecorationContext(DiaryEntryView entry)
        {
            if (IsPromptOnly(entry))
            {
                return null;
            }

            return AtmosphericFormattingEnabled() && entry != null ? entry.TextDecorationContext : null;
        }

        /// <summary>
        /// Recipient POVs should stay prose-only. If a model leaks [[speech]] markers into that
        /// second-perspective entry anyway, the roleplay parser strips the markers and renders the
        /// words as ordinary text instead of a dedicated speech block.
        /// </summary>
        private static bool EntryAllowDirectSpeechBlocks(DiaryEntryView entry)
        {
            return entry != null && !IsPromptOnly(entry) && !DiaryEvent.RoleEquals(entry.PovRole, DiaryEvent.RecipientRole);
        }

        /// <summary>
        /// Returns the color strip used to mark the entry group. The saved cue key follows
        /// RimWorld-like meaning (hostile red, muted mental-break green, quiet gray) instead of hashing
        /// localized group labels.
        /// </summary>
        private static Color EntryAccentColor(DiaryEntryView entry)
        {
            return entry == null
                ? UiStyle.DefaultCueColor
                : ColorForCue(entry.ColorCue, entry.Important);
        }

        /// <summary>
        /// Returns the body-page wash for the entry. Conflict pages get stronger cue-specific
        /// washes so they read differently from ordinary quiet pages before the text is inspected.
        /// </summary>
        private static Color EntryPageTintColor(DiaryEntryView entry)
        {
            return UiStyle.PageTintForCue(entry?.ColorCue);
        }

        /// <summary>
        /// Returns the separator line color under the card header, matching any cue-specific wash.
        /// </summary>
        private static Color EntryHeaderRuleColor(DiaryEntryView entry)
        {
            return UiStyle.HeaderRuleForCue(entry?.ColorCue);
        }

        /// <summary>
        /// Maps stable diary color cues onto RimWorld-style UI colors. Empty cues fall back to a
        /// warm neutral for important entries and light gray for non-important ones.
        /// </summary>
        private static Color ColorForCue(string cue, bool important)
        {
            return UiStyle.ColorForCue(cue, important);
        }

        /// <summary>
        /// Reserves a small right-side label for the event group, leaving the date/title room to
        /// shrink gracefully on narrow tabs.
        /// </summary>
        private static Rect GroupLabelRect(Rect titleRect, string groupLabel)
        {
            if (string.IsNullOrWhiteSpace(groupLabel) || titleRect.width < 240f)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float width = Mathf.Min(EntryLabelMaxWidth, Text.CalcSize(groupLabel).x + 18f);
            Text.Font = oldFont;

            return new Rect(titleRect.xMax - width - 8f, titleRect.y + 5f, width, 18f);
        }

        /// <summary>
        /// Draws the color-coded group name as a quiet chip in the card header.
        /// </summary>
        private static void DrawGroupLabel(Rect rect, string label, Color accent)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(accent.r * 0.23f, accent.g * 0.23f, accent.b * 0.23f, 0.72f),
                new Color(accent.r, accent.g, accent.b, 0.92f),
                1);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.Lerp(accent, Color.white, 0.55f);
            Widgets.LabelFit(new Rect(rect.x + 4f, rect.y + 1f, rect.width - 8f, rect.height - 2f), label);
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// New cards fade their text in the first time this tab sees the finished entry. The key
        /// includes the status so a row seen as "writing" can still animate when the page completes.
        /// </summary>
        private static float EntryTextAlpha(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return 1f;
            }

            float firstSeen = EntryFirstSeenAt(entry);
            return Mathf.Clamp01((Time.realtimeSinceStartup - firstSeen) / EntryFadeDurationSeconds);
        }

        private static float EntryFirstSeenAt(DiaryEntryView entry)
        {
            string key = (entry.EventId ?? string.Empty)
                + "|"
                + (entry.PovRole ?? string.Empty)
                + "|"
                + (IsGenerated(entry) ? "written" : entry.LlmStatus ?? string.Empty);

            return FirstSeenAt(key);
        }

        private static float TitleFirstSeenAt(DiaryEntryView entry)
        {
            string key = (entry?.EventId ?? string.Empty)
                + "|"
                + (entry?.PovRole ?? string.Empty)
                + "|title|"
                + (entry?.Title ?? string.Empty);

            return FirstSeenAt(key);
        }

        private static float FirstSeenAt(string key)
        {
            float firstSeen;
            if (!EntryFirstSeenSeconds.TryGetValue(key, out firstSeen))
            {
                if (EntryFirstSeenSeconds.Count >= MaxFirstSeenEntries)
                {
                    EntryFirstSeenSeconds.Clear();
                }

                firstSeen = Time.realtimeSinceStartup;
                EntryFirstSeenSeconds[key] = firstSeen;
            }

            return firstSeen;
        }

        private static float WritingPulse(float phaseOffset)
        {
            return (Mathf.Sin(Time.realtimeSinceStartup * UiStyle.writingPulseSpeed + phaseOffset) + 1f) * 0.5f;
        }

        /// <summary>
        /// Uses the pawn's RimWorld favorite color for dialogue, brightened enough for dark UI.
        /// </summary>
        private static Color PreferredDialogueColor(Pawn pawn)
        {
            Color color = pawn?.story?.favoriteColor != null ? pawn.story.favoriteColor.color : FallbackDialogueColor;
            color.a = 1f;

            // Lift dark favorite colors toward a readable brightness on the dark card. Use perceived
            // luminance (green-weighted) rather than the max channel, so deep blues and reds — which
            // a max-channel check under-corrects — are still raised enough to read.
            float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            const float minLuminance = 0.5f;
            if (luminance < minLuminance)
            {
                float lift = minLuminance - luminance;
                color.r = Mathf.Clamp01(color.r + lift);
                color.g = Mathf.Clamp01(color.g + lift);
                color.b = Mathf.Clamp01(color.b + lift);
            }

            return color;
        }

        /// <summary>
        /// Draws the model id, or the archived failure note, as a tiny low-contrast footer.
        /// </summary>
        private static void DrawModelName(Rect rect, string modelName)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = UiStyle.ModelNameColor;
            Widgets.LabelFit(rect, modelName);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static string EntryFooterNote(DiaryEntryView entry)
        {
            if (IsArchivedGenerationFallback(entry))
            {
                return "PawnDiary.Tab.ArchivedGenerationFailedFooter".Translate();
            }

            return entry?.LlmModel ?? string.Empty;
        }

        private static string ArchivedGenerationFallbackBody(DiaryEntryView entry)
        {
            string fact = ArchivedGenerationFallbackFact(entry);
            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = "PawnDiary.Tab.ArchivedGenerationFallbackUnknown".Translate();
            }

            return "PawnDiary.Tab.ArchivedGenerationFallback".Translate(fact);
        }

        private static string ArchivedGenerationFallbackTitle(DiaryEntryView entry)
        {
            return FirstWords(ArchivedGenerationFallbackFact(entry), DiaryTuning.ArchivedFallbackTitleWords);
        }

        private static string ArchivedGenerationFallbackFact(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string fact = PromptFieldValue(entry.LlmPrompt, "what you saw");
            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = PromptFieldValue(entry.LlmPrompt, "what happened");
            }

            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = PromptFieldValue(entry.LlmPrompt, "death facts");
            }

            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = PromptFieldValue(entry.LlmPrompt, "arrival facts");
            }

            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = PromptFieldValue(entry.LlmPrompt, "entry");
            }

            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = entry.Text;
            }

            if (string.IsNullOrWhiteSpace(fact))
            {
                fact = FirstPromptLine(entry.LlmPrompt);
            }

            return TrimArchivedFallbackText(CollapseWhitespace(fact));
        }

        private static string PromptFieldValue(string prompt, string label)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            string normalized = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
            string prefix = label + ":";
            int start = 0;
            while (start < normalized.Length)
            {
                int end = normalized.IndexOf('\n', start);
                if (end < 0)
                {
                    end = normalized.Length;
                }

                string line = normalized.Substring(start, end - start).Trim();
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(prefix.Length).Trim();
                }

                start = end + 1;
            }

            return string.Empty;
        }

        private static string FirstPromptLine(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            string normalized = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
            int start = 0;
            while (start < normalized.Length)
            {
                int end = normalized.IndexOf('\n', start);
                if (end < 0)
                {
                    end = normalized.Length;
                }

                string line = normalized.Substring(start, end - start).Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }

                start = end + 1;
            }

            return string.Empty;
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] chars = new char[value.Length];
            int count = 0;
            bool previousWhitespace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace && count > 0)
                    {
                        chars[count++] = ' ';
                    }

                    previousWhitespace = true;
                    continue;
                }

                chars[count++] = c;
                previousWhitespace = false;
            }

            return new string(chars, 0, count).Trim();
        }

        private static string TrimArchivedFallbackText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int maxChars = DiaryTuning.ArchivedFallbackTextMaxChars;
            if (value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, Math.Max(1, maxChars - 3)).TrimEnd() + "...";
        }

        private static string FirstWords(string value, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(value) || maxWords <= 0)
            {
                return string.Empty;
            }

            string normalized = CollapseWhitespace(value);
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

            string title = normalized.Substring(0, Math.Min(end, normalized.Length)).Trim();
            if (end < normalized.Length)
            {
                title += "...";
            }

            return title;
        }

        /// <summary>
        /// Draws the existing English-only diagnostic block in tiny muted text.
        /// </summary>
        private static void DrawDebugText(Rect rect, string debugText)
        {
            if (string.IsNullOrWhiteSpace(debugText))
            {
                return;
            }

            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = UiStyle.DebugTextColor;
            Widgets.Label(rect, debugText);

            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws a compact linked-entry card showing a truncated preview of the other pawn's
        /// diary entry for the same event. Clicking it selects the other pawn, opens their diary
        /// tab, and scrolls to the same event.
        /// </summary>
        private static void DrawLinkedEntry(LinkedEntryView link, Rect rect, Pawn currentPawn)
        {
            if (link == null)
            {
                return;
            }

            // Hover highlight for the clickable area
            bool hovered = Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(rect, hovered ? LinkedEntryHoverColor : LinkedEntryBgColor);
            // Draw border using thin solid rects (Widgets.DrawBox with color is unavailable)
            GUI.color = LinkedEntryBorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // Left-side accent strip colored by the linked role
            Color stripColor = DiaryEvent.RoleEquals(link.OtherRole, DiaryEvent.InitiatorRole)
                ? UiStyle.PaletteColor(UiStyle.linkedInitiatorPaletteIndex, new Color(0.95f, 0.58f, 0.32f))
                : UiStyle.PaletteColor(UiStyle.linkedRecipientPaletteIndex, new Color(0.45f, 0.63f, 0.92f));
            Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, 4f, rect.height - 2f), stripColor);

            // Label line: "Alice's perspective (initiator):"
            string roleLabel = DiaryEvent.RoleEquals(link.OtherRole, DiaryEvent.InitiatorRole)
                ? "PawnDiary.Tab.LinkedInitiator".Translate(link.OtherPawnName)
                : "PawnDiary.Tab.LinkedRecipient".Translate(link.OtherPawnName);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = UiStyle.LinkedEntryLabelColor;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 14f, LinkedEntryLabelHeight), roleLabel);

            // Truncated preview text
            GUI.color = LinkedEntryTextColor;
            Text.Font = GameFont.Tiny;
            string preview = link.TruncatedText;
            if (!link.Generated && !string.IsNullOrWhiteSpace(preview))
            {
                preview = "PawnDiary.Tab.LinkedNotGenerated".Translate() + " " + preview;
            }
            else if (string.IsNullOrWhiteSpace(preview))
            {
                preview = "PawnDiary.Tab.LinkedNoText".Translate();
            }
            Widgets.Label(new Rect(rect.x + 10f, rect.y + LinkedEntryLabelHeight + 2f, rect.width - 14f, LinkedEntryTextHeight), preview);
            GUI.color = oldColor;
            Text.Font = oldFont;

            // Click handler: navigate to the other pawn's diary at this event
            if (Widgets.ButtonInvisible(rect, false))
            {
                NavigateToLinkedEntry(link, currentPawn);
            }

            // Tooltip hint
            if (hovered)
            {
                TooltipHandler.TipRegion(rect, "PawnDiary.Tab.LinkedTooltip".Translate(link.OtherPawnName));
            }
        }

        /// <summary>
        /// Selects the other pawn involved in a linked entry, opens their diary tab,
        /// and scrolls to the same event.
        /// </summary>
        private static void NavigateToLinkedEntry(LinkedEntryView link, Pawn currentPawn)
        {
            if (link == null || string.IsNullOrWhiteSpace(link.OtherPawnId))
            {
                return;
            }

            Pawn otherPawn = FindPawnByLoadId(link.OtherPawnId);
            if (otherPawn == null || !otherPawn.Spawned)
            {
                return;
            }

            // Select the other pawn (same pattern as the Social-tab click patch)
            if (Find.Selector == null)
            {
                return;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(otherPawn, true, false);

            // Request scroll to the shared event and open the diary tab
            ITab_Pawn_Diary.RequestScrollToEntry(otherPawn, link.EventId);
            InspectTabBase opened = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Diary));
            if (!(opened is ITab_Pawn_Diary))
            {
                ITab_Pawn_Diary.ClearPendingScrollRequest();
            }
        }

        /// <summary>
        /// Finds a live Pawn by its RimWorld unique load ID. Searches all pawns on the
        /// current map first, then falls back to the world pawns list.
        /// </summary>
        private static Pawn FindPawnByLoadId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            // Fast path: check the current map's pawn list
            if (Find.CurrentMap != null)
            {
                foreach (Pawn p in Find.CurrentMap.mapPawns.AllPawns)
                {
                    if (p != null && p.GetUniqueLoadID() == pawnId)
                    {
                        return p;
                    }
                }
            }

            // Fallback: world pawns (off-map colonists, etc.)
            if (Find.WorldPawns != null)
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAlive)
                {
                    if (p != null && p.GetUniqueLoadID() == pawnId)
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates the height needed for a single diary entry card, accounting for
        /// dynamic text wrapping of the generated diary text and the linked-entry card
        /// (if present) positioned before or after the main text.
        /// </summary>
        private static float EntryHeight(
            DiaryEntryView entry,
            float width,
            bool showLlmDebugInfo,
            IEnumerable<DiaryNameHighlight> nameHighlights)
        {
            // Must match the draw width in FillTab (entryRect.width - 20f) so the measured wrap
            // height equals what is actually rendered; a wider measure clips long entries at the bottom.
            float innerWidth = width - 20f;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            IEnumerable<DiaryNameHighlight> entryNameHighlights = IsPromptOnly(entry) ? null : nameHighlights;
            float textHeight = RoleplayTextHeight(
                EntryBodyText(entry, showLlmDebugInfo),
                innerWidth,
                EntryAtmosphereCue(entry),
                EntryAllowDirectSpeechBlocks(entry),
                EntryTextDecorationContext(entry),
                StableTextSeed(EntryKey(entry)),
                entryNameHighlights);
            Text.Font = oldFont;

            float height = EntryTextTop + textHeight + EntryBottomPadding;

            // Add space for the linked-entry card and its surrounding padding
            if (entry.LinkedEntry != null)
            {
                height += LinkedEntryTotalHeight + LinkedEntryPadding;
            }

            if (HasModelName(entry))
            {
                height += ModelNameTopPadding + ModelNameHeight;
            }

            if (showLlmDebugInfo)
            {
                float debugHeight = DebugTextHeight(entry?.DebugText, innerWidth);
                if (debugHeight > 0f)
                {
                    height += DebugTextTopPadding + debugHeight;
                }
            }

            // Reserve the dev-only footer so the bottom-left copy badge clears the model-name line.
            // Outside dev mode DevCopyFooter is 0 and production card heights are unchanged.
            height += DevCopyFooter;
            return height;
        }

        /// <summary>
        /// Measures the tiny diagnostic text block shown only when the dev debug toggle is enabled.
        /// </summary>
        private static float DebugTextHeight(string debugText, float width)
        {
            if (string.IsNullOrWhiteSpace(debugText))
            {
                return 0f;
            }

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float height = Text.CalcHeight(debugText, width);
            Text.Font = oldFont;
            return height;
        }
    }
}
