// One saved memory fragment (design/MEMORY_SYSTEM_DESIGN.md §4): a small self-contained piece of
// remembered experience owned by a single pawn. Fragments are deposited from an event's frozen
// fact strings (never from generated diary prose), carry a closed-vocabulary tag set plus free
// association keywords and an importance weight, and are later surfaced associatively by the pure
// MemoryRecallSelector. Tags are STRING tokens (MemoryTagTokens), not a flags enum, so unknown
// tags written by a newer mod version degrade to "no match" instead of corrupting a bitfield.
//
// All Scribe keys are additive: old saves without memory data load with empty fields and no
// errors, and the PostLoadInit block repairs null/clamped values defensively.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" / Scribe).
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// A persisted per-pawn memory fragment. Owned and stored by <see cref="PawnMemoryRepository"/>
    /// on the game component; the pure layer only ever sees <see cref="MemoryFragmentSnapshot"/>
    /// copies made via <see cref="ToSnapshot"/>, so pure code can never mutate saved state.
    /// </summary>
    public class MemoryFragment : IExposable
    {
        public string memoryId = string.Empty;        // GUID "N" format, stable across saves
        public string pawnId = string.Empty;          // owner's Pawn.GetUniqueLoadID()
        public string sourceEventId = string.Empty;   // DiaryEvent.eventId that deposited it
        public string text = string.Empty;            // fragment prose, <= fragmentTextMaxChars
        public List<string> tags = new List<string>();      // MemoryTagTokens vocabulary
        public List<string> keywords = new List<string>();  // normalized free strings, <= 8
        public float importance;                      // 0..1
        public int createdTick;                       // event tick (may be historical)
        public int lastRecalledTick;                  // init = createdTick; refreshed on recall
        public int recallCount;                       // diagnostics + eviction tie-break
        // Lore provenance (design/LORE_MEMORY_SEED_PLAN.md §3). Empty for lived memories; the
        // authoritative DiaryLoreSeedDef name for an authored lore seed. Frozen at deposit.
        public string loreSeedDefName = string.Empty;
        // Narrative-age offset (§3.1): affects only the rendered age band and the minimum-age
        // recall guard. Real createdTick/lastRecalledTick stay authoritative for recency decay,
        // cooldowns, and eviction — never backdate createdTick (§16 G1).
        public int narrativeAgeOffsetTicks;

        public void ExposeData()
        {
            Scribe_Values.Look(ref memoryId, "id");
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref sourceEventId, "sourceEventId");
            Scribe_Values.Look(ref text, "text");
            Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
            Scribe_Collections.Look(ref keywords, "keywords", LookMode.Value);
            // A missing importance (old save or hand-edited row) loads as the everyday 0.3 middle
            // of the scale instead of 0, which would make the row instantly evictable.
            Scribe_Values.Look(ref importance, "importance", 0.3f);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref lastRecalledTick, "lastRecalledTick", 0);
            Scribe_Values.Look(ref recallCount, "recallCount", 0);
            // Additive lore keys: rows saved before the lore layer load as lived memories.
            Scribe_Values.Look(ref loreSeedDefName, "loreSeedDefName");
            Scribe_Values.Look(ref narrativeAgeOffsetTicks, "narrativeAgeOffsetTicks", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                memoryId = Clean(memoryId);
                pawnId = Clean(pawnId);
                sourceEventId = Clean(sourceEventId);
                text = Clean(text);
                if (tags == null)
                {
                    tags = new List<string>();
                }

                if (keywords == null)
                {
                    keywords = new List<string>();
                }

                importance = Math.Max(0f, Math.Min(1f, importance));
                loreSeedDefName = Clean(loreSeedDefName);
                narrativeAgeOffsetTicks = Math.Max(0, narrativeAgeOffsetTicks);
                if (lastRecalledTick < createdTick)
                {
                    // A recall can never predate the memory itself; repair odd saves upward.
                    lastRecalledTick = createdTick;
                }
            }
        }

        /// <summary>
        /// Copies this saved row into the plain snapshot the pure selector/planner consume.
        /// Lists are copied so later mutation of saved state can never leak into a pure call.
        /// </summary>
        internal MemoryFragmentSnapshot ToSnapshot()
        {
            return new MemoryFragmentSnapshot
            {
                memoryId = memoryId,
                pawnId = pawnId,
                sourceEventId = sourceEventId,
                tags = tags == null ? new List<string>() : new List<string>(tags),
                keywords = keywords == null ? new List<string>() : new List<string>(keywords),
                importance = importance,
                createdTick = createdTick,
                lastRecalledTick = lastRecalledTick,
                recallCount = recallCount,
                text = text,
                loreSeedDefName = loreSeedDefName ?? string.Empty,
                narrativeAgeOffsetTicks = narrativeAgeOffsetTicks
            };
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
