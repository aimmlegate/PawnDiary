// Pure Quality Wave B6 policy: the day-digest line buffer and the low-salience daily soft cap.
//
// B6 has two halves and both of their DECISIONS are plain arithmetic over primitives, so they live
// here and are unit-tested without RimWorld:
//   • the digest buffer — which of a pawn's small moments are remembered for the day reflection, how
//     many, and which one is dropped when the buffer is full;
//   • the soft cap — whether a low-value page may be written now, or should instead collapse into
//     that pawn's digest buffer, plus the important-first highlight slot split.
//
// The impure halves stay where they belong: DiaryGameComponent.DaySummary.cs owns the saved rows and
// the localized evidence lines, and DiaryGameComponent.Dispatch.cs owns the emit/suppress side effect.
//
// New to C#/RimWorld? See AGENTS.md. (TS analogy: this file is a set of exported pure functions; the
// component is the store that calls them.)
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One buffered small moment competing for a place in the day reflection. The line is already
    /// localized and prompt-safe; no live Pawn or Def crosses this boundary.
    /// </summary>
    public sealed class DigestLineCandidate
    {
        /// <summary>Game tick the moment happened, kept so the buffer stays chronological.</summary>
        public int tick;

        /// <summary>Stable saved source token (see the SourceKind constants below).</summary>
        public string sourceKind;

        /// <summary>The cleaned single-line evidence cue.</summary>
        public string line;
    }

    /// <summary>
    /// Owns every pure B6 decision so capture, dispatch, and reflection selection share one contract.
    /// </summary>
    internal static class DigestPacingPolicy
    {
        // Stable saved tokens for which source produced a digest line. They are written into the
        // reflection's gameContext tag ("digest:thought"), so treat them as save schema and never
        // rename them. They stay English on purpose (structured schema, not player-facing prose).
        public const string SourceKindInteraction = "interaction";
        public const string SourceKindThought = "thought";
        public const string SourceKindWork = "work";

        /// <summary>
        /// True when the player left the soft cap on. A cap of zero (or lower) disables B6 pacing
        /// entirely: every low-salience page emits exactly as it did before this feature.
        /// </summary>
        public static bool IsSoftCapEnabled(int cap)
        {
            return cap > 0;
        }

        /// <summary>True when this pawn already wrote its allowance of low-value pages today.</summary>
        public static bool IsAtCap(int emittedToday, int cap)
        {
            return IsSoftCapEnabled(cap) && emittedToday >= cap;
        }

        /// <summary>
        /// The decision for one page about to be emitted. A page is suppressed only when it is
        /// low-salience, the cap is on, and EVERY writer it would belong to is already at cap — so a
        /// shared pair page is never half-visible: if one side still has room, both sides get it and
        /// both counts advance (the already-capped side deliberately exceeds this soft limit).
        /// </summary>
        public static bool ShouldSuppressPage(
            bool lowSalience,
            IReadOnlyList<int> writerCounts,
            int cap)
        {
            if (!lowSalience || !IsSoftCapEnabled(cap) || writerCounts == null || writerCounts.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < writerCounts.Count; i++)
            {
                if (!IsAtCap(writerCounts[i], cap))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Advances a pawn's daily low-salience tally. Saturating, so a corrupted or hand-edited save
        /// near int.MaxValue cannot wrap into a negative count that reopens the cap forever.
        /// </summary>
        public static int NextEmittedCount(int emittedToday)
        {
            int current = emittedToday < 0 ? 0 : emittedToday;
            return current == int.MaxValue ? current : current + 1;
        }

        /// <summary>
        /// Clamps the XML buffer size. Zero disables buffering (lines are simply forgotten); the upper
        /// bound is a defensive cap so a mis-typed Def cannot grow the save without limit.
        /// </summary>
        public static int ClampMaxLines(int configured)
        {
            if (configured <= 0)
            {
                return 0;
            }

            const int HardMaxLines = 32;
            return configured > HardMaxLines ? HardMaxLines : configured;
        }

        /// <summary>
        /// Adds one line to a pawn/day buffer, newest last. Exact duplicates are dropped (the same
        /// small talk twice is not two memories), and when the buffer is full the OLDEST rows are
        /// evicted so the reflection always sees the four most recent unique moments.
        /// </summary>
        /// <returns>True when the buffer changed.</returns>
        public static bool AddLine(
            List<DigestLineCandidate> buffer,
            DigestLineCandidate candidate,
            int maxLines)
        {
            if (buffer == null)
            {
                return false;
            }

            int limit = ClampMaxLines(maxLines);
            bool changed = false;
            while (buffer.Count > limit)
            {
                buffer.RemoveAt(0);
                changed = true;
            }

            // Existing rows must converge to a newly lowered XML limit even when buffering is now
            // disabled or this particular candidate is unusable. The caller copies the normalized
            // buffer back whenever this method reports any change.
            if (limit == 0 || candidate == null)
            {
                return changed;
            }

            string line = (candidate.line ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                return changed;
            }

            candidate.line = line;
            candidate.sourceKind = (candidate.sourceKind ?? string.Empty).Trim();
            for (int i = 0; i < buffer.Count; i++)
            {
                DigestLineCandidate existing = buffer[i];
                if (existing != null && string.Equals(existing.line, line, StringComparison.Ordinal))
                {
                    return changed;
                }
            }

            buffer.Add(candidate);
            while (buffer.Count > limit)
            {
                buffer.RemoveAt(0);
            }

            return true;
        }

        /// <summary>
        /// How many highlight slots the important pool may claim first. Important evidence is what
        /// earns a reflection in the first place, so it selects before news/filler/digest can take a
        /// slot; only what it leaves over is offered to the background pool.
        /// </summary>
        public static int ImportantSelectionSlots(int importantCandidateCount, int maxHighlights)
        {
            if (maxHighlights <= 0 || importantCandidateCount <= 0)
            {
                return 0;
            }

            return importantCandidateCount < maxHighlights ? importantCandidateCount : maxHighlights;
        }

        /// <summary>Slots left for the background pool after important evidence has selected.</summary>
        public static int RemainingSelectionSlots(int selectedCount, int maxHighlights)
        {
            if (maxHighlights <= 0)
            {
                return 0;
            }

            int used = selectedCount < 0 ? 0 : selectedCount;
            return used >= maxHighlights ? 0 : maxHighlights - used;
        }
    }
}
