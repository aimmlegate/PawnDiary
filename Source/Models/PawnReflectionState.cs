// Per-pawn runtime coordination state for the unified reflection scheduler. This stores only bounded
// cadence, the N4 linked-memory baseline, and one pending major-arc request; diary events remain the
// factual history.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "persistence & ticking").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Save-safe reflection cooldowns plus one deferred major-arc request. No Pawn, Def, or prompt
    /// object is retained, so old/no-DLC saves can normalize this row without resolving live content.
    /// </summary>
    public class PawnReflectionState : IExposable
    {
        private const int MaximumRelatedEventIdLength = 256;

        public bool baselineOnNextOpportunity = true;
        // N4.2 is additive after N4.1. Existing saves may already have baselineOnNextOpportunity=false,
        // so this separate default-true marker prevents historical continuity/belief debt from firing.
        public bool linkedBaselineOnNextOpportunity = true;
        public int lastReflectionTick = -1;
        public int lastMajorArcTick = -1;
        public int lastCrossArcTick = -1;
        public int lastBeliefTick = -1;
        public int lastQuadrumTick = -1;
        public int lastDayTick = -1;
        public bool pendingMajorArc;
        public int pendingMajorArcRequestedTick = -1;
        public string pendingMajorArcAvoidEventId = string.Empty;

        /// <summary>Serializes additive N4 state. Missing old-save fields keep safe silent defaults.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref baselineOnNextOpportunity, "baselineOnNextOpportunity", true);
            Scribe_Values.Look(ref linkedBaselineOnNextOpportunity,
                "linkedBaselineOnNextOpportunity", true);
            Scribe_Values.Look(ref lastReflectionTick, "lastReflectionTick", -1);
            Scribe_Values.Look(ref lastMajorArcTick, "lastMajorArcTick", -1);
            Scribe_Values.Look(ref lastCrossArcTick, "lastCrossArcTick", -1);
            Scribe_Values.Look(ref lastBeliefTick, "lastBeliefTick", -1);
            Scribe_Values.Look(ref lastQuadrumTick, "lastQuadrumTick", -1);
            Scribe_Values.Look(ref lastDayTick, "lastDayTick", -1);
            Scribe_Values.Look(ref pendingMajorArc, "pendingMajorArc", false);
            Scribe_Values.Look(ref pendingMajorArcRequestedTick, "pendingMajorArcRequestedTick", -1);
            Scribe_Values.Look(ref pendingMajorArcAvoidEventId, "pendingMajorArcAvoidEventId");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        /// <summary>Clamps malformed save values and clears incomplete pending rows.</summary>
        public void Normalize()
        {
            lastReflectionTick = Math.Max(-1, lastReflectionTick);
            lastMajorArcTick = Math.Max(-1, lastMajorArcTick);
            lastCrossArcTick = Math.Max(-1, lastCrossArcTick);
            lastBeliefTick = Math.Max(-1, lastBeliefTick);
            lastQuadrumTick = Math.Max(-1, lastQuadrumTick);
            lastDayTick = Math.Max(-1, lastDayTick);
            pendingMajorArcRequestedTick = Math.Max(-1, pendingMajorArcRequestedTick);
            pendingMajorArcAvoidEventId = CleanRelatedEventId(pendingMajorArcAvoidEventId);
            if (!pendingMajorArc || pendingMajorArcRequestedTick < 0)
            {
                ClearPendingMajorArc();
            }
        }

        /// <summary>Replaces the bounded pending major request with the newest canonical source event.</summary>
        public void QueueMajorArc(int requestedTick, string avoidRelatedEventId)
        {
            pendingMajorArc = true;
            pendingMajorArcRequestedTick = Math.Max(0, requestedTick);
            pendingMajorArcAvoidEventId = CleanRelatedEventId(avoidRelatedEventId);
        }

        /// <summary>Clears the deferred request after success, disablement, or one ineligible rest check.</summary>
        public void ClearPendingMajorArc()
        {
            pendingMajorArc = false;
            pendingMajorArcRequestedTick = -1;
            pendingMajorArcAvoidEventId = string.Empty;
        }

        /// <summary>Records the one selected reflection after Dispatch confirms that a page was created.</summary>
        public void MarkWritten(string kind, int tick)
        {
            int normalizedTick = Math.Max(0, tick);
            lastReflectionTick = normalizedTick;
            if (kind == NarrativeReflectionKindTokens.MajorArc)
            {
                lastMajorArcTick = normalizedTick;
            }
            else if (kind == NarrativeReflectionKindTokens.CrossArc)
            {
                lastCrossArcTick = normalizedTick;
            }
            else if (kind == NarrativeReflectionKindTokens.Belief)
            {
                lastBeliefTick = normalizedTick;
            }
            else if (kind == NarrativeReflectionKindTokens.Quadrum)
            {
                lastQuadrumTick = normalizedTick;
            }
            else if (kind == NarrativeReflectionKindTokens.Day)
            {
                lastDayTick = normalizedTick;
            }
        }

        /// <summary>Builds detached history rows for the pure coordinator without exposing this save model.</summary>
        internal List<ReflectionHistoryEntry> HistorySnapshot()
        {
            List<ReflectionHistoryEntry> history = new List<ReflectionHistoryEntry>();
            AddHistory(history, NarrativeReflectionKindTokens.MajorArc, lastMajorArcTick);
            AddHistory(history, NarrativeReflectionKindTokens.CrossArc, lastCrossArcTick);
            AddHistory(history, NarrativeReflectionKindTokens.Belief, lastBeliefTick);
            AddHistory(history, NarrativeReflectionKindTokens.Quadrum, lastQuadrumTick);
            AddHistory(history, NarrativeReflectionKindTokens.Day, lastDayTick);
            return history;
        }

        private static void AddHistory(List<ReflectionHistoryEntry> history, string kind, int tick)
        {
            if (tick >= 0)
            {
                history.Add(new ReflectionHistoryEntry { kind = kind, writtenTick = tick });
            }
        }

        private static string CleanRelatedEventId(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            return clean.Length <= MaximumRelatedEventIdLength
                ? clean
                : clean.Substring(0, MaximumRelatedEventIdLength);
        }
    }
}
