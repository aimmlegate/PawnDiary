// MemoryThreadLookupPolicy.cs — exact root/standalone lookup and placement planning for M4.
//
// Frozen labels never participate. A reliable exact subject routes to one canonical root tuple;
// unreliable subject evidence remains standalone and cannot accidentally create a label-keyed root.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Detached exact placement request.</summary>
    internal sealed class MemoryPlacementRequest
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public bool routeReliable;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
    }

    /// <summary>Pure placement decision: exactly one of threaded/standalone is true.</summary>
    internal sealed class MemoryPlacementPlan
    {
        public bool valid;
        public bool threaded;
        public bool standalone;
        public string rootId = string.Empty;
    }

    /// <summary>Plans exact placement and provides label-independent lookup helpers.</summary>
    internal static class MemoryThreadLookupPolicy
    {
        /// <summary>Plans a canonical root only when exact routing evidence is reliable.</summary>
        public static MemoryPlacementPlan PlanPlacement(MemoryPlacementRequest request)
        {
            MemoryPlacementPlan plan = new MemoryPlacementPlan();
            bool ignoredFallback;
            if (request == null || string.IsNullOrEmpty(request.ownerPawnId)
                || !MemoryIdentityCodec.TryValidateEpochToken(
                    request.ownerEpochToken, out ignoredFallback)) return plan;
            if (!request.routeReliable)
            {
                plan.valid = true;
                plan.standalone = true;
                return plan;
            }
            MemoryRootIdentity identity = new MemoryRootIdentity
            {
                ownerPawnId = request.ownerPawnId,
                ownerEpochToken = request.ownerEpochToken,
                primarySubjectKind = request.subjectKind,
                primarySubjectId = request.subjectId
            };
            string rootId;
            if (!MemoryIdentityCodec.TryCreateRootId(identity, out rootId)) return plan;
            plan.valid = true;
            plan.threaded = true;
            plan.rootId = rootId;
            return plan;
        }

        /// <summary>Returns the first exact canonical root tuple match, never a label match.</summary>
        public static int FindExactRoot(
            IReadOnlyList<MemoryReducerRoot> roots,
            string ownerPawnId,
            string ownerEpochToken,
            string subjectKind,
            string subjectId)
        {
            if (roots == null) return -1;
            for (int i = 0; i < roots.Count; i++)
            {
                MemoryReducerRoot root = roots[i];
                if (root != null && string.Equals(root.ownerPawnId, ownerPawnId, StringComparison.Ordinal)
                    && string.Equals(root.ownerEpochToken, ownerEpochToken, StringComparison.Ordinal)
                    && string.Equals(root.subjectKind, subjectKind, StringComparison.Ordinal)
                    && string.Equals(root.subjectId, subjectId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// <summary>Returns the first exact owner/epoch/record standalone match.</summary>
        public static int FindExactStandalone(
            IReadOnlyList<MemoryReducerBlock> blocks,
            string ownerPawnId,
            string ownerEpochToken,
            string recordId)
        {
            if (blocks == null || string.IsNullOrEmpty(recordId)) return -1;
            for (int i = 0; i < blocks.Count; i++)
            {
                MemoryReducerBlock block = blocks[i];
                if (block != null && string.IsNullOrEmpty(block.rootId)
                    && string.IsNullOrEmpty(block.chapterId)
                    && string.Equals(block.ownerPawnId, ownerPawnId, StringComparison.Ordinal)
                    && string.Equals(block.ownerEpochToken, ownerEpochToken, StringComparison.Ordinal)
                    && string.Equals(block.recordId, recordId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}
