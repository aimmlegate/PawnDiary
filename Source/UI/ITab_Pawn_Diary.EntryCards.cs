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

        // Tiny footer action geometry. One footer line holds the player-visible "Copy entry" action
        // on the left and the model-name provenance plus regenerate icon on the right.
        private const float EntryFooterActionButtonSize = 16f;
        private const float EntryFooterActionGap = 6f;

        /// <summary>
        /// Explicit inputs needed to draw one expanded diary entry card.
        /// </summary>
        private struct DiaryEntryCardRenderRequest
        {
            public DiaryEntryView Entry;
            public string EntryKey;
            public Rect LocalEntryRect;
            public Rect VisibleEntryRect;
            public Pawn Pawn;
            public DiaryGameComponent Component;
            public Color AccentColor;
            public Color DialogueColor;
            public bool Expanded;
            public float ExpansionBlend;
            public bool ShowLlmDebugInfo;
            public IEnumerable<DiaryNameHighlight> NameHighlights;
        }

        /// <summary>
        /// Draws expanded diary entry cards. Selection, scroll, and expansion state still live in the
        /// tab; this helper only paints the card and reports whether the header was clicked.
        /// </summary>
        private static class DiaryEntryCardRenderer
        {
            public static bool DrawExpanded(DiaryEntryCardRenderRequest request)
            {
                DiaryEntryView entry = request.Entry;
                Rect localEntryRect = request.LocalEntryRect;
                Rect visibleEntryRect = request.VisibleEntryRect;
                Color accentColor = request.AccentColor;

                Widgets.DrawMenuSection(localEntryRect);
                // Faint warm "page" wash behind the body text, drawn under the hover highlight so
                // mouseover still reads. Starts below the title bar and inside the accent strip.
                Rect pageRect = new Rect(
                    localEntryRect.x + EntryAccentWidth + 2f,
                    localEntryRect.y + EntryTitleHeight,
                    Mathf.Max(0f, localEntryRect.width - EntryAccentWidth - 4f),
                    Mathf.Max(0f, localEntryRect.height - EntryTitleHeight - 2f));
                Widgets.DrawBoxSolid(pageRect, EntryPageTintColor(entry));
                Widgets.DrawHighlightIfMouseover(visibleEntryRect);

                Rect titleRect = new Rect(localEntryRect.x, localEntryRect.y, localEntryRect.width, EntryTitleHeight);
                Widgets.DrawTitleBG(titleRect);

                // Group "spine" down the left edge, with a soft inner highlight for depth, then a warm
                // hairline under the header so the body reads as its own page block.
                Rect accentRect = new Rect(localEntryRect.x + 1f, localEntryRect.y + 1f, EntryAccentWidth, localEntryRect.height - 2f);
                Widgets.DrawBoxSolid(accentRect, accentColor);
                Widgets.DrawBoxSolid(new Rect(accentRect.xMax, accentRect.y, 1f, accentRect.height), AccentHighlightColor);
                Widgets.DrawBoxSolid(
                    new Rect(
                        localEntryRect.x + EntryAccentWidth + 8f,
                        localEntryRect.y + EntryTitleHeight,
                        Mathf.Max(0f, localEntryRect.width - EntryAccentWidth - 20f),
                        1f),
                    EntryHeaderRuleColor(entry));

                Rect groupRect = GroupLabelRect(titleRect, entry?.GroupLabel);
                if (groupRect.width > 0f)
                {
                    DrawGroupLabel(groupRect, entry.GroupLabel, accentColor);
                }

                float headerRight = groupRect.width > 0f ? groupRect.x - 6f : localEntryRect.xMax - 8f;
                DrawEntryHeader(
                    new Rect(localEntryRect.x + 34f, localEntryRect.y + 5f, Mathf.Max(80f, headerRight - localEntryRect.x - 34f), 22f),
                    entry,
                    accentColor);
                DrawExpansionIndicator(titleRect, request.Expanded, request.ExpansionBlend, accentColor);
                bool toggleExpansion = Widgets.ButtonInvisible(titleRect, false);

                TooltipHandler.TipRegion(titleRect, "PawnDiary.Tab.ExpandCollapseTip".Translate());

                // Linked entry for the OTHER pawn rendered BEFORE main text when this pawn is the
                // recipient (shows the initiator's perspective first). When this pawn is the
                // initiator, the linked recipient entry goes AFTER the main text instead.
                float textY = localEntryRect.y + EntryTextTop;
                LinkedEntryView linked = entry?.LinkedEntry;
                bool linkedBefore = linked != null && DiaryEvent.RoleEquals(entry.PovRole, DiaryEvent.RecipientRole);
                bool linkedAfter = linked != null && !linkedBefore;
                string footerNote = EntryFooterNote(entry);
                bool showModelName = !string.IsNullOrWhiteSpace(footerNote);
                bool showRegenerateButton = CanShowRegenerateButton(entry);
                string bodyText = EntryBodyText(entry, request.ShowLlmDebugInfo);
                string debugText = request.ShowLlmDebugInfo && entry != null && !IsPromptOnly(entry) ? entry.DebugText : string.Empty;
                float innerTextWidth = localEntryRect.width - 20f;
                string atmosphereCue = EntryAtmosphereCue(entry);
                bool allowDirectSpeechBlocks = EntryAllowDirectSpeechBlocks(entry);
                DiaryTextDecorationContext decorationContext = EntryTextDecorationContext(entry);
                int roleplaySeed = StableTextSeed(request.EntryKey);
                IEnumerable<DiaryNameHighlight> entryNameHighlights = IsPromptOnly(entry) ? null : request.NameHighlights;
                float mainTextHeight = RoleplayTextHeight(
                    bodyText,
                    innerTextWidth,
                    atmosphereCue,
                    allowDirectSpeechBlocks,
                    decorationContext,
                    roleplaySeed,
                    entryNameHighlights);
                float debugTextHeight = DiaryEntryCardMeasurer.DebugTextHeight(debugText, innerTextWidth);

                if (linkedBefore)
                {
                    Rect linkedRect = new Rect(localEntryRect.x + 10f, textY, localEntryRect.width - 20f, LinkedEntryTotalHeight);
                    DrawLinkedEntry(linked, linkedRect, request.Pawn);
                    textY = linkedRect.yMax + LinkedEntryPadding;
                }

                Rect textRect = new Rect(localEntryRect.x + 12f, textY, localEntryRect.width - 20f, mainTextHeight);
                if (IsGenerating(entry))
                {
                    DrawWritingPlaceholder(textRect);
                }
                else
                {
                    DrawRoleplayText(
                        textRect,
                        bodyText,
                        request.DialogueColor,
                        EntryTextAlpha(entry) * BodyExpansionAlpha(request.ExpansionBlend),
                        atmosphereCue,
                        allowDirectSpeechBlocks,
                        decorationContext,
                        roleplaySeed,
                        entryNameHighlights);
                }

                float afterTextY = textRect.yMax;

                if (linkedAfter)
                {
                    Rect linkedRect = new Rect(localEntryRect.x + 10f, afterTextY + LinkedEntryPadding, localEntryRect.width - 20f, LinkedEntryTotalHeight);
                    DrawLinkedEntry(linked, linkedRect, request.Pawn);
                    afterTextY = linkedRect.yMax;
                }

                if (debugTextHeight > 0f)
                {
                    Rect debugRect = new Rect(localEntryRect.x + 12f, afterTextY + DebugTextTopPadding, localEntryRect.width - 20f, debugTextHeight);
                    DrawDebugText(debugRect, debugText);
                }

                bool showCopyButton = ShowCopyButton(entry);
                if (showModelName || showRegenerateButton || showCopyButton)
                {
                    // One footer line: player-visible "Copy entry" on the left, provenance/model name
                    // and the regenerate icon on the right. HasFooterLine reserves its height so the
                    // measured and drawn card heights stay in sync.
                    Rect footerRect = new Rect(localEntryRect.x + 12f, localEntryRect.yMax - EntryBottomPadding - ModelNameHeight, localEntryRect.width - 24f, ModelNameHeight);
                    DrawEntryFooter(footerRect, footerNote, showCopyButton, showRegenerateButton, entry, request.Pawn, request.Component);
                }

                return toggleExpansion;
            }
        }

        /// <summary>
        /// True when the expanded card should show the player-visible "Copy entry" action: any page
        /// that has finished (has generated/fallback text, or a dev prompt-only card) can be copied.
        /// Still-generating pages have nothing worth copying yet, so the button is hidden for them.
        /// </summary>
        private static bool ShowCopyButton(DiaryEntryView entry)
        {
            return entry != null && !IsGenerating(entry) && !string.IsNullOrWhiteSpace(EntryCopyText(entry));
        }

        /// <summary>
        /// Draws the "Copy entry" action (icon + label) at the left of the footer line and returns the
        /// horizontal space it consumed. Clicking copies the page text (or the captured prompt for dev
        /// prompt-only cards) to the system clipboard.
        /// </summary>
        private static float DrawCopyAction(Rect rect, DiaryEntryView entry)
        {
            if (entry == null || rect.width <= 0f)
            {
                return 0f;
            }

            Rect iconRect = new Rect(
                rect.x,
                rect.y + (rect.height - EntryFooterActionButtonSize) * 0.5f,
                EntryFooterActionButtonSize,
                EntryFooterActionButtonSize);

            string label = "PawnDiary.Tab.CopyEntry".Translate();
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float labelWidth = Mathf.Min(Mathf.Max(0f, rect.width - EntryFooterActionButtonSize - 4f), Text.CalcSize(label).x);
            Text.Font = oldFont;

            float totalWidth = EntryFooterActionButtonSize + (labelWidth > 0f ? 4f + labelWidth : 0f);
            Rect actionRect = new Rect(rect.x, rect.y, totalWidth, rect.height);
            bool hover = Mouse.IsOver(actionRect);

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hover ? 0.85f : 0.5f);
            GUI.DrawTexture(iconRect, TexButton.Copy);
            if (labelWidth > 0f)
            {
                GameFont oldLabelFont = Text.Font;
                TextAnchor oldAnchor = Text.Anchor;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(iconRect.xMax + 4f, rect.y, labelWidth, rect.height), label);
                Text.Anchor = oldAnchor;
                Text.Font = oldLabelFont;
            }
            GUI.color = oldColor;

            TooltipHandler.TipRegion(actionRect, "PawnDiary.Tab.CopyEntryTip".Translate());
            if (Widgets.ButtonInvisible(actionRect, false))
            {
                string text = EntryCopyText(entry);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    GUIUtility.systemCopyBuffer = text;
                    Messages.Message("PawnDiary.Tab.CopyEntryCopied".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
            }

            return totalWidth;
        }

        /// <summary>
        /// Queues this saved entry for regeneration, including the linked POV for pairwise events
        /// when that other POV is still eligible for generation.
        /// </summary>
        private static void DrawRegenerateButton(Rect regenerateRect, DiaryEntryView entry, Pawn pawn, DiaryGameComponent component)
        {
            if (entry == null
                || pawn == null
                || component == null
                || entry.Archived
                || IsGenerating(entry)
                || IsPromptOnly(entry))
            {
                return;
            }

            if (!DrawRegenerateFooterIcon(regenerateRect, "PawnDiary.Tab.RegenerateEntryTip".Translate()))
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

        private static bool DrawRegenerateFooterIcon(Rect rect, string tooltip)
        {
            bool hover = Mouse.IsOver(rect);
            Color baseColor = UiStyle.RegenerateEntryButtonColor;
            Color iconColor = hover
                ? new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(baseColor.a + 0.18f))
                : baseColor;

            Color oldColor = GUI.color;
            GUI.color = iconColor;
            bool clicked = Widgets.ButtonImage(rect, TexButton.Rename);
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
        /// True when an entry has a provenance/status note worth showing in the footer.
        /// </summary>
        private static bool HasModelName(DiaryEntryView entry)
        {
            return !string.IsNullOrWhiteSpace(EntryFooterNote(entry));
        }

        /// <summary>
        /// True when the expanded card should reserve the footer line: any of the provenance/model
        /// note, the regenerate action, or the player-visible copy action needs it.
        /// </summary>
        private static bool HasFooterLine(DiaryEntryView entry)
        {
            return HasModelName(entry) || CanShowRegenerateButton(entry) || ShowCopyButton(entry);
        }

        private static bool CanShowRegenerateButton(DiaryEntryView entry)
        {
            return entry != null
                && !entry.Archived
                && !IsGenerating(entry)
                && !IsPromptOnly(entry);
        }

        // Small gap between the muted date segment and the stronger title segment in a card header.
        private const float HeaderDateTitleGap = 8f;

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
        /// Draws the card header as "date \u2014 title". The date is rendered in a smaller in-game font
        /// (GameFont.Tiny) and a muted tone so it reads as a quiet label ahead of the stronger title,
        /// matching the reference mockup. Finished titles keep the short fade + soft accent pulse;
        /// pending title follow-ups keep the date visible and animate dots in the future title slot.
        /// </summary>
        private static void DrawEntryHeader(Rect rect, DiaryEntryView entry, Color accent)
        {
            if (entry == null)
            {
                return;
            }

            float dateWidth = DrawHeaderDate(rect, entry.Date);
            bool hasDate = dateWidth > 0f;
            Rect titleRect = new Rect(rect.x + dateWidth, rect.y, Mathf.Max(0f, rect.width - dateWidth), rect.height);

            if (IsTitleGenerating(entry))
            {
                DrawPendingTitleDots(titleRect, accent, hasDate);
                return;
            }

            string title = EntryDisplayTitle(entry);
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            DrawHeaderTitle(titleRect, entry, title, accent, hasDate);
        }

        /// <summary>
        /// Draws the entry date at the normal header font (GameFont.Small) so it is easy to read, and
        /// returns the horizontal space it consumed (including a trailing gap when a title still fits
        /// after it). The muted date tone still keeps it visually quieter than the title. Returns 0 for
        /// a blank date.
        /// </summary>
        private static float DrawHeaderDate(Rect rect, string date)
        {
            if (string.IsNullOrWhiteSpace(date) || rect.width <= 0f)
            {
                return 0f;
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float width = Mathf.Min(rect.width, Text.CalcSize(date).x);
            GUI.color = UiStyle.EntryDateColor;
            Widgets.Label(new Rect(rect.x, rect.y, width, rect.height), date);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;

            float gap = width < rect.width ? HeaderDateTitleGap : 0f;
            return Mathf.Min(rect.width, width + gap);
        }

        /// <summary>
        /// Draws the title segment in the normal header font with the first-seen fade and accent pulse
        /// the old single-label header used, now applied to just the title. A leading "\u2014 " separator is
        /// drawn only when a date precedes it, so a rare date-less entry shows no dangling em-dash.
        /// </summary>
        private static void DrawHeaderTitle(Rect rect, DiaryEntryView entry, string title, Color accent, bool hasDate)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float age = Time.realtimeSinceStartup - TitleFirstSeenAt(entry);
            float alpha = Mathf.Clamp01(age / TitleFadeDurationSeconds);
            float pulse = Mathf.Lerp(0.22f, 0.38f, WritingPulse(1.4f));
            Color titleColor = Color.Lerp(UiStyle.TitleTextColor, accent, pulse);
            titleColor.a = alpha;
            GUI.color = titleColor;

            Widgets.LabelFit(rect, hasDate ? "\u2014 " + title : title);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Keeps the header active while the cheaper title follow-up is still in flight: draws the
        /// "\u2014 " separator (only when a date precedes it) and animates dots where the title will land.
        /// </summary>
        private static void DrawPendingTitleDots(Rect rect, Color accent, bool hasDate)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float dotsWidth = WritingDotSize * 3f + WritingDotGap * 2f;
            string separator = hasDate ? "\u2014 " : string.Empty;
            float separatorWidth = separator.Length == 0
                ? 0f
                : Mathf.Min(Text.CalcSize(separator).x, Mathf.Max(0f, rect.width - dotsWidth));
            if (separatorWidth > 0f)
            {
                GUI.color = UiStyle.PendingTitlePrefixColor;
                Widgets.Label(new Rect(rect.x, rect.y, separatorWidth, rect.height), separator);
            }

            float dotsX = rect.x + separatorWidth;
            if (dotsX + dotsWidth > rect.xMax)
            {
                dotsX = Mathf.Max(rect.x, rect.xMax - dotsWidth);
            }

            Color dotColor = Color.Lerp(UiStyle.PendingTitleDotBaseColor, accent, 0.45f);
            Rect dotsRect = new Rect(dotsX, rect.y + rect.height * 0.5f - 3f, dotsWidth, 8f);
            DrawWritingDots(dotsRect, dotColor, 1.1f);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws a slim centered "Aprimay \u00b7 Spring \u00b7 5500" separator between the year's entry cards.
        /// A hairline rule runs to each side of the centered label. Icon-free by design.
        /// </summary>
        private static void DrawQuadrumDivider(Rect rect, string label)
        {
            if (rect.width <= 0f || string.IsNullOrEmpty(label))
            {
                return;
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            // GameFont.Small (up from Tiny) so the season/quadrum separator is clearly readable between
            // month groups; the reserved row height (quadrumDividerHeight) is sized to match in XML.
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            float labelWidth = Mathf.Min(rect.width, Text.CalcSize(label).x);
            float labelLeft = rect.x + (rect.width - labelWidth) * 0.5f;
            float labelRight = labelLeft + labelWidth;
            float lineY = rect.y + rect.height * 0.5f - 0.5f;

            Color lineColor = UiStyle.QuadrumDividerLineColor;
            float leftLineEnd = labelLeft - QuadrumDividerLineGap;
            if (leftLineEnd > rect.x)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, lineY, leftLineEnd - rect.x, 1f), lineColor);
            }

            float rightLineStart = labelRight + QuadrumDividerLineGap;
            if (rect.xMax > rightLineStart)
            {
                Widgets.DrawBoxSolid(new Rect(rightLineStart, lineY, rect.xMax - rightLineStart, 1f), lineColor);
            }

            GUI.color = UiStyle.QuadrumDividerLabelColor;
            Widgets.Label(new Rect(labelLeft, rect.y, labelWidth, rect.height), label);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
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

            return new Rect(titleRect.xMax - width - 8f, titleRect.y + 4f, width, 20f);
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

        /// <summary>
        /// Draws the single card footer line: the player-visible "Copy entry" action on the left, then
        /// the quiet provenance/model note and the normal-play rewrite action on the right, without any
        /// of the three stealing another's space.
        /// </summary>
        private static void DrawEntryFooter(
            Rect rect,
            string modelName,
            bool showCopyButton,
            bool showRegenerateButton,
            DiaryEntryView entry,
            Pawn pawn,
            DiaryGameComponent component)
        {
            // Left: copy action. Right edge of the remaining strip retreats as it consumes width.
            float leftEdge = rect.x;
            if (showCopyButton)
            {
                float copyWidth = DrawCopyAction(rect, entry);
                leftEdge = rect.x + copyWidth + EntryFooterActionGap;
            }

            float rightEdge = rect.xMax;
            if (showRegenerateButton)
            {
                Rect regenerateRect = new Rect(
                    rect.xMax - EntryFooterActionButtonSize,
                    rect.y + (rect.height - EntryFooterActionButtonSize) * 0.5f,
                    EntryFooterActionButtonSize,
                    EntryFooterActionButtonSize);
                rightEdge = regenerateRect.x - EntryFooterActionGap;
                DrawRegenerateButton(regenerateRect, entry, pawn, component);
            }

            if (!string.IsNullOrWhiteSpace(modelName) && rightEdge > leftEdge)
            {
                DrawModelName(new Rect(leftEdge, rect.y, rightEdge - leftEdge, rect.height), modelName);
            }
        }

        private static string EntryFooterNote(DiaryEntryView entry)
        {
            string sourceId = entry?.ExternalSourceId ?? string.Empty;
            string note = string.Empty;
            if (IsArchivedGenerationFallback(entry))
            {
                note = "PawnDiary.Tab.ArchivedGenerationFailedFooter".Translate();
            }
            else
            {
                note = entry?.LlmModel ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return note;
            }

            return string.IsNullOrWhiteSpace(note)
                ? "PawnDiary.Tab.ExternalSourceFooter".Translate(sourceId)
                : "PawnDiary.Tab.ExternalSourceWithFooterNote".Translate(sourceId, note);
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

            // One shared, pure picker (DiaryArchiveFallback) drives both this live render and the
            // archive builder, so a stale/failed page shows the identical fact before and after it is
            // compacted. Archived rows carry no prompt, but compaction bakes the resolved fact into
            // entry.Text, which is exactly the raw-text tier this resolver falls back to.
            string fact = DiaryArchiveFallback.ResolveFact(entry.LlmPrompt, entry.Text);
            return TrimArchivedFallbackText(CollapseWhitespace(fact));
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

            return TextTruncation.SafePrefix(value, Math.Max(1, maxChars - 3)).TrimEnd() + "...";
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
            InspectTabBase opened = ITab_Pawn_Diary.OpenDiaryTab();
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
        private static DiaryEntryCardMeasureRequest EntryMeasureRequest(
            DiaryEntryView entry,
            string entryKey,
            float width,
            bool showLlmDebugInfo,
            IEnumerable<DiaryNameHighlight> nameHighlights)
        {
            return new DiaryEntryCardMeasureRequest
            {
                EntryKey = entryKey,
                Width = width,
                ShowLlmDebugInfo = showLlmDebugInfo,
                BodyText = EntryBodyText(entry, showLlmDebugInfo),
                DebugText = showLlmDebugInfo && entry != null ? entry.DebugText : string.Empty,
                AtmosphereCue = EntryAtmosphereCue(entry),
                AllowDirectSpeechBlocks = EntryAllowDirectSpeechBlocks(entry),
                DecorationContext = EntryTextDecorationContext(entry),
                TextSeed = StableTextSeed(entryKey),
                NameHighlights = IsPromptOnly(entry) ? null : nameHighlights,
                HasLinkedEntry = entry != null && entry.LinkedEntry != null,
                HasFooterLine = HasFooterLine(entry),
                EntryTextTop = EntryTextTop,
                EntryBottomPadding = EntryBottomPadding,
                LinkedEntryPadding = LinkedEntryPadding,
                LinkedEntryTotalHeight = LinkedEntryTotalHeight,
                ModelNameTopPadding = ModelNameTopPadding,
                ModelNameHeight = ModelNameHeight,
                DebugTextTopPadding = DebugTextTopPadding,
                RoleplayTextHeight = RoleplayTextHeight,
            };
        }
    }
}
