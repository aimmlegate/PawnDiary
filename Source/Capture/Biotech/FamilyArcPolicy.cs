// Pure saved-family-arc policy. Guarded runtime adapters supply stable IDs and exact relationship or
// activity facts; this file opens/repairs arcs, records bounded evidence, and decides compaction
// without reading Pawn, Hediff, settings, DefDatabase, or any other live RimWorld object.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Owns deterministic family-arc mutation, normalization, and retention decisions.</summary>
    internal static class FamilyArcPolicy
    {
        private const string FamilyPrefix = "biotech-family|";

        /// <summary>Maps an XML-owned exact Hediff defName to pregnancy or labor.</summary>
        public static string ClassifyFamilyHediff(string defName, BiotechPolicySnapshot policy)
        {
            string value = (defName ?? string.Empty).Trim();
            if (value.Length == 0 || policy == null) return string.Empty;
            if (Contains(policy.familyPregnancyHediffDefNames, value))
            {
                return BiotechFamilyHediffKindTokens.Pregnancy;
            }
            return Contains(policy.familyLaborHediffDefNames, value)
                ? BiotechFamilyHediffKindTokens.Labor
                : string.Empty;
        }

        /// <summary>Maps an exact interaction defName to a stable activity token.</summary>
        public static string ClassifyInteraction(string defName, BiotechPolicySnapshot policy)
        {
            string value = (defName ?? string.Empty).Trim();
            if (value.Length == 0 || policy == null)
            {
                return string.Empty;
            }

            if (Contains(policy.familyActivityExactDefNames, value))
            {
                return BiotechFamilyActivityKindTokens.BabyPlay;
            }

            List<string> prefixes = policy.familyActivityPrefixes;
            if (prefixes != null)
            {
                for (int i = 0; i < prefixes.Count; i++)
                {
                    string prefix = (prefixes[i] ?? string.Empty).Trim();
                    if (prefix.Length > 0 && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return BiotechFamilyActivityKindTokens.Lesson;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>Maps an accepted social-memory defName to its evidence orientation.</summary>
        public static string ClassifyLessonMemory(string defName, BiotechPolicySnapshot policy)
        {
            string value = (defName ?? string.Empty).Trim();
            if (value.Length == 0 || policy == null)
            {
                return string.Empty;
            }

            if (Contains(policy.familyLessonAdultThoughtDefNames, value))
            {
                return BiotechFamilyMemoryKindTokens.AdultRememberedLesson;
            }

            return Contains(policy.familyLessonChildThoughtDefNames, value)
                ? BiotechFamilyMemoryKindTokens.ChildRememberedLesson
                : string.Empty;
        }

        /// <summary>
        /// Repairs malformed rows, merges duplicate arc/supporter IDs, and returns stable sorted state.
        /// The caller supplies the XML-owned supporter-row cap; no live state is consulted.
        /// </summary>
        public static List<BiotechFamilyArcState> Normalize(
            IList<BiotechFamilyArcState> source,
            int currentTick,
            int maximumSupporterRows)
        {
            int now = Math.Max(0, currentTick);
            int supporterCap = Math.Max(1, maximumSupporterRows);
            Dictionary<string, BiotechFamilyArcState> byId =
                new Dictionary<string, BiotechFamilyArcState>(StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    BiotechFamilyArcState row = NormalizeArc(source[i], now, supporterCap);
                    if (row == null)
                    {
                        continue;
                    }

                    BiotechFamilyArcState existing;
                    if (byId.TryGetValue(row.familyArcId, out existing))
                    {
                        MergeArc(existing, row, now, supporterCap);
                    }
                    else
                    {
                        byId.Add(row.familyArcId, row);
                    }
                }
            }

            List<BiotechFamilyArcState> result = new List<BiotechFamilyArcState>(byId.Values);
            result.Sort((left, right) => string.CompareOrdinal(left.familyArcId, right.familyArcId));
            return result;
        }

        /// <summary>Opens or updates the stable arc represented by one exact pregnancy/labor fact.</summary>
        public static BiotechFamilyArcState ObserveFamilyHediff(
            List<BiotechFamilyArcState> arcs,
            FamilyHediffSnapshot snapshot,
            int maximumSupporterRows)
        {
            if (arcs == null || snapshot == null || !ValidId(snapshot.birtherId)
                || !ValidId(snapshot.hediffId)
                || (snapshot.kindToken != BiotechFamilyHediffKindTokens.Pregnancy
                    && snapshot.kindToken != BiotechFamilyHediffKindTokens.Labor))
            {
                return null;
            }

            BiotechFamilyArcState arc = snapshot.kindToken == BiotechFamilyHediffKindTokens.Labor
                ? FindCompatiblePregnancy(arcs, snapshot)
                : FindByHediff(arcs, snapshot.hediffId);
            if (arc == null)
            {
                // A loaded game can first expose the family during labor. The key shape remains
                // frozen and stable; the labor Hediff's unique ID is the best exact fallback anchor.
                string arcId = BiotechArcKeys.FamilyFromPregnancy(snapshot.birtherId, snapshot.hediffId);
                if (arcId.Length == 0)
                {
                    return null;
                }

                arc = new BiotechFamilyArcState
                {
                    familyArcId = arcId,
                    openedTick = Math.Max(0, snapshot.observedTick),
                    baselineOnly = snapshot.kindToken == BiotechFamilyHediffKindTokens.Labor
                };
                arcs.Add(arc);
            }

            if (snapshot.kindToken == BiotechFamilyHediffKindTokens.Pregnancy)
            {
                arc.pregnancyHediffId = snapshot.hediffId.Trim();
                arc.baselineOnly = false;
            }
            else
            {
                arc.laborHediffId = snapshot.hediffId.Trim();
            }

            CopyHediffParticipants(arc, snapshot);
            arc.lastObservedTick = Math.Max(arc.lastObservedTick, Math.Max(0, snapshot.observedTick));
            arc.supporters = NormalizeSupporters(arc.supporters, Math.Max(1, maximumSupporterRows));
            return arc;
        }

        /// <summary>Finds or creates the child-keyed baseline and merges only exact parent relations.</summary>
        public static BiotechFamilyArcState EnsureChildArc(
            List<BiotechFamilyArcState> arcs,
            FamilyChildSnapshot child,
            int maximumSupporterRows)
        {
            if (arcs == null || child == null || !ValidId(child.childId))
            {
                return null;
            }

            BiotechFamilyArcState arc = FindByChild(arcs, child.childId);
            if (arc == null)
            {
                string arcId = BiotechArcKeys.FamilyFromChild(child.childId);
                if (arcId.Length == 0)
                {
                    return null;
                }

                arc = new BiotechFamilyArcState
                {
                    familyArcId = arcId,
                    childId = child.childId.Trim(),
                    currentChildName = Clean(child.childName),
                    openedTick = Math.Max(0, child.observedTick),
                    lastObservedTick = Math.Max(0, child.observedTick),
                    baselineOnly = true
                };
                arcs.Add(arc);
            }
            else
            {
                arc.childId = child.childId.Trim();
                if (!string.IsNullOrWhiteSpace(child.childName))
                {
                    arc.currentChildName = Clean(child.childName);
                }
                arc.lastObservedTick = Math.Max(arc.lastObservedTick, Math.Max(0, child.observedTick));
            }

            if (child.parents != null)
            {
                for (int i = 0; i < child.parents.Count; i++)
                {
                    FamilyParticipantFact parent = child.parents[i];
                    if (parent == null || !ValidId(parent.pawnId)
                        || (parent.roleToken != BiotechFamilyRoleTokens.Parent
                            && parent.roleToken != BiotechFamilyRoleTokens.BirthParent))
                    {
                        continue;
                    }

                    UpsertSupporter(arc, parent.pawnId, parent.displayName, parent.roleToken, 0,
                        Math.Max(0, child.observedTick));
                }
            }

            arc.supporters = NormalizeSupporters(arc.supporters, Math.Max(1, maximumSupporterRows));
            return arc;
        }

        /// <summary>Records one exact lesson/play observation and returns its child family arc.</summary>
        public static BiotechFamilyArcState ObserveActivity(
            List<BiotechFamilyArcState> arcs,
            FamilyActivityObservation observation,
            int maximumSupporterRows)
        {
            if (arcs == null || observation == null || !ValidId(observation.adultId)
                || !ValidId(observation.childId)
                || observation.adultId.Trim() == observation.childId.Trim()
                || (observation.kindToken != BiotechFamilyActivityKindTokens.BabyPlay
                    && observation.kindToken != BiotechFamilyActivityKindTokens.Lesson))
            {
                return null;
            }

            BiotechFamilyArcState arc = EnsureChildArc(arcs, new FamilyChildSnapshot
            {
                childId = observation.childId,
                childName = observation.childName,
                observedTick = observation.observedTick
            }, maximumSupporterRows);
            if (arc == null)
            {
                return null;
            }

            FamilySupportObservationState supporter = UpsertSupporter(
                arc,
                observation.adultId,
                observation.adultName,
                observation.relationToken,
                1,
                Math.Max(0, observation.observedTick));
            if (supporter == null)
            {
                return null;
            }

            if (observation.kindToken == BiotechFamilyActivityKindTokens.BabyPlay)
            {
                supporter.babyPlayCount = SafeIncrement(supporter.babyPlayCount);
            }
            else
            {
                supporter.lessonCount = SafeIncrement(supporter.lessonCount);
            }

            arc.lastObservedTick = Math.Max(arc.lastObservedTick, Math.Max(0, observation.observedTick));
            arc.detailsCompacted = false;
            arc.supporters = NormalizeSupporters(arc.supporters, Math.Max(1, maximumSupporterRows));
            return arc;
        }

        /// <summary>Marks current observation counters as summarized by one canonical growth age.</summary>
        public static void MarkGrowthSummarized(BiotechFamilyArcState arc, int age, int observedTick)
        {
            if (arc == null || BiotechGrowthStageTokens.ForAge(age).Length == 0)
            {
                return;
            }

            if (!arc.recordedGrowthAges.Contains(age))
            {
                arc.recordedGrowthAges.Add(age);
                arc.recordedGrowthAges.Sort();
            }

            if (arc.supporters != null)
            {
                for (int i = 0; i < arc.supporters.Count; i++)
                {
                    FamilySupportObservationState supporter = arc.supporters[i];
                    if (supporter == null) continue;
                    supporter.summarizedLessonCount = Math.Max(0, supporter.lessonCount);
                    supporter.summarizedBabyPlayCount = Math.Max(0, supporter.babyPlayCount);
                    supporter.summarizedCareCount = Math.Max(0, supporter.careCount);
                }
            }

            arc.lastSummarizedObservationTick = Math.Max(
                arc.lastSummarizedObservationTick,
                Math.Max(0, observedTick));
        }

        /// <summary>Returns the unsummarized exact observation count without overflow.</summary>
        public static int UnsummarizedEvidence(FamilySupportObservationState supporter)
        {
            if (supporter == null) return 0;
            return SafeSum(
                Math.Max(0, supporter.lessonCount) - Math.Max(0, supporter.summarizedLessonCount),
                Math.Max(0, supporter.babyPlayCount) - Math.Max(0, supporter.summarizedBabyPlayCount),
                Math.Max(0, supporter.careCount) - Math.Max(0, supporter.summarizedCareCount));
        }

        /// <summary>Chooses keep, compact, or remove from detached liveness/reference facts.</summary>
        public static FamilyArcRetentionAction DecideRetention(
            BiotechFamilyArcState arc,
            FamilyArcRetentionInput input,
            int currentTick,
            int retentionTicks)
        {
            if (arc == null)
            {
                return FamilyArcRetentionAction.Remove;
            }

            input = input ?? new FamilyArcRetentionInput();
            bool unsummarized = false;
            if (arc.supporters != null)
            {
                for (int i = 0; i < arc.supporters.Count && !unsummarized; i++)
                {
                    unsummarized = UnsummarizedEvidence(arc.supporters[i]) > 0;
                }
            }

            // A pregnancy/labor row has no child ID until a later birth owner links it. Keep the row
            // while its exact live Hediff still exists, then give it the same XML-owned retention grace
            // as every other detached arc. Treating "unresolved" as permanent made every completed or
            // interrupted pregnancy immortal when no birth linker was active.
            if (input.familyHediffStillPresent || input.childAliveAndDeveloping || input.hasPendingReference
                || input.hasSavedEventReference || unsummarized || retentionTicks <= 0)
            {
                return FamilyArcRetentionAction.Keep;
            }

            int lastTick = Math.Max(arc.openedTick,
                Math.Max(arc.birthTick, Math.Max(arc.lastObservedTick, arc.lastSummarizedObservationTick)));
            int now = Math.Max(0, currentTick);
            if (now < lastTick || now - lastTick < retentionTicks)
            {
                return FamilyArcRetentionAction.Keep;
            }

            return arc.detailsCompacted
                ? FamilyArcRetentionAction.Remove
                : FamilyArcRetentionAction.Compact;
        }

        /// <summary>Discards detailed observation history while preserving stable identities and milestones.</summary>
        public static void Compact(BiotechFamilyArcState arc)
        {
            if (arc == null) return;
            arc.supporters = NormalizeSupporters(arc.supporters, int.MaxValue);
            for (int i = 0; i < arc.supporters.Count; i++)
            {
                FamilySupportObservationState supporter = arc.supporters[i];
                supporter.lessonCount = 0;
                supporter.babyPlayCount = 0;
                supporter.careCount = 0;
                supporter.summarizedLessonCount = 0;
                supporter.summarizedBabyPlayCount = 0;
                supporter.summarizedCareCount = 0;
                supporter.firstObservedTick = 0;
                supporter.lastObservedTick = 0;
            }
            arc.detailsCompacted = true;
        }

        private static BiotechFamilyArcState NormalizeArc(
            BiotechFamilyArcState arc,
            int currentTick,
            int maximumSupporterRows)
        {
            if (arc == null) return null;
            arc.familyArcId = Clean(arc.familyArcId);
            arc.pregnancyHediffId = Clean(arc.pregnancyHediffId);
            arc.laborHediffId = Clean(arc.laborHediffId);
            arc.childId = Clean(arc.childId);
            arc.birtherId = Clean(arc.birtherId);
            arc.geneticMotherId = Clean(arc.geneticMotherId);
            arc.fatherId = Clean(arc.fatherId);
            if (!ValidFamilyArcId(arc.familyArcId))
            {
                arc.familyArcId = BiotechArcKeys.FamilyFromChild(arc.childId);
                if (arc.familyArcId.Length == 0)
                {
                    arc.familyArcId = BiotechArcKeys.FamilyFromPregnancy(arc.birtherId, arc.pregnancyHediffId);
                }
            }
            if (arc.familyArcId.Length == 0) return null;

            arc.birtherName = Clean(arc.birtherName);
            arc.geneticMotherName = Clean(arc.geneticMotherName);
            arc.fatherName = Clean(arc.fatherName);
            arc.childNameAtBirth = Clean(arc.childNameAtBirth);
            arc.currentChildName = Clean(arc.currentChildName);
            arc.openedTick = ClampTick(arc.openedTick, currentTick);
            arc.birthTick = ClampTick(arc.birthTick, currentTick);
            arc.lastObservedTick = ClampTick(arc.lastObservedTick, currentTick);
            arc.lastSummarizedObservationTick = ClampTick(arc.lastSummarizedObservationTick, currentTick);
            arc.recordedGrowthAges = NormalizeGrowthAges(arc.recordedGrowthAges);
            arc.supporters = NormalizeSupporters(arc.supporters, maximumSupporterRows);
            return arc;
        }

        private static void MergeArc(
            BiotechFamilyArcState target,
            BiotechFamilyArcState source,
            int currentTick,
            int maximumSupporterRows)
        {
            Prefer(ref target.pregnancyHediffId, source.pregnancyHediffId);
            Prefer(ref target.laborHediffId, source.laborHediffId);
            Prefer(ref target.childId, source.childId);
            Prefer(ref target.birtherId, source.birtherId);
            Prefer(ref target.geneticMotherId, source.geneticMotherId);
            Prefer(ref target.fatherId, source.fatherId);
            Prefer(ref target.birtherName, source.birtherName);
            Prefer(ref target.geneticMotherName, source.geneticMotherName);
            Prefer(ref target.fatherName, source.fatherName);
            Prefer(ref target.childNameAtBirth, source.childNameAtBirth);
            Prefer(ref target.currentChildName, source.currentChildName);
            Prefer(ref target.birthOutcomeToken, source.birthOutcomeToken);
            Prefer(ref target.birthMethodToken, source.birthMethodToken);
            target.openedTick = MinimumPositive(target.openedTick, source.openedTick);
            target.birthTick = Math.Max(target.birthTick, source.birthTick);
            target.lastObservedTick = Math.Max(target.lastObservedTick, source.lastObservedTick);
            target.lastSummarizedObservationTick = Math.Max(
                target.lastSummarizedObservationTick, source.lastSummarizedObservationTick);
            target.namingResolved |= source.namingResolved;
            target.closed |= source.closed;
            target.baselineOnly &= source.baselineOnly;
            target.detailsCompacted &= source.detailsCompacted;
            if (source.recordedGrowthAges != null) target.recordedGrowthAges.AddRange(source.recordedGrowthAges);
            if (source.supporters != null) target.supporters.AddRange(source.supporters);
            target.recordedGrowthAges = NormalizeGrowthAges(target.recordedGrowthAges);
            target.supporters = NormalizeSupporters(target.supporters, maximumSupporterRows);
            NormalizeArc(target, currentTick, maximumSupporterRows);
        }

        private static List<FamilySupportObservationState> NormalizeSupporters(
            IList<FamilySupportObservationState> source,
            int maximumRows)
        {
            Dictionary<string, FamilySupportObservationState> byId =
                new Dictionary<string, FamilySupportObservationState>(StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    FamilySupportObservationState row = source[i];
                    string id = Clean(row?.adultId);
                    if (!ValidId(id)) continue;
                    NormalizeSupporter(row, id);
                    FamilySupportObservationState existing;
                    if (byId.TryGetValue(id, out existing))
                    {
                        MergeSupporter(existing, row);
                    }
                    else
                    {
                        byId.Add(id, row);
                    }
                }
            }

            List<FamilySupportObservationState> result =
                new List<FamilySupportObservationState>(byId.Values);
            result.Sort(CompareSupportersForRetention);
            int cap = Math.Max(1, maximumRows);
            if (result.Count > cap) result.RemoveRange(cap, result.Count - cap);
            result.Sort((left, right) => string.CompareOrdinal(left.adultId, right.adultId));
            return result;
        }

        private static FamilySupportObservationState UpsertSupporter(
            BiotechFamilyArcState arc,
            string adultId,
            string displayName,
            string relationToken,
            int observed,
            int tick)
        {
            string id = Clean(adultId);
            if (arc == null || !ValidId(id)) return null;
            if (arc.supporters == null) arc.supporters = new List<FamilySupportObservationState>();
            FamilySupportObservationState row = null;
            for (int i = 0; i < arc.supporters.Count; i++)
            {
                if (arc.supporters[i] != null
                    && string.Equals(arc.supporters[i].adultId, id, StringComparison.Ordinal))
                {
                    row = arc.supporters[i];
                    break;
                }
            }
            if (row == null)
            {
                row = new FamilySupportObservationState { adultId = id };
                arc.supporters.Add(row);
            }

            if (!string.IsNullOrWhiteSpace(displayName)) row.lastDisplayName = Clean(displayName);
            if (relationToken == BiotechFamilyRoleTokens.Parent
                || relationToken == BiotechFamilyRoleTokens.BirthParent)
            {
                if (row.relationToken != BiotechFamilyRoleTokens.BirthParent
                    || relationToken == BiotechFamilyRoleTokens.BirthParent)
                {
                    row.relationToken = relationToken;
                }
            }
            if (observed > 0 && row.firstObservedTick <= 0) row.firstObservedTick = tick;
            if (observed > 0) row.lastObservedTick = Math.Max(row.lastObservedTick, tick);
            return row;
        }

        private static void NormalizeSupporter(FamilySupportObservationState row, string id)
        {
            row.adultId = id;
            row.lastDisplayName = Clean(row.lastDisplayName);
            if (row.relationToken != BiotechFamilyRoleTokens.Parent
                && row.relationToken != BiotechFamilyRoleTokens.BirthParent)
            {
                row.relationToken = string.Empty;
            }
            row.lessonCount = Math.Max(0, row.lessonCount);
            row.babyPlayCount = Math.Max(0, row.babyPlayCount);
            row.careCount = Math.Max(0, row.careCount);
            row.summarizedLessonCount = Math.Min(row.lessonCount, Math.Max(0, row.summarizedLessonCount));
            row.summarizedBabyPlayCount = Math.Min(row.babyPlayCount, Math.Max(0, row.summarizedBabyPlayCount));
            row.summarizedCareCount = Math.Min(row.careCount, Math.Max(0, row.summarizedCareCount));
            row.firstObservedTick = Math.Max(0, row.firstObservedTick);
            row.lastObservedTick = Math.Max(row.firstObservedTick, row.lastObservedTick);
        }

        private static void MergeSupporter(
            FamilySupportObservationState target,
            FamilySupportObservationState source)
        {
            if (!string.IsNullOrWhiteSpace(source.lastDisplayName)) target.lastDisplayName = source.lastDisplayName;
            if (source.relationToken == BiotechFamilyRoleTokens.BirthParent
                || (source.relationToken == BiotechFamilyRoleTokens.Parent
                    && target.relationToken != BiotechFamilyRoleTokens.BirthParent))
            {
                target.relationToken = source.relationToken;
            }
            target.lessonCount = SafeSum(target.lessonCount, source.lessonCount);
            target.babyPlayCount = SafeSum(target.babyPlayCount, source.babyPlayCount);
            target.careCount = SafeSum(target.careCount, source.careCount);
            target.summarizedLessonCount = SafeSum(
                target.summarizedLessonCount, source.summarizedLessonCount);
            target.summarizedBabyPlayCount = SafeSum(
                target.summarizedBabyPlayCount, source.summarizedBabyPlayCount);
            target.summarizedCareCount = SafeSum(target.summarizedCareCount, source.summarizedCareCount);
            target.firstObservedTick = MinimumPositive(target.firstObservedTick, source.firstObservedTick);
            target.lastObservedTick = Math.Max(target.lastObservedTick, source.lastObservedTick);
            NormalizeSupporter(target, target.adultId);
        }

        private static int CompareSupportersForRetention(
            FamilySupportObservationState left,
            FamilySupportObservationState right)
        {
            int leftParent = left.relationToken == BiotechFamilyRoleTokens.Parent
                || left.relationToken == BiotechFamilyRoleTokens.BirthParent ? 1 : 0;
            int rightParent = right.relationToken == BiotechFamilyRoleTokens.Parent
                || right.relationToken == BiotechFamilyRoleTokens.BirthParent ? 1 : 0;
            int value = rightParent.CompareTo(leftParent);
            if (value != 0) return value;
            value = TotalEvidence(right).CompareTo(TotalEvidence(left));
            if (value != 0) return value;
            value = right.lastObservedTick.CompareTo(left.lastObservedTick);
            return value != 0 ? value : string.CompareOrdinal(left.adultId, right.adultId);
        }

        private static BiotechFamilyArcState FindByChild(List<BiotechFamilyArcState> arcs, string childId)
        {
            string id = Clean(childId);
            for (int i = 0; i < arcs.Count; i++)
            {
                if (arcs[i] != null && string.Equals(arcs[i].childId, id, StringComparison.Ordinal)) return arcs[i];
            }
            return null;
        }

        private static BiotechFamilyArcState FindByHediff(List<BiotechFamilyArcState> arcs, string hediffId)
        {
            string id = Clean(hediffId);
            for (int i = 0; i < arcs.Count; i++)
            {
                BiotechFamilyArcState arc = arcs[i];
                if (arc != null && (string.Equals(arc.pregnancyHediffId, id, StringComparison.Ordinal)
                    || string.Equals(arc.laborHediffId, id, StringComparison.Ordinal))) return arc;
            }
            return null;
        }

        private static BiotechFamilyArcState FindCompatiblePregnancy(
            List<BiotechFamilyArcState> arcs,
            FamilyHediffSnapshot snapshot)
        {
            BiotechFamilyArcState best = FindByHediff(arcs, snapshot.hediffId);
            for (int i = 0; i < arcs.Count; i++)
            {
                BiotechFamilyArcState arc = arcs[i];
                if (arc == null || string.IsNullOrWhiteSpace(arc.pregnancyHediffId)
                    || !string.IsNullOrWhiteSpace(arc.laborHediffId)
                    || arc.closed
                    || !string.Equals(arc.birtherId, snapshot.birtherId.Trim(), StringComparison.Ordinal)
                    || !SameWhenKnown(arc.geneticMotherId, snapshot.geneticMotherId)
                    || !SameWhenKnown(arc.fatherId, snapshot.fatherId))
                {
                    continue;
                }
                if (best == null || arc.lastObservedTick > best.lastObservedTick) best = arc;
            }
            return best;
        }

        private static void CopyHediffParticipants(BiotechFamilyArcState arc, FamilyHediffSnapshot snapshot)
        {
            Prefer(ref arc.birtherId, snapshot.birtherId);
            Prefer(ref arc.birtherName, snapshot.birtherName);
            Prefer(ref arc.geneticMotherId, snapshot.geneticMotherId);
            Prefer(ref arc.geneticMotherName, snapshot.geneticMotherName);
            Prefer(ref arc.fatherId, snapshot.fatherId);
            Prefer(ref arc.fatherName, snapshot.fatherName);
        }

        private static List<int> NormalizeGrowthAges(IList<int> ages)
        {
            List<int> result = new List<int>();
            if (ages != null)
            {
                for (int i = 0; i < ages.Count; i++)
                {
                    if (BiotechGrowthStageTokens.ForAge(ages[i]).Length > 0 && !result.Contains(ages[i]))
                    {
                        result.Add(ages[i]);
                    }
                }
            }
            result.Sort();
            return result;
        }

        private static bool Contains(List<string> values, string target)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals((values[i] ?? string.Empty).Trim(), target,
                    StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool SameWhenKnown(string first, string second)
        {
            string left = Clean(first);
            string right = Clean(second);
            return left.Length == 0 || right.Length == 0 || string.Equals(left, right, StringComparison.Ordinal);
        }

        private static int TotalEvidence(FamilySupportObservationState row)
        {
            return row == null ? 0 : SafeSum(row.lessonCount, row.babyPlayCount, row.careCount);
        }

        private static int SafeIncrement(int value)
        {
            return value >= int.MaxValue ? int.MaxValue : Math.Max(0, value) + 1;
        }

        private static int SafeSum(int first, int second)
        {
            long sum = Math.Max(0, first) + (long)Math.Max(0, second);
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        private static int SafeSum(int first, int second, int third)
        {
            return SafeSum(SafeSum(first, second), third);
        }

        private static int ClampTick(int value, int maximum)
        {
            return Math.Max(0, Math.Min(Math.Max(0, maximum), value));
        }

        private static int MinimumPositive(int first, int second)
        {
            if (first <= 0) return Math.Max(0, second);
            if (second <= 0) return first;
            return Math.Min(first, second);
        }

        private static void Prefer(ref string target, string candidate)
        {
            string value = Clean(candidate);
            if (value.Length > 0) target = value;
        }

        private static bool ValidId(string value)
        {
            string cleaned = Clean(value);
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0;
        }

        private static bool ValidFamilyArcId(string value)
        {
            string cleaned = Clean(value);
            if (!cleaned.StartsWith(FamilyPrefix, StringComparison.Ordinal)) return false;
            string[] parts = cleaned.Split('|');
            if (parts.Length != 2 && parts.Length != 3) return false;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!ValidId(parts[i])) return false;
            }
            return true;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
