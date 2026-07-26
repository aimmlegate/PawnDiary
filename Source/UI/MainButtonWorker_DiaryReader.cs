// Bottom main-bar entry point for alternative diary reader mode.
// MainButtonDef instantiates this worker by reflection; visibility is gated entirely by the saved
// mode setting so default-mode players keep the vanilla inspect-tab workflow.
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Shows and toggles the standalone diary reader from RimWorld's main button bar.
    /// </summary>
    public sealed class MainButtonWorker_DiaryReader : MainButtonWorker
    {
        /// <summary>
        /// True only while the player has enabled standalone reader mode.
        /// </summary>
        public override bool Visible
        {
            get { return base.Visible && DiaryUiRouter.ReaderWindowMode; }
        }

        /// <summary>
        /// Opens or closes the singleton standalone reader window.
        /// </summary>
        public override void Activate()
        {
            Dialog_DiaryReader.Toggle();
        }

        /// <summary>
        /// Lets vanilla draw the button, then adds colony-wide unread, writing, and failure overlays.
        /// </summary>
        public override void DoButton(Rect rect)
        {
            base.DoButton(rect);

            try
            {
                DiaryGameComponent component = DiaryGameComponent.Instance;
                if (component == null)
                {
                    return;
                }

                DiaryGameComponent.DiaryCommandStatus status = component.GlobalReaderStatus();
                if (status.HasNewPages)
                {
                    DiaryStatusOverlay.DrawUnreadUnderline(
                        new Rect(
                            rect.x + 8f,
                            rect.yMax - 4f,
                            Mathf.Max(0f, rect.width - 16f),
                            2f));
                }

                DiaryUiStyleDef style = DiaryJournalView.UiStyle;
                float availableBadgeWidth = Mathf.Max(0f, (rect.width - 12f) * 0.5f);
                float badgeWidth = Mathf.Min(
                    Mathf.Max(20f, style.statusBadgeWidth),
                    availableBadgeWidth);
                float badgeHeight = Mathf.Min(
                    Mathf.Max(14f, style.statusBadgeHeight),
                    Mathf.Max(0f, rect.height - 8f));
                if (status.IsWriting)
                {
                    DiaryStatusOverlay.DrawWritingBadge(
                        new Rect(rect.x + 4f, rect.y + 4f, badgeWidth, badgeHeight));
                }
                if (status.HasFailures)
                {
                    DiaryStatusOverlay.DrawFailureBadge(
                        new Rect(rect.xMax - badgeWidth - 4f, rect.y + 4f, badgeWidth, badgeHeight),
                        status.failedCount);
                }
            }
            catch (Exception e)
            {
                // Main-button rendering is global UI. A status-overlay failure must never break the
                // vanilla bottom bar or prevent the base Diary button from being clicked.
                Log.ErrorOnce(
                    "[Pawn Diary] Diary main-button status overlay draw failed: " + e,
                    0x7D1A0004);
            }
        }
    }
}
