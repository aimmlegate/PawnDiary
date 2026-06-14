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
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        private const float ControlsHeight = 116f;

        private Vector2 scrollPosition;

        public ITab_Pawn_Diary()
        {
            size = new Vector2(600f, 500f);
            labelKey = "PawnDiaryTabLabel";
        }

        public override bool IsVisible
        {
            get { return PawnToShow() != null; }
        }

        private Pawn PawnToShow()
        {
            Pawn pawn = SelPawn;
            if (pawn == null)
            {
                Corpse corpse = SelThing as Corpse;
                pawn = corpse?.InnerPawn;
            }

            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            {
                return null;
            }

            return pawn;
        }

        protected override void FillTab()
        {
            Pawn pawn = PawnToShow();
            if (pawn == null)
            {
                return;
            }

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            DiaryGameComponent component = DiaryGameComponent.Current;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 34f), pawn.LabelShortCap + "'s Diary");
            Text.Font = GameFont.Small;

            Rect controlsRect = new Rect(rect.x, rect.y + 36f, rect.width, ControlsHeight);
            DrawPawnControls(pawn, component, controlsRect);

            // The controls are part of the diary tab, so reserve fixed space before the scroll view.
            float entriesY = controlsRect.yMax + 8f;
            Rect outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);
            IReadOnlyList<DiaryEntryView> entries = component?.EntriesFor(pawn) ?? EmptyList;

            if (entries.Count == 0)
            {
                Widgets.Label(outRect, "No interactions have been recorded yet.");
                return;
            }

            List<DiaryEntryView> ordered = entries.OrderByDescending(entry => entry.Tick).ToList();
            float viewWidth = outRect.width - 16f;
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
                "Generate diary entries for this pawn",
                ref enabled,
                "When disabled, events are still recorded here, but no LLM request is sent for this pawn.");
            if (enabled != before)
            {
                component.SetDiaryGenerationEnabled(pawn, enabled);
            }

            // Persona options come from DiaryPersonaDefs.xml so new presets can be added without
            // changing UI code.
            DiaryPersonaDef persona = component.PersonaFor(pawn);
            if (listing.ButtonText("Persona: " + PersonaLabel(persona)))
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

        private static string PersonaLabel(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "default";
            }

            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;
        }

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
