// The one front door for the whole event system. Every Harmony patch that used to call
// `DiaryGameComponent.Current?.RecordXxx(...)` now calls `DiaryEvents.Submit(new XxxSignal(...))`.
// This static class is intentionally tiny: it only forwards to the live component's dispatcher and
// no-ops when there is no game loaded (the same null-guard the old `?.RecordXxx` calls had).
//
// Keeping a single Submit entry point means there is exactly one place to read to see how an event
// flows from "captured" to "generated", and exactly one place to add cross-cutting behavior later
// (telemetry, a dev-tab live feed, global rate limiting) without touching every source.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Static façade for submitting captured game events into the diary pipeline. Safe to call from a
    /// Harmony patch at any time: it no-ops when no game is loaded.
    /// </summary>
    public static class DiaryEvents
    {
        /// <summary>Submits a single captured event (solo or pairwise).</summary>
        public static void Submit(DiarySignal signal)
        {
            if (signal == null)
            {
                return;
            }

            DiaryGameComponent.Current?.Dispatch(signal);
        }

        /// <summary>Submits a colony-wide event that fans out to one entry per eligible colonist.</summary>
        public static void Submit(DiaryFanoutSignal signal)
        {
            if (signal == null)
            {
                return;
            }

            DiaryGameComponent.Current?.Dispatch(signal);
        }
    }
}
