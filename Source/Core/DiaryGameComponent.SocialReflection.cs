// Quality Wave H7 — delayed social-reflection scheduler and saved ownership.
//
// An accepted, allowlisted interaction may reserve one future initiator-side reflection. This
// component partial owns every impure boundary: reading live pawn relationships/opinion, persisting
// pending and cooldown rows, resolving hot/archive source prose, capturing the delayed subject
// snapshot, and dispatching the final solo event. Deterministic admission/roll/timing/formatting stay
// in the pure SocialReflectionPolicy and SocialReflectionContextFormatter.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "architecture barriers").
using System;
using System.Collections.Generic;
using System.Globalization;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string SocialReflectionGroupKey = "socialReflection";
        private const string SocialReflectionPendingSaveKey = "socialReflectionPending";
        private const string SocialReflectionPairCooldownSaveKey = "socialReflectionPairCooldowns";
        private const string SocialReflectionWriterCooldownSaveKey = "socialReflectionWriterCooldowns";
        private const string SocialReflectionHandledSourceSaveKey = "socialReflectionHandledSources";

        private List<PendingSocialReflectionState> pendingSocialReflections =
            new List<PendingSocialReflectionState>();
        private List<SocialReflectionPairCooldownState> socialReflectionPairCooldowns =
            new List<SocialReflectionPairCooldownState>();
        private List<SocialReflectionWriterCooldownState> socialReflectionWriterCooldowns =
            new List<SocialReflectionWriterCooldownState>();
        private List<SocialReflectionHandledSourceState> socialReflectionHandledSources =
            new List<SocialReflectionHandledSourceState>();

        // Transient wake-up hint. Per-row absolute timing is saved; this value is deliberately reset
        // after load so a paused/reloaded game cannot strand an overdue row.
        private int nextSocialReflectionSchedulerTick;

        /// <summary>
        /// Offers one catalog-accepted interaction to H7 and returns its stable source key only when
        /// the saved reservation was committed. Empty means the interaction did not qualify.
        /// </summary>
        internal string TryScheduleSocialReflection(
            Pawn initiator,
            Pawn recipient,
            InteractionDef interactionDef,
            DiaryInteractionGroupDef sourceGroup,
            string initiatorGameText,
            int playLogEntryId,
            int interactionTick)
        {
            if (!GamePlaying || initiator == null || recipient == null || interactionDef == null
                || sourceGroup == null || !sourceGroup.socialReflectionEligible
                || playLogEntryId < 0
                || !IsDiaryEligible(initiator)
                || !IsDiaryEligible(recipient))
            {
                return string.Empty;
            }

            SocialReflectionPolicySnapshot policy = DiarySocialReflectionPolicy.Snapshot();
            if (!IsSocialReflectionFeatureEnabled(policy))
            {
                return string.Empty;
            }

            string writerId = initiator.GetUniqueLoadID();
            string subjectId = recipient.GetUniqueLoadID();
            string sourceKey = SocialReflectionPolicy.SourceKey(
                playLogEntryId.ToString(CultureInfo.InvariantCulture),
                interactionTick,
                interactionDef.defName,
                writerId,
                subjectId);
            string pairKey = SocialReflectionPolicy.PairKey(writerId, subjectId);
            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(pairKey))
            {
                return string.Empty;
            }

            EnsureSocialReflectionLists();
            int now = Find.TickManager == null
                ? Math.Max(0, interactionTick)
                : Find.TickManager.TicksGame;
            PruneSocialReflectionCooldowns(now);
            if (FindPendingSocialReflectionBySource(sourceKey) != null
                || HasHandledSocialReflectionSource(sourceKey))
            {
                return string.Empty;
            }

            string relationSignature = CaptureSocialReflectionRelationSignature(
                initiator, recipient);
            PendingSocialReflectionState writerPending =
                FindPendingSocialReflectionByWriter(writerId);
            PendingSocialReflectionState pairPending =
                FindPendingSocialReflectionByPair(pairKey);
            SocialReflectionWriterCooldownState writerCooldown =
                FindSocialReflectionWriterCooldown(writerId);
            SocialReflectionPairCooldownState pairCooldown =
                FindSocialReflectionPairCooldown(pairKey);

            SocialReflectionAdmissionDecision admission =
                SocialReflectionPolicy.DecideAdmission(new SocialReflectionAdmissionInput
                {
                    sourceKey = sourceKey,
                    writerPawnId = writerId,
                    subjectPawnId = subjectId,
                    relationSignature = relationSignature,
                    nowTick = now,
                    pendingSourceKey = pairPending?.sourceKey ?? string.Empty,
                    writerHasPending = writerPending != null,
                    pendingWriterPawnId = writerPending?.writerPawnId ?? string.Empty,
                    pendingPairKey = pairPending?.pairKey ?? string.Empty,
                    pairPendingWriterPawnId = pairPending?.writerPawnId ?? string.Empty,
                    pendingRelationSignature = pairPending?.relationSignature ?? string.Empty,
                    writerCooldownUntilTick = writerCooldown?.cooldownUntilTick ?? 0,
                    pairCooldownUntilTick = pairCooldown?.cooldownUntilTick ?? 0,
                    pairCooldownRelationSignature = pairCooldown?.relationSignature ?? string.Empty
                });

            // A complete direct-relation change invalidates pair pacing even if independent writer
            // pacing rejects this candidate. Do not remove the writer row.
            if (admission.clearPairCooldown)
            {
                RemoveSocialReflectionPairCooldown(pairKey);
            }

            int opinion;
            if (!admission.accepted
                || !TryReadOpinion(initiator, recipient, out opinion)
                || !SocialReflectionPolicy.PassesChance(
                    sourceKey, opinion, policy.chanceBands))
            {
                return string.Empty;
            }

            bool replacingPairPending = admission.replacePending && pairPending != null;
            int pendingCountAfterReplacement = pendingSocialReflections.Count
                - (replacingPairPending ? 1 : 0);
            SocialReflectionWriterCooldownState replacedWriterCooldown =
                replacingPairPending
                    ? FindSocialReflectionWriterCooldown(pairPending.writerPawnId)
                    : null;
            bool removesWriterRow = replacedWriterCooldown != null
                && string.Equals(
                    replacedWriterCooldown.reservationSourceKey,
                    pairPending.sourceKey,
                    StringComparison.Ordinal);
            bool keepsCandidateWriterRow = writerCooldown != null
                && !ReferenceEquals(writerCooldown, replacedWriterCooldown);
            int requiredWriterRows = socialReflectionWriterCooldowns.Count
                - (removesWriterRow ? 1 : 0)
                + (keepsCandidateWriterRow ? 0 : 1);

            SocialReflectionPairCooldownState currentPairCooldown =
                FindSocialReflectionPairCooldown(pairKey);
            bool removesPairRow = replacingPairPending
                && currentPairCooldown != null
                && string.Equals(
                    currentPairCooldown.reservationSourceKey,
                    pairPending.sourceKey,
                    StringComparison.Ordinal);
            int requiredPairRows = socialReflectionPairCooldowns.Count
                - (removesPairRow ? 1 : 0)
                + (currentPairCooldown != null && !removesPairRow ? 0 : 1);
            if (pendingCountAfterReplacement + 1 > policy.maximumPendingRows
                || requiredWriterRows > policy.maximumWriterCooldownRows
                || requiredPairRows > policy.maximumPairCooldownRows)
            {
                return string.Empty;
            }

            if (replacingPairPending)
            {
                CancelPendingSocialReflection(pairPending, false);
            }

            int delay = SocialReflectionPolicy.DelayTicks(
                sourceKey, policy.minimumDelayTicks, policy.maximumDelayTicks);
            int dueTick = SocialReflectionSafeAdd(now, delay);
            PendingSocialReflectionState pending = new PendingSocialReflectionState
            {
                sourceKey = sourceKey,
                playLogEntryId = playLogEntryId,
                interactionTick = Math.Max(0, interactionTick),
                interactionDefName = interactionDef.defName ?? string.Empty,
                interactionLabel = BoundSocialReflectionText(
                    interactionDef.LabelCap.Resolve(),
                    policy.maximumSourceLabelCharacters),
                initiatorText = BoundSocialReflectionText(
                    DiaryLineCleaner.CleanLine(initiatorGameText),
                    policy.sourceExcerptMaximumCharacters),
                writerPawnId = writerId,
                subjectPawnId = subjectId,
                pairKey = pairKey,
                relationSignature = relationSignature,
                acceptedTick = now,
                dueTick = dueTick,
                nextAttemptTick = dueTick,
                retryIntervalTicks = policy.retryIntervalTicks,
                deadlineTick = SocialReflectionSafeAdd(dueTick, policy.proseGraceTicks)
            };

            UpsertSocialReflectionWriterCooldown(new SocialReflectionWriterCooldownState
            {
                writerPawnId = writerId,
                cooldownUntilTick = SocialReflectionSafeAdd(now, policy.writerCooldownTicks),
                reservationSourceKey = sourceKey
            });
            UpsertSocialReflectionPairCooldown(new SocialReflectionPairCooldownState
            {
                pairKey = pairKey,
                relationSignature = relationSignature,
                cooldownUntilTick = SocialReflectionSafeAdd(now, policy.pairCooldownTicks),
                reservationSourceKey = sourceKey
            });
            pendingSocialReflections.Add(pending);
            // Source ownership is terminal at acceptance, not at eventual event registration. A
            // cancellation releases pacing reservations but never allows the same interaction to roll
            // again after a setting change, relation change, save/load, or pawn return.
            AddHandledSocialReflectionSource(
                sourceKey,
                string.Empty,
                now,
                policy.maximumHandledSourceRows);
            if (nextSocialReflectionSchedulerTick <= 0
                || dueTick < nextSocialReflectionSchedulerTick)
            {
                nextSocialReflectionSchedulerTick = dueTick;
            }

            return sourceKey;
        }

        /// <summary>
        /// Attaches the canonical source event ID after a direct interaction page registers. Batched and
        /// folded sources intentionally leave it blank and are resolved later by PlayLog ID.
        /// </summary>
        internal void AttachSocialReflectionSourceEvent(string sourceKey, string eventId)
        {
            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            PendingSocialReflectionState pending =
                FindPendingSocialReflectionBySource(sourceKey);
            if (pending != null && string.IsNullOrWhiteSpace(pending.sourceEventId))
            {
                pending.sourceEventId = eventId.Trim();
            }
        }

        /// <summary>
        /// Marks an accepted ambient or B6-folded source as permanently facts-only. The delayed row can
        /// then emit at its normal due tick instead of retrying for prose that no canonical page can own.
        /// </summary>
        internal void MarkSocialReflectionSourceProseUnavailable(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return;
            }

            PendingSocialReflectionState pending =
                FindPendingSocialReflectionBySource(sourceKey);
            if (pending != null)
            {
                pending.sourceProseUnavailable = true;
            }
        }

        /// <summary>Scribes all additive H7 state and repairs bounded uniqueness after deep load.</summary>
        private void ExposeSocialReflectionData()
        {
            Scribe_Collections.Look(
                ref pendingSocialReflections,
                SocialReflectionPendingSaveKey,
                LookMode.Deep);
            Scribe_Collections.Look(
                ref socialReflectionPairCooldowns,
                SocialReflectionPairCooldownSaveKey,
                LookMode.Deep);
            Scribe_Collections.Look(
                ref socialReflectionWriterCooldowns,
                SocialReflectionWriterCooldownSaveKey,
                LookMode.Deep);
            Scribe_Collections.Look(
                ref socialReflectionHandledSources,
                SocialReflectionHandledSourceSaveKey,
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int now = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
                NormalizeSocialReflectionState(
                    now, DiarySocialReflectionPolicy.Snapshot());
            }
        }

        /// <summary>Clears saved and transient H7 state for a brand-new colony.</summary>
        private void ResetSocialReflectionForNewGame()
        {
            pendingSocialReflections = new List<PendingSocialReflectionState>();
            socialReflectionPairCooldowns =
                new List<SocialReflectionPairCooldownState>();
            socialReflectionWriterCooldowns =
                new List<SocialReflectionWriterCooldownState>();
            socialReflectionHandledSources =
                new List<SocialReflectionHandledSourceState>();
            nextSocialReflectionSchedulerTick = 0;
        }

        /// <summary>Resets only the transient wake-up hint after loading saved H7 rows.</summary>
        private void ResetSocialReflectionTransientState()
        {
            nextSocialReflectionSchedulerTick = 0;
        }

        /// <summary>
        /// Processes a bounded number of due rows. A large time jump never loops through hourly retry
        /// intervals; each row advances at most once per game tick.
        /// </summary>
        private void TickSocialReflections(int now)
        {
            EnsureSocialReflectionLists();
            if (pendingSocialReflections.Count == 0)
            {
                nextSocialReflectionSchedulerTick = 0;
                return;
            }

            // The player-facing group toggle can change between scheduled wake-ups. Check that cheap
            // dynamic switch every tick so cancellation owns reservations immediately; the static XML
            // policy snapshot is only allocated when work is actually due.
            if (!IsSocialReflectionGroupEnabled())
            {
                CancelAllPendingSocialReflections();
                return;
            }

            if (nextSocialReflectionSchedulerTick > 0
                && now < nextSocialReflectionSchedulerTick)
            {
                return;
            }

            SocialReflectionPolicySnapshot policy =
                DiarySocialReflectionPolicy.Snapshot();
            if (policy?.enabled != true)
            {
                CancelAllPendingSocialReflections();
                return;
            }

            List<PendingSocialReflectionState> due =
                new List<PendingSocialReflectionState>();
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                if (row != null && now >= row.nextAttemptTick)
                {
                    due.Add(row);
                }
            }
            due.Sort(ComparePendingSocialReflectionDue);
            if (due.Count == 0)
            {
                RecalculateNextSocialReflectionTick(now);
                return;
            }

            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();
            int processed = Math.Min(policy.maximumProcessedPerTick, due.Count);
            for (int i = 0; i < processed; i++)
            {
                PendingSocialReflectionState row = due[i];
                if (!pendingSocialReflections.Contains(row))
                {
                    continue;
                }

                Pawn writer;
                Pawn subject;
                livePawnsById.TryGetValue(row.writerPawnId ?? string.Empty, out writer);
                livePawnsById.TryGetValue(row.subjectPawnId ?? string.Empty, out subject);
                if (!IsDiaryEligible(writer) || !IsDiaryEligible(subject))
                {
                    CancelPendingSocialReflection(row, false);
                    continue;
                }

                SocialReflectionSourceResolution source =
                    ResolveSocialReflectionSource(row, policy);
                if (source.state == SocialReflectionSourceState.Retry
                    && now < row.deadlineTick)
                {
                    row.nextAttemptTick = Math.Min(
                        row.deadlineTick,
                        SocialReflectionSafeAdd(now, row.retryIntervalTicks));
                    continue;
                }

                EmitPendingSocialReflection(
                    row, writer, subject, source, policy, now);
            }

            RecalculateNextSocialReflectionTick(now);
        }

        private void EmitPendingSocialReflection(
            PendingSocialReflectionState row,
            Pawn writer,
            Pawn subject,
            SocialReflectionSourceResolution source,
            SocialReflectionPolicySnapshot policy,
            int now)
        {
            SocialReflectionFormattedContext formatted =
                SocialReflectionContextFormatter.Format(
                    CaptureSocialReflectionSubjectFacts(writer, subject, policy),
                    policy);
            string sourceFacts = BoundSocialReflectionText(
                BuildSocialReflectionSourceFacts(row, writer, subject),
                policy.sourceExcerptMaximumCharacters);
            string sourceTitle = source.state == SocialReflectionSourceState.Ready
                ? source.title
                : string.Empty;
            string sourceEnding = source.state == SocialReflectionSourceState.Ready
                ? source.ending
                : string.Empty;
            string gameContext = SocialReflectionEventData.BuildGameContext(
                sourceFacts,
                sourceTitle,
                sourceEnding,
                formatted,
                row.subjectPawnId,
                row.sourceEventId,
                row.sourceKey,
                row.interactionTick,
                row.playLogEntryId,
                row.interactionDefName);

            // Remove the pending owner before synchronous dispatch. If event registration never begins,
            // release only reservations still owned by this source; if it begins, the event/handled row
            // becomes the terminal owner even when a later queue step throws.
            pendingSocialReflections.Remove(row);
            long registrationBefore = events.RegistrationVersion;
            SocialReflectionSignal signal = new SocialReflectionSignal(
                writer,
                subject,
                row.sourceKey,
                gameContext,
                formatted.colorCue,
                now,
                true);
            Dispatch(signal);
            bool registered = events.RegistrationVersion > registrationBefore;
            if (registered)
            {
                AddHandledSocialReflectionSource(
                    row.sourceKey,
                    signal.CreatedEvent?.eventId ?? string.Empty,
                    now,
                    policy.maximumHandledSourceRows);
                return;
            }

            ReleaseSocialReflectionReservations(row, false);
        }

        private SocialReflectionSubjectFacts CaptureSocialReflectionSubjectFacts(
            Pawn writer,
            Pawn subject,
            SocialReflectionPolicySnapshot policy)
        {
            int opinion;
            bool hasOpinion = TryReadOpinion(writer, subject, out opinion);
            string relationLabel = string.Empty;
            try
            {
                PawnRelationDef relation =
                    PawnRelationUtility.GetMostImportantRelation(writer, subject);
                relationLabel = relation == null
                    ? string.Empty
                    : DiaryLineCleaner.CleanLine(
                        relation.GetGenderSpecificLabelCap(subject));
            }
            catch
            {
                relationLabel = string.Empty;
            }

            int? age = null;
            string lifeStage = string.Empty;
            try
            {
                age = subject.ageTracker == null
                    ? (int?)null
                    : subject.ageTracker.AgeBiologicalYears;
                lifeStage = subject.ageTracker?.CurLifeStage == null
                    ? string.Empty
                    : subject.ageTracker.CurLifeStage.LabelCap.Resolve();
            }
            catch
            {
                age = null;
                lifeStage = string.Empty;
            }

            float? beauty = null;
            try
            {
                beauty = subject.GetStatValue(StatDefOf.PawnBeauty);
            }
            catch
            {
                beauty = null;
            }

            return new SocialReflectionSubjectFacts
            {
                displayName = DiaryLineCleaner.CleanLine(subject.LabelShort),
                biologicalAgeYears = age,
                lifeStageLabel = DiaryLineCleaner.CleanLine(lifeStage),
                beauty = beauty,
                primaryRelationLabel = relationLabel,
                opinion = hasOpinion ? opinion : 0,
                opinionBandLabel = hasOpinion
                    ? DiaryBuckets.FormatOpinion(opinion)
                    : string.Empty,
                socialReasons = DiaryContextBuilder.BuildSocialThoughtReasons(
                    writer,
                    subject,
                    Math.Max(0, policy.maximumOpinionReasons))
            };
        }

        private static string BuildSocialReflectionSourceFacts(
            PendingSocialReflectionState row,
            Pawn writer,
            Pawn subject)
        {
            string text = DiaryLineCleaner.CleanLine(row?.initiatorText);
            string label = DiaryLineCleaner.CleanLine(row?.interactionLabel);
            string localizedLabel = writer == null || subject == null
                || string.IsNullOrWhiteSpace(label)
                ? label
                : DiaryLineCleaner.CleanLine(
                    "PawnDiary.Event.Interaction".Translate(
                        writer.LabelShort,
                        label,
                        subject.LabelShort));
            if (string.IsNullOrWhiteSpace(localizedLabel))
            {
                return text;
            }

            if (string.IsNullOrWhiteSpace(text)
                || string.Equals(localizedLabel, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, text, StringComparison.OrdinalIgnoreCase))
            {
                return localizedLabel;
            }

            // Both observations are frozen in saved state. Join them with punctuation only so this
            // remains localization-safe while preserving the label even when interaction prose exists.
            return DiaryLineCleaner.CleanLine(
                localizedLabel.TrimEnd('.', '!', '?', ';', ':') + ": " + text);
        }

        private SocialReflectionSourceResolution ResolveSocialReflectionSource(
            PendingSocialReflectionState row,
            SocialReflectionPolicySnapshot policy)
        {
            if (row == null || row.sourceProseUnavailable)
            {
                return SocialReflectionSourceResolution.FactsOnly();
            }

            if (!string.IsNullOrWhiteSpace(row.sourceEventId))
            {
                SocialReflectionSourceCandidate exact =
                    FindExactSocialReflectionSource(row);
                if (exact != null)
                {
                    return SocialReflectionSourceResolution.FromCandidate(
                        exact, policy);
                }

                // The exact ID may have been pruned before archive indexing on a repaired old save.
                // Fall through to the source PlayLog ID rather than inventing a mismatch.
            }

            SocialReflectionSourceCandidateAccumulator candidates =
                new SocialReflectionSourceCandidateAccumulator();
            int remaining = Math.Max(1, policy.maximumSourceLookupRows);
            PawnDiaryRecord diary = FindDiaryByPawnId(row.writerPawnId);
            if (diary?.eventIds != null)
            {
                for (int i = diary.eventIds.Count - 1; i >= 0 && remaining > 0; i--, remaining--)
                {
                    DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                    if (diaryEvent == null
                        || !diaryEvent.MatchesPlayLogEntry(row.playLogEntryId))
                    {
                        continue;
                    }

                    SocialReflectionSourceCandidate candidate =
                        BuildHotSocialReflectionSourceCandidate(diaryEvent, row);
                    candidates.Add(candidate, diaryEvent.eventId);
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived =
                archive.EntriesForPawn(row.writerPawnId);
            for (int i = archived.Count - 1; i >= 0 && remaining > 0; i--, remaining--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (entry == null || !entry.MatchesPlayLogEntry(row.playLogEntryId))
                {
                    continue;
                }

                SocialReflectionSourceCandidate candidate =
                    BuildArchivedSocialReflectionSourceCandidate(entry, row);
                candidates.Add(candidate, entry.eventId);
            }

            if (candidates.ambiguous)
            {
                return SocialReflectionSourceResolution.FactsOnly();
            }
            if (candidates.candidate == null)
            {
                return SocialReflectionSourceResolution.Retry();
            }

            row.sourceEventId = candidates.candidate.eventId;
            return SocialReflectionSourceResolution.FromCandidate(
                candidates.candidate, policy);
        }

        private SocialReflectionSourceCandidate FindExactSocialReflectionSource(
            PendingSocialReflectionState row)
        {
            DiaryEvent diaryEvent = events.FindEvent(row.sourceEventId);
            if (diaryEvent != null)
            {
                return BuildHotSocialReflectionSourceCandidate(diaryEvent, row);
            }

            ArchivedDiaryEntry entry = archive.Find(
                row.sourceEventId, row.writerPawnId, DiaryEvent.InitiatorRole);
            entry = entry ?? archive.Find(
                row.sourceEventId, row.writerPawnId, DiaryEvent.RecipientRole);
            return entry == null
                ? null
                : BuildArchivedSocialReflectionSourceCandidate(entry, row);
        }

        private static SocialReflectionSourceCandidate
            BuildHotSocialReflectionSourceCandidate(
                DiaryEvent diaryEvent,
                PendingSocialReflectionState row)
        {
            if (diaryEvent == null || row == null
                || string.Equals(
                    diaryEvent.interactionDefName,
                    SocialReflectionEventData.DefNameToken,
                    StringComparison.OrdinalIgnoreCase)
                || diaryEvent.solo
                || string.IsNullOrWhiteSpace(diaryEvent.initiatorPawnId)
                || string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId)
                || !diaryEvent.Involves(row.writerPawnId, row.subjectPawnId)
                || !diaryEvent.MatchesPlayLogEntry(row.playLogEntryId))
            {
                return null;
            }

            string role = diaryEvent.RoleForPawn(row.writerPawnId);
            if (string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            return new SocialReflectionSourceCandidate
            {
                eventId = diaryEvent.eventId ?? string.Empty,
                title = diaryEvent.TitleForRole(role),
                status = diaryEvent.StatusForRole(role),
                generatedText = diaryEvent.HasGeneratedTextForRole(role)
                    ? diaryEvent.DisplayTextForRole(role)
                    : string.Empty
            };
        }

        private static SocialReflectionSourceCandidate
            BuildArchivedSocialReflectionSourceCandidate(
                ArchivedDiaryEntry entry,
                PendingSocialReflectionState row)
        {
            if (entry == null || row == null
                || string.Equals(
                    entry.interactionDefName,
                    SocialReflectionEventData.DefNameToken,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    entry.pawnId, row.writerPawnId, StringComparison.Ordinal)
                || !string.Equals(
                    entry.linkedPawnId, row.subjectPawnId, StringComparison.Ordinal)
                || (!DiaryEvent.RoleEquals(entry.povRole, DiaryEvent.InitiatorRole)
                    && !DiaryEvent.RoleEquals(entry.povRole, DiaryEvent.RecipientRole))
                || !entry.MatchesPlayLogEntry(row.playLogEntryId))
            {
                return null;
            }

            return new SocialReflectionSourceCandidate
            {
                eventId = entry.eventId ?? string.Empty,
                title = entry.title ?? string.Empty,
                status = entry.status ?? string.Empty,
                generatedText = entry.generatedText ?? string.Empty,
                archivedGenerationStale = entry.archivedGenerationStale
            };
        }

        private static string CaptureSocialReflectionRelationSignature(
            Pawn first,
            Pawn second)
        {
            if (first == null || second == null)
            {
                return string.Empty;
            }

            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            List<SocialReflectionRelationFact> facts =
                new List<SocialReflectionRelationFact>();
            AddSocialReflectionRelations(first, second, facts);
            AddSocialReflectionRelations(second, first, facts);
            return SocialReflectionPolicy.RelationSignature(
                firstId, secondId, facts);
        }

        private static void AddSocialReflectionRelations(
            Pawn owner,
            Pawn other,
            List<SocialReflectionRelationFact> facts)
        {
            List<DirectPawnRelation> direct = owner?.relations?.DirectRelations;
            if (direct == null || facts == null)
            {
                return;
            }

            string ownerId = owner.GetUniqueLoadID();
            string otherId = other.GetUniqueLoadID();
            for (int i = 0; i < direct.Count; i++)
            {
                DirectPawnRelation relation = direct[i];
                if (relation?.def == null || relation.otherPawn != other)
                {
                    continue;
                }

                facts.Add(new SocialReflectionRelationFact
                {
                    ownerPawnId = ownerId,
                    otherPawnId = otherId,
                    relationDefName = relation.def.defName ?? string.Empty
                });
            }
        }

        private static bool IsSocialReflectionFeatureEnabled(
            SocialReflectionPolicySnapshot policy)
        {
            return policy?.enabled == true && IsSocialReflectionGroupEnabled();
        }

        private static bool IsSocialReflectionGroupEnabled()
        {
            DiaryInteractionGroupDef group =
                InteractionGroups.ByKey(SocialReflectionGroupKey);
            return group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
        }

        private void CancelAllPendingSocialReflections()
        {
            for (int i = pendingSocialReflections.Count - 1; i >= 0; i--)
            {
                CancelPendingSocialReflection(
                    pendingSocialReflections[i], false);
            }
            nextSocialReflectionSchedulerTick = 0;
        }

        private void CancelPendingSocialReflection(
            PendingSocialReflectionState row,
            bool eventWasRegistered)
        {
            if (row == null)
            {
                return;
            }

            pendingSocialReflections.Remove(row);
            ReleaseSocialReflectionReservations(row, eventWasRegistered);
        }

        private void ReleaseSocialReflectionReservations(
            PendingSocialReflectionState row,
            bool eventWasRegistered)
        {
            if (row == null)
            {
                return;
            }

            SocialReflectionWriterCooldownState writer =
                FindSocialReflectionWriterCooldown(row.writerPawnId);
            if (writer != null
                && SocialReflectionPolicy.ReleasesReservation(
                    row.sourceKey,
                    writer.reservationSourceKey,
                    eventWasRegistered))
            {
                socialReflectionWriterCooldowns.Remove(writer);
            }

            SocialReflectionPairCooldownState pair =
                FindSocialReflectionPairCooldown(row.pairKey);
            if (pair != null
                && SocialReflectionPolicy.ReleasesReservation(
                    row.sourceKey,
                    pair.reservationSourceKey,
                    eventWasRegistered))
            {
                socialReflectionPairCooldowns.Remove(pair);
            }
        }

        private void NormalizeSocialReflectionState(
            int now,
            SocialReflectionPolicySnapshot policy)
        {
            EnsureSocialReflectionLists();
            SocialReflectionPolicySnapshot safe =
                policy ?? SocialReflectionPolicySnapshot.CreateDefault();

            socialReflectionHandledSources =
                NormalizeSocialReflectionHandledSources(
                    socialReflectionHandledSources,
                    safe.maximumHandledSourceRows);
            List<PendingSocialReflectionState> normalizedPending =
                new List<PendingSocialReflectionState>();
            HashSet<string> sources = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> writers = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> pairs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> discardedSources =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                bool valid = NormalizePendingSocialReflectionRow(row, safe);
                bool keep = valid
                    && normalizedPending.Count < safe.maximumPendingRows
                    && !sources.Contains(row.sourceKey)
                    && !writers.Contains(row.writerPawnId)
                    && !pairs.Contains(row.pairKey);
                if (!keep)
                {
                    string discardedSource = CleanSocialReflectionValue(row?.sourceKey);
                    if (discardedSource.Length > 0)
                    {
                        discardedSources.Add(discardedSource);
                    }
                    continue;
                }

                sources.Add(row.sourceKey);
                writers.Add(row.writerPawnId);
                pairs.Add(row.pairKey);
                normalizedPending.Add(row);
            }
            pendingSocialReflections = normalizedPending;
            // Invalid, duplicate, or over-cap pending owners cannot keep writer/pair reservations.
            // Preserve a reservation when an otherwise-discarded duplicate shares the retained source.
            discardedSources.ExceptWith(sources);
            if (discardedSources.Count > 0)
            {
                socialReflectionPairCooldowns.RemoveAll(row =>
                    row != null
                    && discardedSources.Contains(
                        CleanSocialReflectionValue(row.reservationSourceKey)));
                socialReflectionWriterCooldowns.RemoveAll(row =>
                    row != null
                    && discardedSources.Contains(
                        CleanSocialReflectionValue(row.reservationSourceKey)));
            }
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                if (!HasHandledSocialReflectionSource(row.sourceKey))
                {
                    AddHandledSocialReflectionSource(
                        row.sourceKey,
                        string.Empty,
                        row.acceptedTick,
                        safe.maximumHandledSourceRows);
                }
            }

            socialReflectionPairCooldowns = NormalizeSocialReflectionPairCooldowns(
                socialReflectionPairCooldowns,
                now,
                safe.maximumPairCooldownRows);
            socialReflectionWriterCooldowns =
                NormalizeSocialReflectionWriterCooldowns(
                    socialReflectionWriterCooldowns,
                    now,
                    safe.maximumWriterCooldownRows);
            nextSocialReflectionSchedulerTick = 0;
        }

        private static bool NormalizePendingSocialReflectionRow(
            PendingSocialReflectionState row,
            SocialReflectionPolicySnapshot policy)
        {
            if (row == null)
            {
                return false;
            }

            row.sourceKey = CleanSocialReflectionValue(row.sourceKey);
            row.sourceEventId = CleanSocialReflectionValue(row.sourceEventId);
            row.interactionDefName =
                CleanSocialReflectionValue(row.interactionDefName);
            row.interactionLabel =
                BoundSocialReflectionText(
                    CleanSocialReflectionValue(row.interactionLabel),
                    policy.maximumSourceLabelCharacters);
            row.initiatorText = BoundSocialReflectionText(
                CleanSocialReflectionValue(row.initiatorText),
                policy.sourceExcerptMaximumCharacters);
            row.writerPawnId = CleanSocialReflectionValue(row.writerPawnId);
            row.subjectPawnId = CleanSocialReflectionValue(row.subjectPawnId);
            row.relationSignature =
                CleanSocialReflectionValue(row.relationSignature);
            row.pairKey = SocialReflectionPolicy.PairKey(
                row.writerPawnId, row.subjectPawnId);
            if (row.sourceKey.Length == 0 || row.pairKey.Length == 0
                || row.playLogEntryId < 0 || row.interactionDefName.Length == 0)
            {
                return false;
            }

            row.acceptedTick = Math.Max(0, row.acceptedTick);
            row.interactionTick = Math.Max(0, row.interactionTick);
            row.dueTick = Math.Max(row.acceptedTick, row.dueTick);
            row.deadlineTick = Math.Max(row.dueTick, row.deadlineTick);
            // A hand-edited or damaged save may put the next wake after the prose deadline. Clamp it
            // back to that terminal attempt so the row cannot remain pending beyond its frozen grace.
            row.nextAttemptTick = Math.Min(
                row.deadlineTick,
                Math.Max(row.dueTick, row.nextAttemptTick));
            if (row.retryIntervalTicks <= 0)
            {
                // Additive migration for rows accepted before retry cadence became frozen.
                row.retryIntervalTicks = policy.retryIntervalTicks;
            }
            row.retryIntervalTicks = Math.Max(1, row.retryIntervalTicks);
            return true;
        }

        private static List<SocialReflectionPairCooldownState>
            NormalizeSocialReflectionPairCooldowns(
                List<SocialReflectionPairCooldownState> source,
                int now,
                int maximum)
        {
            Dictionary<string, SocialReflectionPairCooldownState> byKey =
                new Dictionary<string, SocialReflectionPairCooldownState>(
                    StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    SocialReflectionPairCooldownState row = source[i];
                    if (row == null || row.cooldownUntilTick <= now)
                    {
                        continue;
                    }

                    row.pairKey = CleanSocialReflectionValue(row.pairKey);
                    row.relationSignature =
                        CleanSocialReflectionValue(row.relationSignature);
                    row.reservationSourceKey =
                        CleanSocialReflectionValue(row.reservationSourceKey);
                    if (row.pairKey.Length == 0
                        || row.reservationSourceKey.Length == 0)
                    {
                        continue;
                    }

                    SocialReflectionPairCooldownState existing;
                    if (!byKey.TryGetValue(row.pairKey, out existing)
                        || row.cooldownUntilTick > existing.cooldownUntilTick)
                    {
                        byKey[row.pairKey] = row;
                    }
                }
            }

            List<SocialReflectionPairCooldownState> result =
                new List<SocialReflectionPairCooldownState>(byKey.Values);
            result.Sort((left, right) =>
                right.cooldownUntilTick.CompareTo(left.cooldownUntilTick));
            if (result.Count > maximum)
            {
                result.RemoveRange(maximum, result.Count - maximum);
            }
            return result;
        }

        private static List<SocialReflectionWriterCooldownState>
            NormalizeSocialReflectionWriterCooldowns(
                List<SocialReflectionWriterCooldownState> source,
                int now,
                int maximum)
        {
            Dictionary<string, SocialReflectionWriterCooldownState> byKey =
                new Dictionary<string, SocialReflectionWriterCooldownState>(
                    StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    SocialReflectionWriterCooldownState row = source[i];
                    if (row == null || row.cooldownUntilTick <= now)
                    {
                        continue;
                    }

                    row.writerPawnId =
                        CleanSocialReflectionValue(row.writerPawnId);
                    row.reservationSourceKey =
                        CleanSocialReflectionValue(row.reservationSourceKey);
                    if (row.writerPawnId.Length == 0
                        || row.reservationSourceKey.Length == 0)
                    {
                        continue;
                    }

                    SocialReflectionWriterCooldownState existing;
                    if (!byKey.TryGetValue(row.writerPawnId, out existing)
                        || row.cooldownUntilTick > existing.cooldownUntilTick)
                    {
                        byKey[row.writerPawnId] = row;
                    }
                }
            }

            List<SocialReflectionWriterCooldownState> result =
                new List<SocialReflectionWriterCooldownState>(byKey.Values);
            result.Sort((left, right) =>
                right.cooldownUntilTick.CompareTo(left.cooldownUntilTick));
            if (result.Count > maximum)
            {
                result.RemoveRange(maximum, result.Count - maximum);
            }
            return result;
        }

        private static List<SocialReflectionHandledSourceState>
            NormalizeSocialReflectionHandledSources(
                List<SocialReflectionHandledSourceState> source,
                int maximum)
        {
            Dictionary<string, SocialReflectionHandledSourceState> byKey =
                new Dictionary<string, SocialReflectionHandledSourceState>(
                    StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    SocialReflectionHandledSourceState row = source[i];
                    if (row == null)
                    {
                        continue;
                    }

                    row.sourceKey = CleanSocialReflectionValue(row.sourceKey);
                    row.reflectionEventId =
                        CleanSocialReflectionValue(row.reflectionEventId);
                    row.handledTick = Math.Max(0, row.handledTick);
                    if (row.sourceKey.Length == 0)
                    {
                        continue;
                    }

                    SocialReflectionHandledSourceState existing;
                    if (!byKey.TryGetValue(row.sourceKey, out existing)
                        || row.handledTick > existing.handledTick)
                    {
                        byKey[row.sourceKey] = row;
                    }
                }
            }

            List<SocialReflectionHandledSourceState> result =
                new List<SocialReflectionHandledSourceState>(byKey.Values);
            result.Sort((left, right) =>
                right.handledTick.CompareTo(left.handledTick));
            if (result.Count > maximum)
            {
                result.RemoveRange(maximum, result.Count - maximum);
            }
            return result;
        }

        private void EnsureSocialReflectionLists()
        {
            pendingSocialReflections = pendingSocialReflections
                ?? new List<PendingSocialReflectionState>();
            socialReflectionPairCooldowns = socialReflectionPairCooldowns
                ?? new List<SocialReflectionPairCooldownState>();
            socialReflectionWriterCooldowns = socialReflectionWriterCooldowns
                ?? new List<SocialReflectionWriterCooldownState>();
            socialReflectionHandledSources = socialReflectionHandledSources
                ?? new List<SocialReflectionHandledSourceState>();
        }

        private void PruneSocialReflectionCooldowns(int now)
        {
            for (int i = socialReflectionPairCooldowns.Count - 1; i >= 0; i--)
            {
                SocialReflectionPairCooldownState row =
                    socialReflectionPairCooldowns[i];
                if (row == null || row.cooldownUntilTick <= now)
                {
                    socialReflectionPairCooldowns.RemoveAt(i);
                }
            }
            for (int i = socialReflectionWriterCooldowns.Count - 1; i >= 0; i--)
            {
                SocialReflectionWriterCooldownState row =
                    socialReflectionWriterCooldowns[i];
                if (row == null || row.cooldownUntilTick <= now)
                {
                    socialReflectionWriterCooldowns.RemoveAt(i);
                }
            }
        }

        private PendingSocialReflectionState FindPendingSocialReflectionBySource(
            string sourceKey)
        {
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                if (row != null && string.Equals(
                    row.sourceKey, sourceKey, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        private PendingSocialReflectionState FindPendingSocialReflectionByWriter(
            string writerPawnId)
        {
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                if (row != null && string.Equals(
                    row.writerPawnId, writerPawnId, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        private PendingSocialReflectionState FindPendingSocialReflectionByPair(
            string pairKey)
        {
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                if (row != null && string.Equals(
                    row.pairKey, pairKey, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        private SocialReflectionWriterCooldownState
            FindSocialReflectionWriterCooldown(string writerPawnId)
        {
            for (int i = 0; i < socialReflectionWriterCooldowns.Count; i++)
            {
                SocialReflectionWriterCooldownState row =
                    socialReflectionWriterCooldowns[i];
                if (row != null && string.Equals(
                    row.writerPawnId, writerPawnId, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        private SocialReflectionPairCooldownState
            FindSocialReflectionPairCooldown(string pairKey)
        {
            for (int i = 0; i < socialReflectionPairCooldowns.Count; i++)
            {
                SocialReflectionPairCooldownState row =
                    socialReflectionPairCooldowns[i];
                if (row != null && string.Equals(
                    row.pairKey, pairKey, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        private bool HasHandledSocialReflectionSource(string sourceKey)
        {
            for (int i = 0; i < socialReflectionHandledSources.Count; i++)
            {
                SocialReflectionHandledSourceState row =
                    socialReflectionHandledSources[i];
                if (row != null && string.Equals(
                    row.sourceKey, sourceKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private void UpsertSocialReflectionWriterCooldown(
            SocialReflectionWriterCooldownState replacement)
        {
            SocialReflectionWriterCooldownState existing =
                FindSocialReflectionWriterCooldown(replacement.writerPawnId);
            if (existing != null)
            {
                socialReflectionWriterCooldowns.Remove(existing);
            }
            socialReflectionWriterCooldowns.Add(replacement);
        }

        private void UpsertSocialReflectionPairCooldown(
            SocialReflectionPairCooldownState replacement)
        {
            SocialReflectionPairCooldownState existing =
                FindSocialReflectionPairCooldown(replacement.pairKey);
            if (existing != null)
            {
                socialReflectionPairCooldowns.Remove(existing);
            }
            socialReflectionPairCooldowns.Add(replacement);
        }

        private void RemoveSocialReflectionPairCooldown(string pairKey)
        {
            SocialReflectionPairCooldownState existing =
                FindSocialReflectionPairCooldown(pairKey);
            if (existing != null)
            {
                socialReflectionPairCooldowns.Remove(existing);
            }
        }

        private void AddHandledSocialReflectionSource(
            string sourceKey,
            string reflectionEventId,
            int now,
            int maximum)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return;
            }

            for (int i = socialReflectionHandledSources.Count - 1; i >= 0; i--)
            {
                if (string.Equals(
                    socialReflectionHandledSources[i]?.sourceKey,
                    sourceKey,
                    StringComparison.Ordinal))
                {
                    socialReflectionHandledSources.RemoveAt(i);
                }
            }
            socialReflectionHandledSources.Add(
                new SocialReflectionHandledSourceState
                {
                    sourceKey = sourceKey,
                    reflectionEventId = reflectionEventId ?? string.Empty,
                    handledTick = Math.Max(0, now)
                });
            // A handled row is the terminal source owner, including while that source is pending.
            // A deliberately small XML cap may discard old terminal history, but never evict an
            // active pending source and accidentally make it admissible again after cancellation.
            HashSet<string> pendingSources =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState pending = pendingSocialReflections[i];
                if (pending != null && !string.IsNullOrWhiteSpace(pending.sourceKey))
                {
                    pendingSources.Add(pending.sourceKey);
                }
            }
            int cap = Math.Max(1, Math.Max(maximum, pendingSources.Count));
            while (socialReflectionHandledSources.Count > cap)
            {
                int oldestIndex = -1;
                for (int i = 0; i < socialReflectionHandledSources.Count; i++)
                {
                    SocialReflectionHandledSourceState candidate =
                        socialReflectionHandledSources[i];
                    if (candidate != null && pendingSources.Contains(candidate.sourceKey))
                    {
                        continue;
                    }

                    int candidateTick = candidate?.handledTick ?? int.MinValue;
                    if (oldestIndex < 0
                        || candidateTick
                            < (socialReflectionHandledSources[oldestIndex]?.handledTick
                                ?? int.MinValue))
                    {
                        oldestIndex = i;
                    }
                }
                if (oldestIndex < 0)
                {
                    break;
                }
                socialReflectionHandledSources.RemoveAt(oldestIndex);
            }
        }

        private void RecalculateNextSocialReflectionTick(int now)
        {
            if (pendingSocialReflections.Count == 0)
            {
                nextSocialReflectionSchedulerTick = 0;
                return;
            }

            int next = int.MaxValue;
            for (int i = 0; i < pendingSocialReflections.Count; i++)
            {
                PendingSocialReflectionState row = pendingSocialReflections[i];
                if (row != null)
                {
                    next = Math.Min(next, row.nextAttemptTick);
                }
            }
            nextSocialReflectionSchedulerTick = next <= now
                ? SocialReflectionSafeAdd(now, 1)
                : next;
        }

        private static int ComparePendingSocialReflectionDue(
            PendingSocialReflectionState left,
            PendingSocialReflectionState right)
        {
            int byAttempt = (left?.nextAttemptTick ?? int.MaxValue)
                .CompareTo(right?.nextAttemptTick ?? int.MaxValue);
            if (byAttempt != 0)
            {
                return byAttempt;
            }
            return (left?.acceptedTick ?? int.MaxValue)
                .CompareTo(right?.acceptedTick ?? int.MaxValue);
        }

        private static int SocialReflectionSafeAdd(int tick, int duration)
        {
            long result = (long)Math.Max(0, tick) + Math.Max(0, duration);
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }

        private static string CleanSocialReflectionValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }

        private static string BoundSocialReflectionText(string value, int maximum)
        {
            string cleaned = CleanSocialReflectionValue(value);
            int cap = Math.Max(0, maximum);
            if (cleaned.Length <= cap)
            {
                return cleaned;
            }
            return cap == 0
                ? string.Empty
                : TextTruncation.SafePrefix(cleaned, cap);
        }

        private enum SocialReflectionSourceState
        {
            Retry,
            Ready,
            FactsOnly
        }

        private sealed class SocialReflectionSourceCandidate
        {
            public string eventId = string.Empty;
            public string title = string.Empty;
            public string status = string.Empty;
            public string generatedText = string.Empty;
            public bool archivedGenerationStale;
        }

        private sealed class SocialReflectionSourceCandidateAccumulator
        {
            private readonly HashSet<string> eventIds =
                new HashSet<string>(StringComparer.Ordinal);

            public SocialReflectionSourceCandidate candidate;
            public bool ambiguous;

            public void Add(
                SocialReflectionSourceCandidate value,
                string matchedEventId)
            {
                string id = value?.eventId ?? matchedEventId ?? string.Empty;
                if (eventIds.Contains(id))
                {
                    return;
                }
                if (!string.IsNullOrWhiteSpace(id))
                {
                    eventIds.Add(id);
                }

                if (value == null)
                {
                    // A PlayLog match that cannot prove this exact pair is ambiguous ambient/source
                    // evidence and must never be quoted as if it were canonical.
                    ambiguous = true;
                    return;
                }

                if (candidate != null)
                {
                    ambiguous = true;
                    return;
                }
                candidate = value;
            }
        }

        private sealed class SocialReflectionSourceResolution
        {
            public SocialReflectionSourceState state;
            public string title = string.Empty;
            public string ending = string.Empty;

            public static SocialReflectionSourceResolution Retry()
            {
                return new SocialReflectionSourceResolution
                {
                    state = SocialReflectionSourceState.Retry
                };
            }

            public static SocialReflectionSourceResolution FactsOnly()
            {
                return new SocialReflectionSourceResolution
                {
                    state = SocialReflectionSourceState.FactsOnly
                };
            }

            public static SocialReflectionSourceResolution FromCandidate(
                SocialReflectionSourceCandidate candidate,
                SocialReflectionPolicySnapshot policy)
            {
                if (candidate == null)
                {
                    return FactsOnly();
                }
                bool complete = DiaryEvent.RoleEquals(
                    candidate.status, DiaryEvent.CompleteStatus);
                if (!complete || candidate.archivedGenerationStale
                    || string.IsNullOrWhiteSpace(candidate.generatedText))
                {
                    bool stillPending = DiaryEvent.RoleEquals(
                            candidate.status, DiaryEvent.PendingStatus)
                        || DiaryEvent.RoleEquals(
                            candidate.status, DiaryEvent.NotGeneratedStatus);
                    return stillPending ? Retry() : FactsOnly();
                }

                SocialReflectionPolicySnapshot safe =
                    policy ?? SocialReflectionPolicySnapshot.CreateDefault();
                string title = DiaryLineCleaner.CleanLine(candidate.title);
                if (title.Length > safe.maximumSourceLabelCharacters)
                {
                    title = TextTruncation.SafePrefix(
                        title, safe.maximumSourceLabelCharacters);
                }
                return new SocialReflectionSourceResolution
                {
                    state = SocialReflectionSourceState.Ready,
                    title = title,
                    ending = SocialReflectionPolicy.SourceExcerpt(
                        candidate.generatedText,
                        safe.sourceExcerptSentenceCount,
                        safe.sourceExcerptMaximumCharacters)
                };
            }
        }
    }
}
