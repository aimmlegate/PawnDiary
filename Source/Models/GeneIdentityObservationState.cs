// Saved, per-pawn Biotech gene baseline. This model stores only primitive detached facts: a version,
// current xenotype identity, stable installed-gene Def names, and whether the bounded list is
// incomplete. Live Pawn/Gene reads remain in DlcContext, and selection/event ownership remains in
// pure capture policies.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable").
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Versioned gene membership baseline nested inside Biotech pawn progression state.</summary>
    public class GeneIdentityObservationState : IExposable
    {
        public int geneObservationVersion;
        public string xenotypeDefName = string.Empty;
        public string xenotypeLabel = string.Empty;
        public List<string> geneDefNames = new List<string>();
        public bool membershipTruncated;

        /// <summary>Scribes the additive frozen keys; old saves load an uninitialized version-zero row.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(
                ref geneObservationVersion,
                BiotechSaveKeys.GeneObservationVersion,
                0);
            Scribe_Values.Look(
                ref xenotypeDefName,
                BiotechSaveKeys.GeneObservedXenotypeDefName);
            Scribe_Values.Look(
                ref xenotypeLabel,
                BiotechSaveKeys.GeneObservedXenotypeLabel);
            Scribe_Collections.Look(
                ref geneDefNames,
                BiotechSaveKeys.GeneObservedDefNames,
                LookMode.Value);
            Scribe_Values.Look(
                ref membershipTruncated,
                BiotechSaveKeys.GeneObservedMembershipTruncated,
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        /// <summary>Repairs malformed loaded data without upgrading it into a false baseline.</summary>
        public void Normalize()
        {
            Apply(GeneIdentityObservationPolicy.Normalize(
                Snapshot(),
                GeneIdentityObservationPolicy.HardMaximumGeneDefNames,
                GeneSaliencePolicySnapshot.HardMaximumTextCharacters));
        }

        /// <summary>Silently replaces the row with the current detached live observation.</summary>
        internal void Observe(
            GeneIdentitySnapshot identity,
            int maximumGeneDefNames,
            int labelCharacterLimit)
        {
            Apply(GeneIdentityObservationPolicy.Observe(
                identity,
                maximumGeneDefNames,
                labelCharacterLimit));
        }

        /// <summary>True only after a real empty-or-nonempty current-version observation.</summary>
        internal bool HasCurrentBaseline()
        {
            return GeneIdentityObservationPolicy.HasCurrentBaseline(Snapshot());
        }

        /// <summary>Returns a detached copy for pure fallback comparison before this row advances.</summary>
        internal GeneIdentityObservationSnapshot CaptureSnapshot()
        {
            return Snapshot();
        }

        /// <summary>
        /// Discards a DLC-dependent comparison baseline while retaining a valid, harmless save row.
        /// The next Biotech-enabled scan establishes current truth silently.
        /// </summary>
        internal void Invalidate()
        {
            geneObservationVersion = 0;
            xenotypeDefName = string.Empty;
            xenotypeLabel = string.Empty;
            geneDefNames = new List<string>();
            membershipTruncated = false;
        }

        private GeneIdentityObservationSnapshot Snapshot()
        {
            return new GeneIdentityObservationSnapshot
            {
                observationVersion = geneObservationVersion,
                xenotypeDefName = xenotypeDefName,
                xenotypeLabel = xenotypeLabel,
                membershipTruncated = membershipTruncated,
                geneDefNames = geneDefNames == null
                    ? new List<string>()
                    : new List<string>(geneDefNames)
            };
        }

        private void Apply(GeneIdentityObservationSnapshot snapshot)
        {
            geneObservationVersion = snapshot?.observationVersion ?? 0;
            xenotypeDefName = snapshot?.xenotypeDefName ?? string.Empty;
            xenotypeLabel = snapshot?.xenotypeLabel ?? string.Empty;
            geneDefNames = snapshot?.geneDefNames ?? new List<string>();
            membershipTruncated = snapshot?.membershipTruncated == true;
        }
    }
}
