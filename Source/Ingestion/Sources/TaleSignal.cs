// Tale ingestion signal — the impure capture+emit half of the "vanilla Tale recorded" source
// (TaleRecorder.RecordTale). Replaces the old DiaryGameComponent.RecordTale. Tales are vanilla's
// broad notable-history events (deaths, wounds, surgeries, births, recruitment, research, disasters).
// This is the most branchy source: one Tale can route to Drop / RouteBatch / GeneratePair /
// GenerateSolo / a neutral death-description (pair or solo). The pure route choice lives in
// TaleEventData.Decide (tested); this class snapshots the live RimWorld facts and performs the
// requested side effect.
//
// Pure decision, the CoveredElsewhere skip-list, the dedup key, and the base game-context format live
// in Source/Capture/Events/TaleEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one TaleRecorder event and emits it via the route the catalog chose. Built by
    /// <see cref="TaleRecorderPatch"/> and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class TaleSignal : DiarySignal
    {
        private readonly Tale tale;
        private readonly TaleDef taleDef;
        private readonly Pawn firstPawn;
        private readonly Pawn secondPawn;
        private readonly bool firstEligible;
        private readonly bool secondEligible;
        private readonly Pawn deathVictim;
        private readonly bool deathDescription;
        private readonly bool routesDeathDescription;
        private readonly DiaryInteractionGroupDef batchGroup;
        private readonly DiaryInteractionGroupDef personaMilestoneGroup;
        private readonly Pawn personaKiller;
        private readonly Pawn personaVictim;
        private readonly string personaKillerRole;
        private readonly string personaVictimRole;
        private readonly PersonaWeaponSnapshot personaWeapon;
        private readonly PersonaBondStateSnapshot personaBond;
        private readonly PersonaMilestoneDecision personaMilestone;
        private readonly PersonaKillCorrelationScope personaKillScope;
        private readonly TaleEventData payload;

        public TaleSignal(Tale tale, TaleDef taleDef)
        {
            taleDef = taleDef ?? tale?.def;
            this.tale = tale;
            this.taleDef = taleDef;

            if (!DiaryGameComponent.GamePlaying || tale == null || taleDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            ExtractTalePawns(tale, out firstPawn, out secondPawn);

            deathVictim = DeathVictimForTale(taleDef, firstPawn, secondPawn);
            deathDescription = DiaryGameComponent.IsDeathDescriptionEligible(deathVictim);

            firstEligible = DiaryGameComponent.IsDiaryEligible(firstPawn) || (deathDescription && firstPawn == deathVictim);
            secondEligible = DiaryGameComponent.IsDiaryEligible(secondPawn) || (deathDescription && secondPawn == deathVictim);

            string killerRole;
            string victimRole;
            if (ModsConfig.RoyaltyActive)
            {
                RoyaltyPolicySnapshot royaltyPolicy = DiaryRoyaltyPolicy.Snapshot();
                if (PersonaMilestonePolicy.TryResolveRoles(
                    taleDef.defName, royaltyPolicy, out killerRole, out victimRole))
                {
                    Pawn killer = PawnForRole(killerRole, firstPawn, secondPawn);
                    Pawn victim = PawnForRole(victimRole, firstPawn, secondPawn);
                    PersonaKillCorrelationScope killScope;
                    bool exactScope = PersonaKillThoughtCorrelation.TryMatchActiveKill(
                        killer, victim, out killScope);
                    DiaryInteractionGroupDef milestoneGroup = InteractionGroups.ClassifyDefName(
                        GroupDomain.Tale, PersonaMilestoneContextFormatter.FirstKillDefName);
                    bool exactMilestoneGroup = milestoneGroup != null
                        && string.Equals(
                            milestoneGroup.defName, "personaWeaponMilestone", StringComparison.Ordinal);
                    bool milestoneGroupEnabled = exactMilestoneGroup
                        && PawnDiaryMod.Settings.IsGroupEnabled(milestoneGroup.defName);
                    PersonaWeaponSnapshot capturedWeapon = null;
                    PersonaBondStateSnapshot capturedBond = null;
                    PersonaMilestoneDecision capturedMilestone = null;
                    bool enrich = exactScope
                        && DiaryGameComponent.Instance != null
                        && DiaryGameComponent.Instance.TryObserveRoyaltyPersonaMilestone(
                            killer,
                            victim,
                            taleDef.defName,
                            killerRole,
                            victimRole,
                            (int)Math.Ceiling(Math.Max(0f, taleDef.baseInterest)),
                            exactScope,
                            milestoneGroupEnabled,
                            Find.TickManager?.TicksGame ?? 0,
                            out capturedWeapon,
                            out capturedBond,
                            out capturedMilestone);
                    if (enrich)
                    {
                        personaMilestoneGroup = milestoneGroup;
                        personaKiller = killer;
                        personaVictim = victim;
                        personaKillerRole = killerRole;
                        personaVictimRole = victimRole;
                        personaWeapon = capturedWeapon;
                        personaBond = capturedBond;
                        personaMilestone = capturedMilestone;
                        personaKillScope = killScope;
                    }
                }
            }

            routesDeathDescription = deathDescription && personaMilestone == null;
            batchGroup = routesDeathDescription || personaMilestone != null
                ? null
                : DiaryGameComponent.TaleBatchGroupFor(taleDef);

            payload = new TaleEventData
            {
                PawnId = firstPawn?.GetUniqueLoadID() ?? string.Empty,
                Tick = Find.TickManager?.TicksGame ?? 0,
                DefName = personaMilestone != null
                    ? PersonaMilestoneContextFormatter.FirstKillDefName
                    : taleDef.defName,
                FirstPawnId = firstPawn?.GetUniqueLoadID(),
                SecondPawnId = secondPawn?.GetUniqueLoadID(),
                FirstEligible = firstEligible,
                SecondEligible = secondEligible,
                IsCoveredElsewhere = personaMilestone == null
                    && TaleEventData.CoveredElsewhere.Contains(taleDef.defName),
                IsGameConditionDuplicate = DefDatabase<GameConditionDef>.GetNamedSilentFail(taleDef.defName) != null,
                IsBatched = batchGroup != null,
                IsDeathDescription = routesDeathDescription,
                ForceSolo = personaMilestone != null,
                ForceFirstPawnPov = personaMilestone != null && ReferenceEquals(personaKiller, firstPawn),
            };
        }

        public override DiaryEventData Payload => payload;

        /// <summary>
        /// Stages an XML-owned ordinary combat Tale only when its exact killer/victim roles match
        /// the active Pawn.Kill scope. This never qualifies a milestone on its own.
        /// </summary>
        public bool TryStageAsPersonaKillCompanion()
        {
            if (!ModsConfig.RoyaltyActive || taleDef == null) return false;
            string killerRole;
            string victimRole;
            if (!PersonaMilestonePolicy.TryResolveCompanionRoles(
                taleDef.defName,
                DiaryRoyaltyPolicy.Snapshot(),
                out killerRole,
                out victimRole)) return false;
            Pawn killer = PawnForRole(killerRole, firstPawn, secondPawn);
            Pawn victim = PawnForRole(victimRole, firstPawn, secondPawn);
            PersonaKillCorrelationScope scope;
            return PersonaKillThoughtCorrelation.TryMatchActiveKill(killer, victim, out scope)
                && PersonaKillThoughtCorrelation.TryStageOrSuppressCompanionTale(
                    scope, taleDef.defName, this);
        }

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: firstEligible || secondEligible,
                userEnabled: personaMilestoneGroup != null
                    ? PawnDiaryMod.Settings.IsGroupEnabled(personaMilestoneGroup.defName)
                    : PawnDiaryMod.Settings.IsTaleEnabled(taleDef),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload != null ? payload.DedupKey() : string.Empty;

        public override int DedupWindowTicks => DiaryTuning.Current.taleDedupTicks;

        public override string EventTypeDedupKey(DiaryEventData payload, CaptureDecision decision)
        {
            return routesDeathDescription
                ? GenericEventTypeDedup.DeathDescriptionKey(deathVictim?.GetUniqueLoadID())
                : string.Empty;
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            // Pure routing (unit-tested in DiaryCapturePolicyTests); this method only renders the live
            // text and drives the sink for the chosen shape.
            TaleEventData.TaleEmitPlan plan = TaleEventData.PlanEmit(
                decision, firstEligible, payload.ForceSolo, payload.ForceFirstPawnPov);
            if (plan.Shape == TaleEventData.TaleEmitShape.Drop)
            {
                return;
            }

            string sourceLabel = CleanTaleLabel(taleDef);
            string label = personaMilestone != null
                ? "PawnDiary.Event.Persona.FirstKill.Label".Translate().Resolve()
                : sourceLabel;
            Def attachedDef = AttachedDefFor(tale);
            string instruction = personaMilestone != null
                ? InteractionGroups.InstructionForPersonaWeapon(personaMilestoneGroup)
                : InteractionGroups.InstructionForTale(taleDef);
            string gameContext;
            if (personaMilestone != null)
            {
                gameContext = TaleEventData.BuildGameContext(
                    PersonaMilestoneContextFormatter.FirstKillDefName,
                    label,
                    tale.GetType().Name,
                    attachedDef?.defName,
                    attachedDef == null
                        ? string.Empty
                        : DiaryLineCleaner.CleanLine(attachedDef.LabelCap.Resolve()));
                gameContext = PersonaMilestoneContextFormatter.FormatFirstKill(
                    gameContext,
                    personaWeapon,
                    personaBond,
                    personaMilestone.selectedTraits,
                    taleDef.defName,
                    sourceLabel,
                    personaKillerRole,
                    personaVictimRole,
                    DiaryRoyaltyPolicy.Snapshot());
            }
            else
            {
                gameContext = BuildTaleGameContext(tale, taleDef, label, attachedDef);
            }
            if (routesDeathDescription)
            {
                gameContext = AppendDeathDescriptionContext(gameContext, deathVictim, firstPawn, secondPawn);
            }

            if (plan.Shape == TaleEventData.TaleEmitShape.Batch)
            {
                if (batchGroup != null)
                {
                    sink.RecordBatchedTale(batchGroup, firstPawn, secondPawn, firstEligible, secondEligible,
                        taleDef, label, attachedDef, instruction);
                }
                return;
            }

            if (plan.Shape == TaleEventData.TaleEmitShape.Pair)
            {
                string text = BuildTalePairText(firstPawn, secondPawn, label, attachedDef);
                DiaryEvent pairEvent = CreatePairwiseEvent(sink, firstPawn, secondPawn, payload.DefName, label,
                    text, text, instruction, gameContext);
                if (plan.DeathDescription)
                {
                    sink.AddDeathEventRef(deathVictim, pairEvent.eventId);
                    sink.QueueDeathDescriptionFor(pairEvent);
                    return;
                }

                sink.QueuePair(pairEvent);
                return;
            }

            // Solo.
            Pawn povPawn = plan.PovIsFirstPawn ? firstPawn : secondPawn;
            Pawn otherPawn = plan.PovIsFirstPawn ? secondPawn : firstPawn;
            string soloText = personaMilestone != null
                ? "PawnDiary.Event.Persona.FirstKill.Fallback".Translate(
                    personaKiller.LabelShortCap,
                    string.IsNullOrWhiteSpace(personaWeapon.displayName)
                        ? "PawnDiary.Event.Persona.WeaponFallback".Translate().Resolve()
                        : personaWeapon.displayName,
                    personaVictim.LabelShortCap).Resolve()
                : DiaryGameComponent.BuildTaleSoloText(povPawn, label, otherPawn, attachedDef);
            DiaryEvent soloEvent = CreateSoloEvent(
                sink, povPawn, otherPawn, payload.DefName, label, soloText, instruction, gameContext);
            if (plan.DeathDescription)
            {
                sink.AddDeathEventRef(deathVictim, soloEvent.eventId);
                sink.QueueDeathDescriptionFor(soloEvent);
                return;
            }

            sink.QueueSolo(soloEvent, DiaryEvent.InitiatorRole);

            // QueueSolo is the final repository/queue step for this canonical Tale page. Promote
            // durable milestone ownership only after it succeeds, so a future queue failure leaves
            // the observed-but-unrecorded state recoverable and lets the kill scope release safely.
            if (personaMilestone != null)
            {
                sink.MarkRoyaltyPersonaMilestoneAccepted(
                    personaWeapon.weaponThingId, personaBond.bondEpoch);
                // Dispatch/Emit is synchronous inside TaleRecorder.RecordTale. Claim must therefore
                // happen before PawnKillPatch.Finalizer closes the exact scope; if that call chain
                // ever becomes queued, this invariant must be revisited with an explicit hand-off.
                PersonaKillThoughtCorrelation.Claim(personaKillScope, payload.Tick);
                ApplyPersonaNarrativeEvidence(sink, soloEvent);
            }
        }

        // ── Tale-specific helpers moved verbatim from the old DiaryGameComponent.Tales.cs ──

        private static void ExtractTalePawns(Tale tale, out Pawn firstPawn, out Pawn secondPawn)
        {
            firstPawn = null;
            secondPawn = null;

            Tale_DoublePawn doublePawnTale = tale as Tale_DoublePawn;
            if (doublePawnTale != null)
            {
                firstPawn = doublePawnTale.firstPawnData?.pawn;
                secondPawn = doublePawnTale.secondPawnData?.pawn;
                return;
            }

            Tale_SinglePawn singlePawnTale = tale as Tale_SinglePawn;
            if (singlePawnTale != null)
            {
                firstPawn = singlePawnTale.pawnData?.pawn;
            }
        }

        private static Def AttachedDefFor(Tale tale)
        {
            Tale_DoublePawnAndDef doublePawnAndDef = tale as Tale_DoublePawnAndDef;
            if (doublePawnAndDef != null)
            {
                return doublePawnAndDef.defData?.def;
            }

            Tale_SinglePawnAndDef singlePawnAndDef = tale as Tale_SinglePawnAndDef;
            return singlePawnAndDef?.defData?.def;
        }

        private static string CleanTaleLabel(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return "unknown";
            }

            string label = DiaryLineCleaner.CleanLine(taleDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? taleDef.defName : label;
        }

        private static string BuildTalePairText(Pawn firstPawn, Pawn secondPawn, string label, Def attachedDef)
        {
            string text = "PawnDiary.Event.TalePair".Translate(firstPawn.LabelShortCap, secondPawn.LabelShortCap, label).Resolve();
            return DiaryGameComponent.AppendAttachedDefText(text, attachedDef);
        }

        private static string BuildTaleGameContext(Tale tale, TaleDef taleDef, string label, Def attachedDef)
        {
            List<string> parts = new List<string>
            {
                "tale=" + taleDef.defName,
                "label=" + DiaryLineCleaner.CleanLine(label),
                "taleClass=" + tale.GetType().Name
            };

            if (attachedDef != null)
            {
                parts.Add("attachedDef=" + attachedDef.defName);
                parts.Add("attachedLabel=" + DiaryLineCleaner.CleanLine(attachedDef.LabelCap.Resolve()));
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string AppendDeathDescriptionContext(string gameContext, Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            List<string> parts = new List<string>
            {
                gameContext,
                "death_description=true",
                "death_victim=" + DiaryLineCleaner.CleanLine(deathVictim.LabelShortCap),
                "death_victim_id=" + deathVictim.GetUniqueLoadID(),
                "death_victim_role=" + DeathVictimRole(deathVictim, firstPawn, secondPawn)
            };

            Pawn otherPawn = deathVictim == firstPawn ? secondPawn : firstPawn;
            if (otherPawn != null)
            {
                parts.Add("other_pawn=" + DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap));
            }

            string deathFacts = DeathContextCache.ConsumeOrBuild(deathVictim);
            if (!string.IsNullOrWhiteSpace(deathFacts))
            {
                parts.Add(deathFacts);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static Pawn DeathVictimForTale(TaleDef taleDef, Pawn firstPawn, Pawn secondPawn)
        {
            if (taleDef == null || string.IsNullOrWhiteSpace(taleDef.defName))
            {
                return null;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyTale(taleDef);
            string victimRole = group?.DeathVictimRoleFor(taleDef.defName);
            if (string.Equals(victimRole, DiaryEvent.InitiatorRole, System.StringComparison.OrdinalIgnoreCase))
            {
                return firstPawn;
            }

            if (string.Equals(victimRole, DiaryEvent.RecipientRole, System.StringComparison.OrdinalIgnoreCase))
            {
                return secondPawn;
            }

            return null;
        }

        private static Pawn PawnForRole(string role, Pawn firstPawn, Pawn secondPawn)
        {
            if (string.Equals(role, RoyaltyTaleRoleTokens.Initiator, StringComparison.Ordinal))
                return firstPawn;
            if (string.Equals(role, RoyaltyTaleRoleTokens.Recipient, StringComparison.Ordinal))
                return secondPawn;
            return null;
        }

        private void ApplyPersonaNarrativeEvidence(DiaryGameComponent sink, DiaryEvent diaryEvent)
        {
            try
            {
                string pawnId = personaKiller.GetUniqueLoadID();
                NarrativeEvidence evidence = RoyaltyNarrativeEvidenceFactory.Persona(
                    diaryEvent.eventId,
                    diaryEvent.tick,
                    pawnId,
                    DiaryEvent.InitiatorRole,
                    personaWeapon,
                    personaBond.bondEpoch,
                    PersonaNarrativePhaseTokens.FirstConsequentialKill,
                    string.Empty,
                    payload.DefName,
                    pawnCanKnow: true);
                if (evidence == null) return;
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = pawnId,
                        povRole = DiaryEvent.InitiatorRole,
                        royalty = sink.RoyaltyNarrativeSnapshotFor(personaKiller, diaryEvent.tick),
                        recentSelectedCandidateKeys = sink.RecentNarrativeSelectedCandidateKeys(pawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full),
                        evidence = new List<NarrativeEvidence> { evidence }
                    });
                if (result.evidence.Count > 0)
                    diaryEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, result);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Persona first-kill Narrative Continuity evidence failed; the page remains: "
                    + exception,
                    "PawnDiary.PersonaMilestone.NarrativeEvidence".GetHashCode());
            }
        }

        private static string DeathVictimRole(Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            if (deathVictim == firstPawn)
            {
                return DiaryEvent.InitiatorRole;
            }

            if (deathVictim == secondPawn)
            {
                return DiaryEvent.RecipientRole;
            }

            return DiaryEvent.NeutralRole;
        }
    }
}
