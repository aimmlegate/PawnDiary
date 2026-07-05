// Pure registry for external context providers. The RimWorld-facing API specializes this as
// Func<Pawn,string>, while tests use a plain context type to verify replacement, caps, and
// disable-on-throw behavior without loading Verse.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stores ordered context providers and runs them through <see cref="PromptContextLines"/>.
    /// </summary>
    internal sealed class ContextProviderRegistry<TContext>
    {
        private readonly Dictionary<string, ProviderEntry> providers =
            new Dictionary<string, ProviderEntry>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private readonly int maxProviders;

        /// <summary>
        /// Creates a registry that accepts at most <paramref name="maxProviders"/> distinct provider
        /// ids (a non-positive value means unlimited). The cap is a defensive limit: a misbehaving
        /// adapter that registers under a churning id (per-pawn, per-tick) must not grow this registry
        /// — nor the per-build walk over it — without bound.
        /// </summary>
        public ContextProviderRegistry(int maxProviders)
        {
            this.maxProviders = maxProviders;
        }

        /// <summary>
        /// Registers or replaces a provider. Replacing an id keeps its original order and re-enables it.
        /// Returns false for a blank id, a null provider, or a new id once the cap is reached.
        /// </summary>
        public bool Register(string id, Func<TContext, string> provider)
        {
            string normalizedId = NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId) || provider == null)
            {
                return false;
            }

            if (!providers.ContainsKey(normalizedId))
            {
                if (maxProviders > 0 && order.Count >= maxProviders)
                {
                    return false;
                }

                order.Add(normalizedId);
            }

            providers[normalizedId] = new ProviderEntry(provider);
            return true;
        }

        /// <summary>
        /// Invokes providers in registration order, disables a provider after its first exception, and
        /// returns cleaned context lines joined in prompt-ready form.
        /// </summary>
        public string BuildContextLines(
            TContext context,
            int maxLines,
            int maxLineChars,
            Action<string, Exception> onProviderFailed)
        {
            List<string> cleanedLines = BuildContextLineList(context, maxLines, maxLineChars, onProviderFailed);
            return cleanedLines.Count == 0 ? string.Empty : string.Join("; ", cleanedLines.ToArray());
        }

        /// <summary>
        /// Same collection as <see cref="BuildContextLines"/>, but returns the individual cleaned
        /// lines instead of joining them. Used by the public pawn-summary snapshot, which keeps each
        /// provider's contribution as its own list entry rather than collapsing them into one string.
        /// </summary>
        public List<string> BuildContextLineList(
            TContext context,
            int maxLines,
            int maxLineChars,
            Action<string, Exception> onProviderFailed)
        {
            List<string> cleanedLines = new List<string>();
            if (maxLines <= 0 || maxLineChars <= 0 || order.Count == 0)
            {
                return cleanedLines;
            }

            // Snapshot the id order before invoking providers: a provider callback that re-enters
            // Register (e.g. registers a new id from inside its own callback) must not mutate the
            // live `order` list mid-iteration. Each iteration re-looks-up the entry from `providers`
            // so a replacement or disable is honored, but the set of ids walked is the snapshot taken
            // here. Mirrors ListenerRegistry.Notify.
            string[] ids = new string[order.Count];
            order.CopyTo(ids);
            for (int i = 0; i < ids.Length && cleanedLines.Count < maxLines; i++)
            {
                string id = ids[i];
                ProviderEntry entry;
                if (!providers.TryGetValue(id, out entry) || entry.Disabled)
                {
                    continue;
                }

                try
                {
                    string line = PromptContextLines.CleanLine(entry.Provider(context), maxLineChars);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        cleanedLines.Add(line);
                    }
                }
                catch (Exception e)
                {
                    entry.Disabled = true;
                    if (onProviderFailed != null)
                    {
                        onProviderFailed(id, e);
                    }
                }
            }

            // Lines are already CleanLine'd, non-blank, and capped to maxLines in the loop above.
            return cleanedLines;
        }

        private static string NormalizeId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        private sealed class ProviderEntry
        {
            public readonly Func<TContext, string> Provider;
            public bool Disabled;

            public ProviderEntry(Func<TContext, string> provider)
            {
                Provider = provider;
            }
        }
    }
}
