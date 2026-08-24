// DiaryAcceptedPromptRetentionPolicy.cs — pure deterministic M2 accepted-prompt cap planner.
//
// The impure event repository supplies detached physical POV units. This policy measures their
// incremental XML text cost, orders them oldest-first, and returns the exact prefix to clear. It has
// no DiaryEvent, Scribe, Verse, settings, or file-system dependency.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    internal sealed class DiaryAcceptedPromptUnit
    {
        public int eventTick;
        public string eventId = string.Empty;
        public string povRole = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
    }

    internal sealed class DiaryAcceptedPromptRetentionPlan
    {
        public bool valid;
        public long retainedEscapedBytes;
        public readonly List<DiaryAcceptedPromptUnit> clearOldestPrefix =
            new List<DiaryAcceptedPromptUnit>();
    }

    /// <summary>Pure accepted-pair count/escaped-byte enforcement from §T6.11.</summary>
    internal static class DiaryAcceptedPromptRetentionPolicy
    {
        public const int ProductionPairCap = 2000;
        public const long ProductionEscapedByteCap = 16L * 1024L * 1024L;
        public const int DefensivePairCeiling = 4000;
        public const long DefensiveEscapedByteCeiling = 64L * 1024L * 1024L;
        public const int AcceptedPromptPairOverheadV1 = 256;

        public static DiaryAcceptedPromptRetentionPlan Plan(
            IEnumerable<DiaryAcceptedPromptUnit> source,
            int requestedPairCap,
            long requestedByteCap)
        {
            DiaryAcceptedPromptRetentionPlan plan = new DiaryAcceptedPromptRetentionPlan();
            if (requestedPairCap < 0 || requestedByteCap < 0) return plan;
            int pairCap = Math.Min(requestedPairCap, DefensivePairCeiling);
            long byteCap = Math.Min(requestedByteCap, DefensiveEscapedByteCeiling);
            List<DiaryAcceptedPromptUnit> units = new List<DiaryAcceptedPromptUnit>();
            long total = 0;
            try
            {
                foreach (DiaryAcceptedPromptUnit unit in source
                    ?? Array.Empty<DiaryAcceptedPromptUnit>())
                {
                    if (unit == null || (string.IsNullOrEmpty(unit.systemPrompt)
                        && string.IsNullOrEmpty(unit.userPrompt))) continue;
                    total = checked(total + Charge(unit));
                    units.Add(unit);
                }
            }
            catch (OverflowException)
            {
                return plan;
            }

            units.Sort(Compare);
            int removeCount = 0;
            while (units.Count - removeCount > pairCap || total > byteCap)
            {
                if (removeCount >= units.Count) break;
                total -= Charge(units[removeCount]);
                plan.clearOldestPrefix.Add(units[removeCount]);
                removeCount++;
            }
            plan.valid = total >= 0 && units.Count - removeCount <= pairCap && total <= byteCap;
            plan.retainedEscapedBytes = Math.Max(0, total);
            return plan;
        }

        internal static long Charge(DiaryAcceptedPromptUnit unit)
        {
            if (unit == null) return 0;
            return checked((long)AcceptedPromptPairOverheadV1
                + EscapedUtf8Bytes(unit.systemPrompt)
                + EscapedUtf8Bytes(unit.userPrompt));
        }

        private static int Compare(
            DiaryAcceptedPromptUnit left,
            DiaryAcceptedPromptUnit right)
        {
            int byTick = left.eventTick.CompareTo(right.eventTick);
            if (byTick != 0) return byTick;
            int byId = string.CompareOrdinal(left.eventId, right.eventId);
            if (byId != 0) return byId;
            return RoleOrder(left.povRole).CompareTo(RoleOrder(right.povRole));
        }

        private static int RoleOrder(string role)
        {
            if (string.Equals(role, "initiator", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(role, "recipient", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(role, "neutral", StringComparison.OrdinalIgnoreCase)) return 2;
            return 3;
        }

        private static long EscapedUtf8Bytes(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            StringBuilder escaped = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                switch (value[index])
                {
                    case '&': escaped.Append("&amp;"); break;
                    case '<': escaped.Append("&lt;"); break;
                    case '>': escaped.Append("&gt;"); break;
                    default: escaped.Append(value[index]); break;
                }
            }
            return Encoding.UTF8.GetByteCount(escaped.ToString());
        }
    }
}
