// Pure Brainwipe projection for shared Biotech family arcs. Family identities, birth outcomes, raw
// supporter observations, and growth milestones are shared truth; detached per-POV baselines prevent
// those same facts from crossing only the wiped pawn's new autobiographical boundary.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Projects exact pawn memory boundaries while preserving shared family truth.</summary>
    internal static class BiotechFamilyMemoryResetPolicy
    {
        /// <summary>
        /// Rebaselines the exact pawn's adult-supporter POV in every arc without deleting the shared row
        /// that the child independently remembers. When that pawn is an arc's child, also captures the
        /// lifetime counters as a child-only baseline. Parent IDs, child identity, birth facts, raw family
        /// observations, and recorded growth ages remain unchanged.
        /// </summary>
        public static void ResetForPawn(
            IList<BiotechFamilyArcState> arcs,
            string pawnId,
            int memoryBoundaryTick)
        {
            string id = CleanPawnId(pawnId);
            if (arcs == null || id.Length == 0) return;

            int boundary = Math.Max(0, memoryBoundaryTick);
            for (int i = 0; i < arcs.Count; i++)
            {
                BiotechFamilyArcState arc = arcs[i];
                if (arc == null) continue;

                if (arc.supporters == null)
                {
                    arc.supporters = new List<FamilySupportObservationState>();
                }
                bool capturedAdultBoundary = false;
                for (int supporterIndex = 0; supporterIndex < arc.supporters.Count; supporterIndex++)
                {
                    FamilySupportObservationState supporter = arc.supporters[supporterIndex];
                    if (supporter != null && string.Equals(
                        CleanPawnId(supporter.adultId),
                        id,
                        StringComparison.Ordinal))
                    {
                        CaptureAdultBoundary(supporter, boundary);
                        capturedAdultBoundary = true;
                    }
                }
                if (!capturedAdultBoundary)
                {
                    FamilySupportObservationState parentBoundary = ParentBoundaryRow(arc, id);
                    if (parentBoundary != null)
                    {
                        CaptureAdultBoundary(parentBoundary, boundary);
                        arc.supporters.Add(parentBoundary);
                    }
                }

                if (arc.childMemorySupportBaselines == null)
                {
                    arc.childMemorySupportBaselines = new List<FamilySupportMemoryBaselineState>();
                }

                if (string.Equals(CleanPawnId(arc.childId), id, StringComparison.Ordinal))
                {
                    CaptureChildBoundary(arc, id, boundary);
                }
            }
        }

        /// <summary>
        /// Returns one detached supporter row as this POV may remember it. Exact saved child and adult
        /// boundaries receive their own lifetime-count deltas; unrelated POVs receive shared raw truth.
        /// </summary>
        public static FamilySupportObservationState ProjectSupporterForPov(
            BiotechFamilyArcState arc,
            FamilySupportObservationState supporter,
            string povPawnId)
        {
            if (supporter == null) return null;
            FamilySupportObservationState projected = CloneSupporter(supporter);
            bool childBoundary = BoundaryAppliesToPov(arc, povPawnId);
            bool adultBoundary = AdultBoundaryAppliesToPov(supporter, povPawnId);
            if (!childBoundary && !adultBoundary) return projected;

            FamilySupportMemoryBaselineState baseline = childBoundary
                ? FindBaseline(arc.childMemorySupportBaselines, supporter.adultId)
                : null;
            // A supporter first observed after Brainwipe legitimately has no saved boundary row. Its
            // implicit baseline is zero; returning the raw row here would accidentally inherit the
            // adult-facing summarized cursor and let an adult-only page consume the child's evidence.
            int lessonBaseline = baseline?.lessonCount ?? 0;
            int babyPlayBaseline = baseline?.babyPlayCount ?? 0;
            int careBaseline = baseline?.careCount ?? 0;
            if (adultBoundary)
            {
                lessonBaseline = Math.Max(lessonBaseline, supporter.adultMemoryLessonBaseline);
                babyPlayBaseline = Math.Max(
                    babyPlayBaseline,
                    supporter.adultMemoryBabyPlayBaseline);
                careBaseline = Math.Max(careBaseline, supporter.adultMemoryCareBaseline);
            }
            projected.lessonCount = Delta(supporter.lessonCount, lessonBaseline);
            projected.babyPlayCount = Delta(supporter.babyPlayCount, babyPlayBaseline);
            projected.careCount = Delta(supporter.careCount, careBaseline);
            // A boundary-specific projection is already measured from its own memory edge. Treat every
            // visible delta as unsummarized regardless of the shared lifetime consumption cursor.
            projected.summarizedLessonCount = 0;
            projected.summarizedBabyPlayCount = 0;
            projected.summarizedCareCount = 0;
            if (projected.lessonCount == 0 && projected.babyPlayCount == 0
                && projected.careCount == 0)
            {
                projected.firstObservedTick = 0;
                projected.lastObservedTick = 0;
            }
            else
            {
                int applicableBoundaryTick = childBoundary
                    ? Math.Max(0, arc.childMemoryBoundaryTick)
                    : 0;
                if (adultBoundary)
                {
                    applicableBoundaryTick = Math.Max(
                        applicableBoundaryTick,
                        Math.Max(0, supporter.adultMemoryBoundaryTick));
                }
                projected.firstObservedTick = applicableBoundaryTick;
            }
            return projected;
        }

        /// <summary>True when this exact POV has any upbringing evidence after its memory boundary.</summary>
        public static bool HasObservedUpbringingForPov(
            BiotechFamilyArcState arc,
            string povPawnId)
        {
            if (arc?.supporters == null) return false;
            if (!BoundaryAppliesToPov(arc, povPawnId))
            {
                FamilySupportObservationState adultRow = FindSupporter(arc.supporters, povPawnId);
                if (AdultBoundaryAppliesToPov(adultRow, povPawnId))
                {
                    FamilySupportObservationState adultProjection = ProjectSupporterForPov(
                        arc,
                        adultRow,
                        povPawnId);
                    return TotalEvidence(adultProjection) > 0;
                }
            }
            for (int i = 0; i < arc.supporters.Count; i++)
            {
                FamilySupportObservationState projected = ProjectSupporterForPov(
                    arc,
                    arc.supporters[i],
                    povPawnId);
                if (projected != null && (projected.lessonCount > 0
                    || projected.babyPlayCount > 0 || projected.careCount > 0)) return true;
            }
            return false;
        }

        /// <summary>True only for the exact child POV whose additive memory boundary is active.</summary>
        public static bool HasChildMemoryBoundaryForPov(
            BiotechFamilyArcState arc,
            string povPawnId)
        {
            return BoundaryAppliesToPov(arc, povPawnId);
        }

        /// <summary>True when either the exact child or exact adult supporter POV has a memory edge.</summary>
        public static bool HasMemoryBoundaryForPov(BiotechFamilyArcState arc, string povPawnId)
        {
            return BoundaryAppliesToPov(arc, povPawnId)
                || AdultBoundaryAppliesToPov(FindSupporter(arc?.supporters, povPawnId), povPawnId);
        }

        /// <summary>True only for an exact saved supporter row rebased for this adult POV.</summary>
        public static bool HasAdultMemoryBoundaryForPov(
            BiotechFamilyArcState arc,
            string povPawnId)
        {
            return AdultBoundaryAppliesToPov(
                FindSupporter(arc?.supporters, povPawnId),
                povPawnId);
        }

        /// <summary>Advances the child-only baseline after a canonical growth owner consumes evidence.</summary>
        public static void MarkChildPovSummarized(BiotechFamilyArcState arc)
        {
            if (arc == null || !BoundaryAppliesToPov(
                arc,
                arc.childMemoryBoundaryPawnId)) return;
            CaptureChildBoundary(
                arc,
                arc.childMemoryBoundaryPawnId,
                arc.childMemoryBoundaryTick);
        }

        /// <summary>Advances only the exact wiped adult's detached supporter cursor.</summary>
        public static void MarkAdultPovSummarized(BiotechFamilyArcState arc, string adultPawnId)
        {
            FamilySupportObservationState supporter = FindSupporter(
                arc?.supporters,
                adultPawnId);
            if (!AdultBoundaryAppliesToPov(supporter, adultPawnId)) return;
            CaptureAdultBoundary(supporter, supporter.adultMemoryBoundaryTick);
        }

        /// <summary>
        /// Returns the evidence both prospective pair POVs can truthfully share. Each category keeps the
        /// smaller visible and unsummarized delta, so one writer's older epoch cannot enter shared context.
        /// </summary>
        public static FamilySupportObservationState ProjectSupporterForPair(
            BiotechFamilyArcState arc,
            FamilySupportObservationState supporter,
            string childPovPawnId)
        {
            if (supporter == null) return null;
            FamilySupportObservationState child = ProjectSupporterForPov(
                arc,
                supporter,
                childPovPawnId);
            FamilySupportObservationState adult = ProjectSupporterForPov(
                arc,
                supporter,
                supporter.adultId);
            FamilySupportObservationState result = CloneSupporter(supporter);
            IntersectCounter(
                child.lessonCount,
                child.summarizedLessonCount,
                adult.lessonCount,
                adult.summarizedLessonCount,
                out result.lessonCount,
                out result.summarizedLessonCount);
            IntersectCounter(
                child.babyPlayCount,
                child.summarizedBabyPlayCount,
                adult.babyPlayCount,
                adult.summarizedBabyPlayCount,
                out result.babyPlayCount,
                out result.summarizedBabyPlayCount);
            IntersectCounter(
                child.careCount,
                child.summarizedCareCount,
                adult.careCount,
                adult.summarizedCareCount,
                out result.careCount,
                out result.summarizedCareCount);
            if (TotalEvidence(result) == 0)
            {
                result.firstObservedTick = 0;
                result.lastObservedTick = 0;
            }
            else
            {
                result.firstObservedTick = Math.Max(
                    Math.Max(0, child.firstObservedTick),
                    Math.Max(0, adult.firstObservedTick));
                int childLast = Math.Max(0, child.lastObservedTick);
                int adultLast = Math.Max(0, adult.lastObservedTick);
                int sharedLast = childLast > 0 && adultLast > 0
                    ? Math.Min(childLast, adultLast)
                    : Math.Max(childLast, adultLast);
                result.lastObservedTick = Math.Max(result.firstObservedTick, sharedLast);
            }
            return result;
        }

        /// <summary>Repairs one additive adult cursor after ordinary supporter counts normalize.</summary>
        public static void NormalizeAdultBoundary(FamilySupportObservationState supporter)
        {
            if (supporter == null) return;
            if (!supporter.adultMemoryBoundaryActive)
            {
                supporter.adultMemoryBoundaryTick = 0;
                supporter.adultMemoryLessonBaseline = 0;
                supporter.adultMemoryBabyPlayBaseline = 0;
                supporter.adultMemoryCareBaseline = 0;
                return;
            }

            supporter.adultMemoryBoundaryTick = Math.Max(0, supporter.adultMemoryBoundaryTick);
            supporter.adultMemoryLessonBaseline = Math.Min(
                Math.Max(0, supporter.lessonCount),
                Math.Max(0, supporter.adultMemoryLessonBaseline));
            supporter.adultMemoryBabyPlayBaseline = Math.Min(
                Math.Max(0, supporter.babyPlayCount),
                Math.Max(0, supporter.adultMemoryBabyPlayBaseline));
            supporter.adultMemoryCareBaseline = Math.Min(
                Math.Max(0, supporter.careCount),
                Math.Max(0, supporter.adultMemoryCareBaseline));
        }

        /// <summary>Merges duplicate exact-adult rows without exposing either row's pre-boundary counts.</summary>
        public static void MergeAdultBoundary(
            FamilySupportObservationState target,
            FamilySupportObservationState source)
        {
            if (target == null || source == null) return;
            bool targetActive = target.adultMemoryBoundaryActive;
            bool sourceActive = source.adultMemoryBoundaryActive;
            if (!targetActive && !sourceActive) return;

            bool sourceIsNewer = sourceActive && (!targetActive
                || source.adultMemoryBoundaryTick > target.adultMemoryBoundaryTick);
            int tick = Math.Max(
                targetActive ? target.adultMemoryBoundaryTick : 0,
                sourceActive ? source.adultMemoryBoundaryTick : 0);
            if (sourceIsNewer)
            {
                target.adultMemoryLessonBaseline = SafeSum(
                    target.lessonCount,
                    source.adultMemoryLessonBaseline);
                target.adultMemoryBabyPlayBaseline = SafeSum(
                    target.babyPlayCount,
                    source.adultMemoryBabyPlayBaseline);
                target.adultMemoryCareBaseline = SafeSum(
                    target.careCount,
                    source.adultMemoryCareBaseline);
            }
            else if (targetActive && sourceActive
                && target.adultMemoryBoundaryTick == source.adultMemoryBoundaryTick)
            {
                target.adultMemoryLessonBaseline = SafeSum(
                    target.adultMemoryLessonBaseline,
                    source.adultMemoryLessonBaseline);
                target.adultMemoryBabyPlayBaseline = SafeSum(
                    target.adultMemoryBabyPlayBaseline,
                    source.adultMemoryBabyPlayBaseline);
                target.adultMemoryCareBaseline = SafeSum(
                    target.adultMemoryCareBaseline,
                    source.adultMemoryCareBaseline);
            }
            else
            {
                // The target has the newer (or only) boundary. Treat the unbounded/older duplicate's
                // entire raw partition as preceding that boundary; this fails closed on corrupt saves.
                target.adultMemoryLessonBaseline = SafeSum(
                    target.adultMemoryLessonBaseline,
                    source.lessonCount);
                target.adultMemoryBabyPlayBaseline = SafeSum(
                    target.adultMemoryBabyPlayBaseline,
                    source.babyPlayCount);
                target.adultMemoryCareBaseline = SafeSum(
                    target.adultMemoryCareBaseline,
                    source.careCount);
            }
            target.adultMemoryBoundaryActive = true;
            target.adultMemoryBoundaryTick = Math.Max(0, tick);
        }

        /// <summary>Repairs an additive loaded boundary after ordinary supporter normalization.</summary>
        public static void NormalizeBoundary(
            BiotechFamilyArcState arc,
            int currentTick,
            int maximumRows)
        {
            if (arc == null) return;
            string childId = CleanPawnId(arc.childId);
            string boundaryChildId = CleanPawnId(arc.childMemoryBoundaryPawnId);
            if (childId.Length == 0 || !string.Equals(
                childId, boundaryChildId, StringComparison.Ordinal))
            {
                ClearBoundary(arc);
                return;
            }

            Dictionary<string, FamilySupportObservationState> supporters =
                new Dictionary<string, FamilySupportObservationState>(StringComparer.Ordinal);
            if (arc.supporters != null)
            {
                for (int i = 0; i < arc.supporters.Count; i++)
                {
                    FamilySupportObservationState supporter = arc.supporters[i];
                    string adultId = CleanPawnId(supporter?.adultId);
                    if (adultId.Length > 0) supporters[adultId] = supporter;
                }
            }

            Dictionary<string, FamilySupportMemoryBaselineState> byId =
                new Dictionary<string, FamilySupportMemoryBaselineState>(StringComparer.Ordinal);
            if (arc.childMemorySupportBaselines != null)
            {
                for (int i = 0; i < arc.childMemorySupportBaselines.Count; i++)
                {
                    FamilySupportMemoryBaselineState row = arc.childMemorySupportBaselines[i];
                    string adultId = CleanPawnId(row?.adultId);
                    FamilySupportObservationState supporter;
                    if (adultId.Length == 0 || !supporters.TryGetValue(adultId, out supporter)) continue;
                    FamilySupportMemoryBaselineState existing;
                    if (!byId.TryGetValue(adultId, out existing))
                    {
                        existing = new FamilySupportMemoryBaselineState { adultId = adultId };
                        byId.Add(adultId, existing);
                    }
                    existing.lessonCount = Math.Max(existing.lessonCount,
                        Math.Min(Math.Max(0, row.lessonCount), Math.Max(0, supporter.lessonCount)));
                    existing.babyPlayCount = Math.Max(existing.babyPlayCount,
                        Math.Min(Math.Max(0, row.babyPlayCount), Math.Max(0, supporter.babyPlayCount)));
                    existing.careCount = Math.Max(existing.careCount,
                        Math.Min(Math.Max(0, row.careCount), Math.Max(0, supporter.careCount)));
                }
            }

            arc.childMemoryBoundaryPawnId = childId;
            arc.childMemoryBoundaryTick = Math.Max(0,
                Math.Min(Math.Max(0, currentTick), arc.childMemoryBoundaryTick));
            arc.childMemorySupportBaselines = new List<FamilySupportMemoryBaselineState>(byId.Values);
            arc.childMemorySupportBaselines.Sort(
                (left, right) => string.CompareOrdinal(left.adultId, right.adultId));
            int cap = Math.Max(1, maximumRows);
            if (arc.childMemorySupportBaselines.Count > cap)
            {
                arc.childMemorySupportBaselines.RemoveRange(
                    cap,
                    arc.childMemorySupportBaselines.Count - cap);
            }
        }

        /// <summary>Merges a duplicate arc's child boundary without moving an established epoch backward.</summary>
        public static void MergeBoundary(
            BiotechFamilyArcState target,
            BiotechFamilyArcState source)
        {
            if (target == null || source == null) return;
            string sourceChild = CleanPawnId(source.childMemoryBoundaryPawnId);
            if (sourceChild.Length == 0) return;
            string targetChild = CleanPawnId(target.childMemoryBoundaryPawnId);
            if (!string.Equals(targetChild, sourceChild, StringComparison.Ordinal)
                || source.childMemoryBoundaryTick > target.childMemoryBoundaryTick)
            {
                target.childMemoryBoundaryPawnId = sourceChild;
                target.childMemoryBoundaryTick = Math.Max(0, source.childMemoryBoundaryTick);
                target.childMemorySupportBaselines = CloneBaselines(
                    source.childMemorySupportBaselines);
                return;
            }
            if (source.childMemoryBoundaryTick < target.childMemoryBoundaryTick) return;

            if (target.childMemorySupportBaselines == null)
                target.childMemorySupportBaselines = new List<FamilySupportMemoryBaselineState>();
            IList<FamilySupportMemoryBaselineState> incoming = source.childMemorySupportBaselines;
            if (incoming == null) return;
            for (int i = 0; i < incoming.Count; i++)
            {
                FamilySupportMemoryBaselineState row = incoming[i];
                string adultId = CleanPawnId(row?.adultId);
                if (adultId.Length == 0) continue;
                FamilySupportMemoryBaselineState existing = FindBaseline(
                    target.childMemorySupportBaselines,
                    adultId);
                if (existing == null)
                {
                    target.childMemorySupportBaselines.Add(CloneBaseline(row));
                }
                else
                {
                    existing.lessonCount = SafeSum(existing.lessonCount, row.lessonCount);
                    existing.babyPlayCount = SafeSum(existing.babyPlayCount, row.babyPlayCount);
                    existing.careCount = SafeSum(existing.careCount, row.careCount);
                }
            }
        }

        private static void CaptureChildBoundary(
            BiotechFamilyArcState arc,
            string childId,
            int boundaryTick)
        {
            arc.childMemoryBoundaryPawnId = childId;
            arc.childMemoryBoundaryTick = boundaryTick;
            arc.childMemorySupportBaselines = new List<FamilySupportMemoryBaselineState>();
            if (arc.supporters == null) return;
            for (int i = 0; i < arc.supporters.Count; i++)
            {
                FamilySupportObservationState supporter = arc.supporters[i];
                string adultId = CleanPawnId(supporter?.adultId);
                if (adultId.Length == 0) continue;
                arc.childMemorySupportBaselines.Add(new FamilySupportMemoryBaselineState
                {
                    adultId = adultId,
                    lessonCount = Math.Max(0, supporter.lessonCount),
                    babyPlayCount = Math.Max(0, supporter.babyPlayCount),
                    careCount = Math.Max(0, supporter.careCount)
                });
            }
            arc.childMemorySupportBaselines.Sort(
                (left, right) => string.CompareOrdinal(left.adultId, right.adultId));
        }

        private static void CaptureAdultBoundary(
            FamilySupportObservationState supporter,
            int boundaryTick)
        {
            if (supporter == null) return;
            supporter.adultMemoryBoundaryActive = true;
            supporter.adultMemoryBoundaryTick = Math.Max(0, boundaryTick);
            supporter.adultMemoryLessonBaseline = Math.Max(0, supporter.lessonCount);
            supporter.adultMemoryBabyPlayBaseline = Math.Max(0, supporter.babyPlayCount);
            supporter.adultMemoryCareBaseline = Math.Max(0, supporter.careCount);
        }

        private static FamilySupportObservationState ParentBoundaryRow(
            BiotechFamilyArcState arc,
            string adultId)
        {
            if (arc == null || adultId.Length == 0) return null;
            string role = string.Empty;
            string displayName = string.Empty;
            if (string.Equals(CleanPawnId(arc.birtherId), adultId, StringComparison.Ordinal))
            {
                role = BiotechFamilyRoleTokens.BirthParent;
                displayName = arc.birtherName;
            }
            else if (string.Equals(
                CleanPawnId(arc.geneticMotherId), adultId, StringComparison.Ordinal))
            {
                role = BiotechFamilyRoleTokens.Parent;
                displayName = arc.geneticMotherName;
            }
            else if (string.Equals(CleanPawnId(arc.fatherId), adultId, StringComparison.Ordinal))
            {
                role = BiotechFamilyRoleTokens.Parent;
                displayName = arc.fatherName;
            }
            if (role.Length == 0) return null;
            return new FamilySupportObservationState
            {
                adultId = adultId,
                lastDisplayName = displayName ?? string.Empty,
                relationToken = role
            };
        }

        private static bool BoundaryAppliesToPov(BiotechFamilyArcState arc, string povPawnId)
        {
            string povId = CleanPawnId(povPawnId);
            return arc != null && povId.Length > 0
                && string.Equals(CleanPawnId(arc.childId), povId, StringComparison.Ordinal)
                && string.Equals(
                    CleanPawnId(arc.childMemoryBoundaryPawnId),
                    povId,
                    StringComparison.Ordinal);
        }

        private static bool AdultBoundaryAppliesToPov(
            FamilySupportObservationState supporter,
            string povPawnId)
        {
            string povId = CleanPawnId(povPawnId);
            return supporter != null && supporter.adultMemoryBoundaryActive && povId.Length > 0
                && string.Equals(CleanPawnId(supporter.adultId), povId, StringComparison.Ordinal);
        }

        private static FamilySupportObservationState FindSupporter(
            IList<FamilySupportObservationState> supporters,
            string adultId)
        {
            string id = CleanPawnId(adultId);
            if (supporters == null || id.Length == 0) return null;
            for (int i = 0; i < supporters.Count; i++)
            {
                FamilySupportObservationState row = supporters[i];
                if (row != null && string.Equals(
                    CleanPawnId(row.adultId), id, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        private static FamilySupportMemoryBaselineState FindBaseline(
            IList<FamilySupportMemoryBaselineState> baselines,
            string adultId)
        {
            string id = CleanPawnId(adultId);
            if (baselines == null || id.Length == 0) return null;
            for (int i = 0; i < baselines.Count; i++)
            {
                if (baselines[i] != null && string.Equals(
                    CleanPawnId(baselines[i].adultId), id, StringComparison.Ordinal)) return baselines[i];
            }
            return null;
        }

        private static List<FamilySupportMemoryBaselineState> CloneBaselines(
            IList<FamilySupportMemoryBaselineState> source)
        {
            List<FamilySupportMemoryBaselineState> result =
                new List<FamilySupportMemoryBaselineState>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                FamilySupportMemoryBaselineState clone = CloneBaseline(source[i]);
                if (clone != null) result.Add(clone);
            }
            return result;
        }

        private static FamilySupportMemoryBaselineState CloneBaseline(
            FamilySupportMemoryBaselineState source)
        {
            string adultId = CleanPawnId(source?.adultId);
            return adultId.Length == 0 ? null : new FamilySupportMemoryBaselineState
            {
                adultId = adultId,
                lessonCount = Math.Max(0, source.lessonCount),
                babyPlayCount = Math.Max(0, source.babyPlayCount),
                careCount = Math.Max(0, source.careCount)
            };
        }

        private static FamilySupportObservationState CloneSupporter(
            FamilySupportObservationState source)
        {
            return new FamilySupportObservationState
            {
                adultId = source.adultId ?? string.Empty,
                lastDisplayName = source.lastDisplayName ?? string.Empty,
                relationToken = source.relationToken ?? string.Empty,
                lessonCount = Math.Max(0, source.lessonCount),
                babyPlayCount = Math.Max(0, source.babyPlayCount),
                careCount = Math.Max(0, source.careCount),
                summarizedLessonCount = Math.Max(0, source.summarizedLessonCount),
                summarizedBabyPlayCount = Math.Max(0, source.summarizedBabyPlayCount),
                summarizedCareCount = Math.Max(0, source.summarizedCareCount),
                firstObservedTick = Math.Max(0, source.firstObservedTick),
                lastObservedTick = Math.Max(0, source.lastObservedTick),
                adultMemoryBoundaryActive = source.adultMemoryBoundaryActive,
                adultMemoryBoundaryTick = Math.Max(0, source.adultMemoryBoundaryTick),
                adultMemoryLessonBaseline = Math.Max(0, source.adultMemoryLessonBaseline),
                adultMemoryBabyPlayBaseline = Math.Max(0, source.adultMemoryBabyPlayBaseline),
                adultMemoryCareBaseline = Math.Max(0, source.adultMemoryCareBaseline)
            };
        }

        private static void ClearBoundary(BiotechFamilyArcState arc)
        {
            arc.childMemoryBoundaryPawnId = string.Empty;
            arc.childMemoryBoundaryTick = 0;
            arc.childMemorySupportBaselines = new List<FamilySupportMemoryBaselineState>();
        }

        private static int Delta(int current, int baseline)
        {
            return Math.Max(0, Math.Max(0, current) - Math.Max(0, baseline));
        }

        private static int TotalEvidence(FamilySupportObservationState supporter)
        {
            return supporter == null ? 0 : SafeSum(
                SafeSum(supporter.lessonCount, supporter.babyPlayCount),
                supporter.careCount);
        }

        private static void IntersectCounter(
            int firstCount,
            int firstSummarized,
            int secondCount,
            int secondSummarized,
            out int visibleCount,
            out int visibleSummarized)
        {
            int first = Math.Max(0, firstCount);
            int second = Math.Max(0, secondCount);
            visibleCount = Math.Min(first, second);
            int firstNew = Math.Max(0, first - Math.Max(0, firstSummarized));
            int secondNew = Math.Max(0, second - Math.Max(0, secondSummarized));
            int visibleNew = Math.Min(visibleCount, Math.Min(firstNew, secondNew));
            visibleSummarized = visibleCount - visibleNew;
        }

        private static int SafeSum(int first, int second)
        {
            long sum = Math.Max(0, first) + (long)Math.Max(0, second);
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        private static string CleanPawnId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.Length <= 200
                && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0
                && cleaned.IndexOf('=') < 0 && cleaned.IndexOf('\r') < 0
                && cleaned.IndexOf('\n') < 0
                ? cleaned
                : string.Empty;
        }
    }
}
