// Saved, per-pawn Biotech gene baseline. This model stores only primitive detached facts: a version,
// current xenotype identity, and stable installed-gene Def names. Live Pawn/Gene reads remain in
// DlcContext, and selection/event ownership remains in pure capture policies.
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

        private GeneIdentityObservationSnapshot Snapshot()
        {
            return new GeneIdentityObservationSnapshot
            {
                observationVersion = geneObservationVersion,
                xenotypeDefName = xenotypeDefName,
                xenotypeLabel = xenotypeLabel,
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
        }
    }
}
