// Registry for adapter-supplied "psychotype generators". An integration that produces a pawn's outlook
// asynchronously (e.g. the 1-2-3 Personalities bridge's LLM transform) registers one of these so the
// per-pawn voice editor (Dialog_PawnWritingStyle) can show a "Regenerate" button and a "generating…"
// status without the adapter needing Harmony or any UI code of its own.
//
// Mirrors the pawn-context-provider registry: process-global, main-thread, replace-by-sourceId, capped,
// and a generator that throws is disabled for the rest of the session. The public contract is
// PawnDiaryApi.RegisterExternalPsychotypeGenerator; everything here is internal plumbing.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// An adapter's hook for driving the per-pawn psychotype editor's "Regenerate" affordance. All three
    /// callbacks run on the main thread while the editor is open. Only <see cref="reroll"/> is required.
    /// </summary>
    public sealed class ExternalPsychotypeGenerator
    {
        /// <summary>The registering mod's sourceId (used as the registry key and for logs). Required.</summary>
        public string sourceId = string.Empty;

        /// <summary>True when this source can (re)generate the pawn's outlook right now — the editor shows
        /// its Regenerate button only while some generator returns true. Null counts as "always".</summary>
        public Func<Pawn, bool> canReroll;

        /// <summary>True while a generation is in flight for the pawn — the editor shows a "generating…"
        /// status and disables the button. Null counts as "never busy".</summary>
        public Func<Pawn, bool> isBusy;

        /// <summary>Kicks off a fresh generation for the pawn. Required.</summary>
        public Action<Pawn> reroll;
    }

    /// <summary>
    /// Process-global registry of <see cref="ExternalPsychotypeGenerator"/>s. Internal: the public API is
    /// <see cref="PawnDiaryApi.RegisterExternalPsychotypeGenerator"/>; the voice editor reads it directly.
    /// </summary>
    internal static class ExternalPsychotypeGenerators
    {
        // A churning sourceId must not grow this without bound; adapters register a small fixed set.
        private const int MaxGenerators = 16;

        private static readonly Dictionary<string, ExternalPsychotypeGenerator> generators =
            new Dictionary<string, ExternalPsychotypeGenerator>();
        private static readonly List<string> order = new List<string>();

        /// <summary>Registers or replaces a generator by its (trimmed) sourceId. Main thread.</summary>
        public static void Register(ExternalPsychotypeGenerator generator)
        {
            if (generator == null)
            {
                return;
            }

            string id = string.IsNullOrWhiteSpace(generator.sourceId) ? string.Empty : generator.sourceId.Trim();
            if (id.Length == 0 || generator.reroll == null)
            {
                return;
            }

            if (!generators.ContainsKey(id))
            {
                if (order.Count >= MaxGenerators)
                {
                    return;
                }

                order.Add(id);
            }

            generators[id] = generator;
        }

        /// <summary>True when any registered generator reports it can (re)generate this pawn's outlook.</summary>
        public static bool CanReroll(Pawn pawn)
        {
            if (pawn == null || order.Count == 0)
            {
                return false;
            }

            string[] ids = order.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                ExternalPsychotypeGenerator generator;
                if (!generators.TryGetValue(ids[i], out generator))
                {
                    continue;
                }

                if (generator.canReroll == null)
                {
                    return true;
                }

                try
                {
                    if (generator.canReroll(pawn))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Disable(ids[i], e);
                }
            }

            return false;
        }

        /// <summary>True when any registered generator reports a generation is in flight for this pawn.</summary>
        public static bool IsBusy(Pawn pawn)
        {
            if (pawn == null || order.Count == 0)
            {
                return false;
            }

            string[] ids = order.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                ExternalPsychotypeGenerator generator;
                if (!generators.TryGetValue(ids[i], out generator) || generator.isBusy == null)
                {
                    continue;
                }

                try
                {
                    if (generator.isBusy(pawn))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Disable(ids[i], e);
                }
            }

            return false;
        }

        /// <summary>Triggers a fresh generation on every generator that can currently reroll this pawn.</summary>
        public static void Reroll(Pawn pawn)
        {
            if (pawn == null || order.Count == 0)
            {
                return;
            }

            string[] ids = order.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                ExternalPsychotypeGenerator generator;
                if (!generators.TryGetValue(ids[i], out generator) || generator.reroll == null)
                {
                    continue;
                }

                try
                {
                    if (generator.canReroll == null || generator.canReroll(pawn))
                    {
                        generator.reroll(pawn);
                    }
                }
                catch (Exception e)
                {
                    Disable(ids[i], e);
                }
            }
        }

        // A throwing generator is removed for the rest of the session, logged once, so a broken adapter
        // never spams the editor or the log.
        private static void Disable(string id, Exception e)
        {
            generators.Remove(id);
            order.Remove(id);
            Log.ErrorOnce(
                "[Pawn Diary] Integration API: external psychotype generator '" + id
                + "' threw and was disabled for this session: " + e,
                ("PawnDiary.Api.PsychotypeGenerator.Disable." + id).GetHashCode());
        }
    }
}
