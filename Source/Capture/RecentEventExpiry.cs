// Pure dedup-window policy for the transient "recent events" stores.
//
// Why this exists: every diary source dedups repeats inside its own tick window. Before the
// event-ingestion-bus refactor each source owned its own dictionary, so pruning only ever saw one
// window. The bus consolidated those dictionaries into shared stores keyed by source-prefixed
// strings, which means ONE store now holds keys with very different windows (e.g. an ability at
// ~300 ticks and a hediff at ~60000). Pruning every key by the *current caller's* window — the
// naive consolidation — lets a short-window source evict a long-window key that is still live,
// re-admitting an event the old code suppressed.
//
// The rule that fixes it is simple enough that it must be impossible to get wrong later: a key
// expires by ITS OWN window, never by another source's. This file is that rule, pulled out as a
// pure helper so the behavior is pinned by the standalone tests (DiaryCapturePolicyTests) without
// the RimWorld-coupled dedup store.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Pure policy for deciding whether a transient dedup entry is still live. Extracted from the
    /// RimWorld-coupled dedup store so the per-key-window rule is unit-testable.
    /// </summary>
    public static class RecentEventExpiry
    {
        /// <summary>
        /// True when the entry has aged past ITS OWN recorded window as of <paramref name="now"/>.
        /// This is the predicate the prune sweep applies to every key: a short-window caller must
        /// never evict a longer-window key that is still inside its own window. A window of zero or
        /// less means "this source opted out of dedup"; such entries are treated as never expiring
        /// here, and callers must separately skip marking/checking them (see <see cref="IsWithinWindow"/>).
        /// </summary>
        public static bool IsExpired(int recordedTick, int windowTicks, int now)
        {
            if (windowTicks <= 0)
            {
                return false;
            }

            return now - recordedTick >= windowTicks;
        }

        /// <summary>
        /// True when <paramref name="now"/> still falls inside the dedup window for an entry recorded
        /// at <paramref name="recordedTick"/>. Uses the caller's CURRENT window so a live XML tuning
        /// change is honored on the check (a key marked under an old window is re-checked against the
        /// policy in force right now). A window of zero or less means "no dedup" and always returns
        /// false, so a zero-window source neither blocks an event nor gets marked.
        /// </summary>
        public static bool IsWithinWindow(int recordedTick, int windowTicks, int now)
        {
            if (windowTicks <= 0)
            {
                return false;
            }

            return now - recordedTick < windowTicks;
        }
    }
}
