// Short-lived exact ownership bridge for persona-weapon kill side effects. Vanilla can grant a
// trait's kill memory and record ordinary combat Tales around KilledMajorThreat inside Pawn.Kill.
// We stage only exact policy-owned signals from that same call, let a durable first-kill page claim
// them, and otherwise submit them unchanged when Pawn.Kill closes. This stores no save data.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    /// <summary>One nested Pawn.Kill scope for an exact persona wielder and victim.</summary>
    internal sealed class PersonaKillCorrelationScope
    {
        public string killerPawnId = string.Empty;
        public string victimPawnId = string.Empty;
        public int openedTick;
        public int correlationTicks = 60;
        public bool claimed;
        public List<string> thoughtDefNames = new List<string>();
        public List<string> stagedThoughtDefNames = new List<string>();
        // If the Tale callback wins the race, these are the exact expected Defs that have not yet
        // arrived in this still-open Pawn.Kill scope. They are consumed once each; after End the
        // memory hook has no victim identity, so late memories deliberately fail open.
        public List<string> pendingThoughtDefNames = new List<string>();
        public List<ThoughtSignal> stagedThoughtSignals = new List<ThoughtSignal>();
        public List<string> stagedCompanionTaleDefNames = new List<string>();
        public List<TaleSignal> stagedCompanionTaleSignals = new List<TaleSignal>();
    }

    /// <summary>Stages, claims, or releases exact persona kill-memory signals.</summary>
    internal static class PersonaKillThoughtCorrelation
    {
        private const int MaximumActiveScopes = 16;
        // Trait candidates are already XML-bounded to 128. Matching that defensive bound prevents
        // a large modded persona from losing accepted memories while keeping transient state finite.
        private const int MaximumStagedThoughtSignalsPerScope = 128;
        // Vanilla currently emits at most eight policy-owned companion Tales for one kill.
        private const int MaximumStagedCompanionTalesPerScope = 16;
        private static readonly List<PersonaKillCorrelationScope> Active =
            new List<PersonaKillCorrelationScope>();

        /// <summary>Opens an exact scope after DlcContext proved the killer's current primary bond.</summary>
        public static PersonaKillCorrelationScope Begin(
            Pawn victim,
            Pawn killer,
            IList<string> thoughtDefNames,
            int tick,
            int correlationTicks)
        {
            PruneActive(tick);
            string killerId = killer?.GetUniqueLoadID() ?? string.Empty;
            string victimId = victim?.GetUniqueLoadID() ?? string.Empty;
            if (!ModsConfig.RoyaltyActive || killerId.Length == 0 || victimId.Length == 0) return null;
            PersonaKillCorrelationScope scope = new PersonaKillCorrelationScope
            {
                killerPawnId = killerId,
                victimPawnId = victimId,
                openedTick = Math.Max(0, tick),
                correlationTicks = Math.Max(1, correlationTicks),
                thoughtDefNames = CopyThoughtNames(thoughtDefNames)
            };
            Active.Add(scope);
            while (Active.Count > MaximumActiveScopes)
            {
                // FIFO eviction is a defensive bound, not an ownership decision. Release the
                // evicted scope through the normal close path so staged Thought/Tale signals are
                // returned to their ordinary pipelines instead of disappearing silently.
                End(Active[0]);
            }
            return scope;
        }

        /// <summary>Returns the most-recently-added exact active kill scope; no tick-only matching is allowed.</summary>
        public static bool TryMatchActiveKill(
            Pawn killer,
            Pawn victim,
            out PersonaKillCorrelationScope scope)
        {
            PruneActive(CurrentTick());
            scope = null;
            string killerId = killer?.GetUniqueLoadID() ?? string.Empty;
            string victimId = victim?.GetUniqueLoadID() ?? string.Empty;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                PersonaKillCorrelationScope row = Active[i];
                if (row != null && string.Equals(row.killerPawnId, killerId, StringComparison.Ordinal)
                    && string.Equals(row.victimPawnId, victimId, StringComparison.Ordinal))
                {
                    scope = row;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Claims a matching thought signal. True means the caller must not submit it normally;
        /// false means it was unrelated and remains on the ordinary Thought path.
        /// </summary>
        public static bool TryStageOrSuppress(Pawn pawn, string thoughtDefName, ThoughtSignal signal, int tick)
        {
            // This hook sees every memory in every game. Make the no-Royalty path one cheap flag
            // check before touching either correlation list.
            if (!ModsConfig.RoyaltyActive) return false;
            PruneActive(tick);
            if (Active.Count == 0) return false;
            string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                PersonaKillCorrelationScope scope = Active[i];
                if (!PersonaThoughtOwnershipPolicy.Matches(
                    scope?.killerPawnId, scope?.thoughtDefNames, pawnId, thoughtDefName)) continue;

                // After a milestone claim, suppress only one still-missing expected ThoughtDef.
                // A later ordinary memory with the same Def must remain on the ordinary path.
                if (scope.claimed)
                {
                    if (!RemoveName(scope.pendingThoughtDefNames, thoughtDefName)) continue;
                    return true;
                }

                PersonaKillSignalAction action = PersonaKillCorrelationPolicy.Decide(
                    scopeClaimed: false,
                    alreadyStaged: ContainsName(scope.stagedThoughtDefNames, thoughtDefName),
                    stagedCount: scope.stagedThoughtSignals.Count,
                    maximumStagedSignals: MaximumStagedThoughtSignalsPerScope);
                if (action == PersonaKillSignalAction.PassThrough) return false;
                if (action == PersonaKillSignalAction.Stage && signal != null)
                {
                    scope.stagedThoughtDefNames.Add((thoughtDefName ?? string.Empty).Trim());
                    scope.stagedThoughtSignals.Add(signal);
                }
                return true;
            }
            // Do not consult a post-scope owner cache here. This callback has no victim identity,
            // so suppressing by killer+ThoughtDef could swallow a legitimate later kill-thought.
            // The active scope already handles both thought→Tale and Tale→thought ordering; once
            // Pawn.Kill closes, an unmatched memory must follow the ordinary path.
            return false;
        }

        /// <summary>
        /// Stages one exact ordinary combat Tale from the active Pawn.Kill call. True means the
        /// caller must not submit it now; overflow fails open to ordinary capture.
        /// </summary>
        public static bool TryStageOrSuppressCompanionTale(
            PersonaKillCorrelationScope scope,
            string taleDefName,
            TaleSignal signal)
        {
            PruneActive(CurrentTick());
            if (scope == null || !Active.Contains(scope)) return false;
            PersonaKillSignalAction action = PersonaKillCorrelationPolicy.Decide(
                scope.claimed,
                ContainsName(scope.stagedCompanionTaleDefNames, taleDefName),
                scope.stagedCompanionTaleSignals.Count,
                MaximumStagedCompanionTalesPerScope);
            if (action == PersonaKillSignalAction.PassThrough) return false;
            if (action == PersonaKillSignalAction.Stage && signal != null)
            {
                scope.stagedCompanionTaleDefNames.Add((taleDefName ?? string.Empty).Trim());
                scope.stagedCompanionTaleSignals.Add(signal);
            }
            return true;
        }

        /// <summary>Promotes the exact active scope only after the canonical Tale page exists.</summary>
        public static void Claim(PersonaKillCorrelationScope scope, int tick)
        {
            if (scope == null || !Active.Contains(scope)) return;
            scope.claimed = true;
            scope.pendingThoughtDefNames = MissingNames(
                scope.thoughtDefNames, scope.stagedThoughtDefNames);
            scope.stagedThoughtSignals.Clear();
            scope.stagedThoughtDefNames.Clear();
            scope.stagedCompanionTaleSignals.Clear();
            scope.stagedCompanionTaleDefNames.Clear();
        }

        /// <summary>Closes one exact scope and releases unclaimed thoughts to their normal pipeline.</summary>
        public static void End(PersonaKillCorrelationScope scope)
        {
            if (scope == null) return;
            if (!Active.Remove(scope)) return;
            // Claimed scopes have already transferred ownership to the canonical Tale page. Any
            // pending expected memory after this point is intentionally fail-open (see above).
            if (scope.claimed) return;

            // Pawn.Kill's Finalizer runs after the synchronous TaleRecorder callback, so an
            // unclaimed fallback page can be queued after that kill Tale. This chronology is the
            // deliberate fail-open trade-off; changing it requires a pre-dispatch hand-off.
            List<ThoughtSignal> thoughts = new List<ThoughtSignal>(scope.stagedThoughtSignals);
            List<TaleSignal> tales = new List<TaleSignal>(scope.stagedCompanionTaleSignals);
            scope.stagedThoughtSignals.Clear();
            scope.stagedThoughtDefNames.Clear();
            scope.stagedCompanionTaleSignals.Clear();
            scope.stagedCompanionTaleDefNames.Clear();
            for (int i = 0; i < thoughts.Count; i++)
            {
                ThoughtSignal captured = thoughts[i];
                DiaryPatchSafety.Run("PersonaKillThoughtCorrelation.ReleaseThought",
                    () => DiaryEvents.Submit(captured));
            }
            for (int i = 0; i < tales.Count; i++)
            {
                TaleSignal captured = tales[i];
                DiaryPatchSafety.Run("PersonaKillThoughtCorrelation.ReleaseTale",
                    () => DiaryEvents.Submit(captured));
            }
        }

        /// <summary>Clears all unsaved ownership state at a new-game or load boundary.</summary>
        public static void Clear()
        {
            Active.Clear();
        }

        /// <summary>Number of live scopes exposed only for loaded boundary-reset assertions.</summary>
        internal static int ActiveCountForTests => Active.Count;

        private static void PruneActive(int tick)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                PersonaKillCorrelationScope row = Active[i];
                if (row == null)
                {
                    Active.RemoveAt(i);
                    continue;
                }

                if (PersonaKillCorrelationPolicy.IsExpired(
                    row.openedTick, tick, row.correlationTicks)) End(row);
            }
        }

        private static int CurrentTick()
        {
            return Find.TickManager?.TicksGame ?? 0;
        }

        private static List<string> CopyThoughtNames(IList<string> source)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (source == null ? 0 : source.Count); i++)
            {
                string value = (source[i] ?? string.Empty).Trim();
                if (value.Length > 0 && seen.Add(value)) result.Add(value);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static List<string> MissingNames(IList<string> expected, IList<string> alreadyStaged)
        {
            List<string> result = CopyThoughtNames(expected);
            for (int i = 0; i < (alreadyStaged == null ? 0 : alreadyStaged.Count); i++)
                RemoveName(result, alreadyStaged[i]);
            return result;
        }

        private static bool ContainsName(IList<string> source, string value)
        {
            string key = (value ?? string.Empty).Trim();
            for (int i = 0; i < (source == null ? 0 : source.Count); i++)
                if (string.Equals((source[i] ?? string.Empty).Trim(), key,
                    StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool RemoveName(List<string> source, string value)
        {
            string key = (value ?? string.Empty).Trim();
            for (int i = 0; i < (source == null ? 0 : source.Count); i++)
            {
                if (!string.Equals((source[i] ?? string.Empty).Trim(), key,
                    StringComparison.OrdinalIgnoreCase)) continue;
                source.RemoveAt(i);
                return true;
            }
            return false;
        }
    }
}
