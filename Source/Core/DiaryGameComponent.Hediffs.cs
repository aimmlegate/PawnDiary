// Generic hediff diary signals, built on the Event Catalog pattern (see Source/Capture/).
// The Harmony AddHediff hook and the lightweight severity scanner both route through XML Hediff-
// domain groups, so support for modded health conditions is data-only: compatibility packs add/patch
// DiaryInteractionGroupDefs instead of adding C# patches here.
//
// This is one piece of the partial DiaryGameComponent class -- see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Which capture path produced a hediff signal: the AddHediff hook (Appeared) vs. the periodic
    /// severity scan (Progressed). Top-level internal so the HediffSignal (PawnDiary.Ingestion) and the
    /// scanner share one enum.
    /// </summary>
    internal enum HediffSignalSource
    {
        Appeared,
        Progressed
    }

    public partial class DiaryGameComponent
    {
        // Active hediff severity baselines, keyed by pawn + HediffDef + body part. Transient:
        // rebuilt by scanner after load so existing conditions do not suddenly become new diary pages.
        private readonly Dictionary<string, ActiveHediffProgressionState> activeHediffProgressions =
            new Dictionary<string, ActiveHediffProgressionState>();
        private int nextHediffProgressionScanTick;
        private bool baselineHediffProgressionsOnNextScan;

        /// <summary>
        /// Clears transient severity baselines. Loaded games baseline on the first scan so old
        /// health conditions do not emit catch-up entries.
        /// </summary>
        private void ResetHediffProgressionState(bool baselineNextScan)
        {
            activeHediffProgressions.Clear();
            nextHediffProgressionScanTick = 0;
            baselineHediffProgressionsOnNextScan = baselineNextScan;
        }

        /// <summary>
        /// Periodically scans active hediffs for XML-configured severity-step increases.
        /// </summary>
        private void ScanHediffProgressionsForDiaryEvents(bool snapshotOnly)
        {
            if (PawnDiaryMod.Settings == null)
            {
                return;
            }

            HashSet<string> seenStateKeys = new HashSet<string>();
            Dictionary<string, HediffProgressionMatch> activeByStateKey =
                new Dictionary<string, HediffProgressionMatch>();
            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (!IsDiaryEligible(pawn) || pawn.health?.hediffSet?.hediffs == null)
                {
                    continue;
                }

                activeByStateKey.Clear();
                string pawnId = pawn.GetUniqueLoadID();
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int j = 0; j < hediffs.Count; j++)
                {
                    HediffProgressionMatch match = MatchHediffProgression(pawn, hediffs[j]);
                    if (match == null)
                    {
                        continue;
                    }

                    string stateKey = HediffProgressionStateKey(pawnId, match.defName, match.partKey);
                    HediffProgressionMatch existing;
                    if (activeByStateKey.TryGetValue(stateKey, out existing)
                        && existing.stage >= match.stage)
                    {
                        continue;
                    }

                    activeByStateKey[stateKey] = match;
                }

                foreach (KeyValuePair<string, HediffProgressionMatch> pair in activeByStateKey)
                {
                    seenStateKeys.Add(pair.Key);
                    UpdateHediffProgressionState(pair.Key, pair.Value, snapshotOnly);
                }
            }

            List<string> staleKeys = new List<string>();
            foreach (string key in activeHediffProgressions.Keys)
            {
                if (!seenStateKeys.Contains(key))
                {
                    staleKeys.Add(key);
                }
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                activeHediffProgressions.Remove(staleKeys[i]);
            }
        }

        private void UpdateHediffProgressionState(string stateKey, HediffProgressionMatch match, bool snapshotOnly)
        {
            ActiveHediffProgressionState state;
            bool firstSeen = !activeHediffProgressions.TryGetValue(stateKey, out state);
            if (firstSeen)
            {
                state = new ActiveHediffProgressionState();
                activeHediffProgressions[stateKey] = state;
            }

            int previousStage = state.currentStage;
            state.currentStage = match.stage;

            if (snapshotOnly || firstSeen)
            {
                return;
            }

            if (match.stage > previousStage && match.recordable)
            {
                DiaryEvents.Submit(new HediffSignal(match.pawn, match.hediff, HediffSignalSource.Progressed));
            }
        }

        private HediffProgressionMatch MatchHediffProgression(Pawn pawn, Hediff hediff)
        {
            DiaryInteractionGroupDef group;
            HediffSignalPolicy policy;
            if (!TryGetHediffPolicy(hediff, out group, out policy)
                || !policy.recordOnSeverityIncrease
                || policy.severityStep <= 0f
                || !CanRecordHediffMode(policy)
                || !HediffPassesBasicPolicy(policy, hediff))
            {
                return null;
            }

            return new HediffProgressionMatch
            {
                pawn = pawn,
                hediff = hediff,
                defName = HediffDefName(hediff),
                partKey = HediffPartKey(hediff),
                stage = HediffSeverityStage(hediff, policy),
                recordable = ShouldRecordHediff(policy, hediff)
            };
        }

        internal static HediffEventData BuildHediffEventData(Pawn pawn, Hediff hediff,
            DiaryInteractionGroupDef group, HediffSignalPolicy policy, HediffSignalSource source)
        {
            bool isAddedPart = IsAddedPartHediff(hediff);
            bool isMissingPart = IsMissingPartHediff(hediff);
            bool isOrganicAddedPart = isAddedPart && hediff.def.organicAddedBodypart;
            string partKindToken = BodyPartEventPolicy.PartKindToken(isAddedPart, isMissingPart, isOrganicAddedPart);
            string partTierToken = string.Empty;
            string attitudeToken = string.Empty;
            string causeToken = string.Empty;
            if (!string.IsNullOrEmpty(partKindToken))
            {
                if (isAddedPart)
                {
                    partTierToken = BodyPartTierToken(hediff, isOrganicAddedPart);
                }

                if (isMissingPart)
                {
                    causeToken = MissingPartCauseToken(hediff);
                }
            }

            return new HediffEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = HediffDefName(hediff),
                Label = HediffLabel(hediff),
                SourceToken = source == HediffSignalSource.Progressed ? "severity_progression" : "add",
                GroupKey = group.defName,
                ModeToken = policy.mode.ToString(),
                SeverityF2 = hediff.Severity.ToString("F2", CultureInfo.InvariantCulture),
                StageString = HediffSeverityStage(hediff, policy).ToString(CultureInfo.InvariantCulture),
                CleanedStageLabel = DiaryLineCleaner.CleanLine(hediff.CurStage?.label),
                CleanedBodyPartLabel = hediff.Part == null ? null : DiaryLineCleaner.CleanLine(hediff.Part.LabelCap),
                PartKindToken = partKindToken,
                PartTierToken = partTierToken,
                AttitudeToken = attitudeToken,
                CauseToken = causeToken,
                PassesPolicy = ShouldRecordHediff(policy, hediff),
                PolicyRecordsSource = PolicyRecordsSource(policy, source),
                ModeRecordable = CanRecordHediffMode(policy),
            };
        }

        internal void RememberHediffProgressionState(Pawn pawn, Hediff hediff, HediffSignalPolicy policy)
        {
            if (pawn == null
                || hediff == null
                || hediff.def == null
                || policy == null
                || !policy.recordOnSeverityIncrease
                || policy.severityStep <= 0f
                || !HediffPassesBasicPolicy(policy, hediff))
            {
                return;
            }

            string stateKey = HediffProgressionStateKey(
                pawn.GetUniqueLoadID(),
                HediffDefName(hediff),
                HediffPartKey(hediff));
            activeHediffProgressions[stateKey] = new ActiveHediffProgressionState
            {
                currentStage = HediffSeverityStage(hediff, policy)
            };
        }

        internal void RecordImmediateHediffEvent(Pawn pawn, Hediff hediff, DiaryInteractionGroupDef group,
            HediffSignalPolicy policy, HediffSignalSource source, HediffEventData data)
        {
            string text = ImmediateHediffText(policy, source, pawn.LabelShortCap, data.Label);
            BeliefEventEvidence beliefEvidence = null;
            BeliefContextBuildResult preparedBelief = null;
            if (!string.IsNullOrEmpty(data.PartKindToken))
            {
                beliefEvidence = BeliefEventEvidenceFactory.ForBodyModification(
                    data.PawnId,
                    data.Tick,
                    data.DefName,
                    data.Label,
                    data.CleanedBodyPartLabel,
                    data.PartKindToken,
                    data.PartTierToken);
                preparedBelief = BeliefContextBuilder.Build(
                    pawn,
                    beliefEvidence,
                    "body|" + data.PawnId + "|" + data.Tick.ToString(CultureInfo.InvariantCulture),
                    data.Tick,
                    DiaryEvent.InitiatorRole);
                data.AttitudeToken = BodyPartEventPolicy.ResolveAttitude(
                    data.PartKindToken,
                    data.PartTierToken,
                    BodyModContext.FactsFor(pawn, BodyModContext.IdeologyStance(pawn, preparedBelief?.resolution)));
            }
            string instruction = InteractionGroups.InstructionForGroup(group);
            if (!string.IsNullOrEmpty(data.PartKindToken))
            {
                instruction = AppendBodyPartInstructionCues(instruction, data);
            }
            string gameContext = HediffEventData.BuildGameContext(
                data.DefName, data.Label, data.SourceToken, data.GroupKey, data.ModeToken,
                data.SeverityF2, data.StageString, data.CleanedStageLabel, data.CleanedBodyPartLabel,
                data.PartKindToken, data.PartTierToken, data.AttitudeToken, data.CauseToken);
            gameContext = AppendBiotechFamilyContext(hediff, gameContext);

            DiaryEvent diaryEvent = AddSoloEvent(
                pawn, null, data.DefName, data.Label, text, instruction, gameContext,
                beliefEvidence, preparedBelief);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
        }

        private static string ImmediateHediffText(HediffSignalPolicy policy, HediffSignalSource source,
            string pawnLabel, string hediffLabel)
        {
            string configuredText = source == HediffSignalSource.Progressed
                ? policy?.progressedText
                : policy?.appearedText;
            if (!string.IsNullOrWhiteSpace(configuredText))
            {
                return PromptTextTemplate.Format(configuredText, pawnLabel, hediffLabel);
            }

            string configured = source == HediffSignalSource.Progressed
                ? policy?.progressedTextKey
                : policy?.appearedTextKey;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Translate(pawnLabel, hediffLabel).Resolve();
            }

            string fallbackKey = source == HediffSignalSource.Progressed
                ? "PawnDiary.Event.HediffProgressed"
                : "PawnDiary.Event.HediffAppeared";
            return fallbackKey.Translate(pawnLabel, hediffLabel).Resolve();
        }

        internal void RecordDayReflectionHediffSignal(Pawn pawn, Hediff hediff, HediffSignalPolicy policy,
            HediffSignalSource source)
        {
            if (!DiaryTuning.Current.daySummaryEnabled)
            {
                return;
            }

            string key = DaySummaryKey(pawn.GetUniqueLoadID(), CurrentDayIndex);
            List<DayHediffRecord> list;
            if (!pendingDayHediffs.TryGetValue(key, out list))
            {
                list = new List<DayHediffRecord>();
                pendingDayHediffs[key] = list;
            }

            string defName = HediffDefName(hediff);
            bool progressed = source == HediffSignalSource.Progressed;
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].defName, defName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DayHediffRecord record = list[i];
                record.progressed = record.progressed || progressed;
                record.weight = Mathf.Max(record.weight, policy.dayReflectionWeight);
                list[i] = record;
                return;
            }

            list.Add(new DayHediffRecord
            {
                defName = defName,
                label = HediffLabel(hediff),
                weight = policy.dayReflectionWeight,
                progressed = progressed
            });
        }

        internal static bool TryGetHediffPolicy(Hediff hediff, out DiaryInteractionGroupDef group,
            out HediffSignalPolicy policy)
        {
            group = null;
            policy = null;
            if (hediff == null || hediff.def == null || PawnDiaryMod.Settings == null)
            {
                return false;
            }

            group = InteractionGroups.ClassifyHediff(HediffClassifierKey(hediff));
            if (group == null || !group.HasHediffPolicy || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return false;
            }

            policy = group.hediff;
            return policy != null && policy.enabled;
        }

        internal static bool IsMissingPartHediff(Hediff hediff)
        {
            return hediff is Hediff_MissingPart
                || (hediff?.def?.hediffClass != null
                    && typeof(Hediff_MissingPart).IsAssignableFrom(hediff.def.hediffClass));
        }

        private static bool IsAddedPartHediff(Hediff hediff)
        {
            return hediff?.def?.hediffClass != null
                && typeof(Hediff_AddedPart).IsAssignableFrom(hediff.def.hediffClass);
        }

        private static string HediffClassifierKey(Hediff hediff)
        {
            if (hediff?.def == null)
            {
                return string.Empty;
            }

            bool isAddedPart = IsAddedPartHediff(hediff);
            bool isMissingPart = IsMissingPartHediff(hediff);
            return BodyPartEventPolicy.BuildHediffClassifierKey(
                HediffDefName(hediff),
                isAddedPart,
                isMissingPart,
                isAddedPart && hediff.def.organicAddedBodypart);
        }

        private static string BodyPartTierToken(Hediff hediff, bool isOrganicAddedPart)
        {
            HediffDef def = hediff?.def;
            if (def == null)
            {
                return string.Empty;
            }

            string techLevel = def.spawnThingOnRemoved == null
                ? string.Empty
                : def.spawnThingOnRemoved.techLevel.ToString();
            float efficiency = def.addedPartProps == null ? 0f : def.addedPartProps.partEfficiency;
            bool betterThanNatural = def.addedPartProps != null && def.addedPartProps.betterThanNatural;
            DiaryTuningDef tuning = DiaryTuning.Current;
            return BodyPartEventPolicy.ResolveTier(
                def.defName,
                isOrganicAddedPart,
                techLevel,
                efficiency,
                betterThanNatural,
                tuning.bodyPartTierOverrideAnomalous,
                tuning.bodyPartTierOverrideCrude,
                tuning.bodyPartTierOverrideProsthetic,
                tuning.bodyPartTierOverrideBionic,
                tuning.bodyPartTierOverrideArchotech,
                tuning.bodyPartCrudeEfficiencyBelow,
                tuning.bodyPartProstheticEfficiencyMax,
                tuning.bodyPartBionicEfficiencyMax);
        }

        private static string MissingPartCauseToken(Hediff hediff)
        {
            bool isFresh = ReadBoolMember(hediff, "IsFresh");
            object lastInjury = ReadMember(hediff, "lastInjury");
            return BodyPartEventPolicy.CauseToken(isFresh, DefNameForMember(lastInjury));
        }

        private static string AppendBodyPartInstructionCues(string instruction, HediffEventData data)
        {
            StringBuilder builder = new StringBuilder(instruction ?? string.Empty);
            AppendTranslatedCue(builder, BodyPartEventPolicy.AttitudeCueKey(data.AttitudeToken));
            AppendTranslatedCue(builder, BodyPartEventPolicy.TierCueKey(data.PartTierToken));
            AppendTranslatedCue(builder, BodyPartEventPolicy.CauseCueKey(data.CauseToken));
            return builder.ToString();
        }

        private static void AppendTranslatedCue(StringBuilder builder, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string cue = key.Translate().Resolve();
            if (string.IsNullOrWhiteSpace(cue))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            builder.Append(cue);
        }

        private static bool ReadBoolMember(object target, string memberName)
        {
            object value = ReadMember(target, memberName);
            return value is bool && (bool)value;
        }

        private static object ReadMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            FieldInfo field = type.GetField(memberName, flags);
            return field != null ? field.GetValue(target) : null;
        }

        private static string DefNameForMember(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            Def def = value as Def;
            if (def != null)
            {
                return def.defName ?? string.Empty;
            }

            object nestedDef = ReadMember(value, "def");
            Def typedNestedDef = nestedDef as Def;
            if (typedNestedDef != null)
            {
                return typedNestedDef.defName ?? string.Empty;
            }

            return ReadMember(value, "defName") as string ?? string.Empty;
        }

        private static bool PolicyRecordsSource(HediffSignalPolicy policy, HediffSignalSource source)
        {
            return source == HediffSignalSource.Progressed
                ? policy.recordOnSeverityIncrease
                : policy.recordOnAdd;
        }

        private static bool CanRecordHediffMode(HediffSignalPolicy policy)
        {
            return policy != null
                && (policy.mode == HediffDiaryMode.Immediate || DiaryTuning.Current.daySummaryEnabled);
        }

        private static bool ShouldRecordHediff(HediffSignalPolicy policy, Hediff hediff)
        {
            if (!HediffPassesBasicPolicy(policy, hediff))
            {
                return false;
            }

            HediffDef def = hediff.def;
            if (policy.chronicAlways && def.chronic)
            {
                return true;
            }

            if (policy.sickThoughtAlways && def.makesSickThought)
            {
                return true;
            }

            if (policy.addictionAlways && hediff is Hediff_Addiction)
            {
                return true;
            }

            if (policy.missingPartAlways && hediff is Hediff_MissingPart)
            {
                return true;
            }

            return hediff.Severity >= Mathf.Max(0f, policy.minSeverity);
        }

        private static bool HediffPassesBasicPolicy(HediffSignalPolicy policy, Hediff hediff)
        {
            if (policy == null || hediff == null || hediff.def == null)
            {
                return false;
            }

            if (policy.visibleOnly && !hediff.Visible)
            {
                return false;
            }

            if (policy.badOnly && !hediff.def.isBad)
            {
                return false;
            }

            if (policy.excludeInjuries && hediff is Hediff_Injury)
            {
                return false;
            }

            return true;
        }

        private static int HediffSeverityStage(Hediff hediff, HediffSignalPolicy policy)
        {
            if (hediff == null || policy == null || policy.severityStep <= 0f)
            {
                return 0;
            }

            return Mathf.FloorToInt(Mathf.Max(0f, hediff.Severity) / policy.severityStep);
        }

        internal static string HediffDedupKey(Pawn pawn, Hediff hediff, HediffSignalPolicy policy,
            HediffSignalSource source)
        {
            int stage = source == HediffSignalSource.Progressed ? HediffSeverityStage(hediff, policy) : 0;
            return "hediff|" + source.ToString().ToLowerInvariant()
                + "|" + pawn.GetUniqueLoadID()
                + "|" + HediffDefName(hediff)
                + "|" + HediffPartKey(hediff)
                + "|" + stage.ToString(CultureInfo.InvariantCulture);
        }

        private static string HediffDefName(Hediff hediff)
        {
            return hediff?.def?.defName ?? hediff?.Label ?? string.Empty;
        }

        private static string HediffLabel(Hediff hediff)
        {
            string label = DiaryLineCleaner.CleanLine(hediff?.LabelCap);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            label = DiaryLineCleaner.CleanLine(hediff?.def?.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? HediffDefName(hediff) : label;
        }

        private static string HediffPartKey(Hediff hediff)
        {
            BodyPartRecord part = hediff?.Part;
            if (part == null)
            {
                return "whole_body";
            }

            List<string> path = new List<string>();
            BodyPartRecord current = part;
            while (current != null)
            {
                string defName = current.def?.defName ?? "part";
                path.Add(defName + "[" + BodyPartSiblingIndex(current).ToString(CultureInfo.InvariantCulture) + "]");
                current = current.parent;
            }

            path.Reverse();
            return string.Join("/", path.ToArray());
        }

        private static int BodyPartSiblingIndex(BodyPartRecord part)
        {
            if (part?.parent?.parts == null)
            {
                return 0;
            }

            List<BodyPartRecord> siblings = part.parent.parts;
            for (int i = 0; i < siblings.Count; i++)
            {
                if (ReferenceEquals(siblings[i], part))
                {
                    return i;
                }
            }

            return 0;
        }

        private static string HediffProgressionStateKey(string pawnId, string defName, string partKey)
        {
            return pawnId + "|" + defName + "|" + partKey;
        }

        private class ActiveHediffProgressionState
        {
            public int currentStage;
        }

        private class HediffProgressionMatch
        {
            public Pawn pawn;
            public Hediff hediff;
            public string defName;
            public string partKey;
            public int stage;
            public bool recordable;
        }
    }
}
