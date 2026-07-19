// Royalty save ownership, silent baselining, and Phase-2 persona lifecycle orchestration. Guarded
// adapters copy live weapon/Pawn facts here; pure policy commits state before an optional localized
// page is dispatched, so disabled groups and failed generation never create later catch-up pages.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
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
        private List<RoyalSuccessionState> royaltyPendingSuccessions = new List<RoyalSuccessionState>();

        private void ExposeRoyaltyData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) NormalizeRoyalSuccessionFacts();
            Scribe_Values.Look(
                ref royaltyPersonaObservationVersion,
                RoyaltySaveKeys.PersonaObservationVersion,
                0);
            Scribe_Collections.Look(
                ref royaltyPersonaBonds,
                RoyaltySaveKeys.PersonaBonds,
                LookMode.Deep);
            Scribe_Collections.Look(
                ref royaltyPendingSuccessions,
                RoyaltySaveKeys.PendingSuccessions,
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeRoyaltyPersonaBonds();
                NormalizeRoyalSuccessionFacts();
            }
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

            if (royaltyPersonaObservationVersion < RoyaltyStatePersistence.CurrentPersonaObservationVersion)
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
                royaltyPersonaObservationVersion = RoyaltyStatePersistence.CurrentPersonaObservationVersion;
            }

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (!IsDiaryEligible(pawn)) continue;
                PawnDiaryRecord diary = FindDiary(pawn, true);
                PawnProgressionState progression = diary?.EnsureProgressionState();
                RoyaltyPawnProgressionState royalty = progression?.EnsureRoyaltyState();
                if (royalty == null
                    || (royalty.observationVersion >= RoyaltyStatePersistence.CurrentObservationVersion
                        && royalty.observationAvailable))
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
                RoyaltyStatePersistence.CurrentPersonaObservationVersion,
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
        /// Drops malformed/terminal rows, migrates first-version expiry, and applies the XML-owned cap.
        /// </summary>
        private void NormalizeRoyalSuccessionFacts()
        {
            List<RoyalSuccessionFact> source = new List<RoyalSuccessionFact>();
            for (int i = 0; i < (royaltyPendingSuccessions?.Count ?? 0); i++)
                if (royaltyPendingSuccessions[i] != null)
                    source.Add(royaltyPendingSuccessions[i].ToSnapshot());
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            List<RoyalSuccessionFact> normalized = RoyalSuccessionPolicy.Normalize(
                source, Find.TickManager?.TicksGame ?? 0, policy.maximumPendingSuccessions);
            royaltyPendingSuccessions = new List<RoyalSuccessionState>();
            for (int i = 0; i < normalized.Count; i++)
                royaltyPendingSuccessions.Add(RoyalSuccessionState.FromSnapshot(normalized[i]));
        }

        /// <summary>
        /// Establishes any old-save baseline before vanilla changes a coded weapon, then captures its
        /// exact pre-action facts for transfer classification. Called only inside the CodeFor prefix.
        /// </summary>
        internal PersonaWeaponSnapshot BeginRoyaltyPersonaCoding(ThingWithComps weapon, Pawn pawn)
        {
            if (!RoyaltyPersonaRuntimeReady() || weapon == null || pawn == null) return null;
            BaselineRoyaltyStateIfNeeded(SnapshotFreeColonists());
            PersonaWeaponSnapshot before;
            return DlcContext.TryCapturePersonaWeapon(weapon, pawn, out before) ? before : null;
        }

        /// <summary>Commits the exact post-CodeFor formation or transfer and returns page ownership.</summary>
        internal bool CompleteRoyaltyPersonaCoding(
            ThingWithComps weapon,
            Pawn pawn,
            PersonaWeaponSnapshot before)
        {
            if (!RoyaltyPersonaRuntimeReady() || weapon == null || pawn == null) return false;
            PersonaWeaponSnapshot after;
            if (!DlcContext.TryCapturePersonaWeapon(weapon, pawn, out after)) return false;

            bool exactDifferentPawn = before != null
                && string.Equals(before.weaponThingId, after.weaponThingId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(before.codedPawnId)
                && !string.Equals(before.codedPawnId, after.codedPawnId, StringComparison.Ordinal);
            bool exactLivePrevious = exactDifferentPawn && HasLivePersonaBond(
                after.weaponThingId, before.codedPawnId);
            string observation = exactLivePrevious
                ? PersonaObservationTokens.Transfer
                : PersonaObservationTokens.Coding;
            return ApplyRoyaltyPersonaObservation(after, pawn, observation, true);
        }

        /// <summary>Records exact primary/not-primary evidence from persona equipment callbacks.</summary>
        internal void ObserveRoyaltyPersonaEquipment(ThingWithComps weapon, Pawn pawn)
        {
            if (!RoyaltyPersonaRuntimeReady() || weapon == null || pawn == null) return;
            BaselineRoyaltyStateIfNeeded(SnapshotFreeColonists());
            PersonaWeaponSnapshot captured;
            if (!DlcContext.TryCapturePersonaWeapon(weapon, pawn, out captured)) return;
            ApplyRoyaltyPersonaObservation(
                captured,
                pawn,
                captured.isCurrentlyPrimary
                    ? PersonaObservationTokens.Primary
                    : PersonaObservationTokens.NotPrimary,
                false);
        }

        /// <summary>Commits an exact weapon-destruction ending before vanilla clears the coded pawn.</summary>
        internal void ObserveRoyaltyPersonaDestroyed(ThingWithComps weapon)
        {
            if (!RoyaltyPersonaRuntimeReady() || weapon == null) return;
            BaselineRoyaltyStateIfNeeded(SnapshotFreeColonists());
            CompBladelinkWeapon comp = weapon.TryGetComp<CompBladelinkWeapon>();
            Pawn pawn = comp?.CodedPawn;
            PersonaWeaponSnapshot captured;
            if (!DlcContext.TryCapturePersonaWeapon(weapon, pawn, out captured)) return;
            string token = pawn?.Dead == true
                ? PersonaObservationTokens.PawnDeath
                : PersonaObservationTokens.Destroyed;
            ApplyRoyaltyPersonaObservation(captured, pawn, token, false);
        }

        /// <summary>Marks map removal as unavailable evidence; it can cancel but never prove separation.</summary>
        internal void ObserveRoyaltyPersonaMapRemoved(ThingWithComps weapon)
        {
            ObserveRoyaltyPersonaCleanup(weapon, PersonaObservationTokens.MapRemoved);
        }

        /// <summary>
        /// Classifies UnCode as pawn death only with exact dead-pawn evidence. Other UnCode calls are
        /// intentionally ambiguous and leave live state for reconciliation or a higher-priority hook.
        /// </summary>
        internal void ObserveRoyaltyPersonaUncode(ThingWithComps weapon)
        {
            if (!RoyaltyPersonaRuntimeReady() || weapon == null) return;
            CompBladelinkWeapon comp = weapon.TryGetComp<CompBladelinkWeapon>();
            Pawn pawn = comp?.CodedPawn;
            ObserveRoyaltyPersonaCleanup(
                weapon,
                pawn?.Dead == true
                    ? PersonaObservationTokens.PawnDeath
                    : PersonaObservationTokens.UnknownUncode);
        }

        /// <summary>
        /// Reconciles slow separation/recovery truth from saved live bonds. A missing/off-map weapon
        /// becomes Unavailable, never NotPrimary, so caravan/loading/map transitions stay silent.
        /// </summary>
        private void ReconcileRoyaltyPersonaBonds()
        {
            if (!RoyaltyPersonaRuntimeReady()) return;
            List<Pawn> colonists = SnapshotFreeColonists();
            BaselineRoyaltyStateIfNeeded(colonists);

            Dictionary<string, PersonaWeaponSnapshot> visible =
                new Dictionary<string, PersonaWeaponSnapshot>(StringComparer.Ordinal);
            Dictionary<string, Pawn> pawnsById = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
                if (pawnId.Length > 0) pawnsById[pawnId] = pawn;
                List<PersonaWeaponSnapshot> weapons = DlcContext.CapturePersonaWeapons(pawn);
                for (int j = 0; j < weapons.Count; j++)
                    if (weapons[j] != null && !string.IsNullOrWhiteSpace(weapons[j].weaponThingId))
                        visible[weapons[j].weaponThingId] = weapons[j];
            }

            // The versioned first baseline only sees free colonists on currently loaded maps. A
            // pre-existing bond can become visible later when a caravan returns or another map is
            // loaded. Adopt each such bond silently at first sight; otherwise the global version bit
            // would make that historical bond permanently invisible to lifecycle reconciliation.
            List<PersonaWeaponSnapshot> newlyVisible =
                new List<PersonaWeaponSnapshot>(visible.Values);
            HashSet<string> adoptedThisPass = new HashSet<string>(StringComparer.Ordinal);
            newlyVisible.Sort((left, right) => string.CompareOrdinal(
                left?.weaponThingId ?? string.Empty,
                right?.weaponThingId ?? string.Empty));
            for (int i = 0; i < newlyVisible.Count; i++)
            {
                PersonaWeaponSnapshot weapon = newlyVisible[i];
                if (weapon == null || PersonaBondIndex(weapon.weaponThingId) >= 0) continue;
                Pawn pawn;
                pawnsById.TryGetValue(weapon.codedPawnId ?? string.Empty, out pawn);
                ApplyRoyaltyPersonaObservation(
                    weapon,
                    pawn,
                    PersonaObservationTokens.Baseline,
                    false);
                // First sight establishes historical truth only. Treating the same observation as
                // not-primary below would immediately move a brand-new baseline into separation
                // pending, even though no elapsed post-baseline evidence has been observed yet.
                if (PersonaBondIndex(weapon.weaponThingId) >= 0)
                    adoptedThisPass.Add(weapon.weaponThingId);
            }

            if (royaltyPersonaBonds == null || royaltyPersonaBonds.Count == 0) return;

            // Snapshot rows first because Apply may replace/normalize the backing saved list.
            List<PersonaBondStateSnapshot> rows = new List<PersonaBondStateSnapshot>();
            for (int i = 0; i < royaltyPersonaBonds.Count; i++)
                if (royaltyPersonaBonds[i] != null) rows.Add(royaltyPersonaBonds[i].ToSnapshot());
            for (int i = 0; i < rows.Count; i++)
            {
                PersonaBondStateSnapshot row = rows[i];
                if (row == null || !PersonaBondPhaseTokens.IsLive(row.phaseToken)) continue;
                if (adoptedThisPass.Contains(row.weaponThingId ?? string.Empty)) continue;
                PersonaWeaponSnapshot weapon;
                Pawn pawn;
                pawnsById.TryGetValue(row.currentPawnId ?? string.Empty, out pawn);
                if (visible.TryGetValue(row.weaponThingId ?? string.Empty, out weapon)
                    && string.Equals(weapon.codedPawnId, row.currentPawnId, StringComparison.Ordinal))
                {
                    ApplyRoyaltyPersonaObservation(
                        weapon,
                        pawn,
                        weapon.isCurrentlyPrimary
                            ? PersonaObservationTokens.Primary
                            : PersonaObservationTokens.NotPrimary,
                        false);
                }
                else
                {
                    ApplyRoyaltyPersonaObservation(SnapshotFromState(row), pawn,
                        PersonaObservationTokens.Unavailable, false);
                }
            }
        }

        private void ObserveRoyaltyPersonaCleanup(ThingWithComps weapon, string token)
        {
            if (!RoyaltyPersonaRuntimeReady() || weapon == null) return;
            BaselineRoyaltyStateIfNeeded(SnapshotFreeColonists());
            CompBladelinkWeapon comp = weapon.TryGetComp<CompBladelinkWeapon>();
            Pawn pawn = comp?.CodedPawn;
            PersonaWeaponSnapshot captured;
            if (DlcContext.TryCapturePersonaWeapon(weapon, pawn, out captured))
                ApplyRoyaltyPersonaObservation(captured, pawn, token, false);
        }

        /// <summary>Runs one pure lifecycle transition, saves it, then optionally dispatches its page.</summary>
        private bool ApplyRoyaltyPersonaObservation(
            PersonaWeaponSnapshot weapon,
            Pawn pawn,
            string observationToken,
            bool normalPlayCoding)
        {
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.weaponThingId)) return false;
            if (pawn != null && !string.Equals(
                weapon.codedPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal)) return false;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            int index = PersonaBondIndex(weapon.weaponThingId);
            PersonaBondStateSnapshot previous = index >= 0
                ? royaltyPersonaBonds[index].ToSnapshot()
                : new PersonaBondStateSnapshot();
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyPersonaWeapon(
                PersonaWeaponEventData.BondFormedDefName);
            bool groupEnabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName)
                && pawn != null && IsDiaryEligible(pawn);
            int now = Find.TickManager?.TicksGame ?? 0;
            PersonaLifecycleDecision lifecycle = PersonaLifecyclePolicy.Evaluate(
                previous,
                new PersonaLifecycleObservation
                {
                    observationToken = observationToken,
                    weapon = weapon,
                    tick = Math.Max(0, now),
                    normalPlay = normalPlayCoding && Scribe.mode == LoadSaveMode.Inactive,
                    groupEnabled = groupEnabled
                },
                policy);

            // The pure policy can know that output is enabled, but only the impure repository can
            // prove a durable separation page actually exists. Commit the phase first with recovery
            // ownership false, then promote it transactionally after successful event creation.
            bool separationAwaitsPageAcceptance = lifecycle.shouldEmit
                && lifecycle.narrativePhase == PersonaNarrativePhaseTokens.BondSeparated;
            if (separationAwaitsPageAcceptance && lifecycle.nextState != null)
                lifecycle.nextState.separationEmitted = false;

            if (lifecycle.stateChanged)
            {
                PersonaBondStateSnapshot normalized = RoyaltyStatePersistence.NormalizePersona(
                    lifecycle.nextState, policy.maximumTraitCandidates);
                if (normalized == null) return false;
                lifecycle.nextState = normalized;
                if (index >= 0) royaltyPersonaBonds[index] = PersonaBondState.FromSnapshot(normalized);
                else
                {
                    royaltyPersonaBonds.Add(PersonaBondState.FromSnapshot(normalized));
                    NormalizeRoyaltyPersonaBonds();
                }
                royaltyPersonaObservationVersion = RoyaltyStatePersistence.CurrentPersonaObservationVersion;
            }

            if (!lifecycle.shouldEmit || group == null || pawn == null) return false;
            string defName = PersonaWeaponEventData.DefNameForPhase(lifecycle.narrativePhase);
            group = InteractionGroups.ClassifyPersonaWeapon(defName);
            if (group == null) return false;
            string eventIdentity = RoyaltyArcKeys.Persona(
                lifecycle.nextState.weaponThingId, lifecycle.nextState.bondEpoch)
                + "|" + lifecycle.narrativePhase;
            string traitToken = PersonaWeaponEventData.TraitEventTokenForPhase(lifecycle.narrativePhase);
            List<PersonaTraitFact> selected = PersonaTraitPolicy.Select(
                lifecycle.nextState.traits, traitToken, eventIdentity, policy);
            string duration = PersonaLifecycleDuration(previous, lifecycle.nextState, lifecycle.narrativePhase, now);
            PersonaWeaponSignal signal = new PersonaWeaponSignal(
                pawn, weapon, previous, lifecycle, selected, duration, policy, group, now);
            bool dispatched = Dispatch(signal);
            bool accepted = dispatched && signal.CreatedEvent != null;
            if (separationAwaitsPageAcceptance && accepted)
                MarkRoyaltyPersonaSeparationAccepted(
                    lifecycle.nextState.weaponThingId, lifecycle.nextState.bondEpoch);
            return accepted;
        }

        private void MarkRoyaltyPersonaSeparationAccepted(string weaponThingId, int bondEpoch)
        {
            int index = PersonaBondIndex(weaponThingId);
            PersonaBondState state = index >= 0 ? royaltyPersonaBonds[index] : null;
            if (state == null || state.bondEpoch != bondEpoch
                || state.phaseToken != PersonaBondPhaseTokens.Separated) return;
            state.separationEmitted = true;
            royaltyPersonaObservationVersion = RoyaltyStatePersistence.CurrentPersonaObservationVersion;
        }

        private bool HasLivePersonaBond(string weaponThingId, string pawnId)
        {
            int index = PersonaBondIndex(weaponThingId);
            PersonaBondState state = index >= 0 ? royaltyPersonaBonds[index] : null;
            return state != null && PersonaBondPhaseTokens.IsLive(state.phaseToken)
                && string.Equals(state.currentPawnId, pawnId, StringComparison.Ordinal);
        }

        private int PersonaBondIndex(string weaponThingId)
        {
            for (int i = 0; i < (royaltyPersonaBonds?.Count ?? 0); i++)
                if (royaltyPersonaBonds[i] != null && string.Equals(
                    royaltyPersonaBonds[i].weaponThingId, weaponThingId, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static PersonaWeaponSnapshot SnapshotFromState(PersonaBondStateSnapshot state)
        {
            return new PersonaWeaponSnapshot
            {
                weaponThingId = state?.weaponThingId ?? string.Empty,
                weaponDefName = state?.weaponDefName ?? string.Empty,
                displayName = state?.lastDisplayName ?? string.Empty,
                codedPawnId = state?.currentPawnId ?? string.Empty,
                codedPawnName = state?.currentPawnName ?? string.Empty,
                traits = PersonaTraitPolicy.CopyFacts(state?.traits)
            };
        }

        private static string PersonaLifecycleDuration(
            PersonaBondStateSnapshot previous,
            PersonaBondStateSnapshot next,
            string phase,
            int now)
        {
            int started = -1;
            if (phase == PersonaNarrativePhaseTokens.BondSeparated)
                started = previous?.pendingSeparationTick ?? -1;
            else if (phase == PersonaNarrativePhaseTokens.BondRecovered)
                started = previous?.lastPrimaryObservedTick ?? -1;
            else if (phase == PersonaNarrativePhaseTokens.BondEnded)
                started = next?.bondStartedTick ?? -1;
            if (started < 0 || now <= started) return string.Empty;
            return Math.Max(1, now - started).ToStringTicksToPeriod();
        }

        private static bool RoyaltyPersonaRuntimeReady()
        {
            return ModsConfig.RoyaltyActive && GamePlaying && Scribe.mode == LoadSaveMode.Inactive;
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
            if (pawn.Spawned && pawn.Map != null && activeEventWindows != null)
            {
                int now = Find.TickManager.TicksGame;
                for (int i = 0; i < activeEventWindows.Count; i++)
                {
                    ActiveEventWindowState active = activeEventWindows[i];
                    if (active == null || !RoyalAscentPolicy.ActivePressureApplies(
                        active.startDefName,
                        active.startCorrelationId,
                        active.startNarrativeArcKey,
                        active.startedTick,
                        active.expiresTick,
                        now,
                        policy,
                        ModsConfig.RoyaltyActive)) continue;

                    string pressureText = FormatRoyaltyNarrative(
                        policy.royalAscentPressureNarrativeFormat);
                    if (pressureText.Length > 0)
                    {
                        result.courtPressure = new RoyaltyCourtPressureNarrativeFact
                        {
                            arcPrefix = policy.royalAscentArcPrefix,
                            arcKey = active.startNarrativeArcKey,
                            text = pressureText,
                            sourceTick = active.startedTick
                        };
                    }
                    break;
                }
            }
            List<PersonaWeaponSnapshot> visiblePersonaWeapons =
                DlcContext.CapturePersonaWeapons(pawn);

            if (royaltyPersonaObservationVersion >= RoyaltyStatePersistence.CurrentPersonaObservationVersion
                && royaltyPersonaBonds != null)
            {
                for (int i = 0; i < royaltyPersonaBonds.Count; i++)
                {
                    PersonaBondState state = royaltyPersonaBonds[i];
                    if (state == null || !RoyaltyStatePersistence.IsCurrentVisiblePersonaBond(
                        state.ToSnapshot(), pawnId, visiblePersonaWeapons)) continue;
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
