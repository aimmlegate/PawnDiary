// Runtime bridge for API v4 pawn-context providers. Other mods register a Func<Pawn,string>; this
// runner invokes those delegates during the existing impure pawn-summary snapshot and returns only
// cleaned strings for the prompt pipeline.
using System;
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// Process-global registry for external pawn-context providers.
    /// </summary>
    internal static class PawnContextProviders
    {
        // Defensive caps for third-party prompt context. These are parser-style safety limits, not
        // tunable policy, so they stay hardcoded per AGENTS.md.
        private const int MaxProviderLines = 8;
        private const int MaxProviderLineChars = 200;

        private static readonly ContextProviderRegistry<Pawn> Registry = new ContextProviderRegistry<Pawn>();

        public static bool Register(string id, Func<Pawn, string> provider)
        {
            return Registry.Register(id, provider);
        }

        public static string BuildContextLines(Pawn pawn)
        {
            if (pawn == null || !ExternalIntegrationsAllowed)
            {
                return string.Empty;
            }

            return Registry.BuildContextLines(pawn, MaxProviderLines, MaxProviderLineChars, LogProviderFailure);
        }

        private static bool ExternalIntegrationsAllowed
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.allowExternalIntegrations;
            }
        }

        private static void LogProviderFailure(string id, Exception exception)
        {
            Log.ErrorOnce(
                "[Pawn Diary] Integration API: pawn-context provider '" + id
                + "' threw and has been disabled for this session: " + exception,
                ("PawnDiary.Api.ContextProvider.Exception." + id).GetHashCode());
        }
    }
}
