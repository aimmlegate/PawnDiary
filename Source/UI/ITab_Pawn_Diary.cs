// The hidden inspector tab (UI) that renders the selected pawn's finished diary entries.
// RimWorld calls FillTab() to draw it using immediate-mode GUI (the whole tab is re-emitted each
// frame) after a pawn/corpse command opens it. It reads entries via DiaryGameComponent.EntriesFor.
// See AGENTS.md ("lifecycle").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // Inspector tab that shows the selected pawn's diary. It stays registered with the inspect pane
    // so RimWorld can host it, but the visible entry point is a pawn/corpse command button.
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary : ITab
    {
        // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        // Per-frame cache for the heaviest part of drawing the tab: rebuilding a DiaryEntryView for
        // every one of the pawn's events (each runs group classification + gameContext parsing). The
        // built list is reused until the pawn's render token changes — a new event, or an entry's
        // status/text/title changing — so a tab that is merely being read does no rebuild work.
        private Pawn cachedEntriesPawn;
        private DiaryRenderToken cachedEntriesToken;
        private IReadOnlyList<DiaryEntryView> cachedEntries = EmptyList;

        // The visible subset, year list, per-year ordering, and measurement arrays are derived from
        // cachedEntries. FillTab runs every draw frame, so these buffers avoid per-frame LINQ/List/
        // array churn while the selected pawn and render token stay unchanged.
        private IReadOnlyList<DiaryEntryView> cachedVisibleSource = EmptyList;
        private Pawn cachedVisiblePawn;
        private DiaryRenderToken cachedVisibleToken;
        private bool cachedVisibleShowDebug;
        private bool cachedVisibleShowGenerating;
        private bool cachedVisibleShowPromptOnly;
        private int cachedVisibleRevision;
        private int cachedGeneratingCount;
        private readonly List<DiaryEntryView> cachedVisibleEntries = new List<DiaryEntryView>();
        private readonly List<int> cachedVisibleYears = new List<int>();
        private readonly List<DiaryEntryView> cachedOrderedEntries = new List<DiaryEntryView>();
        private int cachedOrderedVisibleRevision = -1;
        private int cachedOrderedYear = int.MinValue;
        private string[] entryKeysBuffer = new string[0];
        private bool[] expandedTargetsBuffer = new bool[0];
        private float[] expansionBlendsBuffer = new float[0];
        private float[] fullHeightsBuffer = new float[0];
        private float[] heightsBuffer = new float[0];

        // Diary tab presentation values are XML-backed via DiaryUiStyleDef. These accessors keep the
        // drawing code readable while letting modders retune spacing/colors without recompiling.
        private static DiaryUiStyleDef UiStyle => DiaryUiStyles.Current;
        private static float ControlLineHeight => UiStyle.controlLineHeight;
        private static float ControlGap => UiStyle.controlGap;
        private static float EntryTitleHeight => UiStyle.entryTitleHeight;
        private static float EntryTextTop => UiStyle.entryTextTop;
        private static float EntryBottomPadding => UiStyle.entryBottomPadding;
        private static float StatusBadgeWidth => UiStyle.statusBadgeWidth;
        private static float StatusBadgeHeight => UiStyle.statusBadgeHeight;
        private static float StatusBadgeRightPadding => UiStyle.statusBadgeRightPadding;
        private static float RoleplayLineGap => UiStyle.roleplayLineGap;
        private static float RoleplayParagraphGap => UiStyle.roleplayParagraphGap;
        private static float SpeechBlockLeftInset => UiStyle.speechBlockLeftInset;
        private static float SpeechBlockVerticalPadding => UiStyle.speechBlockVerticalPadding;
        private static float EntryGap => UiStyle.entryGap;
        private static int AutoExpandedEntryCount => UiStyle.autoExpandedEntryCount;
        private const int DevMockDiaryTargetCount = 360;
        private static float CollapsedEntryHeight => UiStyle.CollapsedEntryHeight;
        private static float ExpansionAnimationSpeed => UiStyle.expansionAnimationSpeed;
        private static float LinkedEntryPadding => UiStyle.linkedEntryPadding;
        private static float LinkedEntryLabelHeight => UiStyle.linkedEntryLabelHeight;
        private static float LinkedEntryTextHeight => UiStyle.linkedEntryTextHeight;
        private static float LinkedEntryTotalHeight => UiStyle.linkedEntryTotalHeight;
        private static float YearFilterHeight => UiStyle.yearFilterHeight;
        private static float YearFilterGap => UiStyle.yearFilterGap;
        private static float YearButtonWidth => UiStyle.yearButtonWidth;
        private static float ModelNameTopPadding => UiStyle.modelNameTopPadding;
        private static float ModelNameHeight => UiStyle.modelNameHeight;
        private static float DebugTextTopPadding => UiStyle.debugTextTopPadding;
        private static float EntryAccentWidth => UiStyle.entryAccentWidth;
        private static float EntryLabelMaxWidth => UiStyle.entryLabelMaxWidth;
        private static float EntryFadeDurationSeconds => UiStyle.entryFadeDurationSeconds;
        private static float TitleFadeDurationSeconds => UiStyle.titleFadeDurationSeconds;
        private static float WritingDotSize => UiStyle.writingDotSize;
        private static float WritingDotGap => UiStyle.writingDotGap;
        private static float AtmosphereInset => UiStyle.atmosphereInset;
        private static float MemorialInset => UiStyle.memorialInset;
        private static string SpeechBlockOpenMarker => string.IsNullOrWhiteSpace(UiStyle.speechBlockOpenMarker) ? DiaryDirectSpeechParser.DefaultOpenMarker : UiStyle.speechBlockOpenMarker;
        private static string SpeechBlockCloseMarker => string.IsNullOrWhiteSpace(UiStyle.speechBlockCloseMarker) ? DiaryDirectSpeechParser.DefaultCloseMarker : UiStyle.speechBlockCloseMarker;

        private static Color QuietColor => UiStyle.QuietTextColor;
        private static Color NarrativeColor => UiStyle.NarrativeTextColor;
        private static Color FallbackDialogueColor => UiStyle.FallbackDialogueColor;
        private static Color SpeechBlockBgColor => UiStyle.SpeechBlockBgColor;
        private static Color HeaderRuleColor => UiStyle.HeaderRuleColor;
        private static Color AccentHighlightColor => UiStyle.AccentHighlightColor;
        private static Color LinkedEntryBgColor => UiStyle.LinkedEntryBgColor;
        private static Color LinkedEntryBorderColor => UiStyle.LinkedEntryBorderColor;
        private static Color LinkedEntryTextColor => UiStyle.LinkedEntryTextColor;
        private static Color LinkedEntryHoverColor => UiStyle.LinkedEntryHoverColor;

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
            public bool directSpeech;
        }

        public ITab_Pawn_Diary()
        {
            size = new Vector2(UiStyle.tabWidth, UiStyle.tabHeight);
            labelKey = "PawnDiaryTabLabelFixed";
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
        /// Hides the Diary tab button from RimWorld's normal inspector tab strip unless the player
        /// chooses tab access in settings. The tab remains registered either way so command buttons
        /// and Social-log links can open this same UI.
        /// </summary>
        public override bool Hidden
        {
            get { return PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.showDiaryInspectTab; }
        }

        /// <summary>
        /// True when the pawn should have diary UI access, matching the tab's existing visibility rule.
        /// </summary>
        public static bool CanShowDiaryFor(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike && pawn.IsColonist;
        }

        /// <summary>
        /// Updates the inspect-tab label before RimWorld draws the tab strip. Both labels reserve the
        /// same invisible left/right dot slots, so the word stays centered when the new-page dot appears.
        /// </summary>
        internal void RefreshTabLabelStatus()
        {
            string key = "PawnDiaryTabLabelFixed";
            Pawn pawn = PawnToShow();
            DiaryGameComponent component = DiaryGameComponent.Current;
            if (pawn != null && component != null && component.CommandStatusFor(pawn).HasNewPages)
            {
                key = "PawnDiaryTabLabelNew";
            }

            labelKey = key;
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

            if (!CanShowDiaryFor(pawn))
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
            component?.AcknowledgeGeneratedEntriesFor(pawn);

            // Only rebuild the entry views when something actually changed; otherwise the tab would
            // re-classify and re-parse every entry ~60 times a second while it is just being read.
            IReadOnlyList<DiaryEntryView> entries;
            DiaryRenderToken token = default(DiaryRenderToken);
            if (component == null)
            {
                entries = EmptyList;
            }
            else
            {
                token = component.RenderTokenFor(pawn);
                if (pawn != cachedEntriesPawn || !token.Equals(cachedEntriesToken) || cachedEntries == null)
                {
                    cachedEntries = component.EntriesFor(pawn);
                    cachedEntriesPawn = pawn;
                    cachedEntriesToken = token;
                }

                entries = cachedEntries;
            }
            bool showLlmDebugInfo = ShouldShowLlmDebugInfo();
            // Dev-mode-only: when on, reveal in-progress/stuck entries in the list (the full debug
            // toggle already shows them, so this only matters when debug info is off).
            bool showGeneratingEntries = ShouldShowGeneratingEntries();
            bool showPromptOnlyEntries = ShouldShowPromptOnlyEntries();
            RebuildVisibleEntryCachesIfNeeded(entries, pawn, token, showLlmDebugInfo, showGeneratingEntries, showPromptOnlyEntries);
            int generatingCount = cachedGeneratingCount;
            List<DiaryEntryView> visibleEntries = cachedVisibleEntries;

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

            List<int> years = cachedVisibleYears;
            EnsureSelectedYear(pawn, years);
            SelectYearForPendingScroll(pawn, visibleEntries);

            if (years.Count > 1)
            {
                Rect yearRect = new Rect(rect.x, entriesY, rect.width, YearFilterHeight);
                DrawYearFilter(yearRect, years, visibleEntries);
                entriesY = yearRect.yMax + YearFilterGap;
                outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);
            }

            List<DiaryEntryView> ordered = OrderedEntriesForSelectedYear(selectedYear);

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
            List<DiaryNameHighlight> nameHighlights = NameHighlightsFor(pawn);
            EnsureEntryMeasurementBufferCapacity(ordered.Count);
            string[] entryKeys = entryKeysBuffer;
            bool[] expandedTargets = expandedTargetsBuffer;
            float[] expansionBlends = expansionBlendsBuffer;
            float[] fullHeights = fullHeightsBuffer;
            float[] heights = heightsBuffer;
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
                    ? EntryHeight(entry, viewWidth, showLlmDebugInfo, nameHighlights)
                    : CollapsedEntryHeight;

                entryKeys[i] = entryKey;
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
                string debugText = showLlmDebugInfo && !IsPromptOnly(entry) ? entry.DebugText : string.Empty;
                float innerTextWidth = localEntryRect.width - 20f;
                string atmosphereCue = EntryAtmosphereCue(entry);
                bool allowDirectSpeechBlocks = EntryAllowDirectSpeechBlocks(entry);
                DiaryTextDecorationContext decorationContext = EntryTextDecorationContext(entry);
                int roleplaySeed = StableTextSeed(entryKeys[i]);
                IEnumerable<DiaryNameHighlight> entryNameHighlights = IsPromptOnly(entry) ? null : nameHighlights;
                float mainTextHeight = RoleplayTextHeight(
                    bodyText,
                    innerTextWidth,
                    atmosphereCue,
                    allowDirectSpeechBlocks,
                    decorationContext,
                    roleplaySeed,
                    entryNameHighlights);
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
                        allowDirectSpeechBlocks,
                        decorationContext,
                        roleplaySeed,
                        entryNameHighlights);
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
                    // Anchor above the dev-only footer (DevCopyFooter, 0 outside dev mode) so the
                    // bottom-left copy badge never overlaps or clips the model name.
                    Rect modelRect = new Rect(localEntryRect.x + 12f, localEntryRect.yMax - DevCopyFooter - EntryBottomPadding - ModelNameHeight, localEntryRect.width - 24f, ModelNameHeight);
                    DrawModelName(modelRect, entry.LlmModel);
                }

                // Dev-only copy badge sits in the reserved bottom-left footer, drawn last so it
                // floats above the page wash/highlight without competing with body text or model name.
                DrawCopyButton(localEntryRect, entry);

                GUI.EndGroup();
                curY += height + EntryGap;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Refreshes the filtered entries and year list only when the backing render token or view
        /// toggles change. This is the allocation-sensitive part of the immediate-mode tab draw.
        /// </summary>
        private void RebuildVisibleEntryCachesIfNeeded(
            IReadOnlyList<DiaryEntryView> entries,
            Pawn pawn,
            DiaryRenderToken token,
            bool showLlmDebugInfo,
            bool showGeneratingEntries,
            bool showPromptOnlyEntries)
        {
            if (cachedVisibleSource == entries
                && cachedVisiblePawn == pawn
                && token.Equals(cachedVisibleToken)
                && cachedVisibleShowDebug == showLlmDebugInfo
                && cachedVisibleShowGenerating == showGeneratingEntries
                && cachedVisibleShowPromptOnly == showPromptOnlyEntries
                && cachedVisiblePreviewKind == devPreviewKind)
            {
                return;
            }

            cachedVisibleSource = entries;
            cachedVisiblePawn = pawn;
            cachedVisibleToken = token;
            cachedVisibleShowDebug = showLlmDebugInfo;
            cachedVisibleShowGenerating = showGeneratingEntries;
            cachedVisibleShowPromptOnly = showPromptOnlyEntries;
            cachedVisiblePreviewKind = devPreviewKind;
            cachedGeneratingCount = 0;
            cachedVisibleEntries.Clear();
            cachedVisibleYears.Clear();

            AddDevPreviewEntryIfNeeded(cachedVisibleEntries, cachedVisibleYears, pawn);

            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    DiaryEntryView entry = entries[i];
                    bool generating = IsGenerating(entry);
                    if (generating)
                    {
                        cachedGeneratingCount++;
                    }

                    if (entry != null
                        && (showLlmDebugInfo
                            || IsGenerated(entry)
                            || (showGeneratingEntries && generating)
                            || (showPromptOnlyEntries && IsPromptOnly(entry))))
                    {
                        cachedVisibleEntries.Add(entry);
                        AddYearIfMissing(cachedVisibleYears, EntryYear(entry));
                    }
                }
            }

            cachedVisibleYears.Sort((left, right) => right.CompareTo(left));
            cachedVisibleRevision++;
        }

        /// <summary>
        /// Reuses the selected-year ordering until the visible-entry cache or selected year changes.
        /// </summary>
        private List<DiaryEntryView> OrderedEntriesForSelectedYear(int year)
        {
            if (cachedOrderedVisibleRevision == cachedVisibleRevision && cachedOrderedYear == year)
            {
                return cachedOrderedEntries;
            }

            cachedOrderedEntries.Clear();
            for (int i = 0; i < cachedVisibleEntries.Count; i++)
            {
                DiaryEntryView entry = cachedVisibleEntries[i];
                if (EntryYear(entry) == year)
                {
                    cachedOrderedEntries.Add(entry);
                }
            }

            cachedOrderedEntries.Sort((left, right) => right.Tick.CompareTo(left.Tick));
            cachedOrderedVisibleRevision = cachedVisibleRevision;
            cachedOrderedYear = year;
            return cachedOrderedEntries;
        }

        /// <summary>
        /// Appends a year once. The list is tiny in practice, so a linear scan avoids a per-frame set.
        /// </summary>
        private static void AddYearIfMissing(List<int> years, int year)
        {
            if (!years.Contains(year))
            {
                years.Add(year);
            }
        }

        /// <summary>
        /// Grows reusable per-entry draw buffers when a larger diary page is opened.
        /// </summary>
        private void EnsureEntryMeasurementBufferCapacity(int count)
        {
            if (entryKeysBuffer.Length < count)
            {
                Array.Resize(ref entryKeysBuffer, count);
            }

            if (expandedTargetsBuffer.Length < count)
            {
                Array.Resize(ref expandedTargetsBuffer, count);
            }

            if (expansionBlendsBuffer.Length < count)
            {
                Array.Resize(ref expansionBlendsBuffer, count);
            }

            if (fullHeightsBuffer.Length < count)
            {
                Array.Resize(ref fullHeightsBuffer, count);
            }

            if (heightsBuffer.Length < count)
            {
                Array.Resize(ref heightsBuffer, count);
            }
        }




    }
}
