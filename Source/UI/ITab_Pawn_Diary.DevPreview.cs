// Dev-only transient diary preview controls. These buttons render fake DiaryEntryView objects through
// the normal card path so formatting can be checked without writing mock events into the save.
using System.Collections.Generic;
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
        private enum DevDiaryPreviewKind
        {
            None,
            Plain,
            Speech,
            Staggered,
            Combat,
            Strange,
            Death
        }

        // The currently selected transient preview. It is UI state only: no DiaryEvent is registered
        // and nothing is saved, so closing the tab/game drops it.
        private DevDiaryPreviewKind devPreviewKind = DevDiaryPreviewKind.None;
        private DevDiaryPreviewKind cachedVisiblePreviewKind = DevDiaryPreviewKind.None;

        /// <summary>
        /// Draws compact dev-mode text buttons that switch the transient formatting preview.
        /// </summary>
        private void DrawDevPreviewButtons(Listing_Standard listing, Pawn pawn)
        {
            Rect row = listing.GetRect(ControlLineHeight);
            float gap = 4f;
            float buttonWidth = (row.width - gap * 6f) / 7f;
            float x = row.x;

            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewPlain", DevDiaryPreviewKind.Plain, true, pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewSpeech", DevDiaryPreviewKind.Speech, true, pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewStaggered", DevDiaryPreviewKind.Staggered, true, pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewCombat", DevDiaryPreviewKind.Combat, true, pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewStrange", DevDiaryPreviewKind.Strange, true, pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewDeath", DevDiaryPreviewKind.Death, true, pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), "PawnDiary.Tab.DevPreviewClear", DevDiaryPreviewKind.None, devPreviewKind != DevDiaryPreviewKind.None, pawn);

            TooltipHandler.TipRegion(row, "PawnDiary.Tab.DevPreviewTip".Translate());
        }

        private void DrawDevPreviewButton(Rect rect, string labelKey, DevDiaryPreviewKind kind, bool enabled, Pawn pawn)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = enabled;
            if (Widgets.ButtonText(rect, labelKey.Translate()))
            {
                SetDevPreviewKind(kind, pawn);
            }

            GUI.enabled = oldEnabled;
        }

        /// <summary>
        /// Inserts the transient preview into the visible entry cache before real saved entries.
        /// </summary>
        private void AddDevPreviewEntryIfNeeded(List<DiaryEntryView> entries, List<int> years, Pawn pawn)
        {
            if (devPreviewKind == DevDiaryPreviewKind.None || entries == null || years == null)
            {
                return;
            }

            DiaryEntryView preview = BuildDevPreviewEntry(pawn, devPreviewKind);
            entries.Add(preview);
            AddYearIfMissing(years, EntryYear(preview));
        }

        private void SetDevPreviewKind(DevDiaryPreviewKind kind, Pawn pawn)
        {
            devPreviewKind = kind;
            yearFilterPawnId = null;
            scrollPosition.y = 0f;
            cachedOrderedVisibleRevision = -1;

            if (kind != DevDiaryPreviewKind.None)
            {
                entryExpansionOverrides[DevPreviewEventId(pawn, kind) + "|" + DiaryEvent.InitiatorRole] = true;
            }
        }

        private static DiaryEntryView BuildDevPreviewEntry(Pawn pawn, DevDiaryPreviewKind kind)
        {
            string pawnName = pawn == null
                ? "PawnDiary.Dev.PreviewPawnFallback".Translate().Resolve()
                : pawn.LabelShortCap;
            string date = DevPreviewDate();
            string body = DevPreviewBody(kind, pawnName);
            string rawText = "PawnDiary.Dev.PreviewRawText".Translate(pawnName, DevPreviewButtonLabel(kind)).Resolve();
            string colorCue = DevPreviewColorCue(kind);
            string atmosphereCue = DevPreviewAtmosphereCue(kind);

            return new DiaryEntryView(
                int.MaxValue,
                date,
                rawText,
                body,
                DiaryEvent.CompleteStatus,
                string.Empty,
                string.Empty,
                "dev-preview",
                string.Empty,
                DevPreviewEventId(pawn, kind),
                DiaryEvent.InitiatorRole,
                "PawnDiary.Dev.PreviewLabel".Translate().Resolve(),
                colorCue,
                atmosphereCue,
                0,
                false,
                kind == DevDiaryPreviewKind.Strange,
                null,
                DevPreviewTitle(kind),
                false,
                body,
                DevPreviewDecorationContext(kind, colorCue, atmosphereCue));
        }

        private static string DevPreviewDate()
        {
            int ticksAbs = Find.TickManager == null ? 0 : Find.TickManager.TicksAbs;
            return GenDate.DateFullStringAt(ticksAbs, Vector2.zero);
        }

        private static string DevPreviewEventId(Pawn pawn, DevDiaryPreviewKind kind)
        {
            string pawnId = pawn == null ? "unknown" : pawn.GetUniqueLoadID();
            return "PawnDiary.DevPreview." + pawnId + "." + kind;
        }

        private static string DevPreviewBody(DevDiaryPreviewKind kind, string pawnName)
        {
            return DevPreviewBodyKey(kind).Translate(pawnName).Resolve();
        }

        private static string DevPreviewTitle(DevDiaryPreviewKind kind)
        {
            return DevPreviewTitleKey(kind).Translate().Resolve();
        }

        private static string DevPreviewButtonLabel(DevDiaryPreviewKind kind)
        {
            return DevPreviewButtonKey(kind).Translate().Resolve();
        }

        private static string DevPreviewBodyKey(DevDiaryPreviewKind kind)
        {
            switch (kind)
            {
                case DevDiaryPreviewKind.Speech:
                    return "PawnDiary.Dev.PreviewBodySpeech";
                case DevDiaryPreviewKind.Staggered:
                    return "PawnDiary.Dev.PreviewBodyStaggered";
                case DevDiaryPreviewKind.Combat:
                    return "PawnDiary.Dev.PreviewBodyCombat";
                case DevDiaryPreviewKind.Strange:
                    return "PawnDiary.Dev.PreviewBodyStrange";
                case DevDiaryPreviewKind.Death:
                    return "PawnDiary.Dev.PreviewBodyDeath";
                default:
                    return "PawnDiary.Dev.PreviewBodyPlain";
            }
        }

        private static string DevPreviewTitleKey(DevDiaryPreviewKind kind)
        {
            switch (kind)
            {
                case DevDiaryPreviewKind.Speech:
                    return "PawnDiary.Dev.PreviewTitleSpeech";
                case DevDiaryPreviewKind.Staggered:
                    return "PawnDiary.Dev.PreviewTitleStaggered";
                case DevDiaryPreviewKind.Combat:
                    return "PawnDiary.Dev.PreviewTitleCombat";
                case DevDiaryPreviewKind.Strange:
                    return "PawnDiary.Dev.PreviewTitleStrange";
                case DevDiaryPreviewKind.Death:
                    return "PawnDiary.Dev.PreviewTitleDeath";
                default:
                    return "PawnDiary.Dev.PreviewTitlePlain";
            }
        }

        private static string DevPreviewButtonKey(DevDiaryPreviewKind kind)
        {
            switch (kind)
            {
                case DevDiaryPreviewKind.Speech:
                    return "PawnDiary.Tab.DevPreviewSpeech";
                case DevDiaryPreviewKind.Staggered:
                    return "PawnDiary.Tab.DevPreviewStaggered";
                case DevDiaryPreviewKind.Combat:
                    return "PawnDiary.Tab.DevPreviewCombat";
                case DevDiaryPreviewKind.Strange:
                    return "PawnDiary.Tab.DevPreviewStrange";
                case DevDiaryPreviewKind.Death:
                    return "PawnDiary.Tab.DevPreviewDeath";
                case DevDiaryPreviewKind.None:
                    return "PawnDiary.Tab.DevPreviewClear";
                default:
                    return "PawnDiary.Tab.DevPreviewPlain";
            }
        }

        private static string DevPreviewColorCue(DevDiaryPreviewKind kind)
        {
            switch (kind)
            {
                case DevDiaryPreviewKind.Staggered:
                    return "daze";
                case DevDiaryPreviewKind.Combat:
                    return "combat";
                case DevDiaryPreviewKind.Strange:
                    return "strangeChat";
                case DevDiaryPreviewKind.Death:
                    return "white";
                default:
                    return "quiet";
            }
        }

        private static string DevPreviewAtmosphereCue(DevDiaryPreviewKind kind)
        {
            if (kind == DevDiaryPreviewKind.Strange)
            {
                return DiaryEntryView.AtmosphereUnsettled;
            }

            return kind == DevDiaryPreviewKind.Death ? DiaryEntryView.AtmosphereMemorial : string.Empty;
        }

        private static DiaryTextDecorationContext DevPreviewDecorationContext(DevDiaryPreviewKind kind, string colorCue, string atmosphereCue)
        {
            DiaryTextDecorationContext context = new DiaryTextDecorationContext
            {
                povRole = DiaryEvent.InitiatorRole,
                defName = "PawnDiary_DevPreview",
                colorCue = colorCue,
                atmosphereCue = atmosphereCue,
                domain = "DevPreview",
                gameContext = "dev_preview=true; preview_kind=" + kind
            };
            context.eventTags.Add("dev_preview");

            if (kind == DevDiaryPreviewKind.Staggered)
            {
                context.hediffs.Add(new DiaryTextDecorationHediffFact
                {
                    defName = "AlcoholHigh",
                    label = "drunk",
                    severity = 0.7f,
                    visible = true
                });
            }

            return context;
        }
    }
}
