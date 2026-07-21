// The hidden inspector tab (UI) that renders the selected pawn's finished diary entries.
// RimWorld calls FillTab() to draw it using immediate-mode GUI (the whole tab is re-emitted each
// frame) after a pawn/corpse command opens it. It indexes saved events through
// DiaryGameComponent.BeginTabYearIndexBuild and materializes the selected year's cards on demand.
// See AGENTS.md ("lifecycle").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // Inspector tab that shows the selected pawn's diary. It is visible in the pawn tab row by
    // default, and can be hidden when the player prefers the bottom command button instead.
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary : ITab
    {
        // The entry cache is impure UI state: it stores the selected live Pawn and reuses visible lists
        // between immediate-mode draw frames until the pawn's render token or dev filters change.
        private readonly DiaryTabVisibleEntriesCache visibleEntriesCache = new DiaryTabVisibleEntriesCache();

        // The selected-year measurement arrays are rebuilt every frame from the cache's ordered page.
        // Keeping them here leaves the cache focused on entry visibility rather than card rendering.
        private string[] entryKeysBuffer = new string[0];
        private bool[] expandedTargetsBuffer = new bool[0];
        private float[] expansionBlendsBuffer = new float[0];
        private float[] fullHeightsBuffer = new float[0];
        private float[] heightsBuffer = new float[0];
        private float[] entryOffsetsBuffer = new float[0];
        // Non-null for entries that open a new quadrum: the localized "Aprimay · Spring · 5500" header
        // drawn just above the card. Filled during the same layout pass that computes row offsets so
        // the reserved divider space and the drawn divider can never disagree.
        private string[] dividerLabelsBuffer = new string[0];

        // Virtualized scroll layout for the selected year. The ordered entry List is reused by
        // DiaryTabVisibleEntriesCache, so the cache key includes its revision/year in addition to the
        // object reference. Offscreen cards are not drawn, and idle frames reuse row offsets instead
        // of measuring/summing hundreds of collapsed rows every draw.
        private List<DiaryEntryView> cachedLayoutEntries;
        private int cachedLayoutVisibleRevision = -1;
        private string cachedLayoutPawnId;
        private int cachedLayoutSelectedYear = UnknownYear;
        private float cachedLayoutViewWidth = -1f;
        private bool cachedLayoutShowDebug;
        private DiaryRenderToken cachedLayoutToken;
        private int cachedLayoutHighlightVersion = -1;
        private int cachedLayoutExpansionVersion = -1;
        private float cachedLayoutViewHeight;
        private bool cachedLayoutAnimationSettled = true;
        private bool layoutBuildInProgress;
        private List<DiaryEntryView> layoutBuildEntries;
        private int layoutBuildVisibleRevision = -1;
        private string layoutBuildPawnId;
        private int layoutBuildSelectedYear = UnknownYear;
        private float layoutBuildViewWidth = -1f;
        private bool layoutBuildShowDebug;
        private DiaryRenderToken layoutBuildToken;
        private int layoutBuildHighlightVersion = -1;
        private int layoutBuildExpansionVersion = -1;
        private int layoutBuildIndex;
        private float layoutBuildCurY;
        private bool layoutBuildAnimationSettled = true;
        // Quadrum key of the previously laid-out row, carried across sliced layout frames so a divider
        // is placed exactly at each quadrum change even when the build spans several frames.
        private int layoutBuildPrevQuadrumKey = DiaryQuadrumDivider.UndatedKey;

        // Cached full (expanded) card heights. The helper owns the measured-height dictionary, while
        // this tab still decides when rows exist, whether they are expanded, and where they sit in the
        // virtual scroll layout.
        private readonly DiaryEntryCardMeasurer entryCardMeasurer = new DiaryEntryCardMeasurer();

        // Diary tab presentation values are XML-backed via DiaryUiStyleDef. These accessors keep the
        // drawing code readable while letting modders retune spacing/colors without recompiling.
        private static DiaryUiStyleDef UiStyle => DiaryUiStyles.Current;
        private const float FallbackTabWidth = 720f;
        private const float FallbackTabHeight = 800f;
        private const float FallbackTabMinHeight = 360f;
        private const float FallbackTabScreenHeightMargin = 72f;
        // The tab hangs to the LEFT of the inspect pane, so a wide tab (now that it carries the filter
        // panel) must not exceed the logical screen width or it runs off the left edge. Clamp the width
        // like the height is clamped, keeping at least a usable minimum.
        private const float FallbackTabMinWidth = 400f;
        private const float FallbackTabScreenWidthMargin = 100f;
        // Vanilla inspect-tab geometry, not tunable style: InspectTabBase.TabRect hangs the tab's
        // bottom edge at PaneTopY minus the 30px tab-button strip, and MainTabWindow_Inspect puts
        // PaneTopY at UI.screenHeight - 165 (inspect pane) - 35 (bottom button bar). The height
        // clamp must subtract this chrome or the tab's top edge pokes above the screen top.
        private const float VanillaTabButtonStripHeight = 30f;
        private const float FallbackInspectChromeBelowTab = 230f; // 165 pane + 35 bottom bar + 30 strip
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
        private const int DevMockDiaryTargetYears = 3;
        private const int DevMockDiaryEntriesPerYear = 2000;
        private const int DevMockDiaryTargetCount = DevMockDiaryTargetYears * DevMockDiaryEntriesPerYear;
        private static float CollapsedEntryHeight => UiStyle.CollapsedEntryHeight;
        private static float ExpansionAnimationSpeed => UiStyle.expansionAnimationSpeed;
        private static float LinkedEntryPadding => UiStyle.linkedEntryPadding;
        private static float LinkedEntryLabelHeight => UiStyle.linkedEntryLabelHeight;
        private static float LinkedEntryTextHeight => UiStyle.linkedEntryTextHeight;
        private static float LinkedEntryTotalHeight => UiStyle.linkedEntryTotalHeight;
        private static float YearFilterHeight => UiStyle.yearFilterHeight;
        private static float YearFilterGap => UiStyle.yearFilterGap;

        // Player-facing writing-style opener in the Diary header. Layout values come from XML via
        // UiStyle (DiaryUiStyleDef). DevControlsHeight is a stable display-only estimate of the dev
        // block (mock/prompt-suite/dev-preview rows) so PawnControlsHeight can reserve its space.
        private static float WritingStyleIconSize => UiStyle.writingStyleIconSize;
        private static float WritingStyleIconRightGap => UiStyle.writingStyleIconRightGap;
        private static float WritingStyleIconAlpha => UiStyle.writingStyleIconAlpha;
        private static float WritingStyleIconHoverAlpha => UiStyle.writingStyleIconHoverAlpha;
        // Stable display-only estimate of the dev block height (4 toggle rows + 3 full-width fixture
        // buttons + the 3-column preview grid). It only needs to be an upper bound: the filter panel
        // scrolls, so a little slack just adds scroll space, while under-reserving would clip the last
        // preview row. Keep this in step with DrawPawnControls + DrawDevPreviewButtons if they change.
        private const float DevControlsHeight = 360f;
        private static float YearButtonWidth => UiStyle.yearButtonWidth;
        private static float ModelNameTopPadding => UiStyle.modelNameTopPadding;
        private static float ModelNameHeight => UiStyle.modelNameHeight;
        private static float DebugTextTopPadding => UiStyle.debugTextTopPadding;
        private static float EntryAccentWidth => UiStyle.entryAccentWidth;
        private static float EntryLabelMaxWidth => UiStyle.entryLabelMaxWidth;
        private static float QuadrumDividerHeight => UiStyle.quadrumDividerHeight;
        private static float QuadrumDividerTopGap => UiStyle.quadrumDividerTopGap;
        private static float QuadrumDividerLineGap => UiStyle.quadrumDividerLineGap;
        private static float EntryFadeDurationSeconds => UiStyle.entryFadeDurationSeconds;
        private static float TitleFadeDurationSeconds => UiStyle.titleFadeDurationSeconds;
        private static float VirtualizedEntryOverscanHeight => UiStyle.VirtualizedEntryOverscanHeight;
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
        private int entryExpansionVersion;
        private float lastExpansionAnimationSeconds;
        // Set by the Social-tab click patch before opening this tab. FillTab consumes it once
        // the relevant pawn's generated entry list is available.
        private static string pendingScrollPawnId;
        private static string pendingScrollEventId;
        // When the player chooses gizmo-only access, Hidden keeps the tab out of the normal tab
        // strip. Programmatic opens still need one frame where RimWorld can see the registered tab.
        private static bool commandOpenRequested;

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
            // The inspect window (and thus PaneTopY) may not exist yet while RimWorld is still
            // constructing shared tab instances, so the constructor sizes from the vanilla chrome
            // constants; UpdateSize re-derives from the live pane before every draw.
            ApplyResponsiveTabSize(UI.screenHeight - FallbackInspectChromeBelowTab);
            labelKey = "PawnDiaryTabLabel";
        }

        /// <summary>
        /// Vanilla hook: InspectTabBase.TabRect calls this immediately before computing the tab
        /// rect each frame, so the refreshed size lands in the same frame and tracks resolution,
        /// UI-scale, and pane-moving mods via the live PaneTopY.
        /// </summary>
        protected override void UpdateSize()
        {
            base.UpdateSize();
            ApplyResponsiveTabSize(PaneTopY - VanillaTabButtonStripHeight);
        }

        /// <summary>
        /// Applies the XML preferred tab size, clamping height to the space actually available
        /// above the tab's bottom anchor (the inspect pane's tab strip, not the screen bottom).
        /// This is UI-only state, so refreshing it during draw is safe.
        /// </summary>
        private void ApplyResponsiveTabSize(float tabBottomAnchorY)
        {
            DiaryUiStyleDef style = UiStyle;
            size = new Vector2(
                ResponsiveTabWidth(style),
                ResponsiveTabHeight(style, tabBottomAnchorY));
        }

        /// <summary>
        /// True when the player currently wants the right-hand filter/controls panel shown. Defaults to
        /// on when settings are not yet loaded (early startup) so the tab still sizes for the panel.
        /// </summary>
        private static bool FilterPanelSettingEnabled()
        {
            return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.showDiaryFilterPanel;
        }

        /// <summary>
        /// Clamps the preferred tab width to the logical screen so the panel-widened tab cannot extend
        /// off the left edge on small resolutions or high UI scale. The panel itself hides gracefully
        /// (see ResolveFilterPanelWidth) when the clamped width is too small to hold both columns.
        /// </summary>
        private static float ResponsiveTabWidth(DiaryUiStyleDef style)
        {
            float preferred = PositiveFiniteOrFallback(style.tabWidth, FallbackTabWidth);
            // tabWidth carries the right-hand filter panel (panel width + gap) on top of the journal
            // column. When the player hides the panel, drop that allotment so the whole tab shrinks
            // back to a journal-only window instead of leaving the journal floating in the wide frame.
            if (!FilterPanelSettingEnabled() && style.filterPanelWidth > 0f)
            {
                float panelAllotment = style.filterPanelWidth + Mathf.Max(0f, style.filterPanelGap);
                preferred = Mathf.Max(FallbackTabMinWidth, preferred - panelAllotment);
            }

            float screenWidth = UI.screenWidth;
            if (!IsPositiveFinite(screenWidth))
            {
                return preferred;
            }

            float maxWidth = screenWidth - FallbackTabScreenWidthMargin;
            float minWidth = Mathf.Min(preferred, FallbackTabMinWidth);
            if (maxWidth < minWidth)
            {
                return minWidth;
            }

            return Mathf.Clamp(preferred, minWidth, maxWidth);
        }

        private static float ResponsiveTabHeight(DiaryUiStyleDef style, float tabBottomAnchorY)
        {
            float preferred = PositiveFiniteOrFallback(style.tabHeight, FallbackTabHeight);
            if (!IsPositiveFinite(tabBottomAnchorY))
            {
                return preferred;
            }

            float margin = NonNegativeFiniteOrFallback(style.tabScreenHeightMargin, FallbackTabScreenHeightMargin);
            float maxHeight = tabBottomAnchorY - margin;
            if (!IsPositiveFinite(maxHeight))
            {
                maxHeight = tabBottomAnchorY;
            }

            maxHeight = Mathf.Max(1f, maxHeight);
            float minHeight = PositiveFiniteOrFallback(style.tabMinHeight, FallbackTabMinHeight);
            if (maxHeight < minHeight)
            {
                return maxHeight;
            }

            return Mathf.Clamp(preferred, minHeight, maxHeight);
        }

        private static float PositiveFiniteOrFallback(float value, float fallback)
        {
            return IsPositiveFinite(value) ? value : fallback;
        }

        private static float NonNegativeFiniteOrFallback(float value, float fallback)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Requests that the next Diary tab draw for this pawn scroll to the given event card.
        /// Used by the Social-tab play-log click patch.
        /// </summary>
        internal static void RequestScrollToEntry(Pawn pawn, string eventId)
        {
            pendingScrollPawnId = pawn?.GetUniqueLoadID();
            pendingScrollEventId = eventId;
        }

        /// <summary>
        /// Clears a pending scroll request when the tab could not be opened after all.
        /// </summary>
        internal static void ClearPendingScrollRequest()
        {
            pendingScrollPawnId = null;
            pendingScrollEventId = null;
        }

        /// <summary>
        /// Opens the Diary tab from a gizmo, social-log link, or linked-entry card even when the
        /// player has hidden the normal inspect-tab button in settings.
        /// </summary>
        internal static InspectTabBase OpenDiaryTab()
        {
            DiaryModStartup.EnsureDiaryTabInjected();
            commandOpenRequested = true;
            InspectTabBase opened = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Diary));
            if (!(opened is ITab_Pawn_Diary))
            {
                commandOpenRequested = false;
            }

            return opened;
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
            get
            {
                return !commandOpenRequested
                    && (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.showDiaryInspectTab);
            }
        }

        protected override void CloseTab()
        {
            commandOpenRequested = false;
            base.CloseTab();
        }

        /// <summary>
        /// True when the pawn should have diary UI access, matching the tab's existing visibility rule.
        /// </summary>
        internal static bool CanShowDiaryFor(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike && pawn.IsColonist;
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
            DiaryGameComponent component = DiaryGameComponent.Instance;
            component?.AcknowledgeGeneratedEntriesFor(pawn);
            ApplyPendingDevPreview(pawn);

            bool showLlmDebugInfo = ShouldShowLlmDebugInfo();
            // Dev-mode-only: when on, reveal in-progress/stuck entries in the list (the full debug
            // toggle already shows them, so this only matters when debug info is off).
            bool showGeneratingEntries = ShouldShowGeneratingEntries();
            bool showPromptOnlyEntries = ShouldShowPromptOnlyEntries();
            // Build a cheap year/count index first. Full DiaryEntryView cards are materialized only
            // for selectedYear below, so selecting a pawn with thousands of archived pages stays
            // responsive.
            DiaryRenderToken token;
            visibleEntriesCache.RebuildIndexIfNeeded(
                pawn,
                component,
                showLlmDebugInfo,
                showGeneratingEntries,
                showPromptOnlyEntries,
                devPreviewKind,
                out token);
            int generatingCount = visibleEntriesCache.GeneratingCount;

            // Two-column layout: the journal (virtualized cards) on the left, and an independent,
            // non-virtualized filter/controls panel on the right. The panel hosts the year selector,
            // filter stubs, and — in dev mode — the diary dev tools. The journal keeps its familiar
            // width because tabWidth grew by the panel's width; hiding the panel (header toggle) shrinks
            // the whole tab back to that journal width (ResponsiveTabWidth), with the year pager falling
            // back inline below and the dev tools staying panel-only.
            bool filterPanelEnabled = FilterPanelSettingEnabled();
            float panelWidth = filterPanelEnabled ? ResolveFilterPanelWidth(rect.width) : 0f;
            Rect filterPanelRect = new Rect(rect.xMax - panelWidth, rect.y, panelWidth, rect.height);
            Rect journalRect = panelWidth > 0f
                ? new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - panelWidth - FilterPanelGap), rect.height)
                : rect;

            Rect headerRect = new Rect(journalRect.x, journalRect.y, journalRect.width, 34f);
            float headerRight = journalRect.xMax;
            Rect writingIndicatorRect = Rect.zero;
            if (generatingCount > 0)
            {
                writingIndicatorRect = new Rect(
                    journalRect.xMax - StatusBadgeRightPadding - StatusBadgeWidth,
                    journalRect.y + 3f,
                    StatusBadgeWidth,
                    StatusBadgeHeight);
                headerRight = writingIndicatorRect.x - 8f;
            }

            // Filter-panel toggle: a compact list icon that shows/hides the right-hand sidebar. It sits
            // at the far right of the journal header — just after the writing-style icon — so the header
            // controls stay grouped at the top-right of the journal column, clear of RimWorld's
            // inspect-pane close button. Drawn first so it takes the rightmost slot.
            {
                float toggleSize = Mathf.Max(1f, WritingStyleIconSize);
                Rect toggleRect = new Rect(
                    headerRight - toggleSize,
                    journalRect.y + Mathf.Max(0f, (headerRect.height - toggleSize) * 0.5f),
                    toggleSize,
                    toggleSize);
                if (DrawFilterPanelToggleIcon(toggleRect, filterPanelEnabled))
                {
                    ToggleFilterPanelVisible();
                }

                headerRight = toggleRect.x - Mathf.Max(0f, WritingStyleIconRightGap);
            }

            // Writing-style opener sits just left of the filter toggle.
            if (ShouldDrawWritingStyleButton(pawn, component))
            {
                float iconSize = Mathf.Max(1f, WritingStyleIconSize);
                Rect writingStyleIconRect = new Rect(
                    headerRight - iconSize,
                    journalRect.y + Mathf.Max(0f, (headerRect.height - iconSize) * 0.5f),
                    iconSize,
                    iconSize);
                DrawWritingStyleHeaderIcon(writingStyleIconRect, pawn, component);
                headerRight = writingStyleIconRect.x - Mathf.Max(0f, WritingStyleIconRightGap);
            }

            headerRect.width = Mathf.Max(0f, headerRight - journalRect.x);
            if (generatingCount > 0)
            {
                DrawWritingIndicator(writingIndicatorRect);
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "PawnDiary.Tab.DiaryHeader".Translate(pawn.LabelShortCap));
            Text.Font = GameFont.Small;

            // Dev tools and the year selector normally live in the right-hand panel, so the journal
            // column opens directly under the header (a fixed 36px title row plus the usual gap).
            bool panelVisible = panelWidth > 0f;
            float entriesY = journalRect.y + 36f + EntryGap;

            // Resolve the year list and the selected year's ordered cards up front so the filter panel
            // (drawn once, in every state) can host the year selector and the stub tag list, and the
            // journal column below can render or show its own loading/empty state.
            bool indexLoading = visibleEntriesCache.IsIndexLoading;
            List<int> years = indexLoading ? null : visibleEntriesCache.VisibleYears;
            if (!indexLoading)
            {
                component?.AcknowledgeGeneratedEntriesFor(
                    pawn,
                    visibleEntriesCache.CompletedCount,
                    visibleEntriesCache.PendingCount,
                    token);
                EnsureSelectedYear(pawn, years);
                SelectYearForPendingScroll(pawn, visibleEntriesCache);
            }

            List<DiaryEntryView> ordered = null;
            bool haveOrdered = !indexLoading
                && years.Count > 0
                && visibleEntriesCache.TryGetOrderedEntriesForSelectedYear(pawn, selectedYear, out ordered);

            DrawFilterPanel(filterPanelRect, pawn, component, years, visibleEntriesCache, haveOrdered ? ordered : null);

            // Fallback when the panel is hidden (player toggled it off, or the tab is too narrow to fit
            // it via off-default XML): keep the year pager reachable inline in the journal column. The
            // dev tools are deliberately NOT drawn here — they live only in the filter panel, never on
            // the main journal, so hiding the panel gives a clean journal-only window.
            if (!panelVisible)
            {
                if (!indexLoading && years != null && years.Count > 1)
                {
                    Rect yearRect = new Rect(journalRect.x, entriesY, journalRect.width, YearFilterHeight);
                    DrawYearFilter(yearRect, years, visibleEntriesCache);
                    entriesY = yearRect.yMax + YearFilterGap;
                }
            }

            Rect outRect = new Rect(journalRect.x, entriesY, journalRect.width, journalRect.yMax - entriesY);

            if (indexLoading)
            {
                DrawDiaryLoading(outRect, visibleEntriesCache.LoadingProcessed, visibleEntriesCache.LoadingTotal);
                return;
            }

            if (years.Count == 0)
            {
                Widgets.Label(outRect, (showLlmDebugInfo ? "PawnDiary.Tab.NoEntries" : "PawnDiary.Tab.NoGeneratedEntries").Translate());
                return;
            }

            if (!haveOrdered)
            {
                DrawDiaryLoading(outRect, visibleEntriesCache.LoadingProcessed, visibleEntriesCache.LoadingTotal);
                return;
            }

            if (ordered.Count == 0)
            {
                Widgets.Label(outRect, (showLlmDebugInfo ? "PawnDiary.Tab.NoEntries" : "PawnDiary.Tab.NoGeneratedEntries").Translate());
                return;
            }

            // Subtract 16f to leave room for the scrollbar grip inside the scroll view.
            float viewWidth = outRect.width - 16f;
            float animationDelta = ExpansionAnimationDelta();
            List<DiaryNameHighlight> nameHighlights = NameHighlightsFor(pawn);
            int layoutProcessed;
            int layoutTotal;
            if (!RebuildEntryLayoutIfNeeded(
                ordered,
                visibleEntriesCache.VisibleRevision,
                pawn.GetUniqueLoadID(),
                viewWidth,
                showLlmDebugInfo,
                token,
                nameHighlights,
                animationDelta,
                out layoutProcessed,
                out layoutTotal))
            {
                DrawDiaryLoading(
                    outRect,
                    visibleEntriesCache.LoadingProcessed + layoutProcessed,
                    visibleEntriesCache.LoadingTotal + layoutTotal);
                return;
            }

            string[] entryKeys = entryKeysBuffer;
            bool[] expandedTargets = expandedTargetsBuffer;
            float[] expansionBlends = expansionBlendsBuffer;
            float[] fullHeights = fullHeightsBuffer;
            float[] heights = heightsBuffer;
            float[] entryOffsets = entryOffsetsBuffer;
            float viewHeight = cachedLayoutViewHeight;
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            TryApplyPendingScroll(pawn, ordered, entryOffsets, viewHeight, outRect.height);
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, viewHeight - outRect.height));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            // Immediate-mode safety net: BeginScrollView and the per-card GUI.BeginGroup below push
            // onto Unity's shared GUI clip stack, and their matching EndGroup/EndScrollView calls pop
            // it. If a draw call throws mid-card those pops are skipped, leaving the stack unbalanced
            // and corrupting the rest of the frame's UI — not just this tab. The finally closes
            // whatever is still open, so one bad entry degrades to a missing card, never a broken UI.
            Color dialogueColor = PreferredDialogueColor(pawn);
            bool entryGroupOpen = false;
            try
            {
                float overscanHeight = VirtualizedEntryOverscanHeight;
                float visibleTop = Mathf.Max(0f, scrollPosition.y - overscanHeight);
                float visibleBottom = Mathf.Min(viewHeight, scrollPosition.y + outRect.height + overscanHeight);
                int firstVisibleIndex = FirstVisibleEntryIndex(ordered.Count, visibleTop);
                for (int i = firstVisibleIndex; i < ordered.Count; i++)
                {
                    DiaryEntryView entry = ordered[i];
                    bool expanded = expandedTargets[i];
                    float expansionBlend = expansionBlends[i];
                    float fullHeight = fullHeights[i];
                    float height = heights[i];
                    float curY = entryOffsets[i];

                    // Viewport virtualization. Widgets.BeginScrollView clips pixels, but would still
                    // execute every card's immediate-mode UI calls. Cached offsets let us jump to the
                    // first buffered row and stop once rows fall below the buffered viewport. The
                    // overscan keeps rows alive just outside the screen during fast scrolls, avoiding
                    // visible pop-in/animation at the exact viewport edge.
                    if (curY > visibleBottom)
                    {
                        break;
                    }

                    // Season/quadrum divider sits in the reserved space just above this card. Drawn in
                    // scroll-view coordinates (outside the per-card group) so it spans the full width.
                    string dividerLabel = i < dividerLabelsBuffer.Length ? dividerLabelsBuffer[i] : null;
                    if (!string.IsNullOrEmpty(dividerLabel))
                    {
                        Rect dividerRect = new Rect(0f, curY - QuadrumDividerHeight, viewRect.width, QuadrumDividerHeight);
                        DrawQuadrumDivider(dividerRect, dividerLabel);
                    }

                    Rect entryRect = new Rect(0f, curY, viewRect.width, height);

                    Color accentColor = EntryAccentColor(entry);
                    GUI.BeginGroup(entryRect);
                    entryGroupOpen = true;
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
                            SetEntryExpanded(entry, true, expansionBlend);
                        }

                        GUI.EndGroup();
                        entryGroupOpen = false;
                        continue;
                    }

                    if (DiaryEntryCardRenderer.DrawExpanded(
                        new DiaryEntryCardRenderRequest
                        {
                            Entry = entry,
                            EntryKey = entryKeys[i],
                            LocalEntryRect = localEntryRect,
                            VisibleEntryRect = visibleEntryRect,
                            Pawn = pawn,
                            Component = component,
                            AccentColor = accentColor,
                            DialogueColor = dialogueColor,
                            Expanded = expanded,
                            ExpansionBlend = expansionBlend,
                            ShowLlmDebugInfo = showLlmDebugInfo,
                            NameHighlights = nameHighlights,
                        }))
                    {
                        SetEntryExpanded(entry, !expanded, expansionBlend);
                    }

                    GUI.EndGroup();
                    entryGroupOpen = false;
                }
            }
            finally
            {
                // Close a card group left open by a mid-card throw before ending the scroll view, so
                // the GUI clip stack is balanced no matter where the loop above stopped.
                if (entryGroupOpen)
                {
                    GUI.EndGroup();
                }

                Widgets.EndScrollView();
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

            if (entryOffsetsBuffer.Length < count)
            {
                Array.Resize(ref entryOffsetsBuffer, count);
            }

            if (dividerLabelsBuffer.Length < count)
            {
                Array.Resize(ref dividerLabelsBuffer, count);
            }
        }

        /// <summary>
        /// Rebuilds row offsets/heights for the current year only when something that can affect list
        /// layout changed. Idle scroll frames reuse this data, so large archived histories do not make
        /// the tab rescan every card just to compute scroll height.
        /// </summary>
        private bool RebuildEntryLayoutIfNeeded(
            List<DiaryEntryView> ordered,
            int visibleRevision,
            string pawnId,
            float viewWidth,
            bool showLlmDebugInfo,
            DiaryRenderToken token,
            List<DiaryNameHighlight> nameHighlights,
            float animationDelta,
            out int processed,
            out int total)
        {
            processed = ordered?.Count ?? 0;
            total = processed;
            int count = ordered?.Count ?? 0;
            bool listDirty = cachedLayoutEntries != ordered
                || cachedLayoutVisibleRevision != visibleRevision
                || !string.Equals(cachedLayoutPawnId, pawnId, StringComparison.Ordinal)
                || cachedLayoutSelectedYear != selectedYear;
            // Visual layout only needs to rebuild when a real layout input changed: card width, the
            // debug toggle, or the name-highlight set (bold markup changes wrapping). The render
            // token is intentionally NOT tested here: its stateVersion half is the process-wide
            // DiaryStateVersion counter, which ticks whenever ANY pawn's entry status/text/title
            // changes anywhere in the colony. Testing it made every such tick declare the layout
            // dirty and trip the synchronous processAll rebuild below, so viewing a large history
            // during active generation hitched on every tick. Real text changes arrive through a
            // rebuilt ordered list (new visibleRevision), which listDirty already catches — so the
            // token term is redundant as well as harmful. (The cachedLayoutToken field is still
            // recorded for diagnostics; it just no longer drives the dirty decision.)
            bool visualLayoutDirty = cachedLayoutViewWidth != viewWidth
                || cachedLayoutShowDebug != showLlmDebugInfo
                || cachedLayoutHighlightVersion != nameHighlightsVersion;
            bool expansionLayoutDirty = cachedLayoutExpansionVersion != entryExpansionVersion
                || !cachedLayoutAnimationSettled;
            bool bufferDirty = entryOffsetsBuffer.Length < count || heightsBuffer.Length < count;
            bool layoutDirty = visualLayoutDirty || expansionLayoutDirty || bufferDirty;
            bool dirty = listDirty || layoutDirty;
            if (!dirty)
            {
                return true;
            }

            bool alreadyShowingSelectedYear = cachedLayoutEntries != null
                && string.Equals(cachedLayoutPawnId, pawnId, StringComparison.Ordinal)
                && cachedLayoutSelectedYear == selectedYear
                && cachedLayoutViewHeight > 0f;
            if ((!listDirty && layoutDirty) || (listDirty && alreadyShowingSelectedYear))
            {
                // The selected year's data is already visible. Rebuild offsets immediately so scroll,
                // collapse/expand, highlight refreshes, and quiet entry updates never swap the list for
                // the blocking loading panel. Cold loads and explicit year changes still use slices.
                BeginEntryLayoutBuild(ordered, visibleRevision, pawnId, viewWidth, showLlmDebugInfo, token, count);
                ProcessEntryLayoutSlice(
                    ordered,
                    viewWidth,
                    showLlmDebugInfo,
                    token,
                    nameHighlights,
                    animationDelta,
                    count,
                    true,
                    expansionLayoutDirty);
                processed = count;
                total = count;
                return true;
            }

            // Same reasoning as the visualLayoutDirty check above: the in-progress sliced layout
            // build must only be invalidated by a STRUCTURAL change (different list, year, width,
            // filters, highlight set, or expansion version) — never by a global state-version tick.
            // Letting a tick restart this build made the layout scan reset to row zero every frame
            // under active generation, so a large history could never finish laying out and the tab
            // sat on the loading panel. The captured token is still stored (layoutBuildToken) for
            // diagnostics; it is deliberately not part of the identity test.
            if (!layoutBuildInProgress
                || layoutBuildEntries != ordered
                || layoutBuildVisibleRevision != visibleRevision
                || !string.Equals(layoutBuildPawnId, pawnId, StringComparison.Ordinal)
                || layoutBuildSelectedYear != selectedYear
                || layoutBuildViewWidth != viewWidth
                || layoutBuildShowDebug != showLlmDebugInfo
                || layoutBuildHighlightVersion != nameHighlightsVersion
                || layoutBuildExpansionVersion != entryExpansionVersion)
            {
                BeginEntryLayoutBuild(ordered, visibleRevision, pawnId, viewWidth, showLlmDebugInfo, token, count);
            }

            ProcessEntryLayoutSlice(ordered, viewWidth, showLlmDebugInfo, token, nameHighlights, animationDelta, count, false, false);
            processed = layoutBuildInProgress ? layoutBuildIndex : count;
            total = count;
            return !layoutBuildInProgress;
        }

        private void BeginEntryLayoutBuild(
            List<DiaryEntryView> ordered,
            int visibleRevision,
            string pawnId,
            float viewWidth,
            bool showLlmDebugInfo,
            DiaryRenderToken token,
            int count)
        {
            EnsureEntryMeasurementBufferCapacity(count);
            layoutBuildInProgress = true;
            layoutBuildEntries = ordered;
            layoutBuildVisibleRevision = visibleRevision;
            layoutBuildPawnId = pawnId;
            layoutBuildSelectedYear = selectedYear;
            layoutBuildViewWidth = viewWidth;
            layoutBuildShowDebug = showLlmDebugInfo;
            layoutBuildToken = token;
            layoutBuildHighlightVersion = nameHighlightsVersion;
            layoutBuildExpansionVersion = entryExpansionVersion;
            layoutBuildIndex = 0;
            layoutBuildCurY = 0f;
            layoutBuildAnimationSettled = true;
            layoutBuildPrevQuadrumKey = DiaryQuadrumDivider.UndatedKey;
        }

        private void ProcessEntryLayoutSlice(
            List<DiaryEntryView> ordered,
            float viewWidth,
            bool showLlmDebugInfo,
            DiaryRenderToken token,
            List<DiaryNameHighlight> nameHighlights,
            float animationDelta,
            int count,
            bool processAll,
            bool forceAnimation)
        {
            if (!layoutBuildInProgress)
            {
                return;
            }

            int processedThisSlice = 0;
            int animationMax = Math.Max(1, DiaryTuning.UiHistoryScanMaxEventsPerFrame);
            int max = processAll ? int.MaxValue : animationMax;
            float endTime = processAll
                ? float.MaxValue
                : Time.realtimeSinceStartup + Math.Max(0.0001f, DiaryTuning.UiHistoryScanFrameBudgetSeconds);
            bool animateLayout = forceAnimation || count <= animationMax;
            bool cacheMissingAnimationRows = !forceAnimation || count <= MaxExpansionBlendEntries;

            while (layoutBuildIndex < count && processedThisSlice < max)
            {
                int i = layoutBuildIndex;
                DiaryEntryView entry = ordered[i];
                string entryKey = EntryKey(entry);
                bool expanded = IsEntryExpanded(entry, i);
                float expansionBlend = animateLayout
                    ? ExpansionBlendFor(entryKey, expanded, animationDelta, cacheMissingAnimationRows)
                    : (expanded ? 1f : 0f);
                if (Mathf.Abs(expansionBlend - (expanded ? 1f : 0f)) > 0.001f)
                {
                    layoutBuildAnimationSettled = false;
                }

                // Fully collapsed cards only need header height, so avoid expensive wrapped-text
                // measurement until they are expanding or open. Expanded cards are measured once and
                // then cached (see CachedEntryHeight) so reading a long page does not re-measure text
                // every frame.
                float fullHeight = (expanded || expansionBlend > 0f)
                    ? CachedEntryHeight(entry, entryKey, viewWidth, showLlmDebugInfo, token, nameHighlights)
                    : CollapsedEntryHeight;

                // Reserve a quadrum/season divider above this card when it opens a new quadrum. The
                // Undated page (no in-game year) skips dividers entirely. The reserved space here must
                // match exactly what DrawQuadrumDivider paints in the draw loop, so both read the same
                // stored label and geometry.
                int quadrumKey = selectedYear == UnknownYear
                    ? DiaryQuadrumDivider.UndatedKey
                    : DiaryQuadrumDivider.QuadrumKey(entry);
                bool dividerAbove = DiaryQuadrumDivider.HasDividerAbove(layoutBuildPrevQuadrumKey, quadrumKey, i == 0);
                dividerLabelsBuffer[i] = dividerAbove ? DiaryQuadrumDivider.Label(entry) : null;
                layoutBuildPrevQuadrumKey = quadrumKey;
                if (!string.IsNullOrEmpty(dividerLabelsBuffer[i]))
                {
                    // The first divider hugs the top; later ones get a small gap above their card.
                    layoutBuildCurY += QuadrumDividerHeight + (i == 0 ? 0f : QuadrumDividerTopGap);
                }

                entryKeysBuffer[i] = entryKey;
                expandedTargetsBuffer[i] = expanded;
                expansionBlendsBuffer[i] = expansionBlend;
                fullHeightsBuffer[i] = fullHeight;
                heightsBuffer[i] = AnimatedEntryHeight(fullHeight, expansionBlend);
                entryOffsetsBuffer[i] = layoutBuildCurY;

                layoutBuildCurY += heightsBuffer[i];
                if (i < count - 1)
                {
                    layoutBuildCurY += EntryGap;
                }

                layoutBuildIndex++;
                processedThisSlice++;

                if (!processAll && processedThisSlice > 0 && Time.realtimeSinceStartup >= endTime)
                {
                    break;
                }
            }

            if (layoutBuildIndex < count)
            {
                return;
            }

            cachedLayoutEntries = layoutBuildEntries;
            cachedLayoutVisibleRevision = layoutBuildVisibleRevision;
            cachedLayoutPawnId = layoutBuildPawnId;
            cachedLayoutSelectedYear = layoutBuildSelectedYear;
            cachedLayoutViewWidth = layoutBuildViewWidth;
            cachedLayoutShowDebug = layoutBuildShowDebug;
            cachedLayoutToken = layoutBuildToken;
            cachedLayoutHighlightVersion = layoutBuildHighlightVersion;
            cachedLayoutExpansionVersion = layoutBuildExpansionVersion;
            cachedLayoutAnimationSettled = layoutBuildAnimationSettled;
            cachedLayoutViewHeight = layoutBuildCurY + 12f; // includes bottom padding
            layoutBuildInProgress = false;
        }

        /// <summary>
        /// Binary-searches the cached row offsets for the first card that may be visible.
        /// </summary>
        private int FirstVisibleEntryIndex(int count, float visibleTop)
        {
            int low = 0;
            int high = count - 1;
            int result = count;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (entryOffsetsBuffer[mid] + heightsBuffer[mid] < visibleTop)
                {
                    low = mid + 1;
                }
                else
                {
                    result = mid;
                    high = mid - 1;
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the full (expanded) card height for an entry, measuring wrapped text once and
        /// reusing the result across draw frames. Finished entry text is stable, so re-measuring
        /// every expanded card 60x/second while the tab is open is pure waste once it has been laid
        /// out. The cache is dropped wholesale whenever something that can change a card's height
        /// changes: the pawn's render token (entry text/status), the card width, the debug-info
        /// toggle, or the name-highlight set (bold highlight markup can change text wrapping, and the
        /// highlight set changes off the live colony without bumping the render token). Collapsed
        /// cards bypass this entirely via the constant CollapsedEntryHeight.
        /// </summary>
        private float CachedEntryHeight(
            DiaryEntryView entry,
            string entryKey,
            float width,
            bool showLlmDebugInfo,
            DiaryRenderToken token,
            List<DiaryNameHighlight> nameHighlights)
        {
            return entryCardMeasurer.CachedHeight(
                EntryMeasureRequest(entry, entryKey, width, showLlmDebugInfo, nameHighlights),
                token,
                nameHighlightsVersion);
        }




    }
}
