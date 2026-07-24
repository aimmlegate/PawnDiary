// Thin RimWorld inspect-tab host for the reusable DiaryJournalView.
// RimWorld owns this lifecycle object and calls FillTab every frame; all journal rendering,
// virtualization, filters, favorites, and paging live in DiaryJournalView so another window can
// reuse exactly the same behavior. See AGENTS.md ("UI — diary tab").
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Inspector-tab adapter that resolves the selected pawn and delegates drawing to
    /// <see cref="DiaryJournalView"/>.
    /// </summary>
    public class ITab_Pawn_Diary : ITab
    {
        private const float FallbackTabWidth = 720f;
        private const float FallbackTabHeight = 800f;
        private const float FallbackTabMinHeight = 360f;
        private const float FallbackTabScreenHeightMargin = 72f;
        private const float FallbackTabMinWidth = 400f;
        private const float FallbackTabScreenWidthMargin = 100f;
        // Vanilla inspect-tab geometry: the tab bottom sits above the inspect pane and bottom bar.
        private const float VanillaTabButtonStripHeight = 30f;
        private const float FallbackInspectChromeBelowTab = 230f;

        private readonly DiaryJournalView journalView = new DiaryJournalView();

        // Programmatic opens need one frame where RimWorld can see the otherwise-hidden registered tab.
        private static bool commandOpenRequested;

        public ITab_Pawn_Diary()
        {
            ApplyResponsiveTabSize(UI.screenHeight - FallbackInspectChromeBelowTab);
            labelKey = "PawnDiaryTabLabel";
        }

        /// <summary>
        /// Refreshes the tab size before RimWorld computes its screen rectangle.
        /// </summary>
        protected override void UpdateSize()
        {
            base.UpdateSize();
            ApplyResponsiveTabSize(PaneTopY - VanillaTabButtonStripHeight);
        }

        private void ApplyResponsiveTabSize(float tabBottomAnchorY)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            size = new Vector2(
                ResponsiveTabWidth(style),
                ResponsiveTabHeight(style, tabBottomAnchorY));
        }

        private static float ResponsiveTabWidth(DiaryUiStyleDef style)
        {
            float preferred = PositiveFiniteOrFallback(style.tabWidth, FallbackTabWidth);
            if (!DiaryJournalView.FilterPanelSettingEnabled() && style.filterPanelWidth > 0f)
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
            return maxWidth < minWidth ? minWidth : Mathf.Clamp(preferred, minWidth, maxWidth);
        }

        private static float ResponsiveTabHeight(DiaryUiStyleDef style, float tabBottomAnchorY)
        {
            float preferred = PositiveFiniteOrFallback(style.tabHeight, FallbackTabHeight);
            if (!IsPositiveFinite(tabBottomAnchorY))
            {
                return preferred;
            }

            float margin = NonNegativeFiniteOrFallback(
                style.tabScreenHeightMargin,
                FallbackTabScreenHeightMargin);
            float maxHeight = tabBottomAnchorY - margin;
            if (!IsPositiveFinite(maxHeight))
            {
                maxHeight = tabBottomAnchorY;
            }

            maxHeight = Mathf.Max(1f, maxHeight);
            float minHeight = PositiveFiniteOrFallback(style.tabMinHeight, FallbackTabMinHeight);
            return maxHeight < minHeight ? maxHeight : Mathf.Clamp(preferred, minHeight, maxHeight);
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
        /// Forwards a linked-page request into the shared journal renderer.
        /// </summary>
        internal static void RequestScrollToEntry(Pawn pawn, string eventId)
        {
            DiaryJournalView.RequestScrollToEntry(pawn, eventId);
        }

        /// <summary>
        /// Clears a pending linked-page request when its host could not open.
        /// </summary>
        internal static void ClearPendingScrollRequest()
        {
            DiaryJournalView.ClearPendingScrollRequest();
        }

        /// <summary>
        /// Opens the Diary inspect tab even when its normal tab button is hidden in settings.
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

        public override bool IsVisible
        {
            get { return PawnToShow() != null; }
        }

        public override bool Hidden
        {
            get
            {
                return !commandOpenRequested
                    && (PawnDiaryMod.Settings == null
                        || !PawnDiaryMod.Settings.showDiaryInspectTab
                        || PawnDiaryMod.Settings.useDiaryReaderWindow);
            }
        }

        protected override void CloseTab()
        {
            commandOpenRequested = false;
            base.CloseTab();
        }

        /// <summary>
        /// True when the pawn should have diary UI access.
        /// </summary>
        internal static bool CanShowDiaryFor(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike && pawn.IsColonist;
        }

        private Pawn PawnToShow()
        {
            Pawn pawn = SelPawn;
            if (pawn == null)
            {
                pawn = (SelThing as Corpse)?.InnerPawn;
            }

            return CanShowDiaryFor(pawn) ? pawn : null;
        }

        /// <summary>
        /// RimWorld's immediate-mode draw callback for the tab.
        /// </summary>
        protected override void FillTab()
        {
            Pawn pawn = PawnToShow();
            if (pawn == null)
            {
                return;
            }

            journalView.Draw(
                new Rect(0f, 0f, size.x, size.y),
                DiaryReaderSubject.FromPawn(pawn),
                DiaryGameComponent.Instance);
        }
    }
}
