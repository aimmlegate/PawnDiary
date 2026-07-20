// Associative-memory wiring for DiaryGameComponent (design/MEMORY_SYSTEM_DESIGN.md §6–§8.5).
// This partial owns ALL impure memory state: the PawnMemoryRepository, the tombstone dictionary
// for dead-owner grace, the ExposeData/PostLoadInit hooks, and the two appliers called from the
// EventFactory funnels (recall BEFORE deposit). Pure extraction, scoring, and eviction planning
// live under Source/Pipeline/Memory/; this file is the impure seam that copies frozen DiaryEvent
// strings in and copies results back out.
//
// Centralization rule (MEMORY_WIRING_PLAN §2): everything memory-related lives HERE. No other
// partial touches the repository or the tombstone map.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers", "IExposable").
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // The saved store of every pawn's memory fragments. Persisted via ExposeMemoryData;
        // indexes rebuilt in PostLoadInit. Additive Scribe key — old saves load an empty store.
        private readonly PawnMemoryRepository memories = new PawnMemoryRepository();

        // Tombstone map: pawnId -> tick the owner was first noticed absent (dead/gone). Used by
        // the eviction pass to grant a grace period before clearing a dead pawn's fragments.
        // Persisted via the ref-keys/ref-values Scribe idiom (same as observedConditionCooldown).
        private Dictionary<string, int> memoryOwnerAbsentSinceTick = new Dictionary<string, int>();
        private List<string> memoryOwnerAbsentKeys;
        private List<int> memoryOwnerAbsentValues;

        // Deadline gate for the periodic eviction scan (W3). Not scribed — rebuilt on load.
        private int nextMemoryEvictionScanTick;

        /// <summary>
        /// Serializes the memory repository and tombstone map. Called from ExposeData after
        /// archive.ExposeArchive. Additive keys: old saves simply load empty collections.
        /// </summary>
        private void ExposeMemoryData()
        {
            memories.ExposeMemories("pawnMemoryFragments");
            Scribe_Collections.Look(ref memoryOwnerAbsentSinceTick, "memoryOwnerAbsentSinceTick",
                LookMode.Value, LookMode.Value,
                ref memoryOwnerAbsentKeys, ref memoryOwnerAbsentValues);
        }

        /// <summary>
        /// Post-load rebuild for the memory subsystem. Called from the PostLoadInit block in
        /// ExposeData, inside the existing try/catch. Rebuilds the repository index and repairs
        /// a null tombstone map.
        /// </summary>
        private void PostLoadInitMemory()
        {
            if (memoryOwnerAbsentSinceTick == null)
            {
                memoryOwnerAbsentSinceTick = new Dictionary<string, int>();
            }

            memories.RebuildIndex();
            ApplyMemoryEviction();
            nextMemoryEvictionScanTick = 0;
        }

        /// <summary>
        /// Clears transient memory state for a new game.
        /// </summary>
        private void ResetMemoryForNewGame()
        {
            memoryOwnerAbsentSinceTick.Clear();
            nextMemoryEvictionScanTick = 0;
        }

        // ── Capture hooks (called from EventFactory funnels) ─────────────────────────────────────────

        /// <summary>
        /// Runs associative recall for each first-person POV on a just-registered event, freezing
        /// the result onto the event's PovSlot. Called BEFORE deposit so an event can never recall
        /// its own fragment. Wrapped in try/catch per the NarrativeContextBuilder failure-isolation
        /// convention: a memory failure must never abort event registration.
        /// </summary>
        private void ApplyMemoryContextForEvent(DiaryEvent diaryEvent)
        {
            if (!MemorySystemEnabled())
            {
                return;
            }

            try
            {
                MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();
                if (!policy.enabled)
                {
                    return;
                }

                // Initiator POV (always present).
                ApplyMemoryRecallForRole(diaryEvent, DiaryEvent.InitiatorRole, policy);

                // Recipient POV (pairwise events only).
                if (!diaryEvent.solo && !string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId))
                {
                    ApplyMemoryRecallForRole(diaryEvent, DiaryEvent.RecipientRole, policy);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Memory recall failed for event "
                    + (diaryEvent?.eventId ?? "?") + ": " + e,
                    "PawnDiary.Memory.Recall".GetHashCode());
            }
        }

        /// <summary>
        /// Deposits memory fragments for each first-person POV on a just-registered event.
        /// Called AFTER recall. A skipped POV still deposits (the pawn experienced the event);
        /// only recall is skipped for skipped POVs (design §13).
        /// </summary>
        private void DepositMemoryFragments(DiaryEvent diaryEvent)
        {
            if (!MemorySystemEnabled())
            {
                return;
            }

            try
            {
                MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();
                if (!policy.enabled)
                {
                    return;
                }

                // Initiator POV (always present).
                DepositMemoryForRole(diaryEvent, DiaryEvent.InitiatorRole, policy);

                // Recipient POV (pairwise events only).
                if (!diaryEvent.solo && !string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId))
                {
                    DepositMemoryForRole(diaryEvent, DiaryEvent.RecipientRole, policy);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Memory deposit failed for event "
                    + (diaryEvent?.eventId ?? "?") + ": " + e,
                    "PawnDiary.Memory.Deposit".GetHashCode());
            }
        }

        // ── Internal helpers ─────────────────────────────────────────────────────────────────────────

        private static bool MemorySystemEnabled()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            return settings != null && settings.enableMemorySystem;
        }

        /// <summary>
        /// Runs recall for one POV role: builds the query from frozen event strings, calls the pure
        /// selector, freezes the rendered memoryContext onto the slot, and bumps the selected live
        /// rows' lastRecalledTick/recallCount.
        /// </summary>
        private void ApplyMemoryRecallForRole(DiaryEvent diaryEvent, string povRole, MemoryPolicySnapshot policy)
        {
            string pawnId = MemoryRolePawnId(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            // Skipped POVs do not recall (design §13): the page will not be generated, so
            // surfacing memories is wasted work. Deposit still happens below.
            if (diaryEvent.IsSkipped(povRole))
            {
                return;
            }

            IReadOnlyList<MemoryFragment> owned = memories.ForPawn(pawnId);
            if (owned.Count < Math.Max(0, policy.minFragmentsForRecall))
            {
                return;
            }

            // Build query from frozen event strings (same extraction as deposit).
            MemoryExtractionInput input = BuildExtractionInput(diaryEvent, povRole);
            MemoryExtractionResult extraction = MemoryExtraction.Extract(input, policy);
            MemoryRecallQuery query = new MemoryRecallQuery
            {
                tags = extraction.tags,
                keywords = extraction.keywords,
                currentEventId = diaryEvent.eventId,
                currentTick = diaryEvent.tick,
                seed = HumorChancePolicy.StableSeed(diaryEvent.eventId, pawnId)
            };

            // Copy live rows to snapshots for the pure selector.
            List<MemoryFragmentSnapshot> snapshots = new List<MemoryFragmentSnapshot>(owned.Count);
            for (int i = 0; i < owned.Count; i++)
            {
                snapshots.Add(owned[i].ToSnapshot());
            }

            MemoryRecallResult result = MemoryRecallSelector.Recall(query, snapshots, policy);
            if (string.IsNullOrWhiteSpace(result.memoryContext))
            {
                return;
            }

            // Freeze the rendered context onto the event slot.
            diaryEvent.SetMemoryContext(povRole, result.memoryContext);

            // Bump the selected live rows (impure write, main thread only).
            BumpRecalledFragments(owned, result);
        }

        /// <summary>
        /// Deposits one fragment for one POV role if the event clears the noise gate.
        /// </summary>
        private void DepositMemoryForRole(DiaryEvent diaryEvent, string povRole, MemoryPolicySnapshot policy)
        {
            string pawnId = MemoryRolePawnId(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            // Idempotency preflight: one event deposits at most one fragment per pawn.
            if (memories.HasDeposit(pawnId, diaryEvent.eventId))
            {
                return;
            }

            MemoryExtractionInput input = BuildExtractionInput(diaryEvent, povRole);
            MemoryExtractionResult extraction = MemoryExtraction.Extract(input, policy);

            // Noise gate: quiet/ambient events below the importance floor deposit nothing.
            if (extraction.importance < policy.minDepositImportance
                || string.IsNullOrWhiteSpace(extraction.fragmentText))
            {
                return;
            }

            MemoryFragment fragment = new MemoryFragment
            {
                memoryId = Guid.NewGuid().ToString("N"),
                pawnId = pawnId,
                sourceEventId = diaryEvent.eventId,
                text = extraction.fragmentText,
                tags = extraction.tags,
                keywords = extraction.keywords,
                importance = extraction.importance,
                createdTick = diaryEvent.tick,
                lastRecalledTick = diaryEvent.tick,
                recallCount = 0
            };

            memories.Register(fragment);
        }

        /// <summary>
        /// Builds the pure extraction input from a DiaryEvent's frozen strings for one POV role.
        /// Only reads already-persisted fields — no live Pawn/Def access.
        /// </summary>
        private static MemoryExtractionInput BuildExtractionInput(DiaryEvent diaryEvent, string povRole)
        {
            bool recipient = string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase);
            string povName = recipient ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string otherName = recipient ? diaryEvent.initiatorName : diaryEvent.recipientName;
            string rawText = recipient ? diaryEvent.recipientText : diaryEvent.initiatorText;

            return new MemoryExtractionInput
            {
                povName = povName ?? string.Empty,
                otherName = diaryEvent.solo ? string.Empty : (otherName ?? string.Empty),
                interactionLabel = diaryEvent.interactionLabel ?? string.Empty,
                colorCue = diaryEvent.colorCue ?? string.Empty,
                moodImpact = diaryEvent.moodImpact ?? string.Empty,
                importantGroup = diaryEvent.IsImportant(),
                solo = diaryEvent.solo,
                gameContext = diaryEvent.gameContext ?? string.Empty,
                rawText = rawText ?? string.Empty
            };
        }

        /// <summary>
        /// Bumps lastRecalledTick and recallCount on the live repository rows that were selected
        /// by the pure recall. Uses the pick list's memoryIds to find the matching live rows.
        /// </summary>
        private static void BumpRecalledFragments(IReadOnlyList<MemoryFragment> owned, MemoryRecallResult result)
        {
            if (result.picks == null || result.picks.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < owned.Count; i++)
            {
                MemoryFragment fragment = owned[i];
                for (int j = 0; j < result.picks.Count; j++)
                {
                    if (string.Equals(fragment.memoryId, result.picks[j].memoryId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        fragment.lastRecalledTick = now;
                        fragment.recallCount++;
                        break;
                    }
                }
            }
        }

        private static string MemoryRolePawnId(DiaryEvent diaryEvent, string povRole)
        {
            return string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase)
                ? diaryEvent.recipientPawnId
                : diaryEvent.initiatorPawnId;
        }

        // ── Eviction + lifecycle (design §10) ────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the full eviction pass: per-owner Plan -> RemoveByIds -> PlanGlobalCap -> dead-owner
        /// grace cleanup. Called pre-save (beside ApplyDiaryEventLimits), in PostLoadInit, on deposit
        /// overflow, and behind the nextMemoryEvictionScanTick deadline gate in GameComponentTickInner.
        /// </summary>
        private void ApplyMemoryEviction()
        {
            if (!MemorySystemEnabled())
            {
                return;
            }

            try
            {
                MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();
                if (!policy.enabled)
                {
                    return;
                }

                int now = Find.TickManager.TicksGame;
                HashSet<string> evictIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Per-owner eviction plans.
                List<string> ownerIds = memories.OwnerPawnIds();
                for (int i = 0; i < ownerIds.Count; i++)
                {
                    string ownerId = ownerIds[i];
                    IReadOnlyList<MemoryFragment> owned = memories.ForPawn(ownerId);
                    List<MemoryFragmentSnapshot> snapshots = new List<MemoryFragmentSnapshot>(owned.Count);
                    for (int j = 0; j < owned.Count; j++)
                    {
                        snapshots.Add(owned[j].ToSnapshot());
                    }

                    List<string> plan = MemoryEvictionPlanner.Plan(snapshots, now, policy);
                    for (int j = 0; j < plan.Count; j++)
                    {
                        evictIds.Add(plan[j]);
                    }
                }

                // Apply per-owner evictions.
                if (evictIds.Count > 0)
                {
                    memories.RemoveByIds(evictIds);
                }

                // Colony-wide global cap (always runs after per-pawn plans).
                List<string> allOwnerIds = memories.OwnerPawnIds();
                List<MemoryFragmentSnapshot> allSnapshots = new List<MemoryFragmentSnapshot>();
                for (int i = 0; i < allOwnerIds.Count; i++)
                {
                    IReadOnlyList<MemoryFragment> owned = memories.ForPawn(allOwnerIds[i]);
                    for (int j = 0; j < owned.Count; j++)
                    {
                        allSnapshots.Add(owned[j].ToSnapshot());
                    }
                }

                List<string> globalPlan = MemoryEvictionPlanner.PlanGlobalCap(allSnapshots, now, policy);
                if (globalPlan.Count > 0)
                {
                    HashSet<string> globalIds = new HashSet<string>(globalPlan, StringComparer.OrdinalIgnoreCase);
                    memories.RemoveByIds(globalIds);
                }

                // Dead-owner grace cleanup: owners no longer in the colony get a grace period,
                // then their fragments are removed.
                CleanupDeadOwners(now, policy);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Memory eviction failed: " + e,
                    "PawnDiary.Memory.Eviction".GetHashCode());
            }
        }

        /// <summary>
        /// Tracks absent owners and removes their fragments after the grace period. An owner is
        /// "absent" when their pawnId no longer maps to a living colonist. The tombstone map
        /// records when absence was first noticed; after deadOwnerGraceTicks the store is cleared.
        /// </summary>
        private void CleanupDeadOwners(int now, MemoryPolicySnapshot policy)
        {
            List<string> ownerIds = memories.OwnerPawnIds();
            HashSet<string> livePawnIds = BuildLiveColonistIdSet();

            for (int i = 0; i < ownerIds.Count; i++)
            {
                string ownerId = ownerIds[i];
                if (livePawnIds.Contains(ownerId))
                {
                    // Owner is alive; clear any tombstone.
                    memoryOwnerAbsentSinceTick.Remove(ownerId);
                    continue;
                }

                // Owner is absent. Start or check the tombstone.
                int absentSince;
                if (!memoryOwnerAbsentSinceTick.TryGetValue(ownerId, out absentSince))
                {
                    memoryOwnerAbsentSinceTick[ownerId] = now;
                    continue;
                }

                if (now - absentSince >= Math.Max(0, policy.deadOwnerGraceTicks))
                {
                    memories.RemoveOwner(ownerId);
                    memoryOwnerAbsentSinceTick.Remove(ownerId);
                }
            }
        }

        /// <summary>
        /// Builds a set of all currently-alive colonist pawn IDs for the dead-owner check.
        /// </summary>
        private static HashSet<string> BuildLiveColonistIdSet()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (colonists[i] != null)
                {
                    ids.Add(colonists[i].GetUniqueLoadID());
                }
            }

            return ids;
        }

        /// <summary>
        /// Tick-driven eviction scan, gated by the nextMemoryEvictionScanTick deadline. Runs
        /// inside GameComponentTickInner at the XML-tuned memoryEvictionScanIntervalTicks cadence.
        /// </summary>
        private void MaybeRunMemoryEvictionScan(int now)
        {
            if (!MemorySystemEnabled())
            {
                return;
            }

            if (now < nextMemoryEvictionScanTick)
            {
                return;
            }

            MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();
            nextMemoryEvictionScanTick = now + Math.Max(250, policy.memoryEvictionScanIntervalTicks);
            ApplyMemoryEviction();
        }
    }
}
