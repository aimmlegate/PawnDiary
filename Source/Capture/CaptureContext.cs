// Snapshot of impure "should we even consider this event?" facts that DiaryGameComponent pre-computes
// from live RimWorld state before handing the event to the pure Decider. The Decider never touches
// the DefDatabase, Settings, or the tick manager — it only reads these primitives. This is the seam
// that keeps the decision logic unit-testable.
//
// Adding a new fact here is rare (it must apply to every event source). Adding a new tunable per
// source goes on that source's policy type (see ThoughtCapturePolicy) or its payload, not here.
namespace PawnDiary.Capture
{
    internal class CaptureContext
    {
        /// <summary>True if the pawn qualifies for diary tracking (humanlike colonist). Caller
        /// computes this from Pawn before invoking the catalog.</summary>
        public bool Eligible;

        /// <summary>True if the user has not disabled this specific def in settings (e.g. the
        /// per-thought toggle). Caller computes via PawnDiaryMod.Settings.IsXxxEnabled.</summary>
        public bool UserEnabled;

        /// <summary>True if the signal-policy Def for this source is enabled (e.g. DiarySignalPolicies
        /// .Enabled(Thought)). Sources without a signal policy leave this true.</summary>
        public bool SignalEnabled;

        /// <summary>True if the ambient routing policy for this source is enabled. Sources without
        /// ambient routing leave this true.</summary>
        public bool AmbientSignalEnabled;

        /// <summary>Current game tick (Find.TickManager.TicksGame). Used for any time-based decision;
        /// the pure Decider accepts it as an int so it does not need RimWorld.</summary>
        public int Now;
    }
}
