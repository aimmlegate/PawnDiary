// Pure generation-eligibility policy for irreversible or persistent changes to a pawn's body.
// Event capture stays in its source-specific adapters; this helper reads only the detached event
// defName/context they saved and answers whether temporary unconsciousness may suppress the page.
// Keeping the rule free of Verse/RimWorld types lets the full catalog be regression-tested without
// loading the game or any DLC.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;

namespace PawnDiary
{
    /// <summary>
    /// Recognizes permanent body-change diary events that must generate even while their subject is
    /// anesthetized, in a xenogermation coma, or otherwise below the normal Consciousness floor.
    /// </summary>
    internal static class PermanentBodyChangeGenerationPolicy
    {
        /// <summary>
        /// Returns a fresh fallback list of exact event defNames. XML owns the live list, while these
        /// defaults keep a missing or partial tuning Def safe and DLC-independent (all are plain strings).
        /// </summary>
        public static List<string> CreateDefaultDefNames()
        {
            return new List<string>
            {
                "XenotypeChanged",
                "GeneIdentityChanged",
                "PsylinkLevel",
                "BiotechMechlinkInstalled",
                "BiotechMechlinkRemoved",
                "PawnDiary_GhoulTransformation"
            };
        }

        /// <summary>
        /// Returns true when the enabled policy recognizes the event by an exact XML-owned defName or
        /// by one of the stable semantic fields already saved by permanent body-change capture paths.
        /// </summary>
        public static bool AllowsGenerationWhileIncapacitated(
            string eventDefName,
            string gameContext,
            bool enabled,
            IReadOnlyList<string> exactEventDefNames)
        {
            if (!enabled)
            {
                return false;
            }

            if (ContainsName(exactEventDefNames, eventDefName))
            {
                return true;
            }

            // Current gene transitions stamp this marker. The progression_kind fallbacks retain
            // compatibility with older xenotype pages and psylink changes.
            if (DiaryContextFields.IsTrue(gameContext, "gene_identity_transition")
                || DiaryContextFields.FieldEquals(gameContext, "progression_kind", "xenotype")
                || DiaryContextFields.FieldEquals(gameContext, "progression_kind", "gene_identity")
                || DiaryContextFields.FieldEquals(gameContext, "progression_kind", "psylink"))
            {
                return true;
            }

            // Added parts include artificial, organic/anomalous, and transplanted parts. Missing parts
            // represent a permanent loss. Ordinary hediffs have no part_kind and remain gated.
            string partKind = DiaryContextFields.Value(gameContext, "part_kind");
            if (BodyPartEventPolicy.KindHasToken(partKind, BodyPartEventPolicy.KindAddedPart)
                || BodyPartEventPolicy.KindHasToken(partKind, BodyPartEventPolicy.KindMissingPart))
            {
                return true;
            }

            return DiaryContextFields.FieldEquals(
                       gameContext, "mechanitor_moment", "mechlink_installed")
                   || DiaryContextFields.FieldEquals(
                       gameContext, "mechanitor_moment", "mechlink_removed")
                   || DiaryContextFields.FieldEquals(
                       gameContext, "anomaly_kind", "ghoul_transformation");
        }

        private static bool ContainsName(IReadOnlyList<string> names, string candidate)
        {
            if (names == null || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i]?.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
