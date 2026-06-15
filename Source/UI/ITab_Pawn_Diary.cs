// The inspector tab (UI) that renders the selected pawn's diary next to the vanilla Log tab.
// RimWorld calls FillTab() to draw it using immediate-mode GUI (the whole tab is re-emitted each
// frame). It reads entries via DiaryGameComponent.EntriesFor. See CSHARP-NOTES.md ("lifecycle").
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // Inspector tab that shows the selected pawn's diary, sitting next to the vanilla
    // Log tab. Replaces the old standalone Diary window/gizmo.
    public class ITab_Pawn_Diary : ITab
    {
        // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        // Vertical space reserved for the enable/persona controls above the scroll view.
        private const float ControlsHeight = 116f;

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

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 34f), "PawnDiary.Tab.DiaryHeader".Translate(pawn.LabelShortCap));
            Text.Font = GameFont.Small;

            Rect controlsRect = new Rect(rect.x, rect.y + 36f, rect.width, ControlsHeight);
            DrawPawnControls(pawn, component, controlsRect);

            // The controls are part of the diary tab, so reserve fixed space before the scroll view.
            float entriesY = controlsRect.yMax + 8f;
            Rect outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);
            IReadOnlyList<DiaryEntryView> entries = component?.EntriesFor(pawn) ?? EmptyList;

            if (entries.Count == 0)
            {
                Widgets.Label(outRect, "PawnDiary.Tab.NoEntries".Translate());
                return;
            }

            // Newest entries first for diary-style reading order.
            List<DiaryEntryView> ordered = entries.OrderByDescending(entry => entry.Tick).ToList();
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
                string status = entry.StatusText;
                string header = string.IsNullOrWhiteSpace(status) ? entry.Date : $"{entry.Date} ({status})";

                Widgets.Label(new Rect(entryRect.x + 8f, entryRect.y + 6f, entryRect.width - 16f, 22f), header);
                float textHeight = Text.CalcHeight(entry.DisplayText, entryRect.width - 16f);
                Widgets.Label(new Rect(entryRect.x + 8f, entryRect.y + 30f, entryRect.width - 16f, textHeight), entry.DisplayText);

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.72f, 0.72f, 0.72f);
                Widgets.Label(new Rect(entryRect.x + 8f, entryRect.y + 36f + textHeight, entryRect.width - 16f, entryRect.height - textHeight - 44f), entry.DebugText);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                curY += height + 8f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Renders the enable-toggle and persona-picker controls above the diary entries.
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

            // Persona options come from DiaryPersonaDefs.xml so new presets can be added without
            // changing UI code.
            // The persona currently assigned to this pawn.
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

            string rule = persona?.rule ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rule))
            {
                // Show the active rule inline so players can inspect the style without opening XML.
                GameFont oldFont = Text.Font;
                Text.Font = GameFont.Tiny;
                Widgets.Label(listing.GetRect(40f), rule);
                Text.Font = oldFont;
            }

            listing.End();
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
        /// Calculates the height needed for a single diary entry card, accounting for
        /// dynamic text wrapping of both display and debug text.
        /// </summary>
        private static float EntryHeight(DiaryEntryView entry, float width)
        {
            float innerWidth = width - 16f;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float textHeight = Text.CalcHeight(entry.DisplayText, innerWidth);
            Text.Font = GameFont.Tiny;
            float debugHeight = Text.CalcHeight(entry.DebugText, innerWidth);
            Text.Font = oldFont;

            return 54f + textHeight + debugHeight;
        }
    }
}
