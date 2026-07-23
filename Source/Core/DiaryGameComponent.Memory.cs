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

        // Per-pawn lore-seed rosters and lifetime histories (LORE_MEMORY_SEED_PLAN §6). Persisted
        // additively; the dictionary index is rebuilt on load. Rosters are history — never
        // resampled after they are first persisted (§16 G3).
        private List<PawnLoreSeedState> pawnLoreSeedStates = new List<PawnLoreSeedState>();
        private Dictionary<string, PawnLoreSeedState> loreSeedStateByPawnId =
            new Dictionary<string, PawnLoreSeedState>(StringComparer.OrdinalIgnoreCase);

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
            Scribe_Collections.Look(ref pawnLoreSeedStates, "pawnLoreSeedStates", LookMode.Deep);
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

            RebuildLoreSeedStateIndex();
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
            pawnLoreSeedStates.Clear();
            loreSeedStateByPawnId.Clear();
            nextMemoryEvictionScanTick = 0;
        }

        /// <summary>Repairs a null list and rebuilds the pawnId -> lore state index after load.</summary>
        private void RebuildLoreSeedStateIndex()
        {
            if (pawnLoreSeedStates == null)
            {
                pawnLoreSeedStates = new List<PawnLoreSeedState>();
            }

            loreSeedStateByPawnId.Clear();
            for (int i = pawnLoreSeedStates.Count - 1; i >= 0; i--)
            {
                PawnLoreSeedState state = pawnLoreSeedStates[i];
                if (state == null || string.IsNullOrWhiteSpace(state.pawnId)
                    || loreSeedStateByPawnId.ContainsKey(state.pawnId))
                {
                    pawnLoreSeedStates.RemoveAt(i);
                    continue;
                }

                loreSeedStateByPawnId[state.pawnId] = state;
            }
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

                // Projectability gate (LORE_MEMORY_SEED_PLAN §9): recall only when the finally
                // chosen prompt template actually renders MemoryContext. Neutral death/arrival
                // and title pages never project memory, so recalling for them would bump
                // lastRecalledTick/recallCount on rows whose lines can never reach a prompt.
                if (!EventProjectsMemoryContext(diaryEvent))
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
        /// True when the template that generation will finally choose for this event declares an
        /// enabled MemoryContext field. Reuses the exact generation-time path (ToPayload ->
        /// PolicyFor -> TemplateKeyFor) so the prediction cannot drift from the real choice; the
        /// event's template-determining facts (solo/death/arrival/reflection/context markers) are
        /// all frozen before registration. Main thread only — ToPayload/PolicyFor touch
        /// Translate() and DefDatabase.
        /// </summary>
        private static bool EventProjectsMemoryContext(DiaryEvent diaryEvent)
        {
            DiaryEventPayload payload = DiaryPipelineAdapters.ToPayload(diaryEvent);
            DiaryPolicySnapshot promptPolicy = DiaryPipelineAdapters.PolicyFor(payload);
            string templateKey = DiaryPromptPlanner.TemplateKeyFor(new DiaryPromptRequest
            {
                payload = payload,
                policy = promptPolicy
            });
            return DiaryPromptPlanner.ProjectsMemoryContext(promptPolicy.Template(templateKey));
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

            // Copy live rows to snapshots for the pure selector. Disabled lore rows are filtered
            // OUT here (LORE_MEMORY_SEED_PLAN §8.2) so they neither surface nor count toward the
            // store-size gate; enabled lore rows get their prose refreshed from the current
            // language's Def text with the saved prose as fallback (§3.2).
            bool loreActive = LoreSeedsActive(policy);
            List<MemoryFragmentSnapshot> snapshots = new List<MemoryFragmentSnapshot>(owned.Count);
            for (int i = 0; i < owned.Count; i++)
            {
                MemoryFragment row = owned[i];
                if (!loreActive && !string.IsNullOrEmpty(row.loreSeedDefName))
                {
                    continue;
                }

                MemoryFragmentSnapshot snapshot = row.ToSnapshot();
                ApplyLiveLoreText(snapshot);
                snapshots.Add(snapshot);
            }

            if (snapshots.Count < Math.Max(0, policy.minFragmentsForRecall))
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

        // ── Lore seeds (LORE_MEMORY_SEED_PLAN §8) ────────────────────────────────────────────────────

        /// <summary>
        /// The effective lore gate: XML policy AND the player setting. Callers already ensured
        /// the memory system itself is on.
        /// </summary>
        private static bool LoreSeedsActive(MemoryPolicySnapshot policy)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            return settings != null && settings.enableLoreSeeds
                && policy != null && policy.loreSeedsEnabled;
        }

        /// <summary>
        /// Live localized prose with saved fallback (§3.2): a resolvable lore Def supplies the
        /// current language's text; a removed/renamed Def leaves the saved prose in place. Only
        /// prose is refreshed — tags, keywords, and importance stay frozen (§16 G4).
        /// </summary>
        private static void ApplyLiveLoreText(MemoryFragmentSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.loreSeedDefName))
            {
                return;
            }

            DiaryLoreSeedDef def = DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(snapshot.loreSeedDefName);
            if (def != null && !string.IsNullOrWhiteSpace(def.text))
            {
                snapshot.text = def.text.Trim();
            }
        }

        /// <summary>
        /// Lazy initial-seed lifecycle (§8.1), called from the EventFactory funnels immediately
        /// before recall so a seed can surface on the pawn's very first prompt. First eligible
        /// event plans and persists the one-time roster; every later call is a cheap idempotent
        /// missing-target check. Old saves migrate lazily through this same path — no bulk pass.
        /// </summary>
        private void EnsureLoreSeedsForPawn(Pawn pawn)
        {
            if (pawn == null || !MemorySystemEnabled())
            {
                return;
            }

            try
            {
                MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();
                if (!policy.enabled || !LoreSeedsActive(policy))
                {
                    return;
                }

                string pawnId = pawn.GetUniqueLoadID();
                if (string.IsNullOrWhiteSpace(pawnId))
                {
                    return;
                }

                PawnLoreSeedState state;
                loreSeedStateByPawnId.TryGetValue(pawnId, out state);

                // Fast path: roster exists and every target row is already deposited.
                if (state != null && state.initialTargetDefNames.Count > 0
                    && AllInitialTargetsDeposited(pawnId, state))
                {
                    return;
                }

                List<DiaryLoreSeedDef> allDefs = DefDatabase<DiaryLoreSeedDef>.AllDefsListForReading;
                if (state == null && (allDefs == null || allDefs.Count == 0))
                {
                    // No catalog loaded (pre-L3 or all rows removed): nothing to plan.
                    return;
                }

                LoreSeedPolicy lorePolicy = LoreSeedPolicy.FromMemoryPolicy(policy, true);
                if (state == null || state.initialTargetDefNames.Count == 0)
                {
                    state = PlanInitialRoster(pawn, pawnId, state, allDefs, policy, lorePolicy);
                    if (state == null)
                    {
                        return;
                    }
                }

                DepositMissingInitialTargets(pawn, pawnId, state, policy, lorePolicy);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Lore seed preparation failed for pawn "
                    + (pawn?.LabelShort ?? "?") + ": " + e,
                    "PawnDiary.Memory.LoreSeeds".GetHashCode());
            }
        }

        /// <summary>
        /// Runs the pure planner once and persists the returned roster BEFORE any deposit attempt
        /// (§6). Returns null when nothing was eligible — planning simply retries on a later
        /// event, which matters for old saves that predate an L3 catalog.
        /// </summary>
        private PawnLoreSeedState PlanInitialRoster(
            Pawn pawn,
            string pawnId,
            PawnLoreSeedState existing,
            List<DiaryLoreSeedDef> allDefs,
            MemoryPolicySnapshot policy,
            LoreSeedPolicy lorePolicy)
        {
            List<LoreSeedCandidate> candidates = new List<LoreSeedCandidate>(allDefs.Count);
            for (int i = 0; i < allDefs.Count; i++)
            {
                if (allDefs[i] != null)
                {
                    candidates.Add(allDefs[i].ToCandidate(policy));
                }
            }

            LoreSeedPawnFacts facts = CollectLoreSeedPawnFacts(pawn, pawnId, existing);
            int seed = HumorChancePolicy.StableSeed(
                Find.World?.info?.seedString ?? string.Empty, "loreseed|" + pawnId);
            List<LoreSeedPick> picks = LoreSeedPlanner.PlanInitial(candidates, facts, lorePolicy, seed);
            if (picks.Count == 0)
            {
                return null;
            }

            // Best-effort quota diagnostic (§7.1): unknown modded pawns may have no authored
            // match; generic seeds fill the roster and we say so once instead of inventing one.
            if (lorePolicy.minSpecificInitialSeeds > 0 && !AnySpecificPick(picks))
            {
                Log.WarningOnce("[Pawn Diary] No pawn-specific lore seed matched "
                    + pawn.LabelShort + "; generic seeds fill the roster.",
                    ("PawnDiary.Memory.LoreSeeds.NoSpecific|" + pawnId).GetHashCode());
            }

            PawnLoreSeedState state = existing;
            if (state == null)
            {
                state = new PawnLoreSeedState { pawnId = pawnId };
                pawnLoreSeedStates.Add(state);
                loreSeedStateByPawnId[pawnId] = state;
            }

            for (int i = 0; i < picks.Count; i++)
            {
                if (!state.HasSeedDefName(picks[i].seedDefName))
                {
                    state.initialTargetDefNames.Add(picks[i].seedDefName);
                }
            }

            return state;
        }

        /// <summary>
        /// Deposits every roster target whose sentinel row is missing (§8.1 step 3). A removed
        /// Def is skipped without replacement (§6); a partial/faulted earlier attempt safely
        /// resumes here because deposit identity is the stable per-Def sentinel.
        /// </summary>
        private void DepositMissingInitialTargets(
            Pawn pawn,
            string pawnId,
            PawnLoreSeedState state,
            MemoryPolicySnapshot policy,
            LoreSeedPolicy lorePolicy)
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < state.initialTargetDefNames.Count; i++)
            {
                string seedDefName = state.initialTargetDefNames[i];
                string sourceEventId = LoreSeedProvenance.InitialSourceEventId(seedDefName);
                if (memories.HasDeposit(pawnId, sourceEventId))
                {
                    continue;
                }

                DiaryLoreSeedDef def = DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(seedDefName);
                if (def == null)
                {
                    continue;
                }

                bool core = def.retentionTier == LoreSeedTokens.TierCore;
                bool coreAlreadyCounted = state.coreDefNamesEverDeposited.Contains(seedDefName);
                if (core && !coreAlreadyCounted
                    && state.coreDefNamesEverDeposited.Count >= lorePolicy.maxCoreSeedsLifetime)
                {
                    // Defensive lifetime guard: the planner respects the cap, so this only fires
                    // on hand-edited or legacy state. Never over-allocate identity facts (§4).
                    continue;
                }

                LoreSeedCandidate candidate = def.ToCandidate(policy);
                List<string> tags = new List<string>(candidate.tags);
                if (!tags.Contains(MemoryTagTokens.Lore))
                {
                    tags.Add(MemoryTagTokens.Lore);
                }

                string text = candidate.fallbackText;
                if (text.Length > Math.Max(1, policy.fragmentTextMaxChars))
                {
                    // ConfigErrors already flags oversized prose; this is a last-resort bound so
                    // a malformed modded row can never bloat the store.
                    text = text.Substring(0, Math.Max(1, policy.fragmentTextMaxChars)).Trim();
                }

                MemoryFragment fragment = new MemoryFragment
                {
                    memoryId = Guid.NewGuid().ToString("N"),
                    pawnId = pawnId,
                    sourceEventId = sourceEventId,
                    text = text,
                    tags = tags,
                    keywords = candidate.keywords,
                    importance = core ? lorePolicy.coreImportance : lorePolicy.ordinaryImportance,
                    createdTick = now,
                    lastRecalledTick = now,
                    recallCount = 0,
                    loreSeedDefName = seedDefName,
                    narrativeAgeOffsetTicks = LoreSeedPlanner.ClampNarrativeOffset(
                        lorePolicy.narrativeAgeOffsetTicks, pawn.ageTracker?.AgeBiologicalTicks ?? 0L)
                };

                memories.Register(fragment);
                if (core && !coreAlreadyCounted)
                {
                    // Lifetime history records only ACTUAL deposits, after registration succeeds.
                    state.coreDefNamesEverDeposited.Add(seedDefName);
                }
            }
        }

        /// <summary>
        /// Deposit-time pawn facts for eligibility (§8.1): exact childhood/adulthood backstory
        /// Def names plus the union of their spawn categories, guarded xenotype state, hediff
        /// Def names, and the persisted rosters/histories. Plain strings only.
        /// </summary>
        private static LoreSeedPawnFacts CollectLoreSeedPawnFacts(
            Pawn pawn, string pawnId, PawnLoreSeedState state)
        {
            LoreSeedPawnFacts facts = new LoreSeedPawnFacts
            {
                pawnId = pawnId,
                biologicalAgeTicks = pawn.ageTracker?.AgeBiologicalTicks ?? 0L,
                xenotypeDefName = DlcContext.XenotypeDefName(pawn)
            };

            AddBackstoryFacts(facts, pawn.story?.Childhood);
            AddBackstoryFacts(facts, pawn.story?.Adulthood);

            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs != null)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    string hediffDefName = hediffs[i]?.def?.defName;
                    if (!string.IsNullOrWhiteSpace(hediffDefName)
                        && !facts.hediffDefNames.Contains(hediffDefName))
                    {
                        facts.hediffDefNames.Add(hediffDefName);
                    }
                }
            }

            if (state != null)
            {
                facts.initialTargetDefNames.AddRange(state.initialTargetDefNames);
                facts.progressionDefNamesEverDeposited.AddRange(state.progressionDefNamesEverDeposited);
                facts.coreDefNamesEverDeposited.AddRange(state.coreDefNamesEverDeposited);
            }

            return facts;
        }

        private static void AddBackstoryFacts(LoreSeedPawnFacts facts, BackstoryDef backstory)
        {
            if (backstory == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(backstory.defName)
                && !facts.backstoryDefNames.Contains(backstory.defName))
            {
                facts.backstoryDefNames.Add(backstory.defName);
            }

            List<string> categories = backstory.spawnCategories;
            if (categories == null)
            {
                return;
            }

            for (int i = 0; i < categories.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(categories[i])
                    && !facts.backstoryCategories.Contains(categories[i]))
                {
                    facts.backstoryCategories.Add(categories[i]);
                }
            }
        }

        private static bool AnySpecificPick(List<LoreSeedPick> picks)
        {
            for (int i = 0; i < picks.Count; i++)
            {
                if (picks[i].specific)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AllInitialTargetsDeposited(string pawnId, PawnLoreSeedState state)
        {
            for (int i = 0; i < state.initialTargetDefNames.Count; i++)
            {
                string sourceEventId = LoreSeedProvenance.InitialSourceEventId(state.initialTargetDefNames[i]);
                if (!memories.HasDeposit(pawnId, sourceEventId))
                {
                    // A removed Def can never deposit; do not spin on it forever.
                    if (DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(state.initialTargetDefNames[i]) == null)
                    {
                        continue;
                    }

                    return false;
                }
            }

            return true;
        }

        private void RemoveLoreSeedState(string pawnId)
        {
            PawnLoreSeedState state;
            if (!loreSeedStateByPawnId.TryGetValue(pawnId, out state))
            {
                return;
            }

            loreSeedStateByPawnId.Remove(pawnId);
            pawnLoreSeedStates.Remove(state);
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
                // §8.2: while the player has lore disabled, lore rows are invisible to cap
                // planning — preserved, never counted, never evicted by caps.
                bool suppressLore = !LoreSeedsActive(policy);

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

                    List<string> plan = MemoryEvictionPlanner.Plan(snapshots, now, policy, suppressLore);
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

                List<string> globalPlan = MemoryEvictionPlanner.PlanGlobalCap(allSnapshots, now, policy, suppressLore);
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
            // Lore rosters ride the same tombstone/grace lifecycle as fragments: a pawn can hold
            // a roster with zero fragments (all evicted), so the candidate set is the union.
            HashSet<string> candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> ownerIds = memories.OwnerPawnIds();
            for (int i = 0; i < ownerIds.Count; i++)
            {
                candidateIds.Add(ownerIds[i]);
            }

            foreach (string loreOwnerId in loreSeedStateByPawnId.Keys)
            {
                candidateIds.Add(loreOwnerId);
            }

            HashSet<string> livePawnIds = BuildLiveColonistIdSet();
            foreach (string ownerId in candidateIds)
            {
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
                    RemoveLoreSeedState(ownerId);
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
