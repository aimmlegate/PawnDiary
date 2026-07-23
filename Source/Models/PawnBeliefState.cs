// Saved Phase 3 belief-observation state for one pawn. This model contains only strings, scalars,
// and bounded lists; live Pawn, Ideo, Precept, and Meme objects never cross the DlcContext adapter.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Persists passive ideology identity/certainty observations and future reflection bookkeeping.
    /// Old saves construct this object with baselineOnNextScan=true, so their first scan is silent.
    /// </summary>
    public sealed class PawnBeliefState : IExposable
    {
        public bool baselineOnNextScan = true;
        public bool hasLastObservation;
        public string lastIdeologyId = string.Empty;
        public string lastIdeologyName = string.Empty;
        public float lastCertainty;
        public int lastScanTick = -1;

        public bool hasPendingCertainty;
        public float pendingCertaintyBefore;
        public float pendingCertaintyAfter;
        public int pendingCertaintyFirstTick = -1;
        public int pendingCertaintyLastTick = -1;

        public bool pendingIdeologyChange;
        public string pendingPreviousIdeologyId = string.Empty;
        public string pendingPreviousIdeologyName = string.Empty;
        public string pendingCurrentIdeologyId = string.Empty;
        public string pendingCurrentIdeologyName = string.Empty;

        public int lastReflectionTick = -1;
        public int lastReflectionDay = -1;
        public int lastReflectionQuadrum = -1;
        public int reflectionsThisQuadrum;
        public List<string> lastReflectedSourceIds = new List<string>();
        public List<string> recentSelectedPreceptDefNames = new List<string>();
        public List<string> recentSelectedMemeDefNames = new List<string>();

        /// <summary>Reads or writes the additive deep object in a PawnDiaryRecord.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref baselineOnNextScan, "baselineOnNextScan", true);
            Scribe_Values.Look(ref hasLastObservation, "hasLastObservation", false);
            Scribe_Values.Look(ref lastIdeologyId, "lastIdeologyId");
            Scribe_Values.Look(ref lastIdeologyName, "lastIdeologyName");
            Scribe_Values.Look(ref lastCertainty, "lastCertainty", 0f);
            Scribe_Values.Look(ref lastScanTick, "lastScanTick", -1);
            Scribe_Values.Look(ref hasPendingCertainty, "hasPendingCertainty", false);
            Scribe_Values.Look(ref pendingCertaintyBefore, "pendingCertaintyBefore", 0f);
            Scribe_Values.Look(ref pendingCertaintyAfter, "pendingCertaintyAfter", 0f);
            Scribe_Values.Look(ref pendingCertaintyFirstTick, "pendingCertaintyFirstTick", -1);
            Scribe_Values.Look(ref pendingCertaintyLastTick, "pendingCertaintyLastTick", -1);
            Scribe_Values.Look(ref pendingIdeologyChange, "pendingIdeologyChange", false);
            Scribe_Values.Look(ref pendingPreviousIdeologyId, "pendingPreviousIdeologyId");
            Scribe_Values.Look(ref pendingPreviousIdeologyName, "pendingPreviousIdeologyName");
            Scribe_Values.Look(ref pendingCurrentIdeologyId, "pendingCurrentIdeologyId");
            Scribe_Values.Look(ref pendingCurrentIdeologyName, "pendingCurrentIdeologyName");
            Scribe_Values.Look(ref lastReflectionTick, "lastReflectionTick", -1);
            Scribe_Values.Look(ref lastReflectionDay, "lastReflectionDay", -1);
            Scribe_Values.Look(ref lastReflectionQuadrum, "lastReflectionQuadrum", -1);
            Scribe_Values.Look(ref reflectionsThisQuadrum, "reflectionsThisQuadrum", 0);
            Scribe_Collections.Look(ref lastReflectedSourceIds, "lastReflectedSourceIds", LookMode.Value);
            Scribe_Collections.Look(ref recentSelectedPreceptDefNames,
                "recentSelectedPreceptDefNames", LookMode.Value);
            Scribe_Collections.Look(ref recentSelectedMemeDefNames,
                "recentSelectedMemeDefNames", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int now = Find.TickManager?.TicksGame ?? int.MaxValue;
                Normalize(now, DiaryBeliefPolicy.Snapshot());
            }
        }

        /// <summary>Clamps scalar state, repairs impossible ticks, and bounds every saved ID list.</summary>
        internal void Normalize(int currentTick, BeliefPolicySnapshot policy)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            int now = Math.Max(0, currentTick);
            lastIdeologyId = Clean(lastIdeologyId, effective.maximumIdentifierCharacters);
            lastIdeologyName = Clean(lastIdeologyName, effective.maximumFieldCharacters);
            lastCertainty = Clamp01(lastCertainty);
            pendingCertaintyBefore = Clamp01(pendingCertaintyBefore);
            pendingCertaintyAfter = Clamp01(pendingCertaintyAfter);
            pendingPreviousIdeologyId = Clean(
                pendingPreviousIdeologyId, effective.maximumIdentifierCharacters);
            pendingPreviousIdeologyName = Clean(
                pendingPreviousIdeologyName, effective.maximumFieldCharacters);
            pendingCurrentIdeologyId = Clean(
                pendingCurrentIdeologyId, effective.maximumIdentifierCharacters);
            pendingCurrentIdeologyName = Clean(
                pendingCurrentIdeologyName, effective.maximumFieldCharacters);

            if (lastScanTick > now)
            {
                lastScanTick = -1;
                hasLastObservation = false;
                baselineOnNextScan = true;
                ClearPendingCertainty();
                ClearPendingIdeologyChange();
            }
            if (!hasLastObservation || lastIdeologyId.Length == 0)
            {
                hasLastObservation = false;
                baselineOnNextScan = true;
            }
            if (!hasPendingCertainty || pendingCertaintyFirstTick < 0
                || pendingCertaintyLastTick < pendingCertaintyFirstTick
                || pendingCertaintyFirstTick > now || pendingCertaintyLastTick > now
                || (long)now - pendingCertaintyLastTick
                    > effective.pendingBeliefEvidenceMaxAgeTicks)
                ClearPendingCertainty();
            if (!pendingIdeologyChange || pendingPreviousIdeologyId.Length == 0
                || pendingCurrentIdeologyId.Length == 0)
                ClearPendingIdeologyChange();

            int currentDay = now / GenDate.TicksPerDay;
            int currentQuadrum = currentDay / 15;
            if (lastReflectionTick > now || lastReflectionDay > currentDay
                || lastReflectionQuadrum > currentQuadrum)
            {
                lastReflectionTick = -1;
                lastReflectionDay = -1;
                lastReflectionQuadrum = -1;
                reflectionsThisQuadrum = 0;
            }
            lastReflectionTick = Math.Max(-1, lastReflectionTick);
            lastReflectionDay = Math.Max(-1, lastReflectionDay);
            lastReflectionQuadrum = Math.Max(-1, lastReflectionQuadrum);
            reflectionsThisQuadrum = Math.Max(0,
                Math.Min(effective.maximumBeliefReflectionsPerQuadrum, reflectionsThisQuadrum));
            lastReflectedSourceIds = NormalizeIds(
                lastReflectedSourceIds, effective.maximumReflectedBeliefSourceIds,
                effective.maximumIdentifierCharacters);
            recentSelectedPreceptDefNames = NormalizeIds(
                recentSelectedPreceptDefNames, effective.maximumRecentSelections,
                effective.maximumIdentifierCharacters);
            recentSelectedMemeDefNames = NormalizeIds(
                recentSelectedMemeDefNames, effective.maximumRecentSelections,
                effective.maximumIdentifierCharacters);
        }

        /// <summary>Returns a detached copy for the assembly-free observation reducer.</summary>
        internal BeliefScanState ToScanState()
        {
            return new BeliefScanState
            {
                baselineOnNextScan = baselineOnNextScan,
                hasLastObservation = hasLastObservation,
                lastIdeologyId = lastIdeologyId,
                lastIdeologyName = lastIdeologyName,
                lastCertainty = lastCertainty,
                lastScanTick = lastScanTick,
                hasPendingCertainty = hasPendingCertainty,
                pendingCertaintyBefore = pendingCertaintyBefore,
                pendingCertaintyAfter = pendingCertaintyAfter,
                pendingCertaintyFirstTick = pendingCertaintyFirstTick,
                pendingCertaintyLastTick = pendingCertaintyLastTick,
                pendingIdeologyChange = pendingIdeologyChange,
                pendingPreviousIdeologyId = pendingPreviousIdeologyId,
                pendingPreviousIdeologyName = pendingPreviousIdeologyName,
                pendingCurrentIdeologyId = pendingCurrentIdeologyId,
                pendingCurrentIdeologyName = pendingCurrentIdeologyName
            };
        }

        /// <summary>Copies one pure reducer result back into the persisted scalar fields.</summary>
        internal void Apply(BeliefScanState value)
        {
            if (value == null) return;
            baselineOnNextScan = value.baselineOnNextScan;
            hasLastObservation = value.hasLastObservation;
            lastIdeologyId = value.lastIdeologyId;
            lastIdeologyName = value.lastIdeologyName;
            lastCertainty = value.lastCertainty;
            lastScanTick = value.lastScanTick;
            hasPendingCertainty = value.hasPendingCertainty;
            pendingCertaintyBefore = value.pendingCertaintyBefore;
            pendingCertaintyAfter = value.pendingCertaintyAfter;
            pendingCertaintyFirstTick = value.pendingCertaintyFirstTick;
            pendingCertaintyLastTick = value.pendingCertaintyLastTick;
            pendingIdeologyChange = value.pendingIdeologyChange;
            pendingPreviousIdeologyId = value.pendingPreviousIdeologyId;
            pendingPreviousIdeologyName = value.pendingPreviousIdeologyName;
            pendingCurrentIdeologyId = value.pendingCurrentIdeologyId;
            pendingCurrentIdeologyName = value.pendingCurrentIdeologyName;
        }

        private void ClearPendingCertainty()
        {
            hasPendingCertainty = false;
            pendingCertaintyBefore = 0f;
            pendingCertaintyAfter = 0f;
            pendingCertaintyFirstTick = -1;
            pendingCertaintyLastTick = -1;
        }

        private void ClearPendingIdeologyChange()
        {
            pendingIdeologyChange = false;
            pendingPreviousIdeologyId = string.Empty;
            pendingPreviousIdeologyName = string.Empty;
            pendingCurrentIdeologyId = string.Empty;
            pendingCurrentIdeologyName = string.Empty;
        }

        private static List<string> NormalizeIds(List<string> source, int cap, int characterCap)
        {
            List<string> reverse = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                for (int i = source.Count - 1; i >= 0 && reverse.Count < cap; i--)
                {
                    string value = Clean(source[i], characterCap);
                    if (value.Length > 0 && seen.Add(value)) reverse.Add(value);
                }
            }
            reverse.Reverse();
            return reverse;
        }

        private static string Clean(string value, int cap)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length <= cap ? cleaned : cleaned.Substring(0, cap);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
