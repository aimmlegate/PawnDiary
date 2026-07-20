// XML schema and main-thread snapshot adapter for planned Anomaly narrative policy. Every gameplay
// identifier is a plain string, so this Def loads safely when the paid DLC is absent.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>XML row promoting one exact studied defName/progress threshold.</summary>
    public sealed class DiaryAnomalyStudyMilestoneRow
    {
        public string studiedDefName = string.Empty;
        public int minimumProgress;
        public string token = string.Empty;
    }

    /// <summary>Singleton XML-owned policy for Anomaly study, containment, and later visible arcs.</summary>
    public sealed class DiaryAnomalyPolicyDef : Def
    {
        public bool studyEnabled = true;
        public bool recordFirstStudyBreakthrough = true;
        public bool recordCompletedEntityKind = true;
        public List<DiaryAnomalyStudyMilestoneRow> promotedStudyMilestones =
            new List<DiaryAnomalyStudyMilestoneRow>();
        public int monolithKnowledgeMaxAgeTicks = 60000;
        public int studyTaleSuppressionTicks = AnomalyPolicyLimits.DefaultStudyTaleSuppressionTicks;

        public bool containmentEnabled = true;
        public int containmentWitnessRadius = AnomalyPolicyLimits.DefaultWitnessRadius;
        public int containmentMaxWriters = AnomalyPolicyLimits.DefaultContainmentWriters;
        public int containmentMaxEntityLabelsInContext = AnomalyPolicyLimits.DefaultEntityLabels;
        public int containmentDedupTicks = 2500;
        public int recentStudierMaxAgeTicks = 60000;

        // Phase A2.0 consumes creepjoiner output/dedup/radius; A2.1 uses the creepjoiner two-writer
        // ceiling for exact disclosure POVs. Retention remains reserved until liveness-aware pruning
        // can preserve replay safety. A2.2 uses its independent one/two exact-role writer ceiling.
        public bool creepJoinerEnabled = true;
        public int creepJoinerOutcomeDedupTicks = 2500;
        public int creepJoinerArcRetentionTicks = 3600000;
        public int creepJoinerMaxWitnesses = AnomalyPolicyLimits.MaximumCreepJoinerWitnesses;
        public int creepJoinerWitnessRadius = AnomalyPolicyLimits.DefaultCreepJoinerWitnessRadius;
        public bool ghoulTransformationEnabled = true;
        public int ghoulTransformationMaxWriters =
            AnomalyPolicyLimits.DefaultGhoulTransformationWriters;
        public bool voidOutcomeEnabled = true;
        public int taleOwnershipMaxDepth = AnomalyPolicyLimits.DefaultTaleOwnershipDepth;
        public int taleOwnershipExpiryTicks = AnomalyPolicyLimits.DefaultTaleOwnershipExpiryTicks;

        /// <summary>Reports malformed XML while snapshot consumers continue with safe fallbacks.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (monolithKnowledgeMaxAgeTicks < 0) yield return "monolithKnowledgeMaxAgeTicks cannot be negative.";
            if (studyTaleSuppressionTicks < 0) yield return "studyTaleSuppressionTicks cannot be negative.";
            if (containmentWitnessRadius < 1
                || containmentWitnessRadius > AnomalyPolicyLimits.MaximumWitnessRadius)
                yield return "containmentWitnessRadius must be within the defensive supported range.";
            if (containmentMaxWriters < 1
                || containmentMaxWriters > AnomalyPolicyLimits.MaximumContainmentWriters)
                yield return "containmentMaxWriters must be one or two.";
            if (containmentMaxEntityLabelsInContext < 1
                || containmentMaxEntityLabelsInContext > AnomalyPolicyLimits.MaximumEntityLabels)
                yield return "containmentMaxEntityLabelsInContext is outside the defensive supported range.";
            if (containmentDedupTicks < 0) yield return "containmentDedupTicks cannot be negative.";
            if (recentStudierMaxAgeTicks < 0) yield return "recentStudierMaxAgeTicks cannot be negative.";
            if (creepJoinerOutcomeDedupTicks < 0) yield return "creepJoinerOutcomeDedupTicks cannot be negative.";
            if (creepJoinerArcRetentionTicks < 0) yield return "creepJoinerArcRetentionTicks cannot be negative.";
            if (creepJoinerMaxWitnesses < 1
                || creepJoinerMaxWitnesses > AnomalyPolicyLimits.MaximumCreepJoinerWitnesses)
                yield return "creepJoinerMaxWitnesses must be one or two.";
            if (creepJoinerWitnessRadius < 1
                || creepJoinerWitnessRadius > AnomalyPolicyLimits.MaximumWitnessRadius)
                yield return "creepJoinerWitnessRadius is outside the defensive supported range.";
            if (ghoulTransformationMaxWriters < 1
                || ghoulTransformationMaxWriters > AnomalyPolicyLimits.MaximumGhoulTransformationWriters)
                yield return "ghoulTransformationMaxWriters must be one or two.";
            if (taleOwnershipMaxDepth < 1
                || taleOwnershipMaxDepth > AnomalyPolicyLimits.MaximumTaleOwnershipDepth)
                yield return "taleOwnershipMaxDepth is outside the defensive supported range.";
            if (taleOwnershipExpiryTicks < 0) yield return "taleOwnershipExpiryTicks cannot be negative.";

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            if (promotedStudyMilestones == null)
            {
                yield return "promotedStudyMilestones cannot be null.";
                yield break;
            }
            if (promotedStudyMilestones.Count > AnomalyPolicyLimits.MaximumStudyMilestones)
                yield return "promotedStudyMilestones exceeds the defensive supported row count.";

            for (int i = 0; i < promotedStudyMilestones.Count; i++)
            {
                DiaryAnomalyStudyMilestoneRow row = promotedStudyMilestones[i];
                if (row == null || !AnomalyPolicyNormalization.ValidPromotion(
                        row.studiedDefName, row.minimumProgress, row.token))
                {
                    yield return "promotedStudyMilestones rows require a plain studiedDefName, positive progress, and plain token.";
                    continue;
                }

                string key = AnomalyPolicyNormalization.PromotionKey(
                    row.studiedDefName, row.minimumProgress, row.token);
                if (!keys.Add(key)) yield return "promotedStudyMilestones rows must be unique.";
            }
        }
    }

    /// <summary>Copies the singleton live Def into a detached policy snapshot with safe fallbacks.</summary>
    internal static class DiaryAnomalyPolicy
    {
        internal const string DefName = "Diary_AnomalyPolicy";

        /// <summary>Returns normalized policy and never resolves an optional Anomaly Def object.</summary>
        public static AnomalyPolicySnapshot Snapshot()
        {
            DiaryAnomalyPolicyDef source =
                DefDatabase<DiaryAnomalyPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null) return AnomalyPolicySnapshot.CreateDefault();

            // Deliberately return a fresh detached snapshot. These DTOs are mutable for simple pure
            // tests, so caching one instance would let one consumer's mutation leak into later events.
            AnomalyPolicySnapshot raw = new AnomalyPolicySnapshot
            {
                studyEnabled = source.studyEnabled,
                recordFirstStudyBreakthrough = source.recordFirstStudyBreakthrough,
                recordCompletedEntityKind = source.recordCompletedEntityKind,
                monolithKnowledgeMaxAgeTicks = source.monolithKnowledgeMaxAgeTicks,
                studyTaleSuppressionTicks = source.studyTaleSuppressionTicks,
                containmentEnabled = source.containmentEnabled,
                containmentWitnessRadius = source.containmentWitnessRadius,
                containmentMaxWriters = source.containmentMaxWriters,
                containmentMaxEntityLabelsInContext = source.containmentMaxEntityLabelsInContext,
                containmentDedupTicks = source.containmentDedupTicks,
                recentStudierMaxAgeTicks = source.recentStudierMaxAgeTicks,
                creepJoinerEnabled = source.creepJoinerEnabled,
                creepJoinerOutcomeDedupTicks = source.creepJoinerOutcomeDedupTicks,
                creepJoinerArcRetentionTicks = source.creepJoinerArcRetentionTicks,
                creepJoinerMaxWitnesses = source.creepJoinerMaxWitnesses,
                creepJoinerWitnessRadius = source.creepJoinerWitnessRadius,
                ghoulTransformationEnabled = source.ghoulTransformationEnabled,
                ghoulTransformationMaxWriters = source.ghoulTransformationMaxWriters,
                voidOutcomeEnabled = source.voidOutcomeEnabled,
                taleOwnershipMaxDepth = source.taleOwnershipMaxDepth,
                taleOwnershipExpiryTicks = source.taleOwnershipExpiryTicks
            };
            int count = Math.Min(source.promotedStudyMilestones == null
                    ? 0 : source.promotedStudyMilestones.Count,
                AnomalyPolicyLimits.MaximumStudyMilestones);
            for (int i = 0; i < count; i++)
            {
                DiaryAnomalyStudyMilestoneRow row = source.promotedStudyMilestones[i];
                raw.promotedStudyMilestones.Add(row == null ? null : new AnomalyStudyMilestoneRule
                {
                    studiedDefName = row.studiedDefName,
                    minimumProgress = row.minimumProgress,
                    token = row.token
                });
            }
            return AnomalyPolicyNormalization.Normalize(raw);
        }
    }
}
