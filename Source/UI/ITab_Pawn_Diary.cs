// The inspector tab (UI) that renders the selected pawn's finished diary entries.
// RimWorld calls FillTab() to draw it using immediate-mode GUI (the whole tab is re-emitted each
// frame). It reads entries via DiaryGameComponent.EntriesFor. See AGENTS.md ("lifecycle").
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // Inspector tab that shows the selected pawn's diary. Replaces the old standalone
    // Diary window/gizmo and is injected as the final pawn inspector tab at startup.
    public class ITab_Pawn_Diary : ITab
    {
        // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        // Vertical space reserved for the per-pawn generation toggle above the scroll view.
        private const float ControlsHeight = 32f;

        // Unity scroll position; persists across frames so the user's scroll offset isn't lost on redraw.
        private Vector2 scrollPosition;

        public ITab_Pawn_Diary()
        {
            size = new Vector2(600f, 500f);
            labelKey = "PawnDiaryTabLabel";
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

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 34f), "PawnDiary.Tab.DiaryHeader".Translate(pawn.LabelShortCap));
            Text.Font = GameFont.Small;

            if (generatingCount > 0)
            {
                DrawGeneratingIndicator(new Rect(rect.xMax - 150f, rect.y + 3f, 150f, 24f), generatingCount);
            }

            Rect controlsRect = new Rect(rect.x, rect.y + 36f, rect.width, ControlsHeight);
            DrawPawnControls(pawn, component, controlsRect);

            // The controls are part of the diary tab, so reserve fixed space before the scroll view.
            float entriesY = controlsRect.yMax + 8f;
            Rect outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);

            // Production view: show only completed LLM output. Pending/raw/debug rows are still
            // saved in the model, but they are deliberately hidden from the player-facing tab.
            List<DiaryEntryView> ordered = entries
                .Where(IsGenerated)
                .OrderByDescending(entry => entry.Tick)
                .ToList();

            if (ordered.Count == 0)
            {
                Widgets.Label(outRect, "PawnDiary.Tab.NoGeneratedEntries".Translate());
                return;
            }

            // Subtract 16f to leave room for the scrollbar grip inside the scroll view.
            float viewWidth = outRect.width - 16f;
            // Total scrollable height including 12f bottom padding.
            float viewHeight = ordered.Sum(entry => EntryHeight(entry, viewWidth)) + 12f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                float height = EntryHeight(entry, viewRect.width);
                Rect entryRect = new Rect(0f, curY, viewRect.width, height);

                Widgets.DrawMenuSection(entryRect);
                Widgets.DrawHighlightIfMouseover(entryRect);

                Rect titleRect = new Rect(entryRect.x, entryRect.y, entryRect.width, 28f);
                Widgets.DrawTitleBG(titleRect);

                GUI.color = new Color(0.86f, 0.86f, 0.86f);
                Widgets.LabelFit(new Rect(entryRect.x + 8f, entryRect.y + 5f, entryRect.width - 16f, 22f), entry.Date);
                GUI.color = Color.white;

                float textHeight = Text.CalcHeight(entry.GeneratedText, entryRect.width - 16f);
                Widgets.Label(new Rect(entryRect.x + 8f, entryRect.y + 36f, entryRect.width - 16f, textHeight), entry.GeneratedText);

                curY += height + 8f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Renders the per-pawn generation toggle above the diary entries.
        /// </summary>
        private static void DrawPawnControls(Pawn pawn, DiaryGameComponent component, Rect rect)
        {
            if (pawn == null || component == null)
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

            listing.End();
        }

        /// <summary>
        /// Draws a compact, RimWorld-style badge while hidden pending entries are being rewritten.
        /// </summary>
        private static void DrawGeneratingIndicator(Rect rect, int count)
        {
            string label = count == 1
                ? "PawnDiary.Status.Generating".Translate()
                : "PawnDiary.Tab.GeneratingCount".Translate(count);

            Widgets.DrawBoxSolidWithOutline(rect, new Color(0.12f, 0.14f, 0.12f, 0.86f), new Color(0.42f, 0.68f, 0.42f), 1);
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.78f, 0.95f, 0.78f);
            Widgets.LabelFit(rect.ContractedBy(4f), label);
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
        }

        /// <summary>
        /// True when an entry has actual LLM output ready for the production diary list.
        /// </summary>
        private static bool IsGenerated(DiaryEntryView entry)
        {
            return entry != null && !string.IsNullOrWhiteSpace(entry.GeneratedText);
        }

        /// <summary>
        /// True while an entry is still waiting on the background LLM generation pipeline.
        /// </summary>
        private static bool IsGenerating(DiaryEntryView entry)
        {
            return entry != null && entry.LlmStatus == DiaryEvent.PendingStatus;
        }

        /// <summary>
        /// Calculates the height needed for a single diary entry card, accounting for
        /// dynamic text wrapping of the generated diary text.
        /// </summary>
        private static float EntryHeight(DiaryEntryView entry, float width)
        {
            float innerWidth = width - 16f;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float textHeight = Text.CalcHeight(entry.GeneratedText, innerWidth);
            Text.Font = oldFont;

            return 48f + textHeight;
        }
    }
}
