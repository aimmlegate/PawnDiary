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
    internal static class PawnFactCapture
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
                        label = TryReadHediffLabel(hediff),
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

            // Thresholds are XML-tuned (DiaryTuningDef). First band the level falls into wins,
            // checked 4 -> 1; otherwise the pawn writes normally.
            float consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            DiaryTuningDef tuning = DiaryTuning.Current;
            if (consciousness < tuning.staggeredConsciousnessIntensity4Below)
            {
                return 4;
            }

            if (consciousness < tuning.staggeredConsciousnessIntensity3Below)
            {
                return 3;
            }

            if (consciousness < tuning.staggeredConsciousnessIntensity2Below)
            {
                return 2;
            }

            if (consciousness < tuning.staggeredConsciousnessIntensity1Below)
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

        // An intoxicating hediff is whatever the XML text-decoration rules classify as
        // StaggeredWordSizes (see Diary_TextDecorations). Routing through that rule list keeps a
        // single data-owned source of truth for "which hediffs distort speech" and lets modders/DLCs
        // extend the set without a code change. See AGENTS.md ("DLC-safety"/string matchers).
        private static bool IsIntoxicatingHediff(Hediff hediff)
        {
            if (hediff == null || !hediff.Visible)
            {
                return false;
            }

            DiaryTextDecorationHediffFact fact = new DiaryTextDecorationHediffFact
            {
                defName = hediff.def?.defName ?? string.Empty,
                label = TryReadHediffLabel(hediff),
                severity = hediff.Severity,
                visible = hediff.Visible
            };
            return DiaryTextDecorations.HediffMatchesStaggeredRules(DiaryTextDecorationDefs.CurrentRules, fact);
        }

        // Hediff.Label looks like a simple getter, but RimWorld composes it through virtual members
        // (LabelInBrackets/LabelBase) that other mods override, and a broken override can throw on a
        // hediff whose modded state is not fully initialized. The label only feeds optional display
        // decoration, so a throwing hediff degrades to "no label" instead of aborting the diary event.
        // Do not log here: the same broken hediff is re-read by every event while it persists.
        private static string TryReadHediffLabel(Hediff hediff)
        {
            try
            {
                return hediff?.Label ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static int IntoxicationSeverityToIntensity(float severity)
        {
            // Thresholds are XML-tuned (DiaryTuningDef). First band the severity reaches wins,
            // checked 4 -> 1; otherwise the hediff is too mild to distort.
            DiaryTuningDef tuning = DiaryTuning.Current;
            if (severity >= tuning.intoxicationSeverityIntensity4At)
            {
                return 4;
            }

            if (severity >= tuning.intoxicationSeverityIntensity3At)
            {
                return 3;
            }

            if (severity >= tuning.intoxicationSeverityIntensity2At)
            {
                return 2;
            }

            if (severity >= tuning.intoxicationSeverityIntensity1At)
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
