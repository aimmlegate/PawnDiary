// Interaction ingestion signal — the impure capture+emit half of the "social interaction" source
// (PlayLog.Add). Replaces the old DiaryGameComponent.RecordInteraction. Routes per the catalog:
// GenerateSolo (one eligible pawn), GeneratePair (both pawns, both POV), or RouteBatch/RouteAmbient
// (low-value groups merged into a delayed/day batch). Carries the originating PlayLog id so a click in
// RimWorld's Social tab can jump back to the generated entry, and stamps playLogInteractionDefName on
// pair entries so the generated-speech row override can find them.
//
// The patch-side preflight (ShouldCaptureInteractionFromPlayLog / ShouldRenderInteractionTextFromPlayLog)
// stays on the component. Pure decision + game-context format live in
// Source/Capture/Events/InteractionEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one social interaction row and emits it via the route the catalog chose. Built by
    /// <see cref="PlayLogAddPatch"/> and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class InteractionSignal : DiarySignal
    {
        private readonly Pawn initiator;
        private readonly Pawn recipient;
        private readonly InteractionDef interactionDef;
        private readonly string initiatorGameText;
        private readonly string recipientGameText;
        private readonly int playLogEntryId;
        private readonly bool initiatorEligible;
        private readonly bool recipientEligible;
        private readonly DiaryInteractionGroupDef batchGroup;
        private readonly DiaryInteractionGroupDef classifiedGroup;
        private readonly string effectiveGroupDefName;
        private readonly InteractionEventData payload;
        private string socialReflectionSourceKey;

        public InteractionSignal(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string initiatorGameText, string recipientGameText, int playLogEntryId)
        {
            this.initiator = initiator;
            this.recipient = recipient;
            this.interactionDef = interactionDef;
            this.initiatorGameText = initiatorGameText;
            this.recipientGameText = recipientGameText;
            this.playLogEntryId = playLogEntryId;

            if (!DiaryGameComponent.GamePlaying || initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!DiaryGameComponent.IsInteractionSignificant(interactionDef))
            {
                return;
            }

            // Freeze the exact effective group which authorized this PlayLog row. Mutation policy
            // must match this group as well as the DefName, so a later XML reorder cannot let a broad
            // conversion-looking name borrow evidence from a differently-owned route.
            classifiedGroup = InteractionGroups.Classify(interactionDef);
            effectiveGroupDefName = classifiedGroup?.defName ?? string.Empty;

            initiatorEligible = DiaryGameComponent.IsDiaryEligible(initiator);
            recipientEligible = DiaryGameComponent.IsDiaryEligible(recipient);

            bool routeToBatch = false;
            bool routeToAmbient = false;
            batchGroup = DiaryGameComponent.BatchGroupFor(interactionDef);
            bool bothEligible = initiatorEligible && recipientEligible;
            bool oneEligibleAllowed = batchGroup?.batch != null
                && batchGroup.batch.allowSingleEligiblePawn
                && batchGroup.batch.mode == InteractionBatchMode.AmbientDayNote
                && initiatorEligible != recipientEligible;
            if ((bothEligible || oneEligibleAllowed) && batchGroup != null)
            {
                // XML marks low-value groups as delayed batches. The promotion roll is intentionally
                // impure, so the chosen route is pre-computed here before the catalog decides.
                // A group must explicitly opt into one-eligible-pawn batching. That lets a colonist's
                // talks with a guest become one ambient solo note without changing any existing group.
                if (!DiaryGameComponent.ShouldPromoteInteraction(batchGroup, initiator, recipient))
                {
                    routeToAmbient = batchGroup.batch != null
                        && batchGroup.batch.mode == InteractionBatchMode.AmbientDayNote;
                    routeToBatch = !routeToAmbient;
                }
            }

            payload = new InteractionEventData
            {
                PawnId = initiatorEligible ? initiator.GetUniqueLoadID() : recipient.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = interactionDef.defName,
                Label = interactionDef.LabelCap.Resolve(),
                InitiatorPawnId = initiator.GetUniqueLoadID(),
                RecipientPawnId = recipient.GetUniqueLoadID(),
                InitiatorEligible = initiatorEligible,
                RecipientEligible = recipientEligible,
                IsSignificant = true,
                RouteToBatch = routeToBatch,
                RouteToAmbient = routeToAmbient,
            };
        }

        public override DiaryEventData Payload => payload;

        /// <summary>
        /// Reserves H7 from the accepted interaction before B6 can fold its ordinary page. The component
        /// rechecks the strict two-pawn eligibility, allowlist, chance, and saved cooldown invariants.
        /// </summary>
        public override void OnAccepted(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || payload == null || decision == CaptureDecision.Drop)
            {
                return;
            }

            socialReflectionSourceKey = sink.TryScheduleSocialReflection(
                initiator,
                recipient,
                interactionDef,
                classifiedGroup,
                initiatorGameText,
                playLogEntryId,
                payload.Tick);
            if (decision == CaptureDecision.RouteAmbient)
            {
                // Ambient routing cannot own a canonical pair page, and its later batching adapter may
                // throw. Freeze facts-only at acceptance so the delayed row never waits for absent prose.
                sink.MarkSocialReflectionSourceProseUnavailable(
                    socialReflectionSourceKey);
            }
        }

        /// <summary>
        /// Freezes facts-only source handling when ambient routing or B6 pacing produced no canonical
        /// interaction page. This prevents an expected no-prose source from consuming the 24-hour retry
        /// grace after its reflection is due.
        /// </summary>
        public override void OnAcceptedEmissionCompleted(
            DiaryGameComponent sink,
            CaptureDecision decision,
            bool sourceEventRegistered)
        {
            InteractionEventData.InteractionEmitShape shape =
                InteractionEventData.PlanEmit(decision);
            bool directPage = shape == InteractionEventData.InteractionEmitShape.Solo
                || shape == InteractionEventData.InteractionEmitShape.Pair;
            bool factsOnlySource = decision == CaptureDecision.RouteAmbient
                || (directPage && !sourceEventRegistered);
            if (sink != null && factsOnlySource)
            {
                sink.MarkSocialReflectionSourceProseUnavailable(
                    socialReflectionSourceKey);
            }
        }

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: initiatorEligible || recipientEligible,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        // Interaction has no detailed source-specific dedup, matching the old RecordInteraction. The
        // dispatcher still applies the short generic event-type safety window after Decide, so a fluke
        // duplicate PlayLog signal for the same pawn/type/shape does not emit twice.

        // ── Quality Wave B6 pacing ────────────────────────────────────────────────────────────────
        // Everyday chatter is exactly what the soft cap is for; a social fight or an important group
        // stays exempt so the diary never quietly loses a real moment.

        public override bool IsLowSalience =>
            classifiedGroup != null && !classifiedGroup.important && !classifiedGroup.combat;

        public override string DigestSourceKind => DigestPacingPolicy.SourceKindInteraction;

        /// <summary>The initiator's side of the moment, used when there is only one perspective.</summary>
        public override string BuildDigestLine()
        {
            return DigestLineFor(initiator, recipient, initiatorGameText);
        }

        /// <summary>
        /// Each diary remembers its OWN side of a suppressed shared interaction, so neither pawn's
        /// digest reads as if they were the other person.
        /// </summary>
        public override string BuildDigestLineForPawn(string pawnId)
        {
            if (recipient != null && string.Equals(
                pawnId, recipient.GetUniqueLoadID(), StringComparison.Ordinal))
            {
                return DigestLineFor(recipient, initiator, recipientGameText);
            }

            return BuildDigestLine();
        }

        /// <summary>
        /// A suppressed pair page belongs to both diaries, so both writers are inspected before the
        /// cap decides; a solo page reports only its one eligible pawn.
        /// </summary>
        public override void CollectPacedWriters(
            DiaryEventData eventPayload, CaptureDecision decision, List<string> writers)
        {
            if (writers == null || payload == null)
            {
                return;
            }

            if (decision == CaptureDecision.GeneratePair)
            {
                writers.Add(payload.InitiatorPawnId);
                writers.Add(payload.RecipientPawnId);
                return;
            }

            writers.Add(initiatorEligible ? payload.InitiatorPawnId : payload.RecipientPawnId);
        }

        /// <summary>
        /// Reuses the same text the page would have carried: RimWorld's own social-log line when it
        /// rendered one, otherwise the generic "X did Y with Z" fallback.
        /// </summary>
        private string DigestLineFor(Pawn pov, Pawn other, string gameText)
        {
            if (pov == null || interactionDef == null)
            {
                return string.Empty;
            }

            string line = DiaryLineCleaner.CleanLine(gameText);
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            return other == null
                ? interactionLabel
                : DiaryLineCleaner.CleanLine("PawnDiary.Event.Interaction".Translate(
                    pov.LabelShortCap, interactionLabel, other.LabelShortCap));
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            // Pure routing (unit-tested in DiaryCapturePolicyTests); this method renders the live text
            // and drives the sink for the chosen shape.
            InteractionEventData.InteractionEmitShape shape = InteractionEventData.PlanEmit(decision);
            if (shape == InteractionEventData.InteractionEmitShape.Drop)
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryLineCleaner.CleanLine(initiatorGameText);
            string recipientText = DiaryLineCleaner.CleanLine(recipientGameText);

            if (shape == InteractionEventData.InteractionEmitShape.Solo)
            {
                bool counselPage = IsCounselPage();
                CounselEventRule counselRule = counselPage ? CounselRuleForPage() : null;
                BeliefEventEvidence beliefEvidence = counselPage ? null : BeliefEvidenceForPage();
                Pawn eligiblePawn = initiatorEligible ? initiator : recipient;
                Pawn otherPawn = initiatorEligible ? recipient : initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.Interaction".Translate(eligiblePawn.LabelShortCap, interactionLabel, otherPawn.LabelShortCap);
                }

                string gameContext = AppendBeliefGameContext(
                    DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel),
                    beliefEvidence,
                    counselRule);
                DiaryEvent soloEvent = sink.AddSoloEvent(eligiblePawn, otherPawn, interactionDef.defName, interactionLabel,
                    eligibleText, InteractionGroups.InstructionFor(interactionDef), gameContext, beliefEvidence);
                soloEvent.AddPlayLogEntryId(playLogEntryId);
                sink.AttachSocialReflectionSourceEvent(
                    socialReflectionSourceKey, soloEvent.eventId);
                sink.QueueSolo(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionLabel, recipient.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            if (shape == InteractionEventData.InteractionEmitShape.Batch)
            {
                if (batchGroup != null)
                {
                    sink.RecordBatchedInteraction(batchGroup, initiator, recipient, interactionDef,
                        interactionLabel, initiatorText, recipientText, playLogEntryId);
                }
                return;
            }

            // Pair.
            bool pairCounselPage = IsCounselPage();
            CounselEventRule pairCounselRule = pairCounselPage ? CounselRuleForPage() : null;
            BeliefEventEvidence pairBeliefEvidence = pairCounselPage ? null : BeliefEvidenceForPage();
            DiaryEvent diaryEvent = sink.AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionGroups.InstructionFor(interactionDef),
                AppendBeliefGameContext(
                    DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel),
                    pairBeliefEvidence,
                    pairCounselRule),
                pairBeliefEvidence);
            diaryEvent.playLogInteractionDefName = interactionDef.defName;
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            sink.AttachSocialReflectionSourceEvent(
                socialReflectionSourceKey, diaryEvent.eventId);
            sink.QueuePair(diaryEvent);
        }

        /// <summary>
        /// Peeks detached belief mechanics only for a page-producing solo/pair route. Batch and ambient
        /// routes return before this seam, so they do not pay for evidence they cannot persist.
        /// </summary>
        private BeliefEventEvidence BeliefEvidenceForPage()
        {
            // This lookup deliberately happens after the existing capture decision. Detached cache
            // evidence can decorate an authorized page, but it cannot make an interaction eligible.
            BeliefEventEvidence mutation = BeliefMutationEvidenceAdapter.ForInteraction(
                payload.DefName,
                effectiveGroupDefName,
                payload.InitiatorPawnId,
                payload.RecipientPawnId,
                payload.Tick,
                initiator.LabelShort,
                recipient.LabelShort);
            return mutation ?? ConfiguredBeliefEventPolicy.Capture(
                new ConfiguredBeliefEventRequest
                {
                    pawnId = payload.InitiatorPawnId,
                    tick = payload.Tick,
                    sourceDomain = "interaction",
                    sourceDefName = payload.DefName,
                    groupKey = effectiveGroupDefName,
                    povRole = DiaryEvent.InitiatorRole,
                    visibleLabel = interactionDef.LabelCap.Resolve(),
                    visibleField = "event_label",
                    phase = "interaction"
                },
                ModsConfig.IdeologyActive,
                DiaryBeliefPolicy.Snapshot());
        }

        /// <summary>
        /// Resolves context-only Counsel policy after ordinary capture authorization. The caller's
        /// group check keeps every non-Counsel interaction off the policy-snapshot path.
        /// </summary>
        private CounselEventRule CounselRuleForPage()
        {
            return BeliefMutationEvidenceAdapter.ForCounselInteraction(
                payload.DefName, effectiveGroupDefName);
        }

        private bool IsCounselPage()
        {
            return string.Equals(effectiveGroupDefName, CounselEventPolicy.GroupDefName,
                StringComparison.Ordinal);
        }

        /// <summary>Appends only the exact stable markers proved by optional detached evidence.</summary>
        private static string AppendBeliefGameContext(
            string gameContext,
            BeliefEventEvidence beliefEvidence,
            CounselEventRule counselRule)
        {
            string mutationContext = BeliefMutationEventSelector.AppendGameContextMarker(
                gameContext, beliefEvidence);
            return CounselEventPolicy.AppendGameContext(mutationContext, counselRule);
        }
    }
}
