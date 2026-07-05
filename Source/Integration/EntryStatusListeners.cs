// Runtime bridge for API v10 entry-status listeners. Other mods register callbacks that receive
// read-only DiaryEntryStatusSnapshot DTOs after Pawn Diary changes a saved entry POV's lifecycle
// state. The registry itself is pure; this wrapper owns RimWorld thread/settings guards and logging.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// Process-global registry for external entry-status listeners.
    /// </summary>
    internal static class EntryStatusListeners
    {
        // Defensive cap: a churning-id adapter must not make every status change walk an unbounded
        // callback list. This is a parser/safety limit, not player-facing tuning.
        private const int MaxListeners = 32;

        private static readonly ListenerRegistry<DiaryEntryStatusSnapshot> Registry =
            new ListenerRegistry<DiaryEntryStatusSnapshot>(MaxListeners);

        public static bool Register(string id, Action<DiaryEntryStatusSnapshot> listener)
        {
            return Registry.Register(id, listener);
        }

        public static bool Unregister(string id)
        {
            return Registry.Unregister(id);
        }

        public static void Notify(DiaryEntryStatusSnapshot snapshot)
        {
            // Status mutation happens on the main thread. Keep the guard explicit so a future caller
            // cannot accidentally race the unsynchronized registry, and honor the master integrations
            // toggle the same way pawn-context providers do.
            if (snapshot == null || !UnityData.IsInMainThread || !ExternalIntegrationsAllowed)
            {
                return;
            }

            Registry.Notify(snapshot, LogListenerFailure);
        }

        private static bool ExternalIntegrationsAllowed
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.allowExternalIntegrations;
            }
        }

        private static void LogListenerFailure(string id, Exception exception)
        {
            Log.ErrorOnce(
                "[Pawn Diary] Integration API: entry-status listener '" + id
                + "' threw and has been disabled for this session: " + exception,
                ("PawnDiary.Api.EntryStatusListener.Exception." + id).GetHashCode());
        }
    }
}
