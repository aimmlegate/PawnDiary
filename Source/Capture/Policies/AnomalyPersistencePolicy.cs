// Pure normalization and legacy-baseline policy for Anomaly A1 save state. RimWorld Scribe and
// live map/comp inspection stay in the GameComponent/model adapters; this file accepts only detached
// strings, integers, booleans, and lists so corrupt/old-save behavior is standalone-testable.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "IExposable").
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Normalizes the additive study/monolith state and creates silent old-save baselines.</summary>
    internal static class AnomalyPersistencePolicy
    {
        public const int CurrentSchemaVersion = 1;
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
                    input.lastMonolithKnowledgeSnapshot)
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
            if (result.schemaVersion >= CurrentSchemaVersion
                || facts == null || !facts.anomalyAvailable)
            {
                return result;
            }

            List<string> completed = new List<string>(result.completedStudyDefNames);
            if (facts.completedStudyDefNames != null)
            {
                completed.AddRange(facts.completedStudyDefNames);
            }

            result.schemaVersion = CurrentSchemaVersion;
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

        private static List<string> NormalizeStableValues(List<string> source, bool promotionKeys)
        {
            SortedSet<string> values = new SortedSet<string>(StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count && values.Count < MaximumHistoryRows; i++)
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
            return cleaned.Length <= MaximumIdentityCharacters
                ? cleaned
                : cleaned.Substring(0, MaximumIdentityCharacters);
        }
    }
}
