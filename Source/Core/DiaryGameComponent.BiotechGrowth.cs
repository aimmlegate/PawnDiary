// Biotech growth orchestration. Harmony adapters capture exact before/after snapshots here; pure
// policy verifies the mutation; this component persists postponed ownership, consumes ordinary
// progression baselines, attaches Phase-2 family/supporter state, and either dispatches one canonical
// page or releases the normal birthday.
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
        // Saved because the vanilla ChoiceLetter may be postponed across save/load. The row contains
        // no live letter/Pawn reference; RimWorld owns and scribes the actual letter separately.
        private List<PendingBiotechGrowthMoment> pendingBiotechGrowthMoments =
            new List<PendingBiotechGrowthMoment>();

        /// <summary>Starts exact before-state capture for a canonical Biotech growth birthday.</summary>
        internal BiotechGrowthBirthdayState BeginBiotechGrowthBirthday(Pawn pawn, int birthdayAge)
        {
            if (!BiotechGrowthLetterPatch.HooksReady
                || !ModsConfig.BiotechActive
                || !GamePlaying
                || pawn?.ageTracker == null
                || BiotechGrowthStageTokens.ForAge(birthdayAge).Length == 0)
            {
                return null;
            }

            GrowthPawnSnapshot before;
            int growthTier = pawn.ageTracker.GrowthTier;
            if (!DlcContext.TryCaptureGrowthPawn(
                pawn,
                birthdayAge,
                growthTier,
                hasNewResponsibilities: false,
                out before))
            {
                return null;
            }

            return new BiotechGrowthBirthdayState
            {
                pawn = pawn,
                pawnId = before.pawnId,
                birthdayAge = birthdayAge,
                birthdayTick = Find.TickManager.TicksGame,
                growthTier = growthTier,
                familyArcId = EnsureBiotechGrowthFamilyArcId(pawn, before.pawnId),
                beforeSnapshot = before,
                disabledWorkTypesBefore = DlcContext.GrowthDisabledWorkTypeDefNames(pawn)
            };
        }

        /// <summary>
        /// Claims the active birthday only after vanilla configured a real ChoiceLetter_GrowthMoment.
        /// </summary>
        internal void ObserveBiotechGrowthLetterConfigured(
            Pawn pawn,
            int growthTier,
            bool newResponsibilities)
        {
            BiotechGrowthBirthdayState birthday = BiotechGrowthCorrelation.CurrentBirthdayFor(pawn);
            if (birthday == null || birthday.beforeSnapshot == null)
            {
                return;
            }

            birthday.growthTier = Math.Max(0, Math.Min(8, growthTier));
            birthday.beforeSnapshot.growthTier = birthday.growthTier;
            int now = Find.TickManager?.TicksGame ?? birthday.birthdayTick;
            PendingBiotechGrowthMoment row = new PendingBiotechGrowthMoment
            {
                pawnId = birthday.pawnId,
                birthdayAge = birthday.birthdayAge,
                birthdayTick = birthday.birthdayTick,
                configuredTick = now,
                growthTier = birthday.growthTier,
                newResponsibilities = newResponsibilities,
                correlationId = BiotechArcKeys.GrowthCorrelation(birthday.pawnId, birthday.birthdayAge),
                familyArcId = birthday.familyArcId,
                birthdaySnapshot = birthday.beforeSnapshot
            };

            RemovePendingBiotechGrowth(row.pawnId, row.birthdayAge);
            pendingBiotechGrowthMoments.Add(row);
            pendingBiotechGrowthMoments = PendingBiotechGrowthMomentPolicy.Normalize(
                pendingBiotechGrowthMoments,
                now,
                DiaryBiotechPolicy.Snapshot().maximumPendingGrowthRows);
            // If an extreme/corrupt state hit the defensive cap and displaced this row, do not suppress
            // the ordinary Birthday owner. The patch will fail open instead of claiming unsaved work.
            birthday.configuredLetterOwnsBirthday = PendingBiotechGrowthMomentPolicy.FindNewest(
                pendingBiotechGrowthMoments,
                row.pawnId,
                row.birthdayAge) != null;
        }

        /// <summary>
        /// Finishes the biological-birthday call. True means canonical growth owned or deliberately
        /// consumed the birthday; false tells the patch to preserve the original Birthday event path.
        /// </summary>
        internal bool TryFinishBiotechGrowthBirthday(BiotechGrowthBirthdayState birthday)
        {
            if (birthday == null || birthday.pawn == null || birthday.beforeSnapshot == null)
            {
                return false;
            }

            if (birthday.configuredLetterOwnsBirthday)
            {
                return true;
            }

            try
            {
                HashSet<string> afterDisabled = DlcContext.GrowthDisabledWorkTypeDefNames(birthday.pawn);
                bool responsibilitiesOpened = AnyWorkTypeOpened(
                    birthday.disabledWorkTypesBefore,
                    afterDisabled);
                GrowthPawnSnapshot after;
                if (!DlcContext.TryCaptureGrowthPawn(
                    birthday.pawn,
                    birthday.birthdayAge,
                    birthday.growthTier,
                    responsibilitiesOpened,
                    out after))
                {
                    return false;
                }

                GrowthMomentMutation mutation = GrowthMomentPolicy.Diff(
                    birthday.beforeSnapshot,
                    after,
                    new GrowthCommittedChoice
                    {
                        sourceToken = BiotechGrowthSourceTokens.AutoResolved,
                        familyArcId = birthday.familyArcId
                    },
                    DiaryBiotechPolicy.Snapshot());
                if (mutation == null)
                {
                    return false;
                }

                CompleteBiotechGrowth(
                    birthday.pawn,
                    birthday.beforeSnapshot,
                    after,
                    mutation,
                    birthday.birthdayAge);
                return true;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Biotech auto-growth capture failed; preserving the ordinary birthday: "
                    + exception,
                    "PawnDiary.BiotechGrowth.Auto".GetHashCode());
                return false;
            }
        }

        /// <summary>Snapshots the final pre-choice state for a configured, possibly reloaded letter.</summary>
        internal BiotechGrowthChoiceState BeginBiotechGrowthChoice(
            Pawn pawn,
            int growthTier,
            bool choiceAlreadyMade,
            List<SkillDef> selectedSkills,
            Trait selectedTrait)
        {
            if (!BiotechGrowthLetterPatch.HooksReady
                || choiceAlreadyMade
                || !ModsConfig.BiotechActive
                || pawn?.ageTracker == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            int age = (int)pawn.ageTracker.AgeBiologicalYears;
            PendingBiotechGrowthMoment pending = PendingBiotechGrowthMomentPolicy.FindNewest(
                pendingBiotechGrowthMoments,
                pawnId,
                age);
            if (pending == null)
            {
                return null;
            }

            GrowthPawnSnapshot before;
            if (!DlcContext.TryCaptureGrowthPawn(
                pawn,
                pending.birthdayAge,
                pending.growthTier,
                hasNewResponsibilities: false,
                out before))
            {
                return null;
            }

            // Naming is part of the growth choice UI, so compare the birthday-time visible name with
            // the post-choice name. Traits/passions still use this final pre-choice snapshot so an
            // unrelated mutation while the letter was postponed is never misattributed to the choice.
            before.shortName = pending.birthdaySnapshot?.shortName ?? before.shortName;
            before.growthTier = pending.growthTier;
            before.hasNewResponsibilities = false;
            return new BiotechGrowthChoiceState
            {
                pawn = pawn,
                pending = pending,
                beforeSnapshot = before,
                choiceWasAlreadyMade = choiceAlreadyMade,
                committedChoice = new GrowthCommittedChoice
                {
                    selectedTraitKey = DlcContext.GrowthTraitKey(selectedTrait),
                    selectedPassionSkillDefNames = SkillDefNames(selectedSkills),
                    sourceToken = BiotechGrowthSourceTokens.PlayerChoice,
                    familyArcId = string.IsNullOrWhiteSpace(pending.familyArcId)
                        ? EnsureBiotechGrowthFamilyArcId(pawn, pending.pawnId)
                        : pending.familyArcId
                }
            };
        }

        /// <summary>Completes only a verified false-to-true MakeChoices transition.</summary>
        internal void FinishBiotechGrowthChoice(BiotechGrowthChoiceState choice, bool choiceMadeAfter)
        {
            if (choice == null || choice.choiceWasAlreadyMade || !choiceMadeAfter
                || choice.pawn == null || choice.pending == null)
            {
                return;
            }

            PendingBiotechGrowthMoment pending = choice.pending;
            GrowthPawnSnapshot after = null;
            try
            {
                DlcContext.TryCaptureGrowthPawn(
                    choice.pawn,
                    pending.birthdayAge,
                    pending.growthTier,
                    pending.newResponsibilities,
                    out after);
                GrowthMomentMutation mutation = GrowthMomentPolicy.Diff(
                    choice.beforeSnapshot,
                    after,
                    choice.committedChoice,
                    DiaryBiotechPolicy.Snapshot());
                CompleteBiotechGrowth(
                    choice.pawn,
                    choice.beforeSnapshot,
                    after,
                    mutation,
                    pending.birthdayAge);
            }
            catch (Exception exception)
            {
                // The vanilla choice already committed. Advance scanner baselines and release the
                // mature Birthday path; retrying this owner would invent a second growth page.
                Log.ErrorOnce(
                    "[Pawn Diary] Biotech growth-choice capture failed; releasing the ordinary birthday: "
                    + exception,
                    "PawnDiary.BiotechGrowth.Choice".GetHashCode());
                ReleaseBiotechGrowthToOrdinaryBirthday(
                    choice.pawn,
                    choice.beforeSnapshot,
                    after,
                    pending.birthdayAge);
            }
            finally
            {
                RemovePendingBiotechGrowth(pending.pawnId, pending.birthdayAge);
            }
        }

        /// <summary>Exposes the additive pending-growth list under its frozen Phase-0 Scribe key.</summary>
        private void ExposeBiotechGrowthData()
        {
            Scribe_Collections.Look(
                ref pendingBiotechGrowthMoments,
                BiotechSaveKeys.PendingGrowthMoments,
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int now = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
                BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
                pendingBiotechGrowthMoments = PendingBiotechGrowthMomentPolicy.Normalize(
                    pendingBiotechGrowthMoments,
                    now,
                    policy.maximumPendingGrowthRows);
            }
        }

        /// <summary>Clears new-game saved ownership and every transient correlation scope.</summary>
        private void ResetBiotechGrowthForNewGame()
        {
            pendingBiotechGrowthMoments.Clear();
            BiotechGrowthCorrelation.Clear();
        }

        /// <summary>Clears only transient scope state; loaded pending rows must survive.</summary>
        private static void ResetBiotechGrowthTransientState()
        {
            BiotechGrowthCorrelation.Clear();
        }

        /// <summary>
        /// Removes expired/corrupt postponed ownership on the XML progression-scan cadence. A missing
        /// pawn is discarded silently; a still-live pawn receives the mature ordinary fallback once.
        /// </summary>
        private void MaintainPendingBiotechGrowthMoments()
        {
            if (pendingBiotechGrowthMoments == null || pendingBiotechGrowthMoments.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            pendingBiotechGrowthMoments = PendingBiotechGrowthMomentPolicy.Normalize(
                pendingBiotechGrowthMoments,
                now,
                policy.maximumPendingGrowthRows);
            for (int i = pendingBiotechGrowthMoments.Count - 1; i >= 0; i--)
            {
                PendingBiotechGrowthMoment pending = pendingBiotechGrowthMoments[i];
                bool expired = PendingBiotechGrowthMomentPolicy.IsExpired(
                    pending,
                    now,
                    policy.growthPendingExpiryTicks);
                Pawn pawn = null;
                bool impossibleAfterGrace = false;
                if (!expired && PendingBiotechGrowthMomentPolicy.IsPastFallbackGrace(
                    pending,
                    now,
                    policy.growthFallbackGraceTicks))
                {
                    pawn = FindLivePawnByLoadId(pending.pawnId);
                    impossibleAfterGrace = pawn != null
                        && (pawn.ageTracker == null
                            || (int)pawn.ageTracker.AgeBiologicalYears != pending.birthdayAge);
                }

                if (!expired && !impossibleAfterGrace)
                {
                    continue;
                }

                pendingBiotechGrowthMoments.RemoveAt(i);
                pawn = pawn ?? FindLivePawnByLoadId(pending.pawnId);
                if (pawn == null || pawn.DestroyedOrNull())
                {
                    continue;
                }

                PawnProgressionState progression = ProgressionStateForGrowth(pawn, create: false);
                if (progression?.EnsureBiotechState().HasConsumedGrowthAge(pending.birthdayAge) == true)
                {
                    continue;
                }

                if (HasRecordedBiotechGrowth(pending.pawnId, pending.birthdayAge))
                {
                    GrowthPawnSnapshot recordedCurrent;
                    DlcContext.TryCaptureGrowthPawn(
                        pawn,
                        pending.birthdayAge,
                        pending.growthTier,
                        pending.newResponsibilities,
                        out recordedCurrent);
                    ConsumeBiotechGrowthProgression(
                        pawn,
                        pending.birthdaySnapshot,
                        recordedCurrent,
                        pending.birthdayAge);
                    continue;
                }

                GrowthPawnSnapshot current;
                DlcContext.TryCaptureGrowthPawn(
                    pawn,
                    pending.birthdayAge,
                    pending.growthTier,
                    pending.newResponsibilities,
                    out current);
                ReleaseBiotechGrowthToOrdinaryBirthday(
                    pawn,
                    pending.birthdaySnapshot,
                    current,
                    pending.birthdayAge);
            }
        }

        private void CompleteBiotechGrowth(
            Pawn pawn,
            GrowthPawnSnapshot before,
            GrowthPawnSnapshot after,
            GrowthMomentMutation mutation,
            int birthdayAge)
        {
            PawnProgressionState progression = ProgressionStateForGrowth(
                pawn,
                create: IsDiaryEligible(pawn));
            if (progression?.EnsureBiotechState().HasConsumedGrowthAge(birthdayAge) == true)
            {
                return;
            }

            Pawn supporterPawn = null;
            BiotechFamilyArcState familyArc = mutation == null
                ? null
                : PrepareBiotechGrowthFamily(pawn, mutation, out supporterPawn);
            if (familyArc?.recordedGrowthAges?.Contains(birthdayAge) == true)
            {
                ConsumeBiotechGrowthProgression(pawn, before, after, birthdayAge);
                return;
            }

            if (HasRecordedBiotechGrowth(pawn?.GetUniqueLoadID(), birthdayAge))
            {
                ConsumeBiotechGrowthProgression(pawn, before, after, birthdayAge);
                MarkBiotechGrowthFamilySummarized(familyArc, birthdayAge);
                return;
            }

            bool emitted = false;
            if (mutation != null)
            {
                GrowthMomentEventData payload = new GrowthMomentEventData
                {
                    PawnId = mutation.childId,
                    ChildId = mutation.childId,
                    Age = mutation.age,
                    ChildEligible = IsDiaryEligible(pawn),
                    SupporterId = mutation.supporter?.adultId ?? string.Empty,
                    SupporterEligible = IsDiaryEligible(supporterPawn),
                    HasVerifiedMutation = true,
                    AlreadyRecorded = false
                };
                emitted = Dispatch(new GrowthMomentSignal(
                    payload,
                    pawn,
                    supporterPawn,
                    mutation,
                    familyArc));
                // Settings can suppress the page, but the canonical birthday still consumes the
                // current upbringing interval so the same lesson history is not narrated at age 10/13.
                MarkBiotechGrowthFamilySummarized(familyArc, birthdayAge);
            }

            ConsumeBiotechGrowthProgression(pawn, before, after, birthdayAge);
            if (!emitted)
            {
                // RecordEventWindowBirthday owns the legacy setting and silently no-ops when that row
                // is disabled, unavailable, or the child is not an eligible diary writer.
                ReleaseBiotechGrowthToOrdinaryBirthday(
                    pawn,
                    before,
                    after,
                    birthdayAge,
                    progressionAlreadyConsumed: true);
            }
        }

        private void ReleaseBiotechGrowthToOrdinaryBirthday(
            Pawn pawn,
            GrowthPawnSnapshot before,
            GrowthPawnSnapshot after,
            int birthdayAge,
            bool progressionAlreadyConsumed = false)
        {
            if (!progressionAlreadyConsumed)
            {
                try
                {
                    ConsumeBiotechGrowthProgression(pawn, before, after, birthdayAge);
                }
                catch (Exception exception)
                {
                    Log.ErrorOnce(
                        "[Pawn Diary] Could not consume Biotech growth progression baselines; "
                        + "the ordinary birthday will still be released: " + exception,
                        "PawnDiary.BiotechGrowth.FallbackBaseline".GetHashCode());
                }
            }

            try
            {
                RecordEventWindowBirthday(pawn, birthdayAge);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Could not release the ordinary birthday after Biotech growth fallback: "
                    + exception,
                    "PawnDiary.BiotechGrowth.FallbackBirthday".GetHashCode());
            }
        }

        private void ConsumeBiotechGrowthProgression(
            Pawn pawn,
            GrowthPawnSnapshot before,
            GrowthPawnSnapshot after,
            int birthdayAge)
        {
            PawnProgressionState state = ProgressionStateForGrowth(pawn, create: IsDiaryEligible(pawn));
            if (state == null)
            {
                return;
            }

            if (after != null)
            {
                state.knownTraitKeys = TraitKeys(after.traits);
                state.baselineTraitGainOnNextScan = false;
                ConsumePassionSkillMilestones(state, before?.skills, after.skills);
            }

            state.EnsureBiotechState().ConsumeGrowthAge(birthdayAge);
        }

        private PawnProgressionState ProgressionStateForGrowth(Pawn pawn, bool create)
        {
            if (pawn == null)
            {
                return null;
            }

            PawnDiaryRecord diary = FindDiary(pawn, create);
            return diary?.EnsureProgressionState();
        }

        private bool HasRecordedBiotechGrowth(string childId, int birthdayAge)
        {
            if (string.IsNullOrWhiteSpace(childId))
            {
                return false;
            }

            // A supporter-solo page belongs to the adult's diary, not the young child's. Search the
            // bounded hot store and archive by the stable child/age fields instead of assuming which
            // pawn owned the page.
            IReadOnlyList<DiaryEvent> live = events.AllEvents;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = live[i];
                if (RecordedBiotechGrowthMatches(
                    diaryEvent?.interactionDefName,
                    diaryEvent?.gameContext,
                    childId,
                    birthdayAge))
                {
                    return true;
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.AllEntries;
            for (int i = archived.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (RecordedBiotechGrowthMatches(
                    entry?.interactionDefName,
                    entry?.decorationGameContext,
                    childId,
                    birthdayAge))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RecordedBiotechGrowthMatches(
            string interactionDefName,
            string gameContext,
            string childId,
            int birthdayAge)
        {
            return GrowthRecordPolicy.Matches(
                interactionDefName,
                DiaryContextFields.Value(gameContext, BiotechContextKeys.ChildId),
                DiaryContextFields.Value(gameContext, BiotechContextKeys.BirthdayAge),
                childId,
                birthdayAge);
        }

        private static void ConsumePassionSkillMilestones(
            PawnProgressionState state,
            IList<GrowthSkillFact> before,
            IList<GrowthSkillFact> after)
        {
            if (state == null || after == null)
            {
                return;
            }

            Dictionary<string, GrowthSkillFact> oldByKey = new Dictionary<string, GrowthSkillFact>(
                StringComparer.OrdinalIgnoreCase);
            if (before != null)
            {
                for (int i = 0; i < before.Count; i++)
                {
                    GrowthSkillFact fact = before[i];
                    if (fact != null && !string.IsNullOrWhiteSpace(fact.skillDefName))
                    {
                        oldByKey[fact.skillDefName] = fact;
                    }
                }
            }

            for (int i = 0; i < after.Count; i++)
            {
                GrowthSkillFact current = after[i];
                if (current == null || string.IsNullOrWhiteSpace(current.skillDefName))
                {
                    continue;
                }

                GrowthSkillFact previous;
                string previousPassion = oldByKey.TryGetValue(current.skillDefName, out previous)
                    ? previous.passion
                    : BiotechPassionTokens.None;
                if (BiotechPassionTokens.Rank(current.passion)
                    <= BiotechPassionTokens.Rank(previousPassion))
                {
                    continue;
                }

                int oldMilestone = state.HighestSkillMilestone(current.skillDefName);
                ProgressionMilestoneDecision decision = ProgressionMilestonePolicy.EvaluateSkillMilestone(
                    current.level,
                    hasPassion: true,
                    DiaryTuning.Current.progressionSkillMilestones,
                    oldMilestone,
                    baselineMode: true);
                if (decision.newHighestMilestone > oldMilestone)
                {
                    state.SetSkillMilestone(current.skillDefName, decision.newHighestMilestone);
                }
            }
        }

        private static List<string> TraitKeys(IList<GrowthTraitFact> traits)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (traits == null)
            {
                return result;
            }

            for (int i = 0; i < traits.Count; i++)
            {
                string key = (traits[i]?.traitKey ?? string.Empty).Trim();
                if (key.Length > 0 && seen.Add(key))
                {
                    result.Add(key);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool AnyWorkTypeOpened(HashSet<string> before, HashSet<string> after)
        {
            if (before == null || before.Count == 0)
            {
                return false;
            }

            after = after ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string defName in before)
            {
                if (!after.Contains(defName))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> SkillDefNames(List<SkillDef> skills)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (skills == null)
            {
                return result;
            }

            for (int i = 0; i < skills.Count; i++)
            {
                string defName = skills[i]?.defName;
                if (!string.IsNullOrWhiteSpace(defName) && seen.Add(defName))
                {
                    result.Add(defName);
                }
            }

            return result;
        }

        private void RemovePendingBiotechGrowth(string pawnId, int birthdayAge)
        {
            pendingBiotechGrowthMoments.RemoveAll(row => row != null
                && row.birthdayAge == birthdayAge
                && string.Equals(row.pawnId, pawnId, StringComparison.Ordinal));
        }
    }
}
