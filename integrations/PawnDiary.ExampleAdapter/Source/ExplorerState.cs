// Shared mutable state for the API Explorer window and the [DebugAction] quick actions. Owned by
// ExampleAdapterGameComponent (so it lives for the whole game session) and exposed as a static
// singleton — the window is created/destroyed on demand, but the developer's selected pawns,
// remembered handles, and result log should survive opening and closing it.
//
// All fields are main-thread-only: PawnDiaryApi is main-thread-only, IMGUI is main-thread-only, so
// this state is never touched from a worker. No locking needed.
//
// New to C#? See AGENTS.md.
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// One entry in the explorer's running result log. Kept as plain fields so the window can render
    /// it without re-formatting the snapshot each frame.
    /// </summary>
    internal sealed class ExplorerLogEntry
    {
        public string methodName;       // e.g. "SubmitEventWithHandle"
        public string oneLineResult;    // short summary line shown in the log list (e.g. "recorded=true")
        public string detail;           // full formatted output for the selected entry (snapshot dump)
    }

    /// <summary>
    /// A handle remembered from a successful submit, so the Read Entry tab can call
    /// GetEntryStatus / GetEntrySnapshot without the developer re-typing ids.
    /// </summary>
    internal sealed class RememberedHandle
    {
        public DiaryEntryHandle handle;
        public string label;            // human label, e.g. "Bob (initiator)"
    }

    /// <summary>
    /// Session state shared by the explorer window, the [DebugAction] quick actions, and the
    /// registered entry-status listener. Lives for one play session; not saved (matches the rest of
    /// the example adapter — this is a developer tool, not save data).
    /// </summary>
    internal static class ExplorerState
    {
        // ---- pawn selection (by load id, resolved against the live game when the window draws) ----
        public static string selectedSubjectPawnId;
        public static string selectedPartnerPawnId;

        // ---- handles from successful submits, newest first (Read Entry tab consumes these) ----
        public static readonly List<RememberedHandle> RememberedHandles = new List<RememberedHandle>();
        private const int MaxRememberedHandles = 16;

        // ---- the running result log, newest at the bottom; ring-capped ----
        public const int MaxLogEntries = 64;
        public static readonly List<ExplorerLogEntry> Log = new List<ExplorerLogEntry>(MaxLogEntries + 1);
        public static int selectedLogIndex = -1;   // -1 → show the latest entry's detail

        // ---- entry-status listener ring buffer (proves RegisterEntryStatusListener fires) ----
        public const int MaxListenerEvents = 16;
        public static readonly List<string> ListenerEvents = new List<string>(MaxListenerEvents + 1);

        // ---- context-provider invocation counter (proves RegisterPawnContextProvider fires) ----
        public static int providerInvocations;

        /// <summary>
        /// Appends one handle from a successful submit, keeping the list capped. The most recent
        /// handle is at index 0 so the Read Entry dropdown shows it first.
        /// </summary>
        public static void RememberHandle(DiaryEntryHandle handle, string label)
        {
            if (handle == null)
            {
                return;
            }

            // De-dupe by entryKey so re-submitting the same POV doesn't fill the list.
            for (int i = RememberedHandles.Count - 1; i >= 0; i--)
            {
                if (RememberedHandles[i]?.handle?.entryKey == handle.entryKey)
                {
                    RememberedHandles.RemoveAt(i);
                }
            }

            RememberedHandles.Insert(0, new RememberedHandle { handle = handle, label = label ?? string.Empty });
            while (RememberedHandles.Count > MaxRememberedHandles)
            {
                RememberedHandles.RemoveAt(RememberedHandles.Count - 1);
            }
        }

        /// <summary>
        /// Appends a result entry to the running log, capping to <see cref="MaxLogEntries"/>. Selects
        /// the new entry so the detail panel jumps to the latest result.
        /// </summary>
        public static void AppendLog(string methodName, string oneLineResult, string detail)
        {
            Log.Add(new ExplorerLogEntry
            {
                methodName = methodName ?? string.Empty,
                oneLineResult = oneLineResult ?? string.Empty,
                detail = detail ?? string.Empty
            });
            while (Log.Count > MaxLogEntries)
            {
                Log.RemoveAt(0);
            }
            selectedLogIndex = Log.Count - 1;
        }

        /// <summary>
        /// Records one entry-status listener firing (capped). Proves the registered listener works.
        /// </summary>
        public static void RecordListenerEvent(string description)
        {
            ListenerEvents.Add(description ?? string.Empty);
            while (ListenerEvents.Count > MaxListenerEvents)
            {
                ListenerEvents.RemoveAt(0);
            }
        }

        /// <summary>
        /// Resets the result log only. Used by the window's "Clear" button.
        /// </summary>
        public static void ClearLog()
        {
            Log.Clear();
            selectedLogIndex = -1;
        }

        /// <summary>
        /// Resolves the saved subject pawn id against the live game, falling back to the first
        /// eligible colonist if the saved id is gone or blank. Returns null only when no eligible
        /// pawn exists in any player home map.
        /// </summary>
        public static Pawn ResolveSubjectPawn()
        {
            List<Pawn> pool = ExplorerPawns.EligiblePawns();
            if (pool == null || pool.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(selectedSubjectPawnId))
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i]?.GetUniqueLoadID() == selectedSubjectPawnId)
                    {
                        return pool[i];
                    }
                }
            }

            // Fall back to the first eligible pawn and remember it so the picker stays stable.
            Pawn fallback = pool[0];
            selectedSubjectPawnId = fallback.GetUniqueLoadID();
            return fallback;
        }

        /// <summary>
        /// Resolves the saved partner pawn id. Returns null when no partner is selected (the solo
        /// case) or when the saved id has fallen out of the pool.
        /// </summary>
        public static Pawn ResolvePartnerPawn(Pawn subject)
        {
            if (string.IsNullOrEmpty(selectedPartnerPawnId))
            {
                return null;
            }

            List<Pawn> pool = ExplorerPawns.EligiblePawns();
            if (pool == null)
            {
                return null;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                Pawn candidate = pool[i];
                if (candidate == null || candidate == subject)
                {
                    continue;
                }

                if (candidate.GetUniqueLoadID() == selectedPartnerPawnId)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a one-line summary of the listener/provider activity (used by the bottom status bar
        /// and the Hooks tab).
        /// </summary>
        public static string ActivitySummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("listener events: ").Append(ListenerEvents.Count);
            sb.Append("  | provider calls: ").Append(providerInvocations);
            sb.Append("  | remembered handles: ").Append(RememberedHandles.Count);
            return sb.ToString();
        }
    }
}
