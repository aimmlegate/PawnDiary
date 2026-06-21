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
            SocialFight,
            Mental,
            Dark,
            Strange,
            Death,
            Linked,
            Writing,
            TitlePending,
            Markdown
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
            Rect block = listing.GetRect(ControlLineHeight * 2f + ControlGap);
            float gap = 4f;
            float buttonWidth = (block.width - gap * 6f) / 7f;
            Rect rowOne = new Rect(block.x, block.y, block.width, ControlLineHeight);
            Rect rowTwo = new Rect(block.x, rowOne.yMax + ControlGap, block.width, ControlLineHeight);

            DrawDevPreviewButtonRow(
                rowOne,
                buttonWidth,
                gap,
                pawn,
                "PawnDiary.Tab.DevPreviewPlain", DevDiaryPreviewKind.Plain,
                "PawnDiary.Tab.DevPreviewMarkdown", DevDiaryPreviewKind.Markdown,
                "PawnDiary.Tab.DevPreviewSpeech", DevDiaryPreviewKind.Speech,
                "PawnDiary.Tab.DevPreviewStaggered", DevDiaryPreviewKind.Staggered,
                "PawnDiary.Tab.DevPreviewCombat", DevDiaryPreviewKind.Combat,
                "PawnDiary.Tab.DevPreviewSocialFight", DevDiaryPreviewKind.SocialFight,
                "PawnDiary.Tab.DevPreviewDeath", DevDiaryPreviewKind.Death);

            DrawDevPreviewButtonRow(
                rowTwo,
                buttonWidth,
                gap,
                pawn,
                "PawnDiary.Tab.DevPreviewMental", DevDiaryPreviewKind.Mental,
                "PawnDiary.Tab.DevPreviewDark", DevDiaryPreviewKind.Dark,
                "PawnDiary.Tab.DevPreviewStrange", DevDiaryPreviewKind.Strange,
                "PawnDiary.Tab.DevPreviewLinked", DevDiaryPreviewKind.Linked,
                "PawnDiary.Tab.DevPreviewWriting", DevDiaryPreviewKind.Writing,
                "PawnDiary.Tab.DevPreviewTitle", DevDiaryPreviewKind.TitlePending,
                "PawnDiary.Tab.DevPreviewClear", DevDiaryPreviewKind.None);

            TooltipHandler.TipRegion(block, "PawnDiary.Tab.DevPreviewTip".Translate());
        }

        private void DrawDevPreviewButtonRow(
            Rect row,
            float buttonWidth,
            float gap,
            Pawn pawn,
            string key1,
            DevDiaryPreviewKind kind1,
            string key2,
            DevDiaryPreviewKind kind2,
            string key3,
            DevDiaryPreviewKind kind3,
            string key4,
            DevDiaryPreviewKind kind4,
            string key5,
            DevDiaryPreviewKind kind5,
            string key6,
            DevDiaryPreviewKind kind6,
            string key7,
            DevDiaryPreviewKind kind7)
        {
            float x = row.x;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key1, kind1, DevPreviewButtonEnabled(kind1), pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key2, kind2, DevPreviewButtonEnabled(kind2), pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key3, kind3, DevPreviewButtonEnabled(kind3), pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key4, kind4, DevPreviewButtonEnabled(kind4), pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key5, kind5, DevPreviewButtonEnabled(kind5), pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key6, kind6, DevPreviewButtonEnabled(kind6), pawn);
            x += buttonWidth + gap;
            DrawDevPreviewButton(new Rect(x, row.y, buttonWidth, row.height), key7, kind7, DevPreviewButtonEnabled(kind7), pawn);
        }

        private bool DevPreviewButtonEnabled(DevDiaryPreviewKind kind)
        {
            return kind != DevDiaryPreviewKind.None || devPreviewKind != DevDiaryPreviewKind.None;
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
            bool writing = kind == DevDiaryPreviewKind.Writing;
            bool titlePending = kind == DevDiaryPreviewKind.TitlePending;
            LinkedEntryView linkedEntry = kind == DevDiaryPreviewKind.Linked ? BuildDevPreviewLinkedEntry(pawn, kind) : null;

            return new DiaryEntryView(
                int.MaxValue,
                date,
                rawText,
                writing ? string.Empty : body,
                writing ? DiaryEvent.PendingStatus : DiaryEvent.CompleteStatus,
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
                DevPreviewImportant(kind),
                linkedEntry,
                titlePending ? string.Empty : DevPreviewTitle(kind),
                titlePending,
                body,
                DevPreviewDecorationContext(kind, colorCue, atmosphereCue));
        }

        private static LinkedEntryView BuildDevPreviewLinkedEntry(Pawn pawn, DevDiaryPreviewKind kind)
        {
            return new LinkedEntryView(
                string.Empty,
                "PawnDiary.Dev.PreviewLinkedPawn".Translate().Resolve(),
                DiaryEvent.RecipientRole,
                DevPreviewEventId(pawn, kind),
                "PawnDiary.Dev.PreviewLinkedText".Translate().Resolve(),
                true,
                "PawnDiary.Dev.PreviewLinkedTitle".Translate().Resolve());
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
                case DevDiaryPreviewKind.SocialFight:
                    return "PawnDiary.Dev.PreviewBodySocialFight";
                case DevDiaryPreviewKind.Mental:
                    return "PawnDiary.Dev.PreviewBodyMental";
                case DevDiaryPreviewKind.Dark:
                    return "PawnDiary.Dev.PreviewBodyDark";
                case DevDiaryPreviewKind.Strange:
                    return "PawnDiary.Dev.PreviewBodyStrange";
                case DevDiaryPreviewKind.Death:
                    return "PawnDiary.Dev.PreviewBodyDeath";
                case DevDiaryPreviewKind.Linked:
                    return "PawnDiary.Dev.PreviewBodyLinked";
                case DevDiaryPreviewKind.Writing:
                    return "PawnDiary.Dev.PreviewBodyWriting";
                case DevDiaryPreviewKind.TitlePending:
                    return "PawnDiary.Dev.PreviewBodyTitle";
                case DevDiaryPreviewKind.Markdown:
                    return "PawnDiary.Dev.PreviewBodyMarkdown";
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
                case DevDiaryPreviewKind.SocialFight:
                    return "PawnDiary.Dev.PreviewTitleSocialFight";
                case DevDiaryPreviewKind.Mental:
                    return "PawnDiary.Dev.PreviewTitleMental";
                case DevDiaryPreviewKind.Dark:
                    return "PawnDiary.Dev.PreviewTitleDark";
                case DevDiaryPreviewKind.Strange:
                    return "PawnDiary.Dev.PreviewTitleStrange";
                case DevDiaryPreviewKind.Death:
                    return "PawnDiary.Dev.PreviewTitleDeath";
                case DevDiaryPreviewKind.Linked:
                    return "PawnDiary.Dev.PreviewTitleLinked";
                case DevDiaryPreviewKind.Writing:
                    return "PawnDiary.Dev.PreviewTitleWriting";
                case DevDiaryPreviewKind.TitlePending:
                    return "PawnDiary.Dev.PreviewTitlePending";
                case DevDiaryPreviewKind.Markdown:
                    return "PawnDiary.Dev.PreviewTitleMarkdown";
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
                case DevDiaryPreviewKind.SocialFight:
                    return "PawnDiary.Tab.DevPreviewSocialFight";
                case DevDiaryPreviewKind.Mental:
                    return "PawnDiary.Tab.DevPreviewMental";
                case DevDiaryPreviewKind.Dark:
                    return "PawnDiary.Tab.DevPreviewDark";
                case DevDiaryPreviewKind.Strange:
                    return "PawnDiary.Tab.DevPreviewStrange";
                case DevDiaryPreviewKind.Death:
                    return "PawnDiary.Tab.DevPreviewDeath";
                case DevDiaryPreviewKind.Linked:
                    return "PawnDiary.Tab.DevPreviewLinked";
                case DevDiaryPreviewKind.Writing:
                    return "PawnDiary.Tab.DevPreviewWriting";
                case DevDiaryPreviewKind.TitlePending:
                    return "PawnDiary.Tab.DevPreviewTitle";
                case DevDiaryPreviewKind.Markdown:
                    return "PawnDiary.Tab.DevPreviewMarkdown";
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
                case DevDiaryPreviewKind.SocialFight:
                    return "socialFight";
                case DevDiaryPreviewKind.Mental:
                    return "mentalBreak";
                case DevDiaryPreviewKind.Dark:
                    return "extremeDark";
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
            if (kind == DevDiaryPreviewKind.Strange || kind == DevDiaryPreviewKind.Dark)
            {
                return DiaryEntryView.AtmosphereUnsettled;
            }

            if (kind == DevDiaryPreviewKind.Mental)
            {
                return DiaryEntryView.AtmosphereFractured;
            }

            return kind == DevDiaryPreviewKind.Death ? DiaryEntryView.AtmosphereMemorial : string.Empty;
        }

        private static bool DevPreviewImportant(DevDiaryPreviewKind kind)
        {
            return kind == DevDiaryPreviewKind.Combat
                || kind == DevDiaryPreviewKind.SocialFight
                || kind == DevDiaryPreviewKind.Mental
                || kind == DevDiaryPreviewKind.Dark
                || kind == DevDiaryPreviewKind.Strange
                || kind == DevDiaryPreviewKind.Death;
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
