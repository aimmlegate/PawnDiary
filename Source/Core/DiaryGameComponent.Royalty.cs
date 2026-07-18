// Royalty Phase-1 save ownership and silent baselining. This partial intentionally creates no page:
// it only initializes detached bond/title/psylink truth so later hooks cannot backfill old events.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Version zero means the key was absent (an old save); version one plus an empty list means a
        // real baseline found no coded persona weapons. Never infer initialization from Count alone.
        private int royaltyPersonaObservationVersion;
        private List<PersonaBondState> royaltyPersonaBonds = new List<PersonaBondState>();

        private void ExposeRoyaltyData()
        {
            Scribe_Values.Look(
                ref royaltyPersonaObservationVersion,
                RoyaltySaveKeys.PersonaObservationVersion,
                0);
            Scribe_Collections.Look(
                ref royaltyPersonaBonds,
                RoyaltySaveKeys.PersonaBonds,
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) NormalizeRoyaltyPersonaBonds();
        }

        /// <summary>
        /// Establishes the old-save persona and per-pawn title/psylink baselines. It is safe to call
        /// repeatedly: current-version rows return without dispatching or mutating diary pages.
        /// </summary>
        private void BaselineRoyaltyStateIfNeeded(List<Pawn> colonists)
        {
            if (!ModsConfig.RoyaltyActive || colonists == null) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();

            if (royaltyPersonaObservationVersion < RoyaltyStatePersistence.CurrentObservationVersion)
            {
                List<PersonaBondStateSnapshot> baselines = new List<PersonaBondStateSnapshot>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < colonists.Count; i++)
                {
                    List<PersonaWeaponSnapshot> weapons = DlcContext.CapturePersonaWeapons(colonists[i]);
                    for (int j = 0; j < weapons.Count; j++)
                    {
                        PersonaBondStateSnapshot baseline = RoyaltyStatePersistence.BaselinePersona(
                            weapons[j],
                            now,
                            policy.maximumTraitCandidates);
                        if (baseline != null && seen.Add(baseline.weaponThingId)) baselines.Add(baseline);
                    }
                }
                baselines = RoyaltyStatePersistence.NormalizePersonas(
                    baselines,
                    policy.maximumTraitCandidates,
                    RoyaltyStatePersistence.HardMaximumPersonaStates);
                royaltyPersonaBonds = new List<PersonaBondState>();
                for (int i = 0; i < baselines.Count; i++)
                    royaltyPersonaBonds.Add(PersonaBondState.FromSnapshot(baselines[i]));
                royaltyPersonaObservationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
            }

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (!IsDiaryEligible(pawn)) continue;
                PawnDiaryRecord diary = FindDiary(pawn, true);
                PawnProgressionState progression = diary?.EnsureProgressionState();
                RoyaltyPawnProgressionState royalty = progression?.EnsureRoyaltyState();
                if (royalty == null
                    || royalty.observationVersion >= RoyaltyStatePersistence.CurrentObservationVersion)
                    continue;

                List<RoyalTitleSnapshot> titles = DlcContext.CaptureRoyalTitles(pawn);
                royalty.Baseline(titles, DlcContext.CurrentPsylinkLevel(pawn), now, progression);
                // Keep the legacy scalar title baseline current for save/downgrade compatibility and
                // to guarantee the unchanged generic scanner cannot emit an upgrade catch-up page.
                RoyalTitleSnapshot mostSenior = null;
                for (int j = 0; j < titles.Count; j++)
                    if (mostSenior == null || titles[j].seniority > mostSenior.seniority)
                        mostSenior = titles[j];
                progression.lastObservedRoyalTitleDefName = mostSenior?.titleDefName ?? string.Empty;
                progression.lastObservedRoyalTitleLabel = mostSenior?.titleLabel ?? string.Empty;
            }
        }

        private void NormalizeRoyaltyPersonaBonds()
        {
            royaltyPersonaObservationVersion = Math.Max(0, Math.Min(
                RoyaltyStatePersistence.CurrentObservationVersion,
                royaltyPersonaObservationVersion));
            List<PersonaBondStateSnapshot> source = new List<PersonaBondStateSnapshot>();
            if (royaltyPersonaBonds != null)
                for (int i = 0; i < royaltyPersonaBonds.Count; i++)
                    if (royaltyPersonaBonds[i] != null) source.Add(royaltyPersonaBonds[i].ToSnapshot());
            List<PersonaBondStateSnapshot> normalized = RoyaltyStatePersistence.NormalizePersonas(
                source,
                DiaryRoyaltyPolicy.Snapshot().maximumTraitCandidates,
                RoyaltyStatePersistence.HardMaximumPersonaStates);
            royaltyPersonaBonds = new List<PersonaBondState>();
            for (int i = 0; i < normalized.Count; i++)
                royaltyPersonaBonds.Add(PersonaBondState.FromSnapshot(normalized[i]));
        }

        /// <summary>
        /// Copies the exact POV's current persona bonds and faction-specific titles for the pure N3-R
        /// provider. The snapshot authorizes no page and contains no live Pawn, weapon, title, or Def.
        /// </summary>
        internal RoyaltyNarrativeSnapshot RoyaltyNarrativeSnapshotFor(Pawn pawn, int sourceTick)
        {
            RoyaltyNarrativeSnapshot result = new RoyaltyNarrativeSnapshot();
            if (!ModsConfig.RoyaltyActive || pawn == null) return result;

            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            string pawnId = pawn.GetUniqueLoadID() ?? string.Empty;
            string pawnName = PromptTextSanitizer.LocalizedPromptText(pawn.LabelShortCap);
            if (!policy.enabled || pawnId.Length == 0 || pawnName.Length == 0) return result;

            result.providerAvailable = true;
            result.povPawnId = pawnId;
            result.pawnCanKnow = true;
            result.hasVerifiedPovConnection = true;

            if (royaltyPersonaObservationVersion >= RoyaltyStatePersistence.CurrentObservationVersion
                && royaltyPersonaBonds != null)
            {
                for (int i = 0; i < royaltyPersonaBonds.Count; i++)
                {
                    PersonaBondState state = royaltyPersonaBonds[i];
                    if (state == null || !PersonaBondPhaseTokens.IsLive(state.phaseToken)
                        || !string.Equals(state.currentPawnId, pawnId, StringComparison.Ordinal)) continue;
                    string arcKey = RoyaltyArcKeys.Persona(state.weaponThingId, state.bondEpoch);
                    string text = FormatRoyaltyNarrative(
                        policy.personaNarrativeFormat,
                        pawnName,
                        state.lastDisplayName);
                    if (arcKey.Length == 0 || text.Length == 0) continue;
                    result.personaBonds.Add(new RoyaltyPersonaNarrativeFact
                    {
                        weaponThingId = state.weaponThingId ?? string.Empty,
                        weaponName = state.lastDisplayName ?? string.Empty,
                        bondEpoch = state.bondEpoch,
                        arcKey = arcKey,
                        text = text,
                        // This is a verified current relationship, so age begins at this event-time
                        // snapshot rather than at an old bond-formation tick.
                        sourceTick = sourceTick
                    });
                }
            }

            List<RoyalTitleSnapshot> titles = DlcContext.CaptureRoyalTitles(pawn);
            for (int i = 0; i < titles.Count; i++)
            {
                RoyalTitleSnapshot title = titles[i];
                if (title == null || string.IsNullOrWhiteSpace(title.factionId)
                    || string.IsNullOrWhiteSpace(title.titleDefName)) continue;
                string format = title.dutyCategoryTokens != null && title.dutyCategoryTokens.Count > 0
                    ? policy.titleWithDutiesNarrativeFormat
                    : policy.titleNarrativeFormat;
                string text = FormatRoyaltyNarrative(
                    format,
                    pawnName,
                    title.titleLabel,
                    title.factionName);
                if (text.Length == 0) continue;
                result.titles.Add(new RoyaltyTitleNarrativeFact
                {
                    factionId = title.factionId,
                    titleDefName = title.titleDefName,
                    text = text,
                    dutyCategoryTokens = title.dutyCategoryTokens == null
                        ? new List<string>()
                        : new List<string>(title.dutyCategoryTokens),
                    sourceTick = sourceTick
                });
            }
            return result;
        }

        private static string FormatRoyaltyNarrative(string format, params object[] values)
        {
            if (string.IsNullOrWhiteSpace(format)) return string.Empty;
            try
            {
                return PromptTextSanitizer.LocalizedPromptText(string.Format(format, values));
            }
            catch (FormatException)
            {
                // Bad translated placeholders disable this optional lens; the source page survives.
                return string.Empty;
            }
        }
    }
}
