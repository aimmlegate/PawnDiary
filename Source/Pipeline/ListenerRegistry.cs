// Pure ordered callback registry. Integration-facing code wraps this with RimWorld/thread guards,
// while tests can verify replacement, caps, unregister, and disable-on-throw without loading Verse.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stores ordered listeners and invokes them with one payload, disabling a listener after its
    /// first exception so a broken adapter cannot keep throwing on every status change.
    /// </summary>
    internal sealed class ListenerRegistry<TPayload>
    {
        private readonly Dictionary<string, ListenerEntry> listeners =
            new Dictionary<string, ListenerEntry>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private readonly int maxListeners;

        public ListenerRegistry(int maxListeners)
        {
            this.maxListeners = maxListeners;
        }

        /// <summary>
        /// Registers or replaces a listener. Replacing keeps the original order and re-enables it.
        /// </summary>
        public bool Register(string id, Action<TPayload> listener)
        {
            string normalizedId = NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId) || listener == null)
            {
                return false;
            }

            if (!listeners.ContainsKey(normalizedId))
            {
                if (maxListeners > 0 && order.Count >= maxListeners)
                {
                    return false;
                }

                order.Add(normalizedId);
            }

            listeners[normalizedId] = new ListenerEntry(listener);
            return true;
        }

        /// <summary>
        /// Removes a listener id. Returns false when the id is blank or not registered.
        /// </summary>
        public bool Unregister(string id)
        {
            string normalizedId = NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId) || !listeners.Remove(normalizedId))
            {
                return false;
            }

            order.Remove(normalizedId);
            return true;
        }

        /// <summary>
        /// Invokes listeners in registration order and returns the number that ran successfully.
        /// </summary>
        public int Notify(TPayload payload, Action<string, Exception> onListenerFailed)
        {
            if (listeners.Count == 0)
            {
                return 0;
            }

            int delivered = 0;
            string[] ids = order.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                ListenerEntry entry;
                if (!listeners.TryGetValue(id, out entry) || entry.Disabled)
                {
                    continue;
                }

                try
                {
                    entry.Listener(payload);
                    delivered++;
                }
                catch (Exception e)
                {
                    entry.Disabled = true;
                    if (onListenerFailed != null)
                    {
                        onListenerFailed(id, e);
                    }
                }
            }

            return delivered;
        }

        private static string NormalizeId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        private sealed class ListenerEntry
        {
            public readonly Action<TPayload> Listener;
            public bool Disabled;

            public ListenerEntry(Action<TPayload> listener)
            {
                Listener = listener;
            }
        }
    }
}
