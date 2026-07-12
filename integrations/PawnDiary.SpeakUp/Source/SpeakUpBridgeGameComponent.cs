// Per-game lifetime boundary for the adapter's transient Talk accumulators. SpeakUp does not save its
// CurrentTalks/Scheduled lists, so Pawn Diary must not carry a half-finished conversation across a load
// either. RimWorld auto-instantiates this GameComponent; no XML or Harmony registration is required.
using Verse;

namespace PawnDiarySpeakUp
{
    /// <summary>Clears process-static conversation state whenever a game finishes initializing.</summary>
    public class SpeakUpBridgeGameComponent : GameComponent
    {
        /// <summary>Required constructor; RimWorld supplies the current game.</summary>
        public SpeakUpBridgeGameComponent(Game game)
        {
        }

        /// <summary>Drops in-flight Talk observations silently after new-game or save-load setup.</summary>
        public override void FinalizeInit()
        {
            TalkCapture.ResetTransient();
        }
    }
}
