// Pure normalization and legacy-baseline policy for additive Anomaly save state. RimWorld Scribe and
// live map/tracker inspection stay in the GameComponent/model adapters; this file accepts only
// detached strings, integers, booleans, and lists so corrupt/old-save behavior is standalone-testable.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "IExposable").
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Normalizes additive visible state and creates silent phase-by-phase old-save baselines.</summary>
    internal static class AnomalyPersistencePolicy
    {
        public const int StudySchemaVersion = 1;
        public const int CurrentSchemaVersion = 2;
        public const int CurrentCreepJoinerArcSchemaVersion = 1;
        public const int MaximumHistoryRows = 4096;
        private const int MaximumIdentityCharacters = 200;

        /// <summary>Returns a detached, bounded state without inventing any historical milestone.</summary>
        public static AnomalyPersistentStateSnapshot Normalize(AnomalyPersistentStateSnapshot source)
        {
            AnomalyPersistentStateSnapshot input = source ?? new AnomalyPersistentStateSnapshot();
            return new AnomalyPersistentStateSnapshot
            {
                // Preserve a future positive version instead of downgrading it and accidentally
                // treating its state as a legacy save on the next load.
                schemaVersion = Math.Max(0, input.schemaVersion),
                firstStudyBreakthroughObserved = input.firstStudyBreakthroughObserved,
                completedStudyDefNames = NormalizeStableValues(
                    input.completedStudyDefNames, false),
                promotedStudyMilestoneKeys = NormalizeStableValues(
                    input.promotedStudyMilestoneKeys, true),
                monolithBaselineLevelDefName = CleanStablePart(
                    input.monolithBaselineLevelDefName),
                lastMonolithKnowledgeSnapshot = NormalizeMonolithKnowledge(
                    input.lastMonolithKnowledgeSnapshot),
                creepJoinerArcs = NormalizeCreepJoinerArcs(input.creepJoinerArcs)
            };
        }

        /// <summary>Creates trustworthy empty history for a new game; it never emits a page.</summary>
        public static AnomalyPersistentStateSnapshot NewGame(string currentMonolithLevelDefName)
        {
            return new AnomalyPersistentStateSnapshot
            {
                schemaVersion = CurrentSchemaVersion,
                monolithBaselineLevelDefName = CleanStablePart(currentMonolithLevelDefName)
            };
        }

        /// <summary>
        /// Advances a pre-A1 save only after the DLC is available. An incomplete scan marks the
        /// colony's first breakthrough as already observed, preferring silence over a false "first".
        /// </summary>
        public static AnomalyPersistentStateSnapshot BaselineLegacy(
            AnomalyPersistentStateSnapshot source,
            AnomalyLegacyBaselineFacts facts)
        {
            AnomalyPersistentStateSnapshot result = Normalize(source);
            if (result.schemaVersion >= StudySchemaVersion
                || facts == null || !facts.anomalyAvailable)
            {
                return result;
            }

            List<string> completed = new List<string>(result.completedStudyDefNames);
            if (facts.completedStudyDefNames != null)
            {
                completed.AddRange(facts.completedStudyDefNames);
            }

            result.schemaVersion = StudySchemaVersion;
            result.completedStudyDefNames = NormalizeStableValues(completed, false);
            result.firstStudyBreakthroughObserved = result.firstStudyBreakthroughObserved
                || facts.anyCommittedStudyProgress
                || !facts.historyComplete;
            string level = CleanStablePart(facts.currentMonolithLevelDefName);
            if (level.Length > 0) result.monolithBaselineLevelDefName = level;
            // A baseline describes existing state, not one exact study callback. Never manufacture a
            // researcher-owned knowledge snapshot that a later activation could consume.
            result.lastMonolithKnowledgeSnapshot = null;
            return result;
        }

        /// <summary>
        /// Advances a pre-A2 save only while Anomaly is available. Current joined creepjoiners become
        /// state-only rows; no arrival event, visible outcome, secret, or causality is manufactured.
        /// </summary>
        public static AnomalyPersistentStateSnapshot BaselineCreepJoiners(
            AnomalyPersistentStateSnapshot source,
            CreepJoinerLegacyBaselineFacts facts)
        {
            AnomalyPersistentStateSnapshot result = Normalize(source);
            if (result.schemaVersion >= CurrentSchemaVersion
                || result.schemaVersion > StudySchemaVersion
                || facts == null || !facts.anomalyAvailable)
            {
                return result;
            }

            List<CreepJoinerArcSnapshot> merged = new List<CreepJoinerArcSnapshot>(
                result.creepJoinerArcs);
            int count = Math.Min(
                facts.currentJoined.Count,
                AnomalyPolicyLimits.MaximumCreepJoinerArcInputs);
            for (int i = 0; i < count; i++)
            {
                CreepJoinerArcSnapshot row = facts.currentJoined[i];
                if (row == null) continue;
                merged.Add(new CreepJoinerArcSnapshot
                {
                    pawnId = row.pawnId,
                    // These four values are intentionally not imported from a baseline scan.
                    arrivalEventId = string.Empty,
                    joinedTick = row.joinedTick,
                    lastVisiblePhase = CreepJoinerPhaseTokens.Joined,
                    lastVisibleEventId = string.Empty,
                    terminal = false,
                    schemaVersion = CurrentCreepJoinerArcSchemaVersion
                });
            }

            result.creepJoinerArcs = NormalizeCreepJoinerArcs(merged);
            result.schemaVersion = CurrentSchemaVersion;
            return result;
        }

        /// <summary>Returns one normalized visible-only arc, or null for an unusable current row.</summary>
        public static CreepJoinerArcSnapshot NormalizeCreepJoinerArc(CreepJoinerArcSnapshot source)
        {
            if (source == null || source.joinedTick < 0) return null;
            string pawnId = CleanStablePart(source.pawnId);
            if (pawnId.Length == 0) return null;

            int version = Math.Max(0, source.schemaVersion);
            string phase = NormalizeCreepJoinerPhase(source.lastVisiblePhase);
            string arrivalEventId = CleanStablePart(source.arrivalEventId);
            string visibleEventId = CleanStablePart(source.lastVisibleEventId);
            bool future = version > CurrentCreepJoinerArcSchemaVersion;
            bool outcome = CreepJoinerPhaseTokens.IsOutcome(phase);
            bool terminal = future || source.terminal || outcome;
            if (future || (terminal && !outcome))
            {
                // Unknown/future terminal state is retained only as a replay barrier. It never becomes
                // a current prompt phase, and a possibly mismatched event ID is not reused.
                phase = string.Empty;
                visibleEventId = string.Empty;
            }
            else if (!terminal)
            {
                phase = CreepJoinerPhaseTokens.Joined;
                visibleEventId = string.Empty;
            }

            return new CreepJoinerArcSnapshot
            {
                pawnId = pawnId,
                arrivalEventId = arrivalEventId,
                joinedTick = source.joinedTick,
                lastVisiblePhase = phase,
                lastVisibleEventId = visibleEventId,
                terminal = terminal,
                schemaVersion = future ? version : CurrentCreepJoinerArcSchemaVersion
            };
        }

        /// <summary>Normalizes, de-duplicates, sorts, and caps creepjoiner rows deterministically.</summary>
        public static List<CreepJoinerArcSnapshot> NormalizeCreepJoinerArcs(
            List<CreepJoinerArcSnapshot> source)
        {
            Dictionary<string, CreepJoinerArcSnapshot> byPawn =
                new Dictionary<string, CreepJoinerArcSnapshot>(StringComparer.Ordinal);
            int count = Math.Min(
                source?.Count ?? 0,
                AnomalyPolicyLimits.MaximumCreepJoinerArcInputs);
            for (int i = 0; i < count; i++)
            {
                CreepJoinerArcSnapshot row = NormalizeCreepJoinerArc(source[i]);
                if (row == null) continue;
                CreepJoinerArcSnapshot current;
                byPawn[row.pawnId] = byPawn.TryGetValue(row.pawnId, out current)
                    ? MergeDuplicateArc(current, row)
                    : row;
            }

            List<CreepJoinerArcSnapshot> result = new List<CreepJoinerArcSnapshot>(byPawn.Values);
            result.Sort((left, right) => string.CompareOrdinal(left.pawnId, right.pawnId));
            if (result.Count > AnomalyPolicyLimits.MaximumCreepJoinerArcs)
                result.RemoveRange(
                    AnomalyPolicyLimits.MaximumCreepJoinerArcs,
                    result.Count - AnomalyPolicyLimits.MaximumCreepJoinerArcs);
            return result;
        }

        private static AnomalyMonolithKnowledgeSnapshot NormalizeMonolithKnowledge(
            AnomalyMonolithKnowledgeSnapshot source)
        {
            if (source == null || source.tick < 0 || source.reachedProgress < 0)
            {
                return null;
            }

            string researcher = CleanStablePart(source.researcherPawnId);
            string stage = NormalizeStudyStage(source.studyStage);
            if (researcher.Length == 0 || (stage.Length == 0 && !source.becameActivatable))
            {
                return null;
            }

            return new AnomalyMonolithKnowledgeSnapshot
            {
                researcherPawnId = researcher,
                studyStage = stage,
                tick = source.tick,
                reachedProgress = source.reachedProgress,
                becameActivatable = source.becameActivatable,
                consumed = source.consumed
            };
        }

        private static string NormalizeStudyStage(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned == AnomalyStudyStageTokens.FirstBreakthrough
                    || cleaned == AnomalyStudyStageTokens.CompletedKind
                    || cleaned == AnomalyStudyStageTokens.Promoted
                ? cleaned
                : string.Empty;
        }

        private static string NormalizeCreepJoinerPhase(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return CreepJoinerPhaseTokens.IsKnown(cleaned) ? cleaned : string.Empty;
        }

        private static CreepJoinerArcSnapshot MergeDuplicateArc(
            CreepJoinerArcSnapshot left,
            CreepJoinerArcSnapshot right)
        {
            // Corrupt duplicates have no trustworthy chronology beyond joinedTick. Preserve the
            // strongest replay barrier, choose every optional ID lexically, and choose a terminal
            // phase with a stable explicit rank so input order cannot affect the normalized save.
            bool terminal = left.terminal || right.terminal;
            CreepJoinerArcSnapshot phaseOwner = PhaseRank(left.lastVisiblePhase)
                    >= PhaseRank(right.lastVisiblePhase)
                ? left : right;
            int version = Math.Max(left.schemaVersion, right.schemaVersion);
            bool future = version > CurrentCreepJoinerArcSchemaVersion;
            return new CreepJoinerArcSnapshot
            {
                pawnId = left.pawnId,
                arrivalEventId = LexicalNonEmpty(left.arrivalEventId, right.arrivalEventId),
                joinedTick = Math.Min(left.joinedTick, right.joinedTick),
                lastVisiblePhase = future || (terminal && !CreepJoinerPhaseTokens.IsOutcome(
                        phaseOwner.lastVisiblePhase))
                    ? string.Empty : phaseOwner.lastVisiblePhase,
                lastVisibleEventId = future
                    ? string.Empty
                    : LexicalNonEmpty(left.lastVisibleEventId, right.lastVisibleEventId),
                terminal = future || terminal,
                schemaVersion = future ? version : CurrentCreepJoinerArcSchemaVersion
            };
        }

        private static int PhaseRank(string phase)
        {
            if (phase == AnomalyOutcomeTokens.Departed) return 4;
            if (phase == AnomalyOutcomeTokens.Aggressive) return 3;
            if (phase == AnomalyOutcomeTokens.Rejected) return 2;
            if (phase == CreepJoinerPhaseTokens.Joined) return 1;
            return 0;
        }

        private static string LexicalNonEmpty(string left, string right)
        {
            string a = CleanStablePart(left);
            string b = CleanStablePart(right);
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            return string.CompareOrdinal(a, b) <= 0 ? a : b;
        }

        private static List<string> NormalizeStableValues(List<string> source, bool promotionKeys)
        {
            SortedSet<string> values = new SortedSet<string>(StringComparer.Ordinal);
            if (source != null)
            {
                // Bound input inspection as well as output size. A corrupt list containing only
                // duplicates/invalid rows must not make load-time normalization scan without a ceiling.
                int count = Math.Min(source.Count, MaximumHistoryRows);
                for (int i = 0; i < count; i++)
                {
                    string cleaned = promotionKeys
                        ? CleanPromotionKey(source[i])
                        : CleanStablePart(source[i]);
                    if (cleaned.Length > 0) values.Add(cleaned);
                }
            }

            return new List<string>(values);
        }

        private static string CleanPromotionKey(string value)
        {
            string cleaned = CleanText(value);
            if (cleaned.Length == 0) return string.Empty;
            string[] parts = cleaned.Split('|');
            int progress;
            if (parts.Length != 3 || !int.TryParse(parts[1], out progress)
                || !AnomalyPolicyNormalization.ValidPromotion(parts[0], progress, parts[2]))
            {
                return string.Empty;
            }

            return AnomalyPolicyNormalization.PromotionKey(parts[0], progress, parts[2]);
        }

        private static string CleanStablePart(string value)
        {
            string cleaned = CleanText(value);
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0
                    || cleaned.IndexOf('=') >= 0
                ? string.Empty
                : cleaned;
        }

        private static string CleanText(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            if (cleaned.Length == 0 || cleaned.IndexOf('\r') >= 0 || cleaned.IndexOf('\n') >= 0)
                return string.Empty;
            // These values participate in save ownership and replay identity. Truncating one can
            // collide with a different valid identity that shares the same prefix, so an oversized
            // corrupt row must fail closed as a whole instead of being rewritten into a new key.
            return cleaned.Length <= MaximumIdentityCharacters
                ? cleaned
                : string.Empty;
        }
    }
}
