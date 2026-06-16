// The inspector tab (UI) that renders the selected pawn's finished diary entries.
// RimWorld calls FillTab() to draw it using immediate-mode GUI (the whole tab is re-emitted each
// frame). It reads entries via DiaryGameComponent.EntriesFor. See AGENTS.md ("lifecycle").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // Inspector tab that shows the selected pawn's diary. Replaces the old standalone
    // Diary window/gizmo and is injected immediately after the vanilla Needs tab at startup.
    public class ITab_Pawn_Diary : ITab
    {
        // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        // Vertical space constants for the per-pawn controls above the scroll view. The actual
        // height is dynamic because dev-mode controls are hidden during normal play.
        private const float ControlLineHeight = 28f;
        private const float ControlGap = 2f;
        private const float EntryTitleHeight = 28f;
        private const float EntryTextTop = 42f;
        private const float EntryBottomPadding = 10f;
        private const float StatusBadgeWidth = 34f;
        private const float StatusBadgeHeight = 24f;
        private const float StatusBadgeRightPadding = 24f;
        private const float RoleplayLineGap = 5f;
        private const float RoleplayParagraphGap = 8f;
        private const float EntryGap = 8f;
        private const int AutoExpandedEntryCount = 15;
        private const int DevMockDiaryTargetCount = 360;
        private const float CollapsedEntryChromePadding = 2f;
        private const float CollapsedEntryHeight = EntryTitleHeight + CollapsedEntryChromePadding;
        private const float ExpansionAnimationSpeed = 5.5f;
        private const float LinkedEntryPadding = 8f;
        private const float LinkedEntryLabelHeight = 20f;
        private const float LinkedEntryTextHeight = 36f;
        private const float LinkedEntryTotalHeight = 64f;
        private const float YearFilterHeight = 32f;
        private const float YearFilterGap = 6f;
        private const float YearButtonWidth = 112f;
        private const float ModelNameTopPadding = 4f;
        private const float ModelNameHeight = 20f;
        private const float DebugTextTopPadding = 8f;
        private const float EntryAccentWidth = 6f;
        private const float EntryLabelMaxWidth = 148f;
        private const float EntryFadeDurationSeconds = 0.55f;
        private const float TitleFadeDurationSeconds = 0.8f;
        private const float WritingDotSize = 4f;
        private const float WritingDotGap = 5f;

        private static readonly Color QuietColor = new Color(0.42f, 0.48f, 0.52f);
        private static readonly Color NarrativeColor = new Color(0.78f, 0.78f, 0.72f);
        private static readonly Color FallbackDialogueColor = new Color(0.58f, 0.80f, 1f);
        // Faint warm wash painted behind each card's body text to suggest a journal page. Kept at a
        // very low alpha so it tints the dark RimWorld card chrome without muddying it.
        private static readonly Color PageTintColor = new Color(0.91f, 0.83f, 0.66f, 0.07f);
        // Warm hairline drawn under the header so the page body reads as its own block.
        private static readonly Color HeaderRuleColor = new Color(0.62f, 0.58f, 0.50f, 0.35f);
        // Soft inner highlight beside the group "spine" to give the left edge a little depth.
        private static readonly Color AccentHighlightColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color LinkedEntryBgColor = new Color(0.15f, 0.17f, 0.20f, 0.85f);
        private static readonly Color LinkedEntryBorderColor = new Color(0.35f, 0.45f, 0.55f);
        private static readonly Color LinkedEntryTextColor = new Color(0.65f, 0.70f, 0.75f);
        private static readonly Color LinkedEntryHoverColor = new Color(0.25f, 0.30f, 0.38f, 0.90f);
        // Pleasant, readable accents for group-coded diary cards. The group label selects the color
        // deterministically, so old entries get the same marker every time without adding save data.
        private static readonly Color[] EntryAccentPalette =
        {
            new Color(0.95f, 0.58f, 0.32f),
            new Color(0.84f, 0.70f, 0.34f),
            new Color(0.48f, 0.72f, 0.50f),
            new Color(0.38f, 0.70f, 0.72f),
            new Color(0.45f, 0.63f, 0.92f),
            new Color(0.70f, 0.56f, 0.88f),
            new Color(0.86f, 0.50f, 0.66f),
            new Color(0.62f, 0.68f, 0.76f)
        };

        // Cached roleplay body style, reused every frame to avoid allocating a fresh GUIStyle per
        // line. Built once from the active font, then refreshed (font size + color) on each use so it
        // still tracks UI-scale changes. Rich text is enabled so the inline <b>/<i>/<color> tags from
        // DiaryTextFormat render instead of printing literally. Shared by the measure and draw passes.
        private static GUIStyle bodyStyle;
        private static readonly Dictionary<string, float> EntryFirstSeenSeconds = new Dictionary<string, float>();
        // Upper bound on the first-seen fade cache. It keys on eventId|role|status (and title), so a
        // long session would otherwise grow it without limit. When exceeded we clear it wholesale;
        // the only visible effect is that currently-shown cards fade in once more, which is rare (it
        // takes hundreds of distinct entry states to trigger) and harmless.
        private const int MaxFirstSeenEntries = 512;
        // Old saves have only the display date, not an absolute tick. Use this sentinel when a
        // date cannot be grouped into a game year; such entries stay reachable on an "undated" page.
        private const int UnknownYear = int.MinValue;

        // Unity scroll position; persists across frames so the user's scroll offset isn't lost on redraw.
        private Vector2 scrollPosition;
        // The Diary tab pages history by in-game year so one enormous save cannot create one
        // enormous Unity scroll view. Stored per tab instance, and reset when the selected pawn changes.
        private string yearFilterPawnId;
        private int selectedYear = UnknownYear;
        // Manual expand/collapse choices are session UI state. The default is "newest 15 open,
        // older pages collapsed", but a player's click on a specific card wins until the tab dies.
        private readonly Dictionary<string, bool> entryExpansionOverrides = new Dictionary<string, bool>();
        private readonly Dictionary<string, float> entryExpansionBlend = new Dictionary<string, float>();
        private float lastExpansionAnimationSeconds;
        // Set by the Social-tab click patch before opening this tab. FillTab consumes it once
        // the relevant pawn's generated entry list is available.
        private static string pendingScrollPawnId;
        private static string pendingScrollEventId;

        public ITab_Pawn_Diary()
        {
            size = new Vector2(720f, 650f);
            labelKey = "PawnDiaryTabLabel";
        }

        /// <summary>
        /// Requests that the next Diary tab draw for this pawn scroll to the given event card.
        /// Used by the Social-tab play-log click patch.
        /// </summary>
        public static void RequestScrollToEntry(Pawn pawn, string eventId)
        {
            pendingScrollPawnId = pawn?.GetUniqueLoadID();
            pendingScrollEventId = eventId;
        }

        /// <summary>
        /// Clears a pending scroll request when the tab could not be opened after all.
        /// </summary>
        public static void ClearPendingScrollRequest()
        {
            pendingScrollPawnId = null;
            pendingScrollEventId = null;
        }

        /// <summary>
        /// Only show the diary tab for colonist pawns (or corpses of colonists).
        /// </summary>
        public override bool IsVisible
        {
            get { return PawnToShow() != null; }
        }

        /// <summary>
        /// Resolves the pawn to display a diary for, handling both selected colonists
        /// and selected corpses of colonists.
        /// </summary>
        private Pawn PawnToShow()
        {
            Pawn pawn = SelPawn;
            if (pawn == null)
            {
                Corpse corpse = SelThing as Corpse;
                pawn = corpse?.InnerPawn;
            }

            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike || !pawn.IsColonist)
            {
                return null;
            }

            return pawn;
        }

        /// <summary>
        /// RimWorld's immediate-mode draw callback for the entire tab content.
        /// Called every frame; the whole UI is rebuilt from scratch each time.
        /// </summary>
        protected override void FillTab()
        {
            Pawn pawn = PawnToShow();
            if (pawn == null)
            {
                return;
            }

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            // Singleton component that owns all diary state for the current game.
            DiaryGameComponent component = DiaryGameComponent.Current;

            IReadOnlyList<DiaryEntryView> entries = component?.EntriesFor(pawn) ?? EmptyList;
            int generatingCount = entries.Count(IsGenerating);
            bool showLlmDebugInfo = ShouldShowLlmDebugInfo();
            // Dev-mode-only: when on, reveal in-progress/stuck entries in the list (the full debug
            // toggle already shows them, so this only matters when debug info is off).
            bool showGeneratingEntries = ShouldShowGeneratingEntries();

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 34f);
            if (generatingCount > 0)
            {
                Rect statusRect = new Rect(
                    rect.xMax - StatusBadgeRightPadding - StatusBadgeWidth,
                    rect.y + 3f,
                    StatusBadgeWidth,
                    StatusBadgeHeight);
                headerRect.width = Mathf.Max(0f, statusRect.x - rect.x - 8f);
                DrawWritingIndicator(statusRect);
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "PawnDiary.Tab.DiaryHeader".Translate(pawn.LabelShortCap));
            Text.Font = GameFont.Small;

            // In normal play the header stands alone; the dev-only controls (and the space they
            // need) appear only when RimWorld dev mode is on. PawnControlsHeight() returns 0 outside
            // dev mode, so entries sit directly under the header.
            float controlsY = rect.y + 36f;
            float controlsHeight = PawnControlsHeight();
            if (Prefs.DevMode)
            {
                Rect controlsRect = new Rect(rect.x, controlsY, rect.width, controlsHeight);
                DrawPawnControls(pawn, component, controlsRect);
            }

            // Production view: show only completed LLM output. Dev mode can reveal raw/pending
            // rows plus the diagnostic prompt/status block for troubleshooting.
            List<DiaryEntryView> visibleEntries = entries
                .Where(entry => showLlmDebugInfo || IsGenerated(entry) || (showGeneratingEntries && IsGenerating(entry)))
                .ToList();

            // The controls and optional year pager are part of the diary tab, so reserve their
            // space before the scroll view. The year pager is intentionally based on visible entries
            // only: production view pages finished diary text, while dev mode can page raw/pending
            // troubleshooting rows.
            float entriesY = controlsY + controlsHeight + EntryGap;
            Rect outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);

            if (visibleEntries.Count == 0)
            {
                Widgets.Label(outRect, (showLlmDebugInfo ? "PawnDiary.Tab.NoEntries" : "PawnDiary.Tab.NoGeneratedEntries").Translate());
                return;
            }

            List<int> years = YearsFor(visibleEntries);
            EnsureSelectedYear(pawn, years);
            SelectYearForPendingScroll(pawn, visibleEntries);

            if (years.Count > 1)
            {
                Rect yearRect = new Rect(rect.x, entriesY, rect.width, YearFilterHeight);
                DrawYearFilter(yearRect, years, CountEntriesForYear(visibleEntries, selectedYear));
                entriesY = yearRect.yMax + YearFilterGap;
                outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);
            }

            List<DiaryEntryView> ordered = visibleEntries
                .Where(entry => EntryYear(entry) == selectedYear)
                .OrderByDescending(entry => entry.Tick)
                .ToList();

            if (ordered.Count == 0)
            {
                Widgets.Label(outRect, (showLlmDebugInfo ? "PawnDiary.Tab.NoEntries" : "PawnDiary.Tab.NoGeneratedEntries").Translate());
                return;
            }

            // Subtract 16f to leave room for the scrollbar grip inside the scroll view.
            float viewWidth = outRect.width - 16f;
            // Measure each card once per frame and reuse the result for the scroll-height sum, the
            // pending-scroll jump, and the draw loop. Wrapping height is otherwise recomputed two or
            // three times per entry every frame.
            float animationDelta = ExpansionAnimationDelta();
            bool[] expandedTargets = new bool[ordered.Count];
            float[] expansionBlends = new float[ordered.Count];
            float[] fullHeights = new float[ordered.Count];
            float[] heights = new float[ordered.Count];
            float viewHeight = 12f; // includes 12f bottom padding
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                string entryKey = EntryKey(entry);
                bool expanded = IsEntryExpanded(entry, i);
                float expansionBlend = ExpansionBlendFor(entryKey, expanded, animationDelta);
                // Fully collapsed cards only need header height, so avoid expensive wrapped-text
                // measurement until they are expanding or open.
                float fullHeight = (expanded || expansionBlend > 0f)
                    ? EntryHeight(entry, viewWidth, showLlmDebugInfo)
                    : CollapsedEntryHeight;

                expandedTargets[i] = expanded;
                expansionBlends[i] = expansionBlend;
                fullHeights[i] = fullHeight;
                heights[i] = AnimatedEntryHeight(fullHeight, expansionBlend);
                viewHeight += heights[i];
                if (i < ordered.Count - 1)
                {
                    viewHeight += EntryGap;
                }
            }
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            TryApplyPendingScroll(pawn, ordered, heights, viewHeight, outRect.height);
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, viewHeight - outRect.height));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            Color dialogueColor = PreferredDialogueColor(pawn);
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                bool expanded = expandedTargets[i];
                float expansionBlend = expansionBlends[i];
                float fullHeight = fullHeights[i];
                float height = heights[i];
                Rect entryRect = new Rect(0f, curY, viewRect.width, height);

                Color accentColor = EntryAccentColor(entry);
                GUI.BeginGroup(entryRect);
                Rect localEntryRect = new Rect(0f, 0f, entryRect.width, fullHeight);
                Rect visibleEntryRect = new Rect(0f, 0f, entryRect.width, height);
                // Keep the full-card chrome while a row is still animating. Swapping to the compact
                // renderer early made the border/header framing appear to jump near the closed state.
                bool compactCollapsed = !expanded && expansionBlend <= 0f;
                if (compactCollapsed)
                {
                    DrawCollapsedEntry(entry, visibleEntryRect, accentColor, expanded, expansionBlend);
                    if (Widgets.ButtonInvisible(visibleEntryRect, false))
                    {
                        SetEntryExpanded(entry, true);
                    }

                    GUI.EndGroup();
                    curY += height + EntryGap;
                    continue;
                }

                Widgets.DrawMenuSection(localEntryRect);
                // Faint warm "page" wash behind the body text, drawn under the hover highlight so
                // mouseover still reads. Starts below the title bar and inside the accent strip.
                Rect pageRect = new Rect(
                    localEntryRect.x + EntryAccentWidth + 2f,
                    localEntryRect.y + EntryTitleHeight,
                    Mathf.Max(0f, localEntryRect.width - EntryAccentWidth - 4f),
                    Mathf.Max(0f, localEntryRect.height - EntryTitleHeight - 2f));
                Widgets.DrawBoxSolid(pageRect, PageTintColor);
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
                    HeaderRuleColor);

                Rect groupRect = GroupLabelRect(titleRect, entry.GroupLabel);
                if (groupRect.width > 0f)
                {
                    DrawGroupLabel(groupRect, entry.GroupLabel, accentColor);
                }
                float headerRight = groupRect.width > 0f ? groupRect.x - 6f : localEntryRect.xMax - 8f;
                DrawEntryHeader(
                    new Rect(localEntryRect.x + 34f, localEntryRect.y + 5f, Mathf.Max(80f, headerRight - localEntryRect.x - 34f), 22f),
                    entry,
                    accentColor);
                DrawExpansionIndicator(titleRect, expanded, expansionBlend, accentColor);
                if (Widgets.ButtonInvisible(titleRect, false))
                {
                    SetEntryExpanded(entry, !expanded);
                }

                TooltipHandler.TipRegion(titleRect, "PawnDiary.Tab.ExpandCollapseTip".Translate());

                // Linked entry for the OTHER pawn rendered BEFORE main text when this pawn is the
                // recipient (shows the initiator's perspective first). When this pawn is the
                // initiator, the linked recipient entry goes AFTER the main text instead.
                float textY = localEntryRect.y + EntryTextTop;
                LinkedEntryView linked = entry.LinkedEntry;
                bool linkedBefore = linked != null && DiaryEvent.RoleEquals(entry.PovRole, DiaryEvent.RecipientRole);
                bool linkedAfter = linked != null && !linkedBefore;
                bool showModelName = HasModelName(entry);
                string bodyText = EntryBodyText(entry, showLlmDebugInfo);
                string debugText = showLlmDebugInfo ? entry.DebugText : string.Empty;
                float innerTextWidth = localEntryRect.width - 20f;
                float mainTextHeight = RoleplayTextHeight(bodyText, innerTextWidth);
                float debugTextHeight = DebugTextHeight(debugText, innerTextWidth);

                if (linkedBefore)
                {
                    Rect linkedRect = new Rect(localEntryRect.x + 10f, textY, localEntryRect.width - 20f, LinkedEntryTotalHeight);
                    DrawLinkedEntry(linked, linkedRect, pawn);
                    textY = linkedRect.yMax + LinkedEntryPadding;
                }

                Rect textRect = new Rect(localEntryRect.x + 12f, textY, localEntryRect.width - 20f, mainTextHeight);
                if (IsGenerating(entry))
                {
                    DrawWritingPlaceholder(textRect);
                }
                else
                {
                    DrawRoleplayText(textRect, bodyText, dialogueColor, EntryTextAlpha(entry) * BodyExpansionAlpha(expansionBlend));
                }
                float afterTextY = textRect.yMax;

                if (linkedAfter)
                {
                    Rect linkedRect = new Rect(localEntryRect.x + 10f, afterTextY + LinkedEntryPadding, localEntryRect.width - 20f, LinkedEntryTotalHeight);
                    DrawLinkedEntry(linked, linkedRect, pawn);
                    afterTextY = linkedRect.yMax;
                }

                if (debugTextHeight > 0f)
                {
                    Rect debugRect = new Rect(localEntryRect.x + 12f, afterTextY + DebugTextTopPadding, localEntryRect.width - 20f, debugTextHeight);
                    DrawDebugText(debugRect, debugText);
                }

                if (showModelName)
                {
                    Rect modelRect = new Rect(localEntryRect.x + 12f, localEntryRect.yMax - EntryBottomPadding - ModelNameHeight, localEntryRect.width - 24f, ModelNameHeight);
                    DrawModelName(modelRect, entry.LlmModel);
                }

                GUI.EndGroup();
                curY += height + EntryGap;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws a closed diary page as a deliberate compact row: one bordered header, one accent
        /// strip, and no body tint/rule. This keeps collapsed histories readable instead of looking
        /// like clipped full cards.
        /// </summary>
        private static void DrawCollapsedEntry(DiaryEntryView entry, Rect rect, Color accent, bool expanded, float expansionBlend)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.DrawTitleBG(rect);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect accentRect = new Rect(rect.x + 1f, rect.y + 1f, EntryAccentWidth, rect.height - 2f);
            Widgets.DrawBoxSolid(accentRect, accent);
            Widgets.DrawBoxSolid(new Rect(accentRect.xMax, accentRect.y, 1f, accentRect.height), AccentHighlightColor);

            Rect groupRect = GroupLabelRect(rect, entry?.GroupLabel);
            if (groupRect.width > 0f)
            {
                groupRect.y = rect.y + (rect.height - groupRect.height) * 0.5f;
                DrawGroupLabel(groupRect, entry.GroupLabel, accent);
            }

            float headerRight = groupRect.width > 0f ? groupRect.x - 6f : rect.xMax - 8f;
            Rect headerRect = new Rect(
                rect.x + 34f,
                rect.y + (rect.height - 22f) * 0.5f,
                Mathf.Max(80f, headerRight - rect.x - 34f),
                22f);
            DrawEntryHeader(headerRect, entry, accent);
            DrawExpansionIndicator(rect, expanded, expansionBlend, accent);

            TooltipHandler.TipRegion(rect, "PawnDiary.Tab.ExpandCollapseTip".Translate());
        }

        /// <summary>
        /// Returns the per-entry key used for session expand/collapse state. Event id plus POV role
        /// is stable for saved entries; the fallback keeps damaged entries clickable without throwing.
        /// </summary>
        private static string EntryKey(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string eventPart = string.IsNullOrWhiteSpace(entry.EventId)
                ? ((entry.Date ?? string.Empty) + "|" + entry.Tick)
                : entry.EventId;
            return eventPart + "|" + (entry.PovRole ?? string.Empty);
        }

        /// <summary>
        /// Default policy: keep the newest visible pages open, collapse the rest. A manual click on
        /// a specific entry stores an override and wins over this automatic rule.
        /// </summary>
        private bool IsEntryExpanded(DiaryEntryView entry, int orderedIndex)
        {
            bool expanded;
            if (entryExpansionOverrides.TryGetValue(EntryKey(entry), out expanded))
            {
                return expanded;
            }

            return orderedIndex < AutoExpandedEntryCount;
        }

        /// <summary>
        /// Stores a manual expansion choice and keeps the current animation position if one exists.
        /// </summary>
        private void SetEntryExpanded(DiaryEntryView entry, bool expanded)
        {
            string key = EntryKey(entry);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            entryExpansionOverrides[key] = expanded;
        }

        /// <summary>
        /// Frame delta for the cheap expand/collapse animation. Capped so alt-tabbing or a long hitch
        /// does not make every visible card jump through a huge simulated time step.
        /// </summary>
        private float ExpansionAnimationDelta()
        {
            float now = Time.realtimeSinceStartup;
            if (lastExpansionAnimationSeconds <= 0f)
            {
                lastExpansionAnimationSeconds = now;
                return 0f;
            }

            float delta = Mathf.Clamp(now - lastExpansionAnimationSeconds, 0f, 0.05f);
            lastExpansionAnimationSeconds = now;
            return delta;
        }

        /// <summary>
        /// Moves one entry's cached animation blend toward its target. New entries start at the
        /// target state so opening a tab does not animate every old page at once.
        /// </summary>
        private float ExpansionBlendFor(string entryKey, bool expanded, float delta)
        {
            if (string.IsNullOrEmpty(entryKey))
            {
                return expanded ? 1f : 0f;
            }

            float target = expanded ? 1f : 0f;
            float current;
            if (!entryExpansionBlend.TryGetValue(entryKey, out current))
            {
                current = target;
            }
            else if (delta > 0f)
            {
                current = Mathf.MoveTowards(current, target, delta * ExpansionAnimationSpeed);
            }

            entryExpansionBlend[entryKey] = current;
            return current;
        }

        /// <summary>
        /// Converts the raw 0..1 blend into a smoother ease curve for height and text alpha.
        /// </summary>
        private static float SmoothExpansionBlend(float blend)
        {
            blend = Mathf.Clamp01(blend);
            return blend * blend * (3f - 2f * blend);
        }

        /// <summary>
        /// Height used by the scroll view for this frame.
        /// </summary>
        private static float AnimatedEntryHeight(float fullHeight, float expansionBlend)
        {
            return Mathf.Lerp(CollapsedEntryHeight, fullHeight, SmoothExpansionBlend(expansionBlend));
        }

        /// <summary>
        /// Extra fade for body prose during expand/collapse. The title stays fully readable.
        /// </summary>
        private static float BodyExpansionAlpha(float expansionBlend)
        {
            return Mathf.Clamp01((SmoothExpansionBlend(expansionBlend) - 0.12f) / 0.88f);
        }

        /// <summary>
        /// Draws the small plus/minus affordance in the card header. The expanding height is the
        /// actual animation; this indicator simply gives the click target a familiar hint.
        /// </summary>
        private static void DrawExpansionIndicator(Rect titleRect, bool expanded, float expansionBlend, Color accent)
        {
            Rect indicatorRect = new Rect(titleRect.x + 8f, titleRect.y + (titleRect.height - 20f) * 0.5f, 18f, 20f);
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            float glow = Mathf.Lerp(0.42f, 0.75f, SmoothExpansionBlend(expansionBlend));
            GUI.color = Color.Lerp(new Color(0.62f, 0.65f, 0.68f, 0.85f), accent, glow);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(indicatorRect, expanded ? "-" : "+");

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws the one-year-at-a-time pager above the diary list. The buttons move through years
        /// that actually have visible entries, newest first; all entries for the selected year are
        /// shown in the scroll view below.
        /// </summary>
        private void DrawYearFilter(Rect rect, List<int> years, int entryCount)
        {
            if (years == null || years.Count <= 1)
            {
                return;
            }

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);
            int index = years.IndexOf(selectedYear);
            if (index < 0)
            {
                index = 0;
                selectedYear = years[0];
            }

            Rect newerRect = new Rect(inner.x, inner.y, YearButtonWidth, inner.height);
            Rect olderRect = new Rect(inner.xMax - YearButtonWidth, inner.y, YearButtonWidth, inner.height);
            Rect labelRect = new Rect(newerRect.xMax + 8f, inner.y, olderRect.x - newerRect.xMax - 16f, inner.height);

            if (DrawYearButton(newerRect, "PawnDiary.Tab.NewerYear", index > 0))
            {
                SelectYear(years[index - 1]);
            }

            if (DrawYearButton(olderRect, "PawnDiary.Tab.OlderYear", index < years.Count - 1))
            {
                SelectYear(years[index + 1]);
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.86f, 0.86f, 0.80f);
            Widgets.LabelFit(labelRect, "PawnDiary.Tab.YearFilter".Translate(YearLabel(selectedYear), entryCount));
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws a pager button with a disabled visual state while keeping all text localizable.
        /// </summary>
        private static bool DrawYearButton(Rect rect, string labelKey, bool enabled)
        {
            bool oldEnabled = GUI.enabled;
            Color oldColor = GUI.color;
            GUI.enabled = enabled;
            if (!enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.42f);
            }

            bool clicked = Widgets.ButtonText(rect, labelKey.Translate());
            GUI.enabled = oldEnabled;
            GUI.color = oldColor;
            return enabled && clicked;
        }

        /// <summary>
        /// Converts a year key into the short label shown by the pager.
        /// </summary>
        private static string YearLabel(int year)
        {
            if (year == UnknownYear)
            {
                return "PawnDiary.Tab.UnknownYear".Translate().ToString();
            }

            return "PawnDiary.Tab.YearLabel".Translate(year).ToString();
        }

        /// <summary>
        /// Changes the visible diary year and returns the scroll position to the top of that year.
        /// </summary>
        private void SelectYear(int year)
        {
            selectedYear = year;
            scrollPosition.y = 0f;
        }

        /// <summary>
        /// Keeps the selected year valid for the current pawn. New pawns open on their newest
        /// available year, which is the first item because <see cref="YearsFor"/> sorts descending.
        /// </summary>
        private void EnsureSelectedYear(Pawn pawn, List<int> years)
        {
            if (years == null || years.Count == 0)
            {
                selectedYear = UnknownYear;
                yearFilterPawnId = null;
                scrollPosition.y = 0f;
                return;
            }

            string pawnId = pawn?.GetUniqueLoadID();
            if (yearFilterPawnId != pawnId || !years.Contains(selectedYear))
            {
                yearFilterPawnId = pawnId;
                SelectYear(years[0]);
            }
        }

        /// <summary>
        /// A Social-tab or linked-entry jump may target an older year. Switch the pager first so
        /// TryApplyPendingScroll can find the event in the filtered list and place it on screen.
        /// </summary>
        private void SelectYearForPendingScroll(Pawn pawn, List<DiaryEntryView> visibleEntries)
        {
            if (pawn == null || visibleEntries == null || string.IsNullOrWhiteSpace(pendingScrollPawnId) || string.IsNullOrWhiteSpace(pendingScrollEventId))
            {
                return;
            }

            if (pawn.GetUniqueLoadID() != pendingScrollPawnId)
            {
                return;
            }

            for (int i = 0; i < visibleEntries.Count; i++)
            {
                DiaryEntryView entry = visibleEntries[i];
                if (entry != null && entry.EventId == pendingScrollEventId)
                {
                    selectedYear = EntryYear(entry);
                    yearFilterPawnId = pendingScrollPawnId;
                    SetEntryExpanded(entry, true);
                    return;
                }
            }
        }

        /// <summary>
        /// Returns the distinct in-game years represented by the entries, newest first. Entries with
        /// a missing/malformed display date use an "undated" page so old saves remain reachable
        /// instead of disappearing.
        /// </summary>
        private static List<int> YearsFor(List<DiaryEntryView> entries)
        {
            List<int> years = new List<int>();
            if (entries == null)
            {
                years.Add(UnknownYear);
                return years;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                int year = EntryYear(entries[i]);
                if (!years.Contains(year))
                {
                    years.Add(year);
                }
            }

            if (years.Count == 0)
            {
                years.Add(UnknownYear);
            }

            years.Sort((left, right) => right.CompareTo(left));
            return years;
        }

        /// <summary>
        /// Counts the entries that will appear on a year page for the pager label.
        /// </summary>
        private static int CountEntriesForYear(List<DiaryEntryView> entries, int year)
        {
            if (entries == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (EntryYear(entries[i]) == year)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Extracts the game year from the saved display date. Existing saves store game ticks
        /// (`TicksGame`) plus this formatted date, not absolute ticks, so the date is the stable
        /// old-save-compatible source for year paging.
        /// </summary>
        private static int EntryYear(DiaryEntryView entry)
        {
            return ExtractYear(entry?.Date);
        }

        /// <summary>
        /// Finds the final run of digits in RimWorld's full date string (for example the 5503 in
        /// "1st of Aprimay, 5503"). Reading from the end keeps this tolerant of localized month names
        /// and day ordinals earlier in the string.
        /// </summary>
        private static int ExtractYear(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return UnknownYear;
            }

            int end = -1;
            for (int i = date.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(date[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                return UnknownYear;
            }

            int start = end;
            while (start > 0 && char.IsDigit(date[start - 1]))
            {
                start--;
            }

            string yearText = date.Substring(start, end - start + 1);
            int year;
            if (int.TryParse(yearText, out year))
            {
                return year;
            }

            return UnknownYear;
        }

        /// <summary>
        /// Consumes a pending Social-tab jump request by placing the target card near the top
        /// of the scroll view. If the entry disappeared before redraw, clear the stale request.
        /// </summary>
        private void TryApplyPendingScroll(Pawn pawn, List<DiaryEntryView> ordered, float[] heights, float viewHeight, float outHeight)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(pendingScrollPawnId) || string.IsNullOrWhiteSpace(pendingScrollEventId))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (pawnId != pendingScrollPawnId)
            {
                return;
            }

            float curY = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                if (entry != null && entry.EventId == pendingScrollEventId)
                {
                    float maxScroll = Mathf.Max(0f, viewHeight - outHeight);
                    scrollPosition.y = Mathf.Clamp(curY, 0f, maxScroll);
                    ClearPendingScrollRequest();
                    return;
                }

                curY += heights[i] + EntryGap;
            }

            ClearPendingScrollRequest();
        }

        /// <summary>
        /// Returns the height needed for the per-pawn controls above the diary list.
        /// Dev-only rows are omitted in normal play, keeping the tab focused on entries.
        /// </summary>
        private static float PawnControlsHeight()
        {
            if (!Prefs.DevMode)
            {
                return 0f;
            }

            float lines = 2f; // generation toggle + dev-only mock-history filler
            if (PawnDiaryMod.Settings != null)
            {
                lines += 3f; // dev toggles: persona controls, LLM diagnostics, show generating
                if (ShouldShowPersonaSettings())
                {
                    lines += 1f; // persona picker
                }
            }

            return lines * ControlLineHeight + (lines - 1f) * ControlGap;
        }

        /// <summary>
        /// Renders the per-pawn generation toggle plus dev-mode-only troubleshooting controls.
        /// </summary>
        private static void DrawPawnControls(Pawn pawn, DiaryGameComponent component, Rect rect)
        {
            if (pawn == null || component == null)
            {
                return;
            }

            if (!Prefs.DevMode)
            {
                return;
            }

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            // Toggling this only gates future LLM requests. Recorded events remain visible as raw
            // diary entries, which lets players pause generation without losing history.
            bool enabled = component.DiaryGenerationEnabledFor(pawn);
            bool before = enabled;
            listing.CheckboxLabeled(
                "PawnDiary.Tab.GenerateForPawn".Translate(),
                ref enabled,
                "PawnDiary.Tab.GenerateForPawnTip".Translate());
            if (enabled != before)
            {
                component.SetDiaryGenerationEnabled(pawn, enabled);
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool writeGlobalSettings = false;
            if (Prefs.DevMode && settings != null)
            {
                bool showPersonaSettings = settings.showPersonaSettings;
                bool showPersonaBefore = showPersonaSettings;
                listing.CheckboxLabeled(
                    "PawnDiary.Tab.ShowPersonaSettings".Translate(),
                    ref showPersonaSettings,
                    "PawnDiary.Tab.ShowPersonaSettingsTip".Translate());
                if (showPersonaSettings != showPersonaBefore)
                {
                    settings.showPersonaSettings = showPersonaSettings;
                    writeGlobalSettings = true;
                }

                bool showLlmDebugInfo = settings.showLlmDebugInfo;
                bool showDebugBefore = showLlmDebugInfo;
                listing.CheckboxLabeled(
                    "PawnDiary.Tab.ShowLlmDebugInfo".Translate(),
                    ref showLlmDebugInfo,
                    "PawnDiary.Tab.ShowLlmDebugInfoTip".Translate());
                if (showLlmDebugInfo != showDebugBefore)
                {
                    settings.showLlmDebugInfo = showLlmDebugInfo;
                    writeGlobalSettings = true;
                }

                bool showGeneratingEntries = settings.showGeneratingEntries;
                bool showGeneratingBefore = showGeneratingEntries;
                listing.CheckboxLabeled(
                    "PawnDiary.Tab.ShowGeneratingEntries".Translate(),
                    ref showGeneratingEntries,
                    "PawnDiary.Tab.ShowGeneratingEntriesTip".Translate());
                if (showGeneratingEntries != showGeneratingBefore)
                {
                    settings.showGeneratingEntries = showGeneratingEntries;
                    writeGlobalSettings = true;
                }
            }

            Rect mockButtonRect = listing.GetRect(ControlLineHeight);
            if (Widgets.ButtonText(mockButtonRect, "PawnDiary.Tab.FillMockEntries".Translate(DevMockDiaryTargetCount)))
            {
                int added = component.FillMockDiaryEntriesForDev(pawn, DevMockDiaryTargetCount);
                if (added > 0)
                {
                    Messages.Message(
                        "PawnDiary.Tab.MockEntriesAdded".Translate(added, pawn.LabelShortCap),
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
                else
                {
                    Messages.Message(
                        "PawnDiary.Tab.MockEntriesAlreadyFilled".Translate(DevMockDiaryTargetCount, pawn.LabelShortCap),
                        MessageTypeDefOf.NeutralEvent,
                        false);
                }
            }

            TooltipHandler.TipRegion(
                mockButtonRect,
                "PawnDiary.Tab.FillMockEntriesTip".Translate(DevMockDiaryTargetCount));

            // Persona picker. Options come from DiaryPersonaDefs.xml so new presets can be added
            // without touching UI code; the choice is saved per pawn and used for future generations.
            if (ShouldShowPersonaSettings())
            {
                DiaryPersonaDef persona = component.PersonaFor(pawn);
                if (listing.ButtonText("PawnDiary.Tab.PersonaButton".Translate(PersonaLabel(persona))))
                {
                    List<FloatMenuOption> options = DiaryPersonas.All
                        .OrderBy(PersonaLabel)
                        .Select(option =>
                        {
                            DiaryPersonaDef selected = option;
                            return new FloatMenuOption(PersonaLabel(selected), delegate
                            {
                                component.SetPersona(pawn, selected.defName);
                            });
                        })
                        .ToList();

                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            listing.End();

            if (writeGlobalSettings)
            {
                WriteGlobalSettings();
            }
        }

        /// <summary>
        /// Returns the human-readable label for a persona, falling back to "default" if null
        /// or to defName if the label is blank.
        /// </summary>
        private static string PersonaLabel(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate();
            }

            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;
        }

        /// <summary>
        /// Draws compact animated dots while hidden pending entries are being written.
        /// </summary>
        private static void DrawWritingIndicator(Rect rect)
        {
            DrawWritingDots(
                new Rect(rect.x + rect.width * 0.5f - 10f, rect.y + rect.height * 0.5f - 2f, 24f, 8f),
                new Color(0.78f, 0.95f, 0.78f),
                0f);
        }

        /// <summary>
        /// True when an entry has actual LLM output ready for the production diary list.
        /// </summary>
        private static bool IsGenerated(DiaryEntryView entry)
        {
            return entry != null && !string.IsNullOrWhiteSpace(entry.GeneratedText);
        }

        /// <summary>
        /// Dev-mode preference gate for manual persona editing in the Diary tab.
        /// </summary>
        private static bool ShouldShowPersonaSettings()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showPersonaSettings;
        }

        /// <summary>
        /// Dev-mode preference gate for raw/pending entries and the LLM prompt/status block.
        /// </summary>
        private static bool ShouldShowLlmDebugInfo()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showLlmDebugInfo;
        }

        /// <summary>
        /// Dev-mode preference gate for revealing entries still in the LLM generation pipeline
        /// (in-progress or stuck), without the full prompt/status diagnostic block.
        /// </summary>
        private static bool ShouldShowGeneratingEntries()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showGeneratingEntries;
        }

        /// <summary>
        /// Persists global mod UI preferences changed from this pawn tab.
        /// </summary>
        private static void WriteGlobalSettings()
        {
            PawnDiaryMod mod = LoadedModManager.GetMod<PawnDiaryMod>();
            if (mod != null)
            {
                mod.WriteSettings();
            }
        }

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

        /// <summary>
        /// True while an entry is still waiting on the background LLM generation pipeline.
        /// </summary>
        private static bool IsGenerating(DiaryEntryView entry)
        {
            return entry != null && entry.LlmStatus == DiaryEvent.PendingStatus;
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
            return entry != null && !string.IsNullOrWhiteSpace(entry.LlmModel);
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

            if (!TitlesEnabled() || string.IsNullOrWhiteSpace(entry.Title))
            {
                return entry.Date ?? string.Empty;
            }

            return (entry.Date ?? string.Empty) + " \u2014 " + entry.Title;
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

            bool hasTitle = entry != null && TitlesEnabled() && !string.IsNullOrWhiteSpace(entry.Title);
            Color oldColor = GUI.color;
            if (hasTitle)
            {
                float age = Time.realtimeSinceStartup - TitleFirstSeenAt(entry);
                float alpha = Mathf.Clamp01(age / TitleFadeDurationSeconds);
                float pulse = Mathf.Lerp(0.22f, 0.38f, WritingPulse(1.4f));
                Color titleColor = Color.Lerp(new Color(0.88f, 0.86f, 0.79f), accent, pulse);
                titleColor.a = alpha;
                GUI.color = titleColor;
            }
            else
            {
                GUI.color = new Color(0.88f, 0.86f, 0.79f);
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

            const float dotsWidth = WritingDotSize * 3f + WritingDotGap * 2f;
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
                    GUI.color = new Color(0.86f, 0.86f, 0.86f, 0.95f);
                    Widgets.LabelFit(prefixRect, prefix);
                    dotsX = prefixRect.xMax;
                }
            }

            if (dotsX + dotsWidth > rect.xMax)
            {
                dotsX = Mathf.Max(rect.x, rect.xMax - dotsWidth);
            }

            Color dotColor = Color.Lerp(new Color(0.68f, 0.72f, 0.76f), accent, 0.45f);
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
        /// Returns the color strip used to mark the entry group. Important groups keep the palette
        /// color bright; quieter groups use a softer version so the diary stays calm at a glance.
        /// </summary>
        private static Color EntryAccentColor(DiaryEntryView entry)
        {
            Color accent = PaletteColor(entry?.GroupLabel);
            if (entry == null || entry.Important)
            {
                return accent;
            }

            return Color.Lerp(QuietColor, accent, 0.42f);
        }

        /// <summary>
        /// Picks a stable palette color from a localized group label without relying on runtime
        /// string hash behavior. New to hashing? It turns text into a repeatable number.
        /// </summary>
        private static Color PaletteColor(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return EntryAccentPalette[EntryAccentPalette.Length - 1];
            }

            int hash = 17;
            for (int i = 0; i < label.Length; i++)
            {
                hash = hash * 31 + char.ToLowerInvariant(label[i]);
            }

            if (hash < 0)
            {
                hash = ~hash;
            }

            return EntryAccentPalette[hash % EntryAccentPalette.Length];
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
            return (Mathf.Sin(Time.realtimeSinceStartup * 5.5f + phaseOffset) + 1f) * 0.5f;
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
        /// Draws the model id as a tiny, low-contrast note at the end of a diary card.
        /// </summary>
        private static void DrawModelName(Rect rect, string modelName)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.45f, 0.48f, 0.50f, 0.62f);
            Widgets.LabelFit(rect, modelName);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
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
            GUI.color = new Color(0.54f, 0.58f, 0.60f, 0.90f);
            Widgets.Label(rect, debugText);

            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws generated text as light roleplay prose. Each line is formatted to rich text by
        /// <see cref="DiaryTextFormat"/> so markdown emphasis renders and quoted speech is colored
        /// inline with the pawn's dialogue color, while the surrounding narration stays muted prose.
        /// The fade-in alpha is applied through GUI.color so inline-colored spans fade with the rest.
        /// </summary>
        private static void DrawRoleplayText(Rect rect, string text, Color dialogueColor, float alpha)
        {
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            // GUI.color multiplies with both the style color and any inline <color> spans, so a single
            // alpha here fades the whole page uniformly during the first-seen reveal.
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

            GUIStyle style = BodyStyle();
            float curY = rect.y;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    curY += RoleplayParagraphGap;
                    continue;
                }

                string rich = DiaryTextFormat.ToRichText(line, dialogueColor);
                float height = style.CalcHeight(new GUIContent(rich), rect.width);
                GUI.Label(new Rect(rect.x, curY, rect.width, height), rich, style);
                curY += height + RoleplayLineGap;
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
                ? EntryAccentPalette[0] : EntryAccentPalette[4];
            Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, 4f, rect.height - 2f), stripColor);

            // Label line: "Alice's perspective (initiator):"
            string roleLabel = DiaryEvent.RoleEquals(link.OtherRole, DiaryEvent.InitiatorRole)
                ? "PawnDiary.Tab.LinkedInitiator".Translate(link.OtherPawnName)
                : "PawnDiary.Tab.LinkedRecipient".Translate(link.OtherPawnName);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.80f, 0.85f, 0.92f);
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
        private static float EntryHeight(DiaryEntryView entry, float width, bool showLlmDebugInfo)
        {
            // Must match the draw width in FillTab (entryRect.width - 20f) so the measured wrap
            // height equals what is actually rendered; a wider measure clips long entries at the bottom.
            float innerWidth = width - 20f;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float textHeight = RoleplayTextHeight(EntryBodyText(entry, showLlmDebugInfo), innerWidth);
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

        /// <summary>
        /// Measures the same roleplay lines that DrawRoleplayText renders. Uses the same rich-text
        /// formatting and body style so the measured wrap height matches what is drawn; the dialogue
        /// color is irrelevant to height (only the bold spans matter, and those are applied here too),
        /// so a fixed fallback color is passed.
        /// </summary>
        private static float RoleplayTextHeight(string text, float width)
        {
            GUIStyle style = BodyStyle();
            float height = 0f;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    height += RoleplayParagraphGap;
                    continue;
                }

                string rich = DiaryTextFormat.ToRichText(line, FallbackDialogueColor);
                height += style.CalcHeight(new GUIContent(rich), width) + RoleplayLineGap;
            }

            return Mathf.Max(Text.LineHeight, height);
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
            bodyStyle.normal.textColor = NarrativeColor;
            return bodyStyle;
        }
    }
}
