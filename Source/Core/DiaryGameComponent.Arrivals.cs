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
                    // Isolate each colonist. This scan gates ALL diary capture on a new game
                    // (EnsureStartingArrivalsBefore retries it before every signal, and ticking waits
                    // on it too), so a pawn whose context build or capture throws must cost only their
                    // own arrival page — letting the exception escape kept the gate closed forever and
                    // silenced the whole diary while erroring on every event.
                    try
                    {
                        DiaryEvents.Submit(new ArrivalSignal(colonists[i], BuildStartingArrivalContext(colonists[i])));
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[Pawn Diary] Skipped the starting-arrival entry for "
                            + (colonists[i]?.LabelShort ?? "an unnamed colonist") + ": " + e);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Load-time check for the arrival bootstrap: true when any free map colonist eligible for a
        /// diary has no arrival page yet. Mirrors TryRecordStartingColonistArrivals' iteration
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
                    if (pawn != null && IsDiaryEligible(pawn) && !HasArrivalEventFor(pawn.GetUniqueLoadID()))
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
                    parts.Add("scenario_name=" + scenarioName);
                }

                string scenarioDescription = PromptTextSanitizer.LocalizedPromptText(scenario.description);
                if (!string.IsNullOrWhiteSpace(scenarioDescription))
                {
                    parts.Add("scenario_description=" + scenarioDescription);
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

            string title = PromptContextValue(backstory.TitleCapFor(pawn.gender));
            if (!string.IsNullOrWhiteSpace(title))
            {
                parts.Add(prefix + "_backstory=" + title);
            }

            // Starting-arrival entries need the full in-game backstory description so the model can
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
        /// Resolves the pawn-adjusted backstory description without trusting
        /// <c>BackstoryDef.FullDescriptionFor</c>: other mods transpile that vanilla method (e.g.
        /// Vanilla Expanded Framework), and a bad interaction can throw NullReferenceException for
        /// specific modded backstories. Falls back to the raw description template with its unresolved
        /// [PAWN_*] grammar tokens stripped (so the model does not see "[PAWN_nameDef] grew up...") — the
        /// arrival entry still gets its backstory line and, more importantly, arrival recording never aborts.
        /// </summary>
        private static string SafeBackstoryDescription(BackstoryDef backstory, Pawn pawn)
        {
            try
            {
                return backstory.FullDescriptionFor(pawn).Resolve();
            }
            catch (Exception e)
            {
                // One warning per backstory def, not one per attempt: while the arrival gate is
                // pending, the same broken def would otherwise report on every diary signal.
                Log.WarningOnce(
                    "[Pawn Diary] BackstoryDef.FullDescriptionFor threw for '" + backstory.defName
                    + "' (another mod patches it?); using the raw description text instead: " + e,
                    ("PawnDiary.ArrivalBackstoryDescription." + backstory.defName).GetHashCode());
                return StripGrammarTokens(backstory.baseDesc);
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
            List<string> parts = new List<string>();

            AddSkillGains(parts, backstory?.skillGains);
            AddWorkTypes(parts, ArrivalBackstoryLabel("DisabledWork"), backstory?.DisabledWorkTypes);
            AddWorkGivers(parts, ArrivalBackstoryLabel("DisabledTasks"), backstory?.DisabledWorkGivers);
            AddWorkTags(parts, ArrivalBackstoryLabel("DisabledWorkTags"), backstory?.workDisables ?? WorkTags.None);
            AddWorkTags(parts, ArrivalBackstoryLabel("RequiredWorkTags"), backstory?.requiredWorkTags ?? WorkTags.None);
            AddTraits(parts, ArrivalBackstoryLabel("ForcedTraits"), backstory?.forcedTraits, pawn);
            AddTraits(parts, ArrivalBackstoryLabel("DisallowedTraits"), backstory?.disallowedTraits, pawn);

            return string.Join(" | ", parts.ToArray());
        }

        private static void AddSkillGains(List<string> parts, List<SkillGain> gains)
        {
            if (parts == null || gains == null || gains.Count == 0)
            {
                return;
            }

            List<string> labels = new List<string>();
            for (int i = 0; i < gains.Count; i++)
            {
                SkillGain gain = gains[i];
                string skill = DefLabel(gain?.skill);
                if (!string.IsNullOrWhiteSpace(skill) && gain.amount != 0)
                {
                    labels.Add(skill + " +" + gain.amount);
                }
            }

            if (labels.Count > 0)
            {
                parts.Add(ArrivalBackstoryLabel("SkillBonuses") + ": " + string.Join(", ", labels.ToArray()));
            }
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
            return PromptTextSanitizer.OneLine(value).Replace(';', ',');
        }
    }
}
