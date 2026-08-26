// Pure deterministic chance and cadence policy for M10 quiet-memory work.
//
// The runtime adapter supplies detached owner/epoch/day fields and commits the returned cadence only
// on the main thread. This file has no Verse/Unity/settings/save/transport dependency, so it cannot
// consume RimWorld's global gameplay RNG or mutate a saved opportunity while merely evaluating it.
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PawnDiary
{
    /// <summary>Detached current state and effective gates for one quiet-memory evaluation.</summary>
    internal sealed class MemoryQuietCadenceRequest
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public int absoluteDay = -1;
        public int absoluteQuadrum = -1;
        public int lastEvaluatedAbsoluteDay = -1;
        public int lastActivatedAbsoluteQuadrum = -1;
        public string lastDecisionKey = string.Empty;
        public int chanceBasisPoints;
        public bool standardReflectionEnabled;
        public bool useMemoriesInWriting;
        public bool allowExtraMemoryAiRequests;
        public bool occasionalMemoryReflections;
        public long optionalRequestInvalidationGeneration;
        public bool higherPriorityWorkAvailable;
        public bool hasEligibleMemory;
    }

    /// <summary>
    /// One pure evaluation result. The day/key fields may commit after evaluation; the quadrum field
    /// is deliberately absent because <see cref="MemoryQuietCadencePolicy.TryCommitActivatedQuadrum"/>
    /// owns that later activation-only decision.
    /// </summary>
    internal sealed class MemoryQuietCadencePlan
    {
        public bool valid;
        public bool evaluatedNow;
        public bool alreadyEvaluatedToday;
        public bool chancePassed;
        public bool candidateEligible;
        public int evaluatedAbsoluteDay = -1;
        public int absoluteQuadrum = -1;
        public int priorActivatedAbsoluteQuadrum = -1;
        public int normalizedBasisPoints;
        public ulong sample;
        public string rngHash = string.Empty;
        public string decisionKey = string.Empty;
    }

    /// <summary>
    /// Exact SHA-256/basis-point implementation frozen by memory-plan §T6.10. All framing uses the
    /// repository's shipped UTF-16-length + ':' segment grammar and BOM-less UTF-8 hashing.
    /// </summary>
    internal static class MemoryDeterministicRngV1
    {
        private const string RngDomain = "quiet-memory-rng-v1";
        private const string DecisionDomain = "quiet-memory-decision-v1";
        private const string PassToken = "pass";
        private const string FailToken = "fail";
        private const ulong ThresholdQuotient = 1844674407370955UL;
        private const ulong ThresholdRemainder = 1616UL;

        /// <summary>Clamps XML input to the exact integer domain 0..10,000.</summary>
        public static int NormalizeBasisPoints(int configured)
        {
            if (configured < 0) return 0;
            return configured > 10000 ? 10000 : configured;
        }

        /// <summary>
        /// Returns the finite exclusive threshold. Ten-thousand basis points represents 2^64 and is
        /// therefore reported as the one non-representable special case.
        /// </summary>
        public static bool TryGetExclusiveThreshold(int configuredBasisPoints, out ulong threshold)
        {
            int basisPoints = NormalizeBasisPoints(configuredBasisPoints);
            if (basisPoints == 10000)
            {
                threshold = 0;
                return false;
            }

            ulong value = (ulong)basisPoints;
            threshold = checked(value * ThresholdQuotient
                + (value * ThresholdRemainder) / 10000UL);
            return true;
        }

        /// <summary>Applies the exact '&lt; threshold' rule without floating point or rejection sampling.</summary>
        public static bool PassesSample(ulong sample, int configuredBasisPoints)
        {
            int basisPoints = NormalizeBasisPoints(configuredBasisPoints);
            if (basisPoints == 0) return false;
            if (basisPoints == 10000) return true;
            ulong threshold;
            return TryGetExclusiveThreshold(basisPoints, out threshold) && sample < threshold;
        }

        /// <summary>Evaluates one exact owner/epoch/day tuple and returns its replay-proof decision.</summary>
        public static bool TryEvaluate(
            string ownerPawnId,
            string ownerEpochToken,
            int absoluteDay,
            int configuredBasisPoints,
            out ulong sample,
            out bool passed,
            out string rngHash,
            out string decisionKey)
        {
            sample = 0;
            passed = false;
            rngHash = string.Empty;
            decisionKey = string.Empty;
            if (string.IsNullOrWhiteSpace(ownerPawnId)
                || string.IsNullOrWhiteSpace(ownerEpochToken)
                || absoluteDay < 0)
            {
                return false;
            }

            byte[] digest = Hash(
                RngDomain,
                ownerPawnId,
                ownerEpochToken,
                absoluteDay.ToString(CultureInfo.InvariantCulture));
            sample = LittleEndianUInt64(digest);
            passed = PassesSample(sample, configuredBasisPoints);
            rngHash = LowerHex(digest);
            decisionKey = CreateDecisionKey(
                ownerPawnId, ownerEpochToken, absoluteDay, passed);
            return decisionKey.Length == 64;
        }

        /// <summary>Creates the saved proof token for the supplied deterministic pass/fail result.</summary>
        public static string CreateDecisionKey(
            string ownerPawnId,
            string ownerEpochToken,
            int absoluteDay,
            bool passed)
        {
            if (string.IsNullOrWhiteSpace(ownerPawnId)
                || string.IsNullOrWhiteSpace(ownerEpochToken)
                || absoluteDay < 0)
            {
                return string.Empty;
            }

            return LowerHex(Hash(
                DecisionDomain,
                ownerPawnId,
                ownerEpochToken,
                absoluteDay.ToString(CultureInfo.InvariantCulture),
                passed ? PassToken : FailToken));
        }

        /// <summary>Validates an existing day key and recovers only its exact pass/fail token.</summary>
        public static bool TryReadDecision(
            string ownerPawnId,
            string ownerEpochToken,
            int absoluteDay,
            string decisionKey,
            out bool passed)
        {
            passed = false;
            string value = decisionKey ?? string.Empty;
            string pass = CreateDecisionKey(ownerPawnId, ownerEpochToken, absoluteDay, true);
            if (pass.Length == 64 && string.Equals(value, pass, StringComparison.Ordinal))
            {
                passed = true;
                return true;
            }

            string fail = CreateDecisionKey(ownerPawnId, ownerEpochToken, absoluteDay, false);
            return fail.Length == 64 && string.Equals(value, fail, StringComparison.Ordinal);
        }

        private static byte[] Hash(params string[] fields)
        {
            StringBuilder framed = new StringBuilder();
            for (int index = 0; fields != null && index < fields.Length; index++)
            {
                string safe = fields[index] ?? string.Empty;
                framed.Append(safe.Length.ToString(CultureInfo.InvariantCulture));
                framed.Append(':');
                framed.Append(safe);
            }

            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(framed.ToString()));
            }
        }

        private static ulong LittleEndianUInt64(byte[] digest)
        {
            if (digest == null || digest.Length < 8) return 0;
            return (ulong)digest[0]
                | ((ulong)digest[1] << 8)
                | ((ulong)digest[2] << 16)
                | ((ulong)digest[3] << 24)
                | ((ulong)digest[4] << 32)
                | ((ulong)digest[5] << 40)
                | ((ulong)digest[6] << 48)
                | ((ulong)digest[7] << 56);
        }

        private static string LowerHex(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                result.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }

    /// <summary>
    /// Pure daily/quadrum policy around <see cref="MemoryDeterministicRngV1"/>. It evaluates only the
    /// current day (never catch-up) and cannot consume the quadrum slot before committed activation.
    /// </summary>
    internal static class MemoryQuietCadencePolicy
    {
        public static MemoryQuietCadencePlan Plan(MemoryQuietCadenceRequest request)
        {
            MemoryQuietCadencePlan plan = new MemoryQuietCadencePlan();
            if (request == null
                || string.IsNullOrWhiteSpace(request.ownerPawnId)
                || string.IsNullOrWhiteSpace(request.ownerEpochToken)
                || request.absoluteDay < 0
                || request.absoluteQuadrum < 0)
            {
                return plan;
            }

            plan.valid = true;
            plan.evaluatedAbsoluteDay = request.absoluteDay;
            plan.absoluteQuadrum = request.absoluteQuadrum;
            plan.priorActivatedAbsoluteQuadrum = request.lastActivatedAbsoluteQuadrum;
            plan.normalizedBasisPoints = MemoryDeterministicRngV1.NormalizeBasisPoints(
                request.chanceBasisPoints);

            bool gatesEffective = request.standardReflectionEnabled
                && request.useMemoriesInWriting
                && request.allowExtraMemoryAiRequests
                && request.occasionalMemoryReflections
                && request.optionalRequestInvalidationGeneration > 0
                && request.optionalRequestInvalidationGeneration < long.MaxValue;
            if (!gatesEffective
                || request.higherPriorityWorkAvailable
                || !request.hasEligibleMemory
                || request.lastActivatedAbsoluteQuadrum >= request.absoluteQuadrum
                || request.lastEvaluatedAbsoluteDay > request.absoluteDay)
            {
                return plan;
            }

            if (request.lastEvaluatedAbsoluteDay == request.absoluteDay)
            {
                plan.alreadyEvaluatedToday = true;
                bool savedPass;
                if (MemoryDeterministicRngV1.TryReadDecision(
                        request.ownerPawnId,
                        request.ownerEpochToken,
                        request.absoluteDay,
                        request.lastDecisionKey,
                        out savedPass))
                {
                    plan.chancePassed = savedPass;
                    plan.candidateEligible = savedPass;
                    plan.decisionKey = request.lastDecisionKey ?? string.Empty;
                    return plan;
                }

                // A corrupt same-day key must not create a reroll. Repair it to the exact deterministic
                // fail proof for this owner/day; the main-thread adapter may commit this bounded repair.
                plan.evaluatedNow = true;
                plan.chancePassed = false;
                plan.decisionKey = MemoryDeterministicRngV1.CreateDecisionKey(
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.absoluteDay,
                    false);
                return plan;
            }

            ulong sample;
            bool passed;
            string rngHash;
            string decisionKey;
            if (!MemoryDeterministicRngV1.TryEvaluate(
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.absoluteDay,
                    request.chanceBasisPoints,
                    out sample,
                    out passed,
                    out rngHash,
                    out decisionKey))
            {
                plan.valid = false;
                return plan;
            }

            plan.evaluatedNow = true;
            plan.sample = sample;
            plan.rngHash = rngHash;
            plan.decisionKey = decisionKey;
            plan.chancePassed = passed;
            plan.candidateEligible = passed;
            return plan;
        }

        /// <summary>
        /// Returns the sole quadrum value that may commit. Owner/event state and the common queue must
        /// both already be committed; queue rejection or failed activation consumes nothing.
        /// </summary>
        public static bool TryCommitActivatedQuadrum(
            MemoryQuietCadencePlan plan,
            bool ownerAndEventStateCommitted,
            bool commonQueueActivated,
            out int activatedAbsoluteQuadrum)
        {
            activatedAbsoluteQuadrum = -1;
            if (plan == null || !plan.valid || !plan.candidateEligible
                || !ownerAndEventStateCommitted || !commonQueueActivated
                || plan.absoluteQuadrum < 0
                || plan.priorActivatedAbsoluteQuadrum >= plan.absoluteQuadrum)
            {
                return false;
            }

            activatedAbsoluteQuadrum = plan.absoluteQuadrum;
            return true;
        }
    }
}
