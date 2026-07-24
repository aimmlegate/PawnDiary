// Dev-only transient diary preview controls. These buttons render fake DiaryEntryView objects through
// the normal card path so formatting can be checked without writing mock events into the save.
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Dev-preview helpers for the reusable diary journal renderer.
    /// </summary>
    internal sealed partial class DiaryJournalView
    {
        internal enum DevDiaryPreviewKind
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
        private static string pendingDevPreviewPawnId;
        private static DevDiaryPreviewKind pendingDevPreviewKind = DevDiaryPreviewKind.None;
        private static int pendingDevPreviewVersion;
        private int appliedDevPreviewVersion;

        /// <summary>
        /// Dev panel entry point: request a transient card preview in the pawn's Diary tab.
        /// </summary>
        internal static void RequestDevPreviewForDev(Pawn pawn, DevDiaryPreviewKind kind)
        {
            if (pawn == null)
            {
                return;
            }

            pendingDevPreviewPawnId = pawn.GetUniqueLoadID();
            pendingDevPreviewKind = kind;
            pendingDevPreviewVersion++;

            if (!DiaryUiRouter.ReaderWindowMode
                && pawn.Spawned
                && Find.Selector != null
                && !Find.Selector.IsSelected(pawn))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn, true, false);
            }

            DiaryUiRouter.OpenDiaryFor(pawn);
        }

        // The 14 transient formatting previews, in display order. Rendered as a 3-column grid (below)
        // so the labels stay readable inside the narrow filter panel; the old 7-per-row layout clipped
        // them to unreadable slivers. Keys and kinds are parallel arrays — keep them in step.
        private static readonly string[] DevPreviewButtonKeys =
        {
            "PawnDiary.Tab.DevPreviewPlain",
            "PawnDiary.Tab.DevPreviewMarkdown",
            "PawnDiary.Tab.DevPreviewSpeech",
            "PawnDiary.Tab.DevPreviewStaggered",
            "PawnDiary.Tab.DevPreviewCombat",
            "PawnDiary.Tab.DevPreviewSocialFight",
            "PawnDiary.Tab.DevPreviewDeath",
            "PawnDiary.Tab.DevPreviewMental",
            "PawnDiary.Tab.DevPreviewDark",
            "PawnDiary.Tab.DevPreviewStrange",
            "PawnDiary.Tab.DevPreviewLinked",
            "PawnDiary.Tab.DevPreviewWriting",
            "PawnDiary.Tab.DevPreviewTitle",
            "PawnDiary.Tab.DevPreviewClear",
        };

        private static readonly DevDiaryPreviewKind[] DevPreviewButtonKinds =
        {
            DevDiaryPreviewKind.Plain,
            DevDiaryPreviewKind.Markdown,
            DevDiaryPreviewKind.Speech,
            DevDiaryPreviewKind.Staggered,
            DevDiaryPreviewKind.Combat,
            DevDiaryPreviewKind.SocialFight,
            DevDiaryPreviewKind.Death,
            DevDiaryPreviewKind.Mental,
            DevDiaryPreviewKind.Dark,
            DevDiaryPreviewKind.Strange,
            DevDiaryPreviewKind.Linked,
            DevDiaryPreviewKind.Writing,
            DevDiaryPreviewKind.TitlePending,
            DevDiaryPreviewKind.None,
        };

        private const int DevPreviewButtonColumns = 3;

        /// <summary>
        /// Height the preview grid reserves, so the dev-block height estimate can stay honest.
        /// </summary>
        private static float DevPreviewButtonsHeight()
        {
            int rows = Mathf.CeilToInt(DevPreviewButtonKeys.Length / (float)DevPreviewButtonColumns);
            return rows * ControlLineHeight + Mathf.Max(0, rows - 1) * ControlGap;
        }

        /// <summary>
        /// Draws the transient formatting-preview buttons as a compact 3-column grid in the small Tiny
        /// font, so every label fits inside the narrow filter panel.
        /// </summary>
        private void DrawDevPreviewButtons(Listing_Standard listing, Pawn pawn)
        {
            int count = DevPreviewButtonKeys.Length;
            int cols = DevPreviewButtonColumns;
            float gap = 4f;
            Rect block = listing.GetRect(DevPreviewButtonsHeight());
            float colWidth = (block.width - gap * (cols - 1)) / cols;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                Rect buttonRect = new Rect(
                    block.x + col * (colWidth + gap),
                    block.y + row * (ControlLineHeight + ControlGap),
                    colWidth,
                    ControlLineHeight);
                DrawDevPreviewButton(
                    buttonRect,
                    DevPreviewButtonKeys[i],
                    DevPreviewButtonKinds[i],
                    DevPreviewButtonEnabled(DevPreviewButtonKinds[i]),
                    pawn);
            }
            Text.Font = oldFont;

            TooltipHandler.TipRegion(block, "PawnDiary.Tab.DevPreviewTip".Translate());
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

        private void SetDevPreviewKind(DevDiaryPreviewKind kind, Pawn pawn)
        {
            devPreviewKind = kind;
            yearFilterPawnId = null;
            scrollPosition.y = 0f;
            visibleEntriesCache.InvalidateOrdering();

            if (kind != DevDiaryPreviewKind.None)
            {
                entryExpansionOverrides[DevPreviewEventId(pawn, kind) + "|" + DiaryEvent.InitiatorRole] = true;
            }
        }

        private void ApplyPendingDevPreview(Pawn pawn)
        {
            if (pawn == null || appliedDevPreviewVersion == pendingDevPreviewVersion)
            {
                return;
            }

            if (!string.Equals(pawn.GetUniqueLoadID(), pendingDevPreviewPawnId, System.StringComparison.Ordinal))
            {
                return;
            }

            appliedDevPreviewVersion = pendingDevPreviewVersion;
            SetDevPreviewKind(pendingDevPreviewKind, pawn);
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
                DevPreviewButtonLabel(kind),
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
