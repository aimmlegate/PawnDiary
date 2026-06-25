// Impure collector for the pawn facts DiaryEvent needs at record time. This is the ONE place the
// diary system reads live Pawn health/hediffs/capacities/traits to snapshot two display-only values:
//   * the serialized hediff/trait fact string used by XML text-decoration rules, and
//   * the 0..4 "staggered handwriting" intensity (low consciousness or intoxication).
//
// Why a dedicated helper? DiaryEvent is a persisted IExposable model and must stay pure: it should
// only hold plain saved values, not reach into live Verse/RimWorld state. The capture moment — when
// AddPairwiseEvent/AddSoloEvent in DiaryGameComponent.EventFactory are building a new event — is the
// one time a live Pawn is guaranteed to exist, so we read it HERE, produce plain int/string results,
// and hand them to the model via DiaryEvent.SetTextDecorationFacts / SetStaggeredIntensity. After
// that the model never touches the Pawn again. This mirrors DlcContext's role for DLC-gated reads.
//
// New to C#/RimWorld? The live Pawn object is not saved with the event, so the small facts we need
// for display must be snapshotted while the pawn's state is available. See AGENTS.md ("barrier").
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Guarded, impure reads of base-game pawn state needed to snapshot display-only diary facts at
    /// event-record time. All accessors return plain values safe to store on the persisted model.
    /// </summary>
    public static class PawnFactCapture
    {
        /// <summary>
        /// Snapshots the pawn's visible hediffs and traits as a serialized fact string for the XML
        /// text-decoration rules. Empty for a null/dead pawn. The serialized form is produced by the
        /// pure <see cref="DiaryTextDecorations.SerializePawnFacts"/> codec, so the only impure step
        /// here is reading the live hediff/trait lists.
        /// </summary>
        public static string TextDecorationFacts(Pawn pawn)
        {
            return DiaryTextDecorations.SerializePawnFacts(BuildTextDecorationContext(pawn));
        }

        /// <summary>
        /// Snapshots the display-only "staggered handwriting" severity (0..4) from low consciousness
        /// or intoxicating hediffs. Higher means more distorted text. Returns 0 for a null/unhealthy
        /// pawn. The result is clamped to 0..4 before it is handed back.
        /// </summary>
        public static int StaggeredIntensity(Pawn pawn)
        {
            if (pawn == null || pawn.health == null)
            {
                return 0;
            }

            int intensity = LowConsciousnessStaggeredIntensity(pawn);
            int intoxication = IntoxicationStaggeredIntensity(pawn);
            return ClampStaggeredIntensity(Math.Max(intensity, intoxication));
        }

        private static DiaryTextDecorationContext BuildTextDecorationContext(Pawn pawn)
        {
            DiaryTextDecorationContext context = new DiaryTextDecorationContext();
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs != null)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff hediff = hediffs[i];
                    if (hediff == null)
                    {
                        continue;
                    }

                    context.hediffs.Add(new DiaryTextDecorationHediffFact
                    {
                        defName = hediff.def?.defName ?? string.Empty,
                        label = hediff.Label ?? string.Empty,
                        severity = hediff.Severity,
                        visible = hediff.Visible
                    });
                }
            }

            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];
                    if (trait == null)
                    {
                        continue;
                    }

                    context.traits.Add(new DiaryTextDecorationTraitFact
                    {
                        defName = trait.def?.defName ?? string.Empty,
                        label = trait.LabelCap ?? string.Empty,
                        degree = trait.Degree
                    });
                }
            }

            return context;
        }

        private static int LowConsciousnessStaggeredIntensity(Pawn pawn)
        {
            if (pawn?.health?.capacities == null)
            {
                return 0;
            }

            float consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            if (consciousness < 0.14f)
            {
                return 4;
            }

            if (consciousness < 0.20f)
            {
                return 3;
            }

            if (consciousness < 0.35f)
            {
                return 2;
            }

            if (consciousness < 0.55f)
            {
                return 1;
            }

            return 0;
        }

        private static int IntoxicationStaggeredIntensity(Pawn pawn)
        {
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return 0;
            }

            int intensity = 0;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!IsIntoxicatingHediff(hediff))
                {
                    continue;
                }

                intensity = Math.Max(intensity, IntoxicationSeverityToIntensity(hediff.Severity));
            }

            return intensity;
        }

        private static bool IsIntoxicatingHediff(Hediff hediff)
        {
            if (hediff == null || !hediff.Visible)
            {
                return false;
            }

            string defName = hediff.def?.defName ?? string.Empty;
            string label = hediff.Label ?? string.Empty;
            string text = (defName + " " + label).ToLowerInvariant();
            return text.Contains("drunk")
                || text.Contains("alcohol")
                || text.Contains("hangover")
                || text.Contains("smokeleaf")
                || text.Contains("psychite")
                || text.Contains("yayo")
                || text.Contains("flake")
                || text.Contains("gojuice")
                || text.Contains("go-juice")
                || text.Contains("wake-up")
                || text.Contains("wakeup")
                || defName.EndsWith("High", StringComparison.OrdinalIgnoreCase);
        }

        private static int IntoxicationSeverityToIntensity(float severity)
        {
            if (severity >= 1.05f)
            {
                return 4;
            }

            if (severity >= 0.80f)
            {
                return 3;
            }

            if (severity >= 0.55f)
            {
                return 2;
            }

            if (severity >= 0.30f)
            {
                return 1;
            }

            return 0;
        }

        // Write-side defensive cap. DiaryEvent keeps its own copy for the save/load and read paths;
        // the two are intentionally independent so neither layer depends on the other.
        private static int ClampStaggeredIntensity(int intensity)
        {
            if (intensity < 0)
            {
                return 0;
            }

            return intensity > 4 ? 4 : intensity;
        }
    }
}
