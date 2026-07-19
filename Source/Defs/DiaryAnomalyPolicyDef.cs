// XML schema and main-thread snapshot adapter for planned Anomaly narrative policy. Every gameplay
// identifier is a plain string, so this Def loads safely when the paid DLC is absent.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>XML row promoting one exact studied defName/progress threshold.</summary>
    public sealed class DiaryAnomalyStudyMilestoneDef
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
        public List<DiaryAnomalyStudyMilestoneDef> promotedStudyMilestones =
            new List<DiaryAnomalyStudyMilestoneDef>();
        public int monolithKnowledgeMaxAgeTicks = 60000;
        public int studyTaleSuppressionTicks = 2500;

        public bool containmentEnabled = true;
        public int containmentWitnessRadius = AnomalyPolicyLimits.DefaultWitnessRadius;
        public int containmentMaxWriters = AnomalyPolicyLimits.DefaultContainmentWriters;
        public int containmentMaxEntityLabelsInContext = AnomalyPolicyLimits.DefaultEntityLabels;
        public int containmentDedupTicks = 2500;
        public int recentStudierMaxAgeTicks = 60000;

        // These later-phase switches and lifetimes are frozen now with the shared policy schema;
        // Phase A1.0 does not register any creepjoiner, ghoul, or void runtime behavior.
        public bool creepJoinerEnabled = true;
        public int creepJoinerOutcomeDedupTicks = 2500;
        public int creepJoinerArcRetentionTicks = 3600000;
        public int creepJoinerMaxWitnesses = 2;
        public bool ghoulTransformationEnabled = true;
        public bool voidOutcomeEnabled = true;
        public int taleOwnershipMaxDepth = AnomalyPolicyLimits.DefaultTaleOwnershipDepth;
        public int taleOwnershipExpiryTicks = 2500;

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
            if (creepJoinerMaxWitnesses < 1 || creepJoinerMaxWitnesses > 2)
                yield return "creepJoinerMaxWitnesses must be one or two.";
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

            for (int i = 0; i < promotedStudyMilestones.Count; i++)
            {
                DiaryAnomalyStudyMilestoneDef row = promotedStudyMilestones[i];
                if (row == null || string.IsNullOrWhiteSpace(row.studiedDefName)
                    || row.studiedDefName.IndexOf('|') >= 0 || row.minimumProgress <= 0
                    || string.IsNullOrWhiteSpace(row.token) || row.token.IndexOf('|') >= 0)
                {
                    yield return "promotedStudyMilestones rows require a plain studiedDefName, positive progress, and plain token.";
                    continue;
                }

                string key = row.studiedDefName.Trim() + "|" + row.minimumProgress + "|" + row.token.Trim();
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
            AnomalyPolicySnapshot result = AnomalyPolicySnapshot.CreateDefault();
            DiaryAnomalyPolicyDef source =
                DefDatabase<DiaryAnomalyPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null)
            {
                return result;
            }

            result.studyEnabled = source.studyEnabled;
            result.recordFirstStudyBreakthrough = source.recordFirstStudyBreakthrough;
            result.recordCompletedEntityKind = source.recordCompletedEntityKind;
            result.monolithKnowledgeMaxAgeTicks = NonNegative(
                source.monolithKnowledgeMaxAgeTicks, result.monolithKnowledgeMaxAgeTicks);
            result.studyTaleSuppressionTicks = NonNegative(
                source.studyTaleSuppressionTicks, result.studyTaleSuppressionTicks);
            result.containmentEnabled = source.containmentEnabled;
            result.containmentWitnessRadius = InRange(
                source.containmentWitnessRadius, 1, AnomalyPolicyLimits.MaximumWitnessRadius,
                result.containmentWitnessRadius);
            result.containmentMaxWriters = InRange(
                source.containmentMaxWriters, 1, AnomalyPolicyLimits.MaximumContainmentWriters,
                result.containmentMaxWriters);
            result.containmentMaxEntityLabelsInContext = InRange(
                source.containmentMaxEntityLabelsInContext, 1, AnomalyPolicyLimits.MaximumEntityLabels,
                result.containmentMaxEntityLabelsInContext);
            result.containmentDedupTicks = NonNegative(
                source.containmentDedupTicks, result.containmentDedupTicks);
            result.recentStudierMaxAgeTicks = NonNegative(
                source.recentStudierMaxAgeTicks, result.recentStudierMaxAgeTicks);
            result.creepJoinerEnabled = source.creepJoinerEnabled;
            result.creepJoinerOutcomeDedupTicks = NonNegative(
                source.creepJoinerOutcomeDedupTicks, result.creepJoinerOutcomeDedupTicks);
            result.creepJoinerArcRetentionTicks = NonNegative(
                source.creepJoinerArcRetentionTicks, result.creepJoinerArcRetentionTicks);
            result.creepJoinerMaxWitnesses = InRange(source.creepJoinerMaxWitnesses, 1, 2,
                result.creepJoinerMaxWitnesses);
            result.ghoulTransformationEnabled = source.ghoulTransformationEnabled;
            result.voidOutcomeEnabled = source.voidOutcomeEnabled;
            result.taleOwnershipMaxDepth = InRange(
                source.taleOwnershipMaxDepth, 1, AnomalyPolicyLimits.MaximumTaleOwnershipDepth,
                result.taleOwnershipMaxDepth);
            result.taleOwnershipExpiryTicks = NonNegative(
                source.taleOwnershipExpiryTicks, result.taleOwnershipExpiryTicks);
            CopyPromotions(source.promotedStudyMilestones, result.promotedStudyMilestones);
            return result;
        }

        private static void CopyPromotions(
            List<DiaryAnomalyStudyMilestoneDef> source,
            List<AnomalyStudyMilestoneRule> destination)
        {
            if (source == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                DiaryAnomalyStudyMilestoneDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.studiedDefName)
                    || row.studiedDefName.IndexOf('|') >= 0 || row.minimumProgress <= 0
                    || string.IsNullOrWhiteSpace(row.token) || row.token.IndexOf('|') >= 0)
                {
                    continue;
                }

                string studiedDefName = row.studiedDefName.Trim();
                string token = row.token.Trim();
                string key = studiedDefName + "|" + row.minimumProgress + "|" + token;
                if (seen.Add(key))
                {
                    destination.Add(new AnomalyStudyMilestoneRule
                    {
                        studiedDefName = studiedDefName,
                        minimumProgress = row.minimumProgress,
                        token = token
                    });
                }
            }
        }

        private static int NonNegative(int value, int fallback)
        {
            return value < 0 ? fallback : value;
        }

        private static int InRange(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }
    }
}
