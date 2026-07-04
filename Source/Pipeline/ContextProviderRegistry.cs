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

        /// <summary>
        /// Registers or replaces a provider. Replacing an id keeps its original order and re-enables it.
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
            if (maxLines <= 0 || maxLineChars <= 0 || order.Count == 0)
            {
                return string.Empty;
            }

            List<string> cleanedLines = new List<string>();
            for (int i = 0; i < order.Count && cleanedLines.Count < maxLines; i++)
            {
                string id = order[i];
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

            return PromptContextLines.Join(cleanedLines, maxLines, maxLineChars);
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
