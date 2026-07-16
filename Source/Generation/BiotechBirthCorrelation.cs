// Main-thread transient ownership for canonical Biotech births and exact miscarriage context. The
// ApplyBirthOutcome scope stages mature Tale/Thought signals until the richer owner succeeds; failure
// releases them in original order. A short exact LordJob identity bridges the later ritual postfix.
//
// New to C#/RimWorld? See AGENTS.md ("Harmony patches", "DLC-safety", and static cache reset).
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Per-call detached/live state carried only across one ApplyBirthOutcome invocation.</summary>
    internal sealed class BiotechBirthCallState
    {
        public BirthMutationSnapshot snapshot;
        public bool birtherAliveBefore;
        public Pawn birther;
        public Pawn geneticMother;
        public Pawn father;
        public BirthCorrelationScope correlationScope;
        public bool canonicalClaimed;
    }

    /// <summary>Per-call exact miscarriage arc carried through prefix/postfix/finalizer.</summary>
    internal sealed class BiotechMiscarriageCallState
    {
        public BiotechFamilyArcState arc;
        public MiscarriageCorrelationScope correlationScope;
    }

    /// <summary>One stack-safe birth scope; callers should create/close it only through the owner.</summary>
    internal sealed class BirthCorrelationScope
    {
        internal LordJob_Ritual ritualJob;
        internal BiotechPolicySnapshot policy;
        internal readonly List<DiarySignal> stagedSignals = new List<DiarySignal>();
    }

    /// <summary>One exact miscarriage context scope with detached stable participant IDs.</summary>
    internal sealed class MiscarriageCorrelationScope
    {
        internal string familyArcId = string.Empty;
        internal string birtherId = string.Empty;
        internal string geneticMotherId = string.Empty;
        internal string fatherId = string.Empty;
        internal BiotechPolicySnapshot policy;
    }

    /// <summary>Coordinates birth duplicate suppression without retaining state across games.</summary>
    internal static class BiotechBirthCorrelation
    {
        private const int MaximumRecentRitualOwners = 32;
        // Harmony and RimWorld invoke these scopes on the main game thread only. They are static so
        // nested Tale/Thought patches can see the active ApplyBirthOutcome call, and Clear runs at
        // every game/new-game/load boundary so state never crosses saves.
        private static readonly List<BirthCorrelationScope> BirthScopes =
            new List<BirthCorrelationScope>();
        private static readonly List<MiscarriageCorrelationScope> MiscarriageScopes =
            new List<MiscarriageCorrelationScope>();
        private static readonly List<RecentRitualBirthOwner> RecentRitualOwners =
            new List<RecentRitualBirthOwner>();

        /// <summary>Opens the innermost owner scope before vanilla emits nested birth signals.</summary>
        public static BirthCorrelationScope BeginBirth(
            LordJob_Ritual ritualJob,
            BiotechPolicySnapshot policy)
        {
            BirthCorrelationScope scope = new BirthCorrelationScope
            {
                ritualJob = ritualJob,
                policy = policy ?? BiotechPolicySnapshot.CreateDefault()
            };
            BirthScopes.Add(scope);
            return scope;
        }

        /// <summary>
        /// Stages only the mature birth Tale/Thought Defs while a canonical owner is active. Other
        /// signals keep their ordinary routing even when they happen during childbirth.
        /// </summary>
        public static bool TryStageMatureSignal(string sourceDefName, DiarySignal signal)
        {
            if (BirthScopes.Count == 0 || signal == null)
            {
                return false;
            }

            BirthCorrelationScope scope = BirthScopes[BirthScopes.Count - 1];
            if (!BirthCorrelationPolicy.IsMatureBirthDef(sourceDefName, scope.policy))
            {
                return false;
            }

            DiaryEventData capturedPayload = signal.Payload;
            signal.PreserveHistoricalOrdering(
                capturedPayload == null
                    ? Find.TickManager?.TicksGame ?? 0
                    : capturedPayload.Tick);
            scope.stagedSignals.Add(signal);
            return true;
        }

        /// <summary>
        /// Closes one scope. A successful canonical owner consumes staged mature signals; otherwise
        /// they are submitted after the scope is removed so release cannot recursively re-stage them.
        /// </summary>
        public static void CloseBirth(
            BirthCorrelationScope scope,
            bool canonicalClaimed,
            int currentTick,
            int ritualExpiryTicks)
        {
            if (scope == null)
            {
                return;
            }

            BirthScopes.Remove(scope);
            if (canonicalClaimed && scope.ritualJob != null)
            {
                RememberRitualOwner(
                    scope.ritualJob,
                    Math.Max(0, currentTick),
                    Math.Max(1, ritualExpiryTicks));
            }

            if (canonicalClaimed)
            {
                return;
            }

            for (int i = 0; i < scope.stagedSignals.Count; i++)
            {
                try
                {
                    DiaryEvents.Submit(scope.stagedSignals[i]);
                }
                catch (Exception exception)
                {
                    // One malformed/modded mature signal must not prevent later signals from being
                    // released. Vanilla already completed, so this is best-effort diary fallback only.
                    Log.ErrorOnce(
                        "[Pawn Diary] A staged Biotech birth fallback signal failed; later staged "
                        + "signals will still be released: " + exception,
                        "PawnDiary.BiotechBirth.ReleaseSignal".GetHashCode());
                }
            }
        }

        /// <summary>Consumes the exact recent owner for the later childbirth-ritual postfix.</summary>
        public static bool ShouldSuppressRitual(LordJob_Ritual ritualJob, int currentTick)
        {
            if (ritualJob == null)
            {
                return false;
            }

            int now = Math.Max(0, currentTick);
            PruneRitualOwners(now);
            for (int i = RecentRitualOwners.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(RecentRitualOwners[i].ritualJob, ritualJob))
                {
                    RecentRitualOwners.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Opens exact family-loss context before vanilla creates sibling memories.</summary>
        public static MiscarriageCorrelationScope BeginMiscarriage(
            BiotechFamilyArcState arc,
            BiotechPolicySnapshot policy)
        {
            if (arc == null || string.IsNullOrWhiteSpace(arc.familyArcId))
            {
                return null;
            }

            MiscarriageCorrelationScope scope = new MiscarriageCorrelationScope
            {
                familyArcId = arc.familyArcId,
                birtherId = arc.birtherId ?? string.Empty,
                geneticMotherId = arc.geneticMotherId ?? string.Empty,
                fatherId = arc.fatherId ?? string.Empty,
                policy = policy ?? BiotechPolicySnapshot.CreateDefault()
            };
            MiscarriageScopes.Add(scope);
            return scope;
        }

        /// <summary>Returns exact family-arc/role context for Miscarried or PartnerMiscarried only.</summary>
        public static string MiscarriageContext(Pawn pawn, string thoughtDefName)
        {
            if (pawn == null || MiscarriageScopes.Count == 0)
            {
                return string.Empty;
            }

            MiscarriageCorrelationScope scope = MiscarriageScopes[MiscarriageScopes.Count - 1];
            string role = BirthCorrelationPolicy.MiscarriageRole(
                thoughtDefName,
                pawn.GetUniqueLoadID(),
                scope.birtherId,
                scope.geneticMotherId,
                scope.fatherId,
                scope.policy);

            if (role.Length == 0)
            {
                return string.Empty;
            }

            return BiotechContextKeys.FamilyArcId + "=" + BiotechContextText.Clean(scope.familyArcId)
                + "; " + BiotechContextKeys.FamilyStage + "=" + BiotechFamilyEndTokens.Loss
                + "; " + BiotechContextKeys.InitiatorFamilyRole + "=" + role;
        }

        /// <summary>Closes one miscarriage scope even when vanilla throws.</summary>
        public static void CloseMiscarriage(MiscarriageCorrelationScope scope)
        {
            if (scope != null)
            {
                MiscarriageScopes.Remove(scope);
            }
        }

        /// <summary>Clears every static scope/cache at a Game construction/new-game/load boundary.</summary>
        public static void Clear()
        {
            BirthScopes.Clear();
            MiscarriageScopes.Clear();
            RecentRitualOwners.Clear();
        }

        private static void RememberRitualOwner(LordJob_Ritual ritualJob, int now, int expiryTicks)
        {
            PruneRitualOwners(now);
            for (int i = RecentRitualOwners.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(RecentRitualOwners[i].ritualJob, ritualJob))
                {
                    RecentRitualOwners[i].expiresTick = now + expiryTicks;
                    return;
                }
            }

            if (RecentRitualOwners.Count >= MaximumRecentRitualOwners)
            {
                RecentRitualOwners.RemoveAt(0);
            }

            RecentRitualOwners.Add(new RecentRitualBirthOwner
            {
                ritualJob = ritualJob,
                expiresTick = now + expiryTicks
            });
        }

        private static void PruneRitualOwners(int now)
        {
            for (int i = RecentRitualOwners.Count - 1; i >= 0; i--)
            {
                if (now >= RecentRitualOwners[i].expiresTick)
                {
                    RecentRitualOwners.RemoveAt(i);
                }
            }
        }

        private sealed class RecentRitualBirthOwner
        {
            public LordJob_Ritual ritualJob;
            public int expiresTick;
        }
    }
}
