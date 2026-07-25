// Pure interpretation of RimWorld's Hediff.SummaryHealthPercentImpact value.
//
// RimWorld reports bodily harm as a positive magnitude here (injuries and missing parts are > 0).
// Keeping the sign and normalization rule in a System-only helper prevents prompt adapters from
// independently reintroducing the old inverted-sign assumption.
using System;

namespace PawnDiary
{
    /// <summary>Interprets a hediff's positive health-loss magnitude for prompt selection.</summary>
    internal static class HealthImpactPolicy
    {
        /// <summary>True when the reported positive harm clears the caller's visibility threshold.</summary>
        public static bool IsMeaningfulHarm(float impact, float threshold)
        {
            return !float.IsNaN(impact)
                && !float.IsInfinity(impact)
                && impact > Math.Max(0f, threshold);
        }

        /// <summary>Returns a finite positive harm magnitude clamped to the normal percentage range.</summary>
        public static float NormalizedHarm(float impact)
        {
            if (float.IsNaN(impact) || float.IsInfinity(impact) || impact <= 0f)
            {
                return 0f;
            }

            return Math.Min(1f, impact);
        }
    }
}
