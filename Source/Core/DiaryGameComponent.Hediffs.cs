// Generic hediff diary signals. The Harmony AddHediff hook and the lightweight severity scanner
// both route through XML Hediff-domain groups, so support for modded health conditions is data-only:
// compatibility packs add/patch DiaryInteractionGroupDefs instead of adding C# patches here.
// This is one piece of the partial DiaryGameComponent class -- see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private enum HediffSignalSource
        {
            Appeared,
            Progressed
        }

        // Active hediff severity baselines, keyed by pawn + HediffDef + body part. Transient:
        // rebuilt by scanner after load so existing conditions do not suddenly become new diary pages.
        private readonly Dictionary<string, ActiveHediffProgressionState> activeHediffProgressions =
            new Dictionary<string, ActiveHediffProgressionState>();
        private int nextHediffProgressionScanTick;
        private bool baselineHediffProgressionsOnNextScan;

        /// <summary>
        /// Records a colonist's newly-added hediff according to the matching Hediff-domain group.
        /// Called from the Pawn_HealthTracker.AddHediff Harmony hook.
        /// </summary>
        public void RecordHediffAppeared(Pawn pawn, Hediff hediff)
        {
            RecordHediffSignal(pawn, hediff, HediffSignalSource.Appeared);
        }

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
                RecordHediffSignal(match.pawn, match.hediff, HediffSignalSource.Progressed);
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

        private void RecordHediffSignal(Pawn pawn, Hediff hediff, HediffSignalSource source)
        {
            if (pawn == null || hediff == null || hediff.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            DiaryInteractionGroupDef group;
            HediffSignalPolicy policy;
            if (!TryGetHediffPolicy(hediff, out group, out policy))
            {
                return;
            }

            // AddHediff is the best moment to establish a progression baseline, even when the
            // hediff is still too mild to record. Later scanner passes can then detect a real worsen.
            if (source == HediffSignalSource.Appeared)
            {
                RememberHediffProgressionState(pawn, hediff, policy);
            }

            if (!PolicyRecordsSource(policy, source)
                || !CanRecordHediffMode(policy)
                || !ShouldRecordHediff(policy, hediff))
            {
                return;
            }

            string dedupKey = HediffDedupKey(pawn, hediff, policy, source);
            if (RecentlyRecorded(recentHediffEvents, dedupKey, Math.Max(0, policy.dedupTicks)))
            {
                return;
            }

            if (policy.mode == HediffDiaryMode.Immediate)
            {
                RecordImmediateHediffEvent(pawn, hediff, group, policy, source);
                return;
            }

            RecordDayReflectionHediffSignal(pawn, hediff, policy, source);
        }

        private void RememberHediffProgressionState(Pawn pawn, Hediff hediff, HediffSignalPolicy policy)
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

        private void RecordImmediateHediffEvent(Pawn pawn, Hediff hediff, DiaryInteractionGroupDef group,
            HediffSignalPolicy policy, HediffSignalSource source)
        {
            string defName = HediffDefName(hediff);
            string label = HediffLabel(hediff);
            string textKey = source == HediffSignalSource.Progressed
                ? "PawnDiary.Event.HediffProgressed"
                : "PawnDiary.Event.HediffAppeared";
            string text = textKey.Translate(pawn.LabelShortCap, label).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(group);
            string gameContext = BuildHediffGameContext(hediff, group, policy, source);

            DiaryEvent diaryEvent = AddSoloEvent(pawn, null, defName, label, text, instruction, gameContext);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
        }

        private void RecordDayReflectionHediffSignal(Pawn pawn, Hediff hediff, HediffSignalPolicy policy,
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

        private static bool TryGetHediffPolicy(Hediff hediff, out DiaryInteractionGroupDef group,
            out HediffSignalPolicy policy)
        {
            group = null;
            policy = null;
            if (hediff == null || hediff.def == null || PawnDiaryMod.Settings == null)
            {
                return false;
            }

            group = InteractionGroups.ClassifyHediff(hediff.def);
            if (group == null || !group.HasHediffPolicy || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return false;
            }

            policy = group.hediff;
            return policy != null && policy.enabled;
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

        private static string HediffDedupKey(Pawn pawn, Hediff hediff, HediffSignalPolicy policy,
            HediffSignalSource source)
        {
            int stage = source == HediffSignalSource.Progressed ? HediffSeverityStage(hediff, policy) : 0;
            return "hediff|" + source.ToString().ToLowerInvariant()
                + "|" + pawn.GetUniqueLoadID()
                + "|" + HediffDefName(hediff)
                + "|" + HediffPartKey(hediff)
                + "|" + stage.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildHediffGameContext(Hediff hediff, DiaryInteractionGroupDef group,
            HediffSignalPolicy policy, HediffSignalSource source)
        {
            string label = HediffLabel(hediff);
            StringBuilder builder = new StringBuilder();
            builder.Append("hediff=").Append(HediffDefName(hediff));
            builder.Append("; label=").Append(label);
            builder.Append("; source=").Append(source == HediffSignalSource.Progressed
                ? "severity_progression"
                : "add");
            builder.Append("; group=").Append(group.defName);
            builder.Append("; mode=").Append(policy.mode.ToString());
            builder.Append("; severity=").Append(hediff.Severity.ToString("F2", CultureInfo.InvariantCulture));
            builder.Append("; stage=").Append(HediffSeverityStage(hediff, policy).ToString(CultureInfo.InvariantCulture));

            string part = hediff.Part == null ? string.Empty : DiaryContextBuilder.CleanLine(hediff.Part.LabelCap);
            if (!string.IsNullOrWhiteSpace(part))
            {
                builder.Append("; body_part=").Append(part);
            }

            return builder.ToString();
        }

        private static string HediffDefName(Hediff hediff)
        {
            return hediff?.def?.defName ?? hediff?.Label ?? string.Empty;
        }

        private static string HediffLabel(Hediff hediff)
        {
            string label = DiaryContextBuilder.CleanLine(hediff?.LabelCap);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            label = DiaryContextBuilder.CleanLine(hediff?.def?.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? HediffDefName(hediff) : label;
        }

        private static string HediffPartKey(Hediff hediff)
        {
            BodyPartRecord part = hediff?.Part;
            if (part == null)
            {
                return "whole_body";
            }

            string defName = part.def?.defName ?? "part";
            return defName + "#" + part.GetHashCode().ToString(CultureInfo.InvariantCulture);
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
