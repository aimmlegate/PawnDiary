// Bottom main-bar entry point for alternative diary reader mode.
// MainButtonDef instantiates this worker by reflection; visibility is gated entirely by the saved
// mode setting so default-mode players keep the vanilla inspect-tab workflow.
using RimWorld;

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
    }
}
