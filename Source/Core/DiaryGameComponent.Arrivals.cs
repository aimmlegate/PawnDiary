// The colony-arrival flow: every colonist gets a neutral first diary entry describing how they
// joined (game start vs. recruited/joined later). Founding colonists are scanned once on the first
// tick that has maps (StartedNewGame runs before maps exist); pawns who join later are recorded by
// the Pawn.SetFaction Harmony patch, which calls RecordColonistArrival directly. These build the
// "arrival_*" game-context string the neutral arrival prompt reads.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// New-game bootstrap: records one neutral arrival entry for each starting colonist once
        /// RimWorld has finished creating maps and free-colonist lists.
        /// </summary>
        private bool TryRecordStartingColonistArrivals()
        {
            if (!CanRecordGameplayEventNow())
            {
                return false;
            }

            if (Find.Maps == null || Find.Maps.Count == 0)
            {
                return false;
            }

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                if (map?.mapPawns?.FreeColonists == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn colonist = colonists[i];
                    if (colonist != null
                        && HasArrivalBoundaryFor(colonist.GetUniqueLoadID()))
                    {
                        // A load-time repair may re-arm because one later pawn is missing. Do not
                        // resubmit every earlier colonist: knowledge-only arrivals deliberately have
                        // no page, and their tick-based memory key would otherwise change each load.
                        continue;
                    }

                    // Isolate each colonist. This scan gates ALL diary capture on a new game
                    // (EnsureStartingArrivalsBefore retries it before every signal, and ticking waits
                    // on it too), so a pawn whose context build or capture throws must cost only their
                    // own arrival page — letting the exception escape kept the gate closed forever and
                    // silenced the whole diary while erroring on every event.
                    try
                    {
                        DiaryEvents.Submit(new ArrivalSignal(colonist, BuildStartingArrivalContext(colonist)));
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[Pawn Diary] Skipped the starting-arrival entry for "
                            + (colonist?.LabelShort ?? "an unnamed colonist") + ": " + e);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Opens ordinary generation after the starting-arrival prerequisite finishes. The load/new-game
        /// scan may already have consumed its one requested pass while this gate was closed, so always ask
        /// for a fresh pass here; otherwise a not-generated page preserved during bootstrap can strand.
        /// </summary>
        private void CompleteInitialArrivalBootstrap()
        {
            if (!initialArrivalScanPending)
            {
                return;
            }

            initialArrivalScanPending = false;
            RequestGenerationScan();
        }

        /// <summary>
        /// Load-time check for the arrival bootstrap: true when any free map colonist eligible for a
        /// diary has neither an arrival page nor its durable arrival knowledge yet. Mirrors
        /// TryRecordStartingColonistArrivals' iteration
        /// (maps -> FreeColonists) so a re-armed scan can always reach every pawn this reports. Saves
        /// in this state exist: the pre-2026-07-08 wedge aborted the founding scan mid-loop (pawns
        /// after the broken one never got pages), the mod can be added to an existing colony, and a
        /// join can happen while recording is off.
        /// </summary>
        internal bool AnyFreeColonistMissingArrivalPage()
        {
            if (Find.Maps == null)
            {
                return false;
            }

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                if (map?.mapPawns?.FreeColonists == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];
                    if (pawn != null
                        && IsDiaryEligible(pawn)
                        && !HasArrivalBoundaryFor(pawn.GetUniqueLoadID()))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // internal: the ArrivalSignal capture reads this through DiaryGameComponent.Instance to drop a
        // duplicate arrival page (the pawn already has one). Checks BOTH the hot event store AND the
        // compact archive: a founding colonist's arrival page compacts into the archive once the pawn
        // passes the hot page cap (100). Missing the archive here made the load-time backfill
        // (AnyFreeColonistMissingArrivalPage) re-arm the founding scan and mint a SECOND arrival page on
        // every load of a mature colony, since the capture drop reads this same method.
        internal bool HasArrivalEventFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                if (allEvents[i] != null && allEvents[i].IsArrivalDescriptionFor(pawnId))
                {
                    return true;
                }
            }

            // Old arrival refs compact into the archive before their hot ref is dropped; a compacted
            // arrival still means "this pawn already has an arrival page".
            return archive.FirstArrivalTickForPawn(pawnId).HasValue;
        }

        /// <summary>
        /// True when a disabled arrival page still produced its durable faction-joined knowledge.
        /// This remains separately inspectable for diagnostics and loaded tests, while the capture
        /// boundary below treats either a page or this marker as proof the one-time arrival ran.
        /// </summary>
        internal bool HasArrivalKnowledgeFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            PawnKnowledgeState state = diary?.KnowledgeStateOrNull();
            if (state == null) return false;
            if (!state.IsCurrentSchema())
                return state.HasEventKind(KnowledgeTokens.EventKindFactionJoined);
            return HasCurrentMemoryFactKind(
                state,
                KnowledgeTokens.EventKindFactionJoined);
        }

        private static bool HasCurrentMemoryFactKind(
            PawnKnowledgeState state,
            string factKind)
        {
            if (state == null || string.IsNullOrWhiteSpace(factKind)) return false;
            for (int index = 0; state.standaloneBlocks != null
                && index < state.standaloneBlocks.Count; index++)
            {
                if (BlockHasFactKind(state.standaloneBlocks[index], factKind)) return true;
            }
            for (int rootIndex = 0; state.threadRoots != null
                && rootIndex < state.threadRoots.Count; rootIndex++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[rootIndex];
                for (int blockIndex = 0; root?.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                {
                    if (BlockHasFactKind(root.visibleBlocks[blockIndex], factKind)) return true;
                }
                if (BlockHasFactKind(root?.rollingSummaryBlock, factKind)) return true;
            }
            return false;
        }

        private static bool BlockHasFactKind(SavedMemoryBlock block, string factKind)
        {
            for (int index = 0; block?.facts != null && index < block.facts.Count; index++)
            {
                if (string.Equals(
                        block.facts[index]?.factKind,
                        factKind,
                        StringComparison.Ordinal)) return true;
            }
            for (int bucketIndex = 0; block?.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                if (string.Equals(
                        block.summaryPayload.factBuckets[bucketIndex]?.factKind,
                        factKind,
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal bool HasArrivalBoundaryFor(string pawnId)
        {
            return HasArrivalEventFor(pawnId) || HasArrivalKnowledgeFor(pawnId);
        }

        private static string BuildStartingArrivalContext(Pawn pawn)
        {
            List<string> parts = new List<string>
            {
                "arrival_source=game_start"
            };

            Scenario scenario = Verse.Current.Game?.Scenario;
            if (scenario != null)
            {
                string scenarioName = PromptTextSanitizer.LocalizedPromptText(scenario.name);
                if (!string.IsNullOrWhiteSpace(scenarioName))
                {
                    parts.Add("scenario_name=" + GameContextValue.Sanitize(scenarioName));
                }

                string scenarioDescription = PromptTextSanitizer.LocalizedPromptText(scenario.description);
                if (!string.IsNullOrWhiteSpace(scenarioDescription))
                {
                    parts.Add("scenario_description=" + GameContextValue.Sanitize(scenarioDescription));
                }
            }

            AddBackstoryContext(parts, pawn);

            return string.Join("; ", parts.ToArray());
        }

        private static void AddBackstoryContext(List<string> parts, Pawn pawn)
        {
            if (parts == null || pawn?.story == null)
            {
                return;
            }

            AddBackstoryContext(parts, "childhood", pawn.story.Childhood, pawn);
            AddBackstoryContext(parts, "adulthood", pawn.story.Adulthood, pawn);
        }

        private static void AddBackstoryContext(List<string> parts, string prefix, BackstoryDef backstory, Pawn pawn)
        {
            if (backstory == null || string.IsNullOrWhiteSpace(prefix))
            {
                return;
            }

            // No backstory TITLE fact ("childhood=Industrial orphan"). The description below already
            // names and explains the same past in prose, so the title only repeated it — and a bare
            // job-title label reads as a character sheet, which is the register we keep out of prompts.
            //
            // Starting-arrival entries need the in-game backstory description so the model can
            // connect the pawn's past to the scenario. Use one-line cleanup only, not the
            // sentence-capping LocalizedPromptText helper used for scenario blurbs.
            string description = PromptContextValue(SafeBackstoryDescription(backstory, pawn));
            if (!string.IsNullOrWhiteSpace(description))
            {
                parts.Add(prefix + "_backstory_description=" + description);
            }

            string effects = PromptContextValue(BuildBackstoryEffects(backstory, pawn));
            if (!string.IsNullOrWhiteSpace(effects))
            {
                parts.Add(prefix + "_backstory_effects=" + effects);
            }
        }

        /// <summary>
        /// Resolves ONLY the backstory's flavor prose — the first block RimWorld's
        /// <c>BackstoryDef.FullDescriptionFor</c> builds, before it appends the skill-gain list
        /// ("Mining: +3"), the disabled-work lines, unlocked meditation foci, and the source-mod
        /// credit. Those trailing sections are character-sheet wording, not narrative, and we keep
        /// them out of the prompt for the same reason TraitPersonalityDescription stops before
        /// Trait.TipString's mechanical tail. Whatever remains mechanically relevant is emitted
        /// separately by <see cref="BuildBackstoryEffects"/>.
        ///
        /// Resolving the description ourselves also stops trusting <c>FullDescriptionFor</c>, which
        /// other mods transpile (e.g. Vanilla Expanded Framework) and where a bad interaction could
        /// throw NullReferenceException for specific modded backstories. The catch stays anyway:
        /// it falls back to the raw description template with its unresolved [PAWN_*] grammar tokens
        /// stripped (so the model does not see "[PAWN_nameDef] grew up..."), which keeps the backstory
        /// line and, more importantly, keeps arrival recording from ever aborting.
        /// </summary>
        private static string SafeBackstoryDescription(BackstoryDef backstory, Pawn pawn)
        {
            // BackstoryDef.ResolveReferences copies baseDesc into description when the def only
            // supplies the former, so an empty value here means the backstory genuinely has no prose.
            string template = backstory.description;
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            try
            {
                // The exact chain vanilla uses for the same text: fill [PAWN_*] grammar tokens for
                // this pawn, then strip colour/markup tags.
                return template.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
            }
            catch (Exception e)
            {
                // One warning per backstory def, not one per attempt: while the arrival gate is
                // pending, the same broken def would otherwise report on every diary signal.
                Log.WarningOnce(
                    "[Pawn Diary] Resolving the description of backstory '" + backstory.defName
                    + "' threw (another mod patches it?); using the raw description text instead: " + e,
                    ("PawnDiary.ArrivalBackstoryDescription." + backstory.defName).GetHashCode());
                return StripGrammarTokens(template);
            }
        }

        // Removes unresolved [PAWN_*]/[ANYONE_*] grammar tokens from a raw backstory template and collapses
        // the whitespace they leave behind. Only used on the rare FullDescriptionFor-threw fallback path.
        private static string StripGrammarTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string stripped = Regex.Replace(text, @"\[[^\]]*\]", " ");
            return Regex.Replace(stripped, @"\s{2,}", " ").Trim();
        }

        private static string BuildBackstoryEffects(BackstoryDef backstory, Pawn pawn)
        {
            // Deliberately no skill gains. "Crafting +3" is a number off the character sheet: it tells
            // the model nothing a diary sentence can use, and the backstory prose already says the pawn
            // grew up in mines and workhouses. What stays here is the wording that still shapes a life —
            // work this pawn cannot do, and traits they were born into.
            List<string> parts = new List<string>();

            AddWorkTypes(parts, ArrivalBackstoryLabel("DisabledWork"), backstory?.DisabledWorkTypes);
            AddWorkGivers(parts, ArrivalBackstoryLabel("DisabledTasks"), backstory?.DisabledWorkGivers);
            AddWorkTags(parts, ArrivalBackstoryLabel("DisabledWorkTags"), backstory?.workDisables ?? WorkTags.None);
            AddWorkTags(parts, ArrivalBackstoryLabel("RequiredWorkTags"), backstory?.requiredWorkTags ?? WorkTags.None);
            AddTraits(parts, ArrivalBackstoryLabel("ForcedTraits"), backstory?.forcedTraits, pawn);
            AddTraits(parts, ArrivalBackstoryLabel("DisallowedTraits"), backstory?.disallowedTraits, pawn);

            return string.Join(" | ", parts.ToArray());
        }

        private static string ArrivalBackstoryLabel(string suffix)
        {
            return ("PawnDiary.Event.Arrival.Backstory." + suffix).Translate().Resolve();
        }

        private static void AddWorkTypes(List<string> parts, string label, List<WorkTypeDef> workTypes)
        {
            if (parts == null || workTypes == null || workTypes.Count == 0)
            {
                return;
            }

            List<string> labels = new List<string>();
            for (int i = 0; i < workTypes.Count; i++)
            {
                string workType = DefLabel(workTypes[i]);
                if (!string.IsNullOrWhiteSpace(workType))
                {
                    labels.Add(workType);
                }
            }

            if (labels.Count > 0)
            {
                parts.Add(label + ": " + string.Join(", ", labels.ToArray()));
            }
        }

        private static void AddWorkGivers(List<string> parts, string label, IEnumerable<WorkGiverDef> workGivers)
        {
            if (parts == null || workGivers == null)
            {
                return;
            }

            List<string> labels = new List<string>();
            foreach (WorkGiverDef workGiver in workGivers)
            {
                string task = DefLabel(workGiver);
                if (!string.IsNullOrWhiteSpace(task))
                {
                    labels.Add(task);
                }
            }

            if (labels.Count > 0)
            {
                parts.Add(label + ": " + string.Join(", ", labels.ToArray()));
            }
        }

        private static void AddWorkTags(List<string> parts, string label, WorkTags workTags)
        {
            if (parts == null || workTags == WorkTags.None)
            {
                return;
            }

            parts.Add(label + ": " + workTags);
        }

        private static void AddTraits(List<string> parts, string label, List<BackstoryTrait> traits, Pawn pawn)
        {
            if (parts == null || traits == null || traits.Count == 0)
            {
                return;
            }

            List<string> labels = new List<string>();
            for (int i = 0; i < traits.Count; i++)
            {
                string trait = TraitLabel(traits[i], pawn);
                if (!string.IsNullOrWhiteSpace(trait))
                {
                    labels.Add(trait);
                }
            }

            if (labels.Count > 0)
            {
                parts.Add(label + ": " + string.Join(", ", labels.ToArray()));
            }
        }

        private static string TraitLabel(BackstoryTrait trait, Pawn pawn)
        {
            if (trait?.def == null)
            {
                return string.Empty;
            }

            TraitDegreeData degreeData = trait.def.DataAtDegree(trait.degree);
            if (degreeData != null)
            {
                string degreeLabel = pawn != null ? degreeData.GetLabelFor(pawn) : degreeData.GetLabelFor(Gender.None);
                if (!string.IsNullOrWhiteSpace(degreeLabel))
                {
                    return PromptContextValue(degreeLabel);
                }
            }

            return DefLabel(trait.def);
        }

        private static string DefLabel(Def def)
        {
            return def == null ? string.Empty : PromptContextValue(def.LabelCap.Resolve());
        }

        private static string PromptContextValue(string value)
        {
            return GameContextValue.Sanitize(PromptTextSanitizer.OneLine(value));
        }
    }
}
