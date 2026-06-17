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
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary : ITab
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
        private const float AtmosphereInset = 18f;
        private const float MemorialInset = 34f;

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
        // Legacy accent colors still used by linked-entry role strips. Diary card group colors now
        // come from DiaryEvent.colorCue so localized labels and generated titles never affect them.
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
        private static readonly Color DefaultCueColor = ColoredText.NameColor;
        private static readonly Color WhiteCueColor = new Color(0.92f, 0.92f, 0.86f);
        private static readonly Color QuietCueColor = new Color(0.74f, 0.74f, 0.70f);
        private static readonly Color MentalBreakCueColor = ColoredText.FactionColor_Ally;
        private static readonly Color DazeCueColor = ColoredText.GeneColor;
        private static readonly Color SocialFightCueColor = new Color(1f, 0.52f, 0.16f);
        private static readonly Color ExtremeDarkCueColor = new Color(0.58f, 0.05f, 0.08f);
        private static readonly Color StrangeChatCueColor = new Color(0.42f, 0.96f, 0.50f);

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
        // Same idea for per-card expand/collapse animation blends. The tab object is long-lived and
        // shared across pawns, so stale animation keys should not accumulate forever.
        private const int MaxExpansionBlendEntries = MaxFirstSeenEntries;
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

        private sealed class RoleplayLineBlock
        {
            public string line;
            public float leftInset;
            public float rightInset;
            public float extraTopGap;
            public float extraBottomGap;
            public FontStyle fontStyle = FontStyle.Normal;
            public TextAnchor alignment = TextAnchor.UpperLeft;
            public int seedSalt;
        }

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
                DrawYearFilter(yearRect, years, visibleEntries);
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
                string atmosphereCue = EntryAtmosphereCue(entry);
                int staggeredIntensity = EntryStaggeredIntensity(entry);
                bool distortDirectSpeech = EntryDistortDirectSpeech(entry);
                int roleplaySeed = StableTextSeed(EntryKey(entry));
                float mainTextHeight = RoleplayTextHeight(
                    bodyText,
                    innerTextWidth,
                    atmosphereCue,
                    staggeredIntensity,
                    distortDirectSpeech,
                    roleplaySeed);
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
                    DrawRoleplayText(
                        textRect,
                        bodyText,
                        dialogueColor,
                        EntryTextAlpha(entry) * BodyExpansionAlpha(expansionBlend),
                        atmosphereCue,
                        staggeredIntensity,
                        distortDirectSpeech,
                        roleplaySeed);
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




    }
}
