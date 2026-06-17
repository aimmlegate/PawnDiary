// Expand/collapse helpers for diary entry cards. Split from ITab_Pawn_Diary.cs with no behavior change.
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
        /// Draws a closed diary page as a deliberate compact row: one bordered header, one accent
        /// strip, and no body tint/rule. This keeps collapsed histories readable instead of looking
        /// like clipped full cards.
        /// </summary>
        private static void DrawCollapsedEntry(DiaryEntryView entry, Rect rect, Color accent, bool expanded, float expansionBlend)
        {

            Widgets.DrawMenuSection(rect);

            Rect titleRect = new Rect(rect.x, rect.y, rect.width, EntryTitleHeight);

            Widgets.DrawTitleBG(titleRect);

            Widgets.DrawHighlightIfMouseover(rect);



            Rect accentRect = new Rect(rect.x + 1f, rect.y + 1f, EntryAccentWidth, rect.height - 2f);

            Widgets.DrawBoxSolid(accentRect, accent);

            Widgets.DrawBoxSolid(new Rect(accentRect.xMax, accentRect.y, 1f, accentRect.height), AccentHighlightColor);

            Widgets.DrawBoxSolid(

                new Rect(

                    rect.x + EntryAccentWidth + 8f,

                    rect.y + EntryTitleHeight,

                    Mathf.Max(0f, rect.width - EntryAccentWidth - 20f),

                    1f),

                HeaderRuleColor);



            Rect groupRect = GroupLabelRect(titleRect, entry?.GroupLabel);

            if (groupRect.width > 0f)
            {

                DrawGroupLabel(groupRect, entry.GroupLabel, accent);

            }



            float headerRight = groupRect.width > 0f ? groupRect.x - 6f : rect.xMax - 8f;

            Rect headerRect = new Rect(

                rect.x + 34f,

                rect.y + 5f,

                Mathf.Max(80f, headerRight - rect.x - 34f),

                22f);

            DrawEntryHeader(headerRect, entry, accent);

            DrawExpansionIndicator(titleRect, expanded, expansionBlend, accent);



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

                if (entryExpansionBlend.Count >= MaxExpansionBlendEntries)
                {

                    entryExpansionBlend.Clear();

                }



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
    }
}
