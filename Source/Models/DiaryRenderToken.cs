// Render-invalidation support for the diary tab's per-frame view cache.
//
// The diary tab is immediate-mode: RimWorld redraws it ~60 times a second, and building the entry
// views (group classification + gameContext parsing per entry) is the bulk of that cost. To skip
// the rebuild when nothing changed, the tab compares a cheap "render token" between frames and only
// rebuilds when it differs.
//
// The token has two parts:
//   - eventCount: how many events the pawn has. This rises when an event is added (events are
//     append-only), so additions are detected for free with no instrumentation.
//   - DiaryStateVersion: a process-wide counter bumped by DiaryEvent whenever a field the tab
//     renders changes (status, generated text, title). Bumping in the low-level setters means every
//     caller is covered automatically.
//
// This is transient render state: it is never serialized, and a plain increment is enough because
// callers only ever test the token for equality.
namespace PawnDiary
{
    /// <summary>
    /// Process-wide monotonic counter bumped whenever a rendered DiaryEvent field changes.
    /// </summary>
    public static class DiaryStateVersion
    {
        public static int Current { get; private set; }

        /// <summary>Marks the rendered diary state as changed. Called from DiaryEvent's setters.</summary>
        public static void Bump()
        {
            Current++;
        }
    }

    /// <summary>
    /// Cheap, comparable snapshot of one pawn's rendered diary state. Equal tokens mean the tab can
    /// safely reuse its last-built entry list.
    /// </summary>
    public readonly struct DiaryRenderToken : System.IEquatable<DiaryRenderToken>
    {
        public readonly int eventCount;
        public readonly int stateVersion;

        public DiaryRenderToken(int eventCount, int stateVersion)
        {
            this.eventCount = eventCount;
            this.stateVersion = stateVersion;
        }

        public bool Equals(DiaryRenderToken other)
        {
            return eventCount == other.eventCount && stateVersion == other.stateVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is DiaryRenderToken other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (eventCount * 397) ^ stateVersion;
        }
    }
}
