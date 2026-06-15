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
    // Diary window/gizmo and is injected as the final pawn inspector tab at startup.
    public class ITab_Pawn_Diary : ITab
    {
        // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        // Vertical space reserved for the per-pawn generation toggle above the scroll view.
        private const float ControlsHeight = 32f;
        private const float EntryTitleHeight = 28f;
        private const float EntryTextTop = 38f;
        private const float EntryBottomPadding = 10f;
        private const float RoleplayLineGap = 3f;
        private const float RoleplayParagraphGap = 6f;

        private static readonly Color ImportantColor = new Color(0.96f, 0.62f, 0.22f);
        private static readonly Color QuietColor = new Color(0.42f, 0.48f, 0.52f);
        private static readonly Color NarrativeColor = new Color(0.78f, 0.78f, 0.72f);
        private static readonly Color FallbackDialogueColor = new Color(0.58f, 0.80f, 1f);

        // Unity scroll position; persists across frames so the user's scroll offset isn't lost on redraw.
        private Vector2 scrollPosition;
        // Set by the Social-tab click patch before opening this tab. FillTab consumes it once
        // the relevant pawn's generated entry list is available.
        private static string pendingScrollPawnId;
        private static string pendingScrollEventId;

        public ITab_Pawn_Diary()
        {
            size = new Vector2(600f, 500f);
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

            TryApplyPendingScroll(pawn, ordered, viewWidth, viewHeight, outRect.height);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            Color dialogueColor = PreferredDialogueColor(pawn);
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                float height = EntryHeight(entry, viewRect.width);
                Rect entryRect = new Rect(0f, curY, viewRect.width, height);

                Widgets.DrawMenuSection(entryRect);
                Widgets.DrawHighlightIfMouseover(entryRect);

                Rect titleRect = new Rect(entryRect.x, entryRect.y, entryRect.width, EntryTitleHeight);
                Widgets.DrawTitleBG(titleRect);
                Widgets.DrawBoxSolid(new Rect(entryRect.x + 1f, entryRect.y + 1f, 5f, entryRect.height - 2f), ImportanceColor(entry));

                GUI.color = new Color(0.86f, 0.86f, 0.86f);
                Widgets.LabelFit(new Rect(entryRect.x + 12f, entryRect.y + 5f, entryRect.width - 20f, 22f), EntryHeader(entry));
                GUI.color = Color.white;

                Rect textRect = new Rect(entryRect.x + 12f, entryRect.y + EntryTextTop, entryRect.width - 20f, entryRect.height - EntryTextTop - EntryBottomPadding);
                DrawRoleplayText(textRect, entry.GeneratedText, dialogueColor);

                curY += height + 8f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Consumes a pending Social-tab jump request by placing the target card near the top
        /// of the scroll view. If the entry disappeared before redraw, clear the stale request.
        /// </summary>
        private void TryApplyPendingScroll(Pawn pawn, List<DiaryEntryView> ordered, float viewWidth, float viewHeight, float outHeight)
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

                curY += EntryHeight(entry, viewWidth) + 8f;
            }

            ClearPendingScrollRequest();
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
        /// Produces the compact header shown on each entry card: date, group, and importance.
        /// </summary>
        private static string EntryHeader(DiaryEntryView entry)
        {
            string importance = entry.Important
                ? "PawnDiary.Tab.Important".Translate()
                : "PawnDiary.Tab.Quiet".Translate();

            if (string.IsNullOrWhiteSpace(entry.GroupLabel))
            {
                return entry.Date + " - " + importance;
            }

            return entry.Date + " - " + entry.GroupLabel + " - " + importance;
        }

        /// <summary>
        /// Returns the color strip used to mark whether an entry belongs to an important group.
        /// </summary>
        private static Color ImportanceColor(DiaryEntryView entry)
        {
            return entry != null && entry.Important ? ImportantColor : QuietColor;
        }

        /// <summary>
        /// Uses the pawn's RimWorld favorite color for dialogue, brightened enough for dark UI.
        /// </summary>
        private static Color PreferredDialogueColor(Pawn pawn)
        {
            Color color = pawn?.story?.favoriteColor != null ? pawn.story.favoriteColor.color : FallbackDialogueColor;
            color.a = 1f;

            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (max < 0.55f)
            {
                float lift = 0.55f - max;
                color.r = Mathf.Clamp01(color.r + lift);
                color.g = Mathf.Clamp01(color.g + lift);
                color.b = Mathf.Clamp01(color.b + lift);
            }

            return color;
        }

        /// <summary>
        /// Draws generated text in a light roleplay style: narration is muted/italic, while
        /// dialogue-looking lines are bold and colored with the pawn's preferred color.
        /// </summary>
        private static void DrawRoleplayText(Rect rect, string text, Color dialogueColor)
        {
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            float curY = rect.y;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    curY += RoleplayParagraphGap;
                    continue;
                }

                bool dialogue = IsDialogueLine(line);
                GUIStyle style = RoleplayStyle(dialogue, dialogueColor);
                float height = style.CalcHeight(new GUIContent(line), rect.width);
                GUI.Label(new Rect(rect.x, curY, rect.width, height), line, style);
                curY += height + RoleplayLineGap;
            }

            GUI.color = oldColor;
            Text.Font = oldFont;
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
            float textHeight = RoleplayTextHeight(entry.GeneratedText, innerWidth);
            Text.Font = oldFont;

            return EntryTextTop + textHeight + EntryBottomPadding;
        }

        /// <summary>
        /// Measures the same roleplay lines that DrawRoleplayText renders.
        /// </summary>
        private static float RoleplayTextHeight(string text, float width)
        {
            float height = 0f;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    height += RoleplayParagraphGap;
                    continue;
                }

                GUIStyle style = RoleplayStyle(IsDialogueLine(line), FallbackDialogueColor);
                height += style.CalcHeight(new GUIContent(line), width) + RoleplayLineGap;
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
        /// Heuristic for lines that should read as dialogue in a roleplay log.
        /// </summary>
        private static bool IsDialogueLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.TrimStart();
            return trimmed.StartsWith("\"", StringComparison.Ordinal)
                || trimmed.StartsWith("'", StringComparison.Ordinal)
                || trimmed.StartsWith("-\"", StringComparison.Ordinal)
                || trimmed.StartsWith("- '", StringComparison.Ordinal)
                || trimmed.IndexOf('"') >= 0
                || trimmed.IndexOf(':') > 0 && trimmed.IndexOf(':') <= 24;
        }

        /// <summary>
        /// Creates an isolated GUIStyle so line colors/font styles do not leak into other RimWorld UI.
        /// </summary>
        private static GUIStyle RoleplayStyle(bool dialogue, Color dialogueColor)
        {
            GUIStyle style = new GUIStyle(Text.CurFontStyle)
            {
                wordWrap = true,
                fontStyle = dialogue ? FontStyle.BoldAndItalic : FontStyle.Italic
            };
            style.normal.textColor = dialogue ? dialogueColor : NarrativeColor;
            return style;
        }
    }
}
